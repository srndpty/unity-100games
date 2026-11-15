using System;
using System.Collections.Generic;

namespace Game.Core
{
    /// <summary>ブロック 1 個の配置（マス目の列・段と種類）。段は上から 0。</summary>
    public readonly struct BrickPlacement
    {
        public BrickPlacement(int column, int row, BrickKind kind)
        {
            Column = column;
            Row = row;
            Kind = kind;
        }

        public int Column { get; }

        public int Row { get; }

        public BrickKind Kind { get; }
    }

    /// <summary>検証済みの 1 面ぶんの配置。</summary>
    public sealed class StageLayout
    {
        public StageLayout(int columns, int rows, IReadOnlyList<BrickPlacement> bricks)
        {
            Columns = columns;
            Rows = rows;
            Bricks = bricks;
        }

        public int Columns { get; }

        public int Rows { get; }

        /// <summary>左上から行優先の順。この順番がブロックの番号になる。</summary>
        public IReadOnlyList<BrickPlacement> Bricks { get; }
    }

    /// <summary>
    /// 面データ（文字列グリッド）を解析する。記号：<c>N</c> 通常、<c>H</c> 硬い、<c>P</c> 貫通弾、<c>E</c> パドル拡張、<c>.</c> 空き。
    /// 行の長さが列数と違う、未知の記号、ブロックが 1 つもない面は例外にする。
    /// </summary>
    public static class StageParser
    {
        public const char Empty = '.';

        public static StageLayout Parse(StageDefinition? stage, int columns, int stageIndex)
        {
            var rows = stage?.Rows;
            if (rows is null || rows.Length == 0)
            {
                throw Invalid(stageIndex, "行がない");
            }

            var bricks = new List<BrickPlacement>();
            for (var row = 0; row < rows.Length; row++)
            {
                var line = rows[row];
                if (line is null || line.Length != columns)
                {
                    throw Invalid(stageIndex, $"{row + 1} 行目の長さが列数 {columns} と違う");
                }

                for (var column = 0; column < columns; column++)
                {
                    var symbol = line[column];
                    if (symbol == Empty)
                    {
                        continue;
                    }

                    var kind = KindOf(symbol) ?? throw Invalid(stageIndex, $"{row + 1} 行目に未知の記号 '{symbol}'");
                    bricks.Add(new BrickPlacement(column, row, kind));
                }
            }

            if (bricks.Count == 0)
            {
                throw Invalid(stageIndex, "ブロックがない");
            }

            return new StageLayout(columns, rows.Length, bricks);
        }

        public static BrickKind? KindOf(char symbol) => symbol switch
        {
            'N' => BrickKind.Normal,
            'H' => BrickKind.Hard,
            'P' => BrickKind.PierceItem,
            'E' => BrickKind.ExpandItem,
            _ => null,
        };

        private static ArgumentException Invalid(int stageIndex, string reason) =>
            new($"{stageIndex + 1} 面の面データが不正です: {reason}");
    }
}
