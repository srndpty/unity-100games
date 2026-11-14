using System;

namespace Game.Core
{
    public enum MatchEventKind
    {
        PaddleHit,
        WallHit,
        Scored,
        MatchWon,
    }

    /// <summary>1 Tick の間に起きた出来事。演出（音・表示）の起点になる。</summary>
    public readonly struct MatchEvent : IEquatable<MatchEvent>
    {
        public MatchEvent(MatchEventKind kind, Side side)
        {
            Kind = kind;
            Side = side;
        }

        public MatchEventKind Kind { get; }

        /// <summary>打ったパドル・得点した側・勝者。WallHit では意味を持たない。</summary>
        public Side Side { get; }

        public static bool operator ==(MatchEvent a, MatchEvent b) => a.Equals(b);

        public static bool operator !=(MatchEvent a, MatchEvent b) => !a.Equals(b);

        public bool Equals(MatchEvent other) => Kind == other.Kind && Side == other.Side;

        public override bool Equals(object? obj) => obj is MatchEvent other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(Kind, Side);
    }
}
