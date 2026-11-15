using System;
using Game.Core;

namespace Game.App
{
    public enum AppScreen { Title, Ready, Playing, Paused, Result }
    public sealed class FlappyFlow
    {
        public const int MaxTicksPerFrame = 5;
        private readonly FlappyConfig _config;
        private int _nextSeed;
        private double _elapsedTicks;
        private double _accountedTicks;
        private double _timeError;
        private bool _hasFocus = true;
        private bool _flap;
        private AppScreen _resumeScreen;
        public FlappyFlow(FlappyConfig config, int seed)
        {
            if (config == null) { throw new ArgumentNullException(nameof(config)); }
            config.Validate(); _config = config.Copy(); _nextSeed = seed;
        }
        public AppScreen Screen { get; private set; }
        public FlappyState? Game { get; private set; }
        public int GameSeed { get; private set; }
        public bool PendingFlap => _flap;
        public void Submit()
        {
            if (!_hasFocus || (Screen != AppScreen.Title && Screen != AppScreen.Result)) { return; }
            GameSeed = _nextSeed; _nextSeed = unchecked(_nextSeed + 1);
            Game = new FlappyState(_config, GameSeed); Screen = AppScreen.Ready; ResetInput();
        }
        public void RequestFlap()
        {
            if (_hasFocus && (Screen == AppScreen.Ready || Screen == AppScreen.Playing)) { _flap = true; }
        }
        public void TogglePause()
        {
            if (!_hasFocus) { return; }
            if (Screen == AppScreen.Paused) { Screen = _resumeScreen; }
            else if (Screen == AppScreen.Playing || Screen == AppScreen.Ready) { _resumeScreen = Screen; Screen = AppScreen.Paused; }
            ResetInput();
        }
        public void SetFocus(bool focus)
        {
            if (!focus && _hasFocus && (Screen == AppScreen.Playing || Screen == AppScreen.Ready)) { TogglePause(); }
            _hasFocus = focus;
            if (!focus) { ResetInput(); }
        }
        public void Back() { Game = null; Screen = AppScreen.Title; ResetInput(); }
        public void Advance(double seconds)
        {
            if ((Screen != AppScreen.Playing && !(Screen == AppScreen.Ready && _flap)) || !double.IsFinite(seconds) || seconds <= 0) { return; }
            var elapsed = seconds * FlappyConfig.TickRate;
            if (!double.IsFinite(elapsed)) { elapsed = MaxTicksPerFrame; }
            var whole = Math.Floor(elapsed);
            var increment = Math.Min(whole, MaxTicksPerFrame) + (elapsed - whole) - _timeError;
            var total = _elapsedTicks + increment;
            _timeError = (total - _elapsedTicks) - increment; _elapsedTicks = total;
            var accounted = Math.Floor(total);
            var count = (int)Math.Min(MaxTicksPerFrame, accounted - _accountedTicks); _accountedTicks = accounted;
            for (var i = 0; i < count && (Screen == AppScreen.Playing || Screen == AppScreen.Ready); i++) { StepOneTick(); }
        }
        public void StepOneTick()
        {
            if (!_hasFocus || (Screen != AppScreen.Playing && !(Screen == AppScreen.Ready && _flap))) { return; }
            Screen = AppScreen.Playing; Game!.Step(new FlappyCommand(_flap)); _flap = false;
            if (Game.Lost) { Screen = AppScreen.Result; ResetInput(); }
        }
        private void ResetInput() { _flap = false; _elapsedTicks = 0; _accountedTicks = 0; _timeError = 0; }
    }
}
