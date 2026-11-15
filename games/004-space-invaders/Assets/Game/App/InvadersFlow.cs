using System;
using Game.Core;

namespace Game.App
{
    public enum AppScreen { Title, Playing, Paused, Result }

    public sealed class InvadersFlow
    {
        public const int MaxTicksPerFrame = 5;
        private readonly InvadersConfig _config;
        private int _nextSeed;
        private double _fractionalTicks;
        private bool _hasFocus = true;
        public InvadersFlow(InvadersConfig config, int seed)
        {
            if (config == null) { throw new ArgumentNullException(nameof(config)); }
            config.Validate(); _config = config.Copy(); _nextSeed = seed;
        }
        public AppScreen Screen { get; private set; }
        public InvadersState? Game { get; private set; }
        public int GameSeed { get; private set; }
        public PlayerCommand Input { get; private set; }
        public void Submit()
        {
            if (!_hasFocus || (Screen != AppScreen.Title && Screen != AppScreen.Result)) { return; }
            GameSeed = _nextSeed; _nextSeed = unchecked(_nextSeed + 1);
            Game = new InvadersState(_config, GameSeed); Screen = AppScreen.Playing; ResetInput();
        }
        public void SetInput(bool left, bool right, bool fire)
        {
            Input = Screen == AppScreen.Playing && _hasFocus ? new PlayerCommand((right ? 1 : 0) - (left ? 1 : 0), fire) : default;
        }
        public void TogglePause()
        {
            if (!_hasFocus || (Screen != AppScreen.Playing && Screen != AppScreen.Paused)) { return; }
            Screen = Screen == AppScreen.Playing ? AppScreen.Paused : AppScreen.Playing; ResetInput();
        }
        public void SetFocus(bool focus)
        {
            _hasFocus = focus;
            if (!focus)
            {
                if (Screen == AppScreen.Playing) { Screen = AppScreen.Paused; }
                ResetInput();
            }
        }
        public void Back() { Game = null; Screen = AppScreen.Title; ResetInput(); }
        public void Advance(double seconds)
        {
            if (Screen != AppScreen.Playing || !double.IsFinite(seconds) || seconds <= 0) { return; }
            var elapsed = seconds * InvadersConfig.TickRate;
            if (!double.IsFinite(elapsed)) { elapsed = MaxTicksPerFrame; }
            var whole = Math.Floor(elapsed);
            var fraction = elapsed - whole + _fractionalTicks;
            var extra = Math.Floor(fraction); _fractionalTicks = fraction - extra;
            var count = (int)Math.Min(MaxTicksPerFrame, whole + extra);
            for (var i = 0; i < count && Screen == AppScreen.Playing; i++) { StepOneTick(); }
        }
        public void StepOneTick()
        {
            if (Screen != AppScreen.Playing) { return; }
            Game!.Step(Input);
            if (Game.Phase == GamePhase.Won || Game.Phase == GamePhase.Lost) { Screen = AppScreen.Result; ResetInput(); }
        }
        private void ResetInput() { Input = default; _fractionalTicks = 0; }
    }
}
