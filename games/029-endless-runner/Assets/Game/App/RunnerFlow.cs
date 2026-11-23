using System;
using System.Collections.Generic;
using Game.Core;
using TileMotion;

namespace Game.App
{
    public enum AppScreen
    {
        Title,
        Playing,
        Paused,

        /// <summary>障害物に触れて試合が終わった。</summary>
        Result,
    }

    /// <summary>キーの代わりに Tick ごとの操作を決める操縦役（撮影・テスト用）。</summary>
    public interface IPilot
    {
        Command CommandFor(RunnerRun run);
    }

    /// <summary>
    /// 画面遷移と、プレイヤーの意図を Core へ渡す窓口（026 の SiegeFlow と同じ時刻の扱い）。
    /// <para>
    /// 時刻と Tick の対応：プレイを始めた（再開した）時刻を t0 とし、Tick n は区間 [t0 + (n-1)/60, t0 + n/60) を表す。
    /// 時刻 τ の押下は、τ を含む区間の Tick から効く（その前の Tick を先に進めてから押下を列に入れる）。
    /// </para>
    /// <para>
    /// 押下の列：1 Tick の操作は横（←→）と縦（ジャンプ・スライド）を 1 つずつ。列の先頭から順に取り、
    /// 同じ側の 2 つ目に当たったらそこで止めて次の Tick へ回す（押した順を崩さない。012 の知見）。
    /// </para>
    /// <para>
    /// シード：試合ごとに <c>firstSeed</c> から 1 ずつ進めた値を使う（再戦は必ず新しいシード。010 の知見）。
    /// 最高記録は起動している間だけ持つ。
    /// </para>
    /// </summary>
    public sealed class RunnerFlow
    {
        /// <summary>1 フレームで進める Tick 数の上限。</summary>
        public const int MaxTicksPerFrame = 5;

        /// <summary>覚えておく押下の数の上限（連打で列が伸び続けないように、超えた分は捨てる）。</summary>
        public const int MaxPending = 8;

        private readonly RunRules _rules;
        private readonly List<RunMove> _pending = new();
        private bool _hasFocus = true;
        private int _nextSeed;
        private double _origin;
        private long _ticksDone;
        private int _frameBudget = MaxTicksPerFrame;
        private FlowEvents _events;

        /// <param name="rules">検査済みの設定とチャンク。</param>
        /// <param name="firstSeed">最初の試合のシード。以後の試合は 1 ずつ進める。</param>
        public RunnerFlow(RunRules rules, int firstSeed)
        {
            _rules = rules ?? throw new ArgumentNullException(nameof(rules));
            _nextSeed = firstSeed;
        }

        public AppScreen Screen { get; private set; }

        public RunnerRun? Run { get; private set; }

        public RunRules Rules => _rules;

        /// <summary>起動してからの最高記録（m）。</summary>
        public long BestMeters { get; private set; }

        /// <summary>直前の試合で最高記録を更新した。</summary>
        public bool IsNewBest { get; private set; }

        /// <summary>設定されていれば、キーの代わりにこれで動かす（撮影・テスト用）。</summary>
        public IPilot? Pilot { get; set; }

        /// <summary>まだ Core へ渡していない押下の数。</summary>
        public int PendingCount => _pending.Count;

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
                        Run = null;
                        SwitchTo(AppScreen.Title);
                    }

                    break;
                case IntentKind.Quit:
                    break;
                case IntentKind.Press:
                    // 一時停止・結果の間の押下は捨てる（再開した試合へ持ち越さない）。始めた（再開した）時刻ちょうどの押下も捨てる：
                    // Space は決定とジャンプの両方に割り当てているので、試合を始めた押下でそのまま跳ばない。
                    if (Screen == AppScreen.Playing && intent.Time > _origin && _pending.Count < MaxPending) { _pending.Add(intent.Move); }
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
            _pending.Clear();
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
            Step();
        }

        /// <summary>前回の呼び出し以降に起きた出来事を取り出す。</summary>
        public FlowEvents TakeEvents()
        {
            var events = _events;
            _events = FlowEvents.None;
            return events;
        }

        private void Step()
        {
            var run = Run!;
            run.Step(Pilot?.CommandFor(run) ?? TakeCommand());
            _events |= (FlowEvents)((int)run.TakeEvents() << 1);
            if (!run.IsOver) { return; }
            IsNewBest = run.Meters > BestMeters;
            if (IsNewBest) { BestMeters = run.Meters; }
            SwitchTo(AppScreen.Result);
        }

        /// <summary>列の先頭から、横と縦を 1 つずつ取る。同じ側の 2 つ目で止める。</summary>
        private Command TakeCommand()
        {
            var steer = Steer.None;
            var leap = Leap.None;
            while (_pending.Count > 0)
            {
                var move = _pending[0];
                if (move == RunMove.Left || move == RunMove.Right)
                {
                    if (steer != Steer.None) { break; }
                    steer = move == RunMove.Left ? Steer.Left : Steer.Right;
                }
                else
                {
                    if (leap != Leap.None) { break; }
                    leap = move == RunMove.Jump ? Leap.Jump : Leap.Slide;
                }

                _pending.RemoveAt(0);
            }

            return new Command(steer, leap);
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
            Run = new RunnerRun(_rules, _nextSeed);
            _nextSeed = unchecked(_nextSeed + 1);
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
            _pending.Clear();
        }
    }

    /// <summary>画面に反映する出来事。試合の出来事（<see cref="RunEvents"/>）に、試合の開始を足したもの。</summary>
    [Flags]
    public enum FlowEvents
    {
        None = 0,
        Started = 1,
        Steered = (int)RunEvents.Steered << 1,
        Jumped = (int)RunEvents.Jumped << 1,
        Slid = (int)RunEvents.Slid << 1,
        Landed = (int)RunEvents.Landed << 1,
        Crashed = (int)RunEvents.Crashed << 1,
        SpedUp = (int)RunEvents.SpedUp << 1,
    }
}
