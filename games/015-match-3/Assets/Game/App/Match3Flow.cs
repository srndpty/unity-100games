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

    /// <summary>画面遷移と、プレイヤーの操作を Core へ渡す窓口。実時間を固定 Tick へ変換する。</summary>
    public sealed class Match3Flow
    {
        public const int MaxTicksPerFrame = 5;

        private int _nextSeed;
        private double _elapsedTicks;
        private double _accountedTicks;
        private double _timeError;
        private bool _hasFocus = true;
        private GameEvents _events;

        /// <summary>seed は最初の試合のシード。試合ごとに1ずつ進める。</summary>
        public Match3Flow(Match3Config config, int seed)
        {
            if (config == null) { throw new ArgumentNullException(nameof(config)); }
            config.Validate();
            Config = config.Copy();
            _nextSeed = seed;
            MatchFactory = matchSeed => new Match3Game(Config, matchSeed);
        }

        public Match3Config Config { get; }
        public AppScreen Screen { get; private set; }
        public Match3Game? Game { get; private set; }
        public SwapInput Input { get; } = new();

        /// <summary>遊んでいる試合のシード。</summary>
        public int MatchSeed { get; private set; }

        /// <summary>最後に試みた入れ替えの判定。</summary>
        public SwapVerdict? LastVerdict { get; private set; }

        /// <summary>試合の作り方。撮影とテストが決まった盤面や設定を渡すときに差し替える。</summary>
        public Func<int, Match3Game> MatchFactory { get; set; }

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
            ChangeScreen(AppScreen.Title);
        }

        /// <summary>マウスのボタンを押した。位置は盤面座標。</summary>
        public void PointerDown(double x, double y)
        {
            if (AcceptOperation()) { TrySwap(Input.PointerDown(x, y)); }
        }

        /// <summary>押したままマウスを動かした。</summary>
        public void PointerMove(double x, double y)
        {
            if (AcceptOperation()) { TrySwap(Input.PointerMove(x, y)); }
        }

        /// <summary>マウスのボタンを離した。ドラッグの後始末なので、画面や解決中かを問わず行う。</summary>
        public void PointerUp() => Input.PointerUp();

        /// <summary>矢印キー。</summary>
        public void Move(int dx, int dy)
        {
            if (AcceptOperation()) { TrySwap(Input.Move(dx, dy)); }
        }

        /// <summary>Space。</summary>
        public void ToggleSelect()
        {
            if (AcceptOperation()) { Input.ToggleSelect(); }
        }

        /// <summary>フォーカスを失うと一時停止し、選択も取り消す。戻っても自動では再開しない。</summary>
        public void SetFocus(bool focus)
        {
            _hasFocus = focus;
            if (focus) { return; }
            if (Screen == AppScreen.Playing) { ChangeScreen(AppScreen.Paused); }
            ResetInput();
        }

        /// <summary>実時間を進める。処理落ちしても1回で進める Tick 数には上限がある。</summary>
        public void Advance(double seconds)
        {
            if (Screen != AppScreen.Playing || !double.IsFinite(seconds) || seconds <= 0) { return; }

            // 小さい時間の加算でも Tick を取りこぼさないよう、累積の丸め誤差を補償する。
            var elapsed = seconds * Match3Config.TickRate;
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

        /// <summary>1 Tick 進める。プレイ中でなければ何もしない。</summary>
        public void StepTick()
        {
            if (Screen != AppScreen.Playing) { return; }
            Game!.Tick();
            Collect();
        }

        /// <summary>前回の呼び出し以降に試合で起きた出来事を取り出す。</summary>
        public GameEvents TakeEvents()
        {
            var events = _events;
            _events = GameEvents.None;
            return events;
        }

        // 盤面の操作を受け付けるか。解決の演出中に来た操作は捨て、選択とドラッグも取り消す（先行入力しない）。
        private bool AcceptOperation()
        {
            if (!_hasFocus || Screen != AppScreen.Playing) { return false; }
            if (Game!.AcceptsSwap) { return true; }
            Input.Clear();
            return false;
        }

        private void TrySwap(Swap? swap)
        {
            if (swap is not Swap s) { return; }
            LastVerdict = Game!.TrySwap(s.From, s.To);
            Collect();
        }

        private void Begin()
        {
            MatchSeed = _nextSeed;
            _nextSeed = unchecked(_nextSeed + 1);
            Game = MatchFactory(MatchSeed);
            LastVerdict = null;
            _events = GameEvents.None;
            ChangeScreen(AppScreen.Playing);
            Collect();
        }

        private void ChangeScreen(AppScreen screen)
        {
            Screen = screen;
            ResetInput();
        }

        // 選択やドラッグを次の画面へ持ち越さない。
        private void ResetInput()
        {
            Input.Clear();
            _elapsedTicks = 0;
            _accountedTicks = 0;
            _timeError = 0;
        }

        private void Collect()
        {
            _events |= Game!.TakeEvents();
            if (Game.IsOver && Screen == AppScreen.Playing) { ChangeScreen(AppScreen.Result); }
        }
    }
}
