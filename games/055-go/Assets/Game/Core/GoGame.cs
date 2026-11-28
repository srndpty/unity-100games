using System;
using System.Collections.Generic;

namespace Game.Core
{
    /// <summary>着手・パス・投了の判定。Ok 以外は局面を変えない。</summary>
    public enum MoveVerdict
    {
        Ok = 0,
        GameOver,
        NotYourTurn,
        OffBoard,
        Occupied,
        Suicide,

        /// <summary>単純コウ：直前に 1 子を取られた点で、その 1 子を取り返す手。</summary>
        Ko,

        /// <summary>同型反復：着手後の盤面が、対局中に現れた盤面と同じになる（単純コウ以外）。</summary>
        Superko,
    }

    /// <summary>終局の理由。</summary>
    public enum GameEnd
    {
        None = 0,
        TwoPasses,
        Resignation,
        MoveLimit,
    }

    /// <summary>
    /// 1 局の進行：手番・パス・投了・終局と、同型反復を判定する盤面の履歴。
    /// 履歴は各手の後の盤面の Zobrist ハッシュと配置を持つ。ハッシュが一致したら配置そのものを比べて確定する（衝突で誤って拒否しない）。
    /// </summary>
    public sealed class GoGame
    {
        private readonly Board _board;
        private readonly List<ulong> _hashes;
        private readonly List<Stone[]> _snapshots;
        private readonly HashSet<ulong> _seen;
        private readonly List<int> _moves;
        private readonly int[] _captures = new int[3];

        public GoGame(GoConfig config)
            : this(config, new Board(), Stone.Black)
        {
        }

        /// <summary>局面を直接作る（テスト・撮影の決まった局面）。履歴はこの盤面から始まる。</summary>
        public GoGame(GoConfig config, Board board, Stone toMove)
        {
            if (config == null) { throw new ArgumentNullException(nameof(config)); }
            var errors = config.Validate();
            if (errors.Count > 0) { throw new ArgumentException("設定が範囲外です: " + string.Join(" / ", errors), nameof(config)); }
            if (!toMove.IsColor()) { throw new ArgumentOutOfRangeException(nameof(toMove), toMove, "手番は黒か白です。"); }
            Config = config.Copy();
            _board = board?.Clone() ?? throw new ArgumentNullException(nameof(board));
            ToMove = toMove;
            _hashes = new List<ulong> { _board.Hash };
            _snapshots = new List<Stone[]> { _board.CopyCells() };
            _seen = new HashSet<ulong> { _board.Hash };
            _moves = new List<int>();
        }

        private GoGame(GoGame source)
        {
            Config = source.Config;
            _board = source._board.Clone();
            _hashes = new List<ulong>(source._hashes);
            _snapshots = new List<Stone[]>(source._snapshots); // 配置は追加した後に書き換えないので共有してよい
            _seen = new HashSet<ulong>(source._seen);
            _moves = new List<int>(source._moves);
            Array.Copy(source._captures, _captures, _captures.Length);
            ToMove = source.ToMove;
            ConsecutivePasses = source.ConsecutivePasses;
            End = source.End;
            Resigned = source.Resigned;
        }

        /// <summary>検証済みの設定の複製（対局の間は変わらない）。</summary>
        internal GoConfig Config { get; }

        public int Komi => Config.Komi;

        public Stone ToMove { get; private set; }

        /// <summary>これまでの手（着手の点かパス）。投了は含めない。</summary>
        public int MoveCount => _moves.Count;

        public int ConsecutivePasses { get; private set; }

        public GameEnd End { get; private set; }

        public bool IsOver => End != GameEnd.None;

        /// <summary>投了した側（投了でなければ Empty）。</summary>
        public Stone Resigned { get; private set; }

        /// <summary>直前の手（着手の点かパス）。まだ手がなければ null。</summary>
        public int? LastMove => _moves.Count == 0 ? null : _moves[_moves.Count - 1];

        /// <summary>盤面のハッシュ。</summary>
        public ulong Hash => _board.Hash;

        public Stone this[int point] => _board[point];

        /// <summary>その色が取った相手の石の数（アゲハマ）。面積計点なので得点には足さない。</summary>
        public int Captures(Stone color) => _captures[(int)color];

        public int Move(int index) => _moves[index];

        /// <summary>盤の上のその色の石の数。</summary>
        public int StoneCount(Stone color) => _board.StoneCount(color);

        /// <summary>盤の複製（画面・計点・探索用。対局の盤は変わらない）。</summary>
        public Board CopyBoard() => _board.Clone();

        public PointSet ChainAt(int point) => _board.ChainAt(point);

        public int LibertyCount(int point) => _board.LibertyCount(point);

        public GoGame Clone() => new(this);

        /// <summary>着手できるかを、局面を変えずに判定する。</summary>
        public MoveVerdict Check(Stone side, int point)
        {
            if (IsOver) { return MoveVerdict.GameOver; }
            if (side != ToMove) { return MoveVerdict.NotYourTurn; }
            var preview = _board.Preview(point, side);
            switch (preview.Verdict)
            {
                case PlacementVerdict.OffBoard: return MoveVerdict.OffBoard;
                case PlacementVerdict.Occupied: return MoveVerdict.Occupied;
                case PlacementVerdict.Suicide: return MoveVerdict.Suicide;
            }

            if (!Repeats(_board, point, side, preview)) { return MoveVerdict.Ok; }
            return IsSimpleKo(preview.Captured) ? MoveVerdict.Ko : MoveVerdict.Superko;
        }

        public MoveVerdict TryPlay(Stone side, int point)
        {
            var verdict = Check(side, point);
            if (verdict != MoveVerdict.Ok) { return verdict; }

            var captured = _board.Play(point, side);
            _captures[(int)side] += captured.Count;
            ConsecutivePasses = 0;
            Record(point);
            return MoveVerdict.Ok;
        }

        public MoveVerdict TryPass(Stone side)
        {
            if (IsOver) { return MoveVerdict.GameOver; }
            if (side != ToMove) { return MoveVerdict.NotYourTurn; }
            ConsecutivePasses++;
            if (ConsecutivePasses >= 2) { End = GameEnd.TwoPasses; }
            Record(Points.Pass);
            return MoveVerdict.Ok;
        }

        /// <summary>投了。手番でなくてもできる（人間は CPU の思考中でも投了できる）。</summary>
        public MoveVerdict TryResign(Stone side)
        {
            if (!side.IsColor()) { throw new ArgumentOutOfRangeException(nameof(side), side, "黒か白を指定してください。"); }
            if (IsOver) { return MoveVerdict.GameOver; }
            Resigned = side;
            End = GameEnd.Resignation;
            return MoveVerdict.Ok;
        }

        /// <summary>手番の側が打てる点（同型反復まで判定済み）を番号の小さい順に。パスは含めない。</summary>
        public List<int> LegalPoints()
        {
            var list = new List<int>();
            if (IsOver) { return list; }
            for (var p = 0; p < Points.Count; p++)
            {
                if (Check(ToMove, p) == MoveVerdict.Ok) { list.Add(p); }
            }

            return list;
        }

        /// <summary>
        /// 着手後の盤面が対局の履歴（＋探索の経路 <paramref name="extra"/>）に現れたか。
        /// ハッシュで候補を引き、一致したら着手後の配置を作って比べる。
        /// </summary>
        internal bool Repeats(Board board, int point, Stone side, PlacementPreview preview, IReadOnlyList<ulong>? extra = null, IReadOnlyList<Stone[]>? extraSnapshots = null)
        {
            var hash = preview.HashAfter;
            var inHistory = _seen.Contains(hash);
            var inExtra = extra != null && Contains(extra, hash);
            if (!inHistory && !inExtra) { return false; }

            var after = board.CopyCells();
            after[point] = side;
            foreach (var q in preview.Captured) { after[q] = Stone.Empty; }

            if (inHistory)
            {
                for (var i = 0; i < _hashes.Count; i++)
                {
                    if (_hashes[i] == hash && SameCells(_snapshots[i], after)) { return true; }
                }
            }

            if (inExtra)
            {
                for (var i = 0; i < extra!.Count; i++)
                {
                    if (extra[i] == hash && SameCells(extraSnapshots![i], after)) { return true; }
                }
            }

            return false;
        }

        /// <summary>状態のハッシュ（コミ・手数の上限・盤・手番・パス・終局・アゲハマ・同型反復の履歴・手の列）。今の盤が同じでも履歴が違えば値が変わる。</summary>
        public uint StateHash()
        {
            var h = TileMotion.StateHash.Start();
            h.Mix(Config.Komi);
            h.Mix(Config.MoveLimit);
            for (var p = 0; p < Points.Count; p++) { h.Mix((int)_board[p]); }
            h.Mix((int)ToMove);
            h.Mix(ConsecutivePasses);
            h.Mix((int)End);
            h.Mix((int)Resigned);
            h.Mix(_captures[1]);
            h.Mix(_captures[2]);
            h.Mix(_hashes.Count);
            foreach (var x in _hashes) { h.Mix(unchecked((long)x)); }
            h.Mix(_moves.Count);
            foreach (var m in _moves) { h.Mix(m); }
            return h.Value;
        }

        private bool IsSimpleKo(PointSet captured)
        {
            if (captured.Count != 1) { return false; }
            var last = LastMove;
            return last.HasValue && last.Value != Points.Pass && captured.Contains(last.Value);
        }

        private void Record(int move)
        {
            _moves.Add(move);
            _hashes.Add(_board.Hash);
            _snapshots.Add(_board.CopyCells());
            _seen.Add(_board.Hash);
            ToMove = ToMove.Opponent();
            if (End == GameEnd.None && _moves.Count >= Config.MoveLimit) { End = GameEnd.MoveLimit; }
        }

        private static bool Contains(IReadOnlyList<ulong> list, ulong value)
        {
            for (var i = 0; i < list.Count; i++)
            {
                if (list[i] == value) { return true; }
            }

            return false;
        }

        internal static bool SameCells(Stone[] a, Stone[] b)
        {
            for (var i = 0; i < a.Length; i++)
            {
                if (a[i] != b[i]) { return false; }
            }

            return true;
        }
    }
}
