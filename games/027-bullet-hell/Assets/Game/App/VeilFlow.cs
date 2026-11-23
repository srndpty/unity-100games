using System;
using System.Collections.Generic;
using Game.Core;

namespace Game.App
{
    public enum AppScreen
    {
        Title,
        Playing,
        Paused,

        /// <summary>試合が終わった（勝ち・負けは <see cref="VeilRun.Status"/>）。</summary>
        Result,
    }

    /// <summary>キーの代わりに Tick ごとの操作を決める操縦役（撮影・テスト用）。</summary>
    public interface IPilot
    {
        PlayerCommand CommandFor(VeilRun run);
    }

    /// <summary>
    /// 画面遷移と、プレイヤーの意図を Core へ渡す窓口（026 の SiegeFlow と同じ時刻の扱い）。
    /// <para>
    /// 時刻と Tick の対応：プレイを始めた（再開した）時刻を t0 とし、Tick n は区間 [t0 + (n-1)/60, t0 + n/60) を表す。
    /// 時刻 τ の入力は、τ を含む区間の Tick から効く（その前の Tick を先に進めてから入力を反映する）。
    /// </para>
    /// <para>
    /// 1 フレームの処理量：<see cref="Apply"/> と <see cref="Advance"/> は 1 フレームで合わせて <see cref="MaxTicksPerFrame"/> Tick までしか
    /// 進めない。上限を超えた区間は飛ばす（処理落ちの間は試合がゆっくり進む）。
    /// </para>
    /// <para>
    /// シード：試合ごとに <c>firstSeed</c> から 1 ずつ進めた値を使う。「このボスから再挑戦」も新しいシード（1 試合 = 1 シード）。
    /// </para>
    /// </summary>
    public sealed class VeilFlow
    {
        /// <summary>1 フレームで進める Tick 数の上限。</summary>
        public const int MaxTicksPerFrame = 5;

        private readonly VeilConfig _config;
        private readonly BossScript[] _bosses;
        private readonly HeldControls _held = new();
        private bool _hasFocus = true;
        private int _nextSeed;
        private double _origin;
        private long _ticksDone;
        private int _frameBudget = MaxTicksPerFrame;
        private FlowEvents _events;

        /// <param name="config">設定（複製して使う）。</param>
        /// <param name="bosses">ボス 2 体の台本。</param>
        /// <param name="firstSeed">最初の試合のシード。以後の試合は 1 ずつ進める。</param>
        public VeilFlow(VeilConfig config, IReadOnlyList<BossScript> bosses, int firstSeed)
        {
            if (config == null) { throw new ArgumentNullException(nameof(config)); }
            config.Validate();
            VeilConfig.ValidateBosses(bosses);
            _config = config.Copy();
            _bosses = new BossScript[bosses.Count];
            for (var i = 0; i < _bosses.Length; i++) { _bosses[i] = bosses[i]; }
            _nextSeed = firstSeed;
        }

        public AppScreen Screen { get; private set; }

        public VeilRun? Run { get; private set; }

        /// <summary>設定されていれば、キーの代わりにこれで動かす（撮影・テスト用）。</summary>
        public IPilot? Pilot { get; set; }

        /// <summary>直前の Tick に Core へ渡した操作。</summary>
        public PlayerCommand LastCommand { get; private set; }

        /// <summary>結果の画面で「このボスから再挑戦」できるか（負けたときだけ）。</summary>
        public bool CanRetryBoss => Screen == AppScreen.Result && Run != null && Run.Status == RunStatus.Lost;

        public void Apply(Intent intent)
        {
            if (!_hasFocus) { return; }
            AdvanceTo(intent.Time);
            switch (intent.Kind)
            {
                case IntentKind.Submit:
                    if (Screen == AppScreen.Title || Screen == AppScreen.Result) { Begin(intent.Time, 0, 0, retried: false); }
                    else if (Screen == AppScreen.Paused) { Resume(intent.Time); }
                    break;
                case IntentKind.TogglePause:
                    if (Screen == AppScreen.Playing) { SwitchTo(AppScreen.Paused); }
                    else if (Screen == AppScreen.Paused) { Resume(intent.Time); }
                    break;
                case IntentKind.Restart:
                    if (Screen == AppScreen.Paused || Screen == AppScreen.Result) { Begin(intent.Time, 0, 0, retried: false); }
                    break;
                case IntentKind.RetryBoss:
                    if (CanRetryBoss) { Begin(intent.Time, Run!.BossIndex, Run.BossStartScore, retried: true); }
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

        /// <summary>フォーカスを失うとプレイ中は一時停止し、押していたキーと保留の入力を捨てる。戻っても自動では再開しない。</summary>
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
            var command = Pilot?.CommandFor(run) ?? new PlayerCommand(_held.Horizontal, _held.Vertical, _held.Slow, _held.TakeShot(), _held.TakeBomb());
            LastCommand = command;
            run.Step(command);
            _events |= (FlowEvents)((int)run.TakeEvents() << 1);
            if (run.IsOver) { SwitchTo(AppScreen.Result); }
        }

        private void AdvanceTo(double time)
        {
            if (Screen != AppScreen.Playing || !double.IsFinite(time)) { return; }
            var elapsed = Math.Floor((time - _origin) * Field.TicksPerSecond);
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

        private void Begin(double time, int startBoss, long startScore, bool retried)
        {
            Run = new VeilRun(_config, _bosses, _nextSeed, startBoss, startScore);
            _nextSeed = unchecked(_nextSeed + 1);
            _events |= retried ? FlowEvents.Started | FlowEvents.Retried : FlowEvents.Started;
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

    /// <summary>画面に反映する出来事。試合の出来事（<see cref="RunEvents"/>）を 1 ビットずらし、開始と再挑戦を足したもの。</summary>
    [Flags]
    public enum FlowEvents
    {
        None = 0,
        Started = 1,
        Shot = (int)RunEvents.Shot << 1,
        BossHit = (int)RunEvents.BossHit << 1,
        Bomb = (int)RunEvents.Bomb << 1,
        Hurt = (int)RunEvents.Hurt << 1,
        PatternStarted = (int)RunEvents.PatternStarted << 1,
        PatternDefeated = (int)RunEvents.PatternDefeated << 1,
        PatternTimedOut = (int)RunEvents.PatternTimedOut << 1,
        PatternBonus = (int)RunEvents.PatternBonus << 1,
        BossDefeated = (int)RunEvents.BossDefeated << 1,
        Won = (int)RunEvents.Won << 1,
        Lost = (int)RunEvents.Lost << 1,

        /// <summary>「このボスから再挑戦」で始めた。</summary>
        Retried = (int)RunEvents.Lost << 2,
    }
}
