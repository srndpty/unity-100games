using System;
using System.Collections.Generic;
using Game.Core.Runtime;
using Game.Core.Script;

namespace Game.Core
{
    /// <summary>
    /// 作品の本番シナリオの素材の表と規則。Unity 側の素材（背景・立ち絵の Sprite）はこの表と照合し、
    /// ビルドの前の検証もここから同じ規則でコンパイルする。
    /// </summary>
    public static class Scenario
    {
        /// <summary>背景の ID（宿の広間・岬の灯台・宿の廊下・油の倉庫・夜明けの海）。</summary>
        public static IReadOnlyList<string> Backgrounds { get; } = Array.AsReadOnly(new[] { "inn_hall", "lighthouse", "corridor", "storeroom", "dawn_sea" });

        /// <summary>立ち絵の（人物, 表情）。</summary>
        public static IReadOnlyList<KeyValuePair<string, string>> Portraits { get; } = Array.AsReadOnly(new[]
        {
            Pair("mio", "calm"),
            Pair("mio", "smile"),
            Pair("mio", "worried"),
            Pair("ren", "calm"),
            Pair("ren", "smile"),
            Pair("ren", "cold"),
            Pair("sosuke", "calm"),
        });

        public static ScriptCatalog Catalog { get; } = new(Backgrounds, Portraits);

        /// <summary>本番の規則（選択肢 3 箇所・結末 3 つ）。文の上限字数は設定で変えられる。</summary>
        public static ScriptRules Rules(int maxTextLength = 80)
        {
            var r = ScriptRules.Scenario();
            r.MaxTextLength = maxTextLength;
            return r;
        }

        public static CompileResult Compile(string source, int maxTextLength = 80) =>
            ScriptCompiler.Compile(source, Catalog, Rules(maxTextLength));

        /// <summary>結末ごとの代表の選択の列（網羅探索で最初に見つかった列）。撮影・テストで再生する。</summary>
        public static IReadOnlyList<int> RouteTo(CompileResult compiled, string endingId)
        {
            if (compiled == null) { throw new ArgumentNullException(nameof(compiled)); }
            var script = compiled.Script ?? throw new ArgumentException("コンパイルに失敗したスクリプト", nameof(compiled));
            var ending = script.EndingIndex(endingId);
            foreach (var p in compiled.Paths)
            {
                if (p.Ending == ending) { return p.Choices; }
            }

            throw new ArgumentException($"結末 {endingId} への経路がありません", nameof(endingId));
        }

        private static KeyValuePair<string, string> Pair(string character, string expression) => new(character, expression);
    }
}
