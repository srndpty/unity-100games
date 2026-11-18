using System;
using Game.Core;

namespace Game.App
{
    public enum AppScreen { Title, Playing, Paused, Result }
    public sealed class FroggerFlow
    {
        public const int MaxTicksPerFrame = 5;
        private readonly FroggerConfig _config;
        private double _elapsedTicks;
        private double _accountedTicks;
        private double _timeError;
        private bool _hasFocus = true;
        public FroggerFlow(FroggerConfig config)
        {
            if (config == null) { throw new ArgumentNullException(nameof(config)); }
            config.Validate(); _config = config.Copy();
        }
        public AppScreen Screen { get; private set; }
        public FroggerState? Game { get; private set; }
        public MoveDirection PendingMove { get; private set; }
        public void Submit()
        {
            if (!_hasFocus || (Screen != AppScreen.Title && Screen != AppScreen.Result)) { return; }
            Game = new FroggerState(_config); Screen = AppScreen.Playing; ResetInput();
        }
        public void RequestMove(MoveDirection direction)
        {
            if (direction < MoveDirection.None || direction > MoveDirection.Right) { throw new ArgumentOutOfRangeException(nameof(direction)); }
            if (_hasFocus && Screen == AppScreen.Playing && direction != MoveDirection.None &&
                (PendingMove == MoveDirection.None || direction < PendingMove)) { PendingMove = direction; }
        }
        public void TogglePause()
        {
            if (!_hasFocus) { return; }
            if (Screen == AppScreen.Paused) { Screen = AppScreen.Playing; }
            else if (Screen == AppScreen.Playing) { Screen = AppScreen.Paused; }
            ResetInput();
        }
        public void SetFocus(bool focus)
        {
            if (!focus && Screen == AppScreen.Playing) { Screen = AppScreen.Paused; }
            _hasFocus = focus; if (!focus) { ResetInput(); }
        }
        public void Back() { Game = null; Screen = AppScreen.Title; ResetInput(); }
        public void Advance(double seconds)
        {
            if (Screen != AppScreen.Playing || !double.IsFinite(seconds) || seconds <= 0) { return; }
            var elapsed = seconds * FroggerConfig.TickRate;
            if (!double.IsFinite(elapsed)) { elapsed = MaxTicksPerFrame; }
            var whole = Math.Floor(elapsed);
            var increment = Math.Min(whole, MaxTicksPerFrame) + (elapsed - whole) - _timeError;
            var total = _elapsedTicks + increment;
            _timeError = (total - _elapsedTicks) - increment; _elapsedTicks = total;
            var accounted = Math.Floor(total);
            var count = (int)Math.Min(MaxTicksPerFrame, accounted - _accountedTicks); _accountedTicks = accounted;
            for (var i = 0; i < count && Screen == AppScreen.Playing; i++) { StepOneTick(); }
        }
        public void StepOneTick()
        {
            if (!_hasFocus || Screen != AppScreen.Playing) { return; }
            Game!.Step(PendingMove); PendingMove = MoveDirection.None;
            if (Game.Result != RoundResult.Playing) { Screen = AppScreen.Result; ResetInput(); }
        }
        private void ResetInput() { PendingMove = MoveDirection.None; _elapsedTicks = _accountedTicks = _timeError = 0; }
    }
}
