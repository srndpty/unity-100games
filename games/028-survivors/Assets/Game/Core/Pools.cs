using System;
using IntGeometry;
using TileMotion;

namespace Game.Core
{
    /// <summary>
    /// 生きている敵（固定容量の SoA）。番号は Tick の間は変わらない：倒れた敵は体力 0 のまま残し、Tick の終わりに
    /// 末尾と入れ替えて詰める（順序は決定的）。近傍探索のグリッドはこの番号を持つ。
    /// </summary>
    public sealed class EnemySet
    {
        internal int[] IdOf;
        internal int[] DefOf;
        internal int[] X;
        internal int[] Y;
        internal int[] StartX;
        internal int[] StartY;
        internal int[] Vx;
        internal int[] Vy;
        internal int[] Hp;
        internal int[] Flash;
        internal int[] OrbitWait;
        internal int[] SteerTick;

        internal EnemySet(int capacity)
        {
            IdOf = new int[capacity];
            DefOf = new int[capacity];
            X = new int[capacity];
            Y = new int[capacity];
            StartX = new int[capacity];
            StartY = new int[capacity];
            Vx = new int[capacity];
            Vy = new int[capacity];
            Hp = new int[capacity];
            Flash = new int[capacity];
            OrbitWait = new int[capacity];
            SteerTick = new int[capacity];
        }

        /// <summary>被弾の点滅の Tick 数。</summary>
        public const int FlashTicks = 6;

        public int Count { get; private set; }

        public int Capacity => IdOf.Length;

        /// <summary>試合の中で一意の番号。向きを決め直す位相にも使う。</summary>
        public int Id(int i) => IdOf[Check(i)];

        /// <summary><see cref="TideConfig.Enemies"/> の番号。</summary>
        public int Def(int i) => DefOf[Check(i)];

        public Vec2i Position(int i) => new(X[Check(i)], Y[i]);

        /// <summary>この Tick の始めの位置。</summary>
        public Vec2i StartPosition(int i) => new(StartX[Check(i)], StartY[i]);

        public Vec2i Velocity(int i) => new(Vx[Check(i)], Vy[i]);

        public int Health(int i) => Hp[Check(i)];

        public bool IsFlashing(int i) => Flash[Check(i)] > 0;

        /// <summary>最後に向きを決め直した Tick（出現した Tick を含む）。</summary>
        public int LastSteerTick(int i) => SteerTick[Check(i)];

        internal int Add(int id, int def, Vec2i position, Vec2i velocity, int health, int tick)
        {
            if (Count >= Capacity) { throw new InvalidOperationException("敵の数が容量を超えます。"); }
            var i = Count++;
            IdOf[i] = id;
            DefOf[i] = def;
            X[i] = StartX[i] = position.X;
            Y[i] = StartY[i] = position.Y;
            Vx[i] = velocity.X;
            Vy[i] = velocity.Y;
            Hp[i] = health;
            Flash[i] = 0;
            OrbitWait[i] = 0;
            SteerTick[i] = tick;
            return i;
        }

        /// <summary>体力 0 の敵を、末尾と入れ替えて詰める。</summary>
        internal void RemoveDead()
        {
            for (var i = Count - 1; i >= 0; i--)
            {
                if (Hp[i] > 0) { continue; }
                var last = --Count;
                if (i == last) { continue; }
                IdOf[i] = IdOf[last];
                DefOf[i] = DefOf[last];
                X[i] = X[last];
                Y[i] = Y[last];
                StartX[i] = StartX[last];
                StartY[i] = StartY[last];
                Vx[i] = Vx[last];
                Vy[i] = Vy[last];
                Hp[i] = Hp[last];
                Flash[i] = Flash[last];
                OrbitWait[i] = OrbitWait[last];
                SteerTick[i] = SteerTick[last];
            }
        }

        internal EnemySet Clone()
        {
            var copy = (EnemySet)MemberwiseClone();
            copy.IdOf = (int[])IdOf.Clone();
            copy.DefOf = (int[])DefOf.Clone();
            copy.X = (int[])X.Clone();
            copy.Y = (int[])Y.Clone();
            copy.StartX = (int[])StartX.Clone();
            copy.StartY = (int[])StartY.Clone();
            copy.Vx = (int[])Vx.Clone();
            copy.Vy = (int[])Vy.Clone();
            copy.Hp = (int[])Hp.Clone();
            copy.Flash = (int[])Flash.Clone();
            copy.OrbitWait = (int[])OrbitWait.Clone();
            copy.SteerTick = (int[])SteerTick.Clone();
            return copy;
        }

        internal void Mix(ref StateHash hash)
        {
            hash.Mix(Count);
            for (var i = 0; i < Count; i++)
            {
                hash.Mix(IdOf[i]);
                hash.Mix(DefOf[i]);
                hash.Mix(X[i]);
                hash.Mix(Y[i]);
                hash.Mix(Vx[i]);
                hash.Mix(Vy[i]);
                hash.Mix(Hp[i]);
                hash.Mix(Flash[i]);
                hash.Mix(OrbitWait[i]);
                hash.Mix(SteerTick[i]);
            }
        }

        private int Check(int i)
        {
            if ((uint)i >= (uint)Count) { throw new ArgumentOutOfRangeException(nameof(i), i, "範囲外の番号です。"); }
            return i;
        }
    }

    /// <summary>
    /// 武器の弾（固定容量の SoA）。直進し、寿命・貫通数を使い切ると消える。当たった敵の番号（ID）を貫通数まで覚え、同じ敵へ二度当たらない。
    /// </summary>
    public sealed class ProjectileSet
    {
        internal int[] IdOf;
        internal int[] X;
        internal int[] Y;
        internal int[] StartX;
        internal int[] StartY;
        internal int[] Vx;
        internal int[] Vy;
        internal int[] Life;
        internal int[] Damage;
        internal int[] Radius;
        internal int[] PierceLeft;
        internal int[] HitCount;
        internal int[] Hits;

        internal ProjectileSet(int capacity)
        {
            IdOf = new int[capacity];
            X = new int[capacity];
            Y = new int[capacity];
            StartX = new int[capacity];
            StartY = new int[capacity];
            Vx = new int[capacity];
            Vy = new int[capacity];
            Life = new int[capacity];
            Damage = new int[capacity];
            Radius = new int[capacity];
            PierceLeft = new int[capacity];
            HitCount = new int[capacity];
            Hits = new int[capacity * TideConfig.MaxCount];
        }

        public int Count { get; private set; }

        public int Capacity => IdOf.Length;

        public int Id(int i) => IdOf[Check(i)];

        public Vec2i Position(int i) => new(X[Check(i)], Y[i]);

        public Vec2i Velocity(int i) => new(Vx[Check(i)], Vy[i]);

        public int RadiusOf(int i) => Radius[Check(i)];

        /// <summary>あと何体に当たれるか。</summary>
        public int PierceRemaining(int i) => PierceLeft[Check(i)];

        internal void Add(int id, Vec2i position, Vec2i velocity, int life, int damage, int radius, int pierce)
        {
            // 検証を通った設定では容量を超えない（TideConfig.ProjectileCapacityOf）。超えたら壊れているので止める。
            if (Count >= Capacity) { throw new InvalidOperationException("弾の数が上限を超えます。"); }
            var i = Count++;
            IdOf[i] = id;
            X[i] = StartX[i] = position.X;
            Y[i] = StartY[i] = position.Y;
            Vx[i] = velocity.X;
            Vy[i] = velocity.Y;
            Life[i] = life;
            Damage[i] = damage;
            Radius[i] = radius;
            PierceLeft[i] = pierce;
            HitCount[i] = 0;
        }

        internal bool HasHit(int i, int enemyId)
        {
            var from = i * TideConfig.MaxCount;
            for (var k = 0; k < HitCount[i]; k++)
            {
                if (Hits[from + k] == enemyId) { return true; }
            }

            return false;
        }

        internal void RecordHit(int i, int enemyId)
        {
            Hits[(i * TideConfig.MaxCount) + HitCount[i]] = enemyId;
            HitCount[i]++;
            PierceLeft[i]--;
        }

        /// <summary>寿命・貫通数を使い切った弾を、末尾と入れ替えて詰める。</summary>
        internal void RemoveSpent()
        {
            for (var i = Count - 1; i >= 0; i--)
            {
                if (Life[i] > 0 && PierceLeft[i] > 0) { continue; }
                var last = --Count;
                if (i == last) { continue; }
                IdOf[i] = IdOf[last];
                X[i] = X[last];
                Y[i] = Y[last];
                StartX[i] = StartX[last];
                StartY[i] = StartY[last];
                Vx[i] = Vx[last];
                Vy[i] = Vy[last];
                Life[i] = Life[last];
                Damage[i] = Damage[last];
                Radius[i] = Radius[last];
                PierceLeft[i] = PierceLeft[last];
                HitCount[i] = HitCount[last];
                Array.Copy(Hits, last * TideConfig.MaxCount, Hits, i * TideConfig.MaxCount, TideConfig.MaxCount);
            }
        }

        internal ProjectileSet Clone()
        {
            var copy = (ProjectileSet)MemberwiseClone();
            copy.IdOf = (int[])IdOf.Clone();
            copy.X = (int[])X.Clone();
            copy.Y = (int[])Y.Clone();
            copy.StartX = (int[])StartX.Clone();
            copy.StartY = (int[])StartY.Clone();
            copy.Vx = (int[])Vx.Clone();
            copy.Vy = (int[])Vy.Clone();
            copy.Life = (int[])Life.Clone();
            copy.Damage = (int[])Damage.Clone();
            copy.Radius = (int[])Radius.Clone();
            copy.PierceLeft = (int[])PierceLeft.Clone();
            copy.HitCount = (int[])HitCount.Clone();
            copy.Hits = (int[])Hits.Clone();
            return copy;
        }

        internal void Mix(ref StateHash hash)
        {
            hash.Mix(Count);
            for (var i = 0; i < Count; i++)
            {
                hash.Mix(IdOf[i]);
                hash.Mix(X[i]);
                hash.Mix(Y[i]);
                hash.Mix(Vx[i]);
                hash.Mix(Vy[i]);
                hash.Mix(Life[i]);
                hash.Mix(Damage[i]);
                hash.Mix(Radius[i]);
                hash.Mix(PierceLeft[i]);
                hash.Mix(HitCount[i]);
                for (var k = 0; k < HitCount[i]; k++) { hash.Mix(Hits[(i * TideConfig.MaxCount) + k]); }
            }
        }

        private int Check(int i)
        {
            if ((uint)i >= (uint)Count) { throw new ArgumentOutOfRangeException(nameof(i), i, "範囲外の番号です。"); }
            return i;
        }
    }

    /// <summary>
    /// 落ちている経験値の宝石（固定容量の SoA）。取った宝石はその場で消すので、同じ宝石の値が二度加わることはない。
    /// </summary>
    public sealed class GemSet
    {
        internal int[] IdOf;
        internal int[] X;
        internal int[] Y;
        internal long[] Value;
        internal bool[] Attracted;

        internal GemSet(int capacity)
        {
            IdOf = new int[capacity];
            X = new int[capacity];
            Y = new int[capacity];
            Value = new long[capacity];
            Attracted = new bool[capacity];
        }

        public int Count { get; private set; }

        public int Capacity => IdOf.Length;

        public int Id(int i) => IdOf[Check(i)];

        public Vec2i Position(int i) => new(X[Check(i)], Y[i]);

        public long ValueOf(int i) => Value[Check(i)];

        public bool IsAttracted(int i) => Attracted[Check(i)];

        /// <summary>落ちている宝石の値の合計。</summary>
        public long TotalValue
        {
            get
            {
                long total = 0;
                for (var i = 0; i < Count; i++) { total += Value[i]; }
                return total;
            }
        }

        internal void Add(int id, Vec2i position, long value)
        {
            if (Count >= Capacity) { throw new InvalidOperationException("宝石の数が容量を超えます。"); }
            var i = Count++;
            IdOf[i] = id;
            X[i] = position.X;
            Y[i] = position.Y;
            Value[i] = value;
            Attracted[i] = false;
        }

        /// <summary>宝石 <paramref name="i"/> を末尾と入れ替えて消す。</summary>
        internal void RemoveAt(int i)
        {
            var last = --Count;
            if (i == last) { return; }
            IdOf[i] = IdOf[last];
            X[i] = X[last];
            Y[i] = Y[last];
            Value[i] = Value[last];
            Attracted[i] = Attracted[last];
        }

        internal GemSet Clone()
        {
            var copy = (GemSet)MemberwiseClone();
            copy.IdOf = (int[])IdOf.Clone();
            copy.X = (int[])X.Clone();
            copy.Y = (int[])Y.Clone();
            copy.Value = (long[])Value.Clone();
            copy.Attracted = (bool[])Attracted.Clone();
            return copy;
        }

        internal void Mix(ref StateHash hash)
        {
            hash.Mix(Count);
            for (var i = 0; i < Count; i++)
            {
                hash.Mix(IdOf[i]);
                hash.Mix(X[i]);
                hash.Mix(Y[i]);
                hash.Mix(Value[i]);
                hash.Mix(Attracted[i]);
            }
        }

        private int Check(int i)
        {
            if ((uint)i >= (uint)Count) { throw new ArgumentOutOfRangeException(nameof(i), i, "範囲外の番号です。"); }
            return i;
        }
    }
}
