using System;
using Game.Core;

namespace Game.App
{
    public enum AppScreen
    {
        /// <summary>タイトルと操作の説明。</summary>
        Title,

        Playing,

        Paused,

        /// <summary>3 球を失った後の結果（得点・起動中の最高記録）。</summary>
        Result,
    }

    /// <summary>キーの代わりに、Tick ごとの入力を決める操縦役（撮影・テスト用）。</summary>
    public interface IPinballPilot
    {
        /// <summary>試合を始める前に呼ぶ。前の判断の状態を捨てる（023）。</summary>
        void Reset(int seed);

        PinballInput InputFor(PinballRun run);
    }

    /// <summary>
    /// 画面遷移と、プレイヤーの意図を Core へ渡す窓口。
    /// <para>
    /// 時刻と Tick の対応（022・033 と同じ）：プレイを始めた（再開した）時刻を t0 とし、Tick n は区間 [t0 + (n-1)/60, t0 + n/60) を表す。
    /// 時刻 τ の入力は、τ までの Tick を先に進めてから反映する。押したキーはその後の最初の Tick の始めから効く。
    /// </para>
    /// <para>
    /// 1 フレームの処理量：<see cref="Apply"/> と <see cref="Advance"/> は 1 フレームで合わせて <see cref="MaxTicksPerFrame"/> Tick までしか
    /// 進めない。超えた区間は飛ばすが、入力は全て順に反映する。
    /// </para>
    /// </summary>
    public sealed class PinballFlow
    {
        public const int MaxTicksPerFrame = 5;

        public const int TickRate = 60;

        /// <summary>試合ごとにシードを進める量（黄金比、010）。</summary>
        private const int SeedStride = unchecked((int)0x9E3779B9);

        private readonly PinballConfig _config;
        private readonly Table _table;
        private readonly int _baseSeed;
        private int _games;
        private bool _hasFocus = true;
        private bool _left;
        private bool _right;
        private bool _plunger;
        private double _origin;
        private long _ticksDone;
        private int _frameBudget = MaxTicksPerFrame;
        private FlowEvents _events;
        private TickEvents _tickEvents;

        public PinballFlow(PinballConfig config, Table table, int seed)
        {
            if (config == null) { throw new ArgumentNullException(nameof(config)); }
            _table = table ?? throw new ArgumentNullException(nameof(table));
            table.Validate(config);
            _config = config.Copy();
            _baseSeed = seed;
        }

        public AppScreen Screen { get; private set; }

        /// <summary>設定の複製。</summary>
        public PinballConfig Config => _config.Copy();

        public Table Table => _table;

        /// <summary>今（または直前）の試合。タイトルでは null。</summary>
        public PinballRun? Run { get; private set; }

        /// <summary>起動中の最高記録。</summary>
        public long BestScore { get; private set; }

        /// <summary>直前の試合で最高記録を更新したか。</summary>
        public bool IsNewBest { get; private set; }

        /// <summary>今の Tick に使う入力（押しているキー）。</summary>
        public PinballInput Held => new(_left, _right, _plunger);

        /// <summary>設定されていれば、キーの代わりにこれで操作する（撮影・テスト用）。</summary>
        public IPinballPilot? Pilot { get; set; }

        public void Apply(Intent intent)
        {
            if (!_hasFocus) { return; }
            AdvanceTo(intent.Time);
            switch (Screen)
            {
                case AppScreen.Title:
                    if (intent.Kind == IntentKind.Submit || intent.Kind == IntentKind.PlungerDown) { Begin(intent.Time); }
                    break;
                case AppScreen.Playing:
                    ApplyPlaying(intent);
                    break;
                case AppScreen.Paused:
                    if (intent.Kind == IntentKind.Cancel || intent.Kind == IntentKind.Submit) { Resume(intent.Time); }
                    else if (intent.Kind == IntentKind.Retry) { Begin(intent.Time); }
                    else if (intent.Kind == IntentKind.Back) { ToTitle(); }
                    break;
                case AppScreen.Result:
                    if (intent.Kind == IntentKind.Submit || intent.Kind == IntentKind.PlungerDown) { Begin(intent.Time); }
                    else if (intent.Kind == IntentKind.Cancel) { ToTitle(); }
                    break;
                default:
                    throw new InvalidOperationException($"未定義の画面です：{Screen}");
            }
        }

        /// <summary>フォーカスを失うと押しているキーを全て離し、プレイ中は一時停止する（戻っても自動では再開しない）。</summary>
        public void SetFocus(bool focus)
        {
            _hasFocus = focus;
            if (focus) { return; }
            ReleaseAll();
            if (Screen == AppScreen.Playing) { Pause(); }
        }

        /// <summary>時刻 <paramref name="now"/> までの Tick を進め、フレームを終える。1 フレームに 1 回、入力を全て反映した後に呼ぶ。</summary>
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

        /// <summary>前回の呼び出し以降に起きた画面の出来事を取り出す。</summary>
        public FlowEvents TakeEvents()
        {
            var events = _events;
            _events = FlowEvents.None;
            return events;
        }

        /// <summary>前回の呼び出し以降に進めた Tick の出来事の合計を取り出す（音・演出用）。</summary>
        public TickEvents TakeTickEvents()
        {
            var events = _tickEvents;
            _tickEvents = default;
            return events;
        }

        private void ApplyPlaying(Intent intent)
        {
            switch (intent.Kind)
            {
                case IntentKind.LeftDown:
                    _left = true;
                    break;
                case IntentKind.LeftUp:
                    _left = false;
                    break;
                case IntentKind.RightDown:
                    _right = true;
                    break;
                case IntentKind.RightUp:
                    _right = false;
                    break;
                case IntentKind.PlungerDown:
                    _plunger = true;
                    break;
                case IntentKind.PlungerUp:
                    _plunger = false;
                    break;
                case IntentKind.Cancel:
                    Pause();
                    break;
            }
        }

        private void Step()
        {
            var run = Run!;
            var input = Pilot != null ? Pilot.InputFor(run) : Held;
            Accumulate(run.Step(input));
            if (run.Status != RunStatus.Over) { return; }
            IsNewBest = run.Score > BestScore;
            if (IsNewBest) { BestScore = run.Score; }
            ReleaseAll();
            Screen = AppScreen.Result;
            _events |= FlowEvents.Result;
        }

        private void Accumulate(TickEvents events)
        {
            _tickEvents.BumperHits += events.BumperHits;
            _tickEvents.BumpersLit |= events.BumpersLit;
            _tickEvents.TargetHits += events.TargetHits;
            _tickEvents.TargetSets += events.TargetSets;
            _tickEvents.FlipperHits += events.FlipperHits;
            _tickEvents.WallHits += events.WallHits;
            _tickEvents.Launches += events.Launches;
            _tickEvents.BallsLost += events.BallsLost;
            _tickEvents.MultiballStarted |= events.MultiballStarted;
            _tickEvents.BallEnded |= events.BallEnded;
            _tickEvents.GameOver |= events.GameOver;
            _tickEvents.ScoreGained += events.ScoreGained;
        }

        private void AdvanceTo(double time)
        {
            if (Screen != AppScreen.Playing || !double.IsFinite(time)) { return; }
            var elapsed = Math.Floor((time - _origin) * TickRate);
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

        /// <summary>新しいシードで試合を始める（再戦も新しいシード、試合をまたいで乱数を持ち越さない）。</summary>
        private void Begin(double time)
        {
            var seed = unchecked(_baseSeed + (_games * SeedStride));
            _games++;
            Run = new PinballRun(_config, _table, seed);
            Pilot?.Reset(seed);
            IsNewBest = false;
            _tickEvents = default;
            ReleaseAll();
            _events |= FlowEvents.Started;
            Resume(time);
        }

        private void Pause()
        {
            ReleaseAll();
            Screen = AppScreen.Paused;
            _events |= FlowEvents.Paused;
        }

        private void Resume(double time)
        {
            Screen = AppScreen.Playing;
            _origin = time;
            _ticksDone = 0;
        }

        private void ToTitle()
        {
            Run = null;
            ReleaseAll();
            Screen = AppScreen.Title;
            _events |= FlowEvents.Title;
        }

        private void ReleaseAll()
        {
            _left = false;
            _right = false;
            _plunger = false;
        }
    }

    /// <summary>画面に反映する出来事。</summary>
    [Flags]
    public enum FlowEvents
    {
        None = 0,
        Started = 1,
        Paused = 1 << 1,
        Title = 1 << 2,
        Result = 1 << 3,
    }
}
