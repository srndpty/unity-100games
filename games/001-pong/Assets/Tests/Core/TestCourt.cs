using System;

namespace Game.Core.Tests
{
    /// <summary>テスト用の試合進行ヘルパー。</summary>
    internal static class TestCourt
    {
        public const int Seed = 12345;

        /// <summary>CPU 同士でも数分で決着する上限。これを超えたら進行が止まっているとみなす。</summary>
        public const int MaxTicksPerMatch = 60 * 60 * 30;

        public static MatchState NewMatch(PongConfig? config = null) => new MatchState(config ?? new PongConfig(), Seed);

        /// <summary>サーブ待ちを抜けてボールが動き出すまで進める。</summary>
        public static void SkipServe(MatchState match)
        {
            for (var i = 0; i < match.Config.ServeDelayTicks && match.Phase == MatchPhase.Serving; i++)
            {
                match.Tick(0f, 0f);
            }
        }

        /// <summary>決着まで進め、要した Tick 数を返す。</summary>
        public static int PlayToEnd(MatchState match, Func<MatchState, float> left, Func<MatchState, float> right)
        {
            for (var tick = 1; tick <= MaxTicksPerMatch; tick++)
            {
                match.Tick(left(match), right(match));
                if (match.Phase == MatchPhase.Finished)
                {
                    return tick;
                }
            }

            throw new InvalidOperationException("試合が決着しませんでした");
        }
    }
}
