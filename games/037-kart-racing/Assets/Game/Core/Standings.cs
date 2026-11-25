using System;

namespace Game.Core
{
    /// <summary>4 台の順位。完走は記録の早い順、未完走は周回・ゲート・区間の進み・番号の順。同着は番号の小さい方が上。</summary>
    public static class Standings
    {
        public static int GatesPassed(Kart kart, int gateCount) => kart.NextGate == 0 ? gateCount - 1 : kart.NextGate - 1;

        /// <summary>各車の順位（1 が先頭）。必ず 1〜4 が 1 つずつ。</summary>
        public static int[] Places(Kart[] karts, int gateCount)
        {
            var places = new int[KartConfig.KartCount];
            var order = new int[KartConfig.KartCount];
            Write(karts, gateCount, places, order);
            return places;
        }

        /// <summary>順位を <paramref name="places"/> へ書く。呼び出しのたびに配列を作らない。</summary>
        public static void Write(Kart[] karts, int gateCount, int[] places, int[] order)
        {
            if (karts == null) { throw new ArgumentNullException(nameof(karts)); }
            if (karts.Length != KartConfig.KartCount) { throw new ArgumentException("車は 4 台です。", nameof(karts)); }
            if (places == null || places.Length != KartConfig.KartCount) { throw new ArgumentException("順位の置き場は 4 つです。", nameof(places)); }
            if (order == null || order.Length != KartConfig.KartCount) { throw new ArgumentException("並びの置き場は 4 つです。", nameof(order)); }

            for (var i = 0; i < order.Length; i++) { order[i] = i; }
            for (var i = 1; i < order.Length; i++)
            {
                var id = order[i];
                var j = i;
                while (j > 0 && Compare(karts[order[j - 1]], karts[id], order[j - 1], id, gateCount) > 0)
                {
                    order[j] = order[j - 1];
                    j--;
                }

                order[j] = id;
            }

            for (var rank = 0; rank < order.Length; rank++) { places[order[rank]] = rank + 1; }
        }

        private static int Compare(Kart a, Kart b, int idA, int idB, int gateCount)
        {
            if (a.Finished != b.Finished) { return a.Finished ? -1 : 1; }
            if (a.Finished)
            {
                if (a.FinishTick != b.FinishTick) { return a.FinishTick.CompareTo(b.FinishTick); }
                return idA.CompareTo(idB);
            }

            if (a.Laps != b.Laps) { return b.Laps.CompareTo(a.Laps); }
            var gatesA = GatesPassed(a, gateCount);
            var gatesB = GatesPassed(b, gateCount);
            if (gatesA != gatesB) { return gatesB.CompareTo(gatesA); }
            if (a.SectionAlong != b.SectionAlong) { return b.SectionAlong.CompareTo(a.SectionAlong); }
            return idA.CompareTo(idB);
        }
    }
}
