using System;
using IntGeometry;

namespace Game.Core
{
    /// <summary>選手の行動。</summary>
    public enum PlayerAction
    {
        None,

        /// <summary>地を這うパス（目標の点へ）。球を持っているときだけ。</summary>
        Pass,

        /// <summary>浮き球のパス（目標の点へ）。球を持っているときだけ。</summary>
        Lob,

        /// <summary>シュート（目標の点へ低い浮き球）。球を持っているときだけ。</summary>
        Shoot,

        /// <summary>タックル（相手の持つ球を弾く）。球を持っていないときだけ。</summary>
        Tackle,
    }

    /// <summary>1 Tick の選手への命令：移動の向き（長さは問わない、0 なら止まる）・行動・行動の目標の点。</summary>
    public readonly struct PlayerCommand : IEquatable<PlayerCommand>
    {
        public PlayerCommand(Vec2i move, PlayerAction action = PlayerAction.None, Vec2i target = default)
        {
            Move = move;
            Action = action;
            Target = target;
        }

        public Vec2i Move { get; }
        public PlayerAction Action { get; }
        public Vec2i Target { get; }

        public static PlayerCommand Idle => default;

        public bool Equals(PlayerCommand other) => Move == other.Move && Action == other.Action && Target == other.Target;

        public override bool Equals(object? obj) => obj is PlayerCommand other && Equals(other);

        public override int GetHashCode() => unchecked((Move.GetHashCode() * 397) ^ ((int)Action * 31) ^ Target.GetHashCode());

        public static bool operator ==(PlayerCommand left, PlayerCommand right) => left.Equals(right);

        public static bool operator !=(PlayerCommand left, PlayerCommand right) => !left.Equals(right);

        public override string ToString() => $"{Move} {Action} {Target}";
    }

    /// <summary>選手。位置・速度・向き（長さ 1000 の向き）と、動けない・止められない残り Tick を持つ。</summary>
    public sealed class Footballer
    {
        public const int FacingLength = 1000;

        internal Footballer(int index, Team team, bool isKeeper)
        {
            Index = index;
            Team = team;
            IsKeeper = isKeeper;
            Facing = new Vec2i(Pitch.Direction(team) * FacingLength, 0);
        }

        public int Index { get; }
        public Team Team { get; }
        public bool IsKeeper { get; }

        public Vec2i Position { get; internal set; }
        public Vec2i Velocity { get; internal set; }

        /// <summary>向き（長さ 1000）。ドリブルの球はこの向きに置く。</summary>
        public Vec2i Facing { get; internal set; }

        /// <summary>タックルの後・奪われた後に動けない残り Tick。</summary>
        public int Recover { get; internal set; }

        /// <summary>蹴った後に球を止められない残り Tick。</summary>
        public int KickCooldown { get; internal set; }

        public bool CanAct => Recover == 0;

        public Footballer Clone() => (Footballer)MemberwiseClone();
    }
}
