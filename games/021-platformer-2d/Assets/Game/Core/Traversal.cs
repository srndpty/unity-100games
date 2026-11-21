using System;
using System.Collections.Generic;

namespace Game.Core
{
    /// <summary>地面だけを通る道で越えなければならない障害。</summary>
    public readonly struct Obstacle : IEquatable<Obstacle>
    {
        public Obstacle(int column, int gapTiles, int riseTiles)
        {
            if (gapTiles < 0) { throw new ArgumentOutOfRangeException(nameof(gapTiles), gapTiles, "0 以上にしてください。"); }
            Column = column;
            GapTiles = gapTiles;
            RiseTiles = riseTiles;
        }

        /// <summary>踏み切る列（障害の手前の地面の列）。</summary>
        public int Column { get; }

        /// <summary>穴の幅（タイル数）。0 なら段だけ。</summary>
        public int GapTiles { get; }

        /// <summary>着地する地面の、踏み切る地面からの高さ（タイル数、負なら低い）。</summary>
        public int RiseTiles { get; }

        public bool Equals(Obstacle other) => Column == other.Column && GapTiles == other.GapTiles && RiseTiles == other.RiseTiles;

        public override bool Equals(object? obj) => obj is Obstacle other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(Column, GapTiles, RiseTiles);

        public override string ToString() => $"列 {Column}：穴 {GapTiles} タイル、高さ {RiseTiles:+0;-0;0} タイル";

        public static bool operator ==(Obstacle left, Obstacle right) => left.Equals(right);

        public static bool operator !=(Obstacle left, Obstacle right) => !left.Equals(right);
    }

    /// <summary>
    /// 設定で面を通れるかの検証。面は「浮いた足場を使わず、地面だけを通って抜けられる」ように作る決まりにし、
    /// その道の段と穴を、設定の自機で越えられるかを確かめる。越えられるかは式ではなく、自機（<see cref="Player"/>）を
    /// 障害だけの小さな地形で実際に動かして決める。助走は最高速に達するまで取り、跳ぶ Tick を変えて試し、
    /// 押し続けたジャンプで越えられるものがあれば通れるとする（最も有利な条件なので、ここで越えられなければ地面の道は通れない）。
    /// 敵・天井・制限時間内の敵の避け方までは保証しない。
    /// </summary>
    public static class Traversal
    {
        private const int LandingTiles = 6;
        private const int TryWindowTiles = 3;

        /// <summary>地面の高さ：列の最下段から続く固体のタイル数（0 なら穴の列）。浮いた足場は数えない。</summary>
        public static int GroundHeight(TileMap map, int column)
        {
            if (map == null) { throw new ArgumentNullException(nameof(map)); }
            var height = 0;
            while (height < map.Height && map.IsSolid(column, height)) { height++; }
            return height;
        }

        /// <summary>開始位置の列からゴールの列まで、地面だけを通る道の段（上り）と穴。</summary>
        public static List<Obstacle> Obstacles(Level level)
        {
            if (level == null) { throw new ArgumentNullException(nameof(level)); }
            var map = level.Map;
            var goal = GoalColumn(level);
            var result = new List<Obstacle>();
            var column = level.StartColumn;
            while (column < goal)
            {
                var here = GroundHeight(map, column);
                var next = column + 1;
                while (next < goal && GroundHeight(map, next) == 0) { next++; }
                var there = GroundHeight(map, next);
                var gap = next - column - 1;
                if (here > 0 && (gap > 0 || there > here))
                {
                    result.Add(new Obstacle(column, gap, there - here));
                }

                column = next;
            }

            return result;
        }

        /// <summary>開始位置の自機の右端から、ゴールの列の左端までの距離（単位）。</summary>
        public static int RunDistance(Level level)
        {
            if (level == null) { throw new ArgumentNullException(nameof(level)); }
            var start = Level.PlaceOnTile(level.StartColumn, level.StartRow, Player.Width, Player.Height);
            return Math.Max(0, (GoalColumn(level) * Units.Tile) - start.Right + 1);
        }

        /// <summary>
        /// 設定の自機で、穴の幅 <paramref name="gapTiles"/>・高さ <paramref name="riseTiles"/> の障害を越えられるか。
        /// 越えたとは、向こうの地面に足場の上で立ったこと。
        /// </summary>
        public static bool CanPass(PlatformerConfig config, int gapTiles, int riseTiles)
        {
            if (config == null) { throw new ArgumentNullException(nameof(config)); }
            if (gapTiles < 0 || gapTiles > Level.MaxWidth / 2) { throw new ArgumentOutOfRangeException(nameof(gapTiles), gapTiles, "穴の幅が範囲外です。"); }
            if (Math.Abs(riseTiles) > Level.MaxHeight) { throw new ArgumentOutOfRangeException(nameof(riseTiles), riseTiles, "高さが範囲外です。"); }
            config.Validate();

            var left = 1 + Math.Max(0, -riseTiles);
            var right = 1 + Math.Max(0, riseTiles);
            var runway = RunwayTiles(config);
            var map = Terrain(runway, gapTiles, left, right);
            var edge = (runway + gapTiles) * Units.Tile;

            // 跳ばずに走ると、穴なら足場から落ち、段なら壁で止まる。その Tick の手前の数タイルの間に跳ぶ Tick を試す。
            var limit = Baseline(config, map, left, edge);
            var window = (TryWindowTiles * Units.Tile / config.RunSpeed) + 2;
            for (var jumpAt = Math.Max(2, limit - window); jumpAt <= limit; jumpAt++)
            {
                if (TryJump(config, map, left, right, edge, jumpAt)) { return true; }
            }

            return false;
        }

        /// <summary>
        /// 3 面を、設定の自機で地面だけを通って抜けられるか。抜けられなければ理由、抜けられれば null。
        /// 走りの最高速のまま走っても制限時間内にゴールへ着かない面も拒否する。
        /// </summary>
        public static string? Unreachable(PlatformerConfig config, IEnumerable<Level> levels)
        {
            if (config == null) { throw new ArgumentNullException(nameof(config)); }
            if (levels == null) { throw new ArgumentNullException(nameof(levels)); }
            config.Validate();
            var checkedObstacles = new Dictionary<(int, int), bool>();
            foreach (var level in levels)
            {
                foreach (var obstacle in Obstacles(level))
                {
                    var key = (obstacle.GapTiles, obstacle.RiseTiles);
                    if (!checkedObstacles.TryGetValue(key, out var passable))
                    {
                        passable = CanPass(config, obstacle.GapTiles, obstacle.RiseTiles);
                        checkedObstacles[key] = passable;
                    }

                    if (!passable)
                    {
                        return $"「{level.Name}」の{obstacle}を越えられません。ジャンプの速さ・重力・走りの速さを見直してください。";
                    }
                }

                if ((long)config.TimeLimitTicks * config.RunSpeed < RunDistance(level))
                {
                    return $"「{level.Name}」は走りの最高速でも制限時間内にゴールへ着きません。制限時間か走りの速さを見直してください。";
                }
            }

            return null;
        }

        private static int GoalColumn(Level level)
        {
            var map = level.Map;
            for (var column = 0; column < map.Width; column++)
            {
                for (var row = 0; row < map.Height; row++)
                {
                    if (map[column, row] == TileKind.Goal) { return column; }
                }
            }

            throw new ArgumentException($"「{level.Name}」にゴールがありません。", nameof(level));
        }

        // 最高速に達するまでの助走（加速の間に進む距離）と、試す範囲の余裕。
        private static int RunwayTiles(PlatformerConfig config)
        {
            var ticks = (config.RunSpeed + config.GroundAccel - 1) / config.GroundAccel;
            var distance = (long)ticks * config.RunSpeed;
            return (int)(distance / Units.Tile) + TryWindowTiles + 3;
        }

        private static TileMap Terrain(int runway, int gap, int left, int right)
        {
            var width = runway + gap + LandingTiles;
            var height = Math.Max(left, right);
            var tiles = new TileKind[width * height];
            for (var column = 0; column < width; column++)
            {
                var top = column < runway ? left : column < runway + gap ? 0 : right;
                for (var row = 0; row < top; row++) { tiles[(row * width) + column] = TileKind.Solid; }
            }

            return new TileMap(width, height, tiles);
        }

        private static Player Start(PlatformerConfig config, int left)
        {
            return new Player(config, Level.PlaceOnTile(1, left, Player.Width, Player.Height));
        }

        // 跳ばずに右へ走り続け、落ち始めるか壁で止まる Tick を返す。
        private static int Baseline(PlatformerConfig config, TileMap map, int left, int edge)
        {
            var player = Start(config, left);
            var run = new PlayerInput(1, true, false);
            var limit = (map.PixelWidth / Math.Max(1, config.GroundAccel)) + map.PixelWidth;
            for (var tick = 1; tick <= limit; tick++)
            {
                var before = player.Body.X;
                player.Step(map, run);
                if (!player.Grounded || (player.Body.X == before && player.Body.Right >= edge - Units.Tile)) { return tick; }
            }

            return limit;
        }

        private static bool TryJump(PlatformerConfig config, TileMap map, int left, int right, int edge, int jumpAt)
        {
            var player = Start(config, left);
            var landing = right * Units.Tile;
            var limit = jumpAt + (4 * (config.JumpSpeed / config.Gravity)) + (map.PixelWidth / config.RunSpeed) + 60;
            for (var tick = 1; tick <= limit; tick++)
            {
                player.Step(map, new PlayerInput(1, true, tick >= jumpAt));
                if (player.Body.Top <= 0) { return false; }
                if (tick > jumpAt && player.Grounded)
                {
                    return player.Body.Bottom == landing && player.Body.Right > edge;
                }
            }

            return false;
        }
    }
}
