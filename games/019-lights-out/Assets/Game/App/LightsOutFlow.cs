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
        Started = 1 << 0,
        Pressed = 1 << 1,
        Cleared = 1 << 2,
        GaveUp = 1 << 3,
        Retried = 1 << 4,
    }

    /// <summary>画面遷移と、プレイヤーの意図を Core へ渡す窓口。</summary>
    public sealed class LightsOutFlow
    {
        private int _nextSeed;
        private bool _hasFocus = true;
        private FlowEvents _events;

        /// <summary>seed は最初の試合のシード。試合ごとに 1 ずつ進め、再戦では必ず新しいシードを使う。</summary>
        public LightsOutFlow(LightsOutConfig config, int seed)
        {
            if (config == null) { throw new ArgumentNullException(nameof(config)); }
            config.Validate();
            Config = config.Copy();
            _nextSeed = seed;
        }

        public LightsOutConfig Config { get; }

        public AppScreen Screen { get; private set; }

        public LightsOutGame? Game { get; private set; }

        public BoardCursor Cursor { get; } = new();

        /// <summary>あきらめたときの盤の最短解（押すマスの集合）。それ以外は 0。</summary>
        public uint Answer { get; private set; }

        public void Apply(Intent intent)
        {
            if (!_hasFocus) { return; }
            switch (intent.Kind)
            {
                case IntentKind.Submit:
                    if (Screen == AppScreen.Title || Screen == AppScreen.Result) { Begin(); }
                    else if (Screen == AppScreen.Paused) { Screen = AppScreen.Playing; }
                    break;
                case IntentKind.TogglePause:
                    if (Screen == AppScreen.Playing) { Screen = AppScreen.Paused; }
                    else if (Screen == AppScreen.Paused) { Screen = AppScreen.Playing; }
                    break;
                case IntentKind.CursorUp:
                    MoveCursor(0, -1);
                    break;
                case IntentKind.CursorDown:
                    MoveCursor(0, 1);
                    break;
                case IntentKind.CursorLeft:
                    MoveCursor(-1, 0);
                    break;
                case IntentKind.CursorRight:
                    MoveCursor(1, 0);
                    break;
                case IntentKind.PressCursor:
                    Press(Cursor.Cell);
                    break;
                case IntentKind.PressCell:
                    if (Screen == AppScreen.Playing)
                    {
                        Cursor.MoveTo(intent.Cell);
                        Press(intent.Cell);
                    }

                    break;
                case IntentKind.PointCell:
                    if (Screen == AppScreen.Playing) { Cursor.MoveTo(intent.Cell); }
                    break;
                case IntentKind.Retry:
                    if (Screen == AppScreen.Paused && Game!.Reset())
                    {
                        _events |= FlowEvents.Retried;
                        Screen = AppScreen.Playing;
                    }

                    break;
                case IntentKind.NewPuzzle:
                    if (Screen == AppScreen.Paused || Screen == AppScreen.Result) { Begin(); }
                    break;
                case IntentKind.GiveUp:
                    if (Screen == AppScreen.Paused && Game!.GiveUp())
                    {
                        Answer = Game.MinimalSolution();
                        _events |= FlowEvents.GaveUp;
                        Screen = AppScreen.Result;
                    }

                    break;
                case IntentKind.BackToTitle:
                    if (Screen == AppScreen.Paused || Screen == AppScreen.Result)
                    {
                        Game = null;
                        Answer = 0;
                        Screen = AppScreen.Title;
                    }

                    break;
                case IntentKind.Quit:
                    // アプリの終了は Unity 層が行う。ここでは状態を変えない。
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(intent), intent.Kind, "未定義の操作です。");
            }
        }

        /// <summary>フォーカスを失うとプレイ中は一時停止する。戻っても自動では再開しない。失っている間は操作を受け付けない。</summary>
        public void SetFocus(bool focus)
        {
            _hasFocus = focus;
            if (!focus && Screen == AppScreen.Playing) { Screen = AppScreen.Paused; }
        }

        /// <summary>前回の呼び出し以降に起きた出来事を取り出す。</summary>
        public FlowEvents TakeEvents()
        {
            var events = _events;
            _events = FlowEvents.None;
            return events;
        }

        private void MoveCursor(int dx, int dy)
        {
            if (Screen == AppScreen.Playing) { Cursor.Move(dx, dy); }
        }

        private void Press(int cell)
        {
            if (Screen != AppScreen.Playing || Game!.Press(cell) != PressResult.Applied) { return; }
            _events |= FlowEvents.Pressed;
            if (Game.Status == GameStatus.Cleared)
            {
                _events |= FlowEvents.Cleared;
                Screen = AppScreen.Result;
            }
        }

        private void Begin()
        {
            var seed = _nextSeed;
            _nextSeed = unchecked(_nextSeed + 1);
            Game = new LightsOutGame(Config, seed);
            Answer = 0;
            Cursor.Reset();
            _events |= FlowEvents.Started;
            Screen = AppScreen.Playing;
        }
    }
}
