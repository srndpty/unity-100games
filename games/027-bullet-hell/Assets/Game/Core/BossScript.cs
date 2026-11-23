using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Game.Core
{
    /// <summary>固定値か、<c>rand(a, b)</c>（両端を含む一様な整数）の値。</summary>
    public readonly struct Ranged : IEquatable<Ranged>
    {
        public Ranged(int min, int max)
        {
            if (max < min) { throw new ArgumentOutOfRangeException(nameof(max), "最大は最小以上にしてください。"); }
            Min = min;
            Max = max;
        }

        public int Min { get; }

        public int Max { get; }

        public bool IsFixed => Min == Max;

        public static Ranged Fixed(int value) => new(value, value);

        /// <summary>値を 1 つ選ぶ。固定値なら乱数を使わない（rand を足しても他の値の乱数の使い方が変わらない）。</summary>
        public int Pick(SeededRandom random) => IsFixed ? Min : random.Range(Min, Max);

        public bool Equals(Ranged other) => Min == other.Min && Max == other.Max;

        public override bool Equals(object? obj) => obj is Ranged other && Equals(other);

        public override int GetHashCode() => unchecked((Min * 397) ^ Max);

        public static bool operator ==(Ranged left, Ranged right) => left.Equals(right);

        public static bool operator !=(Ranged left, Ranged right) => !left.Equals(right);

        public override string ToString() => IsFixed ? Min.ToString(System.Globalization.CultureInfo.InvariantCulture) : $"rand({Min}, {Max})";
    }

    public enum EmitterKind
    {
        /// <summary>全周に等間隔。</summary>
        Ring,

        /// <summary>決まった向きを中心に扇形。</summary>
        Fan,

        /// <summary>自機の向きを中心に扇形。</summary>
        Aimed,
    }

    /// <summary>
    /// 発射器 1 つ。弾幕の開始から <see cref="Delay"/> Tick 後に 1 回目を撃ち、以後 <see cref="Every"/> Tick ごとに撃つ。
    /// n 回目（0 始まり）の中心の向き = <see cref="Angle"/> + n × <see cref="Spin"/>（<see cref="EmitterKind.Aimed"/> ならさらに自機の向きを足す）。
    /// 位置のずれと向きは 1 回の発射で 1 回選び、速さ・加速・角速度は弾ごとに選ぶ。
    /// </summary>
    public sealed class EmitterSpec
    {
        public EmitterSpec(
            EmitterKind kind,
            int every,
            int delay,
            int count,
            Ranged angle,
            int spread,
            int spin,
            Ranged speed,
            Ranged accel,
            int maxSpeed,
            Ranged curve,
            Ranged offsetX,
            Ranged offsetY,
            int life,
            BulletShape shape,
            BulletColor color)
        {
            if (every < 1) { throw new ArgumentOutOfRangeException(nameof(every), every, "1 以上にしてください。"); }
            if (delay < 0) { throw new ArgumentOutOfRangeException(nameof(delay), delay, "0 以上にしてください。"); }
            if (count < 1) { throw new ArgumentOutOfRangeException(nameof(count), count, "1 以上にしてください。"); }
            if (speed.Min < 0 || speed.Max > maxSpeed) { throw new ArgumentOutOfRangeException(nameof(speed), "速さは 0 以上・上限以下にしてください。"); }
            if (life < 0) { throw new ArgumentOutOfRangeException(nameof(life), life, "0 以上にしてください。"); }
            if (!BulletShapes.IsDefined(shape)) { throw new ArgumentOutOfRangeException(nameof(shape), shape, "未定義の形です。"); }
            if (!BulletShapes.IsDefined(color)) { throw new ArgumentOutOfRangeException(nameof(color), color, "未定義の色です。"); }

            Kind = kind;
            Every = every;
            Delay = delay;
            Count = count;
            Angle = angle;
            Spread = spread;
            Spin = spin;
            Speed = speed;
            Accel = accel;
            MaxSpeed = maxSpeed;
            Curve = curve;
            OffsetX = offsetX;
            OffsetY = offsetY;
            Life = life;
            Shape = shape;
            Color = color;
        }

        public EmitterKind Kind { get; }

        public int Every { get; }

        public int Delay { get; }

        public int Count { get; }

        public Ranged Angle { get; }

        /// <summary>扇の広がり（両端の弾の向きの差）。<see cref="EmitterKind.Ring"/> では使わない。</summary>
        public int Spread { get; }

        public int Spin { get; }

        public Ranged Speed { get; }

        public Ranged Accel { get; }

        public int MaxSpeed { get; }

        public Ranged Curve { get; }

        public Ranged OffsetX { get; }

        public Ranged OffsetY { get; }

        public int Life { get; }

        public BulletShape Shape { get; }

        public BulletColor Color { get; }
    }

    public enum MoveKind
    {
        /// <summary>決まった点へ一定の速さで動く。</summary>
        To,

        /// <summary>その場で待つ。</summary>
        Wait,

        /// <summary>範囲の中の乱数の点へ動く。</summary>
        Wander,
    }

    /// <summary>ボスの動き 1 つ。<see cref="Ticks"/> Tick かけて動き、弾幕の間は動きの列を繰り返す。</summary>
    public sealed class MoveSpec
    {
        public MoveSpec(MoveKind kind, Vec2i target, Rect area, int ticks)
        {
            if (ticks < 1) { throw new ArgumentOutOfRangeException(nameof(ticks), ticks, "1 以上にしてください。"); }
            Kind = kind;
            Target = target;
            Area = area;
            Ticks = ticks;
        }

        public MoveKind Kind { get; }

        /// <summary><see cref="MoveKind.To"/> の行き先（<see cref="Field.Unit"/> 単位）。</summary>
        public Vec2i Target { get; }

        /// <summary><see cref="MoveKind.Wander"/> の範囲（<see cref="Field.Unit"/> 単位）。</summary>
        public Rect Area { get; }

        public int Ticks { get; }
    }

    /// <summary>弾幕 1 つ。体力が 0 になるか制限時間が過ぎたら終わる。</summary>
    public sealed class PatternSpec
    {
        public PatternSpec(string name, int hp, int timeTicks, IList<MoveSpec> moves, IList<EmitterSpec> emitters)
        {
            if (string.IsNullOrEmpty(name)) { throw new ArgumentException("名前を付けてください。", nameof(name)); }
            if (hp < 1) { throw new ArgumentOutOfRangeException(nameof(hp), hp, "1 以上にしてください。"); }
            if (timeTicks < 1) { throw new ArgumentOutOfRangeException(nameof(timeTicks), timeTicks, "1 以上にしてください。"); }
            if (moves == null) { throw new ArgumentNullException(nameof(moves)); }
            if (emitters == null || emitters.Count == 0) { throw new ArgumentException("発射器を 1 つ以上にしてください。", nameof(emitters)); }

            Name = name;
            Hp = hp;
            TimeTicks = timeTicks;
            Moves = Array.AsReadOnly(Copy(moves));
            Emitters = Array.AsReadOnly(Copy(emitters));
        }

        public string Name { get; }

        public int Hp { get; }

        public int TimeTicks { get; }

        public ReadOnlyCollection<MoveSpec> Moves { get; }

        public ReadOnlyCollection<EmitterSpec> Emitters { get; }

        private static T[] Copy<T>(IList<T> source)
        {
            var copy = new T[source.Count];
            source.CopyTo(copy, 0);
            return copy;
        }
    }

    /// <summary>ボス 1 体の台本（DSL のファイル 1 つ）。</summary>
    public sealed class BossScript
    {
        public BossScript(string name, int radius, IList<PatternSpec> patterns)
        {
            if (string.IsNullOrEmpty(name)) { throw new ArgumentException("名前を付けてください。", nameof(name)); }
            if (radius < 1) { throw new ArgumentOutOfRangeException(nameof(radius), radius, "1 以上にしてください。"); }
            if (patterns == null || patterns.Count == 0) { throw new ArgumentException("弾幕を 1 つ以上にしてください。", nameof(patterns)); }

            Name = name;
            Radius = radius;
            var copy = new PatternSpec[patterns.Count];
            patterns.CopyTo(copy, 0);
            Patterns = Array.AsReadOnly(copy);
        }

        public string Name { get; }

        /// <summary>当たりの半径（<see cref="Field.Unit"/> 単位）。</summary>
        public int Radius { get; }

        public ReadOnlyCollection<PatternSpec> Patterns { get; }
    }
}
