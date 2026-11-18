using System;

namespace Game.Core
{
    public enum CellKind
    {
        Wall,
        Path,
        Door,
        House,
    }

    public enum Pellet
    {
        None,
        Dot,
        Power,
    }

    /// <summary>
    /// 文字列で書いた迷路の解釈。
    /// '#' 壁、'.' パン屑、'o' マタタビ、' ' 餌のない通路、'P' 自機の開始位置、'=' 寝床の扉、'H' 寝床の中。
    /// </summary>
    public sealed class MazeLayout
    {
        private static readonly string[] _defaultRows =
        {
            "#############################",
            "#o.........................o#",
            "#.#######.####.####.#######.#",
            "#.#######.####.####.#######.#",
            "#.###......... .........###.#",
            "#.###.###.####=####.###.###.#",
            "#.###.###.#HHHHHHH#.###.###.#",
            " .........#HHHHHHH#......... ",
            "#.###.###.#HHHHHHH#.###.###.#",
            "#.###.###.#########.###.###.#",
            "#.....###.....P.....###.....#",
            "#.###.###.####.####.###.###.#",
            "#.###.###.####.####.###.###.#",
            "#o.........................o#",
            "#############################",
        };

        private readonly CellKind[] _kinds;
        private readonly Pellet[] _pellets;

        public MazeLayout(string[] rows)
        {
            if (rows == null || rows.Length == 0) { throw new ArgumentException("迷路の行がありません。", nameof(rows)); }
            Height = rows.Length;
            Width = rows[0]?.Length ?? 0;
            if (Width == 0) { throw new ArgumentException("迷路の幅が0です。", nameof(rows)); }

            _kinds = new CellKind[Width * Height];
            _pellets = new Pellet[Width * Height];
            var players = 0;
            var doors = 0;
            for (var y = 0; y < Height; y++)
            {
                if (rows[y] == null || rows[y].Length != Width) { throw new ArgumentException("迷路の幅が揃っていません。", nameof(rows)); }
                for (var x = 0; x < Width; x++)
                {
                    var index = (y * Width) + x;
                    switch (rows[y][x])
                    {
                        case '#': _kinds[index] = CellKind.Wall; break;
                        case '.': _kinds[index] = CellKind.Path; _pellets[index] = Pellet.Dot; break;
                        case 'o': _kinds[index] = CellKind.Path; _pellets[index] = Pellet.Power; break;
                        case ' ': _kinds[index] = CellKind.Path; break;
                        case 'P': _kinds[index] = CellKind.Path; PlayerSpawn = new GridPoint(x, y); players++; break;
                        case '=': _kinds[index] = CellKind.Door; Door = new GridPoint(x, y); doors++; break;
                        case 'H': _kinds[index] = CellKind.House; break;
                        default: throw new ArgumentException("迷路に不明な文字があります: " + rows[y][x], nameof(rows));
                    }
                }
            }

            if (players != 1) { throw new ArgumentException("自機の開始位置は1つ必要です。", nameof(rows)); }
            if (doors != 1) { throw new ArgumentException("寝床の扉は1つ必要です。", nameof(rows)); }
        }

        public int Width { get; }
        public int Height { get; }
        public GridPoint PlayerSpawn { get; }
        public GridPoint Door { get; }

        public static MazeLayout CreateDefault() => new(_defaultRows);

        /// <summary>既定の迷路の文字列。テストで一部を書き換えて使う。</summary>
        public static string[] CopyDefaultRows() => (string[])_defaultRows.Clone();

        public bool Contains(int x, int y) => x >= 0 && x < Width && y >= 0 && y < Height;

        public CellKind Kind(int x, int y) => Contains(x, y) ? _kinds[(y * Width) + x] : CellKind.Wall;

        public Pellet PelletAt(int x, int y) => Contains(x, y) ? _pellets[(y * Width) + x] : Pellet.None;
    }
}
