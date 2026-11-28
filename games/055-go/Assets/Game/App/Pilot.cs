using System;
using System.Collections.Generic;
using Game.Core;
using IntGeometry;

namespace Game.App
{
    /// <summary>
    /// 操縦役（撮影・App テスト・PlayMode 用）。プレイヤーの側の手を小さい MCTS で決め、実際の入力（矢印キーと決定・パス）に変える。
    /// 乱数は（試合シード × 手数 × 操縦役）の系列。
    /// </summary>
    public static class Pilot
    {
        public const int RandomPurpose = 3;

        /// <summary>手番の側の手（パスは <see cref="Points.Pass"/>）。</summary>
        public static int ChooseMove(GoGame game, MctsConfig config, int playouts, int matchSeed)
        {
            if (game == null) { throw new ArgumentNullException(nameof(game)); }
            var search = new Mcts(game, config, playouts, RandomSeries.Derive(matchSeed, game.MoveCount, RandomPurpose));
            search.Run(playouts);
            return search.BestMove;
        }

        /// <summary>カーソルを <paramref name="target"/> へ動かす矢印キーの列（横、縦の順）。</summary>
        public static List<InputButton> KeysTo(int from, int target)
        {
            if (!Points.IsOnBoard(from)) { throw new ArgumentOutOfRangeException(nameof(from)); }
            if (!Points.IsOnBoard(target)) { throw new ArgumentOutOfRangeException(nameof(target)); }
            var keys = new List<InputButton>();
            var dx = Points.Column(target) - Points.Column(from);
            var dy = Points.Row(target) - Points.Row(from);
            for (var i = 0; i < Math.Abs(dx); i++) { keys.Add(dx > 0 ? InputButton.Right : InputButton.Left); }
            for (var i = 0; i < Math.Abs(dy); i++) { keys.Add(dy > 0 ? InputButton.Up : InputButton.Down); }
            return keys;
        }

        /// <summary>
        /// プレイヤーの手番なら手を決めて、矢印キーと決定（またはパス）を Flow に積む。積んだら true。
        /// </summary>
        public static bool Act(GoFlow flow, MctsConfig config, int playouts)
        {
            if (flow == null) { throw new ArgumentNullException(nameof(flow)); }
            if (!flow.IsPlayerTurn) { return false; }
            var move = ChooseMove(flow.Game!, config, playouts, flow.MatchSeed);
            if (move == Points.Pass)
            {
                flow.Press(InputButton.Pass);
                return true;
            }

            foreach (var key in KeysTo(flow.Cursor.Point, move)) { flow.Press(key); }
            flow.Press(InputButton.Confirm);
            return true;
        }
    }
}
