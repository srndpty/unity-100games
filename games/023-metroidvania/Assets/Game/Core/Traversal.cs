using System;
using System.Collections.Generic;
using TileMotion;

namespace Game.Core
{
    /// <summary>越える必要がある段差と隙間。右向きに書く（左向きも同じ形）。</summary>
    public readonly struct Crossing
    {
        public Crossing(string name, int rise, int gap, bool doubleJump)
        {
            if (rise < 0 || rise >= Room.Rows) { throw new ArgumentOutOfRangeException(nameof(rise), rise, "段差は 0 以上、部屋の高さ未満にしてください。"); }
            if (gap < 0 || gap >= Room.Columns) { throw new ArgumentOutOfRangeException(nameof(gap), gap, "隙間は 0 以上、部屋の幅未満にしてください。"); }
            if (rise == 0 && gap == 0) { throw new ArgumentException("段差も隙間もない越え方です。"); }
            Name = name ?? throw new ArgumentNullException(nameof(name));
            Rise = rise;
            Gap = gap;
            DoubleJump = doubleJump;
        }

        public string Name { get; }

        /// <summary>踏み切る床から、着く足場の上面までの高さ（タイル）。</summary>
        public int Rise { get; }

        /// <summary>踏み切る床の右端から、着く足場の左端までの隙間（タイル）。</summary>
        public int Gap { get; }

        /// <summary>この場面で二段ジャンプを使えるか。</summary>
        public bool DoubleJump { get; }

        public override string ToString() => $"{Name}（段差 {Rise}・隙間 {Gap}{(DoubleJump ? "・二段ジャンプあり" : string.Empty)}）";
    }

    /// <summary>
    /// 設定の自機で、段差と隙間を実際に越えられるかを確かめる（021 の Traversal の考え方）。式で書かずに <see cref="Player.Step"/> を通すので、
    /// 加減速・可変ジャンプ・接地猶予・1 Tick の順序まで含めてゲーム本体と食い違わない。
    /// 踏み切る床（x &lt; 0、上面 y = 0）の上から右を押し続け、踏み切りの位置（端の手前・端・端から出た接地猶予の間）と助走の長さ、
    /// 二段ジャンプを押す Tick を変えて試し、どれかで足場（x ≥ 隙間、上面 = 段差）に着地すれば越えられるとする。
    /// 同じ跳躍の設定の結果は覚えておく（設定の検証は試合を作るたびに呼ばれるため）。
    /// </summary>
    public static class Traversal
    {
        private const int MaxTicks = 300;

        /// <summary>助走の長さ（踏み切る床の端から自機の右端までの距離。半タイル単位）。</summary>
        private static readonly int[] _runUps = { 0, 4, 16 };

        private static readonly object _lock = new();
        private static readonly Dictionary<string, bool> _cache = new();

        public static bool CanCross(ExplorerConfig config, Crossing crossing)
        {
            if (config == null) { throw new ArgumentNullException(nameof(config)); }
            var key = Key(config, crossing);
            lock (_lock)
            {
                if (_cache.TryGetValue(key, out var known)) { return known; }
            }

            var result = Search(config, crossing);
            lock (_lock) { _cache[key] = result; }
            return result;
        }

        private static bool Search(ExplorerConfig config, Crossing crossing)
        {
            var terrain = new Course(crossing.Rise, crossing.Gap);
            foreach (var runUp in _runUps)
            {
                for (var takeoff = 0; takeoff < 3; takeoff++)
                {
                    // 二段ジャンプは使わない（-1）か、踏み切りから k Tick 目に押し直す。
                    for (var second = crossing.DoubleJump ? 2 : -1; second <= (crossing.DoubleJump ? 60 : -1); second += 2)
                    {
                        if (Attempt(config, terrain, crossing, runUp * (Units.Tile / 2), takeoff, second)) { return true; }
                    }

                    if (crossing.DoubleJump && Attempt(config, terrain, crossing, runUp * (Units.Tile / 2), takeoff, -1)) { return true; }
                }
            }

            return false;
        }

        private static bool Attempt(ExplorerConfig config, Course terrain, Crossing crossing, int runUp, int takeoff, int second)
        {
            var abilities = crossing.DoubleJump ? Ability.DoubleJump : Ability.None;
            var player = new Player(config, new Box(-runUp - Player.Width, 0, Player.Width, Player.Height));
            player.Step(terrain, PlayerInput.None, abilities, false);
            var jumpedAt = -1;
            for (var tick = 0; tick < MaxTicks; tick++)
            {
                var body = player.Body;
                if (jumpedAt < 0)
                {
                    // 踏み切り：端の手前（右端が端）・半分出た所・左端が端（接地猶予の間）。
                    var ready = takeoff switch
                    {
                        0 => body.Right >= 0,
                        1 => body.CenterX >= 0,
                        _ => body.Left >= 0,
                    };
                    if (ready) { jumpedAt = tick; }
                }

                var since = jumpedAt < 0 ? -1 : tick - jumpedAt;
                var jump = since >= 0 && (second < 0 || since != second - 1);
                player.Step(terrain, new PlayerInput(1, jump, false), abilities, false);
                if (player.Grounded && player.Body.Bottom == crossing.Rise * Units.Tile && player.Body.Right > crossing.Gap * Units.Tile)
                {
                    return true;
                }

                if (player.Body.Top < -4 * Units.Tile) { return false; }
            }

            return false;
        }

        /// <summary>跳躍に関わる設定の値と越え方を並べた鍵。</summary>
        private static string Key(ExplorerConfig c, Crossing crossing) => string.Join(",", new[]
        {
            c.MoveSpeed, c.GroundAccel, c.AirAccel, c.GroundDecel, c.AirDecel, c.Gravity, c.MaxFallSpeed,
            c.JumpSpeed, c.DoubleJumpSpeed, c.JumpCutSpeed, c.CoyoteTicks, c.JumpBufferTicks,
            crossing.Rise, crossing.Gap, crossing.DoubleJump ? 1 : 0,
        });

        /// <summary>踏み切る床（列 &lt; 0、行 &lt; 0）と、着く足場（列 ≥ 隙間、行 &lt; 段差）だけの地形。</summary>
        private sealed class Course : ISolidGrid
        {
            private readonly int _rise;
            private readonly int _gap;

            public Course(int rise, int gap)
            {
                _rise = rise;
                _gap = gap;
            }

            public bool IsSolid(int column, int row) => (column < 0 && row < 0) || (column >= _gap && row < _rise);
        }
    }
}
