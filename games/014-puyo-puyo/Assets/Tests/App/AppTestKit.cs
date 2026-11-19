using Game.Core;

namespace Game.App.Tests
{
    internal static class AppTestKit
    {
        public const int RepeatDelay = 10;
        public const int RepeatInterval = 2;
        public const double TickSeconds = 1.0 / SporeConfig.TickRate;

        public static readonly Pair EmberPair = new(Cell.Ember, Cell.Ember);

        /// <summary>CPU がほとんど動かない設定の画面遷移。</summary>
        public static VersusFlow NewFlow(int seed = 100) => new(new SporeConfig(), 3600, 0, RepeatDelay, RepeatInterval, seed);

        /// <summary>次の1組でプレイヤーが負ける試合を始める。</summary>
        public static VersusFlow FlowAboutToLose()
        {
            var flow = NewFlow();
            flow.MatchFactory = seed => DemoMatches.PlayerAboutToLose(new SporeConfig(), seed);
            flow.Submit();
            return flow;
        }

        /// <summary>結果画面まで進める。</summary>
        public static void PlayToResult(VersusFlow flow)
        {
            flow.Perform(GameCommand.SoftDropOn);
            for (var i = 0; i < 2000 && flow.Screen == AppScreen.Playing; i++) { flow.StepTick(); }
        }
    }
}
