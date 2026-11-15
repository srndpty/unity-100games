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
        private SeededMatch? _session;
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
        public MatchState? Match => _session?.State;

        /// <summary>
        /// 現在の試合のシード。<see cref="SeededMatch"/> にこのシードとプレイヤー入力列を与えれば同じ試合を再現できる。
        /// 試合を始めるたび（再戦を含む）に次のシードへ進む。
        /// </summary>
        public int MatchSeed => _session?.Seed ?? 0;

        public MatchSnapshot Previous { get; private set; }

        public MatchSnapshot Current { get; private set; }

        /// <summary>Previous → Current の補間係数（0〜1）。</summary>
        public float Alpha { get; private set; }

        /// <summary>直前の <see cref="Advance"/> で起きた出来事。</summary>
        public IReadOnlyList<MatchEvent> FrameEvents => _frameEvents;

        /// <summary>決定操作。タイトルなら試合開始、結果画面なら再戦（新しいシードの独立した試合）。試合中は無視する。</summary>
        public void Submit()
        {
            if (Screen != AppScreen.Playing)
            {
                StartNewMatch();
            }
        }

        /// <summary>戻る操作。試合を破棄してタイトルへ。</summary>
        public void Back()
        {
            Screen = AppScreen.Title;
            _session = null;
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

            var session = _session!;
            var match = session.State;
            var tickDuration = (double)_config.TickDuration;
            _accumulator += deltaSeconds;
            for (var ticks = 0; ticks < MaxTicksPerFrame && _accumulator >= tickDuration; ticks++)
            {
                _accumulator -= tickDuration;
                Previous = Current;
                session.Tick(playerInput);
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
            _session = new SeededMatch(_config, _cpuConfig, _nextSeed++);
            Screen = AppScreen.Playing;
            _accumulator = 0d;
            Alpha = 0f;
            Current = _session.State.Snapshot;
            Previous = Current;
        }
    }
}
