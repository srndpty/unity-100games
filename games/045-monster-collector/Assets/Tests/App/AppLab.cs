using System;
using System.IO;
using System.Runtime.CompilerServices;
using Game.Core;

namespace Game.App.Tests
{
    /// <summary>App のテストの道具。データは作品の Assets/Game/Data から読む。</summary>
    internal static class AppLab
    {
        private static MonsterData? _data;

        public static string DataText(string name, [CallerFilePath] string source = "")
        {
            var dir = Path.GetDirectoryName(source) ?? ".";
            return File.ReadAllText(Path.GetFullPath(Path.Combine(dir, "..", "..", "Game", "Data", name)));
        }

        public static MonsterData Data => _data ??= MonsterData.Load(
            DataText("Types.txt"),
            DataText("Statuses.txt"),
            DataText("Moves.txt"),
            DataText("Species.txt"),
            DataText("World.txt"));

        public static KeeperFlow Flow(int seed = 1, MemorySaveStore? save = null, MemorySaveStore? records = null) =>
            new(Data, save ?? new MemorySaveStore(), records ?? new MemorySaveStore(), seed);

        /// <summary>操縦役で条件を満たすまで進める（1 Tick に 1 回押す）。上限を超えたら例外。</summary>
        public static int RunUntil(KeeperFlow flow, Autopilot pilot, Func<KeeperFlow, bool> done, int maxTicks)
        {
            for (int t = 0; t < maxTicks; t++)
            {
                if (done(flow)) { return t; }
                pilot.Step(flow);
                flow.StepTicks(1);
            }

            throw new TimeoutException($"{maxTicks} Tick で終わりません（画面 {flow.Screen}、戦闘 {flow.World?.Battle != null}、Lv {Levels(flow)}、倒したトレーナー {flow.World?.TrainersDefeated}）。");
        }

        public static string Levels(KeeperFlow flow)
        {
            if (flow.World == null) { return "-"; }
            var parts = new System.Collections.Generic.List<string>();
            foreach (var m in flow.World.Roster.Party) { parts.Add($"{m.Species.Id}{m.Level}"); }
            return string.Join(",", parts);
        }

        /// <summary>はじめからの直後（最初の 1 匹を選んでフィールドにいる）まで進める。</summary>
        public static KeeperFlow FieldReady(int seed = 1, MemorySaveStore? save = null)
        {
            var flow = Flow(seed, save);
            var pilot = new Autopilot();
            RunUntil(flow, pilot, f => f.Screen == FlowScreen.Field && !f.InDialogue, 2000);
            return flow;
        }
    }
}
