using System;
using IntGeometry;
using TileMotion;

namespace Game.Core
{
    /// <summary>
    /// 警備員の体。マスの中心から隣のマスの中心へ 1 軸ずつ歩く（1 Tick の移動は直線）。次のマスの中心に着いたらその Tick はそこで止まる。
    /// 向きは 1 Tick に <see cref="WardConfig.TurnSpeed"/> まで回す。判断の指示（<see cref="Order"/>）に従うだけで、自分では決めない。
    /// </summary>
    public sealed class GuardBody
    {
        private readonly WardConfig _config;
        private readonly PathField _path;

        public GuardBody(WardConfig config, FacilityMap map, Cell start, int facing)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _path = new PathField(map);
            Position = start.Center;
            PreviousPosition = Position;
            Facing = Trig.Normalize(facing);
        }

        private GuardBody(GuardBody source)
        {
            _config = source._config;
            _path = source._path.Clone();
            Position = source.Position;
            PreviousPosition = source.PreviousPosition;
            LastWalkSpeed = source.LastWalkSpeed;
            Facing = source.Facing;
            Leg = source.Leg;
        }

        /// <summary>体の中心（目の位置）。</summary>
        public Vec2i Position { get; private set; }

        /// <summary>直前の Tick の始めの位置。</summary>
        public Vec2i PreviousPosition { get; private set; }

        public int Facing { get; private set; }

        /// <summary>歩いている途中なら向かっている隣のマス。</summary>
        public Cell? Leg { get; private set; }

        /// <summary>マスの中心にちょうど立っていればそのマス。</summary>
        public Cell? StandingOn
        {
            get
            {
                if (Leg != null) { return null; }
                var cell = Cell.Of(Position);
                return cell.Center == Position ? cell : (Cell?)null;
            }
        }

        public Box BoxAt(Vec2i center) => new(center.X - (_config.BodySize / 2), center.Y - (_config.BodySize / 2), _config.BodySize, _config.BodySize);

        public Box Box => BoxAt(Position);

        internal int LastWalkSpeed { get; private set; }

        public Box PreviousBox => BoxAt(PreviousPosition);

        public GuardBody Clone() => new(this);

        /// <summary>指示を 1 Tick 実行する。</summary>
        public void Execute(Order order, AlertLevel level)
        {
            PreviousPosition = Position;
            LastWalkSpeed = 0;
            if (order.IsLook)
            {
                Turn(order.FacePoint != null ? AngleTo(order.FacePoint.Value) : order.LookBase + SweepOffset(order.LookElapsed));
                return;
            }

            Walk(order.Goal, SpeedOf(order.Pace, level));
            if (order.FacePoint != null) { Turn(AngleTo(order.FacePoint.Value)); }
            else if (Leg != null) { Turn(AngleTo(Leg.Value.Center)); }
        }

        /// <summary>見回しの向きのずれ。三角波（0 → +振れ幅 → 0 → −振れ幅 → 0）を周期で繰り返す。</summary>
        public int SweepOffset(int elapsed)
        {
            var quarter = _config.LookPeriodTicks / 4;
            var t = elapsed % _config.LookPeriodTicks;
            int num;
            if (t < quarter) { num = t; }
            else if (t < 3 * quarter) { num = (2 * quarter) - t; }
            else { num = t - (4 * quarter); }
            return (int)((long)_config.LookSweep * num / quarter);
        }

        public int SpeedOf(Pace pace, AlertLevel level) => pace switch
        {
            Pace.Patrol => level == AlertLevel.Normal ? _config.PatrolSpeed : _config.CautionPatrolSpeed,
            Pace.Investigate => _config.InvestigateSpeed,
            Pace.Chase => _config.ChaseSpeed,
            Pace.Search => _config.SearchSpeed,
            _ => throw new ArgumentOutOfRangeException(nameof(pace), pace, "未定義の歩き方です。"),
        };

        /// <summary>テスト・撮影用：体をマスの中心に置く。</summary>
        public void Place(Cell cell, int facing)
        {
            Position = cell.Center;
            PreviousPosition = Position;
            LastWalkSpeed = 0;
            Leg = null;
            Facing = Trig.Normalize(facing);
        }

        public void MixInto(ref StateHash hash)
        {
            hash.Mix(Position.X);
            hash.Mix(Position.Y);
            hash.Mix(PreviousPosition.X);
            hash.Mix(PreviousPosition.Y);
            hash.Mix(Facing);
            hash.Mix(Leg != null);
            hash.Mix(Leg?.Column ?? 0);
            hash.Mix(Leg?.Row ?? 0);
        }

        private void Walk(Cell goal, int speed)
        {
            if (Leg == null)
            {
                var here = Cell.Of(Position);
                if (here == goal) { return; }
                var next = _path.NextStep(here, goal);
                if (next == here) { return; }
                Leg = next;
            }

            LastWalkSpeed = speed;
            var target = Leg.Value.Center;
            var dx = target.X - Position.X;
            var dy = target.Y - Position.Y;
            var remaining = Math.Abs(dx) + Math.Abs(dy);
            if (remaining <= speed)
            {
                Position = target;
                Leg = null;
                return;
            }

            Position = new Vec2i(Position.X + (Math.Sign(dx) * speed), Position.Y + (Math.Sign(dy) * speed));
        }

        private int AngleTo(Vec2i point)
        {
            var d = point - Position;
            return d.IsZero ? Facing : Trig.Atan2(d.Y, d.X, Facing);
        }

        private void Turn(int target)
        {
            var diff = Trig.Normalize(target - Facing);
            if (diff > Trig.FullTurn / 2) { diff -= Trig.FullTurn; }
            var step = Math.Max(-_config.TurnSpeed, Math.Min(_config.TurnSpeed, diff));
            Facing = Trig.Normalize(Facing + step);
        }
    }

    /// <summary>自機の歩き方。</summary>
    public enum Gait
    {
        Walk,
        Sneak,
        Run,
    }

    /// <summary>1 Tick の自機の入力。<see cref="X"/>・<see cref="Y"/> は -1・0・1。忍び足と走りを両方押したら忍び足。</summary>
    public readonly struct IntruderInput
    {
        public IntruderInput(int x, int y, bool run, bool sneak)
        {
            if (x < -1 || x > 1) { throw new ArgumentOutOfRangeException(nameof(x), x, "-1・0・1 のどれかにしてください。"); }
            if (y < -1 || y > 1) { throw new ArgumentOutOfRangeException(nameof(y), y, "-1・0・1 のどれかにしてください。"); }
            X = x;
            Y = y;
            Run = run;
            Sneak = sneak;
        }

        public int X { get; }

        public int Y { get; }

        public bool Run { get; }

        public bool Sneak { get; }

        public Gait Gait => Sneak ? Gait.Sneak : Run ? Gait.Run : Gait.Walk;

        public static IntruderInput None => default;
    }

    /// <summary>足音。そのマスから壁を回り込んで <see cref="Radius"/> マスまで届く。</summary>
    public readonly struct Noise
    {
        public Noise(Cell source, int radius, Gait gait)
        {
            Source = source;
            Radius = radius;
            Gait = gait;
        }

        public Cell Source { get; }

        public int Radius { get; }

        public Gait Gait { get; }
    }

    /// <summary>自機。8 方向に歩き、壁・木箱で止まる。動いている間、<see cref="WardConfig.StepTicks"/> ごとに足音を出す。</summary>
    public sealed class Intruder
    {
        private readonly WardConfig _config;
        private int _stepCounter;

        public Intruder(WardConfig config, Cell start)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            Body = BoxAt(start.Center);
            Motion = Motion.Still(Body);
        }

        private Intruder(Intruder source)
        {
            _config = source._config;
            _stepCounter = source._stepCounter;
            Body = source.Body;
            Motion = source.Motion;
            Gait = source.Gait;
            FacingX = source.FacingX;
            FacingY = source.FacingY;
        }

        public Box Body { get; private set; }

        public Vec2i Center => new(Body.CenterX, Body.Y + (Body.Height / 2));

        /// <summary>直前の Tick の移動の経路（横 → 縦）。</summary>
        public Motion Motion { get; private set; }

        public Gait Gait { get; private set; }

        /// <summary>最後に動いた向き（描画用）。</summary>
        public int FacingX { get; private set; }

        public int FacingY { get; private set; } = -1;

        public Intruder Clone() => new(this);

        /// <summary>1 Tick 動く。足音が出たら返す。</summary>
        public Noise? Move(FacilityMap map, IntruderInput input)
        {
            Gait = input.Gait;
            var speed = Gait switch
            {
                Gait.Sneak => _config.SneakSpeed,
                Gait.Run => _config.RunSpeed,
                _ => _config.WalkSpeed,
            };
            if (input.X != 0 && input.Y != 0) { speed = WardConfig.DiagonalOf(speed); }

            var start = Body;
            var body = Body;
            BodyMotion.MoveX(map, ref body, input.X * speed);
            var corner = body;
            BodyMotion.MoveY(map, ref body, input.Y * speed);
            Body = body;
            Motion = new Motion(start, corner, body);
            if (input.X != 0 || input.Y != 0)
            {
                FacingX = input.X;
                FacingY = input.Y;
            }

            if (body == start)
            {
                _stepCounter = 0;
                return null;
            }

            var emit = _stepCounter == 0;
            _stepCounter = (_stepCounter + 1) % _config.StepTicks;
            if (!emit) { return null; }
            var radius = Gait switch
            {
                Gait.Sneak => _config.SneakNoise,
                Gait.Run => _config.RunNoise,
                _ => _config.WalkNoise,
            };
            return new Noise(Cell.Of(Center), radius, Gait);
        }

        /// <summary>テスト・撮影用：体の中心をマスの中心に置く。</summary>
        public void Place(Cell cell)
        {
            Body = BoxAt(cell.Center);
            Motion = Motion.Still(Body);
            _stepCounter = 0;
        }

        public void MixInto(ref StateHash hash)
        {
            hash.Mix(Body.X);
            hash.Mix(Body.Y);
            hash.Mix(Motion.Start.X);
            hash.Mix(Motion.Start.Y);
            hash.Mix(Motion.Corner.X);
            hash.Mix(Motion.Corner.Y);
            hash.Mix(_stepCounter);
            hash.Mix((int)Gait);
            hash.Mix(FacingX);
            hash.Mix(FacingY);
        }

        private Box BoxAt(Vec2i center) => new(center.X - (_config.BodySize / 2), center.Y - (_config.BodySize / 2), _config.BodySize, _config.BodySize);
    }
}
