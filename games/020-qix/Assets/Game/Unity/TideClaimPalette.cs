using Game.Core;
using UnityEngine;

namespace Game.Unity
{
    /// <summary>
    /// セルの色。実行時の盤の描画と、エディタで描き出す盤の初期画像（Scene ビュー用）の両方が使う。
    /// 模様はセルの位置の整数ハッシュで決めるので、同じ盤なら毎回同じ画像になる。
    /// </summary>
    public static class TideClaimPalette
    {
        public static readonly Color32 Sea = new(28, 78, 112, 255);
        public static readonly Color32 SeaCrest = new(46, 104, 136, 255);
        public static readonly Color32 Land = new(112, 146, 88, 255);
        public static readonly Color32 Rim = new(222, 204, 156, 255);
        public static readonly Color32 Trail = new(255, 240, 128, 255);

        /// <summary>セルの色。rim は線を引いていない自機が歩ける縁か。</summary>
        public static Color32 CellColor(Cell cell, bool rim, int x, int y)
        {
            switch (cell)
            {
                case Cell.Trail:
                    return Trail;
                case Cell.Claimed:
                    return rim ? Rim : Shade(Land, Noise(x, y) % 13 - 6);
                default:
                    // 斜めの波の筋と、小さなゆらぎ。
                    var crest = (x + (2 * y)) % 11 == 0 || (x + (2 * y) + 1) % 11 == 0 && Noise(x, y) % 3 == 0;
                    return Shade(crest ? SeaCrest : Sea, Noise(x, y) % 7 - 3);
            }
        }

        /// <summary>試合の前（タイトル）の盤の色。外周 1 セルが縁、内側が海。</summary>
        public static Color32 InitialColor(int x, int y, int width, int height)
        {
            var edge = x == 0 || y == 0 || x == width - 1 || y == height - 1;
            return CellColor(edge ? Cell.Claimed : Cell.Open, edge, x, y);
        }

        private static int Noise(int x, int y)
        {
            unchecked
            {
                var h = (uint)((x * 73856093) ^ (y * 19349663));
                h ^= h >> 13;
                h *= 0x5BD1E995u;
                h ^= h >> 15;
                return (int)(h & 0x7FFF);
            }
        }

        private static Color32 Shade(Color32 color, int delta) => new(
            (byte)Mathf.Clamp(color.r + delta, 0, 255),
            (byte)Mathf.Clamp(color.g + delta, 0, 255),
            (byte)Mathf.Clamp(color.b + delta, 0, 255),
            255);
    }
}
