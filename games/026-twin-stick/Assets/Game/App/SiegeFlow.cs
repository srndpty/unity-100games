using System;
using Game.Core;
using TileMotion;

namespace Game.App
{
    public enum AppScreen
    {
        Title,
        Playing,
        Paused,

        /// <summary>試合が終わった（勝ち・負けは <see cref="SiegeRun.Status"/>）。</summary>
        Result,
    }

    /// <summary>キーとマウスの代わりに Tick ごとの操作を決める操縦役（撮影・テスト用）。</summary>
    public interface IPilot
    {
        ShipCommand CommandFor(SiegeRun run);
    }

    /// <summary>
    /// 画面遷移と、プレイヤーの意図を Core へ渡す窓口（025 の VaultFlow と同じ時刻の扱い）。
    /// <para>
    /// 時刻と Tick の対応：プレイを始めた（再開した）時刻を t0 とし、Tick n は区間 [t0 + (n-1)/60, t0 + n/60) を表す。
    /// 時刻 τ の入力は、τ を含む区間の Tick から効く（その前の Tick を先に進めてから入力を反映する）。
    /// </para>
    /// <para>
    /// 照準：マウスの位置は時刻付きの出来事ではなく「今の値」なので、Unity 層が毎フレーム <see cref="SetAim"/> で渡し、
    /// そのフレームで進める Tick はすべてその点を狙う。移動キーとは別の経路で、互いに影響しない。
    /// </para>
    /// <para>
    /// 1 フレームの処理量：<see cref="Apply"/> と <see cref="Advance"/> は 1 フレームで合わせて <see cref="MaxTicksPerFrame"/> Tick までしか
    /// 進めない。上限を超えた区間は飛ばす（処理落ちの間は試合がゆっくり進む）。
    /// </para>
    /// <para>
    /// シード：試合ごとに <c>firstSeed</c> から 1 ずつ進めた値を使う（再戦は必ず新しいシード。010 の知見）。
    /// </para>
    /// </summary>
    public sealed class SiegeFlow
    {
        /// <summary>1 フレームで進める Tick 数の上限。</summary>
        public const int MaxTicksPerFrame = 5;

        private readonly SiegeConfig _config;
        private readonly HeldControls _held = new();
        private bool _hasFocus = true;
        private int _nextSeed;
        private double _origin;
        private long _ticksDone;
        private int _frameBudget = MaxTicksPerFrame;
        private Vec2i _aim;
        private FlowEvents _events;

        /// <param name="config">設定（複製して使う）。</param>
        /// <param name="firstSeed">最初の試合のシード。以後の試合は 1 ずつ進める。</param>
        public SiegeFlow(SiegeConfig config, int firstSeed)
        {
            if (config == null) { throw new ArgumentNullException(nameof(config)); }
            config.Validate();
            _config = config.Copy();
            _nextSeed = firstSeed;
        }

        public AppScreen Screen { get; private set; }

        public SiegeRun? Run { get; private set; }

        /// <summary>設定されていれば、キーとマウスの代わりにこれで動かす（撮影・テスト用）。</summary>
        public IPilot? Pilot { get; set; }

        /// <summary>今の照準点（アリーナ座標）。</summary>
        public Vec2i Aim => _aim;

        public void Apply(Intent intent)
        {
            if (!_hasFocus) { return; }
            AdvanceTo(intent.Time);
            switch (intent.Kind)
            {
                case IntentKind.Submit:
                    if (Screen == AppScreen.Title || Screen == AppScreen.Result) { Begin(intent.Time); }
                    else if (Screen == AppScreen.Paused) { Resume(intent.Time); }
                    break;
                case IntentKind.TogglePause:
                    if (Screen == AppScreen.Playing) { SwitchTo(AppScreen.Paused); }
                    else if (Screen == AppScreen.Paused) { Resume(intent.Time); }
                    break;
                case IntentKind.Restart:
                    if (Screen == AppScreen.Paused || Screen == AppScreen.Result) { Begin(intent.Time); }
                    break;
                case IntentKind.BackToTitle:
                    if (Screen == AppScreen.Paused || Screen == AppScreen.Result)
                    {
                        Run = null;
                        SwitchTo(AppScreen.Title);
                    }

                    break;
                case IntentKind.Quit:
                    break;
                case IntentKind.ControlDown:
                    // 押しっぱなしのキーを一時停止・結果の後へ持ち越さないよう、プレイ中に押したものだけを数える。
                    if (Screen == AppScreen.Playing) { _held.Press(intent.Control); }
                    break;
                case IntentKind.ControlUp:
                    _held.Release(intent.Control);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(intent), intent.Kind, "未定義の操作です。");
            }
        }

        /// <summary>照準点（アリーナ座標）を更新する。以後に進める Tick はこの点を狙う。</summary>
        public void SetAim(Vec2i point) => _aim = point;

        /// <summary>フォーカスを失うとプレイ中は一時停止する。戻っても自動では再開しない。</summary>
        public void SetFocus(bool focus)
        {
            _hasFocus = focus;
            if (focus) { return; }
            if (Screen == AppScreen.Playing) { SwitchTo(AppScreen.Paused); }
            _held.Clear();
        }

        /// <summary>時刻 <paramref name="now"/> までの Tick を進め、フレームを終える。1 フレームに 1 回、入力をすべて反映した後に呼ぶ。</summary>
        public void Advance(double now)
        {
            if (_hasFocus) { AdvanceTo(now); }
            EndFrame();
        }

        /// <summary>時計で進めずにフレームを終える（撮影中など、Tick を直接進めるとき）。</summary>
        public void EndFrame() => _frameBudget = MaxTicksPerFrame;

        /// <summary>時刻と無関係に 1 Tick 進める（撮影・テスト用）。プレイ中でなければ何もしない。</summary>
        public void StepTick()
        {
            if (Screen != AppScreen.Playing) { return; }
            Step();
        }

        /// <summary>前回の呼び出し以降に起きた出来事を取り出す。</summary>
        public FlowEvents TakeEvents()
        {
            var events = _events;
            _events = FlowEvents.None;
            return events;
        }

        private void Step()
        {
            var run = Run!;
            var command = Pilot?.CommandFor(run) ?? new ShipCommand(_held.Horizontal, _held.Vertical, _held.TakeFire(), _aim);
            run.Step(command);
            _events |= (FlowEvents)((int)run.TakeEvents() << 1);
            if (run.IsOver) { SwitchTo(AppScreen.Result); }
        }

        private void AdvanceTo(double time)
        {
            if (Screen != AppScreen.Playing || !double.IsFinite(time)) { return; }
            var elapsed = Math.Floor((time - _origin) * Units.TickRate);
            if (elapsed <= _ticksDone) { return; }

            // このフレームの残りの上限を超える区間は、手前を飛ばして直前の Tick だけを進める。
            var target = elapsed > long.MaxValue / 2 ? long.MaxValue / 2 : (long)elapsed;
            if (target - _ticksDone > _frameBudget) { _ticksDone = target - _frameBudget; }
            while (_ticksDone < target && Screen == AppScreen.Playing)
            {
                _ticksDone++;
                _frameBudget--;
                Step();
            }
        }

        private void Begin(double time)
        {
            Run = new SiegeRun(_config, _nextSeed);
            _nextSeed = unchecked(_nextSeed + 1);
            _aim = Run.Ship.Position + new Vec2i(Units.Tile, 0);
            _events |= FlowEvents.Started;
            Resume(time);
        }

        private void Resume(double time)
        {
            SwitchTo(AppScreen.Playing);
            _origin = time;
            _ticksDone = 0;
        }

        private void SwitchTo(AppScreen screen)
        {
            Screen = screen;
            _held.Clear();
        }
    }

    /// <summary>画面に反映する出来事。試合の出来事（<see cref="RunEvents"/>）に、試合の開始を足したもの。</summary>
    [Flags]
    public enum FlowEvents
    {
        None = 0,
        Started = 1,
        Fired = (int)RunEvents.Fired << 1,
        EnemyWarned = (int)RunEvents.EnemyWarned << 1,
        EnemyActivated = (int)RunEvents.EnemyActivated << 1,
        EnemyHit = (int)RunEvents.EnemyHit << 1,
        EnemyDestroyed = (int)RunEvents.EnemyDestroyed << 1,
        EnemyCulled = (int)RunEvents.EnemyCulled << 1,
        Hurt = (int)RunEvents.Hurt << 1,
        Won = (int)RunEvents.Won << 1,
        Lost = (int)RunEvents.Lost << 1,
    }
}
