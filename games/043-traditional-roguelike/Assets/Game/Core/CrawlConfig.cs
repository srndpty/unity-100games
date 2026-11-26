using System;
using System.Collections.Generic;

namespace Game.Core
{
    /// <summary>敵の 5 種。</summary>
    public enum MonsterKind
    {
        /// <summary>すばしこい小獣：速い（行動の重さ 50）、弱い。</summary>
        Skitter,

        /// <summary>重い岩人形：遅い（重さ 200）、硬くて強い。</summary>
        Golem,

        /// <summary>射手：同じ直線（8 方向）で間に何もなければ離れた所から射つ。</summary>
        Archer,

        /// <summary>盗っ人：隣で持ち物を 1 つ盗んで別の場所へ跳ぶ。倒すと盗んだ品を落とす。</summary>
        Thief,

        /// <summary>眠りの大蛇：眠った状態で出る。自機が隣に来るか傷つくと起きる。強い。</summary>
        Serpent,
    }

    /// <summary>品の 8 種。</summary>
    public enum ItemKind
    {
        HealPotion,
        PoisonPotion,
        SleepPotion,
        Bread,
        BigBread,
        Stone,
        MapScroll,
        WarpScroll,
    }

    /// <summary>敵の 1 種類の値。</summary>
    [Serializable]
    public sealed class MonsterStats
    {
        public MonsterKind Kind;
        public int Hp = 10;
        public int Attack = 3;
        public int Defense;

        /// <summary>1 行動の重さ（時間）。自機は 100。</summary>
        public int ActionCost = 100;

        public int Exp = 5;

        /// <summary>射手の射程（マス）と矢の威力。射手以外は 0。</summary>
        public int Range;
        public int ArrowDamage;

        /// <summary>階ごと（1〜5 階）の出現の重み。</summary>
        public int[] FloorWeights = new int[CrawlConfig.FloorCount];

        public MonsterStats Copy()
        {
            var copy = (MonsterStats)MemberwiseClone();
            copy.FloorWeights = (int[])FloorWeights.Clone();
            return copy;
        }
    }

    /// <summary>品の 1 種類の値。効果の量は <see cref="CrawlConfig"/> にまとめる。</summary>
    [Serializable]
    public sealed class ItemStats
    {
        public ItemKind Kind;

        /// <summary>階ごと（1〜5 階）の出現の重み。</summary>
        public int[] FloorWeights = new int[CrawlConfig.FloorCount];

        public ItemStats Copy()
        {
            var copy = (ItemStats)MemberwiseClone();
            copy.FloorWeights = (int[])FloorWeights.Clone();
            return copy;
        }
    }

    /// <summary>
    /// 試合の設定。Unity の ScriptableObject からそのまま渡すので公開フィールドで書く。<see cref="Validate"/> を通ったものだけを使う。
    /// 盤の大きさ・視界・自機・満腹度・敵と品の表をまとめる。
    /// </summary>
    [Serializable]
    public sealed class CrawlConfig
    {
        public const int FloorCount = 5;
        public const int MonsterKindCount = 5;
        public const int ItemKindCount = 8;

        /// <summary>行動の重さの基準（自機の 1 行動）。</summary>
        public const int PlayerActionCost = 100;

        public int Width = 48;
        public int Height = 27;

        /// <summary>部屋を置く区画の数（3〜<see cref="RoomCellCount"/>）。残りの区画は通路の交点。</summary>
        public int MinRooms = 6;
        public int MaxRooms = 9;

        /// <summary>全域木に足す余分な通路の数。</summary>
        public int MinExtraCorridors = 1;
        public int MaxExtraCorridors = 2;

        /// <summary>生成の検証に失敗したときの引き直しの上限。</summary>
        public int GenerationAttempts = 20;

        public int ViewRadius = 8;

        public int PlayerHp = 24;
        public int PlayerAttack = 5;
        public int PlayerDefense = 1;

        /// <summary>レベルが上がるのに要る経験値の累計（2 レベル目から）。長さ + 1 が最高レベル。</summary>
        public int[] LevelExp = { 8, 20, 40, 70, 110, 160, 230, 320, 430 };

        public int LevelHp = 5;
        public int LevelAttack = 1;

        /// <summary>何レベルごとに防御が 1 上がるか。</summary>
        public int LevelDefenseEvery = 2;

        /// <summary>何行動ごとに HP が 1 回復するか（空腹でないとき）。</summary>
        public int RegenEvery = 6;

        public int MaxSatiety = 100;

        /// <summary>何行動ごとに満腹度が 1 減るか。</summary>
        public int HungerEvery = 10;

        /// <summary>満腹度 0 のとき 1 行動ごとに減る HP。</summary>
        public int StarveDamage = 1;

        /// <summary>この満腹度になったら案内を出す。</summary>
        public int HungryWarning = 20;

        public int HitPercent = 90;

        /// <summary>ダメージの倍率の幅（%）。</summary>
        public int DamageMinPercent = 87;
        public int DamageMaxPercent = 112;

        public int InventoryCapacity = 16;

        /// <summary>1 階に置ける品の上限（落とした品・投げて外れた品を含む）。</summary>
        public int FloorItemCap = 48;

        public int ThrowRange = 10;

        /// <summary>1 階に出る敵の数 = Base + 階 × PerFloor。</summary>
        public int MonstersBase = 4;
        public int MonstersPerFloor = 1;

        /// <summary>1 階に置く品の数 = Base + 階 × PerFloor。</summary>
        public int ItemsBase = 5;
        public int ItemsPerFloor = 1;

        public int HealAmount = 30;
        public int PoisonSelfDamage = 6;
        public int PoisonThrowDamage = 15;
        public int StoneDamage = 7;

        /// <summary>品を投げて当てたときの威力（石・毒薬・眠り薬・回復薬以外）。</summary>
        public int ThrowBumpDamage = 1;

        /// <summary>眠り薬で眠る行動数（自分で飲んだとき・敵に投げたとき）。</summary>
        public int SleepTurns = 6;

        public int BreadSatiety = 50;
        public int BigBreadSatiety = 100;

        public MonsterStats[] Monsters = DefaultMonsters();

        public ItemStats[] Items = DefaultItems();

        public const int RoomCellCount = 9;

        public MonsterStats StatsOf(MonsterKind kind) => Monsters[(int)kind];

        public int MaxLevel => LevelExp.Length + 1;

        public CrawlConfig Copy()
        {
            var copy = (CrawlConfig)MemberwiseClone();
            copy.LevelExp = (int[])LevelExp.Clone();
            copy.Monsters = Array.ConvertAll(Monsters, m => m.Copy());
            copy.Items = Array.ConvertAll(Items, i => i.Copy());
            return copy;
        }

        /// <summary>設定の誤りを理由つきで返す。空なら使える。</summary>
        public IReadOnlyList<string> Problems()
        {
            var problems = new List<string>();
            void Need(bool ok, string message)
            {
                if (!ok) { problems.Add(message); }
            }

            Need(Width >= 30 && Width <= 120, "Width は 30〜120");
            Need(Height >= 21 && Height <= 60, "Height は 21〜60");
            Need(MinRooms >= 3 && MinRooms <= MaxRooms && MaxRooms <= RoomCellCount, "部屋の数は 3 ≤ MinRooms ≤ MaxRooms ≤ 9");
            Need(MinExtraCorridors >= 0 && MinExtraCorridors <= MaxExtraCorridors && MaxExtraCorridors <= 4, "余分な通路は 0 ≤ Min ≤ Max ≤ 4");
            Need(GenerationAttempts >= 1 && GenerationAttempts <= 1000, "GenerationAttempts は 1〜1000");
            Need(ViewRadius >= 1 && ViewRadius <= 30, "ViewRadius は 1〜30");
            Need(PlayerHp >= 1 && PlayerHp <= 999, "PlayerHp は 1〜999");
            Need(PlayerAttack >= 1 && PlayerAttack <= 999, "PlayerAttack は 1〜999");
            Need(PlayerDefense >= 0 && PlayerDefense <= 999, "PlayerDefense は 0〜999");
            Need(LevelExp != null && LevelExp.Length >= 1 && LevelExp.Length <= 98, "LevelExp は 1〜98 個");
            if (LevelExp != null)
            {
                for (int i = 0; i < LevelExp.Length; i++)
                {
                    Need(LevelExp[i] >= 1 && LevelExp[i] <= 1_000_000 && (i == 0 || LevelExp[i] > LevelExp[i - 1]), $"LevelExp[{i}] は 1〜1000000 で増え続ける");
                }
            }

            Need(LevelHp >= 0 && LevelHp <= 99, "LevelHp は 0〜99");
            Need(LevelAttack >= 0 && LevelAttack <= 99, "LevelAttack は 0〜99");
            Need(LevelDefenseEvery >= 1 && LevelDefenseEvery <= 99, "LevelDefenseEvery は 1〜99");
            Need(RegenEvery >= 1 && RegenEvery <= 1000, "RegenEvery は 1〜1000");
            Need(MaxSatiety >= 1 && MaxSatiety <= 10_000, "MaxSatiety は 1〜10000");
            Need(HungerEvery >= 1 && HungerEvery <= 1000, "HungerEvery は 1〜1000");
            Need(StarveDamage >= 1 && StarveDamage <= 999, "StarveDamage は 1〜999");
            Need(HungryWarning >= 0 && HungryWarning < MaxSatiety, "HungryWarning は 0 以上 MaxSatiety 未満");
            Need(HitPercent >= 1 && HitPercent <= 100, "HitPercent は 1〜100");
            Need(DamageMinPercent >= 1 && DamageMinPercent <= DamageMaxPercent && DamageMaxPercent <= 1000, "ダメージの倍率は 1 ≤ Min ≤ Max ≤ 1000");
            Need(InventoryCapacity >= 1 && InventoryCapacity <= 26, "InventoryCapacity は 1〜26");
            Need(FloorItemCap >= 1 && FloorItemCap <= 500, "FloorItemCap は 1〜500");
            Need(ThrowRange >= 1 && ThrowRange <= 30, "ThrowRange は 1〜30");
            // 階ごとの数は実行時に「基本 + 1 階あたり × 階」で求めるので、桁あふれしない long で最も深い階の数を検証する（041）。
            long maxMonsters = MonstersBase + ((long)MonstersPerFloor * FloorCount);
            long maxItems = ItemsBase + ((long)ItemsPerFloor * FloorCount);
            Need(MonstersBase >= 0 && MonstersPerFloor >= 0 && maxMonsters <= 40, "敵の数は 0〜40");
            Need(ItemsBase >= 0 && ItemsPerFloor >= 0 && maxItems <= 40, "品の数は 0〜40");
            Need(maxItems <= FloorItemCap, "置く品の数は FloorItemCap 以下");
            Need(HealAmount >= 1 && HealAmount <= 999, "HealAmount は 1〜999");
            Need(PoisonSelfDamage >= 0 && PoisonSelfDamage <= 999, "PoisonSelfDamage は 0〜999");
            Need(PoisonThrowDamage >= 1 && PoisonThrowDamage <= 999, "PoisonThrowDamage は 1〜999");
            Need(StoneDamage >= 1 && StoneDamage <= 999, "StoneDamage は 1〜999");
            Need(ThrowBumpDamage >= 1 && ThrowBumpDamage <= 999, "ThrowBumpDamage は 1〜999");
            Need(SleepTurns >= 1 && SleepTurns <= 99, "SleepTurns は 1〜99");
            Need(BreadSatiety >= 1 && BigBreadSatiety >= 1 && BreadSatiety <= MaxSatiety && BigBreadSatiety <= MaxSatiety, "パンの満腹度は 1〜MaxSatiety");

            CheckMonsters(problems);
            CheckItems(problems);
            return problems;
        }

        public void Validate()
        {
            var problems = Problems();
            if (problems.Count > 0) { throw new ArgumentException("設定が正しくありません：" + string.Join("、", problems)); }
        }

        private void CheckMonsters(List<string> problems)
        {
            if (Monsters == null || Monsters.Length != MonsterKindCount)
            {
                problems.Add("Monsters は 5 種");
                return;
            }

            var floorTotals = new long[FloorCount];
            for (int i = 0; i < Monsters.Length; i++)
            {
                var m = Monsters[i];
                var name = $"Monsters[{i}]";
                if (m == null)
                {
                    problems.Add(name + " がありません");
                    continue;
                }

                if (m.Kind != (MonsterKind)i) { problems.Add($"{name} の Kind は {(MonsterKind)i}"); }
                if (m.Hp < 1 || m.Hp > 9999) { problems.Add(name + " の Hp は 1〜9999"); }
                if (m.Attack < 0 || m.Attack > 999) { problems.Add(name + " の Attack は 0〜999"); }
                if (m.Defense < 0 || m.Defense > 999) { problems.Add(name + " の Defense は 0〜999"); }
                if (m.ActionCost < 25 || m.ActionCost > 800) { problems.Add(name + " の ActionCost は 25〜800"); }
                if (m.Exp < 0 || m.Exp > 100_000) { problems.Add(name + " の Exp は 0〜100000"); }
                var archer = m.Kind == MonsterKind.Archer;
                if (archer && (m.Range < 2 || m.Range > 20 || m.ArrowDamage < 1 || m.ArrowDamage > 999))
                {
                    problems.Add(name + " の射程は 2〜20、矢の威力は 1〜999");
                }

                if (!archer && (m.Range != 0 || m.ArrowDamage != 0)) { problems.Add(name + " は射手でないので Range・ArrowDamage は 0"); }
                AddWeights(problems, name, m.FloorWeights, floorTotals);
            }

            CheckTotals(problems, "敵", floorTotals);
        }

        private void CheckItems(List<string> problems)
        {
            if (Items == null || Items.Length != ItemKindCount)
            {
                problems.Add("Items は 8 種");
                return;
            }

            var floorTotals = new long[FloorCount];
            for (int i = 0; i < Items.Length; i++)
            {
                var item = Items[i];
                var name = $"Items[{i}]";
                if (item == null)
                {
                    problems.Add(name + " がありません");
                    continue;
                }

                if (item.Kind != (ItemKind)i) { problems.Add($"{name} の Kind は {(ItemKind)i}"); }
                AddWeights(problems, name, item.FloorWeights, floorTotals);
            }

            CheckTotals(problems, "品", floorTotals);
        }

        private static void AddWeights(List<string> problems, string name, int[]? weights, long[] totals)
        {
            if (weights == null || weights.Length != FloorCount)
            {
                problems.Add(name + " の FloorWeights は 5 個");
                return;
            }

            for (int f = 0; f < FloorCount; f++)
            {
                if (weights[f] < 0) { problems.Add($"{name} の FloorWeights[{f}] は 0 以上"); }
                totals[f] += Math.Max(0, weights[f]);
            }
        }

        private static void CheckTotals(List<string> problems, string what, long[] totals)
        {
            for (int f = 0; f < FloorCount; f++)
            {
                if (totals[f] <= 0 || totals[f] > int.MaxValue) { problems.Add($"{f + 1} 階の{what}の重みの合計は 1〜{int.MaxValue}"); }
            }
        }

        private static MonsterStats[] DefaultMonsters() => new[]
        {
            new MonsterStats { Kind = MonsterKind.Skitter, Hp = 7, Attack = 3, Defense = 0, ActionCost = 50, Exp = 3, FloorWeights = new[] { 6, 4, 3, 2, 1 } },
            new MonsterStats { Kind = MonsterKind.Golem, Hp = 30, Attack = 9, Defense = 3, ActionCost = 200, Exp = 14, FloorWeights = new[] { 0, 1, 2, 3, 3 } },
            new MonsterStats { Kind = MonsterKind.Archer, Hp = 12, Attack = 3, Defense = 1, ActionCost = 100, Exp = 8, Range = 6, ArrowDamage = 4, FloorWeights = new[] { 2, 3, 3, 3, 2 } },
            new MonsterStats { Kind = MonsterKind.Thief, Hp = 14, Attack = 2, Defense = 1, ActionCost = 100, Exp = 9, FloorWeights = new[] { 2, 2, 2, 1, 1 } },
            new MonsterStats { Kind = MonsterKind.Serpent, Hp = 40, Attack = 11, Defense = 3, ActionCost = 100, Exp = 24, FloorWeights = new[] { 0, 0, 1, 2, 3 } },
        };

        private static ItemStats[] DefaultItems() => new[]
        {
            new ItemStats { Kind = ItemKind.HealPotion, FloorWeights = new[] { 5, 5, 5, 5, 5 } },
            new ItemStats { Kind = ItemKind.PoisonPotion, FloorWeights = new[] { 2, 2, 2, 2, 2 } },
            new ItemStats { Kind = ItemKind.SleepPotion, FloorWeights = new[] { 2, 2, 2, 2, 2 } },
            new ItemStats { Kind = ItemKind.Bread, FloorWeights = new[] { 4, 4, 4, 4, 4 } },
            new ItemStats { Kind = ItemKind.BigBread, FloorWeights = new[] { 1, 1, 1, 1, 1 } },
            new ItemStats { Kind = ItemKind.Stone, FloorWeights = new[] { 5, 5, 4, 4, 4 } },
            new ItemStats { Kind = ItemKind.MapScroll, FloorWeights = new[] { 2, 2, 2, 2, 2 } },
            new ItemStats { Kind = ItemKind.WarpScroll, FloorWeights = new[] { 2, 2, 2, 2, 2 } },
        };
    }
}
