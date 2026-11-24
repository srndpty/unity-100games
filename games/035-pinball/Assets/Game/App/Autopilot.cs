using System;
using Game.Core;
using IntGeometry;

namespace Game.App
{
    /// <summary>
    /// 撮影・テスト・PlayMode で共有する操縦役（002）。
    /// <list type="bullet">
    /// <item>プランジャー：レーンに球があれば、試合のシードで決めた Tick 数だけ引いて離す。</item>
    /// <item>フリッパー：球がフリッパーの近くへ来たら、試合を複製して「今押す」を <see cref="LookaheadTicks"/> Tick 先まで試し（021）、
    /// 球が打ち上がるなら押して <see cref="HoldTicks"/> Tick 保つ。</item>
    /// <item>近づくたびに <see cref="MissPercent"/> % の確率でわざと見送る（上手すぎて試合が終わらないのを防ぐ）。判断は近づき 1 回に 1 度。</item>
    /// </list>
    /// </summary>
    public sealed class Autopilot : IPinballPilot
    {
        public const int LookaheadTicks = 24;

        public const int HoldTicks = 14;

        public const int MissPercent = 12;

        /// <summary>フリッパーの近くとみなす範囲（軸からの距離の余り、軸より上の高さ）。</summary>
        private const int ZoneMargin = Board.Unit * 3 / 2;

        private const int ZoneHeight = Board.Unit * 4;

        private SeededRandom _random = new(1);
        private int _pullTarget;
        private readonly Side _left = new();
        private readonly Side _right = new();

        public void Reset(int seed)
        {
            _random = new SeededRandom(unchecked(seed ^ 0x50494C54));
            _pullTarget = 0;
            _left.Clear();
            _right.Clear();
        }

        public PinballInput InputFor(PinballRun run)
        {
            if (run == null) { throw new ArgumentNullException(nameof(run)); }
            var plunger = Plunger(run);
            var left = Flip(run, FlipperSide.Left, _left);
            var right = Flip(run, FlipperSide.Right, _right);
            return new PinballInput(left, right, plunger);
        }

        private bool Plunger(PinballRun run)
        {
            if (!HasLaneBall(run))
            {
                _pullTarget = 0;
                return false;
            }

            var config = run.Config;
            if (_pullTarget == 0) { _pullTarget = _random.Range(config.PlungerMaxTicks * 2 / 3, config.PlungerMaxTicks); }
            if (run.Pull < _pullTarget) { return true; }
            _pullTarget = 0;
            return false;
        }

        private bool Flip(PinballRun run, FlipperSide side, Side state)
        {
            if (state.Hold > 0)
            {
                state.Hold--;
                return true;
            }

            if (!NearFlipper(run, side))
            {
                state.Decided = false;
                return false;
            }

            if (state.Decided) { return false; }
            if (!LaunchesUp(run, side)) { return false; }

            // 打てる時機が来た近づき 1 回につき 1 度だけ、見送るかを決める。
            state.Decided = true;
            if (_random.Next(100) < MissPercent) { return false; }
            state.Hold = HoldTicks - 1;
            return true;
        }

        /// <summary>台の上の球が、フリッパーの近くで下へ動いているか。</summary>
        private static bool NearFlipper(PinballRun run, FlipperSide side)
        {
            var spec = run.Table.Flipper(side);
            var reach = (long)spec.Length + ZoneMargin;
            for (var slot = 0; slot < run.BallSlots; slot++)
            {
                var ball = run.BallAt(slot);
                if (ball.State != BallState.Free) { continue; }
                var offset = ball.Position - spec.Pivot;
                if (offset.LengthSquared <= reach * reach && offset.Y <= ZoneHeight && ball.Velocity.Y <= 0) { return true; }
            }

            return false;
        }

        /// <summary>今押して保てば、先読みの間に球がフリッパーより十分上へ打ち上がるか（失う球がないこと）。</summary>
        private static bool LaunchesUp(PinballRun run, FlipperSide side)
        {
            var copy = run.Clone();
            var pivot = run.Table.Flipper(side).Pivot;
            var lostBefore = copy.BallsLost;
            for (var tick = 0; tick < LookaheadTicks && copy.Status == RunStatus.Playing; tick++)
            {
                var hold = tick < HoldTicks;
                copy.Step(new PinballInput(side == FlipperSide.Left && hold, side == FlipperSide.Right && hold, false));
                if (copy.BallsLost != lostBefore) { return false; }
                for (var slot = 0; slot < copy.BallSlots; slot++)
                {
                    var ball = copy.BallAt(slot);
                    if (ball.State == BallState.Free && ball.Position.Y > pivot.Y + ZoneHeight && ball.Velocity.Y > 0) { return true; }
                }
            }

            return false;
        }

        private static bool HasLaneBall(PinballRun run)
        {
            for (var slot = 0; slot < run.BallSlots; slot++)
            {
                if (run.BallAt(slot).State == BallState.Lane) { return true; }
            }

            return false;
        }

        /// <summary>片側のフリッパーの判断の状態。</summary>
        private sealed class Side
        {
            public int Hold;

            public bool Decided;

            public void Clear()
            {
                Hold = 0;
                Decided = false;
            }
        }
    }
}
