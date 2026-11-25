using TileMotion;

namespace Game.Core
{
    /// <summary>
    /// 戦う 1 機（0 がプレイヤー、1〜3 が敵）。飛行状態・耐久・残弾・再装填・ロックオン・敵の操縦の状態を持つ。
    /// 外からは読み取りだけで、変えるのは <see cref="Mission"/>。
    /// </summary>
    public sealed class Fighter
    {
        internal Fighter(int index, Aircraft plane, int health, int ammo)
        {
            Index = index;
            Plane = plane;
            Health = health;
            Ammo = ammo;
            LockTarget = -1;
        }

        public int Index { get; }

        public bool IsPlayer => Index == 0;

        /// <summary>飛行状態（読み取り専用の窓口）。</summary>
        public AircraftView View => new(Plane);

        internal Aircraft Plane { get; private set; }

        public int Health { get; internal set; }

        /// <summary>撃墜されたか、墜落したか。</summary>
        public bool Down { get; internal set; }

        /// <summary>撃墜・墜落した Tick（まだなら -1）。爆発の描画に使う。</summary>
        public int DownTick { get; internal set; } = -1;

        /// <summary>直近に被弾した Tick（まだなら -1）。</summary>
        public int HitTick { get; internal set; } = -1;

        /// <summary>ミサイルで撃墜された（墜落ではない）。</summary>
        public bool ShotDown { get; internal set; }

        public int Ammo { get; internal set; }

        /// <summary>次を撃てるまでの残り Tick。</summary>
        public int Reload { get; internal set; }

        /// <summary>ロックオンしている（しようとしている）目標の番号。なければ -1。</summary>
        public int LockTarget { get; internal set; }

        /// <summary>目標が条件を満たし続けた Tick 数。</summary>
        public int LockTicks { get; internal set; }

        /// <summary>敵：交戦を始めたか。</summary>
        public bool Engaged { get; internal set; }

        /// <summary>敵：攻撃役として追い続けた Tick 数。</summary>
        public int PursuitTicks { get; internal set; }

        /// <summary>敵：離脱の残り Tick（0 なら離脱していない）。</summary>
        public int BreakTicks { get; internal set; }

        /// <summary>敵：巡回の円の中心。</summary>
        public Vec3l PatrolCenter { get; internal set; }

        /// <summary>敵：狙いのぶれ。</summary>
        public Vec3l AimJitter { get; internal set; }

        internal Fighter Clone()
        {
            var copy = (Fighter)MemberwiseClone();
            copy.Plane = Plane.Clone();
            return copy;
        }

        internal void Mix(ref StateHash hash)
        {
            hash.Mix(Index);
            Plane.Mix(ref hash);
            hash.Mix(Health);
            hash.Mix(Down);
            hash.Mix(ShotDown);
            hash.Mix(DownTick);
            hash.Mix(HitTick);
            hash.Mix(Ammo);
            hash.Mix(Reload);
            hash.Mix(LockTarget);
            hash.Mix(LockTicks);
            hash.Mix(Engaged);
            hash.Mix(PursuitTicks);
            hash.Mix(BreakTicks);
            hash.Mix(PatrolCenter.X);
            hash.Mix(PatrolCenter.Y);
            hash.Mix(PatrolCenter.Z);
            hash.Mix(AimJitter.X);
            hash.Mix(AimJitter.Y);
            hash.Mix(AimJitter.Z);
        }
    }

    /// <summary><see cref="Aircraft"/> の読み取り専用の窓口（キャストしても書き換えられない）。</summary>
    public readonly struct AircraftView
    {
        private readonly Aircraft _plane;

        internal AircraftView(Aircraft plane)
        {
            _plane = plane;
        }

        public Vec3l Position => _plane.Position;

        public Vec3l Velocity => _plane.Velocity;

        public Quat Attitude => _plane.Attitude;

        public int Throttle => _plane.Throttle;

        public bool Grounded => _plane.Grounded;

        public bool Crashed => _plane.Crashed;

        public AirData Air => _plane.Air;

        /// <summary>複製（先読み・テスト用）。</summary>
        public Aircraft Copy() => _plane.Clone();
    }
}
