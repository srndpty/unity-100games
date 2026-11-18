using System;
using Game.Core;

namespace Game.App
{
    /// <summary>答えを見て次の手を選ぶ操縦役。テストと撮影台本が勝敗の場面を作るために使う。</summary>
    public static class AnswerKeyPilot
    {
        /// <summary>まだ開いていない安全なセルを探す。初手の前は盤面の中央を返す。</summary>
        public static bool TryNextSafe(MinesweeperGame game, out int x, out int y)
        {
            if (game == null) { throw new ArgumentNullException(nameof(game)); }
            if (game.Field == null)
            {
                x = game.Grid.Width / 2;
                y = game.Grid.Height / 2;
                return true;
            }

            return TryFind(game, false, out x, out y);
        }

        /// <summary>旗の立っていない爆弾を探す。初手の前は爆弾がないので見つからない。</summary>
        public static bool TryNextMine(MinesweeperGame game, out int x, out int y)
        {
            if (game == null) { throw new ArgumentNullException(nameof(game)); }
            x = y = -1;
            return game.Field != null && TryFind(game, true, out x, out y);
        }

        private static bool TryFind(MinesweeperGame game, bool mine, out int x, out int y)
        {
            var grid = game.Grid;
            for (var cell = 0; cell < grid.CellCount; cell++)
            {
                x = grid.X(cell);
                y = grid.Y(cell);
                if (game.Field!.IsMine(cell) == mine && game.Face(x, y) == CellFace.Hidden) { return true; }
            }

            x = y = -1;
            return false;
        }
    }
}
