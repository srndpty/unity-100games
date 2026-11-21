using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Game.Core
{
    public enum EnemyKind
    {
        /// <summary>歩き虫：歩き、壁で引き返し、崖から落ちる。踏める。</summary>
        Walker,

        /// <summary>跳ね虫：その場で一定の間隔で跳ねる。踏める。</summary>
        Hopper,

        /// <summary>茨玉：歩き、壁と崖で引き返す。踏めない（上から触れてもミス）。</summary>
        Thorn,
    }

    public readonly struct EnemySpawn
    {
        public EnemySpawn(EnemyKind kind, int column, int row)
        {
            Kind = kind;
            Column = column;
            Row = row;
        }

        public EnemyKind Kind { get; }

        public int Column { get; }

        public int Row { get; }
    }

    /// <summary>
    /// 1 面のデータ。文字の地図から作る。行は上から下の順に書く（最後の行が最下段）。
    /// <list type="bullet">
    /// <item><c>.</c> 空、<c>#</c> 固体、<c>G</c> ゴールの旗の柱</item>
    /// <item><c>S</c> 自機の開始位置（1 つだけ）</item>
    /// <item><c>w</c> 歩き虫、<c>h</c> 跳ね虫、<c>t</c> 茨玉（真下が固体であること）</item>
    /// </list>
    /// 開始位置と敵のマスは空として扱い、体はそのマスの下端の中央に置く。
    /// </summary>
    public sealed class Level
    {
        public const int MaxWidth = 1024;
        public const int MaxHeight = 64;

        private Level(string name, TileMap map, int startColumn, int startRow, EnemySpawn[] enemies)
        {
            Name = name;
            Map = map;
            StartColumn = startColumn;
            StartRow = startRow;
            Enemies = Array.AsReadOnly(enemies);
        }

        public string Name { get; }

        public TileMap Map { get; }

        public int StartColumn { get; }

        public int StartRow { get; }

        public ReadOnlyCollection<EnemySpawn> Enemies { get; }

        public static Level Parse(string name, IReadOnlyList<string> rows)
        {
            if (name == null) { throw new ArgumentNullException(nameof(name)); }
            if (rows == null) { throw new ArgumentNullException(nameof(rows)); }
            if (rows.Count == 0 || rows.Count > MaxHeight)
            {
                throw new ArgumentException($"行数は 1〜{MaxHeight} にしてください（{rows.Count}）。", nameof(rows));
            }

            var width = rows[0]?.Length ?? 0;
            if (width == 0 || width > MaxWidth)
            {
                throw new ArgumentException($"幅は 1〜{MaxWidth} にしてください（{width}）。", nameof(rows));
            }

            var height = rows.Count;
            var tiles = new TileKind[width * height];
            var enemies = new List<EnemySpawn>();
            var startColumn = -1;
            var startRow = -1;
            var goals = 0;
            for (var line = 0; line < height; line++)
            {
                var text = rows[line];
                if (text == null || text.Length != width)
                {
                    throw new ArgumentException($"{line + 1} 行目の幅が {width} と一致しません。", nameof(rows));
                }

                var row = height - 1 - line;
                for (var column = 0; column < width; column++)
                {
                    var kind = TileKind.Empty;
                    switch (text[column])
                    {
                        case '.':
                            break;
                        case '#':
                            kind = TileKind.Solid;
                            break;
                        case 'G':
                            kind = TileKind.Goal;
                            goals++;
                            break;
                        case 'S':
                            if (startColumn >= 0)
                            {
                                throw new ArgumentException("開始位置 S が 2 つ以上あります。", nameof(rows));
                            }

                            startColumn = column;
                            startRow = row;
                            break;
                        case 'w':
                            enemies.Add(new EnemySpawn(EnemyKind.Walker, column, row));
                            break;
                        case 'h':
                            enemies.Add(new EnemySpawn(EnemyKind.Hopper, column, row));
                            break;
                        case 't':
                            enemies.Add(new EnemySpawn(EnemyKind.Thorn, column, row));
                            break;
                        default:
                            throw new ArgumentException($"{line + 1} 行目 {column + 1} 文字目の '{text[column]}' は使えません。", nameof(rows));
                    }

                    tiles[(row * width) + column] = kind;
                }
            }

            if (startColumn < 0)
            {
                throw new ArgumentException("開始位置 S がありません。", nameof(rows));
            }

            if (goals == 0)
            {
                throw new ArgumentException("ゴール G がありません。", nameof(rows));
            }

            var map = new TileMap(width, height, tiles);
            foreach (var enemy in enemies)
            {
                if (!map.IsSolid(enemy.Column, enemy.Row - 1))
                {
                    throw new ArgumentException($"敵（{enemy.Column}, {enemy.Row}）の真下が固体ではありません。", nameof(rows));
                }
            }

            return new Level(name, map, startColumn, startRow, enemies.ToArray());
        }

        /// <summary>マス (column, row) の下端の中央に、幅 width・高さ height の体を置いた箱。</summary>
        public static Box PlaceOnTile(int column, int row, int width, int height)
        {
            return new Box((column * Units.Tile) + ((Units.Tile - width) / 2), row * Units.Tile, width, height);
        }
    }
}
