using System;
using Game.Core;

namespace Game.App
{
    /// <summary>
    /// 撮影・テスト用の操縦役。接地しているときに数通りの操作（向き × ジャンプの長さ）を、
    /// 面の挑戦の複製（<see cref="LevelRun.Clone"/>）で先読みして選ぶ。
    /// 先読みは「選んだ操作 → 単純な方針（右へ走り、障害の手前で跳ぶ）」を一定 Tick 続け、
    /// ミスしないもののうち、ゴールに着く・右へ進んだ距離が長いものを選ぶ。乱数は使わない。
    /// </summary>
    public sealed class Autopilot
    {
        public const int Horizon = 120;
        public const int FullJumpTicks = 24;

        private static readonly Option[] _options =
        {
            new(1, 0),
            new(1, FullJumpTicks),
            new(1, 8),
            new(0, 0),
            new(0, FullJumpTicks),
            new(-1, 0),
            new(-1, FullJumpTicks),
        };

        private int _horizontal = 1;
        private int _jumpLeft;

        /// <summary>計画を捨てて初めから考え直す（面の開始・やり直しのとき）。</summary>
        public void Reset()
        {
            _horizontal = 1;
            _jumpLeft = 0;
        }

        /// <summary>この Tick の入力。</summary>
        public PlayerInput Next(LevelRun run)
        {
            if (run == null) { throw new ArgumentNullException(nameof(run)); }
            if (run.IsOver) { return PlayerInput.None; }

            if (run.Player.Grounded && _jumpLeft == 0)
            {
                var best = Choose(run);
                _horizontal = best.Horizontal;
                _jumpLeft = best.JumpTicks;
            }

            var jump = _jumpLeft > 0;
            if (_jumpLeft > 0) { _jumpLeft--; }
            return new PlayerInput(_horizontal, true, jump);
        }

        private static Option Choose(LevelRun run)
        {
            var best = _options[0];
            var bestScore = long.MinValue;
            foreach (var option in _options)
            {
                var score = Evaluate(run, option);
                if (score > bestScore)
                {
                    best = option;
                    bestScore = score;
                }
            }

            return best;
        }

        private static long Evaluate(LevelRun source, Option option)
        {
            var run = source.Clone();
            var jumpLeft = option.JumpTicks;
            var horizontal = option.Horizontal;
            var holdOption = 8;
            for (var tick = 0; tick < Horizon; tick++)
            {
                if (holdOption == 0 && jumpLeft == 0 && run.Player.Grounded)
                {
                    horizontal = 1;
                    jumpLeft = NeedsJump(run) ? FullJumpTicks : 0;
                }

                if (holdOption > 0) { holdOption--; }
                var jump = jumpLeft > 0;
                if (jumpLeft > 0) { jumpLeft--; }
                run.Tick(new PlayerInput(horizontal, true, jump));
                if (run.Status == RunStatus.Cleared) { return long.MaxValue - tick; }
                if (run.Status == RunStatus.Missed) { return long.MinValue + tick + 1; }
            }

            return run.Player.Body.X;
        }

        /// <summary>単純な方針：前に壁・穴・敵があれば跳ぶ。</summary>
        private static bool NeedsJump(LevelRun run)
        {
            var body = run.Player.Body;
            var map = run.Map;
            var footRow = Units.FloorDiv(body.Bottom, Units.Tile);
            var front = Units.FloorDiv(body.Right - 1, Units.Tile);
            for (var column = front + 1; column <= front + 2; column++)
            {
                if (map.IsSolid(column, footRow) || !map.IsSolid(column, footRow - 1)) { return true; }
            }

            foreach (var enemy in run.Enemies)
            {
                if (!enemy.IsAlive) { continue; }
                var gap = enemy.Body.Left - body.Right;
                if (gap >= -Units.Tile && gap <= 3 * Units.Tile && enemy.Body.Bottom < body.Top + Units.Tile && enemy.Body.Top > body.Bottom - Units.Tile)
                {
                    return true;
                }
            }

            return false;
        }

        private readonly struct Option
        {
            public Option(int horizontal, int jumpTicks)
            {
                Horizontal = horizontal;
                JumpTicks = jumpTicks;
            }

            public int Horizontal { get; }

            public int JumpTicks { get; }
        }
    }
}
