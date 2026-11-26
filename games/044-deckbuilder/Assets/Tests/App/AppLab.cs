using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using Game.Core;

namespace Game.App.Tests
{
    /// <summary>App のテストの道具。</summary>
    internal static class AppLab
    {
        private static GameData? _data;

        public static string DataText(string name, [CallerFilePath] string source = "")
        {
            var dir = Path.GetDirectoryName(source) ?? ".";
            return File.ReadAllText(Path.GetFullPath(Path.Combine(dir, "..", "..", "Game", "Data", name)));
        }

        public static GameData Data => _data ??= GameData.Load(DataText("Cards.txt"), DataText("Enemies.txt"), DataText("Encounters.txt"), new DeckConfig());

        public static DeckFlow NewFlow(int firstSeed = 1) => new(Data, new DeckConfig(), new PlaybackTiming(), firstSeed);

        /// <summary>タイトルから始めて、ルートの選択肢 option の戦闘に入り、最初の演出を終えた Flow。</summary>
        public static DeckFlow InBattle(int option = 0, int firstSeed = 1)
        {
            var flow = NewFlow(firstSeed);
            flow.Apply(new Intent(IntentKind.Confirm));
            flow.Apply(new Intent(IntentKind.Option, option));
            flow.FinishPlayback();
            return flow;
        }

        /// <summary>手札に打ち込みと構えの両方がある戦闘（シードを順に試す）。</summary>
        public static DeckFlow InBattleWithStrikeAndGuard()
        {
            for (int seed = 1; seed < 100; seed++)
            {
                var flow = InBattle(0, seed);
                var hand = flow.Run!.Battle!.Hand;
                if (hand.Any(c => c.Def.Id == "strike") && hand.Any(c => c.Def.Id == "guard")) { return flow; }
            }

            throw new System.InvalidOperationException("見つかりません。");
        }

        public static CardRef InHand(DeckFlow flow, string id) => flow.Run!.Battle!.Hand.First(c => c.Def.Id == id);

        /// <summary>操縦役で結果の画面まで進める。演出は毎回飛ばす。</summary>
        public static void Drive(DeckFlow flow, IPilot pilot, int maxSteps = 20000)
        {
            for (int i = 0; i < maxSteps; i++)
            {
                if (flow.Screen == AppScreen.Result) { return; }
                flow.FinishPlayback();
                var intent = pilot.Decide(flow);
                if (intent == null)
                {
                    flow.Step();
                    continue;
                }

                flow.Apply(intent.Value);
            }

            throw new System.InvalidOperationException("結果の画面に着きませんでした。");
        }
    }
}
