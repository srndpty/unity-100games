using System;
using System.Collections.Generic;
using Game.Core.Play;

namespace Game.App
{
    /// <summary>
    /// 操縦役：ヒントと同じ「次の一手」（今のシードの答え）を、遊ぶときと同じ入力（向き直りのキー・画面の点のクリック・持ち物の枠）で行う。
    /// クリックする点は <see cref="ClickSearch"/> で、Flow のクリックが届く範囲（<see cref="EscapeFlow.Region"/>）から探す。
    /// 1 つの命令ごとに、入力が実際にその命令として当てはまり、受け付けられたことを Flow の実行の記録で確かめる。
    /// 撮影の台本・App のテスト・PlayMode のテストで共有する（002）。
    /// </summary>
    public sealed class Autopilot
    {
        private readonly Queue<Command> _plan = new();

        /// <summary>行った命令の数。</summary>
        public int Issued { get; private set; }

        /// <summary>
        /// 1 回分の判断。遊ぶ画面で次のクリックを受け付けるなら、計画の次の命令を入力にして当てはめる。
        /// 命令が当てはまらなければ（未実行・拒否・別の命令）、手順か入力の変換が規則と合っていないので例外にする。
        /// </summary>
        public void Drive(EscapeFlow flow)
        {
            if (flow == null) { throw new ArgumentNullException(nameof(flow)); }
            if (!flow.IsIdle) { return; }
            var w = flow.Snapshot()!;
            if (_plan.Count == 0 || !w.Check(_plan.Peek()).Accepted) { Plan(w); }
            Perform(flow, _plan.Dequeue());
            Issued++;
        }

        /// <summary>
        /// 命令を 1 つ、遊ぶときと同じ入力（キー・画面の点のクリック・持ち物の枠・ドラッグ）にして当てはめる（撮影の台本も使う）。
        /// 持ち物の選択は先に命令に合わせる（使う命令でなければ外す）。入力の後、Flow がちょうど 1 つの命令を実行し、
        /// それが command で、受け付けられたことを確かめる。違えば例外。
        /// </summary>
        public static void Perform(EscapeFlow flow, Command command)
        {
            if (flow == null) { throw new ArgumentNullException(nameof(flow)); }
            if (flow.Snapshot() == null) { throw new InvalidOperationException("遊ぶ画面ではありません"); }
            PrepareSelection(flow, command);
            var before = flow.Executed;
            Issue(flow, flow.Snapshot()!, command);
            flow.FlushIntents();
            Verify(flow, command, before);
        }

        /// <summary>入力の後、Flow がちょうど 1 つの命令を実行し、それが command で、受け付けられたかを確かめる（PlayMode の入力でも使う）。</summary>
        public static void Verify(EscapeFlow flow, Command command, long executedBefore)
        {
            if (flow == null) { throw new ArgumentNullException(nameof(flow)); }
            var count = flow.Executed - executedBefore;
            if (count == 0) { throw new InvalidOperationException($"{command} の入力が命令として実行されませんでした（入力が捨てられた）"); }
            if (count != 1) { throw new InvalidOperationException($"{command} の入力で命令が {count} 個実行されました"); }
            if (flow.LastCommand != command) { throw new InvalidOperationException($"{command} のつもりの入力が {flow.LastCommand} として実行されました"); }
            if (!flow.LastOutcome.Accepted) { throw new InvalidOperationException($"{command} を {flow.LastOutcome.Reason} で受け付けませんでした"); }
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

        /// <summary>
        /// 持ち物の選択を命令に合わせる。使う命令なら目的の持ち物を選び、ほかの命令なら選択を外す
        /// （選んだままのクリックは「使う」になり、右クリックは選択を外すだけになるため）。選択の操作は命令を実行しないので、ここで状態を確かめる。
        /// 持ち物を手に取る命令は、枠を選んで同じ枠をもう一度クリックする入力なので、別の持ち物を選んでいれば外す。
        /// </summary>
        private static void PrepareSelection(EscapeFlow flow, Command command)
        {
            var want = command.Kind == CommandKind.UseItem ? command.A : -1;
            var keep = command.Kind == CommandKind.InspectItem && flow.SelectedItem == command.A;
            if (flow.SelectedItem != want && !keep && flow.SelectedItem >= 0)
            {
                flow.Press(IntentKind.Back);
                flow.FlushIntents();
                if (flow.SelectedItem >= 0) { throw new InvalidOperationException("持ち物の選択を外せません"); }
            }

            if (want >= 0 && flow.SelectedItem != want)
            {
                flow.Click(ClickTarget.Item, IndexOf(flow.Inventory, want));
                flow.FlushIntents();
                if (flow.SelectedItem != want) { throw new InvalidOperationException($"持ち物 {want} を選べません"); }
            }
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
                case CommandKind.Rotate:
                    {
                        // 右へドラッグするとヨーが負、上へドラッグするとピッチが正（EscapeFlow と同じ向き）。
                        var step = flow.DragStepUnits;
                        flow.Drag(-command.A * step, command.B * step);
                        return;
                    }

                case CommandKind.InspectItem:
                    {
                        // 枠を選んでもう一度クリックすると手に取る。
                        var slot = IndexOf(w.Inventory, command.A);
                        if (flow.SelectedItem != command.A) { flow.Click(ClickTarget.Item, slot); }
                        flow.Click(ClickTarget.Item, slot);
                        return;
                    }

                default:
                    {
                        var point = ClickSearch.PointFor(w, command, flow.SelectedItem, flow.Region)
                            ?? throw new InvalidOperationException($"{command} になる画面の点が範囲 {flow.Region} にありません（視点 {w.Def.Views[w.View].Id}）");
                        flow.ClickWorld(point.X, point.Y);
                        return;
                    }
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
