using System;

namespace Game.Core
{
    /// <summary>スート。太陽と炎が赤、月と波が黒。</summary>
    public enum Suit
    {
        Sun,
        Flame,
        Moon,
        Wave,
    }

    /// <summary>
    /// 1 枚の札。ランクは 1（A）〜13（K）。52 枚それぞれに 0〜51 の番号が付く。
    /// 表記は「ランク＋スート」の 2 文字（A, 2〜9, T, J, Q, K と S, F, M, W）。例：TS は太陽の 10。
    /// </summary>
    public readonly struct Card : IEquatable<Card>
    {
        public const int MinRank = 1;
        public const int MaxRank = 13;
        public const int SuitCount = 4;
        public const int DeckSize = SuitCount * MaxRank;

        private const string RankChars = "A23456789TJQK";
        private const string SuitChars = "SFMW";

        private readonly byte _id;

        public Card(Suit suit, int rank)
        {
            if (suit < Suit.Sun || suit > Suit.Wave)
            {
                throw new ArgumentOutOfRangeException(nameof(suit), suit, "未定義のスートです。");
            }

            if (rank < MinRank || rank > MaxRank)
            {
                throw new ArgumentOutOfRangeException(nameof(rank), rank, $"{MinRank}〜{MaxRank} で指定してください。");
            }

            _id = (byte)(((int)suit * MaxRank) + rank - 1);
        }

        /// <summary>0〜51 の番号。スートごとに A から K の順。</summary>
        public int Id => _id;

        public Suit Suit => (Suit)(_id / MaxRank);

        public int Rank => (_id % MaxRank) + 1;

        public bool IsRed => IsRedSuit(Suit);

        public static bool IsRedSuit(Suit suit) => suit == Suit.Sun || suit == Suit.Flame;

        public static Card FromId(int id)
        {
            if (id < 0 || id >= DeckSize)
            {
                throw new ArgumentOutOfRangeException(nameof(id), id, $"0〜{DeckSize - 1} で指定してください。");
            }

            return new Card((Suit)(id / MaxRank), (id % MaxRank) + 1);
        }

        public static bool TryParse(string? text, out Card card)
        {
            card = default;
            if (text == null || text.Length != 2) { return false; }
            var rank = RankChars.IndexOf(text[0], StringComparison.Ordinal);
            var suit = SuitChars.IndexOf(text[1], StringComparison.Ordinal);
            if (rank < 0 || suit < 0) { return false; }
            card = new Card((Suit)suit, rank + 1);
            return true;
        }

        public static Card Parse(string text) =>
            TryParse(text, out var card) ? card : throw new FormatException($"札の表記ではありません：{text}");

        public bool Equals(Card other) => _id == other._id;

        public override bool Equals(object? obj) => obj is Card other && Equals(other);

        public override int GetHashCode() => _id;

        public override string ToString() => $"{RankChars[Rank - 1]}{SuitChars[(int)Suit]}";

        public static bool operator ==(Card left, Card right) => left.Equals(right);

        public static bool operator !=(Card left, Card right) => !left.Equals(right);
    }
}
