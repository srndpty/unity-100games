using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("Game.Core.Tests")]
[assembly: InternalsVisibleTo("G038.Core.Tests")]
[assembly: InternalsVisibleTo("Game.App.Tests")]
[assembly: InternalsVisibleTo("G038.App.Tests")]

namespace Game.Core
{
    /// <summary>
    /// 長さ・時間・角度の単位。長さの 1 単位 = 1 µm（速さは µm/Tick、加速は µm/Tick²）。1 秒 = 60 Tick。
    /// 角度は <c>IntGeometry.Trig</c> と同じ 1 周 = 65536。
    /// </summary>
    public static class Units
    {
        public const int TickRate = 60;

        public const long PerMeter = 1_000_000;

        public const long PerMillimeter = 1_000;

        /// <summary>1 周の角度の単位数。</summary>
        public const int FullTurn = 65536;

        /// <summary>m/s を µm/Tick に直す（設定の既定値を読みやすく書くため）。</summary>
        public const int MetersPerSecond = (int)(PerMeter / TickRate);

        /// <summary>度を角度の単位へ直す（四捨五入）。</summary>
        public static int Degrees(int degrees) => (int)(((long)degrees * FullTurn + 180) / 360);
    }
}
