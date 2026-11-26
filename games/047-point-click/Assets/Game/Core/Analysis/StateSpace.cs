using System;
using System.Collections.Generic;
using System.Linq;
using Game.Core.Play;
using Game.Core.Rules;

namespace Game.Core.Analysis
{
    public enum MoveKind
    {
        Act,
        Ask,
        EndTalk,
    }

    /// <summary>探索の 1 手（行為・話題・会話を終える）。歩く位置は持たない。</summary>
    public readonly struct Move
    {
        private Move(MoveKind kind, Verb verb, Target target, int tool, int topic)
        {
            Kind = kind;
            Verb = verb;
            Target = target;
            Tool = tool;
            Topic = topic;
        }

        public MoveKind Kind { get; }
        public Verb Verb { get; }
        public Target Target { get; }
        public int Tool { get; }
        public int Topic { get; }

        public static Move Act(Verb verb, Target target, int tool = -1) => new(MoveKind.Act, verb, target, tool, -1);

        public static Move Ask(int topic) => new(MoveKind.Ask, Verb.Talk, Target.None, -1, topic);

        public static Move EndTalk() => new(MoveKind.EndTalk, Verb.Talk, Target.None, -1, -1);

        /// <summary>ルール表の書き方に近い表記（「use knife on dock.net」）。</summary>
        public string Describe(RuleBook book)
        {
            if (book == null) { throw new ArgumentNullException(nameof(book)); }
            switch (Kind)
            {
                case MoveKind.Ask:
                    {
                        var t = book.Topics[Topic];
                        return $"topic {book.Hotspots[t.Npc].Id} {t.Id}";
                    }

                case MoveKind.EndTalk:
                    return "bye";
                default:
                    {
                        var target = Target.Kind == TargetKind.Item ? book.Items[Target.Index].Id : book.Hotspots[Target.Index].Id;
                        var verb = Verb == Verb.Walk ? "go" : Verb.ToString().ToLowerInvariant();
                        return Tool >= 0 ? $"use {book.Items[Tool].Id} on {target}" : $"{verb} {target}";
                    }
            }
        }

        /// <summary>世界に歩かずに当てはめる（探索・操縦役）。</summary>
        internal void ApplyInstantly(World world)
        {
            switch (Kind)
            {
                case MoveKind.Ask:
                    world.Ask(Topic);
                    break;
                case MoveKind.EndTalk:
                    world.EndTalk();
                    break;
                default:
                    world.Perform(Verb, Target, Tool);
                    break;
            }
        }
    }

    /// <summary>探索の結果。</summary>
    public sealed class StateSpaceReport
    {
        internal StateSpaceReport(int states, int transitions, IReadOnlyList<RuleError> errors, IReadOnlyList<int> precedes)
        {
            States = states;
            Transitions = transitions;
            Errors = errors;
            Precedes = precedes;
        }

        /// <summary>到達できる抽象状態の数（上限を超えたら打ち切った数）。</summary>
        public int States { get; }

        public int Transitions { get; }
        public IReadOnlyList<RuleError> Errors { get; }

        /// <summary>謎ごとに「その謎が解けている到達状態では必ず解けている他の謎」のビット集合（規則から導いた依存）。</summary>
        public IReadOnlyList<int> Precedes { get; }
    }

    /// <summary>
    /// 状態空間の探索。開始から、今の状態で取れる全ての行為（今の部屋の見えている物 × 動詞 × 持ち物、持ち物どうし、
    /// 会話の話題）を歩かずに当てはめ、位置を除いた抽象状態を幅優先にたどる。ここから詰み・使われないルール・
    /// 曖昧なルール・持ち物の違反・解けない謎を拒否し、謎の依存（必ず先に解く謎）を導く。
    /// </summary>
    public static class StateSpace
    {
        public static StateSpaceReport Explore(RuleBook book, int maxStates)
        {
            if (book == null) { throw new ArgumentNullException(nameof(book)); }
            var errors = new List<RuleError>();
            var start = new World(book, new WorldSettings());
            var worlds = new List<World> { start };
            var parent = new List<int> { -1 };
            var via = new List<Move> { default };
            var index = new Dictionary<string, int>(StringComparer.Ordinal) { [start.AbstractKey()] = 0 };
            var reverse = new List<List<int>> { new() };
            var usedRules = new bool[book.Rules.Count];
            var usedTopics = new bool[book.Topics.Count];
            var reportedAmbiguity = new HashSet<long>();
            var reportedViolation = new HashSet<string>(StringComparer.Ordinal);
            int transitions = 0;
            bool aborted = false;

            for (int s = 0; s < worlds.Count && !aborted; s++)
            {
                var w = worlds[s];
                if (w.Escaped) { continue; }
                foreach (var move in Moves(w))
                {
                    if (move.Kind == MoveKind.Act)
                    {
                        if (w.CountMatches(move.Verb, move.Target, move.Tool, out var a, out var b) >= 2
                            && reportedAmbiguity.Add(((long)a!.Index << 32) | (uint)b!.Index))
                        {
                            errors.Add(new RuleError(b.Line, $"行 {a.Line} と同じ状況で当たります（{move.Describe(book)}、到達の手順：{PathText(book, parent, via, s)}）。条件で区別してください"));
                        }
                    }

                    var next = w.Clone();
                    try
                    {
                        move.ApplyInstantly(next);
                    }
                    catch (RuleViolationException e)
                    {
                        if (!reportedViolation.Add(e.Message)) { continue; }
                        errors.Add(new RuleError(e.Line, $"{StripLine(e.Message)}（{move.Describe(book)}、到達の手順：{PathText(book, parent, via, s)}）"));
                        continue;
                    }

                    if (next.LastRule != null) { usedRules[next.LastRule.Index] = true; }
                    if (next.LastTopic != null) { usedTopics[next.LastTopic.Index] = true; }
                    transitions++;
                    var key = next.AbstractKey();
                    if (!index.TryGetValue(key, out var to))
                    {
                        if (worlds.Count >= maxStates)
                        {
                            errors.Add(new RuleError(0, $"状態の数が上限 {maxStates} を超えました。フラグや持ち物の組み合わせを減らしてください"));
                            aborted = true;
                            break;
                        }

                        to = worlds.Count;
                        index[key] = to;
                        worlds.Add(next);
                        parent.Add(s);
                        via.Add(move);
                        reverse.Add(new List<int>());
                    }

                    reverse[to].Add(s);
                }
            }

            var precedes = new int[book.Puzzles.Count];
            if (aborted || errors.Count > 0)
            {
                return new StateSpaceReport(worlds.Count, transitions, errors.AsReadOnly(), Array.AsReadOnly(precedes));
            }

            CheckDeadEnds(book, worlds, parent, via, reverse, errors);
            CheckUsage(book, usedRules, usedTopics, errors);
            DerivePrecedence(book, worlds, precedes, errors);
            return new StateSpaceReport(worlds.Count, transitions, errors.AsReadOnly(), Array.AsReadOnly(precedes));
        }

        /// <summary>今の状態で取れる全ての手（遊ぶときに作れる命令と同じ集合）。</summary>
        public static IEnumerable<Move> Moves(World world)
        {
            if (world == null) { throw new ArgumentNullException(nameof(world)); }
            var book = world.Book;
            if (world.Escaped) { yield break; }
            if (world.TalkingTo >= 0)
            {
                foreach (var t in world.AvailableTopics()) { yield return Move.Ask(t); }
                yield return Move.EndTalk();
                yield break;
            }

            var held = world.Inventory.ToArray();
            foreach (var h in book.Hotspots)
            {
                if (h.Room != world.Room || !world.Visible(h.Index)) { continue; }
                var target = Target.Hotspot(h.Index);
                yield return Move.Act(Verb.Look, target);
                if (h.Kind == HotspotKind.Exit)
                {
                    yield return Move.Act(Verb.Walk, target);
                }
                else
                {
                    yield return Move.Act(Verb.Take, target);
                    yield return Move.Act(Verb.Use, target);
                    yield return Move.Act(Verb.Talk, target);
                }

                foreach (var item in held) { yield return Move.Act(Verb.Use, target, item); }
            }

            foreach (var item in held)
            {
                yield return Move.Act(Verb.Look, Target.Item(item));
                foreach (var other in held)
                {
                    if (other > item) { yield return Move.Act(Verb.Use, Target.Item(other), item); }
                }
            }
        }

        /// <summary>開始から手の列をたどった世界（歩かずに当てはめる）。</summary>
        public static World Replay(RuleBook book, IEnumerable<Move> moves)
        {
            if (moves == null) { throw new ArgumentNullException(nameof(moves)); }
            var w = new World(book, new WorldSettings());
            foreach (var m in moves) { m.ApplyInstantly(w); }
            return w;
        }

        private static void CheckDeadEnds(RuleBook book, List<World> worlds, List<int> parent, List<Move> via, List<List<int>> reverse, List<RuleError> errors)
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
                errors.Add(new RuleError(0, "どの手順でも脱出（@goal の謎を解く）に到達しません"));
                return;
            }

            while (queue.Count > 0)
            {
                var s = queue.Dequeue();
                foreach (var p in reverse[s])
                {
                    if (canEscape[p]) { continue; }
                    canEscape[p] = true;
                    queue.Enqueue(p);
                }
            }

            for (int i = 0; i < worlds.Count; i++)
            {
                if (canEscape[i]) { continue; }
                errors.Add(new RuleError(0, $"詰み：{PathText(book, parent, via, i)} の後は、どう進めても脱出できません"));
                return;
            }
        }

        private static void CheckUsage(RuleBook book, bool[] usedRules, bool[] usedTopics, List<RuleError> errors)
        {
            foreach (var r in book.Rules)
            {
                if (!usedRules[r.Index]) { errors.Add(new RuleError(r.Line, "このルールはどの到達状態でも選ばれません（条件が成り立たないか、前のルールに隠れています）")); }
            }

            foreach (var t in book.Topics)
            {
                if (!usedTopics[t.Index]) { errors.Add(new RuleError(t.Line, "この話題はどの到達状態でも選べません")); }
            }
        }

        private static void DerivePrecedence(RuleBook book, List<World> worlds, int[] precedes, List<RuleError> errors)
        {
            var all = book.Puzzles.Count == 30 ? 0x3FFFFFFF : (1 << book.Puzzles.Count) - 1;
            var seen = new bool[book.Puzzles.Count];
            for (int p = 0; p < precedes.Length; p++) { precedes[p] = all; }
            foreach (var w in worlds)
            {
                var mask = w.SolvedMask();
                for (int p = 0; p < precedes.Length; p++)
                {
                    if ((mask & (1 << p)) == 0) { continue; }
                    seen[p] = true;
                    precedes[p] &= mask;
                }
            }

            for (int p = 0; p < precedes.Length; p++)
            {
                if (!seen[p])
                {
                    errors.Add(new RuleError(book.Puzzles[p].Line, $"謎 {book.Puzzles[p].Id} はどの手順でも解けません"));
                    precedes[p] = 0;
                }
                else
                {
                    precedes[p] &= ~(1 << p);
                }
            }
        }

        private static string PathText(RuleBook book, List<int> parent, List<Move> via, int state)
        {
            var steps = new List<string>();
            for (int s = state; parent[s] >= 0; s = parent[s]) { steps.Add(via[s].Describe(book)); }
            if (steps.Count == 0) { return "開始直後"; }
            steps.Reverse();
            return string.Join(" → ", steps);
        }

        private static string StripLine(string message)
        {
            var i = message.IndexOf('：', StringComparison.Ordinal);
            return message.StartsWith("行 ", StringComparison.Ordinal) && i >= 0 ? message.Substring(i + 1) : message;
        }

    }
}
