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

    public enum PlayerAction
    {
        Left,
        Right,
        SoftDrop,
        RotateClockwise,
        RotateCounterClockwise,
        HardDrop,
        Hold,
    }

    /// <summary>画面遷移と、プレイヤーの操作を Core へ渡す窓口。実時間を固定 Tick へ変換する。</summary>
    public sealed class StrataFlow
    {
        public const int MaxTicksPerFrame = 5;

        private readonly StrataConfig _config;
        private readonly ShiftRepeater _repeater;
        private int _nextSeed;
        private double _elapsedTicks;
        private double _accountedTicks;
        private double _timeError;
        private bool _hasFocus = true;
        private GameEvents _events;

        public StrataFlow(StrataConfig config, int repeatDelayTicks, int repeatIntervalTicks, int seed)
        {
            if (config == null) { throw new ArgumentNullException(nameof(config)); }
            config.Validate();
            _config = config.Copy();
            _repeater = new ShiftRepeater(repeatDelayTicks, repeatIntervalTicks);
            _nextSeed = seed;
        }

        public AppScreen Screen { get; private set; }
        public StrataGame? Game { get; private set; }

        /// <summary>遊んでいる試合のシード。</summary>
        public int GameSeed { get; private set; }

        /// <summary>起動してからの最高スコア。</summary>
        public long BestScore { get; private set; }

        /// <summary>直前の試合で最高スコアを更新したか。</summary>
        public bool IsNewBest { get; private set; }

        /// <summary>決定。タイトルと結果画面では試合を始め、一時停止中は再開する。</summary>
        public void Submit()
        {
            if (!_hasFocus) { return; }
            switch (Screen)
            {
                case AppScreen.Title:
                case AppScreen.Result:
                    Begin();
                    break;
                case AppScreen.Paused:
                    Resume();
                    break;
            }
        }

        public void TogglePause()
        {
            if (!_hasFocus) { return; }
            if (Screen == AppScreen.Playing) { Pause(); }
            else if (Screen == AppScreen.Paused) { Resume(); }
        }

        /// <summary>新しいシードで試合をやり直す。一時停止中と結果画面でだけ受け付ける。</summary>
        public void Restart()
        {
            if (_hasFocus && (Screen == AppScreen.Paused || Screen == AppScreen.Result)) { Begin(); }
        }

        /// <summary>タイトルへ戻る。一時停止中と結果画面でだけ受け付ける。</summary>
        public void BackToTitle()
        {
            if (!_hasFocus || (Screen != AppScreen.Paused && Screen != AppScreen.Result)) { return; }
            Game = null;
            Screen = AppScreen.Title;
            ResetInput();
        }

        /// <summary>キーの押下。プレイ中だけ受け付ける。</summary>
        public void Press(PlayerAction action)
        {
            if (!_hasFocus || Screen != AppScreen.Playing) { return; }
            switch (action)
            {
                case PlayerAction.Left:
                    _repeater.Press(ShiftDirection.Left);
                    Perform(GameCommand.MoveLeft);
                    break;
                case PlayerAction.Right:
                    _repeater.Press(ShiftDirection.Right);
                    Perform(GameCommand.MoveRight);
                    break;
                case PlayerAction.SoftDrop:
                    Perform(GameCommand.SoftDropOn);
                    break;
                case PlayerAction.RotateClockwise:
                    Perform(GameCommand.RotateClockwise);
                    break;
                case PlayerAction.RotateCounterClockwise:
                    Perform(GameCommand.RotateCounterClockwise);
                    break;
                case PlayerAction.HardDrop:
                    Perform(GameCommand.HardDrop);
                    break;
                case PlayerAction.Hold:
                    Perform(GameCommand.Hold);
                    break;
            }
        }

        /// <summary>キーを離す。</summary>
        public void Release(PlayerAction action)
        {
            switch (action)
            {
                case PlayerAction.Left:
                    _repeater.Release(ShiftDirection.Left);
                    break;
                case PlayerAction.Right:
                    _repeater.Release(ShiftDirection.Right);
                    break;
                case PlayerAction.SoftDrop:
                    Perform(GameCommand.SoftDropOff);
                    break;
            }
        }

        /// <summary>Core へ操作を渡す。プレイ中でなければ受け付けない。</summary>
        public bool Perform(GameCommand command)
        {
            if (!_hasFocus || Screen != AppScreen.Playing) { return false; }
            var accepted = Game!.Apply(command);
            Collect();
            return accepted;
        }

        /// <summary>フォーカスを失うと一時停止する。戻っても自動では再開しない。</summary>
        public void SetFocus(bool focus)
        {
            _hasFocus = focus;
            if (focus) { return; }
            if (Screen == AppScreen.Playing) { Pause(); }
            ResetInput();
        }

        /// <summary>実時間を進める。処理落ちしても1回で進める Tick 数には上限がある。</summary>
        public void Advance(double seconds)
        {
            if (Screen != AppScreen.Playing || !double.IsFinite(seconds) || seconds <= 0) { return; }

            // 小さい時間の加算でも Tick を取りこぼさないよう、累積の丸め誤差を補償する。
            var elapsed = seconds * StrataConfig.TickRate;
            if (!double.IsFinite(elapsed)) { elapsed = MaxTicksPerFrame; }
            var whole = Math.Floor(elapsed);
            var increment = Math.Min(whole, MaxTicksPerFrame) + (elapsed - whole) - _timeError;
            var total = _elapsedTicks + increment;
            _timeError = (total - _elapsedTicks) - increment;
            _elapsedTicks = total;
            var accounted = Math.Floor(total);
            var count = (int)Math.Min(MaxTicksPerFrame, accounted - _accountedTicks);
            _accountedTicks = accounted;
            for (var i = 0; i < count && Screen == AppScreen.Playing; i++) { StepTick(); }
        }

        /// <summary>1 Tick 進める。プレイ中でなければ何もしない。</summary>
        public void StepTick()
        {
            if (Screen != AppScreen.Playing) { return; }
            var shift = _repeater.Tick();
            if (shift != null) { Perform(shift == ShiftDirection.Left ? GameCommand.MoveLeft : GameCommand.MoveRight); }
            if (Screen != AppScreen.Playing) { return; }
            Game!.Tick();
            Collect();
        }

        /// <summary>前回の呼び出し以降に起きた出来事を取り出す。</summary>
        public GameEvents TakeEvents()
        {
            var events = _events;
            _events = GameEvents.None;
            return events;
        }

        private void Begin()
        {
            GameSeed = _nextSeed;
            _nextSeed = unchecked(_nextSeed + 1);
            Game = new StrataGame(_config, GameSeed);
            IsNewBest = false;
            Screen = AppScreen.Playing;
            ResetInput();
            _events = GameEvents.None;
            Collect();
        }

        private void Pause()
        {
            Screen = AppScreen.Paused;
            ResetInput();
        }

        private void Resume()
        {
            Screen = AppScreen.Playing;
            ResetInput();
        }

        // 押しっぱなしのキーを次の画面へ持ち越さない。
        private void ResetInput()
        {
            _repeater.Clear();
            Game?.Apply(GameCommand.SoftDropOff);
            _elapsedTicks = 0;
            _accountedTicks = 0;
            _timeError = 0;
        }

        private void Collect()
        {
            _events |= Game!.TakeEvents();
            if (Game.Phase != GamePhase.GameOver || Screen != AppScreen.Playing) { return; }
            Screen = AppScreen.Result;
            IsNewBest = Game.Score > BestScore;
            if (IsNewBest) { BestScore = Game.Score; }
            ResetInput();
        }
    }
}
