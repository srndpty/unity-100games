using System;
using TileMotion;

namespace Game.Core
{
    /// <summary>
    /// 物理とルールの設定。値は全て整数。速度は 1 区間（1/480 秒）あたり 1/<see cref="Board.VelocityScale"/> 単位、
    /// 角度は int-geometry の <c>Trig</c> の単位（1 周 = 65536）、反発係数は / <see cref="RatioDenominator"/>。
    /// Unity のシリアライズのため公開フィールドで書く。使う前に <see cref="Validate"/> で検証する。
    /// </summary>
    [Serializable]
    public sealed class PinballConfig
    {
        /// <summary>反発係数の分母。</summary>
        public const int RatioDenominator = 4096;

        /// <summary>台の上に同時に出せる球の数（マルチボールで 2 個）。</summary>
        public const int MaxBalls = 2;

        /// <summary>重力（1 区間あたりの速度の減り、−y 向き）。</summary>
        public int Gravity = 59;

        /// <summary>球の速さの上限。1 区間の移動は球の半径以下（<see cref="Validate"/> で保証）。</summary>
        public int MaxSpeed = 98304;

        public int BallRadius = 512;

        public int WallRestitution = 2048;

        public int FlipperRestitution = 1024;

        public int BumperRestitution = 2048;

        public int TargetRestitution = 1536;

        /// <summary>バンパーに触れたとき、反射に加えて外向きに足す速さ。</summary>
        public int BumperKick = 24000;

        /// <summary>フリッパーを押している間の角加速度（1 区間あたり）。</summary>
        public int FlipAccel = 150;

        /// <summary>フリッパーが上がる角速度の上限（1 区間あたり）。</summary>
        public int FlipMaxSpeed = 600;

        /// <summary>離したときに戻る角加速度。</summary>
        public int ReturnAccel = 80;

        /// <summary>戻る角速度の上限。</summary>
        public int ReturnMaxSpeed = 400;

        /// <summary>プランジャーを引ききるまでの Tick 数。</summary>
        public int PlungerMaxTicks = 60;

        /// <summary>引き 1 Tick で打ち出す速さ（上向き）。</summary>
        public int PlungerMinSpeed = 20000;

        /// <summary>引ききって打ち出す速さ。</summary>
        public int PlungerMaxSpeed = 46000;

        /// <summary>マルチボールの 2 個目を自動で打ち出す速さの範囲（試合のシードで決める）。</summary>
        public int AutoLaunchMin = 40000;

        public int AutoLaunchMax = 46000;

        public int BumperScore = 100;

        public int TargetScore = 500;

        /// <summary>ターゲットを 3 枚倒しきったときの得点（マルチボールになるかに依らない）。</summary>
        public int TargetSetScore = 2000;

        /// <summary>1 試合の球の数。</summary>
        public int Balls = 3;

        public PinballConfig Copy() => (PinballConfig)MemberwiseClone();

        /// <summary>範囲外の値があれば例外。台に依る条件（フリッパーのすり抜け）は <see cref="Table.Validate"/> が見る。</summary>
        public void Validate()
        {
            Require(BallRadius, Board.Unit / 8, Board.Unit, nameof(BallRadius));
            Require(Gravity, 1, 1024, nameof(Gravity));

            // 1 区間の移動を球の半径以下にする（距離の計算を long に収め、保守的前進の歩数を抑える）。
            Require(MaxSpeed, Gravity + 1, BallRadius * Board.VelocityScale, nameof(MaxSpeed));
            Require(WallRestitution, 0, RatioDenominator, nameof(WallRestitution));
            Require(FlipperRestitution, 0, RatioDenominator, nameof(FlipperRestitution));
            Require(BumperRestitution, 0, RatioDenominator, nameof(BumperRestitution));
            Require(TargetRestitution, 0, RatioDenominator, nameof(TargetRestitution));
            Require(BumperKick, 0, MaxSpeed, nameof(BumperKick));
            Require(FlipAccel, 1, 4096, nameof(FlipAccel));
            Require(FlipMaxSpeed, 1, 4096, nameof(FlipMaxSpeed));
            Require(ReturnAccel, 1, 4096, nameof(ReturnAccel));
            Require(ReturnMaxSpeed, 1, 4096, nameof(ReturnMaxSpeed));
            Require(PlungerMaxTicks, 1, 600, nameof(PlungerMaxTicks));
            Require(PlungerMinSpeed, 1, MaxSpeed, nameof(PlungerMinSpeed));
            Require(PlungerMaxSpeed, PlungerMinSpeed, MaxSpeed, nameof(PlungerMaxSpeed));
            Require(AutoLaunchMin, 1, MaxSpeed, nameof(AutoLaunchMin));
            Require(AutoLaunchMax, AutoLaunchMin, MaxSpeed, nameof(AutoLaunchMax));
            Require(BumperScore, 0, 1_000_000, nameof(BumperScore));
            Require(TargetScore, 0, 1_000_000, nameof(TargetScore));
            Require(TargetSetScore, 0, 1_000_000, nameof(TargetSetScore));
            Require(Balls, 1, 9, nameof(Balls));
        }

        /// <summary>全ての値を混ぜる（状態のハッシュに含め、設定が違えば違うハッシュにする）。</summary>
        public void MixInto(ref StateHash hash)
        {
            foreach (var value in new[]
            {
                Gravity, MaxSpeed, BallRadius, WallRestitution, FlipperRestitution, BumperRestitution, TargetRestitution, BumperKick,
                FlipAccel, FlipMaxSpeed, ReturnAccel, ReturnMaxSpeed, PlungerMaxTicks, PlungerMinSpeed, PlungerMaxSpeed,
                AutoLaunchMin, AutoLaunchMax, BumperScore, TargetScore, TargetSetScore, Balls,
            })
            {
                hash.Mix(value);
            }
        }

        private static void Require(int value, int min, int max, string name)
        {
            if (value < min || value > max)
            {
                throw new ArgumentOutOfRangeException(name, value, $"{name} は {min}〜{max} にしてください。");
            }
        }
    }
}
