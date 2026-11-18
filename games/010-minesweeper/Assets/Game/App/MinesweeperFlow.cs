using System;
using Game.Core;

namespace Game.App
{
    public enum AppScreen
    {
        Title,
        Playing,
        Result,
    }

    [Flags]
    public enum FlowEvents
    {
        None = 0,
        Opened = 1 << 0,
        Cascade = 1 << 1,
        Flagged = 1 << 2,
        Unflagged = 1 << 3,
        Exploded = 1 << 4,
        Won = 1 << 5,
    }

    /// <summary>画面遷移と、プレイヤーの操作を Core へ渡す窓口。</summary>
    public sealed class MinesweeperFlow
    {
        private readonly BoardConfig _config;
        private readonly Func<int> _nextSeed;
        private bool _hasFocus = true;
        private FlowEvents _events;

        public MinesweeperFlow(BoardConfig config, Func<int> nextSeed)
        {
            if (config == null) { throw new ArgumentNullException(nameof(config)); }
            config.Validate();
            _config = config.Copy();
            _nextSeed = nextSeed ?? throw new ArgumentNullException(nameof(nextSeed));
        }

        public AppScreen Screen { get; private set; }
        public MinesweeperGame? Game { get; private set; }

        /// <summary>タイトルと結果画面から新しい試合を始める。再戦のたびに新しいシードを使う。</summary>
        public void Submit()
        {
            if (!_hasFocus || Screen == AppScreen.Playing) { return; }
            Game = new MinesweeperGame(_config, _nextSeed());
            Screen = AppScreen.Playing;
            _events = FlowEvents.None;
        }

        public MoveResult Reveal(int x, int y)
        {
            if (!CanMove) { return MoveResult.Rejected; }
            var result = Game!.Reveal(x, y);
            if (result == MoveResult.Rejected) { return result; }
            if (result == MoveResult.Exploded) { _events |= FlowEvents.Exploded; }
            else { _events |= Game.LastOpenedCount > 1 ? FlowEvents.Cascade : FlowEvents.Opened; }
            if (result == MoveResult.Won) { _events |= FlowEvents.Won; }
            if (Game.IsOver) { Screen = AppScreen.Result; }
            return result;
        }

        public MoveResult ToggleFlag(int x, int y)
        {
            if (!CanMove) { return MoveResult.Rejected; }
            var before = Game!.FlagCount;
            var result = Game.ToggleFlag(x, y);
            if (result != MoveResult.Rejected) { _events |= Game.FlagCount > before ? FlowEvents.Flagged : FlowEvents.Unflagged; }
            return result;
        }

        public void Back()
        {
            if (!_hasFocus) { return; }
            Game = null;
            Screen = AppScreen.Title;
            _events = FlowEvents.None;
        }

        /// <summary>フォーカスを失っている間は操作を受け付けない。</summary>
        public void SetFocus(bool focus) => _hasFocus = focus;

        /// <summary>前回の呼び出し以降に起きた出来事を取り出す。</summary>
        public FlowEvents TakeEvents()
        {
            var events = _events;
            _events = FlowEvents.None;
            return events;
        }

        private bool CanMove => _hasFocus && Screen == AppScreen.Playing;
    }
}
