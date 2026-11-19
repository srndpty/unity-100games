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

    /// <summary>プレイヤーから見た試合の結果。</summary>
    public enum Outcome
    {
        Win,
        Loss,
        Draw,
    }

    [Flags]
    public enum FlowEvents
    {
        None = 0,

        /// <summary>石を置いた（プレイヤーか CPU）。</summary>
        Placed = 1 << 0,

        /// <summary>パスした。</summary>
        Passed = 1 << 1,

        /// <summary>終局した。</summary>
        GameOver = 1 << 2,

        /// <summary>プレイヤーの手が非合法で拒否された。</summary>
        Rejected = 1 << 3,
    }

    /// <summary>画面遷移と、プレイヤーの操作を Core へ渡す窓口。実時間を固定 Tick へ変換し、CPU の手番を進める。</summary>
    public sealed class OthelloFlow
    {
        public const int MaxTicksPerFrame = 5;

        private int _nextSeed;
        private double _elapsedTicks;
        private double _accountedTicks;
        private double _timeError;
        private bool _hasFocus = true;
        private FlowEvents _events;

        /// <summary>seed は最初の試合のシード。試合ごとに1ずつ進める。</summary>
        public OthelloFlow(OthelloConfig config, int seed)
        {
            if (config == null) { throw new ArgumentNullException(nameof(config)); }
            config.Validate();
            Config = config.Copy();
            _nextSeed = seed;
            GameFactory = _ => new OthelloGame(Config);
        }

        public OthelloConfig Config { get; }
        public AppScreen Screen { get; private set; }
        public OthelloGame? Game { get; private set; }
        public CpuPlayer? Cpu { get; private set; }
        public BoardCursor Cursor { get; } = new();

        /// <summary>CPU の強さ。タイトルと結果画面で選ぶ。</summary>
        public CpuLevel Level { get; private set; } = CpuLevel.Normal;

        /// <summary>プレイヤーの石の色。タイトルと結果画面で選ぶ。</summary>
        public Side PlayerSide { get; private set; } = Side.Black;

        public Side CpuSide => PlayerSide.Opponent();

        /// <summary>遊んでいる試合のシード。</summary>
        public int MatchSeed { get; private set; }

        /// <summary>プレイヤーが最後に試みた着手の判定。</summary>
        public PlayVerdict? LastVerdict { get; private set; }

        /// <summary>試合の作り方。撮影とテストが決まった局面から始めるときに差し替える。</summary>
        public Func<int, OthelloGame> GameFactory { get; set; }

        /// <summary>プレイヤーが打てる状態。</summary>
        public bool IsPlayerTurn => Screen == AppScreen.Playing && Game!.AcceptsMove && Game.ToMove == PlayerSide;

        /// <summary>CPU の手番で、考えている。</summary>
        public bool IsCpuTurn => Screen == AppScreen.Playing && Game!.AcceptsMove && Game.ToMove == CpuSide;

        /// <summary>終局した試合の結果。終局前は null。</summary>
        public Outcome? Outcome
        {
            get
            {
                if (Game == null || !Game.IsOver) { return null; }
                var winner = Game.Winner;
                return winner == null ? App.Outcome.Draw : winner == PlayerSide ? App.Outcome.Win : App.Outcome.Loss;
            }
        }

        /// <summary>強さを選ぶ。タイトルと結果画面でだけ受け付ける。</summary>
        public void SelectLevel(CpuLevel level)
        {
            if (!CanChooseOptions) { return; }
            if (level < CpuLevel.Weak || level > CpuLevel.Strong) { throw new ArgumentOutOfRangeException(nameof(level), level, "未定義の強さです。"); }
            Level = level;
        }

        /// <summary>強さを1段ずつ変える。端で止まる。</summary>
        public void ShiftLevel(int delta)
        {
            if (!CanChooseOptions) { return; }
            var next = (int)Level + Math.Sign(delta);
            if (next >= (int)CpuLevel.Weak && next <= (int)CpuLevel.Strong) { Level = (CpuLevel)next; }
        }

        /// <summary>石の色を選ぶ。タイトルと結果画面でだけ受け付ける。</summary>
        public void SelectSide(Side side)
        {
            if (!CanChooseOptions) { return; }
            if (side != Side.Black && side != Side.White) { throw new ArgumentOutOfRangeException(nameof(side), side, "未定義の色です。"); }
            PlayerSide = side;
        }

        public void ToggleSide() => SelectSide(PlayerSide.Opponent());

        /// <summary>決定。タイトルと結果画面では試合を始め、一時停止中は再開し、プレイ中はカーソルの位置に打つ。</summary>
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
                case AppScreen.Playing:
                    PlaceAtCursor();
                    break;
            }
        }

        public void TogglePause()
        {
            if (!_hasFocus) { return; }
            if (Screen == AppScreen.Playing) { ChangeScreen(AppScreen.Paused); }
            else if (Screen == AppScreen.Paused) { ChangeScreen(AppScreen.Playing); }
        }

        /// <summary>新しいシードで試合をやり直す。一時停止中と結果画面でだけ受け付ける。</summary>
        public void Restart()
        {
            if (_hasFocus && (Screen == AppScreen.Paused || Screen == AppScreen.Result)) { Begin(); }
        }

        /// <summary>タイトルへ戻る。一時停止中と結果画面でだけ受け付ける。</summary>
        public void BackToTitle()
        {
            if (!_hasFocus || (Screen != AppScreen.Paused && Screen != AppScreen.Result)) { return; }
            Game = null;
            Cpu = null;
            ChangeScreen(AppScreen.Title);
        }

        /// <summary>盤をクリックした。座標は左上の角が 0、1マスが 1。盤外とプレイヤーの手番以外は捨てる。</summary>
        public void PointerDown(double column, double row)
        {
            if (!AcceptOperation()) { return; }
            if (BoardCursor.SquareAt(column, row) is not Square square) { return; }
            Cursor.MoveTo(square);
            Play(square);
        }

        /// <summary>矢印キー。カーソルは CPU の手番でも動かせる。</summary>
        public void MoveCursor(int dx, int dy)
        {
            if (_hasFocus && Screen == AppScreen.Playing) { Cursor.Move(dx, dy); }
        }

        /// <summary>カーソルの位置に打つ。</summary>
        public void PlaceAtCursor()
        {
            if (AcceptOperation()) { Play(Cursor.Position); }
        }

        /// <summary>フォーカスを失うとプレイ中は一時停止する。戻っても自動では再開しない。</summary>
        public void SetFocus(bool focus)
        {
            _hasFocus = focus;
            if (focus) { return; }
            if (Screen == AppScreen.Playing) { ChangeScreen(AppScreen.Paused); }
            ResetTime();
        }

        /// <summary>実時間を進める。処理落ちしても1回で進める Tick 数には上限がある。</summary>
        public void Advance(double seconds)
        {
            if (Screen != AppScreen.Playing || !double.IsFinite(seconds) || seconds <= 0) { return; }

            // 小さい時間の加算でも Tick を取りこぼさないよう、累積の丸め誤差を補償する。
            var elapsed = seconds * OthelloConfig.TickRate;
            if (!double.IsFinite(elapsed)) { elapsed = MaxTicksPerFrame; }
            var whole = Math.Floor(elapsed);
            var increment = Math.Min(whole, MaxTicksPerFrame) + (elapsed - whole) - _timeError;
            var total = _elapsedTicks + increment;
            _timeError = (total - _elapsedTicks) - increment;
            _elapsedTicks = total;
            var accounted = Math.Floor(total);
            var count = (int)Math.Min(MaxTicksPerFrame, accounted - _accountedTicks);
            _accountedTicks = accounted;
            for (var i = 0; i < count && Screen == AppScreen.Playing; i++) { StepTick(); }
        }

        /// <summary>1 Tick 進める。演出を進め、CPU の手番なら考える。プレイ中でなければ何もしない。</summary>
        public void StepTick()
        {
            if (Screen != AppScreen.Playing) { return; }
            Game!.Tick();
            Cpu!.Tick(Game);
            Collect();
        }

        /// <summary>前回の呼び出し以降に起きた出来事を取り出す。</summary>
        public FlowEvents TakeEvents()
        {
            var events = _events;
            _events = FlowEvents.None;
            return events;
        }

        private bool CanChooseOptions => _hasFocus && (Screen == AppScreen.Title || Screen == AppScreen.Result);

        // 盤への着手を受け付けるか。演出中・CPU の手番に来た操作は捨てる（先行入力しない）。
        private bool AcceptOperation() => _hasFocus && IsPlayerTurn;

        private void Play(Square square)
        {
            LastVerdict = Game!.TryPlay(PlayerSide, square);
            if (LastVerdict != PlayVerdict.Ok) { _events |= FlowEvents.Rejected; }
            Collect();
        }

        private void Begin()
        {
            MatchSeed = _nextSeed;
            _nextSeed = unchecked(_nextSeed + 1);
            Game = GameFactory(MatchSeed);
            Cpu = new CpuPlayer(CpuSide, Config.DepthOf(Level), Config.CpuThinkTicks, MatchSeed);
            LastVerdict = null;
            _events = FlowEvents.None;
            Cursor.Reset();
            ChangeScreen(AppScreen.Playing);
            Collect();
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

        private void Collect()
        {
            var events = Game!.TakeEvents();
            if ((events & GameEvents.Placed) != 0) { _events |= FlowEvents.Placed; }
            if ((events & GameEvents.Passed) != 0) { _events |= FlowEvents.Passed; }
            if ((events & GameEvents.GameOver) != 0) { _events |= FlowEvents.GameOver; }
            if (Game.IsOver && Screen == AppScreen.Playing) { ChangeScreen(AppScreen.Result); }
        }
    }
}
