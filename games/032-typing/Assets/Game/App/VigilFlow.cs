using System;
using Game.Core;

namespace Game.App
{
    public enum AppScreen
    {
        /// <summary>難度を選んで開始する。操作の説明を出す。</summary>
        Title,

        Playing,

        Paused,

        /// <summary>クリア・失敗の結果。</summary>
        Result,
    }

    /// <summary>
    /// 画面遷移と、プレイヤーの意図を Core へ渡す窓口。
    /// <para>
    /// 時刻と Tick の対応（030 の WardFlow と同じ）：プレイを始めた（再開した）時刻を t0 とし、Tick n は区間 [t0 + (n-1)/60, t0 + n/60) を表す。
    /// 時刻 τ の打鍵は、τ までの Tick を先に進めてから反映する。打鍵と画面の操作は届いた順に 1 本の列で処理する。
    /// </para>
    /// <para>
    /// 1 フレームの処理量：<see cref="Apply"/> と <see cref="Advance"/> は 1 フレームで合わせて <see cref="MaxTicksPerFrame"/> Tick までしか
    /// 進めない。超えた区間は飛ばすが、打鍵は全て順に反映する。遊んだ時間（<see cref="PlayedTicks"/>）は飛ばした区間も数える。
    /// </para>
    /// <para>シード：試合ごとに <c>firstSeed</c> から 1 ずつ進めた値を使う（再戦は必ず新しいシード）。</para>
    /// </summary>
    public sealed class VigilFlow
    {
        /// <summary>1 フレームで進める Tick 数の上限。</summary>
        public const int MaxTicksPerFrame = 5;

        public const int TickRate = 60;

        private readonly VigilConfig _config;
        private readonly WordBook _book;
        private readonly long[] _best = new long[3];
        private bool _hasFocus = true;
        private int _nextSeed;
        private double _origin;
        private long _ticksDone;
        private long _playedBase;
        private int _frameBudget = MaxTicksPerFrame;
        private FlowEvents _events;

        /// <param name="config">設定（検証して複製する）。</param>
        /// <param name="book">単語帳。全ての難度で出題できることを検証する。</param>
        /// <param name="firstSeed">最初の試合のシード。以後の試合は 1 ずつ進める。</param>
        public VigilFlow(VigilConfig config, WordBook book, int firstSeed)
        {
            if (config == null) { throw new ArgumentNullException(nameof(config)); }
            _book = book ?? throw new ArgumentNullException(nameof(book));
            config.Validate(book);
            _config = config.Copy();
            _nextSeed = firstSeed;
        }

        public AppScreen Screen { get; private set; }

        /// <summary>タイトルで選んでいる難度（結果からの再挑戦もこの難度）。</summary>
        public Difficulty Selected { get; private set; } = Difficulty.Normal;

        public TypingRun? Run { get; private set; }

        /// <summary>単語帳（描画・撮影が語を探すのに使う）。</summary>
        public WordBook Book => _book;

        /// <summary>設定されていれば、キー入力の代わりにこれで打つ（撮影・テスト用）。</summary>
        public IPilot? Pilot { get; set; }

        /// <summary>今の試合で遊んだ時間（Tick 単位）。一時停止・結果・フォーカス喪失・タイトルは含まない。</summary>
        public long PlayedTicks => _playedBase + _ticksDone;

        /// <summary>直前の結果で、その難度の最高得点を更新したか。</summary>
        public bool IsNewBest { get; private set; }

        /// <summary>起動してからの、難度ごとの最高得点。</summary>
        public long BestScore(Difficulty difficulty) => _best[Index(difficulty)];

        /// <summary>受け付けた打鍵の速さ（1 分あたり）。遊んだ時間から数える。</summary>
        public int KeysPerMinute => Run == null ? 0 : TypingRun.KeysPerMinute(Run.CorrectKeys, PlayedTicks);

        public void Apply(Intent intent)
        {
            if (!_hasFocus) { return; }
            AdvanceTo(intent.Time);
            switch (Screen)
            {
                case AppScreen.Title:
                    if (intent.Kind == IntentKind.Up) { Select(-1); }
                    else if (intent.Kind == IntentKind.Down) { Select(1); }
                    else if (intent.Kind == IntentKind.Submit) { Begin(intent.Time); }
                    break;
                case AppScreen.Playing:
                    if (intent.Kind == IntentKind.Key && Pilot == null) { Run!.Type(intent.Key); }
                    else if (intent.Kind == IntentKind.Backspace && Pilot == null) { Run!.Backspace(); }
                    else if (intent.Kind == IntentKind.Cancel) { Screen = AppScreen.Paused; }
                    CheckEnd();
                    break;
                case AppScreen.Paused:
                    if (intent.Kind == IntentKind.Cancel || intent.Kind == IntentKind.Submit) { Resume(intent.Time); }
                    else if (intent.Kind == IntentKind.Key && intent.Key == 't') { ToTitle(); }
                    break;
                case AppScreen.Result:
                    if (intent.Kind == IntentKind.Submit) { Begin(intent.Time); }
                    else if (intent.Kind == IntentKind.Cancel) { ToTitle(); }
                    break;
                default:
                    throw new InvalidOperationException($"未定義の画面です：{Screen}");
            }
        }

        /// <summary>フォーカスを失うとプレイ中は一時停止する。戻っても自動では再開しない。</summary>
        public void SetFocus(bool focus)
        {
            _hasFocus = focus;
            if (!focus && Screen == AppScreen.Playing) { Screen = AppScreen.Paused; }
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
            _playedBase++;
            Step();
        }

        /// <summary>前回の呼び出し以降に起きた出来事を取り出す。</summary>
        public FlowEvents TakeEvents()
        {
            var events = _events;
            _events = FlowEvents.None;
            return events;
        }

        private static int Index(Difficulty difficulty) => difficulty switch
        {
            Difficulty.Easy => 0,
            Difficulty.Normal => 1,
            Difficulty.Hard => 2,
            _ => throw new ArgumentOutOfRangeException(nameof(difficulty), difficulty, "未定義の難度です。"),
        };

        private void Select(int delta)
        {
            var next = Math.Clamp(Index(Selected) + delta, 0, 2);
            if (next == Index(Selected)) { return; }
            Selected = (Difficulty)next;
            _events |= FlowEvents.Selected;
        }

        private void Step()
        {
            var run = Run!;
            if (Pilot != null)
            {
                var key = Pilot.KeyFor(run);
                if (key == Typist.BackspaceKey) { run.Backspace(); }
                else if (key != null) { run.Type(key.Value); }
            }

            if (run.Status == RunStatus.Playing) { run.Tick(); }
            CheckEnd();
        }

        private void CheckEnd()
        {
            var run = Run!;
            _events |= (FlowEvents)((int)run.TakeEvents() << 2);
            if (Screen != AppScreen.Playing || run.Status == RunStatus.Playing) { return; }
            Screen = AppScreen.Result;
            var index = Index(run.Difficulty);
            IsNewBest = run.Score > _best[index];
            if (IsNewBest) { _best[index] = run.Score; }
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

        private void Begin(double time)
        {
            var seed = _nextSeed;
            _nextSeed = unchecked(_nextSeed + 1);
            Run = new TypingRun(_config, _book, Selected, seed);
            Pilot?.Reset(seed);
            IsNewBest = false;
            _playedBase = 0;
            _ticksDone = 0;
            _events |= FlowEvents.Started;
            Resume(time);
        }

        private void Resume(double time)
        {
            Screen = AppScreen.Playing;
            _playedBase += _ticksDone;
            _origin = time;
            _ticksDone = 0;
        }

        private void ToTitle()
        {
            Run = null;
            Screen = AppScreen.Title;
            _events |= FlowEvents.Title;
        }
    }

    /// <summary>画面に反映する出来事。試合の出来事（<see cref="RunEvents"/>）に、開始・難度の選択・タイトルへの戻りを足したもの。</summary>
    [Flags]
    public enum FlowEvents
    {
        None = 0,
        Started = 1,
        Selected = 1 << 1,
        Correct = (int)RunEvents.Correct << 2,
        Miss = (int)RunEvents.Miss << 2,
        Locked = (int)RunEvents.Locked << 2,
        Released = (int)RunEvents.Released << 2,
        Defeated = (int)RunEvents.Defeated << 2,
        Hit = (int)RunEvents.Hit << 2,
        Spawned = (int)RunEvents.Spawned << 2,
        Cleared = (int)RunEvents.Cleared << 2,
        Failed = (int)RunEvents.Failed << 2,
        Title = (int)RunEvents.Failed << 3,
    }
}
