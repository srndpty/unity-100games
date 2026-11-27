using System;
using System.Collections.Generic;
using IntGeometry;

namespace Game.Core.Rules
{
    /// <summary>動詞。Walk は動詞を選ばずに床や出口をクリックしたとき（歩く・出口なら移動）。</summary>
    public enum Verb
    {
        Walk,
        Look,
        Take,
        Use,
        Talk,
    }

    public enum HotspotKind
    {
        Thing,
        Npc,
        Exit,
    }

    public enum TargetKind
    {
        None,
        Hotspot,
        Item,
    }

    /// <summary>行為の対象（部屋の物・人物・出口か、持ち物）。</summary>
    public readonly struct Target : IEquatable<Target>
    {
        private Target(TargetKind kind, int index)
        {
            Kind = kind;
            Index = index;
        }

        public TargetKind Kind { get; }
        public int Index { get; }

        public static Target None => default;

        public static Target Hotspot(int index) => new(TargetKind.Hotspot, index);

        public static Target Item(int index) => new(TargetKind.Item, index);

        public static bool operator ==(Target left, Target right) => left.Equals(right);

        public static bool operator !=(Target left, Target right) => !left.Equals(right);

        public bool Equals(Target other) => Kind == other.Kind && Index == other.Index;

        public override bool Equals(object? obj) => obj is Target other && Equals(other);

        public override int GetHashCode() => unchecked(((int)Kind * 397) ^ Index);
    }

    public sealed class RoomDef
    {
        internal RoomDef(int index, string id, string name, int width, int start)
        {
            Index = index;
            Id = id;
            Name = name;
            Width = width;
            Start = start;
        }

        public int Index { get; }
        public string Id { get; }
        public string Name { get; }

        /// <summary>床の幅（整数の単位）。主人公の x は 0〜Width。</summary>
        public int Width { get; }

        /// <summary>試合の始めにこの部屋にいるときの x。</summary>
        public int Start { get; }
    }

    /// <summary>部屋の中のクリックできる物（物・人物・出口）。</summary>
    public sealed class HotspotDef
    {
        internal HotspotDef(int index, int line, string id, string name, int room, HotspotKind kind, Rect area, int stand)
        {
            Index = index;
            Line = line;
            Id = id;
            Name = name;
            Room = room;
            Kind = kind;
            Area = area;
            Stand = stand;
        }

        public int Index { get; }
        public int Line { get; }

        /// <summary>「部屋.名前」の ID。</summary>
        public string Id { get; }

        public string Name { get; }
        public int Room { get; }
        public HotspotKind Kind { get; }

        /// <summary>当たりの矩形（部屋の座標、y は床から上へ）。描画もこの矩形に合わせる。</summary>
        public Rect Area { get; }

        /// <summary>行為の前に歩いていく x。</summary>
        public int Stand { get; }

        public bool InitiallyVisible { get; internal set; } = true;

        /// <summary>出口の行き先の部屋（出口でなければ -1）。</summary>
        public int Destination { get; internal set; } = -1;

        /// <summary>出口から入った先の x。</summary>
        public int Arrival { get; internal set; }

        public bool InitiallyLocked { get; internal set; }
    }

    public sealed class ItemDef
    {
        internal ItemDef(int index, string id, string name)
        {
            Index = index;
            Id = id;
            Name = name;
        }

        public int Index { get; }
        public string Id { get; }
        public string Name { get; }
    }

    public sealed class PuzzleDef
    {
        internal PuzzleDef(int index, int line, string id, string name)
        {
            Index = index;
            Line = line;
            Id = id;
            Name = name;
        }

        public int Index { get; }
        public int Line { get; }
        public string Id { get; }
        public string Name { get; }

        /// <summary>宣言した「この謎より先に解く謎」（直接のものだけ）。</summary>
        public IReadOnlyList<int> After { get; internal set; } = Array.Empty<int>();
    }

    public enum ConditionKind
    {
        Has,
        Flag,
        Solved,
    }

    public readonly struct Condition
    {
        public Condition(ConditionKind kind, int index, bool negated)
        {
            Kind = kind;
            Index = index;
            Negated = negated;
        }

        public ConditionKind Kind { get; }
        public int Index { get; }
        public bool Negated { get; }
    }

    public enum EffectKind
    {
        Give,
        Lose,
        Set,
        Clear,
        Solve,
        Show,
        Hide,
        Unlock,
        Say,
    }

    public readonly struct Effect
    {
        public Effect(EffectKind kind, int index, string text)
        {
            Kind = kind;
            Index = index;
            Text = text;
        }

        public EffectKind Kind { get; }
        public int Index { get; }

        /// <summary>Say の文。</summary>
        public string Text { get; }
    }

    /// <summary>動詞・対象・（道具の持ち物）・条件 → 効果の列。</summary>
    public sealed class Rule
    {
        internal Rule(int index, int line, Verb verb, Target target, int tool, IReadOnlyList<Condition> conditions, IReadOnlyList<Effect> effects)
        {
            Index = index;
            Line = line;
            Verb = verb;
            Target = target;
            Tool = tool;
            Conditions = conditions;
            Effects = effects;
        }

        public int Index { get; }
        public int Line { get; }
        public Verb Verb { get; }
        public Target Target { get; }

        /// <summary>使う持ち物（なければ -1）。持ち物どうしの組み合わせは番号の小さい方を Tool、大きい方を Target に正規化する。</summary>
        public int Tool { get; }

        /// <summary>書いた条件と、`solve` を含むルールにコンパイラが足す「その謎が未解決」の条件。</summary>
        public IReadOnlyList<Condition> Conditions { get; }

        public IReadOnlyList<Effect> Effects { get; }
    }

    /// <summary>人物の話題。話すと条件の成り立つ話題が選択肢に並ぶ。</summary>
    public sealed class Topic
    {
        internal Topic(int index, int line, int npc, string id, string question, IReadOnlyList<Condition> conditions, IReadOnlyList<Effect> effects)
        {
            Index = index;
            Line = line;
            Npc = npc;
            Id = id;
            Question = question;
            Conditions = conditions;
            Effects = effects;
        }

        public int Index { get; }
        public int Line { get; }
        public int Npc { get; }
        public string Id { get; }
        public string Question { get; }
        public IReadOnlyList<Condition> Conditions { get; }

        /// <summary>効果の列。Say は人物の返事。</summary>
        public IReadOnlyList<Effect> Effects { get; }
    }

    /// <summary>コンパイル済みのルール表。世界（World）はこれだけを読む。</summary>
    public sealed class RuleBook
    {
        private readonly Dictionary<long, Rule[]> _rulesByKey;

        internal RuleBook(
            IReadOnlyList<RoomDef> rooms,
            IReadOnlyList<HotspotDef> hotspots,
            IReadOnlyList<ItemDef> items,
            IReadOnlyList<string> flags,
            IReadOnlyList<PuzzleDef> puzzles,
            IReadOnlyList<Rule> rules,
            IReadOnlyList<Topic> topics,
            int startRoom,
            int goal,
            int inventoryCapacity,
            uint fingerprint)
        {
            Rooms = rooms;
            Hotspots = hotspots;
            Items = items;
            Flags = flags;
            Puzzles = puzzles;
            Rules = rules;
            Topics = topics;
            StartRoom = startRoom;
            Goal = goal;
            InventoryCapacity = inventoryCapacity;
            Fingerprint = fingerprint;
            var byKey = new Dictionary<long, List<Rule>>();
            foreach (var r in rules)
            {
                var k = Key(r.Verb, r.Target, r.Tool);
                if (!byKey.TryGetValue(k, out var list)) { byKey[k] = list = new List<Rule>(); }
                list.Add(r);
            }

            _rulesByKey = new Dictionary<long, Rule[]>();
            foreach (var kv in byKey) { _rulesByKey[kv.Key] = kv.Value.ToArray(); }
        }

        public IReadOnlyList<RoomDef> Rooms { get; }
        public IReadOnlyList<HotspotDef> Hotspots { get; }
        public IReadOnlyList<ItemDef> Items { get; }
        public IReadOnlyList<string> Flags { get; }
        public IReadOnlyList<PuzzleDef> Puzzles { get; }
        public IReadOnlyList<Rule> Rules { get; }
        public IReadOnlyList<Topic> Topics { get; }
        public int StartRoom { get; }

        /// <summary>解いたら脱出（試合の終わり）になる謎。</summary>
        public int Goal { get; }

        public int InventoryCapacity { get; }

        /// <summary>ルール表の本文の指紋（FNV-1a）。状態のハッシュに混ぜる。</summary>
        public uint Fingerprint { get; }

        public int HotspotIndex(string id)
        {
            foreach (var h in Hotspots)
            {
                if (h.Id == id) { return h.Index; }
            }

            return -1;
        }

        public int ItemIndex(string id)
        {
            foreach (var i in Items)
            {
                if (i.Id == id) { return i.Index; }
            }

            return -1;
        }

        public int PuzzleIndex(string id)
        {
            foreach (var p in Puzzles)
            {
                if (p.Id == id) { return p.Index; }
            }

            return -1;
        }

        public int FlagIndex(string id)
        {
            for (int i = 0; i < Flags.Count; i++)
            {
                if (Flags[i] == id) { return i; }
            }

            return -1;
        }

        public int RoomIndex(string id)
        {
            foreach (var r in Rooms)
            {
                if (r.Id == id) { return r.Index; }
            }

            return -1;
        }

        public int TopicIndex(string npcId, string topicId)
        {
            foreach (var t in Topics)
            {
                if (t.Id == topicId && Hotspots[t.Npc].Id == npcId) { return t.Index; }
            }

            return -1;
        }

        /// <summary>同じ（動詞・対象・道具）のルールを書いた順に。持ち物どうしは正規化してから引く。</summary>
        public IReadOnlyList<Rule> RulesFor(Verb verb, Target target, int tool)
        {
            Normalize(ref target, ref tool);
            return _rulesByKey.TryGetValue(Key(verb, target, tool), out var list) ? list : Array.Empty<Rule>();
        }

        /// <summary>持ち物どうしの組み合わせを「小さい番号が道具」にそろえる（A を B に使う ＝ B を A に使う）。</summary>
        internal static void Normalize(ref Target target, ref int tool)
        {
            if (target.Kind == TargetKind.Item && tool >= 0 && tool > target.Index)
            {
                var t = tool;
                tool = target.Index;
                target = Target.Item(t);
            }
        }

        private static long Key(Verb verb, Target target, int tool) =>
            ((long)verb << 40) | ((long)target.Kind << 32) | ((long)(target.Index & 0xFFFF) << 16) | (long)((tool + 1) & 0xFFFF);
    }
}
