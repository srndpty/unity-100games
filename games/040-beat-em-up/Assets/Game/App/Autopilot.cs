using System;
using Game.Core;
using IntGeometry;

namespace Game.App
{
    /// <summary>
    /// プレイヤー側の操縦役。撮影・App テスト・PlayMode テストで共有する。
    /// 近い敵と奥行きをそろえて正面に立ち、攻撃を連打してコンボを繋ぐ。囲まれたら緊急技。区画が終われば右へ進む。
    /// 打ち方はシード付き乱数でばらつかせる。<see cref="DecisionInterval"/> を大きくすると判断が遅い人寄りの操縦になる。
    /// </summary>
    public sealed class Autopilot
    {
        private const int Px = Game.Core.Px.One;
        private SeededRandom _random;
        private Pad _last;
        private int _countdown;
        private int _presses;

        public Autopilot(int seed, int decisionInterval = 1, bool useSpecial = true)
        {
            if (decisionInterval < 1) { throw new ArgumentOutOfRangeException(nameof(decisionInterval)); }
            _random = new SeededRandom(seed);
            DecisionInterval = decisionInterval;
            UseSpecial = useSpecial;
        }

        /// <summary>判断を変える間隔（Tick）。間は同じ方向を押し続け、攻撃は判断の Tick にだけ押す。</summary>
        public int DecisionInterval { get; }

        public bool UseSpecial { get; }

        /// <summary>試合ごとに判断の状態を捨てる。</summary>
        public void Reset(int seed)
        {
            _random = new SeededRandom(seed);
            _last = Pad.Neutral;
            _countdown = 0;
            _presses = 0;
        }

        public Pad PadFor(Brawl brawl)
        {
            if (brawl == null) { throw new ArgumentNullException(nameof(brawl)); }
            if (_countdown > 0)
            {
                _countdown--;
                return new Pad(_last.X, _last.Z, false, false, false);
            }

            _countdown = DecisionInterval - 1;
            _last = Decide(brawl);
            return _last;
        }

        private Pad Decide(Brawl brawl)
        {
            var p = brawl.Player;
            switch (p.State)
            {
                case ActorState.Knockdown:
                case ActorState.Wakeup:
                case ActorState.Dead:
                case ActorState.Jump:
                    return Pad.Neutral;
                case ActorState.Grab:
                    return Tap(0, 0);
            }

            if (!brawl.Locked)
            {
                int mid = (brawl.Config.ZMin + brawl.Config.ZMax) / 2;
                return new Pad(1, Step(mid - p.Z, 2 * Px), false, false, false);
            }

            if (UseSpecial && ShouldSpecial(brawl))
            {
                _presses++;
                return new Pad(0, 0, false, false, (_presses & 1) == 0);
            }

            var target = Target(brawl);
            if (target == null)
            {
                int center = brawl.CameraX + (brawl.Config.ViewWidth / 2);
                return new Pad(Step(center - p.X, 24 * Px), 0, false, false, false);
            }

            int dx = target.X - p.X;
            int dz = target.Z - p.Z;
            int side = dx >= 0 ? 1 : -1;
            int distance = Math.Abs(dx);
            bool facing = p.Facing == side;
            bool aligned = Math.Abs(dz) <= 3 * Px;
            bool hurtable = Brawl.Hurtable(target);
            if (aligned && facing && hurtable && distance >= 8 * Px && distance <= 24 * Px)
            {
                return Tap(0, 0);
            }

            int wantX = target.X - (side * 18 * Px);
            int moveX = Step(wantX - p.X, 3 * Px);
            if (!facing && distance <= 30 * Px) { moveX = side; }
            if (!hurtable && distance < 30 * Px) { moveX = -side; }
            int moveZ = Step(dz, 2 * Px);
            if (_random.Next(12) == 0) { moveZ = 0; }
            return new Pad(moveX, moveZ, false, false, false);
        }

        private Pad Tap(int x, int z)
        {
            _presses++;
            bool press = (_presses & 1) == 0 && _random.Next(10) != 0;
            return new Pad(x, z, press, false, false);
        }

        private bool ShouldSpecial(Brawl brawl)
        {
            var p = brawl.Player;
            var special = p.Def!.Find("special");
            if (p.Health <= special.Cost * 3 || p.HitStop > 0) { return false; }
            int threats = 0;
            int close = 0;
            for (int i = 1; i < brawl.SlotCount; i++)
            {
                var e = brawl.At(i);
                if (!e.Active || e.Defeated || e.Kind == ActorKind.Boss) { continue; }
                int dx = Math.Abs(e.X - p.X);
                int dz = Math.Abs(e.Z - p.Z);
                if (dx > 34 * Px || dz > 18 * Px) { continue; }
                close++;
                if (e.Move != null && e.Phase == MovePhase.Startup) { threats++; }
            }

            return (threats >= 1 && close >= 3) || (p.State == ActorState.HitStun && close >= 2 && _random.Next(3) == 0);
        }

        private static Actor? Target(Brawl brawl)
        {
            var p = brawl.Player;
            Actor? best = null;
            long bestCost = long.MaxValue;
            for (int i = 1; i < brawl.SlotCount; i++)
            {
                var e = brawl.At(i);
                if (!e.Active || e.Defeated || e.State == ActorState.Dead) { continue; }
                if (e.X < brawl.CameraX || e.X > brawl.CameraX + brawl.Config.ViewWidth) { continue; }
                long cost = Math.Abs((long)e.X - p.X) + (3L * Math.Abs((long)e.Z - p.Z));
                if (!Brawl.Hurtable(e)) { cost += 80L * Px; }
                if (cost < bestCost)
                {
                    best = e;
                    bestCost = cost;
                }
            }

            return best;
        }

        private static int Step(int delta, int deadZone)
        {
            if (delta > deadZone) { return 1; }
            return delta < -deadZone ? -1 : 0;
        }
    }
}
