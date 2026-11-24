using System;
using System.Collections.Generic;
using Game.Core;
using IntGeometry;

namespace Game.App
{
    public enum AppScreen
    {
        /// <summary>タイトルと操作の説明。</summary>
        Title,

        /// <summary>問題選択（解放済みの問と最少手数）。</summary>
        Select,

        Playing,

        Paused,

        /// <summary>1 問の成功・失敗（<see cref="SnipFlow.Run"/> の結果）。</summary>
        Outcome,

        /// <summary>全ての問を解いた後の結果。</summary>
        Result,
    }

    /// <summary>キーやマウスの代わりに、Tick ごとになぞりを決める操縦役（撮影・テスト用）。</summary>
    public interface ISnipPilot
    {
        /// <summary>問を始める・やり直す前に呼ぶ。前の判断の状態を捨てる。</summary>
        void Reset(int puzzle);

        /// <summary>この Tick の前になぞる線分（なければ null）。null を返すまで同じ Tick に繰り返し呼ぶ。</summary>
        (Vec2i From, Vec2i To)? SwipeFor(PuzzleRun run);
    }

    /// <summary>
    /// 画面遷移と、プレイヤーの意図を Core へ渡す窓口。
    /// <para>
    /// 時刻と Tick の対応（032 と同じ）：プレイを始めた（再開した）時刻を t0 とし、Tick n は区間 [t0 + (n-1)/60, t0 + n/60) を表す。
    /// 時刻 τ の入力は、τ までの Tick を先に進めてから反映する。なぞりの線分はその Tick の境目の形で切断を判定する（操作は物理の途中に割り込まない）。
    /// </para>
    /// <para>
    /// 1 フレームの処理量：<see cref="Apply"/> と <see cref="Advance"/> は 1 フレームで合わせて <see cref="MaxTicksPerFrame"/> Tick までしか
    /// 進めない。超えた区間は飛ばすが、入力は全て順に反映する。
    /// </para>
    /// </summary>
    public sealed class SnipFlow
    {
        /// <summary>1 フレームで進める Tick 数の上限。</summary>
        public const int MaxTicksPerFrame = 5;

        public const int TickRate = 60;

        /// <summary>問題選択の 1 行の札の数（↑↓ の移動量）。</summary>
        public const int SelectColumns = 5;

        /// <summary><see cref="FlowEvents"/> の中で試合の出来事を置く位置。</summary>
        public const int RunShift = 8;

        private readonly RopeConfig _config;
        private readonly PuzzleBook _book;
        private bool _hasFocus = true;
        private bool _dropNextClick;
        private bool _dropUntilUp;
        private bool _clickOnSelect;
        private Vec2i? _swipe;
        private double _origin;
        private long _ticksDone;
        private int _frameBudget = MaxTicksPerFrame;
        private FlowEvents _events;

        public SnipFlow(RopeConfig config, PuzzleBook book)
        {
            if (config == null) { throw new ArgumentNullException(nameof(config)); }
            _book = book ?? throw new ArgumentNullException(nameof(book));
            book.Validate(config);
            if (book.Puzzles.Count == 0) { throw new ArgumentException("問がありません。", nameof(book)); }
            _config = config.Copy();
            Progress = new Progress(book.Puzzles.Count);
        }

        public AppScreen Screen { get; private set; }

        public PuzzleBook Book => _book;

        public Progress Progress { get; }

        /// <summary>問題選択のカーソル（解放済みの範囲）。プレイ中・結果では今の問の番号。</summary>
        public int Cursor { get; private set; }

        public PuzzleRun? Run { get; private set; }

        /// <summary>直前の成功で最少手数を更新したか。</summary>
        public bool IsNewBest { get; private set; }

        /// <summary>なぞりの途中の前の点（押していなければ null）。描画の軌跡に使う。</summary>
        public Vec2i? SwipePoint => _swipe;

        /// <summary>設定されていれば、マウスの代わりにこれで切る（撮影・テスト用）。</summary>
        public ISnipPilot? Pilot { get; set; }

        public void Apply(Intent intent)
        {
            if (!_hasFocus) { return; }
            // ウィンドウを選び直したクリックは操作にしない（010）。押下から離すまでを捨てる。
            if (DropsRefocusClick(intent)) { return; }

            // 札のクリック（Choose）は、問題選択の画面で押したクリックにだけ 1 回効く。
            // タイトルでクリックして問題選択へ移ったとき、同じクリックを離して届く札のクリックで問を始めない。
            if (intent.Kind == IntentKind.PointerDown) { _clickOnSelect = Screen == AppScreen.Select; }
            else if (intent.Kind == IntentKind.Choose)
            {
                if (!_clickOnSelect) { return; }
                _clickOnSelect = false;
            }

            AdvanceTo(intent.Time);
            switch (Screen)
            {
                case AppScreen.Title:
                    if (intent.Kind == IntentKind.Submit || intent.Kind == IntentKind.PointerDown) { ToSelect(); }
                    break;
                case AppScreen.Select:
                    ApplySelect(intent);
                    break;
                case AppScreen.Playing:
                    ApplyPlaying(intent);
                    break;
                case AppScreen.Paused:
                    if (intent.Kind == IntentKind.Cancel || intent.Kind == IntentKind.Submit) { Resume(intent.Time); }
                    else if (intent.Kind == IntentKind.Retry) { Retry(intent.Time); }
                    else if (intent.Kind == IntentKind.Back) { ToSelect(); }
                    break;
                case AppScreen.Outcome:
                    ApplyOutcome(intent);
                    break;
                case AppScreen.Result:
                    if (intent.Kind == IntentKind.Submit || intent.Kind == IntentKind.PointerDown || intent.Kind == IntentKind.Cancel) { ToTitle(); }
                    break;
                default:
                    throw new InvalidOperationException($"未定義の画面です：{Screen}");
            }
        }

        /// <summary>
        /// フォーカスが戻った直後の押下から離すまでを捨てる。捨てた押下は問題選択で押したクリックにならないので、
        /// 札のクリック（Choose）が離す前・後のどちらに届いても効かない。
        /// </summary>
        private bool DropsRefocusClick(Intent intent)
        {
            switch (intent.Kind)
            {
                case IntentKind.PointerDown when _dropNextClick:
                    _dropNextClick = false;
                    _dropUntilUp = true;
                    _clickOnSelect = false;
                    return true;
                case IntentKind.PointerMove when _dropUntilUp:
                    return true;
                case IntentKind.PointerUp when _dropUntilUp:
                    _dropUntilUp = false;
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>フォーカスを失うとなぞりを打ち切り、プレイ中は一時停止する。戻った直後のクリックは捨てる。</summary>
        public void SetFocus(bool focus)
        {
            if (focus && !_hasFocus) { _dropNextClick = true; }
            _hasFocus = focus;
            if (focus) { return; }
            _swipe = null;
            _clickOnSelect = false;
            if (Screen == AppScreen.Playing)
            {
                Screen = AppScreen.Paused;
                _events |= FlowEvents.Paused;
            }
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

        /// <summary>前回の呼び出し以降に起きた出来事を取り出す。</summary>
        public FlowEvents TakeEvents()
        {
            var events = _events;
            _events = FlowEvents.None;
            return events;
        }

        private void ApplySelect(Intent intent)
        {
            switch (intent.Kind)
            {
                case IntentKind.Move:
                    var next = Math.Clamp(Cursor + intent.Index, 0, Progress.Unlocked - 1);
                    if (next != Cursor)
                    {
                        Cursor = next;
                        _events |= FlowEvents.Selected;
                    }

                    break;
                case IntentKind.Submit:
                    Begin(Cursor, intent.Time);
                    break;
                case IntentKind.Choose:
                    if (Progress.IsUnlocked(intent.Index)) { Begin(intent.Index, intent.Time); }
                    break;
                case IntentKind.Cancel:
                case IntentKind.Back:
                    ToTitle();
                    break;
            }
        }

        private void ApplyPlaying(Intent intent)
        {
            var run = Run!;
            switch (intent.Kind)
            {
                case IntentKind.PointerDown:
                    _swipe = intent.Point;
                    break;
                case IntentKind.PointerMove:
                    if (_swipe is { } from && from != intent.Point)
                    {
                        if (Pilot == null) { run.Cut(from, intent.Point); }
                        _swipe = intent.Point;
                    }

                    break;
                case IntentKind.PointerUp:
                    // 最後の移動から離した位置までの線分も切る（移動の出来事が離すのと同じ更新で届いても取りこぼさない）。
                    if (_swipe is { } last && last != intent.Point && Pilot == null) { run.Cut(last, intent.Point); }
                    _swipe = null;
                    break;
                case IntentKind.Retry:
                    Retry(intent.Time);
                    break;
                case IntentKind.Cancel:
                    _swipe = null;
                    Screen = AppScreen.Paused;
                    _events |= FlowEvents.Paused;
                    break;
            }

            CheckEnd();
        }

        private void ApplyOutcome(Intent intent)
        {
            var solved = Run!.Status == RunStatus.Succeeded;
            switch (intent.Kind)
            {
                case IntentKind.Submit:
                case IntentKind.PointerDown:
                    if (!solved) { Retry(intent.Time); }
                    else if (Progress.AllSolved && Cursor == Progress.Count - 1) { ToResult(); }
                    else if (Cursor + 1 < Progress.Count) { Begin(Cursor + 1, intent.Time); }
                    else { ToSelect(); }
                    break;
                case IntentKind.Retry:
                    Retry(intent.Time);
                    break;
                case IntentKind.Back:
                case IntentKind.Cancel:
                    ToSelect();
                    break;
            }
        }

        private void Step()
        {
            var run = Run!;
            if (Pilot != null)
            {
                while (Pilot.SwipeFor(run) is { } swipe) { run.Cut(swipe.From, swipe.To); }
            }

            run.Tick();
            CheckEnd();
        }

        private void CheckEnd()
        {
            var run = Run!;
            _events |= (FlowEvents)((int)run.TakeEvents() << RunShift);
            if (Screen != AppScreen.Playing || run.Status == RunStatus.Playing) { return; }
            Screen = AppScreen.Outcome;
            _swipe = null;
            if (run.Status == RunStatus.Succeeded) { IsNewBest = Progress.Record(Cursor, run.Moves); }
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

        private void Begin(int index, double time)
        {
            Cursor = index;
            Run = new PuzzleRun(_config, _book.Puzzles[index]);
            _events |= FlowEvents.Started;
            Restart(time);
        }

        private void Retry(double time)
        {
            Run!.Reset();
            _events |= FlowEvents.Retried;
            Restart(time);
        }

        private void Restart(double time)
        {
            Pilot?.Reset(Cursor);
            IsNewBest = false;
            _swipe = null;
            Resume(time);
        }

        private void Resume(double time)
        {
            Screen = AppScreen.Playing;
            _origin = time;
            _ticksDone = 0;
        }

        private void ToSelect()
        {
            Run = null;
            _swipe = null;
            Cursor = Math.Min(Cursor, Progress.Unlocked - 1);
            Screen = AppScreen.Select;
            _events |= FlowEvents.ToSelect;
        }

        private void ToTitle()
        {
            Run = null;
            _swipe = null;
            Screen = AppScreen.Title;
            _events |= FlowEvents.Title;
        }

        private void ToResult()
        {
            Screen = AppScreen.Result;
            _events |= FlowEvents.Result;
        }
    }

    /// <summary>画面に反映する出来事。試合の出来事（<see cref="RunEvents"/>）を <see cref="SnipFlow.RunShift"/> だけずらして含める。</summary>
    [Flags]
    public enum FlowEvents
    {
        None = 0,
        Started = 1,
        Retried = 1 << 1,
        Selected = 1 << 2,
        Paused = 1 << 3,
        ToSelect = 1 << 4,
        Title = 1 << 5,
        Result = 1 << 6,
        Cut = (int)RunEvents.Cut << SnipFlow.RunShift,
        Succeeded = (int)RunEvents.Succeeded << SnipFlow.RunShift,
        Failed = (int)RunEvents.Failed << SnipFlow.RunShift,
    }

    /// <summary>解答の手順どおりに切る操縦役。手順の Tick に、そのロープだけを横切る短いなぞりを返す。</summary>
    public sealed class Snipper : ISnipPilot
    {
        private readonly IReadOnlyList<IReadOnlyList<CutStep>> _plans;
        private IReadOnlyList<CutStep> _plan = Array.Empty<CutStep>();
        private int _next;

        public Snipper(IReadOnlyList<IReadOnlyList<CutStep>> plans) => _plans = plans ?? throw new ArgumentNullException(nameof(plans));

        public void Reset(int puzzle)
        {
            _plan = puzzle >= 0 && puzzle < _plans.Count ? _plans[puzzle] : Array.Empty<CutStep>();
            _next = 0;
        }

        public (Vec2i From, Vec2i To)? SwipeFor(PuzzleRun run)
        {
            if (run == null) { throw new ArgumentNullException(nameof(run)); }
            while (_next < _plan.Count && _plan[_next].Tick < run.Ticks) { _next++; }
            while (_next < _plan.Count && _plan[_next].Tick == run.Ticks)
            {
                var swipe = CutPlan.SwipeAcross(run, _plan[_next++].Rope);
                if (swipe != null) { return swipe; }
            }

            return null;
        }
    }
}
