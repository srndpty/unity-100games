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
        Moved = 1 << 0,
        Merged = 1 << 1,
        Won = 1 << 2,
        Lost = 1 << 3,
    }

    /// <summary>画面遷移と、プレイヤーの操作を Core へ渡す窓口。</summary>
    public sealed class ForgeFlow
    {
        private readonly ForgeRules _rules;
        private readonly Func<int> _nextSeed;
        private bool _hasFocus = true;
        private FlowEvents _events;

        public ForgeFlow(ForgeRules rules, Func<int> nextSeed)
        {
            if (rules == null) { throw new ArgumentNullException(nameof(rules)); }
            rules.Validate();
            _rules = rules.Copy();
            _nextSeed = nextSeed ?? throw new ArgumentNullException(nameof(nextSeed));
        }

        public AppScreen Screen { get; private set; }
        public ForgeGame? Game { get; private set; }

        /// <summary>タイトルと結果画面から新しい試合を始める。再戦のたびに新しいシードを使う。</summary>
        public void Submit()
        {
            if (!_hasFocus || Screen == AppScreen.Playing) { return; }
            Begin(new ForgeGame(_rules, _nextSeed()));
        }

        /// <summary>用意した盤面から試合を始める。撮影台本と PlayMode テストが決まった場面を作るために使う。</summary>
        public void StartFrom(BoardState board) => Begin(ForgeGame.FromBoard(board, _rules, _nextSeed()));

        public MoveResult Move(Direction direction)
        {
            if (!_hasFocus || Screen != AppScreen.Playing) { return MoveResult.Rejected; }
            var result = Game!.Move(direction);
            if (result == MoveResult.Rejected) { return result; }

            _events |= FlowEvents.Moved;
            if (Game.LastOutcome!.ScoreGained > 0) { _events |= FlowEvents.Merged; }
            if (result == MoveResult.Won) { _events |= FlowEvents.Won; }
            if (result == MoveResult.Lost) { _events |= FlowEvents.Lost; }
            if (Game.IsOver) { Screen = AppScreen.Result; }
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

        private void Begin(ForgeGame game)
        {
            Game = game;
            Screen = AppScreen.Playing;
            _events = FlowEvents.None;
        }
    }
}
