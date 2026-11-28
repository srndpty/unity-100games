using System;

namespace Game.Core
{
    /// <summary>石を置けるかの判定（盤だけで決まる部分。同型反復は <see cref="GoGame"/> が見る）。</summary>
    public enum PlacementVerdict
    {
        Ok = 0,
        OffBoard,
        Occupied,
        Suicide,
    }

    /// <summary>着手を適用する前の見積もり。適用後の盤面のハッシュと取れる石が分かる。</summary>
    public readonly struct PlacementPreview
    {
        public PlacementPreview(PlacementVerdict verdict, PointSet captured, ulong hashAfter, int libertiesBeforeCapture = 0)
        {
            Verdict = verdict;
            Captured = captured;
            HashAfter = hashAfter;
            LibertiesBeforeCapture = libertiesBeforeCapture;
        }

        /// <summary>置いた石の連の呼吸点の数（取る石の跡を足す前）。プレイアウトの「逃げる」の判定に使う。</summary>
        public int LibertiesBeforeCapture { get; }

        public PlacementVerdict Verdict { get; }

        public PointSet Captured { get; }

        public ulong HashAfter { get; }

        public bool IsOk => Verdict == PlacementVerdict.Ok;
    }

    /// <summary>
    /// 9 路の盤。交点の色に加え、連（つながった同じ色の石）ごとの石と呼吸点の集合を着手のたびに差分で更新する。
    /// 連の番号は連に属するどれかの交点の番号（つないだときは置いた点）。盤面の Zobrist ハッシュも差分で持つ。
    /// 書き換え（<see cref="Play"/>）は Core の内部とテストだけが使う。画面は読むだけ。
    /// </summary>
    public sealed class Board
    {
        private readonly Stone[] _cells;
        private readonly int[] _chainOf;
        private readonly PointSet[] _stones;
        private readonly PointSet[] _liberties;

        public Board()
            : this(Zobrist.Standard)
        {
        }

        internal Board(Zobrist zobrist)
        {
            Zobrist = zobrist ?? throw new ArgumentNullException(nameof(zobrist));
            _cells = new Stone[Points.Count];
            _chainOf = new int[Points.Count];
            _stones = new PointSet[Points.Count];
            _liberties = new PointSet[Points.Count];
            for (var p = 0; p < Points.Count; p++) { _chainOf[p] = -1; }
        }

        private Board(Board source)
        {
            Zobrist = source.Zobrist;
            _cells = (Stone[])source._cells.Clone();
            _chainOf = (int[])source._chainOf.Clone();
            _stones = (PointSet[])source._stones.Clone();
            _liberties = (PointSet[])source._liberties.Clone();
            Hash = source.Hash;
        }

        public Zobrist Zobrist { get; }

        /// <summary>盤面（石の配置）の Zobrist ハッシュ。</summary>
        public ulong Hash { get; private set; }

        public Stone this[int point] => _cells[point];

        /// <summary>盤の上の石の数。</summary>
        public int StoneCount(Stone color)
        {
            var n = 0;
            for (var p = 0; p < Points.Count; p++)
            {
                if (_cells[p] == color) { n++; }
            }

            return n;
        }

        /// <summary>その点の石が属する連の石。空点なら空集合。</summary>
        public PointSet ChainAt(int point) => _chainOf[point] < 0 ? PointSet.Empty : _stones[_chainOf[point]];

        /// <summary>その点の石が属する連の呼吸点。空点なら空集合。</summary>
        public PointSet LibertiesAt(int point) => _chainOf[point] < 0 ? PointSet.Empty : _liberties[_chainOf[point]];

        /// <summary>その点の石が属する連の呼吸点の数。空点なら 0。</summary>
        public int LibertyCount(int point) => LibertiesAt(point).Count;

        /// <summary>連の番号（空点は -1）。テストで連の分け方を参照実装と比べる。</summary>
        internal int ChainId(int point) => _chainOf[point];

        public Board Clone() => new(this);

        /// <summary>石の配置の複製（同型反復の盤面比較と画面の写しに使う）。</summary>
        public Stone[] CopyCells() => (Stone[])_cells.Clone();

        /// <summary>石の配置から盤を組む。呼吸点のない連があれば拒否する（着手で作れない盤）。</summary>
        public static Board FromCells(Stone[] cells) => FromCells(cells, Zobrist.Standard);

        internal static Board FromCells(Stone[] cells, Zobrist zobrist)
        {
            if (cells == null || cells.Length != Points.Count) { throw new ArgumentException($"交点は {Points.Count} 個です。", nameof(cells)); }
            var board = new Board(zobrist);
            for (var p = 0; p < Points.Count; p++)
            {
                if (cells[p] != Stone.Empty && !cells[p].IsColor()) { throw new ArgumentException($"{Points.Name(p)} の値 {cells[p]} は石ではありません。", nameof(cells)); }
                board._cells[p] = cells[p];
                if (cells[p] != Stone.Empty) { board.Hash ^= zobrist.Key(p, cells[p]); }
            }

            for (var p = 0; p < Points.Count; p++)
            {
                if (board._cells[p] == Stone.Empty || board._chainOf[p] >= 0) { continue; }
                board.Rebuild(p);
                if (board._liberties[p].IsEmpty) { throw new ArgumentException($"{Points.Name(p)} の連に呼吸点がありません。", nameof(cells)); }
            }

            return board;
        }

        /// <summary>盤を変えずに、置いた結果（取れる石・自殺手か・適用後のハッシュ）を見積もる。</summary>
        public PlacementPreview Preview(int point, Stone color)
        {
            if (!color.IsColor()) { throw new ArgumentOutOfRangeException(nameof(color), color, "黒か白を指定してください。"); }
            if (!Points.IsOnBoard(point)) { return new PlacementPreview(PlacementVerdict.OffBoard, PointSet.Empty, Hash); }
            if (_cells[point] != Stone.Empty) { return new PlacementPreview(PlacementVerdict.Occupied, PointSet.Empty, Hash); }

            var captured = PointSet.Empty;
            var ownLiberties = PointSet.Empty;
            foreach (var n in Points.Neighbors(point))
            {
                var s = _cells[n];
                if (s == Stone.Empty)
                {
                    ownLiberties = ownLiberties.With(n);
                }
                else if (s == color)
                {
                    ownLiberties = ownLiberties.Union(_liberties[_chainOf[n]]);
                }
                else
                {
                    var libs = _liberties[_chainOf[n]];
                    if (libs.Count == 1 && libs.Contains(point)) { captured = captured.Union(_stones[_chainOf[n]]); }
                }
            }

            ownLiberties = ownLiberties.Without(point);
            if (captured.IsEmpty && ownLiberties.IsEmpty) { return new PlacementPreview(PlacementVerdict.Suicide, PointSet.Empty, Hash); }

            var hash = Hash ^ Zobrist.Key(point, color);
            var opponent = color.Opponent();
            foreach (var q in captured) { hash ^= Zobrist.Key(q, opponent); }
            return new PlacementPreview(PlacementVerdict.Ok, captured, hash, ownLiberties.Count);
        }

        /// <summary>
        /// 石を置いて、呼吸点のなくなった相手の連を取る。置けない手（<see cref="Preview"/> が Ok でない）は例外。
        /// 戻り値は取った石。
        /// </summary>
        internal PointSet Play(int point, Stone color)
        {
            var preview = Preview(point, color);
            if (!preview.IsOk) { throw new InvalidOperationException($"{Points.Name(point)} に{color.Label()}は置けません: {preview.Verdict}"); }

            var opponent = color.Opponent();
            _cells[point] = color;
            Hash ^= Zobrist.Key(point, color);

            // 同じ色の隣の連と、置いた石をつなぐ。新しい連の番号は置いた点。
            var stones = PointSet.Of(point);
            var liberties = PointSet.Empty;
            foreach (var n in Points.Neighbors(point))
            {
                var s = _cells[n];
                if (s == Stone.Empty)
                {
                    liberties = liberties.With(n);
                }
                else if (s == color)
                {
                    var id = _chainOf[n];
                    stones = stones.Union(_stones[id]);
                    liberties = liberties.Union(_liberties[id]);
                }
            }

            liberties = liberties.Without(point);
            foreach (var q in stones) { _chainOf[q] = point; }
            _stones[point] = stones;
            _liberties[point] = liberties;

            // 隣の相手の連から置いた点の呼吸点を除き、0 になった連を取る。
            var captured = PointSet.Empty;
            foreach (var n in Points.Neighbors(point))
            {
                if (_cells[n] != opponent) { continue; }
                var id = _chainOf[n];
                _liberties[id] = _liberties[id].Without(point);
                if (_liberties[id].IsEmpty) { captured = captured.Union(_stones[id]); }
            }

            foreach (var q in captured)
            {
                _cells[q] = Stone.Empty;
                _chainOf[q] = -1;
                Hash ^= Zobrist.Key(q, opponent);
            }

            // 取った石の跡は、隣の連（全て置いた側の色）の呼吸点になる。
            foreach (var q in captured)
            {
                foreach (var m in Points.Neighbors(q))
                {
                    if (_cells[m] == color)
                    {
                        var id = _chainOf[m];
                        _liberties[id] = _liberties[id].With(q);
                    }
                }
            }

            return captured;
        }

        /// <summary>
        /// 自分の眼か：上下左右が全て自分の石で、斜めの相手の石が中央なら 1 つ以下、辺・隅なら 0。
        /// 探索とプレイアウトで「自分の眼を埋める手」を除くのに使う（真の眼の簡易判定）。
        /// </summary>
        public bool IsOwnEye(int point, Stone color)
        {
            if (_cells[point] != Stone.Empty) { return false; }
            foreach (var n in Points.Neighbors(point))
            {
                if (_cells[n] != color) { return false; }
            }

            var opponent = color.Opponent();
            var diagonals = Points.Diagonals(point);
            var bad = 0;
            foreach (var d in diagonals)
            {
                if (_cells[d] == opponent) { bad++; }
            }

            return diagonals.Length == 4 ? bad <= 1 : bad == 0;
        }

        /// <summary>その点を含む連を塗りつぶしで求め直し、番号をその点にする（盤を組むときだけ使う）。</summary>
        private void Rebuild(int start)
        {
            var color = _cells[start];
            var stones = PointSet.Of(start);
            var liberties = PointSet.Empty;
            var stack = new int[Points.Count];
            var top = 0;
            stack[top++] = start;
            while (top > 0)
            {
                var p = stack[--top];
                foreach (var n in Points.Neighbors(p))
                {
                    if (_cells[n] == Stone.Empty)
                    {
                        liberties = liberties.With(n);
                    }
                    else if (_cells[n] == color && !stones.Contains(n))
                    {
                        stones = stones.With(n);
                        stack[top++] = n;
                    }
                }
            }

            foreach (var q in stones) { _chainOf[q] = start; }
            _stones[start] = stones;
            _liberties[start] = liberties;
        }
    }
}
