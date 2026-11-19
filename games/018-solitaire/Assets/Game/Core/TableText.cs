using System;
using System.Collections.Generic;
using System.Text;

namespace Game.Core
{
    /// <summary>
    /// 盤の文字表記。テストと撮影の場面を手で書くためのもの。1 行に 1 山を下から上の順に書く。
    /// <code>
    /// draw: 1
    /// stock: *            （* は他の行に出てこない札を番号順に全部。1 か所だけ。場札なら一番上だけ表）
    /// waste: 3S
    /// f1: AS 2S
    /// t1: 9M TW | 8F      （| の左が伏せ札、右が表。| がなければ全部表）
    /// </code>
    /// 書かなかった山は空。draw を省くと 1。
    /// </summary>
    public static class TableText
    {
        private static readonly char[] _space = { ' ' };

        public static Table Parse(string text)
        {
            if (text == null) { throw new ArgumentNullException(nameof(text)); }
            var piles = new List<Card>[PileId.Count];
            for (var i = 0; i < piles.Length; i++) { piles[i] = new List<Card>(); }
            var faceDown = new int[PileId.TableauCount];
            var drawCount = 1;
            PileId? fillPile = null;
            var used = new bool[Card.DeckSize];

            foreach (var rawLine in text.Split('\n'))
            {
                var line = rawLine.Trim();
                if (line.Length == 0) { continue; }
                var colon = line.IndexOf(':', StringComparison.Ordinal);
                if (colon < 0) { throw new FormatException($"「名前: 札」の形ではありません：{line}"); }
                var name = line.Substring(0, colon).Trim();
                var body = line.Substring(colon + 1).Trim();
                if (name == "draw")
                {
                    drawCount = int.Parse(body, System.Globalization.CultureInfo.InvariantCulture);
                    continue;
                }

                var pile = PileOf(name);
                if (body == "*")
                {
                    if (fillPile != null) { throw new FormatException("* は 1 か所にだけ書けます。"); }
                    fillPile = pile;
                    continue;
                }

                var tokens = body.Split(_space, StringSplitOptions.RemoveEmptyEntries);
                var list = piles[pile.Index];
                foreach (var token in tokens)
                {
                    if (token == "|")
                    {
                        if (pile.Kind != PileKind.Tableau) { throw new FormatException($"{name} に伏せ札の区切りは書けません。"); }
                        faceDown[pile.Slot] = list.Count;
                        continue;
                    }

                    var card = Card.Parse(token);
                    if (used[card.Id]) { throw new FormatException($"{card} が 2 回出てきます。"); }
                    used[card.Id] = true;
                    list.Add(card);
                }
            }

            if (fillPile is PileId fill)
            {
                var list = piles[fill.Index];
                for (var id = 0; id < Card.DeckSize; id++)
                {
                    if (!used[id]) { list.Add(Card.FromId(id)); }
                }

                // 場札に埋めたときは一番上だけ表にする。
                if (fill.Kind == PileKind.Tableau && list.Count > 0) { faceDown[fill.Slot] = list.Count - 1; }
            }

            return Table.Create(drawCount, piles, faceDown);
        }

        /// <summary>全山を省略せずに書く。同じ盤なら同じ文字列になる。</summary>
        public static string Format(IReadOnlyTable table)
        {
            if (table == null) { throw new ArgumentNullException(nameof(table)); }
            var builder = new StringBuilder();
            builder.Append("draw: ").Append(table.DrawCount).Append('\n');
            foreach (var pile in PileId.All)
            {
                builder.Append(pile.ToString()).Append(':');
                var count = table.CountOf(pile);
                var down = pile.Kind == PileKind.Tableau ? table.FaceDownCountOf(pile) : -1;
                for (var i = 0; i < count; i++)
                {
                    if (i == down) { builder.Append(" |"); }
                    builder.Append(' ').Append(table.CardAt(pile, i).ToString());
                }

                builder.Append('\n');
            }

            return builder.ToString();
        }

        private static PileId PileOf(string name)
        {
            foreach (var pile in PileId.All)
            {
                if (pile.ToString() == name) { return pile; }
            }

            throw new FormatException($"山の名前ではありません：{name}");
        }
    }
}
