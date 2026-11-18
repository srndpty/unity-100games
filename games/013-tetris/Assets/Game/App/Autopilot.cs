using System;
using System.Collections.Generic;
using Game.Core;

namespace Game.App
{
    /// <summary>撮影とテストのための操縦役。操作中のピースの置き場所を決め、必要な操作の列を返す。</summary>
    public static class Autopilot
    {
        private const int LineWeight = 76;
        private const int HeightWeight = 51;
        private const int HoleWeight = 36;
        private const int BumpWeight = 18;

        public static IReadOnlyList<GameCommand> PlanPiece(StrataGame game)
        {
            if (game == null) { throw new ArgumentNullException(nameof(game)); }
            var plan = new List<GameCommand>();
            if (game.Active is not ActivePiece piece) { return plan; }

            var bestScore = long.MinValue;
            var bestTurns = 0;
            var bestShift = 0;
            var turnCount = piece.Kind == PieceKind.O ? 1 : PieceShapes.RotationCount;
            for (var turns = 0; turns < turnCount; turns++)
            {
                var rotation = (Rotation)(((int)piece.Rotation + turns) % PieceShapes.RotationCount);
                var turned = new ActivePiece(piece.Kind, rotation, piece.X, piece.Y);
                if (!game.Board.Fits(turned)) { continue; }
                foreach (var direction in new[] { -1, 1 })
                {
                    for (var shift = direction == 1 ? 1 : 0; game.Board.Fits(turned.Moved(shift, 0)); shift += direction)
                    {
                        var score = Evaluate(game.Board, turned.Moved(shift, 0));
                        if (score <= bestScore) { continue; }
                        bestScore = score;
                        bestTurns = turns;
                        bestShift = shift;
                    }
                }
            }

            AddTurns(plan, bestTurns);
            for (var i = 0; i < Math.Abs(bestShift); i++) { plan.Add(bestShift < 0 ? GameCommand.MoveLeft : GameCommand.MoveRight); }
            plan.Add(GameCommand.HardDrop);
            return plan;
        }

        private static void AddTurns(List<GameCommand> plan, int turns)
        {
            if (turns == PieceShapes.RotationCount - 1)
            {
                plan.Add(GameCommand.RotateCounterClockwise);
                return;
            }

            for (var i = 0; i < turns; i++) { plan.Add(GameCommand.RotateClockwise); }
        }

        private static long Evaluate(Board board, ActivePiece piece)
        {
            var landed = piece;
            while (board.Fits(landed.Moved(0, -1))) { landed = landed.Moved(0, -1); }
            var locked = board.Lock(landed);
            var rows = locked.FullRows();
            var settled = locked.ClearRows(rows);

            var totalHeight = 0;
            var holes = 0;
            var bumps = 0;
            var previous = -1;
            for (var x = 0; x < Board.Width; x++)
            {
                var height = ColumnHeight(settled, x);
                totalHeight += height;
                for (var y = 0; y < height; y++)
                {
                    if (settled.At(x, y) == null) { holes++; }
                }

                if (previous >= 0) { bumps += Math.Abs(height - previous); }
                previous = height;
            }

            return ((long)rows.Count * LineWeight) - (totalHeight * HeightWeight) - (holes * HoleWeight) - (bumps * BumpWeight);
        }

        private static int ColumnHeight(Board board, int x)
        {
            for (var y = Board.Height - 1; y >= 0; y--)
            {
                if (board.At(x, y) != null) { return y + 1; }
            }

            return 0;
        }
    }
}
