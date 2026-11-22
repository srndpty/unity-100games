using System;
using Game.Core;

namespace Game.App
{
    public enum AppScreen
    {
        Title,
        Playing,
        Paused,

        /// <summary>試合が終わった。勝敗とスコアを出す。</summary>
        Result,
    }

    /// <summary>キーの代わりに Tick ごとの操作を決める操縦役（撮影・テスト用）。</summary>
    public interface IPilot
    {
        AthleteCommand CommandFor(TennisMatch match);
    }

    /// <summary>
    /// 画面遷移と、プレイヤーの意図を Core へ渡す窓口（022・023 と同じ時刻の扱い）。
    /// <para>
    /// 時刻と Tick の対応：プレイを始めた（再開した）時刻を t0 とし、Tick n は区間 [t0 + (n-1)/60, t0 + n/60) を表す。
    /// 時刻 τ の入力は、τ を含む区間の Tick から効く（その前の Tick を先に進めてから入力を反映する）。
    /// どのフレームで届いても同じ Tick に効くので、打点の時機がフレームレートに依らない。
    /// </para>
    /// <para>
    /// 1 フレームの処理量：<see cref="Apply"/> と <see cref="Advance"/> は 1 フレームで合わせて <see cref="MaxTicksPerFrame"/> Tick までしか進めない。
    /// 上限を超えた区間は進めずに飛ばすが、入力は起きた順にすべて反映する。遊んだ時間（<see cref="PlayedTicks"/>）は飛ばした区間も数える。
    /// </para>
    /// <para>再戦のシードは、起動時に受け取った値から試合ごとに 1 ずつ進める（1 試合 = 1 シード）。</para>
    /// </summary>
    public sealed class TennisFlow
    {
        public const int MaxTicksPerFrame = 5;

        private readonly TennisConfig _config;
        private readonly HeldControls _held = new();
        private bool _hasFocus = true;
        private int _nextSeed;
        private double _origin;
        private long _ticksDone;
        private long _playedBase;
        private int _frameBudget = MaxTicksPerFrame;
        private FlowEvents _events;

        /// <param name="config">設定（複製して使う）。</param>
        /// <param name="firstSeed">最初の試合のシード。以後の試合は 1 ずつ進める。</param>
        public TennisFlow(TennisConfig config, int firstSeed)
        {
            if (config == null) { throw new ArgumentNullException(nameof(config)); }
            config.Validate();
            _config = config.Copy();
            _nextSeed = firstSeed;
        }

        public AppScreen Screen { get; private set; }

        public TennisMatch? Match { get; private set; }

        public TennisConfig Config => _config;

        /// <summary>設定されていれば、キー入力の代わりにこれで動かす（撮影・テスト用）。</summary>
        public IPilot? Pilot { get; set; }

        /// <summary>今の試合で遊んだ時間（Tick）。一時停止・フォーカス喪失・タイトルは含まないが、処理落ちで飛ばした区間は含む。</summary>
        public long PlayedTicks => _playedBase + _ticksDone;

        public void Apply(Intent intent)
        {
            if (!_hasFocus) { return; }
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
                        Match = null;
                        SwitchTo(AppScreen.Title);
                    }

                    break;
                case IntentKind.Quit:
                    break;
                case IntentKind.ControlDown:
                    // 押しっぱなしのキーを一時停止の後へ持ち越さないよう、プレイ中に押したものだけを数える。
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

        /// <summary>時刻 <paramref name="now"/> までの Tick を進め、フレームを終える。1 フレームに 1 回、入力をすべて反映した後に呼ぶ。</summary>
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
            _playedBase++;
            Step();
        }

        public FlowEvents TakeEvents()
        {
            var events = _events;
            _events = FlowEvents.None;
            return events;
        }

        private void Step()
        {
            var match = Match!;
            var command = _held.TakeTickCommand();
            if (Pilot != null) { command = Pilot.CommandFor(match); }
            match.Step(command);
            _events |= (FlowEvents)((long)match.TakeEvents() << 1);
            if (match.IsFinished) { SwitchTo(AppScreen.Result); }
        }

        private void AdvanceTo(double time)
        {
            if (Screen != AppScreen.Playing || !double.IsFinite(time)) { return; }
            var elapsed = Math.Floor((time - _origin) * Units.TickRate);
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

        private void Begin(double time)
        {
            Match = new TennisMatch(_config, _nextSeed);
            _nextSeed = unchecked(_nextSeed + 1);
            _playedBase = 0;
            _ticksDone = 0;
            _events |= FlowEvents.Started;
            Resume(time);
        }

        private void Resume(double time)
        {
            SwitchTo(AppScreen.Playing);
            _playedBase += _ticksDone;
            _origin = time;
            _ticksDone = 0;
        }

        private void SwitchTo(AppScreen screen)
        {
            Screen = screen;
            _held.Clear();
        }
    }

    /// <summary>画面に反映する出来事。試合の出来事（<see cref="MatchEvents"/>）に、試合の開始を足したもの。</summary>
    [Flags]
    public enum FlowEvents : long
    {
        None = 0,
        Started = 1,
        Toss = (long)MatchEvents.Toss << 1,
        Serve = (long)MatchEvents.Serve << 1,
        Stroke = (long)MatchEvents.Stroke << 1,
        Lob = (long)MatchEvents.Lob << 1,
        Whiff = (long)MatchEvents.Whiff << 1,
        Bounce = (long)MatchEvents.Bounce << 1,
        NetCord = (long)MatchEvents.NetCord << 1,
        Fault = (long)MatchEvents.Fault << 1,
        PointWon = (long)MatchEvents.PointWon << 1,
        GameWon = (long)MatchEvents.GameWon << 1,
        TiebreakStarted = (long)MatchEvents.TiebreakStarted << 1,
        MatchWon = (long)MatchEvents.MatchWon << 1,
        NextPoint = (long)MatchEvents.NextPoint << 1,
    }
}
