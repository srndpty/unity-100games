using System;
using System.Collections.Generic;
using System.Globalization;
using Game.Core.Analysis;
using Game.Core.Rules;

namespace Game.Core
{
    /// <summary>
    /// 作品の本番の規則（部屋 3・謎 5）と、解答手順の読み込み。ビルドの前の検証もここから同じ規則でコンパイルする。
    /// </summary>
    public static class Adventure
    {
        private static readonly char[] _space = { ' ' };

        /// <summary>本番の上限。文の上限字数は台詞の帯に 1 行で収まる長さ。</summary>
        public static BookLimits Limits() => new() { ExpectedRooms = 3, ExpectedPuzzles = 5, MaxTextLength = 40 };

        public static CompileResult Compile(string source) => RuleCompiler.Compile(source, Limits());

        /// <summary>
        /// 解答手順の読み込み。1 行 1 手で「look 対象」「take 対象」「talk 対象」「use 対象」「use 持ち物 on 対象」
        /// 「go 出口」「topic 部屋.人物 話題」「bye」。`#` で始まる行はコメント。誤りは行番号付きの FormatException。
        /// </summary>
        public static IReadOnlyList<Move> ParseWalkthrough(RuleBook book, string text)
        {
            if (book == null) { throw new ArgumentNullException(nameof(book)); }
            if (text == null) { throw new ArgumentNullException(nameof(text)); }
            var moves = new List<Move>();
            var lines = text.Replace("\r\n", "\n").Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                var s = lines[i].Trim();
                if (s.Length == 0 || s[0] == '#') { continue; }
                moves.Add(ParseMove(book, s) ?? throw new FormatException(string.Format(CultureInfo.InvariantCulture, "解答手順の行 {0}：「{1}」を読めません", i + 1, s)));
            }

            return moves.AsReadOnly();
        }

        private static Move? ParseMove(RuleBook book, string s)
        {
            var t = s.Split(_space, StringSplitOptions.RemoveEmptyEntries);
            switch (t[0])
            {
                case "bye":
                    return t.Length == 1 ? Move.EndTalk() : (Move?)null;
                case "topic":
                    {
                        if (t.Length != 3) { return null; }
                        var topic = book.TopicIndex(t[1], t[2]);
                        return topic < 0 ? (Move?)null : Move.Ask(topic);
                    }

                case "go":
                    {
                        var h = t.Length == 2 ? book.HotspotIndex(t[1]) : -1;
                        return h < 0 || book.Hotspots[h].Kind != HotspotKind.Exit ? (Move?)null : Move.Act(Verb.Walk, Target.Hotspot(h));
                    }

                case "use" when t.Length == 4 && t[2] == "on":
                    {
                        var tool = book.ItemIndex(t[1]);
                        var target = TargetOf(book, t[3]);
                        if (tool < 0 || target == null) { return null; }
                        var tg = target.Value;
                        RuleBook.Normalize(ref tg, ref tool);
                        return Move.Act(Verb.Use, tg, tool);
                    }

                case "look":
                case "take":
                case "talk":
                case "use":
                    {
                        var target = t.Length == 2 ? TargetOf(book, t[1]) : null;
                        if (target == null) { return null; }
                        var verb = t[0] switch
                        {
                            "look" => Verb.Look,
                            "take" => Verb.Take,
                            "talk" => Verb.Talk,
                            _ => Verb.Use,
                        };
                        return Move.Act(verb, target.Value);
                    }

                default:
                    return null;
            }
        }

        private static Target? TargetOf(RuleBook book, string id)
        {
            var h = book.HotspotIndex(id);
            if (h >= 0) { return Target.Hotspot(h); }
            var i = book.ItemIndex(id);
            return i >= 0 ? Target.Item(i) : (Target?)null;
        }
    }
}
