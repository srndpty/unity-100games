using System;
using System.Collections.Generic;
using Game.Core;

namespace Game.App
{
    public enum AppScreen { Title, Playing, Paused, Result }

    public sealed class SnakeFlow
    {
        public const int MaxTicksPerFrame = 5;
        private readonly SnakeConfig _config;
        private readonly Queue<Direction> _turns = new();
        private double _fractionalTicks;
        private int _nextSeed;

        public SnakeFlow(SnakeConfig config, int seed)
        {
            if (config == null) { throw new ArgumentNullException(nameof(config)); }
            config.Validate();
            _config = config.Copy();
            _nextSeed = seed;
        }

        public AppScreen Screen { get; private set; }
        public SnakeState? Game { get; private set; }
        public int GameSeed { get; private set; }
        public int PendingTurns => _turns.Count;
        public double TickDuration => 1d / _config.StepsPerSecond;

        public void Submit()
        {
            if (Screen != AppScreen.Title && Screen != AppScreen.Result) { return; }
            GameSeed = _nextSeed;
            _nextSeed = unchecked(_nextSeed + 1);
            Game = new SnakeState(_config, GameSeed);
            ResetPending();
            Screen = AppScreen.Playing;
        }

        public bool RequestDirection(Direction direction)
        {
            if (Screen != AppScreen.Playing || !SnakeState.IsDirection(direction) || _turns.Count == 2) { return false; }
            var previous = Game!.Direction;
            foreach (var turn in _turns) { previous = turn; }
            if (previous == direction || SnakeState.IsOpposite(previous, direction)) { return false; }
            _turns.Enqueue(direction);
            return true;
        }

        public void TogglePause()
        {
            if (Screen != AppScreen.Playing && Screen != AppScreen.Paused) { return; }
            Screen = Screen == AppScreen.Playing ? AppScreen.Paused : AppScreen.Playing;
            ResetPending();
        }

        public void Back()
        {
            Game = null;
            Screen = AppScreen.Title;
            ResetPending();
        }

        public void Advance(double deltaSeconds)
        {
            if (Screen != AppScreen.Playing || !double.IsFinite(deltaSeconds) || deltaSeconds <= 0d) { return; }
            // 商と剰余を別々に求めると、0.1秒などの丸めで1 Tickを二重に数えうる。
            // Tick単位に変換してから整数部と端数を同じ値から分ける。
            var elapsedTicks = deltaSeconds * _config.StepsPerSecond;
            if (!double.IsFinite(elapsedTicks)) { elapsedTicks = MaxTicksPerFrame; }
            var wholeTicks = Math.Floor(elapsedTicks);
            var fraction = elapsedTicks - wholeTicks + _fractionalTicks;
            var extraTicks = Math.Floor(fraction);
            _fractionalTicks = fraction - extraTicks;
            var count = (int)Math.Min(MaxTicksPerFrame, wholeTicks + extraTicks);
            for (var i = 0; i < count && Screen == AppScreen.Playing; i++) { StepOneTick(); }
        }

        public void StepOneTick()
        {
            if (Screen != AppScreen.Playing) { return; }
            Game!.Step(_turns.Count == 0 ? Game.Direction : _turns.Dequeue());
            if (Game.Phase != GamePhase.Playing)
            {
                Screen = AppScreen.Result;
                ResetPending();
            }
        }

        private void ResetPending() { _turns.Clear(); _fractionalTicks = 0d; }
    }

    /// <summary>偶数列の盤面の巡回路。初期胴体を避けて巡回路へ合流する。</summary>
    public static class Autopilot
    {
        public static Direction Next(SnakeState game)
        {
            if (game == null) { throw new ArgumentNullException(nameof(game)); }
            if (game.Width % 2 != 0) { throw new ArgumentException("操縦役は偶数列の盤面を使います。", nameof(game)); }
            var head = game.Head;
            // 初回は右端へ進み、その列を下って巡回路に入る。
            if (game.TickCount < game.Width / 2 - 1) { return Direction.Right; }
            if (head.Y == 0) { return head.X == 0 ? Direction.Up : Direction.Left; }
            if (head.X % 2 == 0) { return head.Y == game.Height - 1 ? Direction.Right : Direction.Up; }
            return head.Y == 1 && head.X != game.Width - 1 ? Direction.Right : Direction.Down;
        }
    }
}
