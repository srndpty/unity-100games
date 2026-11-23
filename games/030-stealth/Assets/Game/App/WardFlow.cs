using System;
using Game.Core;
using TileMotion;

namespace Game.App
{
    public enum AppScreen
    {
        Title,
        Playing,
        Paused,

        /// <summary>捕まった。決定で新しい試合を始める。</summary>
        Caught,

        /// <summary>目的物を持って脱出した。</summary>
        Escaped,
    }

    /// <summary>キーの代わりに Tick ごとの入力を決める操縦役（撮影・テスト用）。</summary>
    public interface IPilot
    {
        IntruderInput InputFor(Infiltration match);
    }

    /// <summary>
    /// 画面遷移と、プレイヤーの意図を Core へ渡す窓口（025 の VaultFlow と同じ時刻の扱い）。
    /// <para>
    /// 時刻と Tick の対応：プレイを始めた（再開した）時刻を t0 とし、Tick n は区間 [t0 + (n-1)/60, t0 + n/60) を表す。
    /// 時刻 τ の入力は、τ を含む区間の Tick から効く（その前の Tick を先に進めてから入力を反映する）。
    /// </para>
    /// <para>
    /// 1 フレームの処理量：<see cref="Apply"/> と <see cref="Advance"/> は 1 フレームで合わせて <see cref="MaxTicksPerFrame"/> Tick までしか
    /// 進めない。上限を超えた区間は飛ばすが、入力は起きた順にすべて反映する。遊んだ時間（<see cref="PlayedTicks"/>）は飛ばした区間も数える。
    /// </para>
    /// <para>シード：試合ごとに <c>firstSeed</c> から 1 ずつ進めた値を使う（再戦は必ず新しいシード）。</para>
    /// </summary>
    public sealed class WardFlow
    {
        /// <summary>1 フレームで進める Tick 数の上限。</summary>
        public const int MaxTicksPerFrame = 5;

        private readonly WardConfig _config;
        private readonly Func<Facility> _createFacility;
        private readonly HeldControls _held = new();
        private bool _hasFocus = true;
        private int _nextSeed;
        private double _origin;
        private long _ticksDone;
        private long _playedBase;
        private int _frameBudget = MaxTicksPerFrame;
        private FlowEvents _events;

        /// <param name="config">設定（検証して複製する）。</param>
        /// <param name="firstSeed">最初の試合のシード。以後の試合は 1 ずつ進める。</param>
        /// <param name="createFacility">試合ごとに施設を作る関数（既定は本番の施設）。</param>
        public WardFlow(WardConfig config, int firstSeed, Func<Facility>? createFacility = null)
        {
            if (config == null) { throw new ArgumentNullException(nameof(config)); }
            config.Validate();
            _config = config.Copy();
            _nextSeed = firstSeed;
            _createFacility = createFacility ?? Facility.Ward;
        }

        public AppScreen Screen { get; private set; }

        public Infiltration? Match { get; private set; }

        /// <summary>設定されていれば、キー入力の代わりにこれで動かす（撮影・テスト用）。</summary>
        public IPilot? Pilot { get; set; }

        /// <summary>警備員の状態の詳しい表示（F1）。試合をまたいで保つ。</summary>
        public bool ShowDebug { get; private set; }

        /// <summary>今の試合で遊んだ時間（Tick 単位）。一時停止・結果・フォーカス喪失・タイトルは含まない。</summary>
        public long PlayedTicks => _playedBase + _ticksDone;

        public void Apply(Intent intent)
        {
            if (!_hasFocus) { return; }
            AdvanceTo(intent.Time);
            switch (intent.Kind)
            {
                case IntentKind.Submit:
                    if (Screen == AppScreen.Title || IsResult) { Begin(intent.Time); }
                    else if (Screen == AppScreen.Paused) { Resume(intent.Time); }
                    break;
                case IntentKind.TogglePause:
                    if (Screen == AppScreen.Playing) { SwitchTo(AppScreen.Paused); }
                    else if (Screen == AppScreen.Paused) { Resume(intent.Time); }
                    break;
                case IntentKind.Restart:
                    if (Screen == AppScreen.Paused || IsResult) { Begin(intent.Time); }
                    break;
                case IntentKind.BackToTitle:
                    if (Screen == AppScreen.Paused || IsResult)
                    {
                        Match = null;
                        SwitchTo(AppScreen.Title);
                    }

                    break;
                case IntentKind.ToggleDebug:
                    ShowDebug = !ShowDebug;
                    _events |= FlowEvents.DebugToggled;
                    break;
                case IntentKind.Quit:
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

        /// <summary>前回の呼び出し以降に起きた出来事を取り出す。</summary>
        public FlowEvents TakeEvents()
        {
            var events = _events;
            _events = FlowEvents.None;
            return events;
        }

        private bool IsResult => Screen == AppScreen.Caught || Screen == AppScreen.Escaped;

        private void Step()
        {
            var match = Match!;
            var input = Pilot != null ? Pilot.InputFor(match) : _held.TickInput();
            match.Tick(input);
            _events |= (FlowEvents)((int)match.TakeEvents() << 2);
            if (match.Status == MatchStatus.Caught) { SwitchTo(AppScreen.Caught); }
            else if (match.Status == MatchStatus.Escaped) { SwitchTo(AppScreen.Escaped); }
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
            Match = new Infiltration(_config, _createFacility(), _nextSeed);
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

    /// <summary>画面に反映する出来事。試合の出来事（<see cref="WardEvents"/>）に、試合の開始と表示の切り替えを足したもの。</summary>
    [Flags]
    public enum FlowEvents
    {
        None = 0,
        Started = 1,
        DebugToggled = 1 << 1,
        Step = (int)WardEvents.Step << 2,
        Suspicious = (int)WardEvents.Suspicious << 2,
        Discovered = (int)WardEvents.Discovered << 2,
        LostTrack = (int)WardEvents.LostTrack << 2,
        ObjectiveTaken = (int)WardEvents.ObjectiveTaken << 2,
        Caught = (int)WardEvents.Caught << 2,
        Escaped = (int)WardEvents.Escaped << 2,
        Calmed = (int)WardEvents.Calmed << 2,
    }
}
