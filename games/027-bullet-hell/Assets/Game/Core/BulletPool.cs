using System;
using TileMotion;

namespace Game.Core
{
    /// <summary>弾の形。形ごとに当たりの半径が決まる（<see cref="BulletShapes.Radius"/>）。</summary>
    public enum BulletShape : byte
    {
        Small,
        Rice,
        Orb,
        Large,
    }

    /// <summary>弾の色（見た目だけ。ルールには影響しない）。</summary>
    public enum BulletColor : byte
    {
        Red,
        Orange,
        Yellow,
        Green,
        Cyan,
        Blue,
        Purple,
        White,
    }

    public static class BulletShapes
    {
        public const int Count = 4;

        public const int ColorCount = 8;

        /// <summary>形の当たりの半径（<see cref="Field.Unit"/> 単位）。</summary>
        public static int Radius(BulletShape shape) => shape switch
        {
            BulletShape.Small => 3 * Field.Unit,
            BulletShape.Rice => (5 * Field.Unit) / 2,
            BulletShape.Orb => (9 * Field.Unit) / 2,
            BulletShape.Large => 9 * Field.Unit,
            _ => throw new ArgumentOutOfRangeException(nameof(shape), shape, "未定義の形です。"),
        };

        public static bool IsDefined(BulletShape shape) => (int)shape >= 0 && (int)shape < Count;

        public static bool IsDefined(BulletColor color) => (int)color >= 0 && (int)color < ColorCount;
    }

    /// <summary>
    /// 弾 1 発の出し方。速さ・加速は <see cref="BulletPool.SpeedOne"/> を 1 px/Tick とする整数、角度は <see cref="Trig"/> の単位。
    /// </summary>
    public readonly struct BulletSpawn
    {
        public BulletSpawn(Vec2i origin, int angle, int speed, int accel, int maxSpeed, int curve, int life, BulletShape shape, BulletColor color)
        {
            Origin = origin;
            Angle = Trig.Normalize(angle);
            Speed = speed;
            Accel = accel;
            MaxSpeed = maxSpeed;
            Curve = curve;
            Life = life;
            Shape = shape;
            Color = color;
        }

        /// <summary>出る位置（<see cref="Field.Unit"/> 単位）。</summary>
        public Vec2i Origin { get; }

        public int Angle { get; }

        public int Speed { get; }

        /// <summary>1 Tick ごとの速さの増分。</summary>
        public int Accel { get; }

        /// <summary>速さの上限（加速で超えない）。</summary>
        public int MaxSpeed { get; }

        /// <summary>1 Tick ごとの向きの増分（角速度）。</summary>
        public int Curve { get; }

        /// <summary>動く回数の上限（0 なら領域の外に出るまで）。</summary>
        public int Life { get; }

        public BulletShape Shape { get; }

        public BulletColor Color { get; }
    }

    /// <summary>
    /// 敵弾の置き場。固定容量の配列を成分ごとに持つ（SoA）。容量を超える発射は捨てて <see cref="Dropped"/> に数え、
    /// 例外にも配列の拡張にもしない。<see cref="Step"/> は割り当てをしない。
    /// <para>
    /// 1 Tick の運動（弾道式）：向き += 角速度 → 速さ = clamp(速さ + 加速, 0, 上限) → 位置 += 速さ × (cos, sin)。
    /// 位置は 1 px = 2^30 の整数で持ち、速さ（Q16）× cos（Q14）をそのまま足すので丸めの誤差が溜まらない
    /// （直進なら t Tick 後の位置は「出た位置 + t × 速度」にちょうど一致する）。当たり判定には 1 px = 256 の位置を使う。
    /// </para>
    /// <para>
    /// 消える時機：Tick の初めに、寿命（動いた回数）に達した弾と、前の Tick に余白の外へ出た弾を取り除く。
    /// 取り除いても残りの弾の順序は変えない（描画の重なりと再現性のため）。
    /// </para>
    /// </summary>
    public sealed class BulletPool
    {
        /// <summary>当たり判定の位置（1 px = 2^8）から内部の位置（1 px = 2^30）へのシフト量。</summary>
        public const int PositionShift = 22;

        /// <summary>速さの 1 px/Tick。</summary>
        public const int SpeedOne = 1 << 16;

        public const int MaxCapacity = 1 << 16;

        private readonly long[] _x;
        private readonly long[] _y;
        private readonly int[] _startX;
        private readonly int[] _startY;
        private readonly int[] _angle;
        private readonly int[] _speed;
        private readonly int[] _accel;
        private readonly int[] _maxSpeed;
        private readonly int[] _curve;
        private readonly int[] _age;
        private readonly int[] _life;
        private readonly BulletShape[] _shape;
        private readonly BulletColor[] _color;

        public BulletPool(int capacity)
        {
            if (capacity < 1 || capacity > MaxCapacity)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity), capacity, $"1〜{MaxCapacity} にしてください。");
            }

            Capacity = capacity;
            _x = new long[capacity];
            _y = new long[capacity];
            _startX = new int[capacity];
            _startY = new int[capacity];
            _angle = new int[capacity];
            _speed = new int[capacity];
            _accel = new int[capacity];
            _maxSpeed = new int[capacity];
            _curve = new int[capacity];
            _age = new int[capacity];
            _life = new int[capacity];
            _shape = new BulletShape[capacity];
            _color = new BulletColor[capacity];
        }

        public int Capacity { get; }

        public int Count { get; private set; }

        /// <summary>容量が埋まっていて捨てた発射の数（累計）。</summary>
        public long Dropped { get; private set; }

        /// <summary>置き場に入れた弾の数（累計）。</summary>
        public long Spawned { get; private set; }

        /// <summary>
        /// 弾を 1 発入れる。入れた弾はまだ動かず、次の <see cref="Step"/> で 1 回目の移動をする
        /// （その移動の始めの位置が出た位置になる）。満杯なら入れずに false を返し、<see cref="Dropped"/> を増やす。
        /// </summary>
        public bool Spawn(in BulletSpawn spawn)
        {
            if (!BulletShapes.IsDefined(spawn.Shape)) { throw new ArgumentOutOfRangeException(nameof(spawn), spawn.Shape, "未定義の形です。"); }
            if (spawn.Speed < 0 || spawn.Speed > spawn.MaxSpeed || spawn.Life < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(spawn), "速さは 0 以上・上限以下に、寿命は 0 以上にしてください。");
            }

            if (Count == Capacity)
            {
                Dropped++;
                return false;
            }

            var i = Count++;
            _x[i] = (long)spawn.Origin.X << PositionShift;
            _y[i] = (long)spawn.Origin.Y << PositionShift;
            _startX[i] = spawn.Origin.X;
            _startY[i] = spawn.Origin.Y;
            _angle[i] = spawn.Angle;
            _speed[i] = spawn.Speed;
            _accel[i] = spawn.Accel;
            _maxSpeed[i] = spawn.MaxSpeed;
            _curve[i] = spawn.Curve;
            _age[i] = 0;
            _life[i] = spawn.Life;
            _shape[i] = spawn.Shape;
            _color[i] = spawn.Color;
            Spawned++;
            return true;
        }

        /// <summary>1 Tick 進める：消える弾を取り除いてから、残りの弾をすべて 1 回動かす。</summary>
        public void Step()
        {
            RemoveFinished();
            for (var i = 0; i < Count; i++)
            {
                _startX[i] = (int)(_x[i] >> PositionShift);
                _startY[i] = (int)(_y[i] >> PositionShift);
                _age[i]++;
                var angle = Trig.Normalize(_angle[i] + _curve[i]);
                _angle[i] = angle;
                var speed = _speed[i] + _accel[i];
                speed = speed < 0 ? 0 : speed > _maxSpeed[i] ? _maxSpeed[i] : speed;
                _speed[i] = speed;
                _x[i] += (long)speed * Trig.Cos(angle);
                _y[i] += (long)speed * Trig.Sin(angle);
            }
        }

        /// <summary>すべての弾を消す（被弾・ボム・弾幕の切り替え）。</summary>
        public void Clear() => Count = 0;

        /// <summary>この Tick の移動の始めの位置（<see cref="Field.Unit"/> 単位）。</summary>
        public Vec2i StartOf(int index)
        {
            CheckIndex(index);
            return new Vec2i(_startX[index], _startY[index]);
        }

        /// <summary>今の位置（<see cref="Field.Unit"/> 単位。内部の位置を切り捨てる）。</summary>
        public Vec2i PositionOf(int index)
        {
            CheckIndex(index);
            return new Vec2i((int)(_x[index] >> PositionShift), (int)(_y[index] >> PositionShift));
        }

        public int AngleOf(int index)
        {
            CheckIndex(index);
            return _angle[index];
        }

        public int SpeedOf(int index)
        {
            CheckIndex(index);
            return _speed[index];
        }

        /// <summary>動いた回数。</summary>
        public int AgeOf(int index)
        {
            CheckIndex(index);
            return _age[index];
        }

        public BulletShape ShapeOf(int index)
        {
            CheckIndex(index);
            return _shape[index];
        }

        public BulletColor ColorOf(int index)
        {
            CheckIndex(index);
            return _color[index];
        }

        public int RadiusOf(int index) => BulletShapes.Radius(ShapeOf(index));

        /// <summary>中身を写した別の置き場（先読み・セーブ用。共有しない）。</summary>
        public BulletPool Clone()
        {
            var copy = new BulletPool(Capacity);
            copy.CopyFrom(this);
            return copy;
        }

        /// <summary>
        /// <paramref name="source"/> の中身をこの置き場へ写す（操縦役の先読みで、毎回の割り当てをせずに作業用の置き場を使い回す）。
        /// 容量は同じでなければならない。
        /// </summary>
        public void CopyFrom(BulletPool source)
        {
            if (source == null) { throw new ArgumentNullException(nameof(source)); }
            if (source.Capacity != Capacity) { throw new ArgumentException("容量が違う置き場からは写せません。", nameof(source)); }
            Count = source.Count;
            Dropped = source.Dropped;
            Spawned = source.Spawned;
            Array.Copy(source._x, _x, Count);
            Array.Copy(source._y, _y, Count);
            Array.Copy(source._startX, _startX, Count);
            Array.Copy(source._startY, _startY, Count);
            Array.Copy(source._angle, _angle, Count);
            Array.Copy(source._speed, _speed, Count);
            Array.Copy(source._accel, _accel, Count);
            Array.Copy(source._maxSpeed, _maxSpeed, Count);
            Array.Copy(source._curve, _curve, Count);
            Array.Copy(source._age, _age, Count);
            Array.Copy(source._life, _life, Count);
            Array.Copy(source._shape, _shape, Count);
            Array.Copy(source._color, _color, Count);
        }

        /// <summary>以後の動きを決める状態をすべて混ぜる（022 の知見）。</summary>
        public void MixInto(ref StateHash hash)
        {
            hash.Mix(Count);
            hash.Mix(Dropped);
            hash.Mix(Spawned);
            for (var i = 0; i < Count; i++)
            {
                hash.Mix(_x[i]);
                hash.Mix(_y[i]);
                hash.Mix(_startX[i]);
                hash.Mix(_startY[i]);
                hash.Mix(_angle[i]);
                hash.Mix(_speed[i]);
                hash.Mix(_accel[i]);
                hash.Mix(_maxSpeed[i]);
                hash.Mix(_curve[i]);
                hash.Mix(_age[i]);
                hash.Mix(_life[i]);
                hash.Mix((int)_shape[i]);
                hash.Mix((int)_color[i]);
            }
        }

        private void RemoveFinished()
        {
            var bounds = Field.BulletBounds;
            var write = 0;
            for (var read = 0; read < Count; read++)
            {
                var expired = _life[read] > 0 && _age[read] >= _life[read];
                var position = new Vec2i((int)(_x[read] >> PositionShift), (int)(_y[read] >> PositionShift));
                if (expired || !bounds.Contains(position)) { continue; }
                if (write != read) { Move(read, write); }
                write++;
            }

            Count = write;
        }

        private void Move(int from, int to)
        {
            _x[to] = _x[from];
            _y[to] = _y[from];
            _startX[to] = _startX[from];
            _startY[to] = _startY[from];
            _angle[to] = _angle[from];
            _speed[to] = _speed[from];
            _accel[to] = _accel[from];
            _maxSpeed[to] = _maxSpeed[from];
            _curve[to] = _curve[from];
            _age[to] = _age[from];
            _life[to] = _life[from];
            _shape[to] = _shape[from];
            _color[to] = _color[from];
        }

        private void CheckIndex(int index)
        {
            if ((uint)index >= (uint)Count) { throw new ArgumentOutOfRangeException(nameof(index), index, "弾の番号が範囲外です。"); }
        }
    }
}
