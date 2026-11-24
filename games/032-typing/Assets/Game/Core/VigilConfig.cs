using System;

namespace Game.Core
{
    public enum Difficulty
    {
        Easy,
        Normal,
        Hard,
    }

    /// <summary>難度 1 段の設定。時間は全て Tick（60 Tick/秒）。</summary>
    [Serializable]
    public sealed class LevelConfig
    {
        /// <summary>出題する語の段の範囲。</summary>
        public int MinTier = 1;

        public int MaxTier = 1;

        /// <summary>同時に出る敵の上限。</summary>
        public int MaxEnemies = 2;

        /// <summary>出現から手前の線に着くまでの Tick。</summary>
        public int ApproachTicks = 540;

        /// <summary>クリアに必要な撃破数。</summary>
        public int Goal = 10;

        /// <summary>試合開始から最初の出現までの Tick。</summary>
        public int FirstSpawnTicks = 30;

        /// <summary>出現の間隔（両端を含む範囲から乱数で選ぶ）。</summary>
        public int SpawnMinTicks = 150;

        public int SpawnMaxTicks = 210;

        public LevelConfig Copy() => (LevelConfig)MemberwiseClone();

        /// <summary>範囲を検査する。<paramref name="lanes"/> は敵の並ぶ列の数。</summary>
        public void Validate(string name, int lanes)
        {
            Check(MinTier >= 1 && MinTier <= WordBook.MaxTier, name, nameof(MinTier), $"1〜{WordBook.MaxTier}");
            Check(MaxTier >= MinTier && MaxTier <= WordBook.MaxTier, name, nameof(MaxTier), $"{nameof(MinTier)}〜{WordBook.MaxTier}");
            Check(MaxEnemies >= 1 && MaxEnemies <= lanes, name, nameof(MaxEnemies), $"1〜列の数 {lanes}");
            Check(ApproachTicks >= 60 && ApproachTicks <= 36000, name, nameof(ApproachTicks), "60〜36000");
            Check(Goal >= 1 && Goal <= 999, name, nameof(Goal), "1〜999");
            Check(FirstSpawnTicks >= 0 && FirstSpawnTicks <= 3600, name, nameof(FirstSpawnTicks), "0〜3600");
            Check(SpawnMinTicks >= 1 && SpawnMinTicks <= 3600, name, nameof(SpawnMinTicks), "1〜3600");
            Check(SpawnMaxTicks >= SpawnMinTicks && SpawnMaxTicks <= 3600, name, nameof(SpawnMaxTicks), $"{nameof(SpawnMinTicks)}〜3600");
        }

        private static void Check(bool ok, string level, string field, string range)
        {
            if (!ok) { throw new ArgumentOutOfRangeException(field, $"{level}.{field} は {range} の範囲で指定してください。"); }
        }
    }

    /// <summary>試合の設定。Unity のシリアライズのため公開フィールドで持つ。</summary>
    [Serializable]
    public sealed class VigilConfig
    {
        /// <summary>体力。敵が手前の線に着くたびに 1 減る。</summary>
        public int Hp = 3;

        /// <summary>敵の並ぶ列の数（画面の横の位置）。</summary>
        public int Lanes = 5;

        public LevelConfig Easy = new()
        {
            MinTier = 1,
            MaxTier = 1,
            MaxEnemies = 2,
            ApproachTicks = 600,
            Goal = 10,
            FirstSpawnTicks = 30,
            SpawnMinTicks = 150,
            SpawnMaxTicks = 210,
        };

        public LevelConfig Normal = new()
        {
            MinTier = 1,
            MaxTier = 2,
            MaxEnemies = 3,
            ApproachTicks = 540,
            Goal = 15,
            FirstSpawnTicks = 30,
            SpawnMinTicks = 120,
            SpawnMaxTicks = 180,
        };

        public LevelConfig Hard = new()
        {
            MinTier = 2,
            MaxTier = 3,
            MaxEnemies = 4,
            ApproachTicks = 600,
            Goal = 20,
            FirstSpawnTicks = 30,
            SpawnMinTicks = 120,
            SpawnMaxTicks = 180,
        };

        public LevelConfig Level(Difficulty difficulty) => difficulty switch
        {
            Difficulty.Easy => Easy,
            Difficulty.Normal => Normal,
            Difficulty.Hard => Hard,
            _ => throw new ArgumentOutOfRangeException(nameof(difficulty), difficulty, "未定義の難度です。"),
        };

        /// <summary>独立した複製（難度の設定も複製する）。</summary>
        public VigilConfig Copy()
        {
            var copy = (VigilConfig)MemberwiseClone();
            copy.Easy = Easy.Copy();
            copy.Normal = Normal.Copy();
            copy.Hard = Hard.Copy();
            return copy;
        }

        /// <summary>範囲を検査する。<paramref name="book"/> を渡すと、全ての難度の段に同時出現数以上の語があるかも確かめる。</summary>
        public void Validate(WordBook? book = null)
        {
            Range(Hp, 1, 9, nameof(Hp));
            Range(Lanes, 1, 8, nameof(Lanes));
            foreach (var difficulty in new[] { Difficulty.Easy, Difficulty.Normal, Difficulty.Hard })
            {
                var level = Level(difficulty) ?? throw new ArgumentNullException(difficulty.ToString(), $"{difficulty} の設定がありません。");
                level.Validate(difficulty.ToString(), Lanes);
                if (book != null) { CheckWords(book, difficulty); }
            }
        }

        /// <summary>難度 <paramref name="difficulty"/> の段に、同時出現数以上の語があるか（同じ語を同時に出さないため）。</summary>
        public void CheckWords(WordBook book, Difficulty difficulty)
        {
            if (book == null) { throw new ArgumentNullException(nameof(book)); }
            var level = Level(difficulty);
            var words = book.CountInTiers(level.MinTier, level.MaxTier);
            if (words < level.MaxEnemies)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(book),
                    words,
                    $"{difficulty} の段 {level.MinTier}〜{level.MaxTier} の語（{words}）が同時出現数 {level.MaxEnemies} より少ないです。");
            }
        }

        private static void Range(int value, int min, int max, string name)
        {
            if (value < min || value > max) { throw new ArgumentOutOfRangeException(name, value, $"{name} は {min}〜{max} の範囲で指定してください。"); }
        }
    }
}
