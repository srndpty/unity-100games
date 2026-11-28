using Game.Core;
using NUnit.Framework;

namespace Game.App.Tests
{
    /// <summary>App テストの共通：速く終わる探索の設定と、決まった局面から始める Flow。</summary>
    internal static class AppLab
    {
        public static MctsConfig FastMcts() => new()
        {
            WeakPlayouts = 60,
            NormalPlayouts = 80,
            StrongPlayouts = 100,
            OwnershipPlayouts = 100,
        };

        public static FlowSettings FastSettings() => new()
        {
            CpuThinkTicks = 2,
            PlayoutsPerTick = 50,
            PlaceAnimTicks = 4,
        };

        // 黒は E 列、白は F 列の壁。黒の面積 45・白 36。
        public const string Walls = @"
            . . . . X O . . .
            . . . . X O . . .
            . . . . X O . . .
            . . . . X O . . .
            . . . . X O . . .
            . . . . X O . . .
            . . . . X O . . .
            . . . . X O . . .
            . . . . X O . . .";

        public static GoFlow Flow(GoConfig? config = null, MctsConfig? mcts = null, FlowSettings? settings = null, int seed = 42) =>
            new(config ?? new GoConfig(), mcts ?? FastMcts(), seed, settings ?? FastSettings());

        /// <summary>局面を文字列から作る Flow。</summary>
        public static GoFlow FromText(string text, GoConfig? config = null, MctsConfig? mcts = null, FlowSettings? settings = null)
        {
            config ??= new GoConfig();
            var flow = Flow(config, mcts, settings);
            flow.GameFactory = _ => new GoGame(config, BoardText.Parse(text), Stone.Black);
            return flow;
        }

        /// <summary>タイトルから対局を始める。</summary>
        public static void Start(GoFlow flow)
        {
            Assert.That(flow.Screen, Is.EqualTo(FlowScreen.Title));
            flow.Press(InputButton.Confirm);
            flow.FlushIntents();
            Assert.That(flow.Screen, Is.EqualTo(FlowScreen.Playing));
        }

        /// <summary>死石の推定を Tick で回し終える。</summary>
        public static void FinishEstimate(GoFlow flow, int limit = 10_000)
        {
            for (var i = 0; i < limit && flow.IsEstimating; i++) { flow.StepTicks(1); }
            Assert.That(flow.IsEstimating, Is.False, "推定が終わらない");
        }

        /// <summary>CPU が打つ（またはプレイヤーの手番になる・対局が終わる）まで Tick を進める。</summary>
        public static void UntilPlayerTurn(GoFlow flow, int limit = 2000)
        {
            for (var i = 0; i < limit; i++)
            {
                if (flow.Screen != FlowScreen.Playing || flow.IsPlayerTurn) { return; }
                flow.StepTicks(1);
            }

            Assert.Fail("CPU が打たない");
        }
    }
}
