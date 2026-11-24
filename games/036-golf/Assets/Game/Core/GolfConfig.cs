using System;
using IntGeometry;
using TileMotion;

namespace Game.Core
{
    /// <summary>地面の種類。コースの 1 m のマスごとに 1 つ。</summary>
    public enum Surface
    {
        Rough,
        Fairway,
        Tee,
        Green,
        Bunker,

        /// <summary>池。地面に触れたら 1 打罰で前の位置から打ち直し（OB と同じ扱い）。</summary>
        Water,

        /// <summary>OB。ホールの外も OB。</summary>
        OutOfBounds,
    }

    public static class Clubs
    {
        public const int Wood = 0;
        public const int Iron = 1;
        public const int Wedge = 2;
        public const int Putter = 3;
        public const int Count = 4;
    }

    /// <summary>
    /// 物理とルールの設定。値は全て整数（単位は <see cref="Units"/>）。角度は int-geometry の <c>Trig</c> の単位（1 周 = 65536）。
    /// 地面の性質は <see cref="Surface"/> の順の配列、クラブの性質は <see cref="Clubs"/> の順の配列。
    /// Unity のシリアライズのため公開フィールドで書く。使う前に <see cref="Validate"/> で検証する。
    /// </summary>
    [Serializable]
    public sealed class GolfConfig
    {
        public const int SurfaceCount = 7;

        /// <summary>打点の段の範囲（−2〜+2）。</summary>
        public const int MaxImpact = 2;

        /// <summary>重力（µm/Tick²、9.8 m/s²）。</summary>
        public int Gravity = 2722;

        /// <summary>空気抵抗：毎 Tick、速度の Drag/65536 を 0 方向へ減らす。</summary>
        public int Drag = 150;

        /// <summary>風の力：毎 Tick、風速の WindPush/65536 を速度へ足す（飛んでいる間だけ）。</summary>
        public int WindPush = 413;

        /// <summary>風の強さの段の上限（0〜この値の段を引く）。</summary>
        public int MaxWindSteps = 6;

        /// <summary>風の強さの 1 段（µm/Tick、1 m/s）。</summary>
        public int WindStep = 16667;

        /// <summary>クラブの最大初速（µm/Tick）。ウッド・アイアン・ウェッジ・パター。</summary>
        public int[] ClubSpeed = { 1_133_333, 866_667, 633_333, 95_000 };

        /// <summary>クラブの打ち出し角（パターは 0 で転がす）。</summary>
        public int[] ClubLoft = { 2913, 4369, 7282, 0 };

        /// <summary>クラブごとの最初の着地で水平の速さに掛ける割合（/4096、スピンの効き）。</summary>
        public int[] ClubBite = { 4096, 3000, 1600, 4096 };

        /// <summary>打点を 1 段下げる（ImpactY を −1）ごとに打ち出し角を増やす量。</summary>
        public int LoftPerImpact = 364;

        /// <summary>打点を 1 段上げる（ImpactY を +1）ごとに、最初の着地の水平の割合へ足す量（/4096。トップスピンで転がり、バックスピンで止まる）。</summary>
        public int BitePerImpact = 400;

        /// <summary>打点を右へ 1 段ずらすごとの横の加速（µm/Tick²、右へ曲がる）。</summary>
        public int SpinPerImpact = 260;

        /// <summary>インパクトのずれ 1 Tick ごとの横の加速（遅いと右、早いと左へ曲がる）。</summary>
        public int SpinPerErrorTick = 130;

        /// <summary>パターのインパクトのずれ 1 Tick ごとの向きのずれ（遅いと右、早いと左）。</summary>
        public int PuttAnglePerErrorTick = 160;

        /// <summary>メーターが 0 から最大まで上がる Tick 数（往復はこの 2 倍）。</summary>
        public int MeterHalfTicks = 45;

        /// <summary>インパクト帯の半分の幅（Tick）。理想の Tick ± この値の押下が帯の中。</summary>
        public int ImpactWindow = 8;

        /// <summary>ミスショットのパワーの割合（‰）。</summary>
        public int MishitPower = 500;

        /// <summary>着地の鉛直の反発（/4096）。</summary>
        public int[] BounceRestitution = { 900, 1300, 1300, 1000, 200, 0, 0 };

        /// <summary>着地のたびに水平の速さに掛ける割合（/4096）。</summary>
        public int[] BounceKeep = { 2000, 2400, 2400, 2300, 800, 0, 0 };

        /// <summary>転がりの減速（µm/Tick²）。</summary>
        public int[] RollDecel = { 1111, 694, 694, 153, 3333, 0, 0 };

        /// <summary>その場所から打つときのパワーの割合（‰）。池と OB からは打たない。</summary>
        public int[] LiePower = { 850, 1000, 1000, 1000, 650, 0, 0 };

        /// <summary>着地後の鉛直の速さがこれ以下なら跳ねずに転がる。</summary>
        public int RollThreshold = 16667;

        /// <summary>転がる速さがこれ以下で、傾きの加速が減速以下なら止まる。</summary>
        public int StopSpeed = 300;

        /// <summary>カップの半径（球の中心がこの内側に入ったら入りうる）。</summary>
        public int CupRadius = 54_000;

        /// <summary>カップに入れる水平の速さの上限（µm/Tick、1.2 m/s）。速ければ上を通り過ぎる。</summary>
        public int CupMaxSpeed = 20_000;

        /// <summary>木に当たったとき、反射した水平の速さに掛ける割合（/4096）。</summary>
        public int TreeRestitution = 1500;

        /// <summary>1 打の Tick 数の上限。超えたらその場で止めたとみなす。</summary>
        public int MaxShotTicks = 3600;

        /// <summary>1 ホールの打数の上限は par + この値。</summary>
        public int MaxOver = 5;

        /// <summary>球が止まってから次の構えまでの Tick 数。</summary>
        public int RestTicks = 45;

        /// <summary>OB・池の表示から打ち直しの構えまでの Tick 数。</summary>
        public int PenaltyTicks = 90;

        /// <summary>カップインの表示から次のホールまでの Tick 数。</summary>
        public int HoleOutTicks = 150;

        /// <summary>向きを 1 回変える量（パター以外）。</summary>
        public int AimStep = 64;

        /// <summary>パターの向きを 1 回変える量（表の刻みの最小）。</summary>
        public int PuttAimStep = 16;

        /// <summary>パターの予測線を描く Tick 数（傾きの読みを残す）。</summary>
        public int PuttPreviewTicks = 40;

        /// <summary>深い複製（配列も複製する）。</summary>
        public GolfConfig Copy()
        {
            var copy = (GolfConfig)MemberwiseClone();
            copy.ClubSpeed = (int[])ClubSpeed.Clone();
            copy.ClubLoft = (int[])ClubLoft.Clone();
            copy.ClubBite = (int[])ClubBite.Clone();
            copy.BounceRestitution = (int[])BounceRestitution.Clone();
            copy.BounceKeep = (int[])BounceKeep.Clone();
            copy.RollDecel = (int[])RollDecel.Clone();
            copy.LiePower = (int[])LiePower.Clone();
            return copy;
        }

        /// <summary>理想のインパクトの Tick（振り始めからの Tick 数）。</summary>
        public int IdealImpactTick => MeterHalfTicks * 2;

        /// <summary>1 ホールの打数の上限。</summary>
        public int StrokeCap(int par) => par + MaxOver;

        /// <summary>範囲外の値があれば例外。</summary>
        public void Validate()
        {
            Require(Gravity, 100, 10_000, nameof(Gravity));
            Require(Drag, 1, 4096, nameof(Drag));
            Require(WindPush, 0, 4096, nameof(WindPush));
            Require(MaxWindSteps, 0, 20, nameof(MaxWindSteps));
            Require(WindStep, 0, 100_000, nameof(WindStep));

            // 風で速くなりうる上限（風の力と抵抗が釣り合う速さ）も 1 Tick の移動の上限に収める。
            var windTerminal = (long)MaxWindSteps * WindStep * WindPush / Drag;
            Require(windTerminal, 0, Units.MaxTickMove / 4, "風の強さ × WindPush / Drag");

            RequireArray(ClubSpeed, Clubs.Count, nameof(ClubSpeed));
            RequireArray(ClubLoft, Clubs.Count, nameof(ClubLoft));
            RequireArray(ClubBite, Clubs.Count, nameof(ClubBite));
            for (var club = 0; club < Clubs.Count; club++)
            {
                // 速さの上限：球は z = 0 から打ち出され、抵抗で遅くなる一方なので、打ち出しの速さと風の速さの和が 1 Tick の移動の上限になる。
                // 横の曲がり・傾きの加速の余裕に 1/4 を残す。
                Require(ClubSpeed[club], 1000, (Units.MaxTickMove * 3 / 4) - windTerminal, $"{nameof(ClubSpeed)}[{club}]");
                Require(ClubBite[club], 0, Units.Ratio, $"{nameof(ClubBite)}[{club}]");
            }

            Require(ClubLoft[Clubs.Putter], 0, 0, $"{nameof(ClubLoft)}[{Clubs.Putter}]");
            for (var club = 0; club < Clubs.Putter; club++)
            {
                // 打点で増減しても 5°〜70° に収める。
                Require(ClubLoft[club] - (MaxImpact * LoftPerImpact), 910, 12743, $"{nameof(ClubLoft)}[{club}] − 打点");
                Require(ClubLoft[club] + (MaxImpact * LoftPerImpact), 910, 12743, $"{nameof(ClubLoft)}[{club}] + 打点");
            }

            Require(LoftPerImpact, 0, 2000, nameof(LoftPerImpact));
            Require(BitePerImpact, 0, Units.Ratio / 4, nameof(BitePerImpact));
            Require(SpinPerImpact, 0, 5000, nameof(SpinPerImpact));
            Require(SpinPerErrorTick, 0, 5000, nameof(SpinPerErrorTick));
            Require(PuttAnglePerErrorTick, 0, 2048, nameof(PuttAnglePerErrorTick));
            Require(MeterHalfTicks, 10, 300, nameof(MeterHalfTicks));
            Require(ImpactWindow, 1, MeterHalfTicks / 2, nameof(ImpactWindow));
            Require(MishitPower, 0, 1000, nameof(MishitPower));

            RequireArray(BounceRestitution, SurfaceCount, nameof(BounceRestitution));
            RequireArray(BounceKeep, SurfaceCount, nameof(BounceKeep));
            RequireArray(RollDecel, SurfaceCount, nameof(RollDecel));
            RequireArray(LiePower, SurfaceCount, nameof(LiePower));
            for (var surface = 0; surface < (int)Surface.Water; surface++)
            {
                // 反発は 1 未満（跳ねるたびに低くなり、いずれ転がる）。
                Require(BounceRestitution[surface], 0, Units.Ratio - 1, $"{nameof(BounceRestitution)}[{(Surface)surface}]");
                Require(BounceKeep[surface], 0, Units.Ratio, $"{nameof(BounceKeep)}[{(Surface)surface}]");

                // 減速は正（転がる球は必ず遅くなる）。
                Require(RollDecel[surface], 1, 100_000, $"{nameof(RollDecel)}[{(Surface)surface}]");
                Require(LiePower[surface], 1, 1000, $"{nameof(LiePower)}[{(Surface)surface}]");
            }

            Require(RollThreshold, 0, Units.MaxTickMove, nameof(RollThreshold));
            Require(StopSpeed, 0, 100_000, nameof(StopSpeed));
            Require(CupRadius, 10_000, 200_000, nameof(CupRadius));
            Require(CupMaxSpeed, 1, Units.MaxTickMove, nameof(CupMaxSpeed));
            Require(TreeRestitution, 0, Units.Ratio, nameof(TreeRestitution));
            Require(MaxShotTicks, 60, 36_000, nameof(MaxShotTicks));
            Require(MaxOver, 1, 20, nameof(MaxOver));
            Require(RestTicks, 1, 600, nameof(RestTicks));
            Require(PenaltyTicks, 1, 600, nameof(PenaltyTicks));
            Require(HoleOutTicks, 1, 600, nameof(HoleOutTicks));
            Require(AimStep, 16, 4096, nameof(AimStep));
            Require(PuttAimStep, 16, 4096, nameof(PuttAimStep));
            Require(PuttPreviewTicks, 1, 600, nameof(PuttPreviewTicks));
            RejectSpeedsThatBreakTheSweepLimit();
        }

        /// <summary>
        /// 飛行中の横スピンと風、着地後にグリーンの最大の傾きで転がる分を、最大の飛行時間ぶん足しても、
        /// 1 Tick の移動の各成分が <see cref="Units.MaxTickMove"/> を超えないこと。
        /// 初速だけの上限では、抵抗が小さいとスピンで速さが増え、検証を通った設定の途中で掃引が例外になる。
        /// 見積もりは実際より大きい（初速はクラブの速さそのもの、風は進行方向、スピンの長さは四捨五入の 1 を足す、着地しても減速しない、坂は毎 Tick 最大）。
        /// </summary>
        private void RejectSpeedsThatBreakTheSweepLimit()
        {
            var spin = ((long)MaxImpact * SpinPerImpact) + ((long)ImpactWindow * SpinPerErrorTick);
            if (spin > 0) { spin++; }
            var windSpeed = (long)MaxWindSteps * WindStep;
            var windAdd = windSpeed == 0 || WindPush == 0 ? 0 : ((windSpeed * WindPush) + Units.Fine - 1) / Units.Fine;
            long speed = 0;
            for (var club = 0; club < Clubs.Count; club++)
            {
                if (ClubSpeed[club] > speed) { speed = ClubSpeed[club]; }
            }

            long peak = speed;
            for (var tick = 1; tick <= MaxShotTicks; tick++)
            {
                speed -= speed * Drag / Units.Fine;
                speed += windAdd;
                if (spin > 0) { speed = IntMath.SqrtCeiling((speed * speed) + (spin * spin)); }
                if (speed > peak) { peak = speed; }
                if (speed > Units.MaxTickMove)
                {
                    throw new ArgumentOutOfRangeException(nameof(SpinPerImpact), $"横スピンと風を {tick} Tick 足すと 1 Tick の移動の上限 {Units.MaxTickMove} を超えます。");
                }
            }

            // グリーンの傾きは検証の上限（80‰）まで効く。減速より大きければ、最大の飛行時間のあいだ速さが増え続ける。
            var slope = (long)Hole.MaxGrade * Gravity * 5 / 7000;
            var gain = slope > RollDecel[(int)Surface.Green] ? slope - RollDecel[(int)Surface.Green] : 0;
            if (peak + (gain * MaxShotTicks) > Units.MaxTickMove)
            {
                throw new ArgumentOutOfRangeException(nameof(RollDecel), "グリーンの傾きで転がる速さが 1 Tick の移動の上限を超えます。");
            }
        }

        /// <summary>全ての値を混ぜる（状態のハッシュに含め、設定が違えば違うハッシュにする）。</summary>
        public void MixInto(ref StateHash hash)
        {
            foreach (var value in new[]
            {
                Gravity, Drag, WindPush, MaxWindSteps, WindStep, LoftPerImpact, BitePerImpact, SpinPerImpact, SpinPerErrorTick,
                PuttAnglePerErrorTick, MeterHalfTicks, ImpactWindow, MishitPower, RollThreshold, StopSpeed, CupRadius, CupMaxSpeed,
                TreeRestitution, MaxShotTicks, MaxOver, RestTicks, PenaltyTicks, HoleOutTicks, AimStep, PuttAimStep, PuttPreviewTicks,
            })
            {
                hash.Mix(value);
            }

            foreach (var array in new[] { ClubSpeed, ClubLoft, ClubBite, BounceRestitution, BounceKeep, RollDecel, LiePower })
            {
                hash.Mix(array.Length);
                foreach (var value in array) { hash.Mix(value); }
            }
        }

        private static void RequireArray(int[]? values, int length, string name)
        {
            if (values == null || values.Length != length)
            {
                throw new ArgumentException($"{name} は {length} 個の値にしてください。", name);
            }
        }

        private static void Require(long value, long min, long max, string name)
        {
            if (value < min || value > max)
            {
                throw new ArgumentOutOfRangeException(name, value, $"{name} は {min}〜{max} にしてください。");
            }
        }
    }
}
