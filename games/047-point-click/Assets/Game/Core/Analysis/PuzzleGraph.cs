using System;
using System.Collections.Generic;
using System.Text;
using Game.Core.Rules;

namespace Game.Core.Analysis
{
    /// <summary>
    /// 謎の依存グラフ。ルール表の `@puzzle … after …` の宣言（設計の意図）と、状態空間の探索から導いた
    /// 「必ず先に解く謎」（規則の実際）を、推移閉包どうしで照合する。図は推移簡約（直接の依存だけ）で描く。
    /// </summary>
    public static class PuzzleGraph
    {
        /// <summary>宣言の推移閉包（謎ごとに、先に解く謎のビット集合）。循環があれば null。</summary>
        public static int[]? DeclaredClosure(IReadOnlyList<PuzzleDef> puzzles)
        {
            if (puzzles == null) { throw new ArgumentNullException(nameof(puzzles)); }
            var n = puzzles.Count;
            var closure = new int[n];
            for (int p = 0; p < n; p++)
            {
                foreach (var a in puzzles[p].After) { closure[p] |= 1 << a; }
            }

            // ワーシャル–フロイド（謎は 30 個まで）。
            for (int k = 0; k < n; k++)
            {
                for (int p = 0; p < n; p++)
                {
                    if ((closure[p] & (1 << k)) != 0) { closure[p] |= closure[k]; }
                }
            }

            for (int p = 0; p < n; p++)
            {
                if ((closure[p] & (1 << p)) != 0) { return null; }
            }

            return closure;
        }

        /// <summary>推移簡約：閉包から、間に別の謎を挟める依存を除いた直接の依存。</summary>
        public static int[] Reduce(IReadOnlyList<int> closure)
        {
            if (closure == null) { throw new ArgumentNullException(nameof(closure)); }
            var direct = new int[closure.Count];
            for (int p = 0; p < closure.Count; p++)
            {
                var d = closure[p];
                for (int q = 0; q < closure.Count; q++)
                {
                    if ((closure[p] & (1 << q)) != 0) { d &= ~closure[q]; }
                }

                direct[p] = d;
            }

            return direct;
        }

        /// <summary>宣言と導いた依存を照合する。食い違う謎ごとに 1 件の誤り。</summary>
        public static void Compare(RuleBook book, IReadOnlyList<int> derived, List<RuleError> errors)
        {
            if (book == null) { throw new ArgumentNullException(nameof(book)); }
            if (derived == null) { throw new ArgumentNullException(nameof(derived)); }
            if (errors == null) { throw new ArgumentNullException(nameof(errors)); }
            var declared = DeclaredClosure(book.Puzzles);
            if (declared == null) { return; }
            foreach (var p in book.Puzzles)
            {
                if (declared[p.Index] == derived[p.Index]) { continue; }
                errors.Add(new RuleError(p.Line,
                    $"謎 {p.Id} の依存が宣言と食い違います。宣言では [{Names(book, declared[p.Index])}] の後、ルールからは [{Names(book, derived[p.Index])}] の後に解けます"));
            }

            var others = ((1 << book.Puzzles.Count) - 1) & ~(1 << book.Goal);
            if (derived[book.Goal] != others)
            {
                var goal = book.Puzzles[book.Goal];
                errors.Add(new RuleError(goal.Line, $"脱出の謎 {goal.Id} は他の全ての謎の後に解けるようにします（今は [{Names(book, others & ~derived[book.Goal])}] を解かずに脱出できます）"));
            }
        }

        /// <summary>宣言の依存グラフを Mermaid で（README に貼り、テストで照合する）。</summary>
        public static string ToMermaid(RuleBook book)
        {
            if (book == null) { throw new ArgumentNullException(nameof(book)); }
            var closure = DeclaredClosure(book.Puzzles) ?? throw new InvalidOperationException("依存が循環しています");
            var direct = Reduce(closure);
            var sb = new StringBuilder();
            sb.Append("graph LR\n");
            foreach (var p in book.Puzzles) { sb.Append("  ").Append(p.Id).Append("[\"").Append(p.Name).Append("\"]\n"); }
            foreach (var p in book.Puzzles)
            {
                foreach (var q in book.Puzzles)
                {
                    if ((direct[p.Index] & (1 << q.Index)) != 0) { sb.Append("  ").Append(q.Id).Append(" --> ").Append(p.Id).Append('\n'); }
                }
            }

            sb.Append("  ").Append(book.Puzzles[book.Goal].Id).Append(" --> escape((脱出))\n");
            return sb.ToString();
        }

        private static string Names(RuleBook book, int mask)
        {
            var names = new List<string>();
            foreach (var p in book.Puzzles)
            {
                if ((mask & (1 << p.Index)) != 0) { names.Add(p.Id); }
            }

            return string.Join(", ", names);
        }
    }
}
