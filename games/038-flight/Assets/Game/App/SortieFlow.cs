using System;
using Game.Core;

namespace Game.App
{
    public enum AppScreen
    {
        /// <summary>タイトルと操作説明。</summary>
        Title,
        Playing,
        Paused,
        Result,
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
    /// タイトル（操作説明）、ミッション、一時停止、結果。入力は時刻付きの 1 本の列で、
    /// 1 フレームでは時計と入力を合わせて <see cref="MaxTicksPerFrame"/> Tick まで進める。
    /// 舵とスロットルはキーごとに押している印を持ち、発射と目標の切り替えは押した瞬間に次の Tick へ 1 回だけ渡す。
    /// 1 試合 = 1 シード。再挑戦は起動時のシードから 1 ずつ進めた新しいシードで作り直す。
    /// </summary>
    public sealed class SortieFlow
    {
        public const int MaxTicksPerFrame = 5;

        public const int TickRate = Units.TickRate;

        private readonly FlightConfig _flight;
        private readonly MissionConfig _config;
        private readonly int[] _held = new int[HeldCount];
        private bool _hasFocus = true;
        private bool _fire;
        private bool _cycle;
        private int _nextSeed;
        private double _origin;
        private long _ticksDone;
        private int _frameBudget = MaxTicksPerFrame;
        private FlowEvents _events;
        private MissionMoment _moments;

        public SortieFlow(FlightConfig flight, MissionConfig config, int firstSeed)
        {
            if (flight == null) { throw new ArgumentNullException(nameof(flight)); }
            if (config == null) { throw new ArgumentNullException(nameof(config)); }
            _flight = flight.Validate();
            _config = config.Validate(_flight);
            _nextSeed = firstSeed;
            Mission = new Mission(_flight, _config, _nextSeed);
        }

        public AppScreen Screen { get; private set; } = AppScreen.Title;

        public Mission Mission { get; private set; }

        public ISortiePilot? Pilot { get; set; }

        /// <summary>直近の発射の要求の結果（HUD の「未ロック」などの表示用）と、その Tick。</summary>
        public FireResult LastFire { get; private set; }

        public int LastFireTick { get; private set; } = -1;

        public bool Won => Screen == AppScreen.Result && Mission.Outcome == Outcome.Won;

        /// <summary>キーから作る今の操縦（押している印だけから決まる）。</summary>
        public Controls Held => new(
            AxisValue(IsHeld(HeldKey.PitchUp), IsHeld(HeldKey.PitchDown)),
            AxisValue(IsHeld(HeldKey.RollRight), IsHeld(HeldKey.RollLeft)),
            AxisValue(IsHeld(HeldKey.YawRight), IsHeld(HeldKey.YawLeft)),
            AxisValue(IsHeld(HeldKey.ThrottleUp), IsHeld(HeldKey.ThrottleDown)));

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

        /// <summary>フォーカスを失うとキーを離し、まだ渡していない発射・切り替えも捨て、プレイ中は一時停止する。復帰は手動。</summary>
        public void SetFocus(bool focus)
        {
            _hasFocus = focus;
            if (focus) { return; }
            if (Screen == AppScreen.Playing) { Pause(); }
            else { ReleaseAll(); }
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

        public MissionMoment TakeMoments()
        {
            var moments = _moments;
            _moments = MissionMoment.None;
            return moments;
        }

        private void ApplyPlaying(Intent intent)
        {
            var bit = intent.KeyBit;
            switch (intent.Kind)
            {
                case IntentKind.PitchUpDown: Press(HeldKey.PitchUp, bit); break;
                case IntentKind.PitchUpUp: Release(HeldKey.PitchUp, bit); break;
                case IntentKind.PitchDownDown: Press(HeldKey.PitchDown, bit); break;
                case IntentKind.PitchDownUp: Release(HeldKey.PitchDown, bit); break;
                case IntentKind.RollLeftDown: Press(HeldKey.RollLeft, bit); break;
                case IntentKind.RollLeftUp: Release(HeldKey.RollLeft, bit); break;
                case IntentKind.RollRightDown: Press(HeldKey.RollRight, bit); break;
                case IntentKind.RollRightUp: Release(HeldKey.RollRight, bit); break;
                case IntentKind.YawLeftDown: Press(HeldKey.YawLeft, bit); break;
                case IntentKind.YawLeftUp: Release(HeldKey.YawLeft, bit); break;
                case IntentKind.YawRightDown: Press(HeldKey.YawRight, bit); break;
                case IntentKind.YawRightUp: Release(HeldKey.YawRight, bit); break;
                case IntentKind.ThrottleUpDown: Press(HeldKey.ThrottleUp, bit); break;
                case IntentKind.ThrottleUpUp: Release(HeldKey.ThrottleUp, bit); break;
                case IntentKind.ThrottleDownDown: Press(HeldKey.ThrottleDown, bit); break;
                case IntentKind.ThrottleDownUp: Release(HeldKey.ThrottleDown, bit); break;
                case IntentKind.FireDown:
                    // 押している間に同じキーの押下が重なって届いても 2 発目にしない（027）。
                    if (_held[(int)HeldKey.Fire] == 0) { _fire = true; }
                    Press(HeldKey.Fire, bit);
                    break;
                case IntentKind.FireUp: Release(HeldKey.Fire, bit); break;
                case IntentKind.CycleDown:
                    if (_held[(int)HeldKey.Cycle] == 0) { _cycle = true; }
                    Press(HeldKey.Cycle, bit);
                    break;
                case IntentKind.CycleUp: Release(HeldKey.Cycle, bit); break;
                case IntentKind.Cancel: Pause(); break;
            }
        }

        private void Step()
        {
            var command = Pilot != null ? Pilot.CommandFor(Mission) : new PlayerCommand(Held, _fire, _cycle);
            _fire = false;
            _cycle = false;
            var (fire, moments) = Mission.Step(command);
            if (fire != FireResult.None)
            {
                LastFire = fire;
                LastFireTick = Mission.Tick;
            }

            _moments |= moments;
            if (!Mission.IsOver) { return; }
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
            _nextSeed = unchecked(_nextSeed + 1);
            Mission = new Mission(_flight, _config, _nextSeed);
            Pilot?.Reset();
            _moments = MissionMoment.None;
            LastFire = FireResult.None;
            LastFireTick = -1;
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
            Mission = new Mission(_flight, _config, _nextSeed);
            ReleaseAll();
            Screen = AppScreen.Title;
            _events |= FlowEvents.Title;
        }

        private void ReleaseAll()
        {
            Array.Clear(_held, 0, _held.Length);
            _fire = false;
            _cycle = false;
        }

        private void Press(HeldKey key, int bit) => _held[(int)key] |= bit;

        private void Release(HeldKey key, int bit) => _held[(int)key] &= ~bit;

        private bool IsHeld(HeldKey key) => _held[(int)key] != 0;

        private static int AxisValue(bool positive, bool negative) =>
            positive == negative ? 0 : positive ? Controls.Full : -Controls.Full;

        private const int HeldCount = 10;

        private enum HeldKey
        {
            PitchUp,
            PitchDown,
            RollLeft,
            RollRight,
            YawLeft,
            YawRight,
            ThrottleUp,
            ThrottleDown,
            Fire,
            Cycle,
        }
    }
}
