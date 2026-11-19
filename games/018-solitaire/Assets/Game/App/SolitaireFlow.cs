using System;
using Game.Core;

namespace Game.App
{
    public enum AppScreen
    {
        Title,
        Playing,
        Paused,
        Result,
    }

    [Flags]
    public enum FlowEvents
    {
        None = 0,

        /// <summary>札を場札へ動かした。</summary>
        Moved = 1 << 0,

        /// <summary>札を組札へ置いた。</summary>
        ToFoundation = 1 << 1,

        /// <summary>伏せ札が開いた。</summary>
        Flipped = 1 << 2,

        /// <summary>山札をめくった。</summary>
        Drawn = 1 << 3,

        /// <summary>捨て札を山札へ戻した。</summary>
        Recycled = 1 << 4,

        /// <summary>操作が拒否された。</summary>
        Rejected = 1 << 5,

        /// <summary>1 手取り消した。</summary>
        Undone = 1 << 6,

        Won = 1 << 7,

        Stuck = 1 << 8,

        Resigned = 1 << 9,

        /// <summary>自動で仕上げ始めた。</summary>
        AutoCompleteStarted = 1 << 10,
    }

    /// <summary>直前の操作の結果。HUD の案内に使う。次の操作で置き換わる。</summary>
    public enum Notice
    {
        None,

        /// <summary>コマンドが拒否された。理由は <see cref="SolitaireFlow.LastError"/>。</summary>
        Rejected,

        Undone,

        /// <summary>取り消す手がない。</summary>
        NothingToUndo,
    }

    /// <summary>
    /// 画面遷移と、プレイヤーの操作を Core へ渡す窓口。ポインタの操作は <see cref="CardDragInput"/> で意図に変えてから試合へ渡す。
    /// 実時間を Tick に変えて遊んだ時間を数える。
    /// </summary>
    public sealed class SolitaireFlow
    {
        public const int TickRate = 60;

        /// <summary>経過時間の上限（99 時間 59 分 59 秒）。表示の桁と int の範囲に収める。</summary>
        public const int MaxElapsedTicks = ((99 * 3600) + (59 * 60) + 59) * TickRate;

        /// <summary>自動で仕上げるとき、1 枚送るごとの間隔の上限（Tick）。</summary>
        public const int MaxAutoMoveTicks = 60;

        private int _nextSeed;
        private int _autoMoveTicks = 5;
        private int _autoCountdown;
        private double _elapsedTicks;
        private double _accountedTicks;
        private double _timeError;
        private bool _hasFocus = true;
        private FlowEvents _events;

        /// <summary>seed は最初の試合のシード。試合ごとに 1 ずつ進める。</summary>
        public SolitaireFlow(SolitaireConfig config, int seed, double doubleClickSeconds)
        {
            if (config == null) { throw new ArgumentNullException(nameof(config)); }
            config.Validate();
            Config = config.Copy();
            _nextSeed = seed;
            Input = new CardDragInput(doubleClickSeconds);
            GameFactory = s => new SolitaireGame(Config, s);
        }

        public SolitaireConfig Config { get; }

        public AppScreen Screen { get; private set; }

        public SolitaireGame? Game { get; private set; }

        public CardDragInput Input { get; }

        /// <summary>遊んでいる試合のシード。</summary>
        public int MatchSeed { get; private set; }

        /// <summary>プレイ中に進んだ Tick 数。一時停止中は進まない。</summary>
        public int ElapsedTicks { get; private set; }

        public Notice Notice { get; private set; }

        /// <summary>Notice が Rejected のときの理由。</summary>
        public MoveError LastError { get; private set; }

        /// <summary>試合の作り方（シードから）。撮影とテストが決まった盤から始めるときに差し替える。</summary>
        public Func<int, SolitaireGame> GameFactory { get; set; }

        /// <summary>盤への操作を受け付ける状態。</summary>
        public bool IsPlaying => Screen == AppScreen.Playing;

        /// <summary>自動で仕上げている途中。この間は札の操作と取り消しを受け付けない。</summary>
        public bool IsAutoCompleting { get; private set; }

        /// <summary>「自動で仕上げる」を出す状態。</summary>
        public bool CanStartAutoComplete => IsPlaying && !IsAutoCompleting && Game!.CanAutoComplete;

        /// <summary>自動で仕上げるとき、1 枚送るごとの間隔（Tick、1〜<see cref="MaxAutoMoveTicks"/>）。</summary>
        public int AutoMoveTicks
        {
            get => _autoMoveTicks;
            set
            {
                if (value < 1 || value > MaxAutoMoveTicks)
                {
                    throw new ArgumentOutOfRangeException(nameof(value), value, $"1〜{MaxAutoMoveTicks} の範囲で指定してください。");
                }

                _autoMoveTicks = value;
            }
        }

        /// <summary>決定。タイトルと結果画面では試合を始め、一時停止中は再開する。</summary>
        public void Submit()
        {
            if (!_hasFocus) { return; }
            switch (Screen)
            {
                case AppScreen.Title:
                case AppScreen.Result:
                    Begin();
                    break;
                case AppScreen.Paused:
                    ChangeScreen(AppScreen.Playing);
                    break;
            }
        }

        public void TogglePause()
        {
            if (!_hasFocus) { return; }
            if (Screen == AppScreen.Playing) { ChangeScreen(AppScreen.Paused); }
            else if (Screen == AppScreen.Paused) { ChangeScreen(AppScreen.Playing); }
        }

        /// <summary>新しいシードで配り直す。一時停止中と結果画面でだけ受け付ける。</summary>
        public void Restart()
        {
            if (_hasFocus && (Screen == AppScreen.Paused || Screen == AppScreen.Result)) { Begin(); }
        }

        /// <summary>タイトルへ戻る。一時停止中と結果画面でだけ受け付ける。</summary>
        public void BackToTitle()
        {
            if (!_hasFocus || (Screen != AppScreen.Paused && Screen != AppScreen.Result)) { return; }
            Game = null;
            IsAutoCompleting = false;
            ChangeScreen(AppScreen.Title);
        }

        /// <summary>あきらめて結果画面へ。一時停止中だけ受け付ける。</summary>
        public void Resign()
        {
            if (!_hasFocus || Screen != AppScreen.Paused || !Game!.Resign()) { return; }
            IsAutoCompleting = false;
            _events |= FlowEvents.Resigned;
            ChangeScreen(AppScreen.Result);
        }

        /// <summary>
        /// 自動で仕上げ始める。プレイ中に <see cref="SolitaireGame.CanAutoComplete"/> のときだけ受け付ける。
        /// 以後はプレイ中の時間の経過に合わせて <see cref="AutoMoveTicks"/> ごとに 1 枚ずつ組札へ送る（一時停止中は止まる）。
        /// </summary>
        public void StartAutoComplete()
        {
            if (!_hasFocus || !CanStartAutoComplete) { return; }
            Input.Cancel();
            IsAutoCompleting = true;
            _autoCountdown = AutoMoveTicks;
            Report(Notice.None, FlowEvents.AutoCompleteStarted);
        }

        /// <summary>直前の手を取り消す。</summary>
        public void Undo()
        {
            if (!AcceptOperation()) { return; }
            Input.Cancel();
            if (Game!.Undo()) { Report(Notice.Undone, FlowEvents.Undone); }
            else { Report(Notice.NothingToUndo, FlowEvents.None); }
        }

        /// <summary>ポインタを押した。time は実時間の秒（ダブルクリックの判定に使う）。</summary>
        public void PointerDown(TablePoint point, double time)
        {
            if (!AcceptOperation())
            {
                Input.Cancel();
                return;
            }

            Perform(Input.PointerDown(Game!.Table, point, time));
        }

        public void PointerMove(TablePoint point)
        {
            if (AcceptOperation()) { Input.PointerMove(point); }
        }

        public void PointerUp(TablePoint point, double time)
        {
            if (!AcceptOperation())
            {
                Input.Cancel();
                return;
            }

            Perform(Input.PointerUp(Game!.Table, point, time));
        }

        /// <summary>フォーカスを失うとプレイ中は一時停止し、押下・ドラッグ・選択を捨てる。戻っても自動では再開しない。</summary>
        public void SetFocus(bool focus)
        {
            _hasFocus = focus;
            if (focus) { return; }
            Input.Cancel();
            if (Screen == AppScreen.Playing) { ChangeScreen(AppScreen.Paused); }
            ResetTime();
        }

        /// <summary>
        /// 実時間を経過時間に積算する。この Tick はゲームの進行ではなく遊んだ時間の計測なので、
        /// 処理落ちや長い停止の分も 1 回の上限なしで数え、<see cref="MaxElapsedTicks"/> で止める。
        /// </summary>
        public void Advance(double seconds)
        {
            if (Screen != AppScreen.Playing || !double.IsFinite(seconds) || seconds <= 0) { return; }
            var elapsed = seconds * TickRate;
            var before = ElapsedTicks;
            if (!double.IsFinite(elapsed) || ElapsedTicks + elapsed >= MaxElapsedTicks)
            {
                ElapsedTicks = MaxElapsedTicks;
                RunAutoComplete(int.MaxValue);
                return;
            }

            // 小さい時間の加算でも Tick を取りこぼさないよう、累積の丸め誤差を補償する。
            var increment = elapsed - _timeError;
            var total = _elapsedTicks + increment;
            _timeError = (total - _elapsedTicks) - increment;
            _elapsedTicks = total;
            var accounted = Math.Floor(total);
            ElapsedTicks = (int)Math.Min(MaxElapsedTicks, ElapsedTicks + (accounted - _accountedTicks));
            _accountedTicks = accounted;
            RunAutoComplete(ElapsedTicks - before);
        }

        /// <summary>1 Tick 進める。プレイ中でなければ何もしない。</summary>
        public void StepTick()
        {
            if (Screen != AppScreen.Playing) { return; }
            if (ElapsedTicks < MaxElapsedTicks) { ElapsedTicks++; }
            RunAutoComplete(1);
        }

        /// <summary>前回の呼び出し以降に起きた出来事を取り出す。</summary>
        public FlowEvents TakeEvents()
        {
            var events = _events;
            _events = FlowEvents.None;
            return events;
        }

        private bool AcceptOperation() => _hasFocus && Screen == AppScreen.Playing && !IsAutoCompleting;

        // 進んだ Tick の分だけ待ち時間を減らし、0 になるたびに 1 枚送る。処理落ちで大きく進んだときは、その分をまとめて送る。
        private void RunAutoComplete(int ticks)
        {
            if (!IsAutoCompleting || ticks <= 0) { return; }
            _autoCountdown -= Math.Min(ticks, AutoMoveTicks * Card.DeckSize);
            while (_autoCountdown <= 0 && IsAutoCompleting && Screen == AppScreen.Playing)
            {
                _autoCountdown += AutoMoveTicks;
                if (AutoComplete.NextMove(Game!.Table) is not Command move)
                {
                    IsAutoCompleting = false;
                    return;
                }

                ApplyCommand(Game!.Apply(move));
            }
        }

        private void Perform(PointerAction action)
        {
            var game = Game!;
            MoveError error;
            switch (action.Kind)
            {
                case PointerActionKind.ClickStock:
                    error = game.ClickStock();
                    break;
                case PointerActionKind.Move:
                    error = game.Apply(action.Command);
                    break;
                case PointerActionKind.SendToFoundation:
                    error = game.SendToFoundation(action.Pile);
                    break;
                default:
                    return;
            }

            ApplyCommand(error);
        }

        // 適用した結果を案内と出来事にし、試合が終わったら結果画面へ移る。
        private void ApplyCommand(MoveError error)
        {
            var game = Game!;
            if (error != MoveError.None)
            {
                LastError = error;
                Report(Notice.Rejected, FlowEvents.Rejected);
                return;
            }

            Input.Cancel();
            Report(Notice.None, EventsOf(game.LastMove!.Value));
            if (!game.IsOver) { return; }
            _events |= game.Phase == GamePhase.Won ? FlowEvents.Won : FlowEvents.Stuck;
            IsAutoCompleting = false;
            ChangeScreen(AppScreen.Result);
        }

        private static FlowEvents EventsOf(MoveRecord record)
        {
            var command = record.Command;
            var events = command.Kind switch
            {
                CommandKind.Draw => FlowEvents.Drawn,
                CommandKind.Recycle => FlowEvents.Recycled,
                _ => command.To.Kind == PileKind.Foundation ? FlowEvents.ToFoundation : FlowEvents.Moved,
            };
            return record.Flipped ? events | FlowEvents.Flipped : events;
        }

        private void Report(Notice notice, FlowEvents events)
        {
            Notice = notice;
            if (notice != Notice.Rejected) { LastError = MoveError.None; }
            _events |= events;
        }

        private void Begin()
        {
            MatchSeed = _nextSeed;
            _nextSeed = unchecked(_nextSeed + 1);
            Game = GameFactory(MatchSeed);
            ElapsedTicks = 0;
            IsAutoCompleting = false;
            Report(Notice.None, FlowEvents.None);
            _events = FlowEvents.None;
            ChangeScreen(Game.IsOver ? AppScreen.Result : AppScreen.Playing);
        }

        private void ChangeScreen(AppScreen screen)
        {
            Screen = screen;
            Input.Cancel();
            ResetTime();
        }

        private void ResetTime()
        {
            _elapsedTicks = 0;
            _accountedTicks = 0;
            _timeError = 0;
        }
    }
}
