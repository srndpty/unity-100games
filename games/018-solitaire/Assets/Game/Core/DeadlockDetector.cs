using System;
using System.Collections.Generic;
using System.Text;

namespace Game.Core
{
    /// <summary>手詰まりの判定結果。</summary>
    public enum DeadlockVerdict
    {
        /// <summary>前進できる局面に届く。</summary>
        Progress,

        /// <summary>どの手順でも前進できない。この盤はもう勝てない。</summary>
        Stuck,

        /// <summary>上限の局面数までに決まらなかった。手詰まりとはしない。</summary>
        Undetermined,
    }

    /// <summary>
    /// 手詰まりの判定。「前進」＝組札の枚数が増える・伏せ札が開く・山札と捨て札の合計が減る、のどれか。
    /// どれも元に戻せない変化で、勝つ（組札 52 枚）までに必ずどれかが起きるので、前進できる局面に届かなければその盤は勝てない。
    /// 合法な全コマンド（めくり・戻し・組札から場札も含む）で局面を深さ優先に調べ、同じ局面は 1 回だけ調べる。
    /// 局面は場札の列どうし・組札どうしの並べ替えを同じとみなす（入れ替えても前進の可否は変わらない）。
    /// </summary>
    public static class DeadlockDetector
    {
        public static DeadlockVerdict Check(IReadOnlyTable table, int maxStates) => Check(table, maxStates, out _);

        /// <summary>visitedStates は調べた局面の数（最初の局面を含む）。1 回の判定の手間はこれに比例し、maxStates + 1 を超えない。</summary>
        public static DeadlockVerdict Check(IReadOnlyTable table, int maxStates, out int visitedStates)
        {
            visitedStates = 0;
            if (table == null) { throw new ArgumentNullException(nameof(table)); }
            if (maxStates < 1) { throw new ArgumentOutOfRangeException(nameof(maxStates), maxStates, "1 以上で指定してください。"); }

            var work = Table.CopyOf(table);
            var baseline = work.FoundationTotal;
            var keys = new KeyBuilder();
            var visited = new HashSet<string>(StringComparer.Ordinal) { keys.Of(work) };
            visitedStates = 1;
            var rootMoves = Expand(work, baseline);
            if (rootMoves == null) { return DeadlockVerdict.Progress; }

            var stack = new Stack<Frame>();
            stack.Push(new Frame(rootMoves, null));
            while (stack.Count > 0)
            {
                var frame = stack.Peek();
                if (frame.Next >= frame.Moves.Count)
                {
                    stack.Pop();
                    if (frame.Entry is MoveRecord entry) { work.Undo(entry); }
                    continue;
                }

                var record = work.Execute(frame.Moves[frame.Next++]);
                if (!visited.Add(keys.Of(work)))
                {
                    work.Undo(record);
                    continue;
                }

                visitedStates = visited.Count;
                if (visited.Count > maxStates) { return DeadlockVerdict.Undetermined; }
                var moves = Expand(work, baseline);
                if (moves == null) { return DeadlockVerdict.Progress; }
                stack.Push(new Frame(moves, record));
            }

            return DeadlockVerdict.Stuck;
        }

        /// <summary>局面の合法手。そのどれかで前進できるなら null（深く潜る前に、近い前進を先に見つける）。</summary>
        private static List<Command>? Expand(Table work, int baselineFoundation)
        {
            var moves = new List<Command>();
            MoveRules.LegalMoves(work, moves);
            // 探索中は組札が基準を超えない（超えれば前進で止まる）。組札から場札へ下ろした後は、組札へ戻しても前進ではない。
            var foundationAtBaseline = work.FoundationTotal == baselineFoundation;
            foreach (var move in moves)
            {
                if (IsProgress(work, move, foundationAtBaseline)) { return null; }
            }

            return moves;
        }

        /// <summary>合法なコマンドが前進になるかを、盤を動かさずに決める。</summary>
        private static bool IsProgress(Table work, Command move, bool foundationAtBaseline)
        {
            if (move.Kind != CommandKind.Move) { return false; }

            // 捨て札の札は山札にも捨て札にも戻らないので、出せば山札と捨て札の合計が減る。
            if (move.From.Kind == PileKind.Waste) { return true; }
            if (move.To.Kind == PileKind.Foundation && foundationAtBaseline) { return true; }

            // 表の札を全部動かすと、下の伏せ札が開く。
            if (move.From.Kind != PileKind.Tableau) { return false; }
            var faceDown = work.FaceDownCountOf(move.From);
            return faceDown > 0 && move.Count == work.CountOf(move.From) - faceDown;
        }

        private sealed class Frame
        {
            public Frame(List<Command> moves, MoveRecord? entry)
            {
                Moves = moves;
                Entry = entry;
            }

            public List<Command> Moves { get; }

            /// <summary>この局面に来たコマンド。戻るときに取り消す。根は null。</summary>
            public MoveRecord? Entry { get; }

            public int Next { get; set; }
        }

        /// <summary>
        /// 局面の文字列。探索中は伏せ札が開かない（開けば前進）ので、伏せ札の枚数は列の札から決まり、書かなくてよい。
        /// </summary>
        private sealed class KeyBuilder
        {
            private readonly StringBuilder _builder = new();
            private readonly string[] _foundations = new string[PileId.FoundationCount];
            private readonly string[] _tableau = new string[PileId.TableauCount];

            public string Of(IReadOnlyTable table)
            {
                for (var i = 0; i < _foundations.Length; i++) { _foundations[i] = PileString(table, PileId.Foundation(i)); }
                for (var i = 0; i < _tableau.Length; i++) { _tableau[i] = PileString(table, PileId.Tableau(i)); }
                Array.Sort(_foundations, StringComparer.Ordinal);
                Array.Sort(_tableau, StringComparer.Ordinal);

                _builder.Clear();
                _builder.Append(PileString(table, PileId.Stock)).Append('/').Append(PileString(table, PileId.Waste));
                foreach (var pile in _foundations) { _builder.Append('/').Append(pile); }
                foreach (var pile in _tableau) { _builder.Append('/').Append(pile); }
                return _builder.ToString();
            }

            private static string PileString(IReadOnlyTable table, PileId pile)
            {
                var count = table.CountOf(pile);
                var chars = new char[count];
                for (var i = 0; i < count; i++) { chars[i] = (char)('0' + table.CardAt(pile, i).Id); }
                return new string(chars);
            }
        }
    }
}
