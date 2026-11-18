using System;
using System.Collections.Generic;

namespace Game.Core
{
    /// <summary>
    /// 文字の面データを <see cref="LevelMap"/> へ変換する。
    /// <c>#</c> 壁、空白 床、<c>.</c> 目標、<c>$</c> 箱、<c>*</c> 目標上の箱、<c>@</c> 人、<c>+</c> 目標上の人。
    /// 遊べない面は例外にする。
    /// </summary>
    public static class LevelParser
    {
        private static readonly Direction[] _directions = { Direction.Up, Direction.Down, Direction.Left, Direction.Right };

        public static LevelMap Parse(IReadOnlyList<string> rows)
        {
            if (rows == null) { throw new ArgumentNullException(nameof(rows)); }
            var height = rows.Count;
            var width = 0;
            foreach (var row in rows) { width = Math.Max(width, row.Length); }
            if (width == 0 || height == 0) { throw new FormatException("面データが空です。"); }
            if (width > LevelMap.MaxWidth || height > LevelMap.MaxHeight)
            {
                throw new FormatException($"面が大きすぎます: {width}×{height}（上限 {LevelMap.MaxWidth}×{LevelMap.MaxHeight}）");
            }

            var walls = new bool[width * height];
            var goals = new bool[width * height];
            var boxes = new List<Cell>();
            Cell? player = null;
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < rows[y].Length; x++)
                {
                    var cell = new Cell(x, y);
                    var symbol = rows[y][x];
                    switch (symbol)
                    {
                        case '#': walls[(y * width) + x] = true; break;
                        case ' ': break;
                        case '.': goals[(y * width) + x] = true; break;
                        case '$': boxes.Add(cell); break;
                        case '*': boxes.Add(cell); goals[(y * width) + x] = true; break;
                        case '@':
                        case '+':
                            if (player != null) { throw new FormatException("プレイヤーが複数います。"); }
                            player = cell;
                            goals[(y * width) + x] = symbol == '+';
                            break;
                        default: throw new FormatException($"不明な文字です: '{symbol}' {cell}");
                    }
                }
            }

            if (player == null) { throw new FormatException("プレイヤーがいません。"); }
            var floors = FloodFloors(width, height, walls, player.Value);
            RequireSolvableShape(width, height, floors, goals, boxes);
            return new LevelMap(width, height, walls, floors, goals, new BoardState(player.Value, boxes));
        }

        // プレイヤーの位置から壁で止まるまで広げた範囲を床とする。箱は動かせるので通り抜ける。
        private static bool[] FloodFloors(int width, int height, bool[] walls, Cell start)
        {
            var floors = new bool[width * height];
            var pending = new Stack<Cell>();
            floors[(start.Y * width) + start.X] = true;
            pending.Push(start);
            while (pending.Count > 0)
            {
                var cell = pending.Pop();
                foreach (var direction in _directions)
                {
                    var next = cell.Step(direction);
                    if (next.X < 0 || next.X >= width || next.Y < 0 || next.Y >= height)
                    {
                        throw new FormatException("外周が壁で閉じていません: " + cell);
                    }

                    var index = (next.Y * width) + next.X;
                    if (walls[index] || floors[index]) { continue; }
                    floors[index] = true;
                    pending.Push(next);
                }
            }

            return floors;
        }

        private static void RequireSolvableShape(int width, int height, bool[] floors, bool[] goals, List<Cell> boxes)
        {
            var goalCount = 0;
            for (var i = 0; i < goals.Length; i++)
            {
                if (!goals[i]) { continue; }
                goalCount++;
                if (!floors[i]) { throw new FormatException($"到達できない目標があります: {new Cell(i % width, i / width)}"); }
            }

            if (boxes.Count == 0) { throw new FormatException("箱がありません。"); }
            if (boxes.Count > LevelMap.MaxBoxes) { throw new FormatException($"箱が多すぎます: {boxes.Count}（上限 {LevelMap.MaxBoxes}）"); }
            if (boxes.Count != goalCount) { throw new FormatException($"箱と目標の数が違います: 箱 {boxes.Count}、目標 {goalCount}"); }

            var allPlaced = true;
            foreach (var box in boxes)
            {
                var index = (box.Y * width) + box.X;
                if (!floors[index]) { throw new FormatException("到達できない箱があります: " + box); }
                allPlaced &= goals[index];
            }

            if (allPlaced) { throw new FormatException($"開始時点でクリア済みです（{width}×{height}）。"); }
        }
    }
}
