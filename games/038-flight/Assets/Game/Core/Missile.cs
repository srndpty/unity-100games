using TileMotion;

namespace Game.Core
{
    /// <summary>
    /// ミサイルの運動。誘導が求めた加速のうち速度に直交する成分だけを、<c>maxTurn</c> まで使って曲げる。
    /// 速さは <c>accel</c> ずつ <c>maxSpeed</c> まで上げ、曲げても変えない。重力は受けない。
    /// </summary>
    public static class MissileBody
    {
        /// <summary>要求の大きさの上限（重力の約 37 万倍）。運動の上限はこれより十分小さい。</summary>
        private const long CommandLimit = 1_000_000_000;

        /// <summary>1 Tick 進めた速度を返す（位置はこの速度だけ進める）。</summary>
        public static Vec3l Step(Vec3l velocity, Vec3l command, long maxTurn, long accel, long maxSpeed)
        {
            var speed = velocity.Length;
            if (speed == 0) { return velocity; }

            // 大きすぎる要求は向きを保って縮める（上限で切るので大きさは意味を持たない）。
            while (command.Length > CommandLimit) { command = new Vec3l(command.X / 2, command.Y / 2, command.Z / 2); }

            // 速度に直交する成分だけを取り出す：c - v (c·v) / |v|²。比は単位に依らないので、内積は mm 単位で取る。
            var v = velocity;
            var along = Vec3l.Dot(Millimeters(command), Millimeters(v));
            var vv = Millimeters(v).LengthSquared;
            var lateral = command;
            if (vv > 0) { lateral -= Vec3l.MulDiv(v, along, vv); }

            var magnitude = lateral.Length;
            if (magnitude > maxTurn) { lateral = Vec3l.MulDiv(lateral, maxTurn, magnitude); }

            var next = v + lateral;
            var nextSpeed = speed + accel > maxSpeed ? maxSpeed : speed + accel;
            var length = next.Length;
            return length == 0 ? velocity : Vec3l.MulDiv(next, nextSpeed, length);
        }

        private static Vec3l Millimeters(Vec3l value) =>
            new(value.X / Units.PerMillimeter, value.Y / Units.PerMillimeter, value.Z / Units.PerMillimeter);
    }

    /// <summary>なぜミサイルが消えたか。</summary>
    public enum MissileEnd
    {
        None,
        Hit,
        Expired,
        Ground,
    }

    /// <summary>飛んでいる 1 発。番号は発射の順。</summary>
    public sealed class Missile
    {
        internal Missile(int id, int owner, int target, Vec3l position, Vec3l velocity, long maxTurn, long maxSpeed)
        {
            Id = id;
            Owner = owner;
            Target = target;
            Position = position;
            Velocity = velocity;
            MaxTurn = maxTurn;
            MaxSpeed = maxSpeed;
            Tracking = true;
            Active = true;
        }

        public int Id { get; }

        /// <summary>撃った機体の番号（0 がプレイヤー）。</summary>
        public int Owner { get; }

        /// <summary>目標の機体の番号。</summary>
        public int Target { get; }

        public Vec3l Position { get; internal set; }

        public Vec3l Velocity { get; internal set; }

        /// <summary>横向きの加速の上限。</summary>
        public long MaxTurn { get; }

        /// <summary>最高速度。</summary>
        public long MaxSpeed { get; }

        /// <summary>飛んでからの Tick 数。</summary>
        public int Age { get; internal set; }

        /// <summary>目標を追っているか。目標が撃墜されたら false になり、以後は直進する。</summary>
        public bool Tracking { get; internal set; }

        /// <summary>まだ飛んでいるか。</summary>
        public bool Active { get; internal set; }

        public MissileEnd End { get; internal set; }

        internal Missile Clone() => (Missile)MemberwiseClone();

        internal void Mix(ref StateHash hash)
        {
            hash.Mix(Id);
            hash.Mix(Owner);
            hash.Mix(Target);
            hash.Mix(Position.X);
            hash.Mix(Position.Y);
            hash.Mix(Position.Z);
            hash.Mix(Velocity.X);
            hash.Mix(Velocity.Y);
            hash.Mix(Velocity.Z);
            hash.Mix(Age);
            hash.Mix(Tracking);
            hash.Mix(Active);
            hash.Mix((int)End);
        }
    }
}
