using System;
using TileMotion;

namespace Game.Core
{
    /// <summary>
    /// 飛行モデルの設定。値は全て整数（長さは µm、速さは µm/Tick、加速は µm/Tick²、角度は 1 周 = 65536、時間は Tick）。
    /// Unity のシリアライズのため公開フィールドで書く。使う前に <see cref="Validate"/> で検証し、返った複製を使う。
    /// </summary>
    [Serializable]
    public sealed class FlightConfig
    {
        /// <summary>速さの上限の上限（600 m/s）。これ以下で、揚力の基準の速さが 20 m/s 以上なら、揚力・抗力の中間値が long に収まる。</summary>
        public const int MaxSpeedLimit = 600 * Units.MetersPerSecond;

        /// <summary>
        /// 1 Tick の風見の回転の上限（10°）。姿勢の小回転は 1 次の近似 (1, ω/2) で、要求 θ に対して実際は 2·atan(θ/2) 回る。
        /// 10° なら誤差は 0.03° 程度。舵・失速の角速度も検証で 10° 以下にしてある。
        /// </summary>
        public static readonly int MaxWeathervaneTurn = Units.Degrees(10);

        /// <summary>重力加速度（9.8 m/s²）。</summary>
        public int Gravity = 2722;

        /// <summary>スロットル 100% の推力の加速。</summary>
        public int Thrust = 2722;

        /// <summary>水平飛行でスロットル 100% のときの最高速度（推力と抗力が釣り合う速さ）。抗力は速さの 2 乗に比例する。</summary>
        public int MaxSpeed = 200 * Units.MetersPerSecond;

        /// <summary>速さの上限（急降下でもこれを超えない）。</summary>
        public int SpeedLimit = 330 * Units.MetersPerSecond;

        /// <summary>迎角 <see cref="LevelAngle"/> で揚力がちょうど重力と釣り合う速さ。揚力は迎角と速さの 2 乗に比例する。</summary>
        public int LevelSpeed = 120 * Units.MetersPerSecond;

        /// <summary><see cref="LevelSpeed"/> で 1 G の揚力を出す迎角。</summary>
        public int LevelAngle = Units.Degrees(3);

        /// <summary>失速する迎角。これを超えると揚力が <see cref="StallLiftPercent"/> に落ちる。</summary>
        public int StallAngle = Units.Degrees(20);

        /// <summary>失速中の揚力（失速角での値に対する %）。</summary>
        public int StallLiftPercent = 40;

        /// <summary>この速さ未満の空中は失速とみなす。</summary>
        public int StallSpeed = 50 * Units.MetersPerSecond;

        /// <summary><see cref="LevelSpeed"/> で 1 G の横力を出す横滑り角。</summary>
        public int SideAngle = Units.Degrees(6);

        /// <summary>舵が全部効く速さ。これより遅いと効きが (速さ / この値)² に下がる。</summary>
        public int ControlSpeed = 80 * Units.MetersPerSecond;

        /// <summary>1 Tick のピッチの角速度の上限（25°/s）。</summary>
        public int PitchRate = 76;

        /// <summary>1 Tick のロールの角速度の上限（120°/s）。</summary>
        public int RollRate = 364;

        /// <summary>1 Tick のヨーの角速度の上限（9°/s）。</summary>
        public int YawRate = 27;

        /// <summary>失速中に機首を速度の向きへ下げる 1 Tick の角度（30°/s）。<see cref="PitchRate"/> より大きく、引き続けても機首が下がる。</summary>
        public int StallPitchDrop = 91;

        /// <summary>1 Tick に横滑り角の何 ‰ だけ機首を速度の向きへ回すか（風見）。</summary>
        public int Weathervane = 40;

        /// <summary>入力を全部入れたときの 1 Tick のスロットルの変化（‰）。</summary>
        public int ThrottleRate = 12;

        /// <summary>接地中の機体の中心の高さ。空中でこの高さ以下になったら墜落。</summary>
        public int GearHeight = 2 * (int)Units.PerMeter;

        /// <summary>地上で機首を上げられる速さ。</summary>
        public int RotateSpeed = 60 * Units.MetersPerSecond;

        /// <summary>地上で機首を上げられる上限の角度。</summary>
        public int MaxGroundPitch = Units.Degrees(12);

        /// <summary>地上の機首上げ・下げの 1 Tick の角度。</summary>
        public int GroundPitchRate = 40;

        /// <summary>地上の旋回の 1 Tick の角度。</summary>
        public int TaxiTurnRate = 12;

        /// <summary>地上の転がり抵抗の減速。</summary>
        public int RollingFriction = 136;

        /// <summary>全ての設定値を決まった順で混ぜる（状態のハッシュが設定の違いも見分けるため）。値を足したらここにも足す（テストが全フィールドを確かめる）。</summary>
        internal void Mix(ref StateHash hash)
        {
            hash.Mix(Gravity);
            hash.Mix(Thrust);
            hash.Mix(MaxSpeed);
            hash.Mix(SpeedLimit);
            hash.Mix(LevelSpeed);
            hash.Mix(LevelAngle);
            hash.Mix(StallAngle);
            hash.Mix(StallLiftPercent);
            hash.Mix(StallSpeed);
            hash.Mix(SideAngle);
            hash.Mix(ControlSpeed);
            hash.Mix(PitchRate);
            hash.Mix(RollRate);
            hash.Mix(YawRate);
            hash.Mix(StallPitchDrop);
            hash.Mix(Weathervane);
            hash.Mix(ThrottleRate);
            hash.Mix(GearHeight);
            hash.Mix(RotateSpeed);
            hash.Mix(MaxGroundPitch);
            hash.Mix(GroundPitchRate);
            hash.Mix(TaxiTurnRate);
            hash.Mix(RollingFriction);
        }

        public FlightConfig Copy() => (FlightConfig)MemberwiseClone();

        /// <summary>範囲を確かめ、問題なければ複製を返す。</summary>
        public FlightConfig Validate()
        {
            Require(Gravity, 100, 20000, nameof(Gravity));
            Require(Thrust, 1, 40000, nameof(Thrust));
            Require(MaxSpeed, 10 * Units.MetersPerSecond, MaxSpeedLimit, nameof(MaxSpeed));
            Require(SpeedLimit, MaxSpeed, MaxSpeedLimit, nameof(SpeedLimit));
            Require(LevelSpeed, 20 * Units.MetersPerSecond, MaxSpeed, nameof(LevelSpeed));
            Require(StallAngle, 1, Units.Degrees(45), nameof(StallAngle));
            Require(LevelAngle, 1, StallAngle - 1, nameof(LevelAngle));
            Require(StallLiftPercent, 0, 100, nameof(StallLiftPercent));
            Require(StallSpeed, Units.MetersPerSecond, MaxSpeed, nameof(StallSpeed));
            Require(SideAngle, 1, Units.Degrees(45), nameof(SideAngle));
            Require(ControlSpeed, Units.MetersPerSecond, MaxSpeed, nameof(ControlSpeed));
            Require(PitchRate, 0, Units.Degrees(10), nameof(PitchRate));
            Require(RollRate, 0, Units.Degrees(10), nameof(RollRate));
            Require(YawRate, 0, Units.Degrees(10), nameof(YawRate));
            Require(StallPitchDrop, PitchRate + 1, Units.Degrees(10), nameof(StallPitchDrop));
            Require(Weathervane, 0, 1000, nameof(Weathervane));
            Require(ThrottleRate, 1, 1000, nameof(ThrottleRate));
            Require(GearHeight, 1, 100 * (int)Units.PerMeter, nameof(GearHeight));
            Require(RotateSpeed, StallSpeed, MaxSpeed, nameof(RotateSpeed));
            Require(MaxGroundPitch, 1, StallAngle, nameof(MaxGroundPitch));
            Require(GroundPitchRate, 1, Units.Degrees(10), nameof(GroundPitchRate));
            Require(TaxiTurnRate, 0, Units.Degrees(10), nameof(TaxiTurnRate));
            Require(RollingFriction, 0, Thrust, nameof(RollingFriction));

            // 地上で離陸できる：機首上げの上限で 1 G を超える揚力が出る速さまで、推力が抵抗に勝って加速できる。
            var copy = Copy();
            var liftOff = copy.LiftOffSpeed();
            if (liftOff >= MaxSpeed || copy.GroundAccel(liftOff, 1000) <= 0)
            {
                throw new ArgumentException($"この設定では離陸できません（離陸に要る速さ {liftOff} µm/Tick）。");
            }

            return copy;
        }

        /// <summary>舵の効き（Q16、1.0 = 65536）。</summary>
        public long ControlEffect(long speed) => Math.Min(SpeedRatioQ16(speed, ControlSpeed), 1L << 16);

        /// <summary>(速さ / <see cref="LevelSpeed"/>)² の Q16。</summary>
        public long LiftPressure(long speed) => SpeedRatioQ16(speed, LevelSpeed);

        /// <summary>迎角（符号つき）での揚力の加速。失速角を超えると失速角の <see cref="StallLiftPercent"/> % に落ちる。</summary>
        public long Lift(int alpha, long speed)
        {
            long angle = alpha;
            if (Math.Abs(alpha) > StallAngle)
            {
                angle = Math.Sign(alpha) * Fixed.RoundDiv((long)StallAngle * StallLiftPercent, 100);
            }

            return Fixed.RoundDiv(Gravity * angle * LiftPressure(speed), (long)LevelAngle << 16);
        }

        /// <summary>横滑り角（符号つき）での横力の加速の大きさ。角度は失速角で頭打ち。</summary>
        public long SideForce(int beta, long speed)
        {
            long angle = Fixed.Clamp(beta, -StallAngle, StallAngle);
            return Fixed.RoundDiv(Gravity * angle * LiftPressure(speed), (long)SideAngle << 16);
        }

        /// <summary>抗力の加速の大きさ（推力 × (速さ / 最高速度)²）。</summary>
        public long Drag(long speed)
        {
            var s = speed / Units.PerMillimeter;
            var max = MaxSpeed / Units.PerMillimeter;
            return Fixed.RoundDiv(Thrust * s * s, max * max);
        }

        /// <summary>スロットル（‰）での推力の加速。</summary>
        public long ThrustAt(int throttle) => Fixed.RoundDiv((long)Thrust * throttle, 1000);

        /// <summary>地上の加速（推力 − 抗力 − 転がり抵抗）。</summary>
        public long GroundAccel(long speed, int throttle) => ThrustAt(throttle) - Drag(speed) - RollingFriction;

        /// <summary>機首上げの上限で揚力が重力を超える最小の速さ（µm/Tick）。</summary>
        public long LiftOffSpeed()
        {
            long low = 0;
            long high = SpeedLimit;
            while (low < high)
            {
                var mid = (low + high) / 2;
                if (Lift(MaxGroundPitch, mid) > Gravity) { high = mid; }
                else { low = mid + 1; }
            }

            return low;
        }

        private static long SpeedRatioQ16(long speed, int reference)
        {
            var s = speed / Units.PerMillimeter;
            var r = reference / Units.PerMillimeter;
            return Fixed.RoundDiv((s * s) << 16, r * r);
        }

        private static void Require(int value, int min, int max, string name)
        {
            if (value < min || value > max) { throw new ArgumentOutOfRangeException(name, value, $"{min}〜{max} にしてください。"); }
        }
    }
}
