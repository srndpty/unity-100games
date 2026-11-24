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

    /// <summary>キーの代わりに、Tick ごとの操作を決める操縦役。</summary>
    public interface IRacePilot
    {
        /// <summary>レースを始める前に呼ぶ。前のレースの判断は車の状態から作り直す。</summary>
        void Reset();

        Drive DriveFor(Race race);
    }

    /// <summary>画面に反映する出来事。</summary>
    [Flags]
    public enum FlowEvents
    {
        None = 0,
        Started = 1,
        Paused = 1 << 1,
        Title = 1 << 2,
        Result = 1 << 3,
    }

    /// <summary>
    /// タイトル、スタート待ち、レース、一時停止、結果。入力は時刻付きの 1 本の列で、
    /// 1 フレームでは時計と入力を合わせて <see cref="MaxTicksPerFrame"/> Tick まで進める。
    /// </summary>
    public sealed class RaceFlow
    {
        public const int MaxTicksPerFrame = 5;

        public const int TickRate = 60;

        private readonly KartConfig _config;
        private readonly Track _track;
        private bool _hasFocus = true;
        private int _left;
        private int _right;
        private int _accel;
        private int _brake;
        private int _drift;
        private double _origin;
        private long _ticksDone;
        private int _frameBudget = MaxTicksPerFrame;
        private FlowEvents _events;
        private RaceMoment _moments;

        public RaceFlow(KartConfig config, Track track)
        {
            if (config == null) { throw new ArgumentNullException(nameof(config)); }
            _track = track ?? throw new ArgumentNullException(nameof(track));
            _config = config.Validate();
            Race = new Race(_config, _track);
        }

        public AppScreen Screen { get; private set; } = AppScreen.Title;

        public Race Race { get; private set; }

        public KartConfig Config => _config.Copy();

        public IRacePilot? Pilot { get; set; }

        /// <summary>結果で 1 位なら勝利。</summary>
        public bool Won => Screen == AppScreen.Result && Race.PlaceOf(0) == 1;

        public Drive Held => DriveFromKeys();

        public void Apply(Intent intent)
        {
            if (!_hasFocus) { return; }
            AdvanceTo(intent.Time);
            switch (Screen)
            {
                case AppScreen.Title:
                    if (intent.Kind == IntentKind.Submit) { Begin(intent.Time); }
                    break;
                case AppScreen.Playing:
                    ApplyPlaying(intent);
                    break;
                case AppScreen.Paused:
                    if (intent.Kind == IntentKind.Cancel || intent.Kind == IntentKind.Submit) { Resume(intent.Time); }
                    else if (intent.Kind == IntentKind.Retry) { Begin(intent.Time); }
                    else if (intent.Kind == IntentKind.Back) { ToTitle(); }
                    break;
                case AppScreen.Result:
                    if (intent.Kind == IntentKind.Submit) { Begin(intent.Time); }
                    else if (intent.Kind == IntentKind.Cancel) { ToTitle(); }
                    break;
                default:
                    throw new InvalidOperationException($"未定義の画面です：{Screen}");
            }
        }

        /// <summary>フォーカスを失うとキーを離し、ドリフトの溜めをブーストにせず消し、プレイ中は一時停止する。復帰は手動。</summary>
        public void SetFocus(bool focus)
        {
            _hasFocus = focus;
            if (focus) { return; }
            if (Screen == AppScreen.Playing)
            {
                Race.CancelDrift();
                Pause();
            }
            else
            {
                ReleaseAll();
            }
        }

        public void Advance(double now)
        {
            if (_hasFocus) { AdvanceTo(now); }
            EndFrame();
        }

        public void EndFrame() => _frameBudget = MaxTicksPerFrame;

        /// <summary>時刻と無関係に 1 Tick 進める（撮影・テスト用）。</summary>
        public void StepTick()
        {
            if (Screen != AppScreen.Playing) { return; }
            Step();
        }

        public FlowEvents TakeEvents()
        {
            var events = _events;
            _events = FlowEvents.None;
            return events;
        }

        public RaceMoment TakeMoments()
        {
            var moments = _moments;
            _moments = RaceMoment.None;
            return moments;
        }

        private void ApplyPlaying(Intent intent)
        {
            switch (intent.Kind)
            {
                case IntentKind.SteerLeftDown:
                    _left |= intent.KeyBit;
                    break;
                case IntentKind.SteerLeftUp:
                    _left &= ~intent.KeyBit;
                    break;
                case IntentKind.SteerRightDown:
                    _right |= intent.KeyBit;
                    break;
                case IntentKind.SteerRightUp:
                    _right &= ~intent.KeyBit;
                    break;
                case IntentKind.AccelDown:
                    _accel |= intent.KeyBit;
                    break;
                case IntentKind.AccelUp:
                    _accel &= ~intent.KeyBit;
                    break;
                case IntentKind.BrakeDown:
                    _brake |= intent.KeyBit;
                    break;
                case IntentKind.BrakeUp:
                    _brake &= ~intent.KeyBit;
                    break;
                case IntentKind.DriftDown:
                    _drift |= intent.KeyBit;
                    break;
                case IntentKind.DriftUp:
                    _drift &= ~intent.KeyBit;
                    break;
                case IntentKind.Cancel:
                    Race.CancelDrift();
                    Pause();
                    break;
            }
        }

        private Drive DriveFromKeys()
        {
            var steer = 0;
            if (_left != 0 && _right == 0) { steer = 1; }
            else if (_right != 0 && _left == 0) { steer = -1; }
            return new Drive(_accel != 0, _brake != 0, steer, _drift != 0);
        }

        private void Step()
        {
            var drive = Pilot != null ? Pilot.DriveFor(Race) : DriveFromKeys();
            _moments |= Race.Step(drive);
            if (!Race.IsOver) { return; }
            ReleaseAll();
            Screen = AppScreen.Result;
            _events |= FlowEvents.Result;
        }

        private void AdvanceTo(double time)
        {
            if (Screen != AppScreen.Playing || !double.IsFinite(time)) { return; }
            var elapsed = Math.Floor((time - _origin) * TickRate);
            if (elapsed <= _ticksDone) { return; }

            var target = elapsed > long.MaxValue / 2 ? long.MaxValue / 2 : (long)elapsed;
            if (target - _ticksDone > _frameBudget) { _ticksDone = target - _frameBudget; }
            while (_ticksDone < target && Screen == AppScreen.Playing)
            {
                _ticksDone++;
                _frameBudget--;
                Step();
            }
        }

        private void Begin(double time)
        {
            Race = new Race(_config, _track);
            Pilot?.Reset();
            _moments = RaceMoment.None;
            ReleaseAll();
            _events |= FlowEvents.Started;
            Resume(time);
        }

        private void Pause()
        {
            ReleaseAll();
            Screen = AppScreen.Paused;
            _events |= FlowEvents.Paused;
        }

        private void Resume(double time)
        {
            Screen = AppScreen.Playing;
            _origin = time;
            _ticksDone = 0;
        }

        private void ToTitle()
        {
            Race = new Race(_config, _track);
            ReleaseAll();
            Screen = AppScreen.Title;
            _events |= FlowEvents.Title;
        }

        private void ReleaseAll()
        {
            _left = 0;
            _right = 0;
            _accel = 0;
            _brake = 0;
            _drift = 0;
        }
    }
}
