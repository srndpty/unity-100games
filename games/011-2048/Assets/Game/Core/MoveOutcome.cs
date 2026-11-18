using System.Collections.Generic;

namespace Game.Core
{
    public enum Direction
    {
        Left,
        Right,
        Up,
        Down,
    }

    /// <summary>1 枚の板の動き。合体する 2 枚はどちらも <see cref="Merges"/> が true で、同じ <see cref="To"/> へ向かう。</summary>
    public sealed class TileMove
    {
        public TileMove(int from, int to, int value, bool merges)
        {
            From = from;
            To = to;
            Value = value;
            Merges = merges;
        }

        public int From { get; }
        public int To { get; }
        public int Value { get; }
        public bool Merges { get; }
    }

    /// <summary>移動の前後。描画はこの内容だけで板を動かせる。</summary>
    public sealed class MoveOutcome
    {
        public MoveOutcome(BoardState before, BoardState after, Direction direction, int scoreGained, IReadOnlyList<TileMove> moves)
        {
            Before = before;
            After = after;
            Direction = direction;
            ScoreGained = scoreGained;
            Moves = moves;
        }

        public BoardState Before { get; }
        public BoardState After { get; }
        public Direction Direction { get; }
        public int ScoreGained { get; }
        public IReadOnlyList<TileMove> Moves { get; }
        public bool Changed => !Before.Equals(After);
    }
}
