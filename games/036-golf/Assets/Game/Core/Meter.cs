using TileMotion;

namespace Game.Core
{
    public enum MeterResult
    {
        /// <summary>何も決まらない（押下を無視した・まだ進んでいる）。</summary>
        None,

        /// <summary>パワーが決まった。</summary>
        PowerSet,

        /// <summary>インパクト帯の中で押した。<see cref="Meter.Error"/> がずれ。</summary>
        Impact,

        /// <summary>帯の外で押した・押さずに帯を過ぎた。</summary>
        Mishit,

        /// <summary>パワーを決めずにゲージが戻った。打たない（打数に数えない）。</summary>
        Canceled,
    }

    /// <summary>メーターの見え方（値型、割り当てなしで読む）。</summary>
    public readonly struct MeterView
    {
        public MeterView(int t, int gauge, bool powerSet, int power, int idealTick)
        {
            T = t;
            Gauge = gauge;
            PowerSet = powerSet;
            Power = power;
            IdealTick = idealTick;
        }

        public int T { get; }

        public int Gauge { get; }

        public bool PowerSet { get; }

        public int Power { get; }

        public int IdealTick { get; }
    }

    /// <summary>
    /// 2 回押しのメーター。振り始めた Tick を T = 0 とし、<see cref="Step"/> ごとに T が 1 増える。
    /// ゲージは T = 0〜H で 0 → 1000‰、T = H〜2H で 1000 → 0‰（H は <see cref="GolfConfig.MeterHalfTicks"/>）。
    /// <list type="bullet">
    /// <item>1 回目（T = 1〜2H − W − 1）：その T のゲージの値をパワーにする。T = 0 の押下（振り始めと同じ間）は無視する。</item>
    /// <item>パワーを決めずに T = 2H − W に着いたら取り消し。</item>
    /// <item>2 回目：ずれ e = T − 2H。|e| ≤ W ならインパクト、外ならミス（ずれは ±W）。押さずに T = 2H + W + 1 に着いたらミス（+W）。</item>
    /// <item>決まった後の押下は無視する。</item>
    /// </list>
    /// </summary>
    public sealed class Meter
    {
        private readonly int _half;
        private readonly int _window;

        public Meter(GolfConfig config)
        {
            _half = config.MeterHalfTicks;
            _window = config.ImpactWindow;
        }

        /// <summary>振り始めてからの Tick 数。</summary>
        public int T { get; private set; }

        public bool PowerSet { get; private set; }

        /// <summary>決めたパワー（‰）。</summary>
        public int Power { get; private set; }

        /// <summary>インパクトのずれ（Tick、負は早い）。</summary>
        public int Error { get; private set; }

        /// <summary>結果が決まった（インパクト・ミス・取り消し）。</summary>
        public bool Done { get; private set; }

        /// <summary>今のゲージの値（‰）。</summary>
        public int Gauge => GaugeAt(T);

        public int IdealTick => _half * 2;

        /// <summary>T のゲージの値（‰）。</summary>
        public int GaugeAt(int t)
        {
            if (t <= 0) { return 0; }
            if (t <= _half) { return t * 1000 / _half; }
            var down = (2 * _half) - t;
            return down <= 0 ? 0 : down * 1000 / _half;
        }

        public Meter Clone() => (Meter)MemberwiseClone();

        public MeterView View => new(T, Gauge, PowerSet, Power, IdealTick);

        public MeterResult Press()
        {
            if (Done) { return MeterResult.None; }
            if (!PowerSet)
            {
                if (T < 1 || T > IdealTick - _window - 1) { return MeterResult.None; }
                PowerSet = true;
                Power = GaugeAt(T);
                return MeterResult.PowerSet;
            }

            var error = T - IdealTick;
            Done = true;
            if (error < -_window || error > _window)
            {
                Error = error < 0 ? -_window : _window;
                return MeterResult.Mishit;
            }

            Error = error;
            return MeterResult.Impact;
        }

        public MeterResult Step()
        {
            if (Done) { return MeterResult.None; }
            T++;
            if (!PowerSet && T >= IdealTick - _window)
            {
                Done = true;
                return MeterResult.Canceled;
            }

            if (PowerSet && T > IdealTick + _window)
            {
                Done = true;
                Error = _window;
                return MeterResult.Mishit;
            }

            return MeterResult.None;
        }

        public void MixInto(ref StateHash hash)
        {
            hash.Mix(T);
            hash.Mix(PowerSet);
            hash.Mix(Power);
            hash.Mix(Error);
            hash.Mix(Done);
        }
    }
}
