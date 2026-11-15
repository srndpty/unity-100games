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
    /// 画面遷移（タイトル → プレイ → 結果 → 再戦）と、実時間から固定刻みの Tick への変換を統括する。
    /// 1 ゲーム = 1 シード。ゲームを始めるたび（再戦を含む）に次のシードへ進む。
    /// </summary>
    public sealed class BreakoutFlow
    {
        /// <summary>1 フレームで処理する Tick の上限。処理落ちで追いつけないときは時間を切り捨てる。</summary>
        public const int MaxTicksPerFrame = 5;

        private readonly BreakoutConfig _config;
        private readonly List<GameEvent> _frameEvents = new();
        private int _nextSeed;
        private double _accumulator;
        private bool _launchRequested;

        public BreakoutFlow(BreakoutConfig config, int seed)
        {
            config.Validate();
            _config = config;
            _nextSeed = seed;
        }

        public AppScreen Screen { get; private set; } = AppScreen.Title;

        /// <summary>現在のゲーム。タイトル画面では null。</summary>
        public BreakoutState? Game { get; private set; }

        /// <summary>現在のゲームのシード。このシードと入力列を <see cref="BreakoutState"/> に与えれば同じゲームを再現できる。</summary>
        public int GameSeed { get; private set; }

        public BreakoutSnapshot Previous { get; private set; }

        public BreakoutSnapshot Current { get; private set; }

        /// <summary>Previous → Current の補間係数（0〜1）。</summary>
        public float Alpha { get; private set; }

        /// <summary>直前の <see cref="Advance"/> で起きた出来事。</summary>
        public IReadOnlyList<GameEvent> FrameEvents => _frameEvents;

        /// <summary>
        /// 決定操作。タイトルと結果画面では新しいゲームを始める。プレイ中は発射の要求として、次に進む Tick に渡す
        /// （そのフレームに Tick が進まなくても取りこぼさない）。発射を待っていなければ Core が無視する。
        /// </summary>
        public void Submit()
        {
            if (Screen == AppScreen.Playing)
            {
                _launchRequested = true;
                return;
            }

            StartNewGame();
        }

        /// <summary>戻る操作。ゲームを破棄してタイトルへ。</summary>
        public void Back()
        {
            Screen = AppScreen.Title;
            Game = null;
            _frameEvents.Clear();
            _launchRequested = false;
            Previous = default;
            Current = default;
        }

        /// <summary>実時間を進め、溜まった分だけ固定刻みでゲームを進める。<paramref name="move"/> は -1（左）〜 +1（右）。</summary>
        public void Advance(double deltaSeconds, float move)
        {
            _frameEvents.Clear();
            if (Screen != AppScreen.Playing || !(deltaSeconds > 0d))
            {
                return;
            }

            var game = Game!;
            var tickDuration = (double)_config.TickDuration;
            _accumulator += deltaSeconds;
            for (var ticks = 0; ticks < MaxTicksPerFrame && _accumulator >= tickDuration; ticks++)
            {
                _accumulator -= tickDuration;
                Previous = Current;
                game.Tick(move, _launchRequested);
                _launchRequested = false;
                Current = game.Snapshot;
                _frameEvents.AddRange(game.Events);
                if (game.IsOver)
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

        private void StartNewGame()
        {
            GameSeed = _nextSeed++;
            Game = new BreakoutState(_config, GameSeed);
            Screen = AppScreen.Playing;
            _accumulator = 0d;
            _launchRequested = false;
            Alpha = 0f;
            Current = Game.Snapshot;
            Previous = Current;
        }
    }
}
