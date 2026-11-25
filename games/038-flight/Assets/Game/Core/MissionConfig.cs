using System;
using TileMotion;

namespace Game.Core
{
    /// <summary>
    /// ミッション・ロックオン・ミサイル・敵の設定。1 km 以上になりうる長さは m、それ以外の長さは µm、速さは µm/Tick、
    /// 加速は µm/Tick²、角度は 1 周 = 65536、時間は Tick。公開フィールドで書き、<see cref="Validate"/> が返した複製を使う。
    /// </summary>
    [Serializable]
    public sealed class MissionConfig
    {
        public const int EnemyCount = 3;

        /// <summary>区域の正方形の半分の幅（m）。中心は原点。</summary>
        public int ArenaHalfWidth = 6000;

        /// <summary>区域の高さの上限（m）。</summary>
        public int Ceiling = 6000;

        /// <summary>区域の外にこの Tick 続けていたら失敗。</summary>
        public int OutOfAreaTicks = 600;

        /// <summary>制限時間（Tick）。</summary>
        public int MissionTicks = 18000;

        /// <summary>滑走路の南端の z（m）。中心線は x = 0。</summary>
        public int RunwayStartZ = -5500;

        /// <summary>滑走路の長さ（m）。</summary>
        public int RunwayLength = 1500;

        /// <summary>滑走路の半分の幅（m）。</summary>
        public int RunwayHalfWidth = 25;

        /// <summary>敵の初期位置の中心の z（m）。</summary>
        public int EnemyCenterZ = 2500;

        /// <summary>敵の初期高度（m）。</summary>
        public int EnemyAltitude = 1500;

        /// <summary>敵どうしの初期の x の間隔（m）。</summary>
        public int EnemySpacing = 1200;

        /// <summary>敵の初期位置のぶれ（m、各軸 ±）。</summary>
        public int EnemyJitter = 300;

        /// <summary>敵の初速。</summary>
        public int EnemySpeed = 150 * Units.MetersPerSecond;

        /// <summary>敵の巡回の円の半径（m）。</summary>
        public int PatrolRadius = 1200;

        /// <summary>プレイヤーが空中でこの距離（m）より近づくと、その敵は交戦を始める（以後は巡回に戻らない）。</summary>
        public int EngageRange = 5000;

        /// <summary>敵が保とうとする速さ（プレイヤーの最高速度より遅く、全開なら振り切れる）。</summary>
        public int EnemyCruiseSpeed = 150 * Units.MetersPerSecond;

        /// <summary>敵の舵の効き（%）。プレイヤーより小さいので、急旋回を続ければ後ろの敵を前へ回り込ませられる。</summary>
        public int EnemyAgility = 75;

        /// <summary>攻撃役の敵がこの Tick 追い続けたら離脱する。</summary>
        public int EnemyPursuitTicks = 600;

        /// <summary>離脱（まっすぐ離れる）の Tick 数。発射した後も離脱する。</summary>
        public int EnemyBreakTicks = 240;

        /// <summary>攻撃役でない交戦中の敵が、プレイヤーのまわりを回る半径（m）。</summary>
        public int EnemyWaitRadius = 2500;

        /// <summary>敵が引き起こす高度（m）。</summary>
        public int EnemyFloor = 400;

        /// <summary>敵がプレイヤーの何 Tick 先の位置へ向かうか。</summary>
        public int EnemyLeadTicks = 45;

        /// <summary>敵がプレイヤーのミサイルを避け始める距離（m）。0 なら避けない。</summary>
        public int EnemyEvadeRange = 800;

        /// <summary>敵の狙いのぶれ（m、各軸 ±）。<see cref="EnemyJitterTicks"/> ごとに引き直す。</summary>
        public int EnemyAimJitter = 150;

        public int EnemyJitterTicks = 90;

        /// <summary>ロックオンの円錐の半角。</summary>
        public int LockCone = Units.Degrees(12);

        /// <summary>ロックオンの距離（m）。</summary>
        public int LockRange = 2500;

        /// <summary>敵のロックオンの円錐の半角（プレイヤーより狭い）。</summary>
        public int EnemyLockCone = Units.Degrees(8);

        /// <summary>敵のロックオンの距離（m）。</summary>
        public int EnemyLockRange = 1500;

        /// <summary>LOCK までに続ける Tick 数（プレイヤー）。</summary>
        public int LockTicks = 60;

        /// <summary>LOCK までに続ける Tick 数（敵）。</summary>
        public int EnemyLockTicks = 240;

        /// <summary>プレイヤーの残弾。</summary>
        public int Missiles = 16;

        /// <summary>敵 1 機の残弾。</summary>
        public int EnemyMissiles = 4;

        /// <summary>発射してから次を撃てるまでの Tick 数（プレイヤー）。</summary>
        public int Reload = 30;

        /// <summary>発射してから次を撃てるまでの Tick 数（敵）。</summary>
        public int EnemyReload = 360;

        /// <summary>プレイヤーの耐久（この数だけ当たると撃墜）。</summary>
        public int PlayerHealth = 3;

        /// <summary>発射時に機首の向きへ足す速さ。</summary>
        public int LaunchBoost = 30 * Units.MetersPerSecond;

        /// <summary>ミサイルの加速。</summary>
        public int MissileAccel = 40000;

        /// <summary>ミサイルの最高速度。</summary>
        public int MissileSpeed = 420 * Units.MetersPerSecond;

        /// <summary>敵のミサイルの最高速度。</summary>
        public int EnemyMissileSpeed = 280 * Units.MetersPerSecond;

        /// <summary>プレイヤーのミサイルの横向きの加速の上限（25 G）。</summary>
        public int MissileTurn = 68050;

        /// <summary>敵のミサイルの横向きの加速の上限（6 G）。</summary>
        public int EnemyMissileTurn = 16332;

        /// <summary>ミサイルの寿命（Tick）。</summary>
        public int MissileLife = 720;

        /// <summary>近接信管の半径（µm）。</summary>
        public int FuseRadius = 12 * (int)Units.PerMeter;

        /// <summary>比例航法の係数。</summary>
        public int NavGain = 4;

        /// <summary>ミサイルの総数（プレイヤーと敵の残弾の和）。置き場はこの数だけ用意する。</summary>
        public int MissileCapacity => Missiles + (EnemyCount * EnemyMissiles);

        public Runway Runway => new(0, Meters(RunwayStartZ), Meters(RunwayLength), Meters(RunwayHalfWidth));

        public static long Meters(int meters) => meters * Units.PerMeter;

        /// <summary>全ての設定値を決まった順で混ぜる（状態のハッシュが設定の違いも見分けるため）。値を足したらここにも足す（テストが全フィールドを確かめる）。</summary>
        internal void Mix(ref StateHash hash)
        {
            hash.Mix(ArenaHalfWidth);
            hash.Mix(Ceiling);
            hash.Mix(OutOfAreaTicks);
            hash.Mix(MissionTicks);
            hash.Mix(RunwayStartZ);
            hash.Mix(RunwayLength);
            hash.Mix(RunwayHalfWidth);
            hash.Mix(EnemyCenterZ);
            hash.Mix(EnemyAltitude);
            hash.Mix(EnemySpacing);
            hash.Mix(EnemyJitter);
            hash.Mix(EnemySpeed);
            hash.Mix(PatrolRadius);
            hash.Mix(EngageRange);
            hash.Mix(EnemyCruiseSpeed);
            hash.Mix(EnemyAgility);
            hash.Mix(EnemyPursuitTicks);
            hash.Mix(EnemyBreakTicks);
            hash.Mix(EnemyWaitRadius);
            hash.Mix(EnemyFloor);
            hash.Mix(EnemyLeadTicks);
            hash.Mix(EnemyEvadeRange);
            hash.Mix(EnemyAimJitter);
            hash.Mix(EnemyJitterTicks);
            hash.Mix(LockCone);
            hash.Mix(LockRange);
            hash.Mix(EnemyLockCone);
            hash.Mix(EnemyLockRange);
            hash.Mix(LockTicks);
            hash.Mix(EnemyLockTicks);
            hash.Mix(Missiles);
            hash.Mix(EnemyMissiles);
            hash.Mix(Reload);
            hash.Mix(EnemyReload);
            hash.Mix(PlayerHealth);
            hash.Mix(LaunchBoost);
            hash.Mix(MissileAccel);
            hash.Mix(MissileSpeed);
            hash.Mix(EnemyMissileSpeed);
            hash.Mix(MissileTurn);
            hash.Mix(EnemyMissileTurn);
            hash.Mix(MissileLife);
            hash.Mix(FuseRadius);
            hash.Mix(NavGain);
            hash.Mix(MissileCapacity);
        }

        public MissionConfig Copy() => (MissionConfig)MemberwiseClone();

        /// <summary>範囲と、この設定の滑走路で離陸できることを確かめ、問題なければ複製を返す。<paramref name="flight"/> は検証済みのもの。</summary>
        public MissionConfig Validate(FlightConfig flight)
        {
            if (flight == null) { throw new ArgumentNullException(nameof(flight)); }
            Require(ArenaHalfWidth, 1000, 8000, nameof(ArenaHalfWidth));
            Require(Ceiling, 1000, 10000, nameof(Ceiling));
            Require(OutOfAreaTicks, 1, 6000, nameof(OutOfAreaTicks));
            Require(MissionTicks, 60, 216000, nameof(MissionTicks));
            Require(RunwayHalfWidth, 5, 200, nameof(RunwayHalfWidth));
            Require(RunwayLength, 100, ArenaHalfWidth, nameof(RunwayLength));
            Require(RunwayStartZ, -ArenaHalfWidth, ArenaHalfWidth - RunwayLength, nameof(RunwayStartZ));
            Require(EnemySpacing, 0, 2000, nameof(EnemySpacing));
            Require(EnemyJitter, 0, 1000, nameof(EnemyJitter));
            Require(EnemyCenterZ, -ArenaHalfWidth + EnemyJitter, ArenaHalfWidth - EnemyJitter, nameof(EnemyCenterZ));
            Require(EnemyAltitude, EnemyFloor + EnemyJitter, Ceiling - EnemyJitter, nameof(EnemyAltitude));
            Require(EnemySpeed, flight.StallSpeed, flight.MaxSpeed, nameof(EnemySpeed));
            Require(PatrolRadius, 200, 4000, nameof(PatrolRadius));
            Require(EngageRange, 0, 20000, nameof(EngageRange));
            Require(EnemyCruiseSpeed, flight.StallSpeed, flight.MaxSpeed, nameof(EnemyCruiseSpeed));
            Require(EnemyAgility, 10, 100, nameof(EnemyAgility));
            Require(EnemyPursuitTicks, 1, 36000, nameof(EnemyPursuitTicks));
            Require(EnemyBreakTicks, 0, 6000, nameof(EnemyBreakTicks));
            Require(EnemyWaitRadius, 200, 2 * ArenaHalfWidth, nameof(EnemyWaitRadius));
            Require(EnemyFloor, 50, Ceiling / 2, nameof(EnemyFloor));
            Require(EnemyLeadTicks, 0, 600, nameof(EnemyLeadTicks));
            Require(EnemyEvadeRange, 0, 5000, nameof(EnemyEvadeRange));
            Require(EnemyAimJitter, 0, 2000, nameof(EnemyAimJitter));
            Require(EnemyJitterTicks, 1, 6000, nameof(EnemyJitterTicks));
            Require(LockCone, 1, Units.Degrees(60), nameof(LockCone));
            Require(LockRange, 100, 2 * ArenaHalfWidth, nameof(LockRange));
            Require(EnemyLockCone, 1, Units.Degrees(60), nameof(EnemyLockCone));
            Require(EnemyLockRange, 100, 2 * ArenaHalfWidth, nameof(EnemyLockRange));
            Require(LockTicks, 1, 6000, nameof(LockTicks));
            Require(EnemyLockTicks, 1, 6000, nameof(EnemyLockTicks));
            Require(Missiles, 0, 64, nameof(Missiles));
            Require(EnemyMissiles, 0, 64, nameof(EnemyMissiles));
            Require(Reload, 0, 6000, nameof(Reload));
            Require(EnemyReload, 0, 6000, nameof(EnemyReload));
            Require(PlayerHealth, 1, 100, nameof(PlayerHealth));
            Require(LaunchBoost, 0, 200 * Units.MetersPerSecond, nameof(LaunchBoost));
            Require(MissileAccel, 0, 1_000_000, nameof(MissileAccel));
            Require(MissileSpeed, flight.SpeedLimit, FlightConfig.MaxSpeedLimit + (200 * Units.MetersPerSecond), nameof(MissileSpeed));
            Require(EnemyMissileSpeed, flight.StallSpeed, FlightConfig.MaxSpeedLimit + (200 * Units.MetersPerSecond), nameof(EnemyMissileSpeed));
            Require(MissileTurn, 1, 1_000_000, nameof(MissileTurn));
            Require(EnemyMissileTurn, 1, 1_000_000, nameof(EnemyMissileTurn));
            Require(MissileLife, 1, 6000, nameof(MissileLife));
            Require(FuseRadius, (int)Units.PerMeter, 200 * (int)Units.PerMeter, nameof(FuseRadius));
            Require(NavGain, 1, 10, nameof(NavGain));

            var copy = Copy();
            var roll = TakeOffRoll(flight, copy.Runway);
            if (roll < 0) { throw new ArgumentException("この設定の滑走路では、スロットル全開で機首を上げても離陸できません。"); }
            return copy;
        }

        /// <summary>
        /// スロットル全開で、上げられる速さになったら機首を上げ続けたとき、離陸する Tick（滑走路から出る・墜落なら -1）。
        /// 飛行モデルそのものを動かして決める。
        /// </summary>
        public static int TakeOffRoll(FlightConfig flight, Runway runway)
        {
            var plane = Aircraft.OnRunway(flight, runway);
            for (var tick = 0; tick < 6000; tick++)
            {
                var pitch = plane.Velocity.Length >= flight.RotateSpeed ? Controls.Full : 0;
                var result = FlightModel.Step(flight, plane, new Controls(pitch, 0, 0, Controls.Full), runway);
                if (result == FlightEvent.TookOff) { return tick; }
                if (result == FlightEvent.Crashed) { return -1; }
            }

            return -1;
        }

        private static void Require(int value, int min, int max, string name)
        {
            if (value < min || value > max) { throw new ArgumentOutOfRangeException(name, value, $"{min}〜{max} にしてください。"); }
        }
    }
}
