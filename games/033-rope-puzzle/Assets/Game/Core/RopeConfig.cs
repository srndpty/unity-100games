using System;
using IntGeometry;

namespace Game.Core
{
    /// <summary>
    /// 盤の大きさと座標の単位。座標は整数で、1 マス = <see cref="Unit"/> 単位。盤は 16 × 9 マス、原点は左下、y は上向き。
    /// 問題データの座標は 1/16 マス（<see cref="DataUnit"/> 単位）で書く。
    /// </summary>
    public static class Board
    {
        public const int Unit = 65536;

        /// <summary>問題データの座標の 1 目盛り（1/16 マス）。</summary>
        public const int DataUnit = Unit / 16;

        public const int Columns = 16;

        public const int Rows = 9;

        public const int Width = Columns * Unit;

        public const int Height = Rows * Unit;

        /// <summary>なぞりの端点として受け付ける範囲（盤の外に 1 盤ぶん）。外積が long に収まる範囲に限る。</summary>
        public static bool AcceptsSwipePoint(Vec2i point) =>
            point.X >= -Width && point.X <= 2 * Width && point.Y >= -Height && point.Y <= 2 * Height;

        public static bool Contains(Vec2i point) => point.X >= 0 && point.X <= Width && point.Y >= 0 && point.Y <= Height;
    }

    /// <summary>
    /// 物理の設定。値は全て整数（1 Tick = 1/60 秒、長さは <see cref="Board.Unit"/> 単位）。
    /// Unity のシリアライズのため公開フィールドで書く。使う前に <see cref="Validate"/> で検証する。
    /// </summary>
    [Serializable]
    public sealed class RopeConfig
    {
        /// <summary>減衰などの比の分母。</summary>
        public const int RatioDenominator = 4096;

        /// <summary>重力（1 Tick あたりの速さの増分、単位/Tick²）。</summary>
        public int Gravity = 288;

        /// <summary>1 Tick ごとに速さに掛ける比（/ <see cref="RatioDenominator"/>）。0 方向へ切り捨てるので、小さい速さは止まる。</summary>
        public int Damping = 4086;

        /// <summary>距離拘束の反復回数。</summary>
        public int Iterations = 24;

        /// <summary>露玉の半径。</summary>
        public int BodyRadius = 22528;

        /// <summary>ロープの節点の逆質量（重さの逆数の比）。</summary>
        public int NodeInverseMass = 32;

        /// <summary>露玉の逆質量。節点より小さい（重い）。</summary>
        public int BodyInverseMass = 1;

        /// <summary>1 Tick に点が動ける距離の上限。</summary>
        public int MaxStep = 20480;

        /// <summary>壁に触れている間、壁に沿った速さに掛ける比（/ <see cref="RatioDenominator"/>）。</summary>
        public int WallFriction = 3584;

        /// <summary>1 本のロープの節の数の上限（節点の数 + 1）。</summary>
        public int MaxLinks = 32;

        public RopeConfig Copy() => (RopeConfig)MemberwiseClone();

        /// <summary>範囲外の値があれば例外。</summary>
        public void Validate()
        {
            Require(Gravity, 1, 4096, nameof(Gravity));
            Require(Damping, RatioDenominator / 2, RatioDenominator, nameof(Damping));
            Require(Iterations, 1, 64, nameof(Iterations));
            Require(BodyRadius, Board.Unit / 16, Board.Unit, nameof(BodyRadius));
            Require(NodeInverseMass, 1, 64, nameof(NodeInverseMass));
            Require(BodyInverseMass, 1, 64, nameof(BodyInverseMass));
            Require(MaxStep, Gravity + 1, Board.Unit, nameof(MaxStep));
            Require(WallFriction, 0, RatioDenominator, nameof(WallFriction));
            Require(MaxLinks, 2, 64, nameof(MaxLinks));
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
