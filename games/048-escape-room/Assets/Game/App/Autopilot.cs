using System;
using System.Collections.Generic;
using Game.Core.Geometry;
using Game.Core.Play;
using Game.Core.Rooms;

namespace Game.App
{
    /// <summary>
    /// 操縦役：ヒントと同じ「次の一手」（今のシードの答え）を、遊ぶときと同じ入力（向き直りのキー・画面の点のクリック・持ち物の枠）で行う。
    /// クリックする点は、部品の手前の面の中心を画面へ写し、そこが狙った命令にならなければ画面を刻んで探す。
    /// 撮影の台本・App のテスト・PlayMode のテストで共有する（002）。
    /// </summary>
    public sealed class Autopilot
    {
        private const int ScanStep = 20;

        private readonly Queue<Command> _plan = new();

        /// <summary>行った命令の数。</summary>
        public int Issued { get; private set; }

        /// <summary>
        /// 1 回分の判断。遊ぶ画面で次のクリックを受け付けるなら、計画の次の命令を入力にして当てはめる。
        /// 命令を世界が受け付けなかったら、手順が規則と合っていないので例外にする。
        /// </summary>
        public void Drive(EscapeFlow flow)
        {
            if (flow == null) { throw new ArgumentNullException(nameof(flow)); }
            if (!flow.IsIdle) { return; }
            var w = flow.Snapshot()!;
            if (_plan.Count == 0 || !w.Check(_plan.Peek()).Accepted) { Plan(w); }
            var command = _plan.Dequeue();
            Issue(flow, w, command);
            flow.FlushIntents();
            if (!flow.LastOutcome.Accepted) { throw new InvalidOperationException($"{command} を {flow.LastOutcome.Reason} で受け付けませんでした"); }
            Issued++;
        }

        /// <summary>Tick を 1 つずつ進めながら、結果画面になるまで動かす。<paramref name="maxTicks"/> 回の反復で届かなければ例外。</summary>
        public void RunToResult(EscapeFlow flow, int maxTicks = 20000)
        {
            if (flow == null) { throw new ArgumentNullException(nameof(flow)); }
            for (int i = 0; i < maxTicks; i++)
            {
                if (flow.Screen == FlowScreen.Result) { return; }
                Drive(flow);
                flow.StepTicks(1);
            }

            // 最後の反復で結果画面になった場合も成功（ループの先頭だけで調べると、上限ちょうどで届いたのに失敗になる）。
            if (flow.Screen == FlowScreen.Result) { return; }
            throw new InvalidOperationException($"{maxTicks} Tick で結果画面に届きません");
        }

        /// <summary>
        /// 世界の今の視点で、クリックすると狙った命令になる画面の点。部品の手前の面の中心を写した点から試し、
        /// だめなら画面を刻んで探す。見つからなければ null。
        /// </summary>
        public static (int X, int Y)? ClickPointFor(World world, Command command, int selectedItem)
        {
            if (world == null) { throw new ArgumentNullException(nameof(world)); }
            var def = world.Def;
            var pose = def.Views[world.View].Pose;
            foreach (var part in TargetParts(def, command))
            {
                if (!world.IsPresent(part.Index) || !pose.TryProject(part.Box.FrontCenter(pose.Wall), out var x, out var y)) { continue; }
                if (world.CommandAt(x, y, selectedItem) == command) { return (x, y); }
            }

            for (int sy = -CameraPose.HalfHeight + 10; sy < CameraPose.HalfHeight; sy += ScanStep)
            {
                for (int sx = -1760; sx <= 1760; sx += ScanStep)
                {
                    if (world.CommandAt(sx, sy, selectedItem) == command) { return (sx, sy); }
                }
            }

            return null;
        }

        private void Plan(World w)
        {
            _plan.Clear();
            var step = HintEngine.Next(w);
            var at = w.Clone();
            foreach (var c in Navigation.PathTo(at, step.View(at.Def)))
            {
                _plan.Enqueue(c);
                at.Apply(c);
            }

            foreach (var c in step.Answer(at)) { _plan.Enqueue(c); }
        }

        private static void Issue(EscapeFlow flow, World w, Command command)
        {
            switch (command.Kind)
            {
                case CommandKind.Turn:
                    flow.Press(IntentKind.Turn, command.A);
                    return;
                case CommandKind.Back:
                    flow.Press(IntentKind.Back);
                    return;
                case CommandKind.Hint:
                    flow.Press(IntentKind.Hint);
                    return;
                case CommandKind.UseItem:
                    {
                        // 持ち物の枠を選んでから部品をクリックする。
                        var slot = IndexOf(w.Inventory, command.A);
                        if (flow.SelectedItem != command.A) { flow.Click(ClickTarget.Item, slot); }
                        Click(flow, w, command, command.A);
                        return;
                    }

                default:
                    Click(flow, w, command, -1);
                    return;
            }
        }

        private static void Click(EscapeFlow flow, World w, Command command, int selectedItem)
        {
            var point = ClickPointFor(w, command, selectedItem) ?? throw new InvalidOperationException($"{command} になる画面の点がありません（視点 {w.Def.Views[w.View].Id}）");
            flow.ClickWorld(point.X, point.Y);
        }

        private static IEnumerable<PartDef> TargetParts(RoomDef def, Command command)
        {
            switch (command.Kind)
            {
                case CommandKind.Focus:
                    foreach (var p in def.Parts)
                    {
                        if (p.View == command.A) { yield return p; }
                    }

                    break;
                case CommandKind.UseItem:
                    yield return def.Parts[command.B];
                    break;
                case CommandKind.Operate:
                case CommandKind.Take:
                case CommandKind.Inspect:
                    yield return def.Parts[command.A];
                    break;
                default:
                    break;
            }
        }

        private static int IndexOf(IReadOnlyList<int> list, int value)
        {
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i] == value) { return i; }
            }

            throw new InvalidOperationException($"持ち物 {value} を持っていません");
        }
    }
}
