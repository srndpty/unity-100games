using System;

namespace Game.Core.Data
{
    /// <summary>
    /// 区間の検証のための、能力値の取りうる範囲の見積もり。1 日に動きうる最大の増加（行動の効果の最大 × 最大の倍率 ＋ 枠ごとのイベントの効果の最大）と
    /// 最大の減少を求め、d 日目の範囲を「初期値 ± d 日ぶん」を宣言の範囲で切ったものとする。必ず実際の範囲を含む（大きめ）。
    /// </summary>
    public sealed class StatBounds
    {
        private readonly SummerBook _book;
        private readonly long[] _gain;
        private readonly long[] _loss;
        private readonly bool[] _flagPossible;

        public StatBounds(SummerBook book)
        {
            _book = book ?? throw new ArgumentNullException(nameof(book));
            var n = book.Stats.Count;
            _gain = new long[n];
            _loss = new long[n];
            _flagPossible = new bool[book.Flags.Count];
            var maxPercent = 0;
            foreach (var o in book.Outcomes) { maxPercent = Math.Max(maxPercent, o.Percent); }

            // 行動：1 日に 1 つなので、行動ごとの和の最大。
            var actionGain = new long[n];
            var actionLoss = new long[n];
            foreach (var a in book.Actions)
            {
                var g = new long[n];
                var l = new long[n];
                foreach (var e in a.Effects)
                {
                    if (e.Kind != EffectKind.Add)
                    {
                        if (e.Kind == EffectKind.Set) { _flagPossible[e.Target] = true; }
                        continue;
                    }

                    var stat = book.Stats[e.Target];
                    if (e.Max > 0) { g[e.Target] += stat.Cost ? e.Max : (long)e.Max * maxPercent / 100; }
                    if (e.Min < 0) { l[e.Target] += -e.Min; }
                }

                for (int s = 0; s < n; s++)
                {
                    actionGain[s] = Math.Max(actionGain[s], g[s]);
                    actionLoss[s] = Math.Max(actionLoss[s], l[s]);
                }
            }

            // イベント：枠ごとに 1 日に高々 1 本なので、枠ごとに（すぐ当てる効果 ＋ 選択肢の最大）の最大。
            var slotGain = new long[2, n];
            var slotLoss = new long[2, n];
            foreach (var ev in book.Events)
            {
                var g = new long[n];
                var l = new long[n];
                Add(ev.Effects, g, l);
                var cg = new long[n];
                var cl = new long[n];
                foreach (var c in ev.Choices)
                {
                    var g2 = new long[n];
                    var l2 = new long[n];
                    Add(c.Effects, g2, l2);
                    for (int s = 0; s < n; s++)
                    {
                        cg[s] = Math.Max(cg[s], g2[s]);
                        cl[s] = Math.Max(cl[s], l2[s]);
                    }
                }

                var slot = (int)ev.Slot;
                for (int s = 0; s < n; s++)
                {
                    slotGain[slot, s] = Math.Max(slotGain[slot, s], g[s] + cg[s]);
                    slotLoss[slot, s] = Math.Max(slotLoss[slot, s], l[s] + cl[s]);
                }
            }

            for (int s = 0; s < n; s++)
            {
                _gain[s] = actionGain[s] + slotGain[0, s] + slotGain[1, s];
                _loss[s] = actionLoss[s] + slotLoss[0, s] + slotLoss[1, s];
            }
        }

        /// <summary>フラグが立ちうるか（どこかの効果で set される）。</summary>
        public bool FlagPossible(int flag) => _flagPossible[flag];

        /// <summary>d 日目（その日の行動とイベントを含む）に能力値が取りうる範囲。</summary>
        public (int Min, int Max) StatOn(int stat, int day)
        {
            var s = _book.Stats[stat];
            var hi = Math.Min(s.Max, s.Initial + (day * _gain[stat]));
            var lo = Math.Max(s.Min, s.Initial - (day * _loss[stat]));
            return ((int)lo, (int)hi);
        }

        /// <summary>d 日目の被演算子の範囲（区間の評価に渡す）。</summary>
        public Func<Operand, (int Min, int Max)> On(int day) => o => o.Kind switch
        {
            OperandKind.Stat => StatOn(o.Value, day),
            OperandKind.Day => (day, day),
            OperandKind.Weekday => ((int)_book.WeekdayOf(day), (int)_book.WeekdayOf(day)),
            OperandKind.Action => (0, _book.Actions.Count - 1),
            _ => (o.Value, o.Value),
        };

        private void Add(System.Collections.Generic.IReadOnlyList<Effect> effects, long[] g, long[] l)
        {
            foreach (var e in effects)
            {
                if (e.Kind == EffectKind.Set) { _flagPossible[e.Target] = true; }
                if (e.Kind != EffectKind.Add) { continue; }
                if (e.Max > 0) { g[e.Target] += e.Max; }
                if (e.Min < 0) { l[e.Target] += -e.Min; }
            }
        }
    }
}
