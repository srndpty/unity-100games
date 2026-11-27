using System;
using System.Collections.Generic;
using Game.Core.Mechanisms;
using Game.Core.Rooms;

namespace Game.Core.Play
{
    public enum HintKind
    {
        /// <summary>操作できる仕掛けを解く。</summary>
        Solve,

        /// <summary>開いた入れ物の物を拾う。</summary>
        Take,

        /// <summary>持ち物を鍵穴・差し込み口に使う。</summary>
        Use,
    }

    /// <summary>ヒントが案内する次の一手。</summary>
    public readonly struct HintStep
    {
        internal HintStep(HintKind kind, int mechanism, int item, int part)
        {
            Kind = kind;
            Mechanism = mechanism;
            Item = item;
            Part = part;
        }

        public HintKind Kind { get; }

        /// <summary>解く仕掛け（Solve）・持ち物を使う仕掛け（Use）。Take では -1。</summary>
        public int Mechanism { get; }

        /// <summary>拾う・使う持ち物（Solve では -1）。</summary>
        public int Item { get; }

        /// <summary>操作する部品（Take の拾う物・Use の差し込み口。Solve では -1）。</summary>
        public int Part { get; }

        /// <summary>段階を数える対象の鍵（同じ一手が続く間は段階が進む）。</summary>
        public int Key => ((int)Kind * 1000) + (Kind == HintKind.Take ? Part : Mechanism);

        /// <summary>この一手を行う近景。</summary>
        public int View(RoomDef def) => Kind == HintKind.Solve ? def.ViewOf(Mechanism) : def.Parts[Part].View;

        /// <summary>近景でこの一手を行う命令の列（答えの段階のとおりの操作）。</summary>
        public IReadOnlyList<Command> Answer(World world)
        {
            if (world == null) { throw new ArgumentNullException(nameof(world)); }
            var result = new List<Command>();
            switch (Kind)
            {
                case HintKind.Take:
                    result.Add(Command.Take(Part));
                    break;
                case HintKind.Use:
                    result.Add(Command.UseItem(Item, Part));
                    break;
                default:
                    foreach (var slot in world.Room.Mechanisms[Mechanism].SolutionFrom(world.ValuesOf(Mechanism)))
                    {
                        result.Add(Command.Operate(world.Def.SlotPart(Mechanism, slot)));
                    }

                    break;
            }

            return result.AsReadOnly();
        }
    }

    /// <summary>
    /// 次の一手を今の状態から選ぶ。出口から依存をさかのぼり、最初に満たしていないものを案内する：
    /// 操作できる仕掛けなら解く、Locked なら持ち物を使う、持っていなければ拾う、入れ物が閉じていれば入れ物の仕掛けへ。
    /// 文は今のシードの答えから作る。
    /// </summary>
    public static class HintEngine
    {
        public const int MaxLevel = 2;

        public static HintStep Next(World world)
        {
            if (world == null) { throw new ArgumentNullException(nameof(world)); }
            if (world.Escaped) { throw new InvalidOperationException("脱出の後に次の一手はありません。"); }
            return Need(world, world.Def.Exit, 0);
        }

        /// <summary>段階ごとの文（0 方向付け・1 具体的な手がかり・2 答え）。</summary>
        public static string Text(World world, HintStep step, int level)
        {
            if (world == null) { throw new ArgumentNullException(nameof(world)); }
            if (level < 0 || level > MaxLevel) { throw new ArgumentOutOfRangeException(nameof(level)); }
            var def = world.Def;
            switch (step.Kind)
            {
                case HintKind.Take:
                    {
                        var item = def.Items[step.Item];
                        return level switch
                        {
                            0 => $"{item.Where}を調べてみよう。",
                            1 => $"{item.Where}に{item.Name}がある。",
                            _ => $"{def.Views[def.Parts[step.Part].View].Name}で{item.Name}をクリックして拾おう。",
                        };
                    }

                case HintKind.Use:
                    {
                        var item = def.Items[step.Item];
                        var mechanism = def.Mechanisms[step.Mechanism];
                        return level switch
                        {
                            0 => $"{item.Name}の使い道を考えよう。",
                            1 => $"{item.Name}は{mechanism.Name}に合いそうだ。",
                            _ => $"持ち物の{item.Name}を選んで、{def.Views[def.Parts[step.Part].View].Name}の{def.Parts[step.Part].Name}をクリックしよう。",
                        };
                    }

                default:
                    {
                        var mechanism = def.Mechanisms[step.Mechanism];
                        return level switch
                        {
                            0 => mechanism.Direction,
                            1 => mechanism.Clue,
                            _ => $"{mechanism.Name}の答えは {world.Room.Mechanisms[step.Mechanism].AnswerText}。{Moves(world, step.Mechanism)}",
                        };
                    }
            }
        }

        /// <summary>
        /// リングは名前でなく星座の印で出るので、答えの段階に「今の位置からあと何回クリックするか」を添える（試遊：名前と印を結び付けられない）。
        /// ほかの仕掛けは画面の数字・針・色で答えを照らし合わせられるので添えない。
        /// </summary>
        private static string Moves(World world, int mechanism)
        {
            var def = world.Def;
            if (def.Mechanisms[mechanism].Kind != MechanismKind.Rings) { return ""; }
            var counts = new int[MechanismFactory.SlotsOf(MechanismKind.Rings)];
            foreach (var slot in world.Room.Mechanisms[mechanism].SolutionFrom(world.ValuesOf(mechanism))) { counts[slot]++; }
            var parts = new List<string>();
            for (int i = 0; i < counts.Length; i++)
            {
                var name = def.Parts[def.SlotPart(mechanism, i)].Name;
                parts.Add(counts[i] == 0 ? $"{name}はそのまま" : $"{name}をあと {counts[i]} 回");
            }

            return $"（今の位置から、{string.Join("・", parts)}クリック）";
        }

        private static HintStep Need(World world, int mechanism, int depth)
        {
            var def = world.Def;
            if (depth > def.Mechanisms.Count) { throw new InvalidOperationException("仕掛けの依存が循環しています。"); }
            switch (world.PhaseOf(mechanism))
            {
                case Phase.Active:
                    return new HintStep(HintKind.Solve, mechanism, -1, -1);
                case Phase.Solved:
                    throw new InvalidOperationException($"解けた仕掛け {def.Mechanisms[mechanism].Id} を案内しようとしました。");
            }

            var item = def.Mechanisms[mechanism].Item;
            if (world.Has(item)) { return new HintStep(HintKind.Use, mechanism, item, def.SocketOf(mechanism)); }
            if (world.Obtained(item)) { throw new InvalidOperationException($"持ち物 {def.Items[item].Id} を使ったのに {def.Mechanisms[mechanism].Id} が Locked です。"); }
            var pickup = def.PickupOf(item);
            var container = def.Parts[pickup].Container;
            if (container < 0 || world.IsSolved(container)) { return new HintStep(HintKind.Take, -1, item, pickup); }
            return Need(world, container, depth + 1);
        }
    }

    /// <summary>視点を移す命令の列（手に取った物を置く → 壁へ戻る → 向き直る → 寄る）。操縦役・ヒントの検証・探索で使う。</summary>
    public static class Navigation
    {
        public static IReadOnlyList<Command> PathTo(World world, int view)
        {
            if (world == null) { throw new ArgumentNullException(nameof(world)); }
            var def = world.Def;
            if (view < 0 || view >= def.Views.Count) { throw new ArgumentOutOfRangeException(nameof(view)); }
            var result = new List<Command>();
            if (world.Held >= 0) { result.Add(Command.Back); }
            if (world.View == view) { return result.AsReadOnly(); }
            if (!world.OnWall) { result.Add(Command.Back); }
            var from = def.Views[world.View].Wall;
            var to = def.Views[view].Wall;
            var right = (to - from + 4) % 4;
            if (right == 3) { result.Add(Command.Turn(-1)); }
            else
            {
                for (int i = 0; i < right; i++) { result.Add(Command.Turn(1)); }
            }

            if (def.Views[view].Kind == ViewKind.CloseUp) { result.Add(Command.Focus(view)); }
            return result.AsReadOnly();
        }
    }
}
