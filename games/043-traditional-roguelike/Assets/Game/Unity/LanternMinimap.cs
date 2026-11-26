using System;
using Game.Core;
using IntGeometry;
using UnityEngine;
using Tile = Game.Core.Tile;

namespace Game.Unity
{
    /// <summary>
    /// 全体の地図（押している間だけ重ねて出す）の絵。<see cref="PlayerView"/> だけを読み、1 マスを <see cref="Scale"/> 画素の四角にした簡略な絵にする。
    /// 床（部屋・通路）と階段は既知のマスだけ、品は記憶の品、敵は視界の中だけ、自機は今の位置。壁は描かない。
    /// テクスチャはこのクラスが持ち、<see cref="Dispose"/> で破棄する。
    /// </summary>
    public sealed class LanternMinimap : IDisposable
    {
        /// <summary>1 マスの画素数。印（自機・敵・品）はこの四角の中に描く。</summary>
        public const int Scale = 3;

        public static readonly Color32 Empty = new(0, 0, 0, 0);
        public static readonly Color32 Room = new(120, 170, 230, 85);
        public static readonly Color32 Corridor = new(100, 150, 210, 85);
        public static readonly Color32 Stairs = new(120, 240, 220, 230);
        public static readonly Color32 Item = new(255, 220, 90, 255);
        public static readonly Color32 Monster = new(255, 70, 60, 255);
        public static readonly Color32 Player = new(255, 255, 210, 255);

        private readonly Color32[] _pixels;
        private PlayerView _shownView;
        private int _shownVersion = -1;

        public LanternMinimap(int width, int height)
        {
            if (width <= 0 || height <= 0) { throw new ArgumentOutOfRangeException(nameof(width)); }
            Width = width;
            Height = height;
            _pixels = new Color32[width * Scale * height * Scale];
            Texture = new Texture2D(width * Scale, height * Scale, TextureFormat.RGBA32, false)
            {
                name = "LanternMinimap",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
            };
            Texture.SetPixels32(_pixels);
            Texture.Apply();
        }

        public int Width { get; }

        public int Height { get; }

        public Texture2D Texture { get; }

        /// <summary>今の絵のマスの中央の色（テスト用）。</summary>
        public Color32 CenterOf(Vec2i p) => _pixels[PixelIndex(p, 1, 1)];

        /// <summary>今の絵のマスの角の色（地形の色。テスト用）。</summary>
        public Color32 CornerOf(Vec2i p) => _pixels[PixelIndex(p, 0, 0)];

        /// <summary>窓口の今の状態を描く。版が変わっていなければ何もしない。</summary>
        public void Paint(PlayerView view)
        {
            if (view == null) { throw new ArgumentNullException(nameof(view)); }
            if (ReferenceEquals(view, _shownView) && view.Version == _shownVersion) { return; }
            _shownView = view;
            _shownVersion = view.Version;
            for (int y = 0; y < Height; y++)
            {
                for (int x = 0; x < Width; x++)
                {
                    var p = new Vec2i(x, y);
                    var tile = view.TileAt(p);
                    Fill(p, tile switch
                    {
                        Tile.Room => Room,
                        Tile.Corridor => Corridor,
                        Tile.Stairs => Stairs,
                        _ => Empty,
                    });
                    if (view.ItemAt(p).HasValue) { Dot(p, Item); }
                }
            }

            for (int i = 0; i < view.Monsters.Count; i++) { Cross(view.Monsters[i].Position, Monster); }
            Cross(view.Position, Player);
            Texture.SetPixels32(_pixels);
            Texture.Apply();
        }

        public void Dispose() => UnityEngine.Object.Destroy(Texture);

        // テクスチャの行は下から上なので、Core の y（下向き）を反転する。
        private int PixelIndex(Vec2i p, int dx, int dy) => ((((Height - 1 - p.Y) * Scale) + dy) * Width * Scale) + (p.X * Scale) + dx;

        private void Fill(Vec2i p, Color32 color)
        {
            for (int dy = 0; dy < Scale; dy++)
            {
                for (int dx = 0; dx < Scale; dx++) { _pixels[PixelIndex(p, dx, dy)] = color; }
            }
        }

        private void Dot(Vec2i p, Color32 color) => _pixels[PixelIndex(p, 1, 1)] = color;

        private void Cross(Vec2i p, Color32 color)
        {
            Dot(p, color);
            _pixels[PixelIndex(p, 0, 1)] = color;
            _pixels[PixelIndex(p, 2, 1)] = color;
            _pixels[PixelIndex(p, 1, 0)] = color;
            _pixels[PixelIndex(p, 1, 2)] = color;
        }
    }
}
