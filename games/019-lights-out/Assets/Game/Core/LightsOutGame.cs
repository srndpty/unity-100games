using System;

namespace Game.Core
{
    public enum GameStatus
    {
        Playing,
        Cleared,
        GaveUp,
    }

    public enum PressResult
    {
        Applied,

        /// <summary>試合が終わっているので受け付けなかった。盤と手数は変わらない。</summary>
        Rejected,
    }

    /// <summary>1 試合。1 試合 = 1 シードで、シードから出題の盤が決まる。</summary>
    public sealed class LightsOutGame
    {
        public LightsOutGame(LightsOutConfig config, int seed)
        {
            if (config == null) { throw new ArgumentNullException(nameof(config)); }
            config.Validate();
            Seed = seed;
            Puzzle = PuzzleGenerator.Generate(seed, config.ScramblePresses);
            Board = Puzzle.Board;
            if (!Gf2Solver.TrySolveMinimal(Puzzle.Board, out var optimal))
            {
                throw new InvalidOperationException("生成した盤が解けません。");
            }

            OptimalMoves = BitCount.Of(optimal);
        }

        public int Seed { get; }

        public Puzzle Puzzle { get; }

        public LightsBoard Board { get; private set; }

        public int Moves { get; private set; }

        public GameStatus Status { get; private set; }

        public bool IsOver => Status != GameStatus.Playing;

        /// <summary>出題の盤の最短手数。</summary>
        public int OptimalMoves { get; }

        /// <summary>今の盤を全消灯にする最短の押下の集合。押下は可解性を保つので、常に解がある。</summary>
        public uint MinimalSolution()
        {
            if (!Gf2Solver.TrySolveMinimal(Board, out var solution))
            {
                throw new InvalidOperationException("盤が解けない状態になっています。");
            }

            return solution;
        }

        public PressResult Press(int index)
        {
            LightsBoard.RequireIndex(index);
            if (IsOver)
            {
                return PressResult.Rejected;
            }

            Board = Board.Press(index);
            if (Moves < int.MaxValue)
            {
                Moves++;
            }

            if (Board.IsDark)
            {
                Status = GameStatus.Cleared;
            }

            return PressResult.Applied;
        }

        /// <summary>出題の盤に戻し、手数を 0 にする。試合中だけ。</summary>
        public bool Reset()
        {
            if (IsOver)
            {
                return false;
            }

            Board = Puzzle.Board;
            Moves = 0;
            return true;
        }

        public bool GiveUp()
        {
            if (IsOver)
            {
                return false;
            }

            Status = GameStatus.GaveUp;
            return true;
        }
    }
}
