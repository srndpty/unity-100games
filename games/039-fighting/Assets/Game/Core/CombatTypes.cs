using System;
using IntGeometry;

namespace Game.Core
{
    public enum FighterState
    {
        Idle,
        Walk,
        Crouch,
        JumpPre,
        Air,
        Land,
        Attack,
        HitStun,
        BlockStun,
        Thrown,
        Knockdown,
        Wakeup,
        Ko,
    }

    public enum MovePhase
    {
        None,
        Startup,
        Active,
        Recovery,
    }

    public enum BoxKind
    {
        Push,
        Hurt,
        Hit,
        Throw,
        Throwable,
    }

    public enum StrikeHeight
    {
        High,
        Mid,
        Low,
        Throw,
    }

    public enum AttackButton
    {
        None,
        Light,
        Heavy,
        Sweep,
    }

    public enum BodyPose
    {
        Stand,
        Crouch,
        Air,
        Down,
    }

    /// <summary>原点（足元の中央）からの箱。向きは右が正。幅と高さは 1 以上。</summary>
    public readonly struct LocalBox : IEquatable<LocalBox>
    {
        public LocalBox(BoxKind kind, int x, int y, int w, int h)
        {
            if (w <= 0 || h <= 0) { throw new ArgumentOutOfRangeException(nameof(w)); }
            if (x < -2_000_000 || y < -2_000_000 || x > 2_000_000 || y > 2_000_000)
            {
                throw new ArgumentOutOfRangeException(nameof(x));
            }

            if (w > 2_000_000 || h > 2_000_000) { throw new ArgumentOutOfRangeException(nameof(w)); }
            Kind = kind;
            X = x;
            Y = y;
            W = w;
            H = h;
        }

        public BoxKind Kind { get; }

        public int X { get; }

        public int Y { get; }

        public int W { get; }

        public int H { get; }

        public Rect At(int originX, int originY, int facing)
        {
            int minX = facing >= 0 ? originX + X : originX - (X + W);
            int minY = originY + Y;
            return new Rect(minX, minY, minX + W, minY + H);
        }

        public bool Equals(LocalBox other) => Kind == other.Kind && X == other.X && Y == other.Y && W == other.W && H == other.H;

        public override bool Equals(object? obj) => obj is LocalBox other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(Kind, X, Y, W, H);

        public static bool operator ==(LocalBox left, LocalBox right) => left.Equals(right);

        public static bool operator !=(LocalBox left, LocalBox right) => !left.Equals(right);
    }

    public readonly struct WorldBox : IEquatable<WorldBox>
    {
        public WorldBox(BoxKind kind, Rect rect)
        {
            Kind = kind;
            Rect = rect;
        }

        public BoxKind Kind { get; }

        public Rect Rect { get; }

        public bool Equals(WorldBox other) => Kind == other.Kind && Rect.Equals(other.Rect);

        public override bool Equals(object? obj) => obj is WorldBox other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(Kind, Rect);

        public static bool operator ==(WorldBox left, WorldBox right) => left.Equals(right);

        public static bool operator !=(WorldBox left, WorldBox right) => !left.Equals(right);
    }

    /// <summary>1 Tick のパッド。方向はテンキー（5 が中立）。ボタンは押しているか。</summary>
    public readonly struct Pad : IEquatable<Pad>
    {
        public Pad(int direction, bool light, bool heavy, bool sweep)
        {
            if (direction < 1 || direction > 9) { throw new ArgumentOutOfRangeException(nameof(direction)); }
            Direction = direction;
            Light = light;
            Heavy = heavy;
            Sweep = sweep;
        }

        public static Pad Neutral { get; } = new Pad(5, false, false, false);

        public int Direction { get; }

        public bool Light { get; }

        public bool Heavy { get; }

        public bool Sweep { get; }

        public static int AxisX(int direction) => direction % 3 == 0 ? 1 : direction % 3 == 1 ? -1 : 0;

        public static int AxisY(int direction) => direction >= 7 ? 1 : direction <= 3 ? -1 : 0;

        public bool Equals(Pad other) =>
            Direction == other.Direction && Light == other.Light && Heavy == other.Heavy && Sweep == other.Sweep;

        public override bool Equals(object? obj) => obj is Pad other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(Direction, Light, Heavy, Sweep);

        public static bool operator ==(Pad left, Pad right) => left.Equals(right);

        public static bool operator !=(Pad left, Pad right) => !left.Equals(right);
    }

    public readonly struct FrameRow : IEquatable<FrameRow>
    {
        public FrameRow(FighterState state, MovePhase phase, bool hit, bool hurt, bool push, bool throwable, bool frozen)
        {
            State = state;
            Phase = phase;
            Hit = hit;
            Hurt = hurt;
            Push = push;
            Throwable = throwable;
            Frozen = frozen;
        }

        public FighterState State { get; }

        public MovePhase Phase { get; }

        public bool Hit { get; }

        public bool Hurt { get; }

        public bool Push { get; }

        public bool Throwable { get; }

        public bool Frozen { get; }

        public bool Equals(FrameRow other) =>
            State == other.State && Phase == other.Phase && Hit == other.Hit && Hurt == other.Hurt
            && Push == other.Push && Throwable == other.Throwable && Frozen == other.Frozen;

        public override bool Equals(object? obj) => obj is FrameRow other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(State, Phase, Hit, Hurt, Push, Throwable, Frozen);

        public static bool operator ==(FrameRow left, FrameRow right) => left.Equals(right);

        public static bool operator !=(FrameRow left, FrameRow right) => !left.Equals(right);
    }

    /// <summary>画面に出す飛び道具。</summary>
    public readonly struct Shot : IEquatable<Shot>
    {
        public Shot(int owner, int x, int y, int facing, Rect hit)
        {
            Owner = owner;
            X = x;
            Y = y;
            Facing = facing;
            Hit = hit;
        }

        public int Owner { get; }

        public int X { get; }

        public int Y { get; }

        public int Facing { get; }

        public Rect Hit { get; }

        public bool Equals(Shot other) => Owner == other.Owner && X == other.X && Y == other.Y && Facing == other.Facing && Hit.Equals(other.Hit);

        public override bool Equals(object? obj) => obj is Shot other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(Owner, X, Y, Facing, Hit);

        public static bool operator ==(Shot left, Shot right) => left.Equals(right);

        public static bool operator !=(Shot left, Shot right) => !left.Equals(right);
    }
}
