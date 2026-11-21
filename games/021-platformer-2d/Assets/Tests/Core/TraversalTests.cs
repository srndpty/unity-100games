using System;
using NUnit.Framework;
using static Game.Core.Tests.TestKit;

namespace Game.Core.Tests
{
    /// <summary>設定で面の地面の道（段・穴・距離）を通れるかの検証。</summary>
    public sealed class TraversalTests
    {
        private static readonly int[] _heights = { 2, 2, 3, 0, 0, 2, 4, 4, 1, 1 };

        /// <summary>列ごとの地面の高さから地図を作る。列 0 が開始位置、最後の列の次がゴール。<paramref name="platformColumn"/> の 5 段目に浮いた足場を置く。</summary>
        private static Level FromHeights(int[] heights, int platformColumn)
        {
            const int rows = 7;
            var lines = new string[rows];
            for (var line = 0; line < rows; line++)
            {
                var row = rows - 1 - line;
                var chars = new char[heights.Length + 1];
                for (var column = 0; column < heights.Length; column++)
                {
                    chars[column] = row < heights[column] ? '#' : (column == 0 && row == heights[0]) ? 'S' : (column == platformColumn && row == 5) ? '#' : '.';
                }

                chars[heights.Length] = row == 0 ? '#' : 'G';
                lines[line] = new string(chars);
            }

            return Map(lines);
        }

        [Test]
        public void Obstacles_ListsClimbsAndPitsOnGroundRouteOnly()
        {
            // 上り 1・穴 2（向こうが 1 低い）・上り 2・下り。穴の上の浮いた足場は地面に数えない。
            var level = FromHeights(_heights, 3);
            Assert.That(Traversal.GroundHeight(level.Map, 2), Is.EqualTo(3));
            Assert.That(Traversal.GroundHeight(level.Map, 3), Is.Zero, "浮いた足場だけの列は穴");
            Assert.That(Traversal.Obstacles(level), Is.EqualTo(new[]
            {
                new Obstacle(1, 0, 1),
                new Obstacle(2, 2, -1),
                new Obstacle(5, 0, 2),
            }));
        }

        [Test]
        public void Obstacles_PitBetweenGroundsKeepsHeightDifference()
        {
            var level = Map(
                ".......G",
                "S......G",
                "##....##",
                "##....##");
            Assert.That(Traversal.Obstacles(level), Is.EqualTo(new[] { new Obstacle(1, 4, 0) }));
            Assert.That(Traversal.GroundHeight(level.Map, 3), Is.Zero);
        }

        [Test]
        public void RunDistance_IsFromPlayerRightEdgeToGoalColumn()
        {
            var level = Map(
                ".....G",
                "S....G",
                "######");
            var start = Level.PlaceOnTile(0, 1, Player.Width, Player.Height);
            Assert.That(Traversal.RunDistance(level), Is.EqualTo((5 * T) - start.Right + 1));
        }

        /// <summary>段を越えられるかは、押し続けたジャンプの最高点（自機を実際に動かした値）が段の高さに届くかと一致する。</summary>
        [Test]
        public void Climb_MatchesSimulatedApex()
        {
            var flat = Flat();
            foreach (var jump in new[] { 4, 5, 8, 20, 33, 48, 64, 96, 140, 200, 255 })
            {
                var config = new PlatformerConfig { JumpSpeed = jump, JumpCutSpeed = 0, StompBounce = Math.Min(56, jump), HopperJumpSpeed = 72 };
                var apex = Apex(flat, 400, config);
                for (var rise = 1; rise <= 8; rise++)
                {
                    Assert.That(Traversal.CanPass(config, 0, rise), Is.EqualTo(apex >= rise * T), $"ジャンプ {jump}（最高点 {apex}）で高さ {rise}");
                }
            }
        }

        [Test]
        public void Climb_BoundaryOneUnitOfApex()
        {
            // 最高点が 2 タイルにちょうど届く最小のジャンプの速さを探し、その 1 つ下では届かないことを確かめる。
            var flat = Flat();
            var minimum = -1;
            for (var jump = 4; jump <= 96 && minimum < 0; jump++)
            {
                var config = new PlatformerConfig { JumpSpeed = jump, JumpCutSpeed = 0, StompBounce = 4 };
                if (Apex(flat, 400, config) >= 2 * T) { minimum = jump; }
            }

            Assert.That(minimum, Is.GreaterThan(4));
            Assert.That(Traversal.CanPass(new PlatformerConfig { JumpSpeed = minimum, JumpCutSpeed = 0, StompBounce = 4 }, 0, 2), Is.True);
            Assert.That(Traversal.CanPass(new PlatformerConfig { JumpSpeed = minimum - 1, JumpCutSpeed = 0, StompBounce = 4 }, 0, 2), Is.False);
        }

        [Test]
        public void Gap_WiderIsNeverEasierAndHasABoundary()
        {
            var config = new PlatformerConfig();
            var widest = 0;
            for (var gap = 1; gap <= 20; gap++)
            {
                var pass = Traversal.CanPass(config, gap, 0);
                if (pass)
                {
                    Assert.That(widest, Is.EqualTo(gap - 1), "越えられる幅は 1 から連続する");
                    widest = gap;
                }
            }

            Assert.That(widest, Is.InRange(4, 12), "既定の設定は 3 面の最も広い穴（4 タイル）を越えられる");
            Assert.That(Traversal.CanPass(config, widest, -2), Is.True, "低い地面へは越えやすい");
            Assert.That(Traversal.CanPass(config, widest + 1, 0), Is.False);
        }

        [Test]
        public void Gap_WalkSpeedDoesNotLimitBecauseRunIsUsed()
        {
            var slowRun = new PlatformerConfig { WalkSpeed = 8, RunSpeed = 8 };
            var fastRun = new PlatformerConfig { WalkSpeed = 8, RunSpeed = 60 };
            Assert.That(Traversal.CanPass(slowRun, 4, 0), Is.False);
            Assert.That(Traversal.CanPass(fastRun, 4, 0), Is.True);
        }

        [Test]
        public void Default_PassesAllLevels()
        {
            Assert.That(Traversal.Unreachable(new PlatformerConfig(), Levels.All()), Is.Null);
        }

        /// <summary>レビューの例：跳んだ Tick の重力で上向きの速さが 0 になり、1 面の高さ 2 の段を越えられない。</summary>
        [Test]
        public void JumpThatCannotRise_IsRejected()
        {
            var config = new PlatformerConfig { JumpSpeed = 4, JumpCutSpeed = 0, StompBounce = 4 };
            Assert.DoesNotThrow(config.Validate, "個々の値は範囲内");
            Assert.That(Traversal.Unreachable(config, Levels.All()), Does.Contain("野原").And.Contain("越えられません"));
        }

        [Test]
        public void TimeLimitTooShortForRunSpeed_IsRejected()
        {
            var config = new PlatformerConfig { WalkSpeed = 1, RunSpeed = 1, GroundAccel = 1 };
            Assert.That(Traversal.Unreachable(config, Levels.All()), Does.Contain("越えられません").Or.Contain("制限時間"));

            // 段と穴は越えられるが、時間内に走りきれない設定。
            var hurried = new PlatformerConfig { TimeLimitSeconds = PlatformerConfig.MinTimeLimitSeconds };
            Assert.That(Traversal.Unreachable(hurried, Levels.All()), Does.Contain("制限時間"));
        }

        [Test]
        public void RejectsInvalidArguments()
        {
            var config = new PlatformerConfig();
            Assert.Throws<ArgumentOutOfRangeException>(() => Traversal.CanPass(config, -1, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => Traversal.CanPass(new PlatformerConfig { Gravity = 0 }, 1, 0));
            Assert.Throws<ArgumentNullException>(() => Traversal.Unreachable(config, null!));
            Assert.Throws<ArgumentOutOfRangeException>(() => _ = new Obstacle(0, -1, 0));
        }
    }
}
