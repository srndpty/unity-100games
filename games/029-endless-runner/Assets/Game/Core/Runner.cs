using System;
using TileMotion;

namespace Game.Core
{
    /// <summary>横の操作。</summary>
    public enum Steer
    {
        None,
        Left,
        Right,
    }

    /// <summary>縦の操作。</summary>
    public enum Leap
    {
        None,
        Jump,
        Slide,
    }

    /// <summary>1 Tick の操作。横と縦は独立に 1 つずつ。</summary>
    public readonly struct Command : IEquatable<Command>
    {
        public Command(Steer steer, Leap leap)
        {
            if (!Enum.IsDefined(typeof(Steer), steer)) { throw new ArgumentOutOfRangeException(nameof(steer), steer, "未定義の操作です。"); }
            if (!Enum.IsDefined(typeof(Leap), leap)) { throw new ArgumentOutOfRangeException(nameof(leap), leap, "未定義の操作です。"); }
            Steer = steer;
            Leap = leap;
        }

        public static Command None => default;

        public Steer Steer { get; }

        public Leap Leap { get; }

        public bool Equals(Command other) => Steer == other.Steer && Leap == other.Leap;

        public override bool Equals(object? obj) => obj is Command other && Equals(other);

        public override int GetHashCode() => ((int)Steer * 3) + (int)Leap;

        public override string ToString() => $"({Steer}, {Leap})";

        public static bool operator ==(Command left, Command right) => left.Equals(right);

        public static bool operator !=(Command left, Command right) => !left.Equals(right);
    }

    /// <summary>操作を受け付けたか。</summary>
    public enum Outcome
    {
        /// <summary>効いた（操作なしも含む）。</summary>
        Applied,

        /// <summary>捨てた（端より外へのレーン移動、スライド中のスライド）。</summary>
        Dropped,

        /// <summary>今は効かない（レーン移動中の移動、空中のジャンプ・スライド）。先行入力として覚えておける。</summary>
        Deferred,
    }

    /// <summary>
    /// 横の状態。<see cref="PrevX"/> は直前の Tick の始めの位置で、その Tick の軌跡（掃引）を状態だけから決められる。
    /// 移動中は <see cref="From"/> ≠ <see cref="To"/>、着いたら From = To。
    /// </summary>
    public readonly struct LaneState : IEquatable<LaneState>
    {
        public LaneState(int from, int to, int progress, int x, int prevX)
        {
            From = from;
            To = to;
            Progress = progress;
            X = x;
            PrevX = prevX;
        }

        public int From { get; }

        /// <summary>今いる（向かっている）レーン。</summary>
        public int To { get; }

        /// <summary>移動を始めてからの Tick 数（止まっている間は 0）。</summary>
        public int Progress { get; }

        public int X { get; }

        public int PrevX { get; }

        public bool IsMoving => From != To;

        /// <summary>レーン <paramref name="lane"/> に止まっている状態。</summary>
        public static LaneState Rest(RunConfig config, int lane)
        {
            if (lane < 0 || lane >= RunConfig.Lanes) { throw new ArgumentOutOfRangeException(nameof(lane), lane, "レーンは 0〜2 にしてください。"); }
            var x = config.LaneX(lane);
            return new(lane, lane, 0, x, x);
        }

        public LaneState Next(Steer steer, RunConfig config, out Outcome outcome)
        {
            var from = From;
            var to = To;
            var progress = Progress;
            outcome = Outcome.Applied;
            if (steer != Steer.None)
            {
                if (IsMoving)
                {
                    outcome = Outcome.Deferred;
                }
                else
                {
                    var target = to + (steer == Steer.Left ? -1 : 1);
                    if (target < 0 || target >= RunConfig.Lanes)
                    {
                        outcome = Outcome.Dropped;
                    }
                    else
                    {
                        to = target;
                        progress = 0;
                    }
                }
            }

            var x = X;
            if (from != to)
            {
                progress++;
                var fromX = config.LaneX(from);
                x = fromX + ((config.LaneX(to) - fromX) * progress / config.LaneChangeTicks);
                if (progress >= config.LaneChangeTicks)
                {
                    from = to;
                    progress = 0;
                }
            }

            return new(from, to, progress, x, X);
        }

        public bool Equals(LaneState other) => From == other.From && To == other.To && Progress == other.Progress && X == other.X && PrevX == other.PrevX;

        public override bool Equals(object? obj) => obj is LaneState other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(From, To, Progress, X, PrevX);

        public override string ToString() => $"Lane({From}→{To} #{Progress}, x {PrevX}→{X})";

        public static bool operator ==(LaneState left, LaneState right) => left.Equals(right);

        public static bool operator !=(LaneState left, LaneState right) => !left.Equals(right);
    }

    /// <summary>縦の状態（高さ・縦の速さ・スライド）。<see cref="PrevY"/> は直前の Tick の始めの高さ。</summary>
    public readonly struct VerticalState : IEquatable<VerticalState>
    {
        public VerticalState(int y, int velocity, int prevY, Countdown slide)
        {
            Y = y;
            Velocity = velocity;
            PrevY = prevY;
            Slide = slide;
        }

        public static VerticalState Rest => default;

        public int Y { get; }

        public int Velocity { get; }

        public int PrevY { get; }

        /// <summary>スライドの残り Tick（この Tick を含む）。</summary>
        public Countdown Slide { get; }

        public bool IsGrounded => Y == 0 && Velocity == 0;

        public bool IsSliding => Slide.IsActive;

        /// <summary>この Tick の当たり箱の高さ（Tick の間ずっとこの高さとして掃引する）。</summary>
        public int Height(RunConfig config) => IsSliding ? config.SlideHeight : config.StandHeight;

        public VerticalState Next(Leap leap, RunConfig config, out Outcome outcome)
        {
            var slide = Slide;
            slide.Tick();
            var y = Y;
            var velocity = Velocity;
            outcome = Outcome.Applied;
            switch (leap)
            {
                case Leap.None:
                    break;
                case Leap.Jump:
                    if (IsGrounded)
                    {
                        velocity = config.JumpVelocity;
                        slide.Clear();
                    }
                    else
                    {
                        outcome = Outcome.Deferred;
                    }

                    break;
                case Leap.Slide:
                    if (!IsGrounded) { outcome = Outcome.Deferred; }
                    else if (slide.IsActive) { outcome = Outcome.Dropped; }
                    else { slide.Arm(config.SlideTicks); }
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(leap), leap, "未定義の操作です。");
            }

            if (y > 0 || velocity > 0)
            {
                y += velocity;
                velocity -= config.Gravity;
                if (y <= 0)
                {
                    y = 0;
                    velocity = 0;
                }
            }

            return new(y, velocity, Y, slide);
        }

        public bool Equals(VerticalState other) => Y == other.Y && Velocity == other.Velocity && PrevY == other.PrevY && Slide == other.Slide;

        public override bool Equals(object? obj) => obj is VerticalState other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(Y, Velocity, PrevY, Slide.Remaining);

        public override string ToString() => $"Vertical(y {PrevY}→{Y}, v {Velocity}, slide {Slide.Remaining})";

        public static bool operator ==(VerticalState left, VerticalState right) => left.Equals(right);

        public static bool operator !=(VerticalState left, VerticalState right) => !left.Equals(right);
    }

    /// <summary>自機の Tick ごとの出来事（音・演出用）。</summary>
    [Flags]
    public enum RunnerEvents
    {
        None = 0,
        Steered = 1,
        Jumped = 2,
        Slid = 4,
        Landed = 8,
    }

    /// <summary>
    /// 自機。横（<see cref="LaneState"/>）と縦（<see cref="VerticalState"/>）は独立に動き、それぞれに先行入力を 1 つ持つ。
    /// 先行入力は押した Tick を含めて <see cref="RunConfig.InputBufferTicks"/> Tick の間、効くまで毎 Tick 試す。
    /// </summary>
    public struct RunnerBody
    {
        private Steer _steerBuffer;
        private Countdown _steerWait;
        private Leap _leapBuffer;
        private Countdown _leapWait;

        public RunnerBody(LaneState lane, VerticalState vertical)
        {
            Lane = lane;
            Vertical = vertical;
            _steerBuffer = Steer.None;
            _steerWait = default;
            _leapBuffer = Leap.None;
            _leapWait = default;
        }

        public LaneState Lane { get; private set; }

        public VerticalState Vertical { get; private set; }

        public Steer SteerBuffer => _steerWait.IsActive ? _steerBuffer : Steer.None;

        public Leap LeapBuffer => _leapWait.IsActive ? _leapBuffer : Leap.None;

        public static RunnerBody Rest(RunConfig config, int lane) => new(LaneState.Rest(config, lane), VerticalState.Rest);

        /// <summary>止まって接地し、スライドも先行入力もない（チャンクの境目で求める状態）。</summary>
        public bool IsAtRest(RunConfig config) =>
            Lane == LaneState.Rest(config, Lane.To) && Vertical == VerticalState.Rest && SteerBuffer == Steer.None && LeapBuffer == Leap.None;

        public RunnerEvents Step(Command command, RunConfig config)
        {
            var events = RunnerEvents.None;
            _steerWait.Tick();
            if (command.Steer != Steer.None)
            {
                _steerBuffer = command.Steer;
                _steerWait.Arm(config.InputBufferTicks);
            }

            var steer = SteerBuffer;
            Lane = Lane.Next(steer, config, out var steered);
            if (steered != Outcome.Deferred) { _steerWait.Clear(); }
            if (steer != Steer.None && steered == Outcome.Applied) { events |= RunnerEvents.Steered; }

            _leapWait.Tick();
            if (command.Leap != Leap.None)
            {
                _leapBuffer = command.Leap;
                _leapWait.Arm(config.InputBufferTicks);
            }

            var leap = LeapBuffer;
            var wasGrounded = Vertical.IsGrounded;
            Vertical = Vertical.Next(leap, config, out var leapt);
            if (leapt != Outcome.Deferred) { _leapWait.Clear(); }
            if (leap == Leap.Jump && leapt == Outcome.Applied) { events |= RunnerEvents.Jumped; }
            if (leap == Leap.Slide && leapt == Outcome.Applied) { events |= RunnerEvents.Slid; }
            if (!wasGrounded && Vertical.IsGrounded) { events |= RunnerEvents.Landed; }
            return events;
        }

        /// <summary>状態のハッシュに混ぜる。</summary>
        public void MixInto(ref StateHash hash)
        {
            hash.Mix(Lane.From);
            hash.Mix(Lane.To);
            hash.Mix(Lane.Progress);
            hash.Mix(Lane.X);
            hash.Mix(Lane.PrevX);
            hash.Mix(Vertical.Y);
            hash.Mix(Vertical.Velocity);
            hash.Mix(Vertical.PrevY);
            hash.Mix(Vertical.Slide.Remaining);
            hash.Mix((int)_steerBuffer);
            hash.Mix(_steerWait.Remaining);
            hash.Mix((int)_leapBuffer);
            hash.Mix(_leapWait.Remaining);
        }
    }

    /// <summary>自機の当たり箱と、1 Tick の軌跡。</summary>
    public static class RunnerShape
    {
        /// <summary>中心の横位置 <paramref name="x"/>・足元 <paramref name="y"/>・中心の奥行き <paramref name="z"/> の当たり箱。</summary>
        public static Box3 At(RunConfig config, int x, int y, int z, int height) =>
            new(x - (config.RunnerWidth / 2), y, z - (config.RunnerDepth / 2), config.RunnerWidth, height, config.RunnerDepth);

        /// <summary>この Tick の始めの箱（高さは Tick の終わりのもの）。</summary>
        public static Box3 From(RunConfig config, LaneState lane, VerticalState vertical, int z0) =>
            At(config, lane.PrevX, vertical.PrevY, z0, vertical.Height(config));

        public static Box3 To(RunConfig config, LaneState lane, VerticalState vertical, int z1) =>
            At(config, lane.X, vertical.Y, z1, vertical.Height(config));
    }
}
