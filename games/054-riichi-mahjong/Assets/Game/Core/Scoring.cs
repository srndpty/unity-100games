using System;
using System.Collections.Generic;
using System.Linq;

namespace Game.Core
{
    public enum MeldKind { Chi, Pon, OpenKan, ClosedKan, AddedKan }

    public readonly struct Group
    {
        public Group(int tile, MeldKind kind) => (Tile, Kind) = (tile, kind);
        public int Tile { get; }
        public MeldKind Kind { get; }
        public bool Sequence => Kind == MeldKind.Chi;
        public bool Kan => Kind >= MeldKind.OpenKan;
        public bool Open => Kind != MeldKind.ClosedKan;
    }

    [Flags]
    public enum WinContext
    {
        None = 0, Tsumo = 1, Riichi = 2, DoubleRiichi = 4, Ippatsu = 8,
        LastDraw = 16, LastDiscard = 32, Rinshan = 64, RobbingKan = 128,
    }

    public sealed class HandScore
    {
        internal HandScore(int fu, int han, int yakuman, IReadOnlyList<string> yaku, IReadOnlyList<string> fuItems)
            => (Fu, Han, Yakuman, Yaku, FuItems) = (fu, han, yakuman, yaku, fuItems);
        public int Fu { get; }
        public int Han { get; }
        public int Yakuman { get; }
        public IReadOnlyList<string> Yaku { get; }
        public IReadOnlyList<string> FuItems { get; }

        public int Basic => Yakuman > 0 ? 8000 * Yakuman : Han switch
        {
            >= 13 => 8000,
            >= 11 => 6000,
            >= 8 => 4000,
            >= 6 => 3000,
            >= 5 => 2000,
            _ => Math.Min(2000, Fu << (Han + 2)),
        };

        public int Ron(bool dealer) => RoundPayment(Basic * (dealer ? 6 : 4));
        public int Tsumo(bool dealer, bool payerIsDealer) =>
            RoundPayment(Basic * (dealer || payerIsDealer ? 2 : 1));

        private static int RoundPayment(int value) => ((value + 99) / 100) * 100;
    }

    public static class Tiles
    {
        public const int Types = 34;
        public const int Total = 136;
        public static int Type(int id) => id / 4;
        public static bool Honor(int tile) => tile >= 27;
        public static bool Outside(int tile) => Honor(tile) || tile % 9 is 0 or 8;
        public static bool CanSequence(int tile) => tile < 27 && tile % 9 <= 6;
        public static int Dora(int indicator) => indicator switch
        {
            < 27 => indicator % 9 == 8 ? indicator - 8 : indicator + 1,
            < 31 => indicator == 30 ? 27 : indicator + 1,
            _ => indicator == 33 ? 31 : indicator + 1,
        };

        public static int[] Counts(IEnumerable<int> types)
        {
            var counts = new int[Types];
            foreach (var tile in types) { counts[tile]++; }
            return counts;
        }
    }

    public static class Scoring
    {
        private static readonly int[] _orphans = { 0, 8, 9, 17, 18, 26, 27, 28, 29, 30, 31, 32, 33 };

        public static IReadOnlyList<int> Waits(IEnumerable<int> hand, IReadOnlyList<Group> melds)
        {
            var counts = Tiles.Counts(hand);
            if (counts.Sum() != 13 - 3 * melds.Count) { return Array.Empty<int>(); }

            var held = (int[])counts.Clone();
            foreach (var meld in melds)
            {
                if (meld.Sequence)
                {
                    for (var i = 0; i < 3; i++) { held[meld.Tile + i]++; }
                }
                else { held[meld.Tile] += meld.Kan ? 4 : 3; }
            }

            var waits = new List<int>();
            for (var tile = 0; tile < Tiles.Types; tile++)
            {
                if (held[tile] == 4) { continue; }
                counts[tile]++;
                if (IsComplete(counts, melds.Count)) { waits.Add(tile); }

                counts[tile]--;
            }

            return waits.AsReadOnly();
        }

        public static bool CanLockKan(IEnumerable<int> before, int tile, IReadOnlyList<Group> melds)
        {
            var hand = before.ToList();
            if (hand.Count(t => t == tile) != 3) { return false; }
            var after = hand.Where(t => t != tile);
            var expanded = melds.Concat(new[] { new Group(tile, MeldKind.ClosedKan) }).ToArray();
            var waits = Waits(hand, melds);
            if (waits.Count == 0 || !waits.SequenceEqual(Waits(after, expanded))) { return false; }

            foreach (var wait in waits)
            {
                var beforeShapes = Shapes(Tiles.Counts(hand.Append(wait)), melds.Count).ToArray();
                if (beforeShapes.Any(s => !s.Groups.Any(g => !g.Sequence && g.Tile == tile))) { return false; }

                var expected = beforeShapes.Select(s => Signature(s.Pair, s.Groups.Where(g => g.Sequence || g.Tile != tile)));
                var actual = Shapes(Tiles.Counts(after.Append(wait)), expanded.Length)
                    .Select(s => Signature(s.Pair, s.Groups));
                if (!new HashSet<string>(expected).SetEquals(actual)) { return false; }
            }

            return true;
        }

        public static HandScore? Evaluate(IEnumerable<int> hand, IReadOnlyList<Group> melds, int winning,
            int seatWind, WinContext flags, IEnumerable<int> indicators)
        {
            var counts = Tiles.Counts(hand);
            var complete = (int[])counts.Clone();
            foreach (var meld in melds)
            {
                if (meld.Sequence)
                {
                    for (var i = 0; i < 3; i++) { complete[meld.Tile + i]++; }
                }
                else { complete[meld.Tile] += meld.Kan ? 4 : 3; }
            }

            if (complete.Any(n => n > 4)) { return null; }

            var dora = indicators.Sum(i => complete[Tiles.Dora(i)]);
            var present = Enumerable.Range(0, Tiles.Types).Where(t => complete[t] > 0).ToArray();
            var tsumo = flags.HasFlag(WinContext.Tsumo);
            var dealer = seatWind == 27;
            HandScore? best = null;
            var bestAmount = 0;
            foreach (var shape in Shapes(counts, melds.Count))
            {
                for (var i = -1; i < shape.Groups.Length; i++)
                {
                    if (i == -1 && shape.Pair >= 0 && shape.Pair != winning) { continue; }
                    if (i >= 0 && !Contains(shape.Groups[i], winning)) { continue; }

                    var score = ScoreShape(shape, melds, i, winning, seatWind, flags, present, dora);
                    if (score == null) { continue; }

                    var amount = tsumo ? score.Tsumo(dealer, true) + 2 * score.Tsumo(dealer, false) : score.Ron(dealer);
                    if (amount > bestAmount || (amount == bestAmount && best != null && score.Han > best.Han))
                    {
                        best = score;
                        bestAmount = amount;
                    }
                }
            }

            return best;
        }

        private static HandScore? ScoreShape(Shape shape, IReadOnlyList<Group> melds, int assignment, int winning,
            int wind, WinContext flags, int[] present, int dora)
        {
            var tsumo = flags.HasFlag(WinContext.Tsumo);
            var closed = melds.All(g => !g.Open);
            var groups = shape.Groups.Concat(melds).ToArray();
            var triplets = groups.Where(g => !g.Sequence).ToArray();
            var sequences = groups.Where(g => g.Sequence).ToArray();
            var concealed = shape.Groups.Where((g, i) => !g.Sequence && (tsumo || i != assignment)).Count()
                + melds.Count(g => g.Kind == MeldKind.ClosedKan);
            var dragon = triplets.Count(g => g.Tile >= 31);
            var kan = melds.Count(g => g.Kan);
            var yaku = new List<string>();
            var yakuman = 0;
            if (shape.Pair == -2) { yaku.Add("国士無双"); yakuman++; }
            if (concealed == 4) { yaku.Add("四暗刻"); yakuman++; }
            if (dragon == 3) { yaku.Add("大三元"); yakuman++; }
            if (yakuman > 0) { return new HandScore(0, 0, yakuman, yaku.AsReadOnly(), Array.Empty<string>()); }

            var han = 0;
            void Add(bool condition, int value, string name)
            {
                if (!condition) { return; }

                han += value;
                yaku.Add(name);
            }

            Add(flags.HasFlag(WinContext.DoubleRiichi), 2, "ダブルリーチ");
            Add(flags.HasFlag(WinContext.Riichi) && !flags.HasFlag(WinContext.DoubleRiichi), 1, "リーチ");
            Add(flags.HasFlag(WinContext.Ippatsu), 1, "一発");
            Add(tsumo && closed, 1, "門前ツモ");
            Add(flags.HasFlag(WinContext.LastDraw), 1, "海底摸月");
            Add(flags.HasFlag(WinContext.LastDiscard), 1, "河底撈魚");
            Add(flags.HasFlag(WinContext.Rinshan), 1, "嶺上開花");
            Add(flags.HasFlag(WinContext.RobbingKan), 1, "槍槓");
            Add(present.All(t => !Tiles.Outside(t)), 1, "断么九");
            var suits = present.Where(t => !Tiles.Honor(t)).Select(t => t / 9).Distinct().Count();
            var honors = present.Any(Tiles.Honor);
            Add(suits == 1 && honors, closed ? 3 : 2, "混一色");
            Add(suits == 1 && !honors, closed ? 6 : 5, "清一色");
            Add(present.All(Tiles.Outside), 2, "混老頭");
            var seven = shape.Pair == -1;
            Add(seven, 2, "七対子");
            var pairFu = (shape.Pair == wind ? 2 : 0) + (shape.Pair == 27 ? 2 : 0) + (shape.Pair >= 31 ? 2 : 0);
            var waitFu = 0;
            if (!seven)
            {
                waitFu = assignment == -1 || TightWait(shape.Groups[assignment], winning) ? 2 : 0;
            }

            var pinfu = closed && sequences.Length == 4 && pairFu == 0 && waitFu == 0;
            if (!seven)
            {
                Add(pinfu, 1, "平和");
                var doubles = sequences.GroupBy(g => g.Tile).Sum(g => g.Count() / 2);
                Add(closed && doubles == 1, 1, "一盃口");
                Add(closed && doubles == 2, 3, "二盃口");
                Add(triplets.Any(g => g.Tile == 27), 1, "場風 東");
                Add(triplets.Any(g => g.Tile == wind), 1, "自風");
                foreach (var g in triplets.Where(g => g.Tile >= 31)) { Add(true, 1, "役牌 " + "白發中"[g.Tile - 31]); }

                Add(triplets.Length == 4, 2, "対々和");
                Add(concealed == 3, 2, "三暗刻");
                Add(kan == 3, 2, "三槓子");
                Add(dragon == 2 && shape.Pair >= 31, 2, "小三元");
                Add(ThreeSuits(sequences), closed ? 2 : 1, "三色同順");
                Add(ThreeSuits(triplets), 2, "三色同刻");
                Add(sequences.Any(g => g.Tile % 9 == 0 &&
                    sequences.Any(b => b.Tile == g.Tile + 3) && sequences.Any(b => b.Tile == g.Tile + 6)),
                    closed ? 2 : 1, "一気通貫");
                var outside = sequences.Length > 0 && Tiles.Outside(shape.Pair)
                    && triplets.All(g => Tiles.Outside(g.Tile)) && sequences.All(g => g.Tile % 9 is 0 or 6);
                Add(outside && honors, closed ? 2 : 1, "混全帯么九");
                Add(outside && !honors, closed ? 3 : 2, "純全帯么九");
            }

            if (han == 0) { return null; }

            Add(dora > 0, dora, "ドラ " + dora);
            var fuItems = new List<string>();
            var fu = 0;
            void Fu(int value, string name)
            {
                if (value == 0) { return; }

                fu += value;
                fuItems.Add(name + " " + value + "符");
            }

            if (seven) { Fu(25, "七対子"); }
            else
            {
                Fu(20, "副底");
                Fu(waitFu, "待ち");
                Fu(pairFu, "雀頭");
                Fu(tsumo && !pinfu ? 2 : 0, "ツモ");
                Fu(closed && !tsumo ? 10 : 0, "門前ロン");
                for (var i = 0; i < groups.Length; i++)
                {
                    var g = groups[i];
                    if (g.Sequence) { continue; }

                    var open = i < shape.Groups.Length ? !tsumo && i == assignment : g.Open;
                    Fu((g.Kan ? 8 : 2) * (open ? 1 : 2) * (Tiles.Outside(g.Tile) ? 2 : 1), g.Kan ? "槓子" : "刻子");
                }

                if (!closed && fu == 20) { Fu(10, "喰い平和形"); }

                fu = ((fu + 9) / 10) * 10;
            }

            return new HandScore(fu, han, 0, yaku.AsReadOnly(), fuItems.AsReadOnly());
        }

        private static bool ThreeSuits(IEnumerable<Group> groups)
        {
            return groups.Where(g => !Tiles.Honor(g.Tile)).GroupBy(g => g.Tile % 9)
                .Any(g => g.Select(m => m.Tile / 9).Distinct().Count() == 3);
        }

        private static bool TightWait(Group g, int tile) => g.Sequence &&
            (tile == g.Tile + 1 || (g.Tile % 9 == 0 && tile == g.Tile + 2) || (g.Tile % 9 == 6 && tile == g.Tile));

        private static bool Contains(Group g, int tile) => g.Sequence ? tile >= g.Tile && tile <= g.Tile + 2 : tile == g.Tile;

        private static string Signature(int pair, IEnumerable<Group> groups) =>
            pair + ":" + string.Join(",", groups.Select(g => g.Tile * 2 + (g.Sequence ? 1 : 0)).OrderBy(n => n));

        private static bool IsComplete(int[] counts, int melds)
        {
            if (melds == 0 && (counts.Count(n => n == 2) == 7 ||
                (_orphans.All(t => counts[t] > 0) && _orphans.Sum(t => counts[t]) == 14)))
            {
                return true;
            }

            for (var pair = 0; pair < counts.Length; pair++)
            {
                if (counts[pair] < 2) { continue; }

                counts[pair] -= 2;
                var found = HasGroups(counts, 4 - melds);
                counts[pair] += 2;
                if (found) { return true; }
            }

            return false;
        }

        private static bool HasGroups(int[] counts, int remaining)
        {
            if (remaining == 0) { return true; }

            var tile = Array.FindIndex(counts, n => n > 0);
            if (counts[tile] >= 3)
            {
                counts[tile] -= 3;
                var found = HasGroups(counts, remaining - 1);
                counts[tile] += 3;
                if (found) { return true; }
            }

            if (!Tiles.CanSequence(tile) || counts[tile + 1] == 0 || counts[tile + 2] == 0) { return false; }

            for (var i = 0; i < 3; i++) { counts[tile + i]--; }

            var sequence = HasGroups(counts, remaining - 1);
            for (var i = 0; i < 3; i++) { counts[tile + i]++; }

            return sequence;
        }

        private static IEnumerable<Shape> Shapes(int[] input, int meldCount)
        {
            var counts = (int[])input.Clone();
            if (counts.Sum() != 14 - 3 * meldCount || counts.Any(n => n > 4)) { yield break; }

            if (meldCount == 0)
            {
                if (counts.Count(n => n == 2) == 7) { yield return new Shape(-1, Array.Empty<Group>()); }
                if (_orphans.All(t => counts[t] > 0) && _orphans.Sum(t => counts[t]) == 14)
                {
                    yield return new Shape(-2, Array.Empty<Group>());
                }
            }

            for (var pair = 0; pair < counts.Length; pair++)
            {
                if (counts[pair] < 2) { continue; }

                counts[pair] -= 2;
                var groups = new Group[4 - meldCount];
                foreach (var shape in Divide(counts, groups, 0, pair)) { yield return shape; }

                counts[pair] += 2;
            }
        }

        private static IEnumerable<Shape> Divide(int[] counts, Group[] groups, int index, int pair)
        {
            if (index == groups.Length)
            {
                yield return new Shape(pair, (Group[])groups.Clone());
                yield break;
            }

            var tile = Array.FindIndex(counts, n => n > 0);
            if (counts[tile] >= 3)
            {
                counts[tile] -= 3;
                groups[index] = new Group(tile, MeldKind.Pon);
                foreach (var shape in Divide(counts, groups, index + 1, pair)) { yield return shape; }

                counts[tile] += 3;
            }

            if (Tiles.CanSequence(tile) && counts[tile + 1] > 0 && counts[tile + 2] > 0)
            {
                for (var i = 0; i < 3; i++) { counts[tile + i]--; }

                groups[index] = new Group(tile, MeldKind.Chi);
                foreach (var shape in Divide(counts, groups, index + 1, pair)) { yield return shape; }

                for (var i = 0; i < 3; i++) { counts[tile + i]++; }
            }
        }

        private readonly struct Shape
        {
            public Shape(int pair, Group[] groups) => (Pair, Groups) = (pair, groups);
            public int Pair { get; }
            public Group[] Groups { get; }
        }
    }
}
