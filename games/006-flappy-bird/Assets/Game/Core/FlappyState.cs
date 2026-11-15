using System;

namespace Game.Core
{
    public readonly struct FlappyCommand
    {
        public FlappyCommand(bool flap) { Flap = flap; }
        public bool Flap { get; }
    }
    public struct PipeState
    {
        public float X { get; internal set; }
        public float Center { get; internal set; }
        public float Gap { get; internal set; }
        public bool Scored { get; internal set; }
        public long Generation { get; internal set; }
    }
    public sealed class FlappyState
    {
        private readonly FlappyConfig _config;
        private readonly PipeState[] _pipes;
        private uint _random;
        private float _lastCenter;
        private long _generation;
        public FlappyState(FlappyConfig config, int seed)
        {
            if (config == null) { throw new ArgumentNullException(nameof(config)); }
            config.Validate(); _config = config.Copy(); _random = unchecked((uint)seed);
            if (_random == 0) { _random = 0x6d2b79f5; }
            _pipes = new PipeState[_config.Capacity];
            for (var i = 0; i < _pipes.Length; i++) { _pipes[i] = Generate(8 + i * _config.Spacing); }
        }
        public float BirdY { get; private set; }
        public float Velocity { get; private set; }
        public bool Lost { get; private set; }
        public long Score { get; private set; }
        public long TickCount { get; private set; }
        public int Level => (int)Math.Min(_config.MaxLevel, Score / _config.PointsPerLevel);
        public float Speed => Math.Min(7, (float)((double)_config.Speed + Level * (double)_config.SpeedIncrease));
        public int PipeCount => _pipes.Length;
        public PipeState PipeAt(int index) => _pipes[index];
        public void Step(FlappyCommand command)
        {
            if (Lost) { return; }
            var speed = Speed;
            Velocity = Math.Max(-_config.TerminalSpeed, (command.Flap ? _config.FlapSpeed : Velocity) + _config.Gravity / FlappyConfig.TickRate);
            var dy = Velocity / FlappyConfig.TickRate; var move = speed / FlappyConfig.TickRate;
            var collision = float.PositiveInfinity;
            var limit = FlappyConfig.Height / 2 - _config.BirdRadius;
            if (BirdY <= -limit || BirdY >= limit) { collision = 0; }
            else if (dy > 0 && BirdY + dy >= limit) { collision = (limit - BirdY) / dy; }
            else if (dy < 0 && BirdY + dy <= -limit) { collision = (-limit - BirdY) / dy; }
            for (var i = 0; i < _pipes.Length; i++)
            {
                var p = _pipes[i]; var left = p.X - _config.PipeWidth / 2; var right = p.X + _config.PipeWidth / 2;
                collision = Math.Min(collision, Sweep.CircleRectangle(FlappyConfig.BirdX, BirdY, move, dy, _config.BirdRadius, left, right, -FlappyConfig.Height / 2, p.Center - p.Gap / 2));
                collision = Math.Min(collision, Sweep.CircleRectangle(FlappyConfig.BirdX, BirdY, move, dy, _config.BirdRadius, left, right, p.Center + p.Gap / 2, FlappyConfig.Height / 2));
            }
            var fraction = Math.Min(1, collision);
            for (var i = 0; i < _pipes.Length; i++)
            {
                var p = _pipes[i];
                var pass = (p.X + _config.PipeWidth / 2 - (FlappyConfig.BirdX - _config.BirdRadius)) / move;
                if (!p.Scored && pass <= 1 && pass < collision)
                { p.Scored = true; if (Score < long.MaxValue) { Score++; } }
                p.X -= move * fraction; _pipes[i] = p;
            }
            BirdY += dy * fraction; TickCount++; Lost = collision <= 1;
            if (Lost) { return; }
            for (var i = 0; i < _pipes.Length; i++)
            {
                if (_pipes[i].X + _config.PipeWidth / 2 >= -FlappyConfig.Width / 2) { continue; }
                var rightmost = float.NegativeInfinity;
                for (var j = 0; j < _pipes.Length; j++) { rightmost = Math.Max(rightmost, _pipes[j].X); }
                _pipes[i] = Generate(rightmost + _config.Spacing);
            }
        }
        private PipeState Generate(float x)
        {
            var gap = Math.Max(3.5f, (float)((double)_config.Gap - Level * (double)_config.GapDecrease));
            var limit = FlappyConfig.Height / 2 - _config.WallMargin - gap / 2;
            var min = Math.Max(-limit, _lastCenter - _config.MaxCenterChange);
            var max = Math.Min(limit, _lastCenter + _config.MaxCenterChange);
            _random ^= _random << 13; _random ^= _random >> 17; _random ^= _random << 5;
            _lastCenter = min + (max - min) * ((_random >> 8) / 16777216f);
            return new PipeState { X = x, Center = _lastCenter, Gap = gap, Generation = _generation++ };
        }
    }
}
