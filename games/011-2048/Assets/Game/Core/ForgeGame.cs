using System;

namespace Game.Core
{
    public enum GamePhase
    {
        Playing,
        Won,
        Lost,
    }

    public enum MoveResult
    {
        /// <summary>盤面が変わらない手、または終了後の手。状態も乱数も変化しない。</summary>
        Rejected,
        Moved,
        Won,
        Lost,
    }

    /// <summary>1 試合の進行。移動は <see cref="MoveRules"/>、出現は <see cref="SpawnRules"/> に任せ、順序と勝敗だけを決める。</summary>
    public sealed class ForgeGame
    {
        private readonly ForgeRules _rules;
        private readonly SeededRandom _random;

        public ForgeGame(ForgeRules rules, int seed)
            : this(rules, seed, BoardState.Empty)
        {
            for (var i = 0; i < ForgeRules.InitialTiles; i++) { Spawn(); }
            LastSpawn = null;
        }

        private ForgeGame(ForgeRules rules, int seed, BoardState board)
        {
            if (rules == null) { throw new ArgumentNullException(nameof(rules)); }
            rules.Validate();
            _rules = rules.Copy();
            _random = new SeededRandom(seed);
            Seed = seed;
            Board = board;
        }

        public int Seed { get; }
        public BoardState Board { get; private set; }
        public GamePhase Phase { get; private set; }
        public int Score { get; private set; }
        public int MoveCount { get; private set; }

        /// <summary>直前の有効な手の移動。開始直後は null。</summary>
        public MoveOutcome? LastOutcome { get; private set; }

        /// <summary>直前の有効な手で出現した板。勝利した手では出現しないので null。</summary>
        public SpawnedTile? LastSpawn { get; private set; }

        public bool IsOver => Phase != GamePhase.Playing;

        /// <summary>用意した盤面から始める。決着済みの盤面や、板のない盤面は受け付けない。</summary>
        public static ForgeGame FromBoard(BoardState board, ForgeRules rules, int seed)
        {
            if (board == null) { throw new ArgumentNullException(nameof(board)); }
            if (board.TileCount == 0) { throw new ArgumentException("板がありません。", nameof(board)); }
            if (board.MaxValue >= ForgeRules.TargetValue) { throw new ArgumentException("目標に到達済みです。", nameof(board)); }
            if (!MoveRules.HasAnyMove(board)) { throw new ArgumentException("動かせる手がありません。", nameof(board)); }
            return new ForgeGame(rules, seed, board);
        }

        public MoveResult Move(Direction direction)
        {
            if (IsOver || !MoveRules.IsDirection(direction)) { return MoveResult.Rejected; }
            var outcome = MoveRules.Slide(Board, direction);
            if (!outcome.Changed) { return MoveResult.Rejected; }

            Board = outcome.After;
            Score += outcome.ScoreGained;
            MoveCount++;
            LastOutcome = outcome;
            LastSpawn = null;

            if (Board.MaxValue >= ForgeRules.TargetValue)
            {
                Phase = GamePhase.Won;
                return MoveResult.Won;
            }

            Spawn();
            if (MoveRules.HasAnyMove(Board)) { return MoveResult.Moved; }
            Phase = GamePhase.Lost;
            return MoveResult.Lost;
        }

        private void Spawn()
        {
            var tile = SpawnRules.Choose(Board, _random, _rules.FourChancePercent);
            Board = Board.With(tile.Cell, tile.Value);
            LastSpawn = tile;
        }
    }
}
