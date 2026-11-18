using System;
using Game.Core;

namespace Game.App
{
    public enum AppScreen
    {
        Title,
        Playing,
        Paused,
        Result,
    }

    /// <summary>画面遷移、保留入力、実時間から固定 Tick への変換を受け持つ。</summary>
    public sealed class MazeFlow
    {
        public const int MaxTicksPerFrame = 5;

        private readonly MazeGameConfig _config;
        private readonly Func<int> _nextSeed;
        private double _elapsedTicks;
        private double _accountedTicks;
        private double _timeError;
        private bool _hasFocus = true;
        private GameEvents _events;

        public MazeFlow(MazeGameConfig config, Func<int> nextSeed)
        {
            if (config == null) { throw new ArgumentNullException(nameof(config)); }
            config.Validate();
            _config = config.Copy();
            _nextSeed = nextSeed ?? throw new ArgumentNullException(nameof(nextSeed));
        }

        public AppScreen Screen { get; private set; }
        public MazeGameState? Game { get; private set; }
        public Direction PendingDirection { get; private set; } = Direction.None;

        /// <summary>タイトルと結果画面から新しい試合を始める。再戦のたびに新しいシードを使う。</summary>
        public void Submit()
        {
            if (!_hasFocus || (Screen != AppScreen.Title && Screen != AppScreen.Result)) { return; }
            Game = new MazeGameState(_config, _nextSeed());
            Screen = AppScreen.Playing;
            _events = GameEvents.None;
            ResetInput();
        }

        /// <summary>同じ Tick に複数の方向が来たら、上→左→下→右の順で1つを選ぶ。</summary>
        public void RequestDirection(Direction direction)
        {
            if (direction != Direction.None && !direction.IsMove()) { throw new ArgumentOutOfRangeException(nameof(direction)); }
            if (!_hasFocus || Screen != AppScreen.Playing || direction == Direction.None) { return; }
            if (PendingDirection == Direction.None || direction < PendingDirection) { PendingDirection = direction; }
        }

        public void TogglePause()
        {
            if (!_hasFocus) { return; }
            if (Screen == AppScreen.Paused) { Screen = AppScreen.Playing; }
            else if (Screen == AppScreen.Playing) { Screen = AppScreen.Paused; }
            ResetInput();
        }

        /// <summary>フォーカスを失うと停止する。復帰しても自動では再開しない。</summary>
        public void SetFocus(bool focus)
        {
            if (!focus && Screen == AppScreen.Playing) { Screen = AppScreen.Paused; }
            _hasFocus = focus;
            if (!focus) { ResetInput(); }
        }

        public void Back()
        {
            Game = null;
            Screen = AppScreen.Title;
            _events = GameEvents.None;
            ResetInput();
        }

        public void Advance(double seconds)
        {
            if (Screen != AppScreen.Playing || !double.IsFinite(seconds) || seconds <= 0) { return; }
            var elapsed = seconds * MazeGameConfig.TickRate;
            if (!double.IsFinite(elapsed)) { elapsed = MaxTicksPerFrame; }

            // 小さい時間の累積で Tick がずれないよう、補償加算で端数を保つ。
            var whole = Math.Floor(elapsed);
            var increment = Math.Min(whole, MaxTicksPerFrame) + (elapsed - whole) - _timeError;
            var total = _elapsedTicks + increment;
            _timeError = (total - _elapsedTicks) - increment;
            _elapsedTicks = total;
            var accounted = Math.Floor(total);
            var count = (int)Math.Min(MaxTicksPerFrame, accounted - _accountedTicks);
            _accountedTicks = accounted;
            for (var i = 0; i < count && Screen == AppScreen.Playing; i++) { StepOneTick(); }
        }

        public void StepOneTick()
        {
            if (!_hasFocus || Screen != AppScreen.Playing) { return; }
            Game!.Step(PendingDirection);
            PendingDirection = Direction.None;
            _events |= Game.Events;
            if (Game.IsOver)
            {
                Screen = AppScreen.Result;
                ResetInput();
            }
        }

        /// <summary>前回の呼び出し以降に起きた出来事を取り出す。1フレームに複数 Tick 進んでも取りこぼさない。</summary>
        public GameEvents TakeEvents()
        {
            var events = _events;
            _events = GameEvents.None;
            return events;
        }

        private void ResetInput()
        {
            PendingDirection = Direction.None;
            _elapsedTicks = 0;
            _accountedTicks = 0;
            _timeError = 0;
        }
    }
}
