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
    /// <para>
    /// 1 フレームの処理量：<see cref="Apply"/> と <see cref="Advance"/> は 1 フレームで合わせて <see cref="MaxTicksPerFrame"/> Tick までしか
    /// 進めない（<see cref="Advance"/> か <see cref="EndFrame"/> でフレームの終わりとし、次のフレームの分を数え直す）。
    /// 上限を超えた区間は進めずに飛ばす（その間の時間はなかったことにする）が、入力は起きた順にすべて反映するので、
    /// 押している・離したの状態は崩れない。
    /// </para>
    /// </summary>
    public sealed class PrecisionFlow
    {
        /// <summary>1 フレームで進める Tick 数の上限（処理落ちの後に一度に進める量を抑える）。</summary>
        public const int MaxTicksPerFrame = 5;

        private readonly PrecisionConfig _config;
        private readonly Room[] _rooms;
        private readonly HeldControls _held = new();
        private bool _hasFocus = true;
        private double _origin;
        private long _ticksDone;
        private int _frameBudget = MaxTicksPerFrame;
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

        /// <summary>
        /// 時刻 <paramref name="now"/>（秒、<see cref="Intent.Time"/> と同じ時計）までの Tick を進め、フレームを終える。
        /// 1 フレームに 1 回、そのフレームの入力をすべて <see cref="Apply"/> した後に呼ぶ。
        /// </summary>
        public void Advance(double now)
        {
            if (_hasFocus) { AdvanceTo(now); }
            EndFrame();
        }

        /// <summary>時計で進めずにフレームを終える（撮影中など、Tick を直接進めるとき）。次のフレームの上限を数え直す。</summary>
        public void EndFrame() => _frameBudget = MaxTicksPerFrame;

        /// <summary>今のフレームで、まだ進められる Tick 数。</summary>
        public int FrameBudget => _frameBudget;

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

            // このフレームの残りの上限を超える区間は、手前を飛ばして（時間は進まなかったことにして）直前の Tick だけを進める。
            // 同じフレームの他の入力・Advance と上限を分け合うので、入力がいくつたまっても 1 フレームの Tick 数は上限を超えない。
            var target = elapsed > long.MaxValue / 2 ? long.MaxValue / 2 : (long)elapsed;
            if (target - _ticksDone > _frameBudget) { _ticksDone = target - _frameBudget; }
            while (_ticksDone < target && Screen == AppScreen.Playing)
            {
                _ticksDone++;
                _frameBudget--;
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
