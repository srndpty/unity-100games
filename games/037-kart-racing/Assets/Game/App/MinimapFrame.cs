using System;
using Game.Core;
using IntGeometry;

namespace Game.App
{
    /// <summary>ミニマップ上の点（UI の座標。左上が原点で、y は下向き）。</summary>
    public readonly struct MapPoint
    {
        public MapPoint(float x, float y)
        {
            X = x;
            Y = y;
        }

        public float X { get; }

        public float Y { get; }
    }

    /// <summary>
    /// コース全体（ガード壁まで）を、幅 <see cref="Width"/>・高さ <see cref="Height"/> の箱へ縦横比を保って収め、中央に置く写像。
    /// コースの北（+Y）が箱の上になる。
    /// </summary>
    public sealed class MinimapFrame
    {
        private readonly int _minX;
        private readonly int _maxY;
        private readonly float _offsetX;
        private readonly float _offsetY;

        public MinimapFrame(Track track, float width, float height, float padding)
        {
            if (track == null) { throw new ArgumentNullException(nameof(track)); }
            if (!float.IsFinite(width) || !float.IsFinite(height) || !float.IsFinite(padding) || padding < 0f || width <= 2f * padding || height <= 2f * padding)
            {
                throw new ArgumentOutOfRangeException(nameof(padding), "箱の大きさと余白が正しくありません。");
            }

            var minX = int.MaxValue;
            var maxX = int.MinValue;
            var minY = int.MaxValue;
            var maxY = int.MinValue;
            for (var i = 0; i < track.StationCount; i++)
            {
                Include(track.GuardLeft(i), ref minX, ref maxX, ref minY, ref maxY);
                Include(track.GuardRight(i), ref minX, ref maxX, ref minY, ref maxY);
            }

            var spanX = Math.Max(1L, (long)maxX - minX);
            var spanY = Math.Max(1L, (long)maxY - minY);
            Width = width;
            Height = height;
            Scale = Math.Min((width - (2f * padding)) / spanX, (height - (2f * padding)) / spanY);
            _minX = minX;
            _maxY = maxY;
            _offsetX = (width - (spanX * Scale)) * 0.5f;
            _offsetY = (height - (spanY * Scale)) * 0.5f;
        }

        public float Width { get; }

        public float Height { get; }

        /// <summary>1 mm あたりの UI の長さ。</summary>
        public float Scale { get; }

        public MapPoint Map(Vec2i point) => new(
            _offsetX + (((long)point.X - _minX) * Scale),
            _offsetY + (((long)_maxY - point.Y) * Scale));

        private static void Include(Vec2i point, ref int minX, ref int maxX, ref int minY, ref int maxY)
        {
            if (point.X < minX) { minX = point.X; }
            if (point.X > maxX) { maxX = point.X; }
            if (point.Y < minY) { minY = point.Y; }
            if (point.Y > maxY) { maxY = point.Y; }
        }
    }
}
