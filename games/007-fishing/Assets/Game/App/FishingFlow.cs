using System;
using Game.Core;

namespace Game.App
{
    public enum AppScreen { Title, Select, Playing, Paused, Result, Complete }
    public sealed class FishingFlow
    {
        public const int MaxTicksPerFrame = 5;
        private readonly FishingConfig _config;
        private readonly int[] _catches = new int[3];
        private int _nextSeed;
        private bool _focus = true;
        private bool _hook;
        private bool _reel;
        private bool _blocked = true;
        private AppScreen _resume;
        private double _elapsed;
        private double _accounted;
        private double _error;
        public FishingFlow(FishingConfig config, int seed)
        {
            if (config == null) { throw new ArgumentNullException(nameof(config)); }
            config.Validate(); _config = config.Copy(); _nextSeed = seed;
        }
        public AppScreen Screen { get; private set; }
        public FishingState? Game { get; private set; }
        public FishKind Selected { get; private set; }
        public int Throws { get; private set; }
        public int SpeciesCount => (_catches[0] > 0 ? 1 : 0) + (_catches[1] > 0 ? 1 : 0) + (_catches[2] > 0 ? 1 : 0);
        public int CatchCount(FishKind kind)
        {
            if ((int)kind < 0 || (int)kind > 2) { throw new ArgumentOutOfRangeException(nameof(kind)); }
            return _catches[(int)kind];
        }
        public void Submit()
        {
            if (!_focus) { return; }
            if (Screen == AppScreen.Title || Screen == AppScreen.Complete)
            { Array.Clear(_catches, 0, 3); Throws = 0; Selected = FishKind.Crucian; Screen = AppScreen.Select; Game = null; ResetInput(); }
            else if (Screen == AppScreen.Result) { Screen = AppScreen.Select; Game = null; ResetInput(); }
        }
        public void Select(FishKind kind)
        {
            if (_focus && Screen == AppScreen.Select && (int)kind >= 0 && (int)kind <= 2) { Selected = kind; }
        }
        public void Shift(int direction) { Select((FishKind)(((int)Selected + Math.Sign(direction) + 3) % 3)); }
        public void Cast()
        {
            if (!_focus || Screen != AppScreen.Select) { return; }
            Game = new FishingState(_config, Selected, _nextSeed); _nextSeed = unchecked(_nextSeed + 1);
            Throws = Throws == int.MaxValue ? Throws : Throws + 1; Screen = AppScreen.Playing; ResetInput();
        }
        public void SetInput(bool pressed, bool held)
        {
            if (!_focus || Screen != AppScreen.Playing) { return; }
            if (_blocked) { if (!held && !pressed) { _blocked = false; } return; }
            _hook |= pressed; _reel = held;
        }
        public void TogglePause()
        {
            if (!_focus) { return; }
            if (Screen == AppScreen.Paused) { Screen = _resume; }
            else if (Screen == AppScreen.Playing || Screen == AppScreen.Select) { _resume = Screen; Screen = AppScreen.Paused; }
            ResetInput();
        }
        public void SetFocus(bool focus)
        {
            if (!focus && _focus && (Screen == AppScreen.Playing || Screen == AppScreen.Select)) { TogglePause(); }
            _focus = focus; if (!focus) { ResetInput(); }
        }
        public void Back()
        {
            Game = null; Screen = AppScreen.Title; Array.Clear(_catches, 0, 3); Throws = 0; ResetInput();
        }
        public void Advance(double seconds)
        {
            if (Screen != AppScreen.Playing || !_focus || !double.IsFinite(seconds) || seconds <= 0) { return; }
            var ticks = seconds * FishingConfig.TickRate;
            if (!double.IsFinite(ticks)) { ticks = MaxTicksPerFrame; }
            var whole = Math.Floor(ticks);
            var increment = Math.Min(whole, MaxTicksPerFrame) + (ticks - whole) - _error;
            var total = _elapsed + increment; _error = (total - _elapsed) - increment; _elapsed = total;
            var accounted = Math.Floor(total); var count = (int)Math.Min(MaxTicksPerFrame, accounted - _accounted); _accounted = accounted;
            for (var i = 0; i < count && Screen == AppScreen.Playing; i++) { StepOneTick(); }
        }
        public void StepOneTick()
        {
            if (!_focus || Screen != AppScreen.Playing) { return; }
            Game!.Step(new FishingCommand(_hook, _reel)); _hook = false;
            if (!Game.Finished) { return; }
            if (Game.Phase == FishingPhase.Caught)
            { var index = (int)Game.Kind; if (_catches[index] < int.MaxValue) { _catches[index]++; } }
            Screen = SpeciesCount == 3 ? AppScreen.Complete : AppScreen.Result; ResetInput();
        }
        private void ResetInput() { _hook = _reel = false; _blocked = true; _elapsed = _accounted = _error = 0; }
    }
}
