using System.Collections.Generic;

namespace Game.Core
{
    /// <summary>
    /// 1部分ステップの軌跡。与えられた移動量の速さで折れ線を進み、使い切らなければ残りの時間は止まる。
    /// 座標は周回で正規化しない連続値で持ち、接触判定はすべて整数で行う。
    /// </summary>
    public sealed class MotionPath
    {
        private readonly List<Leg> _legs = new(4);
        private int _budget;
        private int _traveled;
        private int _x;
        private int _y;

        /// <summary>軌跡を始める。<paramref name="budget"/> はこの部分ステップ全体で進める移動量。</summary>
        public void Begin(int x, int y, int budget)
        {
            _legs.Clear();
            _x = x;
            _y = y;
            _budget = budget;
            _traveled = 0;
        }

        /// <summary>今の位置から指定の向きへ進む区間を足す。</summary>
        public void Add(int dx, int dy, int units)
        {
            if (units <= 0) { return; }
            _legs.Add(new Leg(_x - (dx * _traveled), _y - (dy * _traveled), dx, dy, _traveled + units));
            _x += dx * units;
            _y += dy * units;
            _traveled += units;
        }

        /// <summary>全体の移動量を、実際に進んだ分だけに縮める。途中で区切った軌跡の時間を合わせるために使う。</summary>
        public void ShortenTo(int budget) => _budget = budget;

        /// <summary>
        /// 同じ時間を動く2つの軌跡が、途中を含めて半径以内に近づくか。x は周期 <paramref name="span"/> で周回する。
        /// <paramref name="includeEnd"/> が false なら、終了の瞬間にだけ起きる接触は数えない。
        /// </summary>
        public bool Touches(MotionPath other, int span, int radius, bool includeEnd = true)
        {
            var mine = 0;
            var theirs = 0;
            var from = new Moment(0, 1);
            while (true)
            {
                var myEnd = EndOf(mine);
                var theirEnd = other.EndOf(theirs);
                var to = myEnd.CompareTo(theirEnd) <= 0 ? myEnd : theirEnd;
                var a = StateOf(mine);
                var b = other.StateOf(theirs);
                for (var copy = -1; copy <= 1; copy++)
                {
                    var rx = a.BaseX - b.BaseX + ((long)copy * span);
                    if (ComesWithin(rx, a.BaseY - b.BaseY, a.VelocityX - b.VelocityX, a.VelocityY - b.VelocityY, from, to, radius, includeEnd || !to.IsEnd)) { return true; }
                }

                if (to.IsEnd) { return false; }
                if (myEnd.CompareTo(to) == 0) { mine++; }
                if (theirEnd.CompareTo(to) == 0) { theirs++; }
                from = to;
            }
        }

        private Moment EndOf(int leg) => leg < _legs.Count ? new Moment(_legs[leg].TraveledAtEnd, _budget) : new Moment(1, 1);

        // 区間中の位置は Base + Velocity * 時刻（部分ステップ全体を 1 とする）で表せる。
        private LegState StateOf(int leg)
        {
            if (leg >= _legs.Count) { return new LegState(_x, _y, 0, 0); }
            var current = _legs[leg];
            return new LegState(current.BaseX, current.BaseY, current.Dx * _budget, current.Dy * _budget);
        }

        // 区間の終わりを含めないときは、終わりでちょうど半径の場合だけ接触にしない（半径より内側なら、その少し前から接触している）。
        private static bool ComesWithin(long rx, long ry, long vx, long vy, Moment from, Moment to, int radius, bool includeTo)
        {
            long limit = radius * (long)radius;
            if (IsWithinAt(rx, ry, vx, vy, from, limit, inclusive: true) || IsWithinAt(rx, ry, vx, vy, to, limit, includeTo)) { return true; }

            var speedSquared = (vx * vx) + (vy * vy);
            if (speedSquared == 0) { return false; }

            // 最接近の時刻 -dot / speedSquared が区間の内側にあるときだけ、直線との距離で判定する。
            var dot = (rx * vx) + (ry * vy);
            if (-dot * from.Denominator <= from.Numerator * speedSquared || -dot * to.Denominator >= to.Numerator * speedSquared) { return false; }
            var cross = (rx * vy) - (ry * vx);
            return cross * cross <= limit * speedSquared;
        }

        private static bool IsWithinAt(long rx, long ry, long vx, long vy, Moment moment, long limit, bool inclusive)
        {
            var x = (rx * moment.Denominator) + (vx * moment.Numerator);
            var y = (ry * moment.Denominator) + (vy * moment.Numerator);
            var distance = (x * x) + (y * y);
            var scaledLimit = limit * moment.Denominator * moment.Denominator;
            return inclusive ? distance <= scaledLimit : distance < scaledLimit;
        }

        private readonly struct Leg
        {
            public Leg(int baseX, int baseY, int dx, int dy, int traveledAtEnd)
            {
                BaseX = baseX;
                BaseY = baseY;
                Dx = dx;
                Dy = dy;
                TraveledAtEnd = traveledAtEnd;
            }

            public int BaseX { get; }
            public int BaseY { get; }
            public int Dx { get; }
            public int Dy { get; }
            public int TraveledAtEnd { get; }
        }

        private readonly struct LegState
        {
            public LegState(long baseX, long baseY, long velocityX, long velocityY)
            {
                BaseX = baseX;
                BaseY = baseY;
                VelocityX = velocityX;
                VelocityY = velocityY;
            }

            public long BaseX { get; }
            public long BaseY { get; }
            public long VelocityX { get; }
            public long VelocityY { get; }
        }

        // 部分ステップ内の時刻を分数で持つ（1 で終わり）。
        private readonly struct Moment
        {
            public Moment(long numerator, long denominator)
            {
                Numerator = numerator;
                Denominator = denominator;
            }

            public long Numerator { get; }
            public long Denominator { get; }
            public bool IsEnd => Numerator >= Denominator;

            public int CompareTo(Moment other) => (Numerator * other.Denominator).CompareTo(other.Numerator * Denominator);
        }
    }
}
