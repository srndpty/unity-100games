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

        /// <summary>数字を置いた（ヒントで埋めたときも含む）。</summary>
        Placed = 1 << 0,

        /// <summary>候補を付け外しした。</summary>
        Noted = 1 << 1,

        /// <summary>数字か候補を消した。</summary>
        Erased = 1 << 2,

        /// <summary>操作が拒否された。</summary>
        Rejected = 1 << 3,

        /// <summary>ヒントを出した。</summary>
        Hinted = 1 << 4,

        /// <summary>全マスが埋まったが、解と違う。</summary>
        FullButWrong = 1 << 5,

        /// <summary>完成した。</summary>
        Solved = 1 << 6,
    }

    /// <summary>直前の操作の結果。HUD の案内に使う。次の操作で置き換わる。</summary>
    public enum Notice
    {
        None,
        Placed,
        Noted,
        Erased,
        Conflict,
        Given,
        Filled,
        FullButWrong,
        HintMistake,
        HintDeduced,
        HintRevealed,
    }

    /// <summary>画面遷移と、プレイヤーの操作を Core へ渡す窓口。実時間を固定 Tick へ変換して経過時間を数える。</summary>
    public sealed class SudokuFlow
    {
        public const int TickRate = 60;

        /// <summary>経過時間の上限（99 時間 59 分 59 秒）。表示の桁と int の範囲に収める。</summary>
        public const int MaxElapsedTicks = (99 * 3600 + 59 * 60 + 59) * TickRate;

        private int _nextSeed;
        private double _elapsedTicks;
        private double _accountedTicks;
        private double _timeError;
        private bool _hasFocus = true;
        private FlowEvents _events;

        /// <summary>seed は最初の試合のシード。試合ごとに 1 ずつ進める。</summary>
        public SudokuFlow(SudokuConfig config, int seed)
        {
            if (config == null) { throw new ArgumentNullException(nameof(config)); }
            config.Validate();
            Config = config.Copy();
            _nextSeed = seed;
            GameFactory = (s, difficulty) => new SudokuGame(PuzzleGenerator.Generate(s, Config, difficulty));
        }

        public SudokuConfig Config { get; }
        public AppScreen Screen { get; private set; }
        public SudokuGame? Game { get; private set; }
        public CellCursor Cursor { get; } = new();

        /// <summary>次の試合の難しさ。タイトルと結果画面で選ぶ。</summary>
        public Difficulty Difficulty { get; private set; } = Difficulty.Normal;

        /// <summary>遊んでいる試合の難しさ。結果画面で次の試合の難しさを選んでも変わらない。</summary>
        public Difficulty MatchDifficulty { get; private set; } = Difficulty.Normal;

        /// <summary>数字キーで候補を付け外しする（オフなら数字を置く）。</summary>
        public bool NoteMode { get; private set; }

        /// <summary>遊んでいる試合のシード。</summary>
        public int MatchSeed { get; private set; }

        /// <summary>プレイ中に進んだ Tick 数。一時停止中は進まない。</summary>
        public int ElapsedTicks { get; private set; }

        public Notice Notice { get; private set; }

        /// <summary>Notice が Conflict のときの重複した相手のマス。それ以外は -1。</summary>
        public int ConflictCell { get; private set; } = -1;

        /// <summary>最後に出したヒント。次の操作で消える。</summary>
        public Hint? LastHint { get; private set; }

        /// <summary>最後にヒントを出した時点の ElapsedTicks。指したマスを光らせる時間の計算に使う。</summary>
        public int HintTick { get; private set; }

        /// <summary>試合の作り方（シードと難しさから）。撮影とテストが決まった盤から始めるときに差し替える。</summary>
        public Func<int, Difficulty, SudokuGame> GameFactory { get; set; }

        /// <summary>盤への操作を受け付ける状態。</summary>
        public bool IsPlaying => Screen == AppScreen.Playing;

        /// <summary>難しさを選ぶ。タイトルと結果画面でだけ受け付ける。</summary>
        public void SelectDifficulty(Difficulty difficulty)
        {
            if (!CanChooseOptions) { return; }
            if (difficulty < Difficulty.Easy || difficulty > Difficulty.Hard) { throw new ArgumentOutOfRangeException(nameof(difficulty), difficulty, "未定義の難しさです。"); }
            Difficulty = difficulty;
        }

        /// <summary>矢印キー。タイトルと結果画面では左右で難しさを選び（端で止まる）、プレイ中は選ぶマスを動かす。</summary>
        public void Navigate(int dx, int dy)
        {
            if (CanChooseOptions)
            {
                var next = (int)Difficulty + Math.Sign(dx);
                if (next >= (int)Difficulty.Easy && next <= (int)Difficulty.Hard) { Difficulty = (Difficulty)next; }
                return;
            }

            MoveCursor(dx, dy);
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

        /// <summary>新しいシードで別の問題を始める。一時停止中と結果画面でだけ受け付ける。</summary>
        public void Restart()
        {
            if (_hasFocus && (Screen == AppScreen.Paused || Screen == AppScreen.Result)) { Begin(); }
        }

        /// <summary>タイトルへ戻る。一時停止中と結果画面でだけ受け付ける。</summary>
        public void BackToTitle()
        {
            if (!_hasFocus || (Screen != AppScreen.Paused && Screen != AppScreen.Result)) { return; }
            Game = null;
            ChangeScreen(AppScreen.Title);
        }

        /// <summary>盤をクリックした。座標は左上の角が 0、1 マスが 1。マスを選ぶだけで、数字は置かない。</summary>
        public void PointerDown(double column, double row)
        {
            if (!AcceptOperation()) { return; }
            if (CellCursor.CellAt(column, row) is int cell)
            {
                Cursor.MoveTo(cell);
                ClearConflict();
            }
        }

        /// <summary>選ぶマスを動かす。下と右が正。</summary>
        public void MoveCursor(int dx, int dy)
        {
            if (!AcceptOperation()) { return; }
            Cursor.Move(dx, dy);
            ClearConflict();
        }

        /// <summary>数字キー。候補モードなら候補を付け外しし、そうでなければ置く。</summary>
        public void InputDigit(int digit)
        {
            if (!AcceptOperation()) { return; }
            var game = Game!;
            var cell = Cursor.Cell;
            var verdict = NoteMode ? game.ToggleNote(cell, digit) : game.Place(cell, digit);
            if (verdict == MoveVerdict.Ok)
            {
                Report(NoteMode ? Notice.Noted : Notice.Placed, NoteMode ? FlowEvents.Noted : FlowEvents.Placed);
                CheckFinished();
                return;
            }

            if (verdict == MoveVerdict.Unchanged)
            {
                Report(Notice.None, FlowEvents.None);
                return;
            }

            Reject(verdict);
        }

        /// <summary>選んでいるマスの数字（なければ候補）を消す。</summary>
        public void Erase()
        {
            if (!AcceptOperation()) { return; }
            var verdict = Game!.Erase(Cursor.Cell);
            if (verdict == MoveVerdict.Ok) { Report(Notice.Erased, FlowEvents.Erased); }
            else if (verdict == MoveVerdict.Empty) { Report(Notice.None, FlowEvents.None); }
            else { Reject(verdict); }
        }

        public void ToggleNoteMode()
        {
            if (AcceptOperation()) { NoteMode = !NoteMode; }
        }

        /// <summary>ヒントを出す。指した・埋めたマスを選ぶ。</summary>
        public void RequestHint()
        {
            if (!AcceptOperation()) { return; }
            var hint = Game!.RequestHint();
            if (hint.Kind == HintKind.None) { return; }
            Report(
                hint.Kind switch
                {
                    HintKind.Mistake => Notice.HintMistake,
                    HintKind.Deduced => Notice.HintDeduced,
                    _ => Notice.HintRevealed,
                },
                hint.Kind == HintKind.Mistake ? FlowEvents.Hinted : FlowEvents.Hinted | FlowEvents.Placed);
            LastHint = hint;
            HintTick = ElapsedTicks;
            Cursor.MoveTo(hint.Cell);
            if (hint.Kind != HintKind.Mistake) { CheckFinished(); }
        }

        /// <summary>フォーカスを失うとプレイ中は一時停止する。戻っても自動では再開しない。</summary>
        public void SetFocus(bool focus)
        {
            _hasFocus = focus;
            if (focus) { return; }
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
            if (!double.IsFinite(elapsed) || ElapsedTicks + elapsed >= MaxElapsedTicks)
            {
                ElapsedTicks = MaxElapsedTicks;
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
        }

        /// <summary>1 Tick 進める。プレイ中でなければ何もしない。</summary>
        public void StepTick()
        {
            if (Screen == AppScreen.Playing && ElapsedTicks < MaxElapsedTicks) { ElapsedTicks++; }
        }

        /// <summary>前回の呼び出し以降に起きた出来事を取り出す。</summary>
        public FlowEvents TakeEvents()
        {
            var events = _events;
            _events = FlowEvents.None;
            return events;
        }

        private bool AcceptOperation() => _hasFocus && Screen == AppScreen.Playing;

        private bool CanChooseOptions => _hasFocus && (Screen == AppScreen.Title || Screen == AppScreen.Result);

        private void Report(Notice notice, FlowEvents events)
        {
            Notice = notice;
            ConflictCell = -1;
            LastHint = null;
            _events |= events;
        }

        // 重複の案内は、拒否したマスと選んでいるマスの位置関係（同じ行・列・ブロック）で文が決まる。
        // 選ぶマスが変わると説明が食い違うので、マスを選び直したら消す。
        private void ClearConflict()
        {
            if (Notice != Notice.Conflict) { return; }
            Notice = Notice.None;
            ConflictCell = -1;
        }

        private void Reject(MoveVerdict verdict)
        {
            Report(
                verdict switch
                {
                    MoveVerdict.Conflict => Notice.Conflict,
                    MoveVerdict.Given => Notice.Given,
                    MoveVerdict.Filled => Notice.Filled,
                    _ => throw new InvalidOperationException($"プレイ中のマスへの操作では起きない判定です: {verdict}"),
                },
                FlowEvents.Rejected);
            ConflictCell = Game!.LastConflict;
        }

        private void CheckFinished()
        {
            var game = Game!;
            if (game.IsSolved)
            {
                _events |= FlowEvents.Solved;
                ChangeScreen(AppScreen.Result);
            }
            else if (game.IsFull)
            {
                Notice = Notice.FullButWrong;
                _events |= FlowEvents.FullButWrong;
            }
        }

        private void Begin()
        {
            MatchSeed = _nextSeed;
            _nextSeed = unchecked(_nextSeed + 1);
            MatchDifficulty = Difficulty;
            Game = GameFactory(MatchSeed, MatchDifficulty);
            ElapsedTicks = 0;
            NoteMode = false;
            HintTick = 0;
            Report(Notice.None, FlowEvents.None);
            _events = FlowEvents.None;
            Cursor.Reset();
            ChangeScreen(AppScreen.Playing);
        }

        private void ChangeScreen(AppScreen screen)
        {
            Screen = screen;
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
