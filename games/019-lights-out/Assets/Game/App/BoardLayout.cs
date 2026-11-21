using System;
using Game.Core;

namespace Game.App
{
    /// <summary>
    /// マスの置き場所。盤の中心を原点とし、x は右、y は上が正（行 0 が最上段）。
    /// 描画とクリックの当たり判定が同じ計算を使う。
    /// </summary>
    public sealed class BoardLayout
    {
        public BoardLayout(float pitch, float hitSize)
        {
            if (!float.IsFinite(pitch) || pitch <= 0) { throw new ArgumentOutOfRangeException(nameof(pitch)); }
            if (!float.IsFinite(hitSize) || hitSize <= 0 || hitSize > pitch)
            {
                throw new ArgumentOutOfRangeException(nameof(hitSize), hitSize, "0 より大きく、間隔以下で指定してください。");
            }

            Pitch = pitch;
            HitSize = hitSize;
        }

        /// <summary>隣のマスの中心までの距離。</summary>
        public float Pitch { get; }

        /// <summary>クリックを受け付けるマスの一辺。間隔より小さければ、マスの間の隙間は押さない。</summary>
        public float HitSize { get; }

        /// <summary>盤全体の一辺（両端のマスの外縁まで）。</summary>
        public float Extent => ((LightsBoard.Size - 1) * Pitch) + HitSize;

        public (float X, float Y) CellCenter(int cell)
        {
            LightsBoard.RequireIndex(cell);
            var half = (LightsBoard.Size - 1) / 2f;
            var x = cell % LightsBoard.Size;
            var y = cell / LightsBoard.Size;
            return ((x - half) * Pitch, (half - y) * Pitch);
        }

        /// <summary>盤の座標にあるマス。マスの外（隙間や盤の外）なら false。</summary>
        public bool TryCellAt(float x, float y, out int cell)
        {
            cell = -1;
            if (!float.IsFinite(x) || !float.IsFinite(y)) { return false; }
            var half = (LightsBoard.Size - 1) / 2f;
            var column = (int)Math.Round((x / Pitch) + half);
            var row = (int)Math.Round(half - (y / Pitch));
            if (column < 0 || column >= LightsBoard.Size || row < 0 || row >= LightsBoard.Size) { return false; }
            var candidate = LightsBoard.IndexOf(column, row);
            var (cx, cy) = CellCenter(candidate);
            var reach = HitSize / 2f;
            if (Math.Abs(x - cx) > reach || Math.Abs(y - cy) > reach) { return false; }
            cell = candidate;
            return true;
        }
    }
}
