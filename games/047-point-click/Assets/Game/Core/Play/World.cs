using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text;
using Game.Core.Rules;
using IntGeometry;
using TileMotion;

namespace Game.Core.Play
{
    /// <summary>世界の設定（遊んで決める値）。</summary>
    public sealed class WorldSettings
    {
        public const int MaxWalkSpeed = 64;

        /// <summary>1 Tick に歩く距離（部屋の整数の単位）。</summary>
        public int WalkSpeed = 10;

        public void Validate()
        {
            if (WalkSpeed < 1 || WalkSpeed > MaxWalkSpeed) { throw new ArgumentException($"WalkSpeed は 1〜{MaxWalkSpeed} です。"); }
        }

        public WorldSettings Copy() => (WorldSettings)MemberwiseClone();
    }

    /// <summary>
    /// 世界の状態とルールの解決。全部屋の物の表示・出口の鍵・持ち物・フラグ・謎をここで 1 つに持ち、
    /// 部屋の移動では「今いる部屋」だけを変える（移動しても状態が残ることを構造で保証する）。
    /// 部屋の物への行為は、立ち位置まで歩いてから着いた Tick に解決する。持ち物だけへの行為はすぐ解決する。
    /// </summary>
    public sealed class World
    {
        private readonly RuleBook _book;
        private readonly int _walkSpeed;
        private readonly List<int> _inventory;
        private readonly ReadOnlyCollection<int> _inventoryView;
        private readonly bool[] _obtained;
        private readonly bool[] _flags;
        private readonly bool[] _solved;
        private readonly bool[] _visible;
        private readonly bool[] _unlocked;
        private readonly List<AdventureEvent> _events = new();
        private readonly ReadOnlyCollection<AdventureEvent> _eventsView;

        private bool _hasPending;
        private Verb _pendingVerb;
        private Target _pendingTarget;
        private int _pendingTool = -1;
        private int _pendingStand;

        public World(RuleBook book, WorldSettings settings)
        {
            _book = book ?? throw new ArgumentNullException(nameof(book));
            if (settings == null) { throw new ArgumentNullException(nameof(settings)); }
            settings.Validate();
            _walkSpeed = settings.WalkSpeed;
            _inventory = new List<int>(book.InventoryCapacity);
            _inventoryView = _inventory.AsReadOnly();
            _obtained = new bool[book.Items.Count];
            _flags = new bool[book.Flags.Count];
            _solved = new bool[book.Puzzles.Count];
            _visible = new bool[book.Hotspots.Count];
            _unlocked = new bool[book.Hotspots.Count];
            foreach (var h in book.Hotspots)
            {
                _visible[h.Index] = h.InitiallyVisible;
                _unlocked[h.Index] = !h.InitiallyLocked;
            }

            Room = book.StartRoom;
            X = book.Rooms[Room].Start;
            Facing = 1;
            TalkingTo = -1;
            _eventsView = _events.AsReadOnly();
        }

        private World(World other)
        {
            _book = other._book;
            _walkSpeed = other._walkSpeed;
            _inventory = new List<int>(other._inventory);
            _inventoryView = _inventory.AsReadOnly();
            _obtained = (bool[])other._obtained.Clone();
            _flags = (bool[])other._flags.Clone();
            _solved = (bool[])other._solved.Clone();
            _visible = (bool[])other._visible.Clone();
            _unlocked = (bool[])other._unlocked.Clone();
            _eventsView = _events.AsReadOnly();
            _hasPending = other._hasPending;
            _pendingVerb = other._pendingVerb;
            _pendingTarget = other._pendingTarget;
            _pendingTool = other._pendingTool;
            _pendingStand = other._pendingStand;
            Room = other.Room;
            X = other.X;
            Facing = other.Facing;
            TalkingTo = other.TalkingTo;
            Escaped = other.Escaped;
            Tick = other.Tick;
            Actions = other.Actions;
        }

        public RuleBook Book => _book;
        public int Room { get; private set; }

        /// <summary>主人公の x（部屋の整数の単位）。</summary>
        public int X { get; private set; }

        /// <summary>向き（右 1・左 -1）。</summary>
        public int Facing { get; private set; }

        /// <summary>会話中の人物の物の番号（会話中でなければ -1）。</summary>
        public int TalkingTo { get; private set; }

        public bool Escaped { get; private set; }
        public long Tick { get; private set; }

        /// <summary>解決した行為の数（ただ歩いただけは数えない）。</summary>
        public int Actions { get; private set; }

        /// <summary>歩いている先（歩いていなければ null）。</summary>
        public int? WalkTarget => _hasPending ? _pendingStand : (int?)null;

        public bool IsWalking => _hasPending;

        /// <summary>持ち物（手に入れた順）。</summary>
        public IReadOnlyList<int> Inventory => _inventoryView;

        /// <summary>最後に受け付けた操作（または Step）で起きた出来事。</summary>
        public IReadOnlyList<AdventureEvent> Events => _eventsView;

        /// <summary>最後の解決で当たったルール（探索が使われたルールを数えるため）。</summary>
        internal Rule? LastRule { get; private set; }

        internal Topic? LastTopic { get; private set; }

        public bool Has(int item) => _inventory.Contains(item);

        public bool Obtained(int item) => _obtained[item];

        public bool Flag(int flag) => _flags[flag];

        public bool Solved(int puzzle) => _solved[puzzle];

        public bool Visible(int hotspot) => _visible[hotspot];

        public bool Unlocked(int hotspot) => _unlocked[hotspot];

        public int SolvedCount
        {
            get
            {
                int n = 0;
                foreach (var s in _solved)
                {
                    if (s) { n++; }
                }

                return n;
            }
        }

        public World Clone() => new(this);

        /// <summary>部屋の座標の点にある、今の部屋の見えている物で一番手前のもの（書いた順の後ろが手前）。なければ -1。</summary>
        public int HotspotAt(Vec2i point)
        {
            for (int i = _book.Hotspots.Count - 1; i >= 0; i--)
            {
                var h = _book.Hotspots[i];
                if (h.Room == Room && _visible[i] && h.Area.Contains(point)) { return i; }
            }

            return -1;
        }

        /// <summary>今選べる話題（会話中でなければ空）。書いた順。</summary>
        public IReadOnlyList<int> AvailableTopics()
        {
            var result = new List<int>();
            if (TalkingTo < 0) { return result; }
            foreach (var t in _book.Topics)
            {
                if (t.Npc == TalkingTo && Holds(t.Conditions)) { result.Add(t.Index); }
            }

            return result;
        }

        /// <summary>
        /// 行為を命じる。部屋の物なら立ち位置まで歩いてから解決する（歩いている途中の命令は前の命令を置き換える）。
        /// 持ち物だけへの行為（見る・組み合わせ）はすぐ解決し、歩くのをやめる。
        /// </summary>
        public Outcome Act(Verb verb, Target target, int tool = -1)
        {
            var check = Check(verb, target, tool);
            if (!check.Accepted) { return check; }
            _events.Clear();
            if (target.Kind == TargetKind.Item)
            {
                _hasPending = false;
                Perform(verb, target, tool);
                return Outcome.Ok;
            }

            _hasPending = true;
            _pendingVerb = verb;
            _pendingTarget = target;
            _pendingTool = tool;
            _pendingStand = _book.Hotspots[target.Index].Stand;
            return Outcome.Ok;
        }

        /// <summary>床の点まで歩く（前の命令は捨てる）。</summary>
        public Outcome WalkTo(int x)
        {
            if (Escaped) { return Outcome.Refuse(Refusal.Escaped); }
            if (TalkingTo >= 0) { return Outcome.Refuse(Refusal.Talking); }
            if (x < 0 || x > _book.Rooms[Room].Width) { return Outcome.Refuse(Refusal.OutOfRange); }
            _events.Clear();
            _hasPending = true;
            _pendingVerb = Verb.Walk;
            _pendingTarget = Target.None;
            _pendingTool = -1;
            _pendingStand = x;
            return Outcome.Ok;
        }

        /// <summary>会話中に話題を選ぶ。</summary>
        public Outcome Ask(int topic)
        {
            if (Escaped) { return Outcome.Refuse(Refusal.Escaped); }
            if (TalkingTo < 0) { return Outcome.Refuse(Refusal.NotTalking); }
            if (topic < 0 || topic >= _book.Topics.Count) { return Outcome.Refuse(Refusal.OutOfRange); }
            var t = _book.Topics[topic];
            if (t.Npc != TalkingTo || !Holds(t.Conditions)) { return Outcome.Refuse(Refusal.Unavailable); }
            _events.Clear();
            LastRule = null;
            LastTopic = t;
            Actions++;
            _events.Add(new AdventureEvent(EventKind.Said, -1, t.Question));
            Apply(t.Effects, t.Line, t.Npc);
            return Outcome.Ok;
        }

        public Outcome EndTalk()
        {
            if (Escaped) { return Outcome.Refuse(Refusal.Escaped); }
            if (TalkingTo < 0) { return Outcome.Refuse(Refusal.NotTalking); }
            _events.Clear();
            _events.Add(new AdventureEvent(EventKind.TalkEnded, TalkingTo));
            TalkingTo = -1;
            return Outcome.Ok;
        }

        /// <summary>1 Tick 進める。歩いていれば進み、着いた Tick に命令を解決する。脱出の後は何もしない。</summary>
        public void Step()
        {
            if (Escaped) { return; }
            _events.Clear();
            Tick++;
            if (!_hasPending) { return; }
            var d = _pendingStand - X;
            if (d != 0)
            {
                Facing = d > 0 ? 1 : -1;
                X += Math.Abs(d) <= _walkSpeed ? d : Facing * _walkSpeed;
            }

            if (X != _pendingStand) { return; }
            _hasPending = false;
            if (_pendingTarget.Kind == TargetKind.Hotspot) { Perform(_pendingVerb, _pendingTarget, _pendingTool); }
        }

        /// <summary>状態のハッシュ。ルール表の指紋・位置・歩く先・持ち物の順序まで混ぜる。</summary>
        public uint Hash()
        {
            var h = StateHash.Start();
            h.Mix(_book.Fingerprint);
            h.Mix(_walkSpeed);
            h.Mix(Room);
            h.Mix(X);
            h.Mix(Facing);
            h.Mix(_hasPending);
            h.Mix((int)_pendingVerb);
            h.Mix((int)_pendingTarget.Kind);
            h.Mix(_pendingTarget.Index);
            h.Mix(_pendingTool);
            h.Mix(_pendingStand);
            h.Mix(_inventory.Count);
            foreach (var i in _inventory) { h.Mix(i); }
            MixBits(ref h, _obtained);
            MixBits(ref h, _flags);
            MixBits(ref h, _solved);
            MixBits(ref h, _visible);
            MixBits(ref h, _unlocked);
            h.Mix(TalkingTo);
            h.Mix(Escaped);
            h.Mix(Tick);
            h.Mix(Actions);
            return h.Value;
        }

        /// <summary>
        /// 探索で比べる状態の鍵。位置・向き・歩く先・Tick・行為の数・持ち物の順序を除いた、以後の進行を決める状態。
        /// </summary>
        internal string AbstractKey()
        {
            var sb = new StringBuilder();
            sb.Append(Room).Append('|').Append(TalkingTo).Append('|').Append(Escaped ? '1' : '0').Append('|');
            for (int i = 0; i < _obtained.Length; i++) { sb.Append(Has(i) ? '2' : _obtained[i] ? '1' : '0'); }
            sb.Append('|');
            AppendBits(sb, _flags);
            AppendBits(sb, _solved);
            AppendBits(sb, _visible);
            AppendBits(sb, _unlocked);
            return sb.ToString();
        }

        internal int SolvedMask()
        {
            int m = 0;
            for (int i = 0; i < _solved.Length; i++)
            {
                if (_solved[i]) { m |= 1 << i; }
            }

            return m;
        }

        /// <summary>命令を受け付けられるか。受け付けないときは理由を返し、何も変えない。</summary>
        internal Outcome Check(Verb verb, Target target, int tool)
        {
            if (Escaped) { return Outcome.Refuse(Refusal.Escaped); }
            if (TalkingTo >= 0) { return Outcome.Refuse(Refusal.Talking); }
            if (tool >= 0)
            {
                if (tool >= _book.Items.Count) { return Outcome.Refuse(Refusal.OutOfRange); }
                if (verb != Verb.Use) { return Outcome.Refuse(Refusal.NotAllowed); }
                if (!Has(tool)) { return Outcome.Refuse(Refusal.NotHeld); }
            }
            else if (tool < -1)
            {
                return Outcome.Refuse(Refusal.OutOfRange);
            }

            switch (target.Kind)
            {
                case TargetKind.Hotspot:
                    if (target.Index < 0 || target.Index >= _book.Hotspots.Count) { return Outcome.Refuse(Refusal.OutOfRange); }
                    if (_book.Hotspots[target.Index].Room != Room || !_visible[target.Index]) { return Outcome.Refuse(Refusal.NotHere); }
                    return Outcome.Ok;
                case TargetKind.Item:
                    if (target.Index < 0 || target.Index >= _book.Items.Count) { return Outcome.Refuse(Refusal.OutOfRange); }
                    if (verb == Verb.Look ? tool >= 0 : verb != Verb.Use || tool < 0 || tool == target.Index) { return Outcome.Refuse(Refusal.NotAllowed); }
                    if (!Has(target.Index)) { return Outcome.Refuse(Refusal.NotHeld); }
                    return Outcome.Ok;
                default:
                    return Outcome.Refuse(Refusal.NotAllowed);
            }
        }

        /// <summary>
        /// 行為を解決する（着いた Tick、または持ち物だけへの行為ですぐ）。探索も歩かずにこれを呼ぶので、遊ぶときと同じ規則になる。
        /// </summary>
        internal void Perform(Verb verb, Target target, int tool)
        {
            LastRule = null;
            LastTopic = null;
            var hotspot = target.Kind == TargetKind.Hotspot ? _book.Hotspots[target.Index] : null;
            if (hotspot != null && hotspot.Kind == HotspotKind.Exit && (verb == Verb.Walk || (verb == Verb.Use && tool < 0)))
            {
                Actions++;
                Travel(hotspot);
                return;
            }

            if (verb == Verb.Walk) { return; }
            Actions++;
            var rule = FindRule(verb, target, tool);
            if (rule != null)
            {
                LastRule = rule;
                Apply(rule.Effects, rule.Line, -1);
            }
            else
            {
                _events.Add(new AdventureEvent(EventKind.Said, DefaultLines.DefaultMarker, DefaultLine(verb, tool)));
            }

            if (verb == Verb.Talk && hotspot != null && hotspot.Kind == HotspotKind.Npc && !Escaped && _visible[hotspot.Index])
            {
                TalkingTo = hotspot.Index;
                _events.Add(new AdventureEvent(EventKind.TalkStarted, hotspot.Index));
            }
        }

        /// <summary>条件の成り立つ最初のルール（書いた順）。なければ null。</summary>
        internal Rule? FindRule(Verb verb, Target target, int tool)
        {
            foreach (var r in _book.RulesFor(verb, target, tool))
            {
                if (Holds(r.Conditions)) { return r; }
            }

            return null;
        }

        /// <summary>条件の成り立つルールの数（探索で曖昧さを調べる）。</summary>
        internal int CountMatches(Verb verb, Target target, int tool, out Rule? first, out Rule? second)
        {
            first = null;
            second = null;
            int n = 0;
            foreach (var r in _book.RulesFor(verb, target, tool))
            {
                if (!Holds(r.Conditions)) { continue; }
                if (n == 0) { first = r; }
                else if (n == 1) { second = r; }
                n++;
            }

            return n;
        }

        internal bool Holds(IReadOnlyList<Condition> conditions)
        {
            foreach (var c in conditions)
            {
                var v = c.Kind switch
                {
                    ConditionKind.Has => Has(c.Index),
                    ConditionKind.Flag => _flags[c.Index],
                    _ => _solved[c.Index],
                };
                if (v == c.Negated) { return false; }
            }

            return true;
        }

        private void Travel(HotspotDef exit)
        {
            if (!_unlocked[exit.Index])
            {
                _events.Add(new AdventureEvent(EventKind.Said, DefaultLines.DefaultMarker, DefaultLines.Locked));
                return;
            }

            Room = exit.Destination;
            X = exit.Arrival;
            _events.Add(new AdventureEvent(EventKind.RoomChanged, Room));
        }

        private void Apply(IReadOnlyList<Effect> effects, int line, int speaker)
        {
            foreach (var e in effects)
            {
                switch (e.Kind)
                {
                    case EffectKind.Give:
                        if (_obtained[e.Index]) { throw Violation(line, $"持ち物 {_book.Items[e.Index].Id} はすでに手に入れています（持ち物は 1 周で 1 回だけ手に入る）"); }
                        if (_inventory.Count >= _book.InventoryCapacity) { throw Violation(line, $"持ち物が {_book.InventoryCapacity} 個を超えます"); }
                        _obtained[e.Index] = true;
                        _inventory.Add(e.Index);
                        _events.Add(new AdventureEvent(EventKind.ItemGained, e.Index));
                        break;
                    case EffectKind.Lose:
                        if (!_inventory.Remove(e.Index)) { throw Violation(line, $"持っていない持ち物 {_book.Items[e.Index].Id} を失います"); }
                        _events.Add(new AdventureEvent(EventKind.ItemLost, e.Index));
                        break;
                    case EffectKind.Set:
                        _flags[e.Index] = true;
                        break;
                    case EffectKind.Clear:
                        _flags[e.Index] = false;
                        break;
                    case EffectKind.Solve:
                        if (_solved[e.Index]) { throw Violation(line, $"謎 {_book.Puzzles[e.Index].Id} を二度解きます"); }
                        _solved[e.Index] = true;
                        _events.Add(new AdventureEvent(EventKind.PuzzleSolved, e.Index));
                        break;
                    case EffectKind.Show:
                        _visible[e.Index] = true;
                        _events.Add(new AdventureEvent(EventKind.HotspotShown, e.Index));
                        break;
                    case EffectKind.Hide:
                        _visible[e.Index] = false;
                        _events.Add(new AdventureEvent(EventKind.HotspotHidden, e.Index));
                        break;
                    case EffectKind.Unlock:
                        _unlocked[e.Index] = true;
                        _events.Add(new AdventureEvent(EventKind.ExitUnlocked, e.Index));
                        break;
                    default:
                        _events.Add(new AdventureEvent(EventKind.Said, -1, e.Text, speaker));
                        break;
                }
            }

            if (_solved[_book.Goal] && !Escaped)
            {
                Escaped = true;
                _hasPending = false;
                if (TalkingTo >= 0)
                {
                    _events.Add(new AdventureEvent(EventKind.TalkEnded, TalkingTo));
                    TalkingTo = -1;
                }

                _events.Add(new AdventureEvent(EventKind.Escaped, _book.Goal));
            }
        }

        private static RuleViolationException Violation(int line, string message) => new(line, $"行 {line}：{message}");

        private static string DefaultLine(Verb verb, int tool) => verb switch
        {
            Verb.Look => DefaultLines.Look,
            Verb.Take => DefaultLines.Take,
            Verb.Talk => DefaultLines.Talk,
            _ => tool >= 0 ? DefaultLines.UseWith : DefaultLines.Use,
        };

        private static void MixBits(ref StateHash h, bool[] bits)
        {
            h.Mix(bits.Length);
            foreach (var b in bits) { h.Mix(b); }
        }

        private static void AppendBits(StringBuilder sb, bool[] bits)
        {
            foreach (var b in bits) { sb.Append(b ? '1' : '0'); }
            sb.Append('|');
        }
    }
}
