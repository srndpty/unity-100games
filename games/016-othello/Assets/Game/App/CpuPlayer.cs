using System;
using Game.Core;

namespace Game.App
{
    /// <summary>
    /// CPU の打ち手。自分の手番が来た最初の Tick に探索して手を決め、考える間を置いてから、プレイヤーと同じ <see cref="OthelloGame.TryPlay"/> で打つ。
    /// 最善の評価の手が複数あるときは試合のシードの乱数で選ぶ。
    /// </summary>
    public sealed class CpuPlayer
    {
        private readonly int _thinkTicks;
        private readonly SeededRandom _random;
        private int _plannedTurn = -1;
        private int _wait;

        public CpuPlayer(Side side, int depth, int thinkTicks, int seed)
        {
            if (depth < 1 || depth > OthelloConfig.MaxDepth) { throw new ArgumentOutOfRangeException(nameof(depth), depth, $"1〜{OthelloConfig.MaxDepth} で指定してください。"); }
            if (thinkTicks < 0) { throw new ArgumentOutOfRangeException(nameof(thinkTicks), thinkTicks, "0 以上を指定してください。"); }
            Side = side;
            Depth = depth;
            _thinkTicks = thinkTicks;
            _random = new SeededRandom(seed);
        }

        public Side Side { get; }
        public int Depth { get; }

        /// <summary>決めたが、まだ打っていない手。</summary>
        public Square? PlannedMove { get; private set; }

        /// <summary>直前の探索で調べた局面の数。</summary>
        public long LastNodes { get; private set; }

        /// <summary>1 Tick ぶん進める。自分の手番で考える間が終わっていれば打つ。</summary>
        public void Tick(OthelloGame game)
        {
            if (game == null) { throw new ArgumentNullException(nameof(game)); }
            if (!game.AcceptsMove || game.ToMove != Side) { return; }

            if (_plannedTurn != game.Turn)
            {
                _plannedTurn = game.Turn;
                var result = Searcher.Search(game.Position, Depth, _random);
                PlannedMove = result.Move;
                LastNodes = result.Nodes;
                _wait = _thinkTicks;
            }

            if (_wait > 0)
            {
                _wait--;
                return;
            }

            var verdict = game.TryPlay(Side, PlannedMove!.Value);
            if (verdict != PlayVerdict.Ok) { throw new InvalidOperationException($"CPU の手 {PlannedMove.Value} が拒否されました: {verdict}"); }
            PlannedMove = null;
        }
    }
}
