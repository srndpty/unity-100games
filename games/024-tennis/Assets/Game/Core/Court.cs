using System;

namespace Game.Core
{
    /// <summary>コートのどちら側か。プレイヤーは常に手前（y が負）、CPU は奥（y が正）。コートチェンジはしない。</summary>
    public enum Side
    {
        Near,
        Far,
    }

    /// <summary>サーブを打つ側のコート（サーバーから見て右がデュース、左がアド）。</summary>
    public enum ServeCourt
    {
        Deuce,
        Ad,
    }

    /// <summary>シングルスのコートの寸法と、位置の判定。</summary>
    public static class Court
    {
        /// <summary>ネットからベースラインまで（11.885 m）。</summary>
        public const int HalfLength = 118850;

        /// <summary>センターからシングルスのサイドラインまで（4.115 m）。</summary>
        public const int HalfWidth = 41150;

        /// <summary>ネットからサービスラインまで（6.40 m）。</summary>
        public const int ServiceLine = 64000;

        /// <summary>ネットの高さ（0.914 m、幅全体で一定）。</summary>
        public const int NetHeight = 9140;

        /// <summary>ネットから後ろの壁の線まで。1 回弾んだ球がここを越えたら、受ける側は返せなかった。</summary>
        public const int FenceY = HalfLength + 60000;

        /// <summary>センターから横の壁の線まで。</summary>
        public const int FenceX = HalfWidth + 40000;

        /// <summary>手前の側は 1（奥へ打つ）、奥の側は -1。その側の選手から見た前向き。</summary>
        public static int Forward(Side side) => side == Side.Near ? 1 : -1;

        public static Side Other(Side side) => side == Side.Near ? Side.Far : Side.Near;

        /// <summary>y 座標がどちらの側か。ネットの上（0）は奥として扱わず、呼び出し側で避ける。</summary>
        public static Side SideOf(int y) => y < 0 ? Side.Near : Side.Far;

        /// <summary>その側の選手から見て右の向きの x の符号（手前は +1、奥は -1）。</summary>
        public static int RightOf(Side side) => Forward(side);

        /// <summary>シングルスのコートの中か（ライン上は中）。</summary>
        public static bool InSingles(int x, int y, Side side)
        {
            var depth = y * Forward(side);
            return Math.Abs(x) <= HalfWidth && depth <= 0 && depth >= -HalfLength;
        }

        /// <summary>
        /// サーバー（<paramref name="server"/>）が <paramref name="court"/> から打つときに入れるべき、対角のサービスボックスの中か（ライン上は中）。
        /// </summary>
        public static bool InServiceBox(int x, int y, Side server, ServeCourt court)
        {
            var receiver = Other(server);
            var depth = -y * Forward(receiver);
            if (depth < 0 || depth > ServiceLine) { return false; }

            // サーバーのデュースサイドは自分の右。対角のボックスは受ける側から見て右（受ける側のデュースサイド）。
            var receiverRight = RightOf(receiver);
            var lateral = x * receiverRight;
            return court == ServeCourt.Deuce ? lateral >= 0 && lateral <= HalfWidth : lateral <= 0 && lateral >= -HalfWidth;
        }

        /// <summary>その側の選手から見た右（デュースサイド）か左（アドサイド）の x の符号。</summary>
        public static int CourtSign(Side side, ServeCourt court) => court == ServeCourt.Deuce ? RightOf(side) : -RightOf(side);
    }

    /// <summary>ポイントの始めに選手が立つ位置。</summary>
    public static class Positions
    {
        /// <summary>サーバーがポイントの始めに立つ位置。</summary>
        public static Vec3 Serve(Side server, ServeCourt court, TennisConfig config) => new(
            Court.CourtSign(server, court) * config.ServeSideOffset,
            -(Court.HalfLength + config.ServeBackOffset) * Court.Forward(server),
            0);

        /// <summary>レシーバーがポイントの始めに立つ位置。</summary>
        public static Vec3 Receive(Side receiver, ServeCourt court, TennisConfig config) => new(
            Court.CourtSign(receiver, court) * config.ReceiveSideOffset,
            -(Court.HalfLength + config.ReceiveBackOffset) * Court.Forward(receiver),
            0);
    }
}
