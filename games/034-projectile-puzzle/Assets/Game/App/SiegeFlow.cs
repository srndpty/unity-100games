using System;
using System.Collections.Generic;
using Game.Core;

namespace Game.App
{
    public enum AppScreen
    {
        /// <summary>タイトルと操作の説明。</summary>
        Title,

        /// <summary>面選択（解放済みの面と最高点・星）。</summary>
        Select,

        Playing,

        Paused,

        /// <summary>1 面の勝ち・負け（<see cref="SiegeFlow.Run"/> の結果）。</summary>
        Outcome,

        /// <summary>全ての面に勝った後の結果。</summary>
        Result,
    }

    /// <summary>
    /// 物理の世界（Unity の Physics2D）。Flow が 1 Tick ごとに呼ぶ。テストでは物理のない偽物に差し替える。
    /// </summary>
    public interface ISiegeWorld
    {
        /// <summary>面 <paramref name="index"/> を初期配置から作り直す（前の面の物は全て消す）。</summary>
        void Load(int index, Stage stage);

        /// <summary>命令を反映して物理を 1 Tick 進め、結果を <paramref name="report"/>（空にして渡す）に書く。</summary>
        void Simulate(TickCommands commands, TickReport report);

        /// <summary>壊れた部材と、外す接合を消す。<paramref name="shot"/> なら盤に残っている弾も消す（静止したとき）。</summary>
        void Remove(IReadOnlyList<int> pieces, IReadOnlyList<int> joints, bool shot);

        /// <summary>面を片付ける（タイトル・面選択へ戻ったとき）。</summary>
        void Clear();
    }

    /// <summary>マウスの代わりに、Tick ごとに撃つ・起爆するかを決める操縦役（撮影・テスト用）。</summary>
    public interface ISiegePilot
    {
        /// <summary>面を始める・やり直す前に呼ぶ。前の判断の状態を捨てる。</summary>
        void Reset(int stage);

        /// <summary>この Tick の前に撃つ引き（撃たなければ null）。</summary>
        V2? PullFor(SiegeRun run);

        /// <summary>この Tick の前に起爆するか。</summary>
        bool DetonateFor(SiegeRun run);
    }

    /// <summary>
    /// 画面遷移と、プレイヤーの意図を Core へ渡す窓口。
    /// <para>
    /// 時刻と Tick の対応（033 と同じ）：プレイを始めた（再開した）時刻を t0 とし、Tick n は区間 [t0 + (n-1)/60, t0 + n/60) を表す。
    /// 時刻 τ の入力は、τ までの Tick を先に進めてから反映する。撃つ・起爆の命令は次の Tick の境目で効く（物理の途中に割り込まない）。
    /// </para>
    /// <para>
    /// 1 フレームの処理量：<see cref="Apply"/> と <see cref="Advance"/> は 1 フレームで合わせて <see cref="MaxTicksPerFrame"/> Tick までしか
    /// 進めない。超えた区間は飛ばすが、入力は全て順に反映する。
    /// </para>
    /// </summary>
    public sealed class SiegeFlow
    {
        /// <summary>1 フレームで進める Tick 数の上限。</summary>
        public const int MaxTicksPerFrame = 4;

        /// <summary><see cref="FlowEvents"/> の中で試合の出来事を置く位置。</summary>
        public const int RunShift = 8;

        private readonly SiegeConfig _config;
        private readonly StageBook _book;
        private readonly ISiegeWorld _world;
        private readonly TickReport _report = new();
        private bool _hasFocus = true;
        private bool _dropNextClick;
        private bool _dropUntilUp;
        private bool _clickOnSelect;
        private bool _aiming;
        private V2 _pull;
        private double _origin;
        private long _ticksDone;
        private int _frameBudget = MaxTicksPerFrame;
        private FlowEvents _events;

        public SiegeFlow(SiegeConfig config, StageBook book, ISiegeWorld world)
        {
            if (config == null) { throw new ArgumentNullException(nameof(config)); }
            _book = book ?? throw new ArgumentNullException(nameof(book));
            _world = world ?? throw new ArgumentNullException(nameof(world));
            book.Validate(config);
            if (book.Stages.Count == 0) { throw new ArgumentException("面がありません。", nameof(book)); }
            _config = config.Copy();
            Progress = new Progress(book.Stages.Count);
        }

        public AppScreen Screen { get; private set; }

        public StageBook Book => _book;

        /// <summary>検証済みの設定の複製（書き換えても Flow・試合・物理の世界には効かない）。</summary>
        public SiegeConfig Config => _config.Copy();

        public Progress Progress { get; }

        /// <summary>面選択のカーソル（解放済みの範囲）。プレイ中・結果では今の面の番号。</summary>
        public int Cursor { get; private set; }

        public SiegeRun? Run { get; private set; }

        /// <summary>直前の勝ちで最高点を更新したか。</summary>
        public bool IsNewBest { get; private set; }

        /// <summary>構えている引き（長さは <see cref="SiegeConfig.MaxPull"/> で頭打ち、構えていなければ null）。描画と弾道プレビューに使う。</summary>
        public V2? Aim => _aiming ? Launch.Clamp(_pull, _config) : null;

        /// <summary>構えている引きで撃ったときの初速（取り消しになる引きなら null）。</summary>
        public V2? AimVelocity => _aiming ? Launch.Velocity(_pull, _config) : null;

        /// <summary>設定されていれば、マウスの代わりにこれで撃つ（撮影・テスト用）。</summary>
        public ISiegePilot? Pilot { get; set; }

        public void Apply(Intent intent)
        {
            if (!_hasFocus) { return; }
            // ウィンドウを選び直したクリックは操作にしない（010）。押下から離すまでを捨てる。
            if (DropsRefocusClick(intent)) { return; }

            // 札のクリック（Choose）は、面選択の画面で押したクリックにだけ 1 回効く（033）。
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

        /// <summary>フォーカスが戻った直後の押下から離すまでを捨てる（033 と同じ）。</summary>
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

        /// <summary>フォーカスを失うと構えを取り消し、プレイ中は一時停止する。戻った直後のクリックは捨てる。</summary>
        public void SetFocus(bool focus)
        {
            if (focus && !_hasFocus) { _dropNextClick = true; }
            _hasFocus = focus;
            if (focus) { return; }
            _aiming = false;
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
            var manual = Pilot == null;
            switch (intent.Kind)
            {
                case IntentKind.PointerDown:
                    if (manual && run.Phase == RunPhase.Aiming && (intent.Point - run.Stage.Launcher).Length <= _config.GrabRadius)
                    {
                        _aiming = true;
                        _pull = intent.Point - run.Stage.Launcher;
                        _events |= FlowEvents.Grabbed;
                    }
                    else if (manual && run.CanDetonate)
                    {
                        run.Detonate();
                    }

                    break;
                case IntentKind.PointerMove:
                    if (_aiming) { _pull = intent.Point - run.Stage.Launcher; }
                    break;
                case IntentKind.PointerUp:
                    if (_aiming)
                    {
                        _pull = intent.Point - run.Stage.Launcher;
                        _aiming = false;
                        if (!run.Launch(_pull)) { _events |= FlowEvents.AimCancelled; }
                    }

                    break;
                case IntentKind.CancelAim:
                    CancelAim();
                    break;
                case IntentKind.Submit:
                    if (manual && run.CanDetonate) { run.Detonate(); }
                    break;
                case IntentKind.Retry:
                    Retry(intent.Time);
                    break;
                case IntentKind.Cancel:
                    if (_aiming) { CancelAim(); }
                    else
                    {
                        Screen = AppScreen.Paused;
                        _events |= FlowEvents.Paused;
                    }

                    break;
            }

            CheckEnd();
        }

        private void CancelAim()
        {
            if (!_aiming) { return; }
            _aiming = false;
            _events |= FlowEvents.AimCancelled;
        }

        private void ApplyOutcome(Intent intent)
        {
            var won = Run!.Phase == RunPhase.Won;
            switch (intent.Kind)
            {
                case IntentKind.Submit:
                case IntentKind.PointerDown:
                    if (!won) { Retry(intent.Time); }
                    else if (Progress.AllCleared && Cursor == Progress.Count - 1) { ToResult(); }
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
                if (Pilot.PullFor(run) is { } pull) { run.Launch(pull); }
                if (Pilot.DetonateFor(run)) { run.Detonate(); }
            }

            var commands = run.BeginTick();
            _report.Clear();
            _world.Simulate(commands, _report);
            run.EndTick(_report);
            _world.Remove(run.BrokenThisTick, run.JointsToRemove, run.ShotRemovedThisTick);
            CheckEnd();
        }

        private void CheckEnd()
        {
            var run = Run!;
            _events |= (FlowEvents)((int)run.TakeEvents() << RunShift);
            if (run.Phase != RunPhase.Aiming) { _aiming = false; }
            if (Screen != AppScreen.Playing || !run.IsOver) { return; }
            Screen = AppScreen.Outcome;
            if (run.Phase == RunPhase.Won) { IsNewBest = Progress.Record(Cursor, run.Score, run.Stars); }
        }

        private void AdvanceTo(double time)
        {
            if (Screen != AppScreen.Playing || !double.IsFinite(time)) { return; }
            var elapsed = Math.Floor((time - _origin) * SiegeRun.TickRate);
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
            _events |= FlowEvents.Started;
            Restart(time);
        }

        private void Retry(double time)
        {
            _events |= FlowEvents.Retried;
            Restart(time);
        }

        private void Restart(double time)
        {
            var stage = _book.Stages[Cursor];
            Run = new SiegeRun(_config, stage);
            _world.Load(Cursor, stage);
            Pilot?.Reset(Cursor);
            IsNewBest = false;
            _aiming = false;
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
            Leave();
            Cursor = Math.Min(Cursor, Progress.Unlocked - 1);
            Screen = AppScreen.Select;
            _events |= FlowEvents.ToSelect;
        }

        private void ToTitle()
        {
            Leave();
            Screen = AppScreen.Title;
            _events |= FlowEvents.Title;
        }

        private void Leave()
        {
            if (Run != null) { _world.Clear(); }
            Run = null;
            _aiming = false;
        }

        private void ToResult()
        {
            Screen = AppScreen.Result;
            _events |= FlowEvents.Result;
        }
    }

    /// <summary>画面に反映する出来事。試合の出来事（<see cref="RunEvents"/>）を <see cref="SiegeFlow.RunShift"/> だけずらして含める。</summary>
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
        Grabbed = 1 << 7,
        AimCancelled = 1 << 24,
        Launched = (int)RunEvents.Launched << SiegeFlow.RunShift,
        Hit = (int)RunEvents.Hit << SiegeFlow.RunShift,
        Broke = (int)RunEvents.Broke << SiegeFlow.RunShift,
        Detonated = (int)RunEvents.Detonated << SiegeFlow.RunShift,
        Ready = (int)RunEvents.Ready << SiegeFlow.RunShift,
        Won = (int)RunEvents.Won << SiegeFlow.RunShift,
        Lost = (int)RunEvents.Lost << SiegeFlow.RunShift,
    }

    /// <summary>
    /// 解答の手順どおりに撃つ操縦役。次の弾を構えられるようになってから <see cref="AimDelay"/> Tick 静かに待って撃ち、
    /// 手順に起爆の Tick があれば、撃ってからその Tick で起爆する。
    /// </summary>
    public sealed class PlannedPilot : ISiegePilot
    {
        /// <summary>撃つ前に待つ、止まっている Tick 数（静止の判定より長い）。</summary>
        public const int AimDelay = 40;

        private readonly IReadOnlyList<IReadOnlyList<ShotStep>> _plans;
        private IReadOnlyList<ShotStep> _plan = Array.Empty<ShotStep>();

        public PlannedPilot(IReadOnlyList<IReadOnlyList<ShotStep>> plans) => _plans = plans ?? throw new ArgumentNullException(nameof(plans));

        public void Reset(int stage) => _plan = stage >= 0 && stage < _plans.Count ? _plans[stage] : Array.Empty<ShotStep>();

        public V2? PullFor(SiegeRun run)
        {
            if (run == null) { throw new ArgumentNullException(nameof(run)); }
            if (run.Phase != RunPhase.Aiming || run.ShotsUsed >= _plan.Count || run.QuietTicks < AimDelay) { return null; }
            return _plan[run.ShotsUsed].Pull;
        }

        public bool DetonateFor(SiegeRun run)
        {
            if (run == null) { throw new ArgumentNullException(nameof(run)); }
            if (!run.CanDetonate || run.ShotsUsed == 0 || run.ShotsUsed > _plan.Count) { return false; }
            var after = _plan[run.ShotsUsed - 1].DetonateAfter;
            return after >= 0 && run.TicksSinceLaunch >= after;
        }
    }
}
