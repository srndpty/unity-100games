using System;
using IntGeometry;

namespace Game.Core
{
    /// <summary>
    /// 近傍探索の一様グリッド。自機を中心に 1 辺 <c>cells</c> マスを置き、毎 Tick、点の番号を計数ソートでマスごとに詰め直す
    /// （固定長の配列だけを使うので、作り直しで割り当てをしない）。
    /// <para>
    /// 範囲の外の点は端のマスに入れる（座標をマスの範囲へ寄せる）。マスの番号への寄せは単調なので、
    /// 「矩形の中の点」は寄せた矩形のマスに必ず含まれる（範囲の外の点を問い合わせで見逃さない）。候補には矩形の外の点も混ざるので、
    /// 呼び出し側が正確な距離で絞る。
    /// </para>
    /// </summary>
    public sealed class SpatialGrid
    {
        private readonly int _cells;
        private readonly int _cellSize;
        private readonly int[] _start;
        private readonly int[] _cellOf;
        private readonly int[] _items;
        private int _originX;
        private int _originY;
        private int _count;

        public SpatialGrid(int cells, int cellSize, int capacity)
        {
            if (cells < 1) { throw new ArgumentOutOfRangeException(nameof(cells)); }
            if (cellSize < 1) { throw new ArgumentOutOfRangeException(nameof(cellSize)); }
            if (capacity < 0) { throw new ArgumentOutOfRangeException(nameof(capacity)); }
            _cells = cells;
            _cellSize = cellSize;
            _start = new int[(cells * cells) + 1];
            _cellOf = new int[capacity];
            _items = new int[capacity];
        }

        /// <summary>入っている点の数。</summary>
        public int Count => _count;

        /// <summary>点 0〜<paramref name="count"/>-1（座標は <paramref name="xs"/>・<paramref name="ys"/>）を、<paramref name="center"/> を中心にしたマスへ詰め直す。</summary>
        public void Build(Vec2i center, int count, int[] xs, int[] ys)
        {
            if (count < 0 || count > _items.Length) { throw new ArgumentOutOfRangeException(nameof(count), count, "容量を超えています。"); }
            _count = count;
            var half = _cells * _cellSize / 2;
            _originX = center.X - half;
            _originY = center.Y - half;
            Array.Clear(_start, 0, _start.Length);
            for (var i = 0; i < count; i++)
            {
                var cell = (CellY(ys[i]) * _cells) + CellX(xs[i]);
                _cellOf[i] = cell;
                _start[cell + 1]++;
            }

            for (var c = 0; c < _cells * _cells; c++) { _start[c + 1] += _start[c]; }

            // 番号の小さい順に入れる（同じマスの中は番号順。結果が入力の並びだけで決まる）。_start[c] を書き込み位置として進め、後で戻す。
            for (var i = 0; i < count; i++) { _items[_start[_cellOf[i]]++] = i; }
            for (var c = (_cells * _cells) - 1; c > 0; c--) { _start[c] = _start[c - 1]; }
            _start[0] = 0;
        }

        /// <summary>
        /// 矩形 [<paramref name="minX"/>, <paramref name="maxX"/>]×[<paramref name="minY"/>, <paramref name="maxY"/>] に重なるマスの点の番号を
        /// <paramref name="buffer"/> に書き、数を返す。矩形の中の点はすべて含まれる（外の点も混ざりうる）。
        /// </summary>
        public int Collect(int minX, int minY, int maxX, int maxY, int[] buffer)
        {
            if (buffer == null) { throw new ArgumentNullException(nameof(buffer)); }
            if (maxX < minX || maxY < minY) { return 0; }
            var x0 = CellX(minX);
            var x1 = CellX(maxX);
            var y0 = CellY(minY);
            var y1 = CellY(maxY);
            var written = 0;
            for (var cy = y0; cy <= y1; cy++)
            {
                var from = _start[(cy * _cells) + x0];
                var to = _start[(cy * _cells) + x1 + 1];
                if (to - from > buffer.Length - written) { throw new ArgumentException("buffer が小さすぎます。", nameof(buffer)); }
                Array.Copy(_items, from, buffer, written, to - from);
                written += to - from;
            }

            return written;
        }

        private int CellX(int x) => Cell(x, _originX);

        private int CellY(int y) => Cell(y, _originY);

        private int Cell(int value, int origin)
        {
            var offset = (long)value - origin;
            if (offset < 0) { return 0; }
            var cell = offset / _cellSize;
            return cell >= _cells ? _cells - 1 : (int)cell;
        }
    }
}
