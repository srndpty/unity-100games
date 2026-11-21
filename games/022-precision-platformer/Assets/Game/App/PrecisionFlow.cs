using System;
using System.Collections.Generic;
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

    [Flags]
    public enum FlowEvents
    {
        None = 0,
        Started = 1 << 0,
        Jumped = 1 << 1,
        WallJumped = 1 << 2,
        Dashed = 1 << 3,
        Died = 1 << 4,
        Respawned = 1 << 5,

        /// <summary>部屋を抜けた（最後の部屋では <see cref="Won"/> も立つ）。</summary>
        RoomCleared = 1 << 6,
        Won = 1 << 7,
    }

    /// <summary>
    /// 画面遷移と、プレイヤーの意図を Core へ渡す窓口。
    /// <para>
    /// 時刻と Tick の対応：プレイを始めた（再開した）時刻を t0 とし、Tick n は区間 [t0 + (n-1)/60, t0 + n/60) を表す。
    /// 時刻 τ の入力は、τ を含む区間の Tick から効く（その前の Tick を先に進めてから入力を反映する）。
    /// どのフレームで届いても同じ Tick に効くので、フレームレートが違っても同じ入力の列なら同じ結果になる。
    /// </para>
    /// </summary>
    public sealed class PrecisionFlow
    {
        /// <summary>遅れがこれを超えたら、超えたぶんの時間を捨てる（処理落ちの後に一度に進める Tick 数の上限）。</summary>
        public const int MaxTicksBehind = 5;

        private readonly PrecisionConfig _config;
        private readonly Room[] _rooms;
        private readonly HeldControls _held = new();
        private bool _hasFocus = true;
        private double _origin;
        private long _ticksDone;
        private FlowEvents _events;

        public PrecisionFlow(PrecisionConfig config, IReadOnlyList<Room> rooms)
        {
            if (config == null) { throw new ArgumentNullException(nameof(config)); }
            if (rooms == null) { throw new ArgumentNullException(nameof(rooms)); }
            config.Validate();
            _config = config.Copy();
            _rooms = new Room[rooms.Count];
            for (var i = 0; i < _rooms.Length; i++) { _rooms[i] = rooms[i]; }

            // 部屋の組を確かめておく（空・null ならここで例外になる）。
            _ = new Climb(_config, _rooms);
        }

        public AppScreen Screen { get; private set; }

        public Climb? Climb { get; private set; }

        /// <summary>設定されていれば、キー入力の代わりに入力の列で動かす（撮影・テスト用）。</summary>
        public RoutePilot? Pilot { get; set; }

        /// <summary>起動してからの最短の完走 Tick 数（まだなければ null）。</summary>
        public long? BestTicks { get; private set; }

        /// <summary>直前の挑戦で最短記録を更新したか。</summary>
        public bool IsNewBest { get; private set; }

        public void Apply(Intent intent)
        {
            if (!_hasFocus) { return; }

            // 入力が起きた時刻より前の Tick を先に進める。
            AdvanceTo(intent.Time);
            switch (intent.Kind)
            {
                case IntentKind.Submit:
                    if (Screen == AppScreen.Title || Screen == AppScreen.Result) { Begin(intent.Time); }
                    else if (Screen == AppScreen.Paused) { Resume(intent.Time); }
                    break;
                case IntentKind.TogglePause:
                    if (Screen == AppScreen.Playing) { SwitchTo(AppScreen.Paused); }
                    else if (Screen == AppScreen.Paused) { Resume(intent.Time); }
                    break;
                case IntentKind.Restart:
                    if (Screen == AppScreen.Paused || Screen == AppScreen.Result) { Begin(intent.Time); }
                    break;
                case IntentKind.BackToTitle:
                    if (Screen == AppScreen.Paused || Screen == AppScreen.Result)
                    {
                        Climb = null;
                        SwitchTo(AppScreen.Title);
                    }

                    break;
                case IntentKind.Quit:
                    // アプリの終了は Unity 層が行う。ここでは状態を変えない。
                    break;
                case IntentKind.ControlDown:
                    // 押しっぱなしのキーを一時停止・結果の後へ持ち越さないよう、プレイ中に押したものだけを数える。
                    if (Screen == AppScreen.Playing) { _held.Press(intent.Control); }
                    break;
                case IntentKind.ControlUp:
                    _held.Release(intent.Control);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(intent), intent.Kind, "未定義の操作です。");
            }
        }

        /// <summary>フォーカスを失うとプレイ中は一時停止する。戻っても自動では再開しない。</summary>
        public void SetFocus(bool focus)
        {
            _hasFocus = focus;
            if (focus) { return; }
            if (Screen == AppScreen.Playing) { SwitchTo(AppScreen.Paused); }
            _held.Clear();
        }

        /// <summary>時刻 <paramref name="now"/>（秒、<see cref="Intent.Time"/> と同じ時計）までの Tick を進める。</summary>
        public void Advance(double now)
        {
            if (!_hasFocus) { return; }
            AdvanceTo(now);
        }

        /// <summary>時刻と無関係に 1 Tick 進める（撮影・テスト用）。プレイ中でなければ何もしない。</summary>
        public void StepTick()
        {
            if (Screen != AppScreen.Playing) { return; }
            var climb = Climb!;
            var input = _held.TakeTickInput();
            if (Pilot != null) { input = Pilot.Next(climb); }
            climb.Tick(input);
            Collect();
        }

        /// <summary>前回の呼び出し以降に起きた出来事を取り出す。</summary>
        public FlowEvents TakeEvents()
        {
            var events = _events;
            _events = FlowEvents.None;
            return events;
        }

        private void AdvanceTo(double time)
        {
            if (Screen != AppScreen.Playing || !double.IsFinite(time)) { return; }
            var elapsed = Math.Floor((time - _origin) * Units.TickRate);
            if (elapsed <= _ticksDone) { return; }

            // 処理落ちで遅れすぎたら、上限を超えたぶんの Tick を飛ばす（その間の時間は進まなかったことにする）。
            var target = elapsed > long.MaxValue / 2 ? long.MaxValue / 2 : (long)elapsed;
            if (target - _ticksDone > MaxTicksBehind) { _ticksDone = target - MaxTicksBehind; }
            while (_ticksDone < target && Screen == AppScreen.Playing)
            {
                _ticksDone++;
                StepTick();
            }
        }

        private void Begin(double time)
        {
            Climb = new Climb(_config, _rooms);
            IsNewBest = false;
            _events |= FlowEvents.Started;
            Resume(time);
        }

        private void Resume(double time)
        {
            SwitchTo(AppScreen.Playing);
            _origin = time;
            _ticksDone = 0;
        }

        private void SwitchTo(AppScreen screen)
        {
            Screen = screen;
            _held.Clear();
        }

        private void Collect()
        {
            var climb = Climb!;
            var kinds = climb.TakeEvents();
            if ((kinds & RunEventKind.Jumped) != 0) { _events |= FlowEvents.Jumped; }
            if ((kinds & RunEventKind.WallJumped) != 0) { _events |= FlowEvents.WallJumped; }
            if ((kinds & RunEventKind.Dashed) != 0) { _events |= FlowEvents.Dashed; }
            if ((kinds & RunEventKind.Died) != 0) { _events |= FlowEvents.Died; }
            if ((kinds & RunEventKind.Respawned) != 0) { _events |= FlowEvents.Respawned; }
            if ((kinds & RunEventKind.Cleared) != 0) { _events |= FlowEvents.RoomCleared; }

            if (climb.IsWon)
            {
                _events |= FlowEvents.Won;
                IsNewBest = BestTicks == null || climb.ElapsedTicks < BestTicks;
                if (IsNewBest) { BestTicks = climb.ElapsedTicks; }
                SwitchTo(AppScreen.Result);
            }
        }
    }
}
