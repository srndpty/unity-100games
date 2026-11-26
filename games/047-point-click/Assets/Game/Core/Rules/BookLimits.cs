using System;

namespace Game.Core.Rules
{
    /// <summary>
    /// ルール表の上限と、作品として求める数（部屋・謎）。展開・探索の前にこの値で拒否する。
    /// 期待する数が 0 なら照合しない（テスト用の小さな表）。
    /// </summary>
    public sealed class BookLimits
    {
        /// <summary>部屋の高さ（全部屋で共通、画面の縦 1080 に合わせた整数の単位）。</summary>
        public const int RoomHeight = 1080;

        public int MaxLines = 2000;
        public int MaxRooms = 8;
        public int MaxRoomWidth = 4096;
        public int MaxHotspots = 64;
        public int MaxItems = 32;
        public int MaxFlags = 64;

        /// <summary>謎は解いた集合を int のビットで持つので 30 まで。</summary>
        public int MaxPuzzles = 16;

        public int MaxRules = 512;
        public int MaxTopics = 64;
        public int MaxConditions = 8;
        public int MaxEffects = 16;

        /// <summary>1 つの文（台詞・地の文）の上限字数。台詞枠に収まる長さ。</summary>
        public int MaxTextLength = 60;

        /// <summary>同時に持てる持ち物の数（持ち物の枠の数）。</summary>
        public int InventoryCapacity = 8;

        /// <summary>状態空間の探索で数える状態の上限。超えたら拒否する。</summary>
        public int MaxStates = 100000;

        public int ExpectedRooms;
        public int ExpectedPuzzles;

        /// <summary>上限そのものが正しいか（正の値・型の上限内）。</summary>
        public void Validate()
        {
            Positive(MaxLines, nameof(MaxLines));
            Positive(MaxRooms, nameof(MaxRooms));
            Positive(MaxRoomWidth, nameof(MaxRoomWidth));
            Positive(MaxHotspots, nameof(MaxHotspots));
            Positive(MaxItems, nameof(MaxItems));
            Positive(MaxFlags, nameof(MaxFlags));
            Positive(MaxPuzzles, nameof(MaxPuzzles));
            Positive(MaxRules, nameof(MaxRules));
            Positive(MaxTopics, nameof(MaxTopics));
            Positive(MaxConditions, nameof(MaxConditions));
            Positive(MaxEffects, nameof(MaxEffects));
            Positive(MaxTextLength, nameof(MaxTextLength));
            Positive(InventoryCapacity, nameof(InventoryCapacity));
            Positive(MaxStates, nameof(MaxStates));
            if (MaxPuzzles > 30) { throw new ArgumentException("謎は 30 個までです（解いた集合を int のビットで持つ）。"); }
            if (ExpectedRooms < 0 || ExpectedPuzzles < 0) { throw new ArgumentException("期待する数は 0 以上です。"); }
        }

        public BookLimits Copy() => (BookLimits)MemberwiseClone();

        private static void Positive(int value, string name)
        {
            if (value <= 0) { throw new ArgumentException($"{name} は正の値です。"); }
        }
    }
}
