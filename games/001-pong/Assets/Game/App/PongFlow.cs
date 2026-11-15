using System.Collections.Generic;
using Game.Core;

namespace Game.App
{
    public enum AppScreen
    {
        Title,
        Playing,
        Result,
    }

    /// <summary>
    /// 画面遷移（タイトル → 試合 → 結果 → 再戦）と、実時間から固定刻みの Tick への変換を統括する。
    /// プレイヤーは左、CPU は右。
    /// </summary>
    public sealed class PongFlow
    {
        /// <summary>1 フレームで処理する Tick の上限。処理落ちで追いつけないときは時間を切り捨てる。</summary>
        public const int MaxTicksPerFrame = 5;

        private readonly PongConfig _config;
        private readonly CpuConfig _cpuConfig;
        private readonly List<MatchEvent> _frameEvents = new();
        private int _nextSeed;
        private CpuController? _cpu;
        private double _accumulator;

        public PongFlow(PongConfig config, CpuConfig cpuConfig, int seed)
        {
            config.Validate();
            cpuConfig.Validate();
            _config = config;
            _cpuConfig = cpuConfig;
            _nextSeed = seed;
        }

        public AppScreen Screen { get; private set; } = AppScreen.Title;

        /// <summary>現在の試合。タイトル画面では null。</summary>
        public MatchState? Match { get; private set; }

        /// <summary>直前に使った試合のシード（再現用）。</summary>
        public int MatchSeed { get; private set; }

        public MatchSnapshot Previous { get; private set; }

        public MatchSnapshot Current { get; private set; }

        /// <summary>Previous → Current の補間係数（0〜1）。</summary>
        public float Alpha { get; private set; }

        /// <summary>直前の <see cref="Advance"/> で起きた出来事。</summary>
        public IReadOnlyList<MatchEvent> FrameEvents => _frameEvents;

        /// <summary>決定操作。タイトルなら試合開始、結果画面なら再戦。試合中は無視する。</summary>
        public void Submit()
        {
            switch (Screen)
            {
                case AppScreen.Title:
                    StartNewMatch();
                    break;
                case AppScreen.Result:
                    Match!.Rematch();
                    BeginPlaying();
                    break;
                case AppScreen.Playing:
                    break;
            }
        }

        /// <summary>戻る操作。試合を破棄してタイトルへ。</summary>
        public void Back()
        {
            Screen = AppScreen.Title;
            Match = null;
            _cpu = null;
            _frameEvents.Clear();
            Previous = default;
            Current = default;
        }

        /// <summary>実時間を進め、溜まった分だけ固定刻みで試合を進める。入力は -1（下）〜 +1（上）。</summary>
        public void Advance(double deltaSeconds, float playerInput)
        {
            _frameEvents.Clear();
            if (Screen != AppScreen.Playing || !(deltaSeconds > 0d))
            {
                return;
            }

            var match = Match!;
            var tickDuration = (double)_config.TickDuration;
            _accumulator += deltaSeconds;
            for (var ticks = 0; ticks < MaxTicksPerFrame && _accumulator >= tickDuration; ticks++)
            {
                _accumulator -= tickDuration;
                Previous = Current;
                match.Tick(playerInput, _cpu!.Decide(match));
                Current = match.Snapshot;
                _frameEvents.AddRange(match.Events);
                if (match.Phase == MatchPhase.Finished)
                {
                    Screen = AppScreen.Result;
                    _accumulator = 0d;
                    break;
                }
            }

            if (_accumulator > tickDuration)
            {
                _accumulator = tickDuration;
            }

            Alpha = (float)(_accumulator / tickDuration);
        }

        private void StartNewMatch()
        {
            MatchSeed = _nextSeed++;
            Match = new MatchState(_config, MatchSeed);
            _cpu = new CpuController(_cpuConfig, Side.Right, ~MatchSeed);
            BeginPlaying();
        }

        private void BeginPlaying()
        {
            Screen = AppScreen.Playing;
            _accumulator = 0d;
            Alpha = 0f;
            Current = Match!.Snapshot;
            Previous = Current;
        }
    }
}
