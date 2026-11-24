using System;
using Game.Core;

namespace Game.App
{
    public enum AppScreen
    {
        /// <summary>タイトルと操作の説明。</summary>
        Title,

        Playing,

        Paused,

        /// <summary>3 ホールの後の結果（スコアカード・起動中の最少打数）。</summary>
        Result,
    }

    /// <summary>キーの代わりに、Tick ごとに操作する操縦役（撮影・テスト用）。</summary>
    public interface IGolfPilot
    {
        /// <summary>ラウンドを始める前に呼ぶ。前の判断の状態を捨てる（023）。</summary>
        void Reset(int seed);

        /// <summary>各 Tick の前に呼ぶ。プレイヤーと同じ操作（向き・クラブ・打点・スイング）をラウンドへ送る。</summary>
        void Act(GolfRound round);
    }

    /// <summary>
    /// 画面遷移と、プレイヤーの意図を Core へ渡す窓口。
    /// <para>
    /// 時刻と Tick の対応（022・035 と同じ）：プレイを始めた（再開した）時刻を t0 とし、Tick n は区間 [t0 + (n-1)/60, t0 + n/60) を表す。
    /// 時刻 τ の入力は、τ までの Tick を先に進めてから反映する。メーターの押下は、その時点までに進んだ Tick 数で決まる（フレームレートに依らない）。
    /// </para>
    /// <para>
    /// 1 フレームの処理量：<see cref="Apply"/> と <see cref="Advance"/> は 1 フレームで合わせて <see cref="MaxTicksPerFrame"/> Tick までしか
    /// 進めない。超えた区間は飛ばすが、入力は全て順に反映する。
    /// </para>
    /// <para>
    /// 一時停止・フォーカス喪失では振っている途中のスイングを取り消す（打数に数えない）。止めている間にゲージを見て押し直せないようにする。
    /// </para>
    /// </summary>
    public sealed class GolfFlow
    {
        public const int MaxTicksPerFrame = 5;

        public const int TickRate = Units.TickRate;

        /// <summary>向きのキーを押してから繰り返しが始まるまでの Tick 数。</summary>
        public const int RepeatDelayTicks = 15;

        /// <summary>向きの繰り返しの間隔（Tick）。</summary>
        public const int RepeatIntervalTicks = 2;

        /// <summary>ラウンドごとにシードを進める量（黄金比、010）。</summary>
        private const int SeedStride = unchecked((int)0x9E3779B9);

        private readonly GolfConfig _config;
        private readonly Course _course;
        private readonly int _baseSeed;
        private int _rounds;
        private bool _hasFocus = true;
        private bool _aimLeft;
        private bool _aimRight;
        private int _aimHeldTicks;
        private double _origin;
        private long _ticksDone;
        private int _frameBudget = MaxTicksPerFrame;
        private FlowEvents _events;
        private RoundEvents _roundEvents;

        public GolfFlow(GolfConfig config, Course course, int seed)
        {
            if (config == null) { throw new ArgumentNullException(nameof(config)); }
            _course = course ?? throw new ArgumentNullException(nameof(course));
            config.Validate();
            _config = config.Copy();
            _baseSeed = seed;
        }

        public AppScreen Screen { get; private set; }

        /// <summary>設定の複製。</summary>
        public GolfConfig Config => _config.Copy();

        public Course Course => _course;

        /// <summary>今（または直前）のラウンド。タイトルでは null。</summary>
        public GolfRound? Round { get; private set; }

        /// <summary>起動中の最少の合計打数（まだなければ 0）。</summary>
        public int BestTotal { get; private set; }

        /// <summary>直前のラウンドで最少記録を更新したか。</summary>
        public bool IsNewBest { get; private set; }

        /// <summary>設定されていれば、キーの代わりにこれで操作する（撮影・テスト用）。</summary>
        public IGolfPilot? Pilot { get; set; }

        /// <summary>押している向きのキー（+1：左、−1：右、0：なし・両方）。</summary>
        public int AimHeld => (_aimLeft ? 1 : 0) - (_aimRight ? 1 : 0);

        public void Apply(Intent intent)
        {
            if (!_hasFocus) { return; }
            AdvanceTo(intent.Time);
            switch (Screen)
            {
                case AppScreen.Title:
                    if (intent.Kind == IntentKind.Submit || intent.Kind == IntentKind.Swing) { Begin(intent.Time); }
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
                    if (intent.Kind == IntentKind.Submit || intent.Kind == IntentKind.Swing) { Begin(intent.Time); }
                    else if (intent.Kind == IntentKind.Cancel) { ToTitle(); }
                    break;
                default:
                    throw new InvalidOperationException($"未定義の画面です：{Screen}");
            }
        }

        /// <summary>フォーカスを失うとキーを離したとみなし、プレイ中は一時停止する（戻っても自動では再開しない）。</summary>
        public void SetFocus(bool focus)
        {
            _hasFocus = focus;
            if (focus) { return; }
            ReleaseAll();
            if (Screen == AppScreen.Playing) { Pause(); }
        }

        /// <summary>時刻 <paramref name="now"/> までの Tick を進め、フレームを終える。1 フレームに 1 回、入力を全て反映した後に呼ぶ。</summary>
        public void Advance(double now)
        {
            if (_hasFocus) { AdvanceTo(now); }
            EndFrame();
        }

        /// <summary>時計で進めずにフレームを終える（撮影中など、Tick を直接進めるとき）。</summary>
        public void EndFrame() => _frameBudget = MaxTicksPerFrame;

        /// <summary>時刻と無関係に 1 Tick 進める（撮影・テスト用）。プレイ中でなければ何もしない。</summary>
        public void StepTick()
        {
            if (Screen != AppScreen.Playing) { return; }
            Step();
        }

        /// <summary>前回の呼び出し以降に起きた画面の出来事を取り出す。</summary>
        public FlowEvents TakeEvents()
        {
            var events = _events;
            _events = FlowEvents.None;
            return events;
        }

        /// <summary>前回の呼び出し以降にラウンドで起きたことの合計を取り出す（音・演出用）。</summary>
        public RoundEvents TakeRoundEvents()
        {
            if (Round != null) { _roundEvents |= Round.TakeEvents(); }
            var events = _roundEvents;
            _roundEvents = RoundEvents.None;
            return events;
        }

        private void ApplyPlaying(Intent intent)
        {
            var round = Round!;
            switch (intent.Kind)
            {
                case IntentKind.AimLeftDown:
                    if (!_aimLeft) { StartAim(round, ref _aimLeft); }
                    break;
                case IntentKind.AimLeftUp:
                    _aimLeft = false;
                    _aimHeldTicks = 0;
                    break;
                case IntentKind.AimRightDown:
                    if (!_aimRight) { StartAim(round, ref _aimRight); }
                    break;
                case IntentKind.AimRightUp:
                    _aimRight = false;
                    _aimHeldTicks = 0;
                    break;
                case IntentKind.ClubUp:
                    round.ShiftClub(-1);
                    break;
                case IntentKind.ClubDown:
                    round.ShiftClub(1);
                    break;
                case IntentKind.ImpactUp:
                    round.SetImpact(round.ImpactX, Math.Min(round.ImpactY + 1, GolfConfig.MaxImpact));
                    break;
                case IntentKind.ImpactDown:
                    round.SetImpact(round.ImpactX, Math.Max(round.ImpactY - 1, -GolfConfig.MaxImpact));
                    break;
                case IntentKind.ImpactLeft:
                    round.SetImpact(Math.Max(round.ImpactX - 1, -GolfConfig.MaxImpact), round.ImpactY);
                    break;
                case IntentKind.ImpactRight:
                    round.SetImpact(Math.Min(round.ImpactX + 1, GolfConfig.MaxImpact), round.ImpactY);
                    break;
                case IntentKind.Swing:
                    round.Press();
                    break;
                case IntentKind.Cancel:
                    Pause();
                    break;
            }
        }

        /// <summary>向きのキーを押した：すぐ 1 回回し、繰り返しの数え直しを始める。</summary>
        private void StartAim(GolfRound round, ref bool held)
        {
            held = true;
            _aimHeldTicks = 0;
            var direction = AimHeld;
            if (direction != 0) { round.TurnAim(direction); }
        }

        private void Step()
        {
            var round = Round!;
            Pilot?.Act(round);
            RepeatAim(round);
            _roundEvents |= round.Step();
            if (round.Phase != RoundPhase.Finished) { return; }
            IsNewBest = BestTotal == 0 || round.Total < BestTotal;
            if (IsNewBest) { BestTotal = round.Total; }
            ReleaseAll();
            Screen = AppScreen.Result;
            _events |= FlowEvents.Result;
        }

        private void RepeatAim(GolfRound round)
        {
            var direction = AimHeld;
            if (direction == 0)
            {
                _aimHeldTicks = 0;
                return;
            }

            _aimHeldTicks++;
            var since = _aimHeldTicks - RepeatDelayTicks;
            if (since >= 0 && since % RepeatIntervalTicks == 0) { round.TurnAim(direction); }
        }

        private void AdvanceTo(double time)
        {
            if (Screen != AppScreen.Playing || !double.IsFinite(time)) { return; }
            var elapsed = Math.Floor((time - _origin) * TickRate);
            if (elapsed <= _ticksDone) { return; }

            // このフレームの残りの上限を超える区間は、手前を飛ばして直前の Tick だけを進める。
            var target = elapsed > long.MaxValue / 2 ? long.MaxValue / 2 : (long)elapsed;
            if (target - _ticksDone > _frameBudget) { _ticksDone = target - _frameBudget; }
            while (_ticksDone < target && Screen == AppScreen.Playing)
            {
                _ticksDone++;
                _frameBudget--;
                Step();
            }
        }

        /// <summary>新しいシードでラウンドを始める（再戦も新しいシード、ラウンドをまたいで乱数を持ち越さない）。</summary>
        private void Begin(double time)
        {
            var seed = unchecked(_baseSeed + (_rounds * SeedStride));
            _rounds++;
            Round = new GolfRound(_config, _course, seed);
            Pilot?.Reset(seed);
            IsNewBest = false;
            _roundEvents = RoundEvents.None;
            ReleaseAll();
            _events |= FlowEvents.Started;
            Resume(time);
        }

        private void Pause()
        {
            ReleaseAll();
            Round?.CancelSwing();
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
            Round = null;
            ReleaseAll();
            Screen = AppScreen.Title;
            _events |= FlowEvents.Title;
        }

        private void ReleaseAll()
        {
            _aimLeft = false;
            _aimRight = false;
            _aimHeldTicks = 0;
        }
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
}
