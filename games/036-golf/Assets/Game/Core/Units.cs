using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("Game.Core.Tests")]
[assembly: InternalsVisibleTo("G036.Core.Tests")]

namespace Game.Core
{
    /// <summary>
    /// 長さと時間の単位。長さは整数の µm（1 m = <see cref="PerMeter"/>）、時間は Tick（1 秒 = <see cref="TickRate"/>）。
    /// 速度は µm/Tick、加速度は µm/Tick²。浮動小数点を使わず、Mono と .NET で同じ結果にする。
    /// </summary>
    public static class Units
    {
        public const int PerMeter = 1_000_000;

        public const int TickRate = 60;

        /// <summary>1 Tick の経路を何等分した点の列とみなすか（木・カップとの接触は、この点のどれかが円の内側に入ったかで決める）。</summary>
        public const int Samples = 256;

        /// <summary>
        /// 1 Tick の水平の移動量の各成分の上限（掃引の計算を long に収める。設定の検証で保証し、超えたら例外）。
        /// 円の半径の上限 <see cref="MaxCircleRadius"/> と合わせて、Samples × (半径 + 2 × 移動) の 2 乗の 2 倍が long に収まる。
        /// </summary>
        public const int MaxTickMove = 2_000_000;

        /// <summary>掃引する円（木・カップ）の半径の上限（4 m）。</summary>
        public const int MaxCircleRadius = 4_000_000;

        /// <summary>分数の係数の分母（反発・水平の残り）。</summary>
        public const int Ratio = 4096;

        /// <summary>抵抗・風の係数の分母。</summary>
        public const int Fine = 65536;
    }
}
