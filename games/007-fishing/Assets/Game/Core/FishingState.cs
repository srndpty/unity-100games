using System;

namespace Game.Core
{
    public enum FishingPhase { Casting, Roaming, Approaching, Biting, Fighting, Landing, Caught, Escaped }
    public enum FishAction { Run, Rest, Turn }
    public enum EscapeReason { None, EarlyHook, MissedBite, BrokenLine, LooseHook, TooFar }
    public readonly struct FishingCommand
    {
        public FishingCommand(bool hook, bool reel) { Hook = hook; Reel = reel; }
        public bool Hook { get; }
        public bool Reel { get; }
    }
    public sealed class FishingState
    {
        private readonly FishingConfig _config;
        private readonly FishProfile _fish;
        private uint _random;
        private int _remaining;
        private int _actionRemaining;
        private readonly int _waitTicks;
        public FishingState(FishingConfig config, FishKind kind, int seed)
        {
            if (config == null) { throw new ArgumentNullException(nameof(config)); }
            config.Validate(); _config = config.Copy();
            _fish = Array.Find(_config.Fish, fish => fish.Kind == kind) ?? throw new ArgumentOutOfRangeException(nameof(kind));
            Kind = kind; Seed = seed; _random = seed == 0 ? 0x9E3779B9u : unchecked((uint)seed);
            _waitTicks = RandomTicks(config.WaitMin, config.WaitMax);
            Distance = _fish.Distance; Stamina = _fish.Stamina; Tension = config.InitialTension;
            _remaining = FishingConfig.Ticks(config.CastSeconds);
        }
        public FishKind Kind { get; }
        public int Seed { get; }
        public long TickCount { get; private set; }
        public int PhaseTick { get; private set; }
        public FishingPhase Phase { get; private set; }
        public FishAction Action { get; private set; }
        public EscapeReason Reason { get; private set; }
        public double Distance { get; private set; }
        public double Tension { get; private set; }
        public double Stamina { get; private set; }
        public int HighTicks { get; private set; }
        public int LowTicks { get; private set; }
        public int RemainingTicks => _remaining;
        public bool Finished => Phase == FishingPhase.Caught || Phase == FishingPhase.Escaped;
        public void Step(FishingCommand command)
        {
            if (Finished) { return; }
            var phase = Phase;
            TickCount++;
            PhaseTick++;
            AdvancePhase(command);
            if (Phase != phase) { PhaseTick = 0; }
        }
        private void AdvancePhase(FishingCommand command)
        {
            if (Phase == FishingPhase.Fighting) { Fight(command.Reel); return; }
            if (Phase == FishingPhase.Landing)
            { if (--_remaining <= 0) { Phase = FishingPhase.Caught; } return; }
            if (Phase == FishingPhase.Biting)
            {
                if (command.Hook) { Phase = FishingPhase.Fighting; Action = FishAction.Run; _actionRemaining = RandomTicks(_fish.RunMin, _fish.RunMax); return; }
                if (--_remaining <= 0) { Escape(EscapeReason.MissedBite); }
                return;
            }
            if (command.Hook) { Escape(EscapeReason.EarlyHook); return; }
            if (--_remaining > 0) { return; }
            switch (Phase)
            {
                case FishingPhase.Casting:
                    Phase = FishingPhase.Roaming; _remaining = _waitTicks - FishingConfig.Ticks(_config.ApproachSeconds);
                    if (_remaining == 0) { Phase = FishingPhase.Approaching; _remaining = FishingConfig.Ticks(_config.ApproachSeconds); }
                    break;
                case FishingPhase.Roaming: Phase = FishingPhase.Approaching; _remaining = FishingConfig.Ticks(_config.ApproachSeconds); break;
                case FishingPhase.Approaching: Phase = FishingPhase.Biting; _remaining = FishingConfig.Ticks(_config.BiteSeconds); break;
            }
        }
        private void Fight(bool reel)
        {
            if (_actionRemaining == 0)
            {
                Action = (FishAction)(((int)Action + 1) % 3);
                _actionRemaining = Action == FishAction.Run ? RandomTicks(_fish.RunMin, _fish.RunMax)
                    : Action == FishAction.Rest ? RandomTicks(_fish.RestMin, _fish.RestMax) : RandomTicks(_config.TurnMin, _config.TurnMax);
            }
            _actionRemaining--;
            var pull = Stamina <= 0 ? 0 : Action == FishAction.Run ? _fish.Pull : Action == FishAction.Rest ? 0 : .3;
            var oldTension = Tension;
            Tension = Math.Clamp(Tension + ((_config.ReelTension * (reel ? 1 : 0)) + _config.PullTension * pull - _config.ReleaseTension) / FishingConfig.TickRate, 0, 100);
            Distance = Math.Max(0, Distance + (_config.FishSpeed * pull - _config.ReelSpeed * (reel ? 1 : 0)) / FishingConfig.TickRate);
            if (oldTension >= 20 && oldTension < 80) { Stamina = Math.Max(0, Stamina - (_config.Fatigue + (reel ? _config.ReelFatigue : 0)) / (double)FishingConfig.TickRate); }
            HighTicks = Tension >= 80 ? HighTicks + 1 : 0; LowTicks = Tension <= 10 ? LowTicks + 1 : 0;
            if (HighTicks >= FishingConfig.Ticks(_config.BreakSeconds)) { Escape(EscapeReason.BrokenLine); }
            else if (LowTicks >= FishingConfig.Ticks(_config.SlackSeconds)) { Escape(EscapeReason.LooseHook); }
            else if (Distance >= 30) { Escape(EscapeReason.TooFar); }
            else if (Distance <= 1 && Stamina <= 0) { Phase = FishingPhase.Landing; _remaining = FishingConfig.Ticks(_config.LandingSeconds); }
        }
        private void Escape(EscapeReason reason) { Phase = FishingPhase.Escaped; Reason = reason; }
        private int RandomTicks(float min, float max)
        {
            _random ^= _random << 13; _random ^= _random >> 17; _random ^= _random << 5;
            var low = FishingConfig.Ticks(min); var high = FishingConfig.Ticks(max);
            return low + (int)(_random % (uint)(high - low + 1));
        }
    }
}
