using System;
using System.Collections.Generic;

namespace Game.Core
{
    /// <summary>
    /// 1 部屋（1 画面）のデータ。文字の地図から作る。行は上から下の順に書く（最後の行が最下段）。
    /// <list type="bullet">
    /// <item><c>.</c> 空、<c>#</c> 固体、<c>^</c> 上向きのトゲ、<c>E</c> 出口</item>
    /// <item><c>S</c> 自機の開始位置（1 つだけ、真下が固体であること）。マスは空として扱い、体はマスの下端の中央に置く</item>
    /// </list>
    /// </summary>
    public sealed class Room
    {
        /// <summary>部屋の幅（タイル）。1 画面に収まる大きさで固定する。</summary>
        public const int Columns = 40;

        /// <summary>部屋の高さ（タイル）。</summary>
        public const int Rows = 22;

        private Room(string name, TileMap map, int startColumn, int startRow)
        {
            Name = name;
            Map = map;
            StartColumn = startColumn;
            StartRow = startRow;
        }

        public string Name { get; }

        public TileMap Map { get; }

        public int StartColumn { get; }

        public int StartRow { get; }

        /// <summary>開始位置に置いた自機の箱。</summary>
        public Box StartBox => PlaceOnTile(StartColumn, StartRow, Player.Width, Player.Height);

        /// <summary>40×22 の部屋を解析する。</summary>
        public static Room Parse(string name, IReadOnlyList<string> rows) => Parse(name, rows, Columns, Rows);

        /// <summary>大きさを指定して解析する（テスト用の小さい部屋）。</summary>
        public static Room Parse(string name, IReadOnlyList<string> rows, int columns, int height)
        {
            if (name == null) { throw new ArgumentNullException(nameof(name)); }
            if (rows == null) { throw new ArgumentNullException(nameof(rows)); }
            if (columns < 1 || columns > Columns || height < 1 || height > Rows)
            {
                throw new ArgumentOutOfRangeException(nameof(columns), $"大きさは 1×1〜{Columns}×{Rows} にしてください（{columns}×{height}）。");
            }

            if (rows.Count != height)
            {
                throw new ArgumentException($"行数が {height} ではありません（{rows.Count}）。", nameof(rows));
            }

            var tiles = new TileKind[columns * height];
            var startColumn = -1;
            var startRow = -1;
            var exits = 0;
            for (var line = 0; line < height; line++)
            {
                var text = rows[line];
                if (text == null || text.Length != columns)
                {
                    throw new ArgumentException($"{line + 1} 行目の幅が {columns} ではありません。", nameof(rows));
                }

                var row = height - 1 - line;
                for (var column = 0; column < columns; column++)
                {
                    var kind = TileKind.Empty;
                    switch (text[column])
                    {
                        case '.':
                            break;
                        case '#':
                            kind = TileKind.Solid;
                            break;
                        case '^':
                            kind = TileKind.Spike;
                            break;
                        case 'E':
                            kind = TileKind.Exit;
                            exits++;
                            break;
                        case 'S':
                            if (startColumn >= 0)
                            {
                                throw new ArgumentException("開始位置 S が 2 つ以上あります。", nameof(rows));
                            }

                            startColumn = column;
                            startRow = row;
                            break;
                        default:
                            throw new ArgumentException($"{line + 1} 行目 {column + 1} 文字目の '{text[column]}' は使えません。", nameof(rows));
                    }

                    tiles[(row * columns) + column] = kind;
                }
            }

            if (startColumn < 0)
            {
                throw new ArgumentException("開始位置 S がありません。", nameof(rows));
            }

            if (exits == 0)
            {
                throw new ArgumentException("出口 E がありません。", nameof(rows));
            }

            var map = new TileMap(columns, height, tiles);
            if (!map.IsSolid(startColumn, startRow - 1))
            {
                throw new ArgumentException($"開始位置（{startColumn}, {startRow}）の真下が固体ではありません。", nameof(rows));
            }

            var start = PlaceOnTile(startColumn, startRow, Player.Width, Player.Height);
            if (map.Touches(start, TileKind.Solid) || map.TouchesSpike(start) || map.Touches(start, TileKind.Exit))
            {
                throw new ArgumentException($"開始位置（{startColumn}, {startRow}）の自機が固体・トゲ・出口に重なります。", nameof(rows));
            }

            return new Room(name, map, startColumn, startRow);
        }

        /// <summary>マス (column, row) の下端の中央に、幅 width・高さ height の体を置いた箱。</summary>
        public static Box PlaceOnTile(int column, int row, int width, int height)
        {
            return new Box((column * Units.Tile) + ((Units.Tile - width) / 2), row * Units.Tile, width, height);
        }
    }
}
