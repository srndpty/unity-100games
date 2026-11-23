using System;
using Game.Core;
using TileMotion;

namespace Game.App
{
    public enum AppScreen
    {
        Title,
        Playing,

        /// <summary>レベルアップの 3 択（試合の Tick は止まっている）。</summary>
        LevelUp,

        Paused,

        /// <summary>試合が終わった（勝ち・負けは <see cref="TideRun.Status"/>）。</summary>
        Result,
    }

    /// <summary>キーの代わりに Tick ごとの操作と 3 択の選択を決める操縦役（撮影・テスト用）。</summary>
    public interface IPilot
    {
        PlayerCommand CommandFor(TideRun run);

        /// <summary>表示中の 3 択から選ぶ番号。</summary>
        int ChoiceFor(TideRun run);
    }

    /// <summary>
    /// 画面遷移と、プレイヤーの意図を Core へ渡す窓口（026 の SiegeFlow と同じ時刻の扱い）。
    /// <para>
    /// 時刻と Tick の対応：プレイを始めた（再開した）時刻を t0 とし、Tick n は区間 [t0 + (n-1)/60, t0 + n/60) を表す。
    /// 時刻 τ の入力は、τ を含む区間の Tick から効く（その前の Tick を先に進めてから入力を反映する）。
    /// </para>
    /// <para>
    /// レベルアップ：Tick を進めて 3 択が出たら <see cref="AppScreen.LevelUp"/> へ移り、選ぶまで Tick を進めない。選んだ時刻から時計を数え直す。
    /// 3 択が出てから <see cref="ChoiceGuardSeconds"/> の間の決定・選択は捨てる（移動キーの押しっぱなしや連打が、出たばかりの候補を選ばない）。
    /// 3 択の間も移動キーの押下・解放は覚えておき（選んだ後もそのまま歩ける）、矢印キーは候補の選び直しにも使う。
    /// </para>
    /// <para>
    /// 1 フレームの処理量：<see cref="Apply"/> と <see cref="Advance"/> は 1 フレームで合わせて <see cref="MaxTicksPerFrame"/> Tick までしか
    /// 進めない。上限を超えた区間は飛ばす（処理落ちの間は試合がゆっくり進む）。
    /// </para>
    /// <para>
    /// シード：試合ごとに <c>firstSeed</c> から 1 ずつ進めた値を使う（再戦は必ず新しいシード。010 の知見）。
    /// </para>
    /// </summary>
    public sealed class TideFlow
    {
        /// <summary>1 フレームで進める Tick 数の上限。</summary>
        public const int MaxTicksPerFrame = 5;

        /// <summary>3 択が出てから決定を受け付けるまでの秒数。</summary>
        public const double ChoiceGuardSeconds = 0.3;

        private readonly TideConfig _config;
        private readonly HeldControls _held = new();
        private bool _hasFocus = true;
        private int _nextSeed;
        private double _origin;
        private long _ticksDone;
        private int _frameBudget = MaxTicksPerFrame;
        private double _choiceShownAt;
        private FlowEvents _events;

        /// <param name="config">設定（複製して使う）。</param>
        /// <param name="firstSeed">最初の試合のシード。以後の試合は 1 ずつ進める。</param>
        public TideFlow(TideConfig config, int firstSeed)
        {
            if (config == null) { throw new ArgumentNullException(nameof(config)); }
            config.Validate();
            _config = config.Copy();
            _nextSeed = firstSeed;
        }

        public AppScreen Screen { get; private set; }

        public TideRun? Run { get; private set; }

        /// <summary>設定されていれば、キーの代わりにこれで動かし、3 択もこれで選ぶ（撮影・テスト用）。</summary>
        public IPilot? Pilot { get; set; }

        /// <summary>3 択で選んでいる候補の番号（矢印キーで動かす）。</summary>
        public int Highlight { get; private set; }

        /// <summary>今の 3 択が出た時刻（秒）。</summary>
        public double ChoiceShownAt => _choiceShownAt;

        public void Apply(Intent intent)
        {
            if (!_hasFocus) { return; }
            AdvanceTo(intent.Time);
            switch (intent.Kind)
            {
                case IntentKind.Submit:
                    if (Screen == AppScreen.Title || Screen == AppScreen.Result) { Begin(intent.Time); }
                    else if (Screen == AppScreen.Paused) { Resume(intent.Time); }
                    else if (Screen == AppScreen.LevelUp) { Pick(Highlight, intent.Time); }
                    break;
                case IntentKind.TogglePause:
                    if (Screen == AppScreen.Playing || Screen == AppScreen.LevelUp) { SwitchTo(AppScreen.Paused); }
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
                case IntentKind.Choose:
                    if (Screen == AppScreen.LevelUp) { Pick(intent.Choice, intent.Time); }
                    break;
                case IntentKind.ControlDown:
                    // 押しっぱなしのキーを一時停止・結果の後へ持ち越さないよう、プレイ中と 3 択の間に押したものだけを数える。
                    if (Screen == AppScreen.Playing || Screen == AppScreen.LevelUp) { _held.Press(intent.Control); }
                    if (Screen == AppScreen.LevelUp) { MoveHighlight(intent.Control); }
                    break;
                case IntentKind.ControlUp:
                    _held.Release(intent.Control);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(intent), intent.Kind, "未定義の操作です。");
            }
        }

        /// <summary>フォーカスを失うとプレイ中は一時停止する（3 択の間はそのまま）。戻っても自動では再開しない。</summary>
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
            var command = Pilot?.CommandFor(run) ?? new PlayerCommand(_held.Horizontal, _held.Vertical);
            run.Step(command);
            _events |= (FlowEvents)((int)run.TakeEvents() << 1);
            if (run.IsOver)
            {
                SwitchTo(AppScreen.Result);
                return;
            }

            if (!run.IsChoosing) { return; }
            if (Pilot != null)
            {
                while (run.IsChoosing) { run.Choose(Pilot.ChoiceFor(run)); }
                _events |= FlowEvents.Chose;
                return;
            }

            _choiceShownAt = _origin + ((double)_ticksDone / Units.TickRate);
            Highlight = 0;
            Screen = AppScreen.LevelUp;
        }

        private void Pick(int choice, double time)
        {
            var run = Run!;
            if (time < _choiceShownAt + ChoiceGuardSeconds || choice >= run.ChoiceCount) { return; }
            run.Choose(choice);
            _events |= FlowEvents.Chose;
            if (run.IsChoosing)
            {
                // 続けてレベルが上がっていれば次の 3 択。選んだ時刻から受け付けの猶予を数え直す。
                _choiceShownAt = time;
                Highlight = 0;
                return;
            }

            Screen = AppScreen.Playing;
            _origin = time;
            _ticksDone = 0;
        }

        private void MoveHighlight(Control control)
        {
            var count = Run!.ChoiceCount;
            var step = control == Control.Left || control == Control.Up ? -1 : 1;
            Highlight = (Highlight + step + count) % count;
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
            Run = new TideRun(_config, _nextSeed);
            _nextSeed = unchecked(_nextSeed + 1);
            _events |= FlowEvents.Started;
            Resume(time);
        }

        /// <summary>時計を <paramref name="time"/> から数え直して戻る（3 択の途中で止めたなら 3 択へ）。</summary>
        private void Resume(double time)
        {
            if (Run!.IsChoosing)
            {
                SwitchTo(AppScreen.LevelUp);
                _choiceShownAt = time;
                return;
            }

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

    /// <summary>画面に反映する出来事。試合の出来事（<see cref="RunEvents"/>）に、試合の開始と 3 択の決定を足したもの。</summary>
    [Flags]
    public enum FlowEvents
    {
        None = 0,
        Started = 1,
        BoltFired = (int)RunEvents.BoltFired << 1,
        Pulsed = (int)RunEvents.Pulsed << 1,
        Lashed = (int)RunEvents.Lashed << 1,
        Arced = (int)RunEvents.Arced << 1,
        EnemyHit = (int)RunEvents.EnemyHit << 1,
        EnemyKilled = (int)RunEvents.EnemyKilled << 1,
        GemCollected = (int)RunEvents.GemCollected << 1,
        LevelUp = (int)RunEvents.LevelUp << 1,
        Hurt = (int)RunEvents.Hurt << 1,
        BossSpawned = (int)RunEvents.BossSpawned << 1,
        Won = (int)RunEvents.Won << 1,
        Lost = (int)RunEvents.Lost << 1,
        Chose = (int)RunEvents.Lost << 2,
    }
}
