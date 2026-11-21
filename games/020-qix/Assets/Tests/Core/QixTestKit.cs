using System.Collections.Generic;

namespace Game.Core.Tests
{
    /// <summary>テスト用の設定と操作の補助。</summary>
    internal static class QixTestKit
    {
        public const int S = QixConfig.SubCell;

        /// <summary>20×14 の小さい盤。自機は 1 Tick に 1 セル、敵はほぼ止まっていて向きを変えない。</summary>
        public static QixConfig Small(int targetPercent = 75)
        {
            return new QixConfig
            {
                Width = 20,
                Height = 14,
                PlayerStepTicks = 1,
                EnemyRadius = S / 4,
                EnemySpeed = 1,
                EnemyTurnMinTicks = QixConfig.MaxTurnTicks,
                EnemyTurnMaxTicks = QixConfig.MaxTurnTicks,
                Lives = 3,
                TargetPercent = targetPercent,
            };
        }

        /// <summary>セル (x, y) の中心に敵を置き、x 方向へごく遅く動かす。</summary>
        public static QixGame WithEnemyAt(QixConfig config, int cellX, int cellY)
        {
            return QixGame.WithEnemy(config, 1, cellX * S + S / 2, cellY * S + S / 2, 1, 0);
        }

        /// <summary>同じ入力のまま <paramref name="ticks"/> Tick 進める。</summary>
        public static void Hold(QixGame game, Direction direction, bool draw, int ticks)
        {
            game.SetInput(direction, draw);
            for (var i = 0; i < ticks; i++)
            {
                game.Tick();
            }
        }

        /// <summary>描画キーを押したまま、向きと歩数の列どおりに進む（自機は 1 Tick に 1 セルの設定で使う）。</summary>
        public static void Draw(QixGame game, params (Direction Direction, int Steps)[] path)
        {
            foreach (var (direction, steps) in path)
            {
                Hold(game, direction, draw: true, steps);
            }

            game.SetInput(Direction.None, draw: true);
        }

        /// <summary>盤の中の海のセルを数える。</summary>
        public static int CountOpen(QixGame game)
        {
            var count = 0;
            for (var y = 0; y < game.Height; y++)
            {
                for (var x = 0; x < game.Width; x++)
                {
                    if (game.CellAt(x, y) == Cell.Open) { count++; }
                }
            }

            return count;
        }

        /// <summary>敵が重なるセルの状態をすべて返す。</summary>
        public static List<Cell> CellsUnderEnemy(QixGame game)
        {
            var cells = new List<Cell>();
            for (var y = 0; y < game.Height; y++)
            {
                for (var x = 0; x < game.Width; x++)
                {
                    if (game.Enemy.Overlaps(x, y)) { cells.Add(game.CellAt(x, y)); }
                }
            }

            return cells;
        }
    }
}
