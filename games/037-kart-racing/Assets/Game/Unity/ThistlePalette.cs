using UnityEngine;

namespace Game.Unity
{
    /// <summary>コース・車・HUD で共有する色（sRGB）。</summary>
    public static class ThistlePalette
    {
        public static readonly Color Field = new(0.20f, 0.36f, 0.20f);

        public static readonly Color Runoff = new(0.36f, 0.56f, 0.27f);

        public static readonly Color Asphalt = new(0.30f, 0.31f, 0.33f);

        public static readonly Color Line = new(0.93f, 0.93f, 0.90f);

        public static readonly Color CurbRed = new(0.82f, 0.16f, 0.14f);

        public static readonly Color CurbWhite = new(0.94f, 0.94f, 0.92f);

        public static readonly Color BarrierA = new(0.16f, 0.30f, 0.70f);

        public static readonly Color BarrierB = new(0.92f, 0.92f, 0.92f);

        public static readonly Color Checker = new(0.08f, 0.08f, 0.09f);

        private static readonly Color[] Karts =
        {
            new(0.97f, 0.76f, 0.12f),
            new(0.85f, 0.16f, 0.16f),
            new(0.16f, 0.42f, 0.88f),
            new(0.18f, 0.66f, 0.34f),
        };

        /// <summary>車の番号の色（スプライトの車体と同じ）。</summary>
        public static Color Kart(int index) => Karts[index % Karts.Length];
    }
}
