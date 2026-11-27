using System;
using Game.Core.Geometry;

namespace Game.Core.Play
{
    public enum CommandKind
    {
        None,

        /// <summary>壁の視点で隣の壁へ向き直る（A：+1 右・-1 左）。</summary>
        Turn,

        /// <summary>壁の視点から近景へ寄る（A：視点）。</summary>
        Focus,

        /// <summary>一段戻る（手に取った物を置く → 近景から壁へ）。</summary>
        Back,

        /// <summary>部品を操作する（A：部品）。</summary>
        Operate,

        /// <summary>持ち物を部品に使う（A：持ち物・B：部品）。</summary>
        UseItem,

        /// <summary>物を拾う（A：部品）。</summary>
        Take,

        /// <summary>部屋の物を手に取る（A：部品）。</summary>
        Inspect,

        /// <summary>持ち物を手に取る（A：持ち物）。</summary>
        InspectItem,

        /// <summary>手に取った物を回す（A：ヨーの段 ±1・B：ピッチの段 ±1）。</summary>
        Rotate,

        /// <summary>次の一手のヒント。</summary>
        Hint,

        /// <summary>同じシードで最初から。</summary>
        Reset,
    }

    /// <summary>世界への命令。状態は <see cref="World.Apply"/> だけで変わる。</summary>
    public readonly struct Command : IEquatable<Command>
    {
        private Command(CommandKind kind, int a = -1, int b = -1)
        {
            Kind = kind;
            A = a;
            B = b;
        }

        public CommandKind Kind { get; }
        public int A { get; }
        public int B { get; }

        public bool IsNone => Kind == CommandKind.None;

        public static Command None => default;

        public static Command Turn(int direction) => new(CommandKind.Turn, direction);

        public static Command Focus(int view) => new(CommandKind.Focus, view);

        public static Command Back => new(CommandKind.Back);

        public static Command Operate(int part) => new(CommandKind.Operate, part);

        public static Command UseItem(int item, int part) => new(CommandKind.UseItem, item, part);

        public static Command Take(int part) => new(CommandKind.Take, part);

        public static Command Inspect(int part) => new(CommandKind.Inspect, part);

        public static Command InspectItem(int item) => new(CommandKind.InspectItem, item);

        public static Command Rotate(int yaw, int pitch) => new(CommandKind.Rotate, yaw, pitch);

        public static Command Hint => new(CommandKind.Hint);

        public static Command Reset => new(CommandKind.Reset);

        public static bool operator ==(Command left, Command right) => left.Equals(right);

        public static bool operator !=(Command left, Command right) => !left.Equals(right);

        public bool Equals(Command other) => Kind == other.Kind && A == other.A && B == other.B;

        public override bool Equals(object? obj) => obj is Command other && Equals(other);

        public override int GetHashCode() => unchecked((((int)Kind * 397) ^ A) * 397) ^ B;

        public override string ToString() => $"{Kind}({A}, {B})";
    }

    public enum Refusal
    {
        None,

        /// <summary>脱出した後。</summary>
        Escaped,

        /// <summary>手に取って見ている間に、回す・戻る・ヒント以外をした。</summary>
        Holding,

        /// <summary>何も手に取っていないのに回した。</summary>
        NotHolding,

        /// <summary>壁の視点でない（向き直る・寄る）、または寄る先がこの壁にない。</summary>
        NotOnWall,

        /// <summary>今の近景の部品ではない。</summary>
        NotInView,

        /// <summary>ない・中に入っていて触れない・もう拾った。</summary>
        NotHere,

        /// <summary>壁の視点で戻った。</summary>
        NothingToBack,

        /// <summary>持っていない持ち物。</summary>
        NotHeld,

        /// <summary>その持ち物は合わない。</summary>
        WrongItem,

        /// <summary>仕掛けに必要な持ち物が入っていない。</summary>
        Locked,

        /// <summary>もう持ち物が入っている。</summary>
        AlreadyInstalled,

        /// <summary>もう解けている。</summary>
        AlreadySolved,

        /// <summary>その部品にその操作はない。</summary>
        NotAllowed,

        /// <summary>番号や向きが範囲外。</summary>
        OutOfRange,
    }

    /// <summary>命令の結果。拒否なら世界の状態（ハッシュ・操作の回数を含む）は何も変わっていない。</summary>
    public readonly struct Outcome
    {
        private Outcome(Refusal reason) => Reason = reason;

        public Refusal Reason { get; }
        public bool Accepted => Reason == Refusal.None;

        public static Outcome Ok => default;

        public static Outcome Refuse(Refusal reason) => new(reason);

        public override string ToString() => Accepted ? "Ok" : Reason.ToString();
    }

    public enum EventKind
    {
        Turned,
        Focused,
        Backed,

        /// <summary>部品を動かした（Index：部品）。</summary>
        Operated,

        /// <summary>順番を間違えて最初からになった（Index：仕掛け）。</summary>
        Mistake,

        /// <summary>持ち物を入れた（Index：仕掛け）。</summary>
        Installed,

        /// <summary>仕掛けが解けて開いた（Index：仕掛け）。</summary>
        Solved,

        /// <summary>拾った（Index：持ち物）。</summary>
        ItemTaken,

        /// <summary>持ち物を使って手放した（Index：持ち物）。</summary>
        ItemUsed,

        /// <summary>手に取った（Index：手に取る物）。</summary>
        Inspected,

        /// <summary>手に取った物を置いた（Index：手に取る物）。</summary>
        PutDown,

        /// <summary>回した（Index：手に取る物）。</summary>
        Rotated,

        /// <summary>回して手がかりの面がこちらを向いた（Index：手に取る物）。</summary>
        ClueShown,

        /// <summary>ヒント（Index：段階 0〜2、Text：文）。</summary>
        Hint,

        Escaped,
        Reset,
    }

    /// <summary>命令の結果の出来事。音と表示は出来事から作る。</summary>
    public readonly struct EscapeEvent
    {
        public EscapeEvent(EventKind kind, int index = -1, string text = "")
        {
            Kind = kind;
            Index = index;
            Text = text;
        }

        public EventKind Kind { get; }
        public int Index { get; }
        public string Text { get; }

        public override string ToString() => $"{Kind}({Index}{(Text.Length > 0 ? ", " + Text : "")})";
    }

    /// <summary>
    /// 手に取った物の向き（ヨー 45° × 8 段・ピッチ 90° × 4 段）。物の座標のベクトルは、先にヨー（y 軸まわり）、
    /// 次にピッチ（x 軸まわり）で回し、手に取った空間（カメラと同じ向き、奥が +z）へ写す。
    /// Unity では AngleAxis(ピッチ × 90, right) × AngleAxis(ヨー × 45, up) を手に取った物の回転にする。
    /// </summary>
    public readonly struct Orientation : IEquatable<Orientation>
    {
        public const int YawSteps = 8;
        public const int PitchSteps = 4;

        public Orientation(int yaw, int pitch)
        {
            Yaw = ((yaw % YawSteps) + YawSteps) % YawSteps;
            Pitch = ((pitch % PitchSteps) + PitchSteps) % PitchSteps;
        }

        public int Yaw { get; }
        public int Pitch { get; }

        public Orientation Rotated(int yaw, int pitch) => new(Yaw + yaw, Pitch + pitch);

        /// <summary>物の座標の面の法線（軸に平行な単位ベクトル）が、手前（-z、見ている人の方）を向いているか。</summary>
        public bool Faces(Vec3i normal)
        {
            var horizontal = normal.X != 0 || normal.Z != 0;

            // 45° の段では水平な法線は斜めになり、どの軸とも一致しない。
            if (horizontal && (Yaw & 1) == 1) { return false; }
            var v = CameraPose.Yaw(normal, Yaw / 2);
            for (int i = 0; i < Pitch; i++) { v = new Vec3i(v.X, -v.Z, v.Y); }
            return v == new Vec3i(0, 0, -1);
        }

        public bool Equals(Orientation other) => Yaw == other.Yaw && Pitch == other.Pitch;

        public override bool Equals(object? obj) => obj is Orientation other && Equals(other);

        public override int GetHashCode() => (Yaw * 8) + Pitch;

        public static bool operator ==(Orientation left, Orientation right) => left.Equals(right);

        public static bool operator !=(Orientation left, Orientation right) => !left.Equals(right);
    }
}
