using System.Collections.Generic;

namespace Game.Core
{
    /// <summary>
    /// 試合の調整値（全て整数。長さは 1 m = 1000 単位、速さは 1 Tick あたり、割合は ‰ か %）。Unity の設定アセットから写す。
    /// 範囲は <see cref="Validate"/> で検証し、試合は検証を通った複製だけを使う。
    /// </summary>
    public sealed class SoccerConfig
    {
        // ---- 試合 ----

        /// <summary>試合時間（Tick）。既定は 5 分。</summary>
        public int MatchTicks = 5 * 60 * Pitch.TickRate;

        /// <summary>得点の間の後、キックオフで蹴れるようになるまでの Tick。</summary>
        public int RestartDelayTicks = 60;

        /// <summary>キックオフで蹴る人がこの Tick 蹴らなければ、いちばん近い味方へ自動で出す。</summary>
        public int KickoffTimeoutTicks = 300;

        /// <summary>得点の後の間（Tick）。</summary>
        public int GoalCelebrationTicks = 120;

        // ---- 球 ----

        public int BallRadius = 110;

        /// <summary>空中で 1 Tick に減る縦の速さ。</summary>
        public int Gravity = 3;

        /// <summary>転がる球が 1 Tick に失う速さ（‰）。</summary>
        public int RollFrictionPerMille = 12;

        /// <summary>この速さ（各成分の大きさの和）以下に落ちた転がる球は止まる。</summary>
        public int StopSpeed = 4;

        /// <summary>弾んだときに残る縦の速さ（%）。</summary>
        public int BounceRestitutionPercent = 45;

        /// <summary>弾んだときに残る横の速さ（%）。</summary>
        public int BounceFrictionPercent = 85;

        /// <summary>弾んだ後の縦の速さがこれより小さければ、転がりに変わる。</summary>
        public int MinBounceSpeed = 8;

        /// <summary>ポストに当たったときに残る速さ（%）。</summary>
        public int PostRestitutionPercent = 70;

        /// <summary>壁で跳ね返ったときに残る、壁に垂直な速さ（%）。</summary>
        public int WallRestitutionPercent = 70;

        // ---- 選手 ----

        public int PlayerRadius = 300;
        public int PlayerMaxSpeed = 100;
        public int PlayerAcceleration = 8;

        /// <summary>
        /// 赤（相手の CPU）の走る速さ（<see cref="PlayerMaxSpeed"/> の %）。ドリブルの割合と掛け合わせる（ドリブル中は 85 % × この割合）。
        /// 到着の予測もこの速さで行う。青（人間とその味方）は 100 %。
        /// </summary>
        public int RedSpeedPercent = 100;

        /// <summary>球を持っている選手の最高速（%）。</summary>
        public int DribbleSpeedPercent = 85;

        /// <summary>保持中に体の前へ置く球の距離。</summary>
        public int DribbleOffset = 380;

        /// <summary>球を自分のものにできる距離（中心どうし）。</summary>
        public int ControlRadius = 450;

        /// <summary>止められる球の高さ（下端）の上限。</summary>
        public int KneeHeight = 450;

        /// <summary>止められる相対速度の上限（各軸の大きさの和ではなく長さ）。</summary>
        public int TrapSpeed = 420;

        /// <summary>蹴った後、同じ選手が球を止められない Tick。</summary>
        public int KickCooldownTicks = 12;

        public int TackleRadius = 650;

        /// <summary>タックルが決まった後に動けない Tick。</summary>
        public int TackleRecoverTicks = 18;

        /// <summary>タックルを空振りした後に動けない Tick。</summary>
        public int TackleMissRecoverTicks = 40;

        /// <summary>タックルで球を奪われた選手が動けない Tick。</summary>
        public int TackledStunTicks = 30;

        /// <summary>タックルで弾いた球の速さ。</summary>
        public int PokeSpeed = 160;

        // ---- キック ----

        public int MinPassSpeed = 120;
        public int MaxPassSpeed = 420;

        /// <summary>地を這うパスが受け手の位置で残している速さ。</summary>
        public int PassArriveSpeed = 70;

        public int LobVerticalSpeed = 100;
        public int MaxLobSpeed = 260;

        public int ShotSpeed = 430;
        public int ShotVerticalSpeed = 30;

        /// <summary>シュートの狙いの横のぶれ（ゴールの位置で ± この単位）。</summary>
        public int ShotSpread = 500;

        /// <summary>パスの狙いのぶれ（受け手の位置で ± この単位）。</summary>
        public int PassSpread = 250;

        public SoccerConfig Copy() => (SoccerConfig)MemberwiseClone();

        /// <summary>範囲の外の値を全て挙げる（空なら有効）。</summary>
        public IReadOnlyList<string> Validate()
        {
            var e = new List<string>();
            void Range(string name, int value, int min, int max)
            {
                if (value < min || value > max) { e.Add($"{name} は {min}〜{max}（{value}）"); }
            }

            Range(nameof(MatchTicks), MatchTicks, 60 * Pitch.TickRate, 20 * 60 * Pitch.TickRate);
            Range(nameof(RestartDelayTicks), RestartDelayTicks, 1, 600);
            Range(nameof(KickoffTimeoutTicks), KickoffTimeoutTicks, 30, 1800);
            Range(nameof(GoalCelebrationTicks), GoalCelebrationTicks, 1, 600);
            Range(nameof(BallRadius), BallRadius, 50, 200);
            Range(nameof(Gravity), Gravity, 1, 20);
            Range(nameof(RollFrictionPerMille), RollFrictionPerMille, 1, 200);
            Range(nameof(StopSpeed), StopSpeed, 1, 50);
            Range(nameof(BounceRestitutionPercent), BounceRestitutionPercent, 0, 90);
            Range(nameof(BounceFrictionPercent), BounceFrictionPercent, 0, 100);
            Range(nameof(MinBounceSpeed), MinBounceSpeed, 1, 100);
            Range(nameof(PostRestitutionPercent), PostRestitutionPercent, 0, 100);
            Range(nameof(WallRestitutionPercent), WallRestitutionPercent, 0, 100);
            Range(nameof(PlayerRadius), PlayerRadius, 100, 600);
            Range(nameof(PlayerMaxSpeed), PlayerMaxSpeed, 20, 300);
            Range(nameof(PlayerAcceleration), PlayerAcceleration, 1, 300);
            Range(nameof(RedSpeedPercent), RedSpeedPercent, 30, 100);
            Range(nameof(DribbleSpeedPercent), DribbleSpeedPercent, 10, 100);
            Range(nameof(DribbleOffset), DribbleOffset, 0, 1000);
            Range(nameof(ControlRadius), ControlRadius, 100, 1500);
            Range(nameof(KneeHeight), KneeHeight, 0, 2000);
            Range(nameof(TrapSpeed), TrapSpeed, 1, 2000);
            Range(nameof(KickCooldownTicks), KickCooldownTicks, 1, 120);
            Range(nameof(TackleRadius), TackleRadius, 100, 2000);
            Range(nameof(TackleRecoverTicks), TackleRecoverTicks, 0, 240);
            Range(nameof(TackleMissRecoverTicks), TackleMissRecoverTicks, 0, 240);
            Range(nameof(TackledStunTicks), TackledStunTicks, 0, 240);
            Range(nameof(PokeSpeed), PokeSpeed, 10, 600);
            Range(nameof(MinPassSpeed), MinPassSpeed, 10, 600);
            Range(nameof(MaxPassSpeed), MaxPassSpeed, MinPassSpeed, 600);
            Range(nameof(PassArriveSpeed), PassArriveSpeed, 0, 400);
            Range(nameof(LobVerticalSpeed), LobVerticalSpeed, 10, 300);
            Range(nameof(MaxLobSpeed), MaxLobSpeed, 10, 600);
            Range(nameof(ShotSpeed), ShotSpeed, 50, 600);
            Range(nameof(ShotVerticalSpeed), ShotVerticalSpeed, 0, 200);
            Range(nameof(ShotSpread), ShotSpread, 0, 3000);
            Range(nameof(PassSpread), PassSpread, 0, 3000);

            // 1 Tick の球の移動は、ポストとの掃引（int-geometry の Sweep.First）の上限に収める（上限を超えた設定は例外ではなく検証で拒否）。
            // 速さの上限：パス・ロブ・シュート・弾いた球の最大。縦の速さは横に効かない。
            var fastest = System.Math.Max(System.Math.Max(MaxPassSpeed, MaxLobSpeed), System.Math.Max(ShotSpeed, PokeSpeed));
            if ((long)fastest * 2 + BallRadius + Pitch.PostRadius > IntGeometry.Sweep.FirstLimit) { e.Add($"球の最高速 {fastest} が掃引の上限を超えます"); }

            // ドリブルで体の前に置く球は、止められる距離の中にあること。
            if (DribbleOffset >= ControlRadius) { e.Add($"DribbleOffset（{DribbleOffset}）は ControlRadius（{ControlRadius}）より小さくします"); }
            return e;
        }
    }
}
