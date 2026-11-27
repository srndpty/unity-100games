using System;
using System.Collections.Generic;
using Game.Core.Mechanisms;
using Game.Core.Play;
using Game.Core.Rooms;

namespace Game.Core.Analysis
{
    /// <summary>探索の 1 手：近景へ移る命令の列と、近景での 1 つ以上の命令（仕掛けを解く手は操作の列）。</summary>
    public sealed class Move
    {
        internal Move(int view, IReadOnlyList<Command> commands, string label)
        {
            View = view;
            Commands = commands;
            Label = label;
        }

        /// <summary>命令を出す近景。</summary>
        public int View { get; }

        public IReadOnlyList<Command> Commands { get; }
        public string Label { get; }

        /// <summary>世界に当てはめる（近景へ移ってから命令を出す）。どれかを受け付けなければ false。</summary>
        public bool ApplyTo(World world)
        {
            if (world == null) { throw new ArgumentNullException(nameof(world)); }
            foreach (var c in Navigation.PathTo(world, View))
            {
                if (!world.Apply(c).Accepted) { return false; }
            }

            foreach (var c in Commands)
            {
                if (!world.Apply(c).Accepted) { return false; }
            }

            return true;
        }
    }

    /// <summary>探索の結果。</summary>
    public sealed class StateSpaceReport
    {
        internal StateSpaceReport(int states, int transitions, IReadOnlyList<string> errors, IReadOnlyList<World> worlds)
        {
            States = states;
            Transitions = transitions;
            Errors = errors;
            Worlds = worlds;
        }

        /// <summary>到達できる抽象状態の数。</summary>
        public int States { get; }

        public int Transitions { get; }
        public IReadOnlyList<string> Errors { get; }

        /// <summary>抽象状態ごとの代表の世界（テストが全ての到達状態で性質を確かめる）。</summary>
        public IReadOnlyList<World> Worlds { get; }
    }

    /// <summary>
    /// 状態空間の探索。各近景で、全ての部品 × 命令の種類 × 持ち物の候補を遊ぶときと同じ <see cref="World.Check"/> で絞り、
    /// 視点を移す命令を含めて当てはめる。仕掛けの値は抽象状態では「解けたか」に縮めるので、1 段ずつの操作に加えて
    /// 「今の値から答えまで動かす」手も加える。抽象状態を幅優先にたどり、迂回・詰み・持ち物の違反を拒否する。
    /// さらに、論理的に受け付ける命令（<see cref="World.Check"/>）と、画面でクリックして作れる命令（<see cref="World.CommandAt"/>）を
    /// 分けて扱う：近景へ寄る命令とクリックで出す命令は、決めた画面の範囲（<see cref="ScreenRegion"/>）の中に
    /// その命令になる点が見つかったときだけ手にし、受け付けるのに点が見つからない命令は誤りにする（fail-closed）。
    /// </summary>
    public static class StateSpace
    {
        public static StateSpaceReport Explore(Room room, int maxStates) => Explore(room, maxStates, ScreenRegion.Guaranteed);

        public static StateSpaceReport Explore(Room room, int maxStates, ScreenRegion region)
        {
            if (room == null) { throw new ArgumentNullException(nameof(room)); }
            var def = room.Def;
            var errors = new List<string>();
            var unclickable = new HashSet<string>(StringComparer.Ordinal);
            var start = new World(room);
            var worlds = new List<World> { start };
            var parent = new List<int> { -1 };
            var via = new List<string> { "" };
            var index = new Dictionary<string, int>(StringComparer.Ordinal) { [start.AbstractKey()] = 0 };
            var reverse = new List<List<int>> { new() };
            int transitions = 0;
            var aborted = false;
            CheckInvariants(def, start, "開始直後", errors);

            for (int s = 0; s < worlds.Count && !aborted; s++)
            {
                var w = worlds[s];
                if (w.Escaped) { continue; }
                var found = new List<string>();
                foreach (var move in Moves(w, region, found))
                {
                    var next = w.Clone();
                    try
                    {
                        if (!move.ApplyTo(next))
                        {
                            errors.Add($"受け付けた候補が当てはまりません：{move.Label}（{PathText(parent, via, s)}）");
                            continue;
                        }
                    }
                    catch (InvalidOperationException e)
                    {
                        errors.Add($"{e.Message}（{move.Label}、{PathText(parent, via, s)}）");
                        continue;
                    }

                    transitions++;
                    var key = next.AbstractKey();
                    if (!index.TryGetValue(key, out var to))
                    {
                        if (worlds.Count >= maxStates)
                        {
                            errors.Add($"状態の数が上限 {maxStates} を超えました");
                            aborted = true;
                            break;
                        }

                        to = worlds.Count;
                        index[key] = to;
                        worlds.Add(next);
                        parent.Add(s);
                        via.Add(move.Label);
                        reverse.Add(new List<int>());
                        CheckInvariants(def, next, PathText(parent, via, to), errors);
                    }

                    reverse[to].Add(s);
                }

                foreach (var f in found)
                {
                    if (unclickable.Add(f)) { errors.Add($"クリックできない：{f} は受け付けるのに、画面の範囲 {region} にその命令になる点がありません（{PathText(parent, via, s)}）"); }
                }
            }

            if (!aborted) { CheckDeadEnds(worlds, parent, via, reverse, errors); }
            return new StateSpaceReport(worlds.Count, transitions, errors.AsReadOnly(), worlds.AsReadOnly());
        }

        /// <summary>今の状態で取れる全ての手（遊ぶときに作れる命令と同じ集合を World.Check で絞る。クリックの可否は見ない）。</summary>
        public static IEnumerable<Move> Moves(World world) => Moves(world, null, null);

        /// <summary>
        /// 今の状態で取れる全ての手。region を渡すと、寄る命令とクリックで出す命令はその範囲で点が見つかるものだけにし、
        /// 受け付けるのに点がない命令を unclickable に「近景:命令」の形で足す。
        /// </summary>
        public static IEnumerable<Move> Moves(World world, ScreenRegion? region, List<string>? unclickable)
        {
            if (world == null) { throw new ArgumentNullException(nameof(world)); }
            var def = world.Def;
            if (world.Escaped) { yield break; }
            foreach (var view in def.Views)
            {
                if (view.Kind != ViewKind.CloseUp) { continue; }
                var at = world.Clone();
                var entered = true;
                foreach (var c in Navigation.PathTo(at, view.Index))
                {
                    if (region != null && ClickSearch.NeedsClick(c) && ClickSearch.PointFor(at, c, ClickSearch.SelectedFor(c), region.Value) == null)
                    {
                        unclickable?.Add($"{def.Views[at.View].Id}:focus {view.Id}");
                        entered = false;
                        break;
                    }

                    if (!at.Apply(c).Accepted) { throw new InvalidOperationException($"近景 {view.Id} へ移れません：{c}"); }
                }

                if (!entered) { continue; }
                foreach (var part in def.Parts)
                {
                    var candidates = new List<Command> { Command.Operate(part.Index), Command.Take(part.Index), Command.Inspect(part.Index) };
                    foreach (var item in world.Inventory) { candidates.Add(Command.UseItem(item, part.Index)); }
                    foreach (var c in candidates)
                    {
                        if (!at.Check(c).Accepted) { continue; }
                        if (region != null && ClickSearch.PointFor(at, c, ClickSearch.SelectedFor(c), region.Value) == null)
                        {
                            unclickable?.Add($"{view.Id}:{Describe(def, c)}");
                            continue;
                        }

                        yield return new Move(view.Index, new[] { c }, $"{view.Id}:{Describe(def, c)}");
                    }
                }

                // 値を縮めた抽象状態のための「答えまで動かす」手（1 段ずつの操作の列そのもの。各段もクリックできるかを確かめる）。
                foreach (var m in def.Mechanisms)
                {
                    if (def.ViewOf(m.Index) != view.Index || at.PhaseOf(m.Index) != Phase.Active) { continue; }
                    var commands = new List<Command>();
                    var sim = at.Clone();
                    var clickable = true;
                    foreach (var slot in at.Room.Mechanisms[m.Index].SolutionFrom(at.ValuesOf(m.Index)))
                    {
                        var c = Command.Operate(def.SlotPart(m.Index, slot));
                        if (region != null && ClickSearch.PointFor(sim, c, -1, region.Value) == null)
                        {
                            unclickable?.Add($"{view.Id}:{Describe(def, c)}");
                            clickable = false;
                            break;
                        }

                        sim.Apply(c);
                        commands.Add(c);
                    }

                    if (clickable) { yield return new Move(view.Index, commands.AsReadOnly(), $"{view.Id}:solve {m.Id}"); }
                }
            }
        }

        private static void CheckInvariants(RoomDef def, World w, string path, List<string> errors)
        {
            var solvedAll = true;
            foreach (var m in def.Mechanisms)
            {
                if (m.Index != def.Exit && !w.IsSolved(m.Index)) { solvedAll = false; }

                // 仕掛けの前提（持ち物）が揃う前に解けていない・操作できない。
                if (m.Item >= 0 && w.PhaseOf(m.Index) != Phase.Locked && (!w.Obtained(m.Item) || w.Has(m.Item)))
                {
                    errors.Add($"迂回：{m.Id} が {m.Item} の持ち物を使わずに {w.PhaseOf(m.Index)} になりました（{path}）");
                }

                if (m.Item >= 0 && w.PhaseOf(m.Index) == Phase.Locked && w.Obtained(m.Item) && !w.Has(m.Item))
                {
                    errors.Add($"持ち物の違反：{def.Items[m.Item].Id} が使われずに消えました（{path}）");
                }
            }

            if (w.Escaped && !solvedAll) { errors.Add($"迂回：解けていない仕掛けがあるのに脱出しました（{path}）"); }
            if (w.IsSolved(def.Exit) != w.Escaped) { errors.Add($"出口の状態と脱出が食い違います（{path}）"); }

            var seen = new HashSet<int>();
            foreach (var i in w.Inventory)
            {
                if (!seen.Add(i)) { errors.Add($"持ち物の違反：{def.Items[i].Id} が二重にあります（{path}）"); }
                if (!w.Obtained(i)) { errors.Add($"持ち物の違反：拾っていない {def.Items[i].Id} を持っています（{path}）"); }
            }

            foreach (var item in def.Items)
            {
                var container = def.Parts[def.PickupOf(item.Index)].Container;
                if (w.Obtained(item.Index) && container >= 0 && !w.IsSolved(container))
                {
                    errors.Add($"迂回：閉じた {def.Mechanisms[container].Id} の中の {item.Id} を拾いました（{path}）");
                }
            }
        }

        private static void CheckDeadEnds(List<World> worlds, List<int> parent, List<string> via, List<List<int>> reverse, List<string> errors)
        {
            var canEscape = new bool[worlds.Count];
            var queue = new Queue<int>();
            for (int i = 0; i < worlds.Count; i++)
            {
                if (!worlds[i].Escaped) { continue; }
                canEscape[i] = true;
                queue.Enqueue(i);
            }

            if (queue.Count == 0)
            {
                errors.Add("どの手順でも脱出に到達しません");
                return;
            }

            while (queue.Count > 0)
            {
                foreach (var p in reverse[queue.Dequeue()])
                {
                    if (canEscape[p]) { continue; }
                    canEscape[p] = true;
                    queue.Enqueue(p);
                }
            }

            for (int i = 0; i < worlds.Count; i++)
            {
                if (!canEscape[i]) { errors.Add($"詰み：{PathText(parent, via, i)} の後は脱出できません"); }
            }
        }

        private static string Describe(RoomDef def, Command c) => c.Kind switch
        {
            CommandKind.UseItem => $"use {def.Items[c.A].Id} on {def.Parts[c.B].Id}",
            _ => $"{c.Kind.ToString().ToLowerInvariant()} {def.Parts[c.A].Id}",
        };

        private static string PathText(List<int> parent, List<string> via, int state)
        {
            var steps = new List<string>();
            for (int s = state; parent[s] >= 0; s = parent[s]) { steps.Add(via[s]); }
            if (steps.Count == 0) { return "開始直後"; }
            steps.Reverse();
            return string.Join(" → ", steps);
        }
    }
}
