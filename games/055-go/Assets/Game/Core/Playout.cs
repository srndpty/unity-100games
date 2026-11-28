using System.Collections.Generic;
using IntGeometry;

namespace Game.Core
{
    /// <summary>
    /// 軽いランダムのプレイアウト。直前の手がアタリを作ったら、まず取る・逃げる（<see cref="Urgent"/>）。
    /// それ以外は自分の眼を埋める手を除いた置ける点から一様に選び、なければパス。
    /// 同型反復は単純コウだけ判定する（直前に 1 子を取られた点への 1 子の取り返し）。
    /// 2 連続パスか手数の上限で打ち切り、盤をそのまま面積で数える（死石の推定なし）。盤は書き換える。
    /// </summary>
    public static class Playout
    {
        /// <summary>打ち切るまで打つ。戻り値は打った手数（パスを含む）。<paramref name="record"/> には打った手を（点 × 4 + 色）で足す（AMAF 用）。</summary>
        internal static int Run(Board board, Stone toMove, int passes, int koPoint, int lastMove, RandomSeries random, int moveLimit, List<int>? record = null)
        {
            var candidates = new int[Points.Count];
            var moves = 0;
            while (passes < 2 && moves < moveLimit)
            {
                var urgent = Urgent(board, toMove, lastMove, koPoint);
                if (urgent != Points.Pass)
                {
                    var taken = board.Play(urgent, toMove);
                    record?.Add((urgent << 2) | (int)toMove);
                    koPoint = KoPointAfter(board, urgent, taken);
                    lastMove = urgent;
                    passes = 0;
                    toMove = toMove.Opponent();
                    moves++;
                    continue;
                }

                var count = 0;
                for (var p = 0; p < Points.Count; p++)
                {
                    if (board[p] == Stone.Empty) { candidates[count++] = p; }
                }

                var played = false;
                while (count > 0)
                {
                    var i = random.Next(count);
                    var point = candidates[i];
                    candidates[i] = candidates[--count];
                    if (point == koPoint || board.IsOwnEye(point, toMove)) { continue; }
                    var preview = board.Preview(point, toMove);
                    if (!preview.IsOk) { continue; }

                    var captured = board.Play(point, toMove);
                    record?.Add((point << 2) | (int)toMove);
                    koPoint = KoPointAfter(board, point, captured);
                    lastMove = point;
                    played = true;
                    break;
                }

                if (played)
                {
                    passes = 0;
                }
                else
                {
                    passes++;
                    koPoint = Points.Pass;
                    lastMove = Points.Pass;
                }

                toMove = toMove.Opponent();
                moves++;
            }

            return moves;
        }

        /// <summary>
        /// 直前の手（相手の手）への応手で、決まった手を優先する。なければ <see cref="Points.Pass"/>。
        /// <list type="number">
        /// <item>取る：直前の手の連の呼吸点が 1 つなら、その点に打って取る。</item>
        /// <item>逃げる：直前の手に接する自分の連の呼吸点が 1 つなら、その点に伸びる（取れるか、伸びた後の呼吸点が 2 つ以上のとき）。</item>
        /// </list>
        /// 単純コウの点と置けない点は除く。
        /// </summary>
        internal static int Urgent(Board board, Stone toMove, int lastMove, int koPoint)
        {
            if (!Points.IsOnBoard(lastMove) || board[lastMove] != toMove.Opponent()) { return Points.Pass; }
            if (board.LibertyCount(lastMove) == 1)
            {
                var point = board.LibertiesAt(lastMove).First();
                if (point != koPoint && board.Preview(point, toMove).IsOk) { return point; }
            }

            foreach (var n in Points.Neighbors(lastMove))
            {
                if (board[n] != toMove || board.LibertyCount(n) != 1) { continue; }
                var point = board.LibertiesAt(n).First();
                if (point == koPoint) { continue; }
                var preview = board.Preview(point, toMove);
                if (preview.IsOk && (!preview.Captured.IsEmpty || preview.LibertiesBeforeCapture >= 2)) { return point; }
            }

            return Points.Pass;
        }

        /// <summary>
        /// 着手の後、相手が次に打てない単純コウの点。1 子を取り、置いた石が 1 子の連で呼吸点が 1 つ（取った点）なら、その点。
        /// </summary>
        internal static int KoPointAfter(Board board, int point, PointSet captured)
        {
            if (captured.Count != 1) { return Points.Pass; }
            if (board.ChainAt(point).Count != 1 || board.LibertyCount(point) != 1) { return Points.Pass; }
            return captured.First();
        }

        /// <summary>プレイアウトの結果の勝者（コミ込み、引き分けは Empty）。</summary>
        internal static Stone Winner(Board board, int komi) => AreaScore.Compute(board, komi).Winner;
    }
}
