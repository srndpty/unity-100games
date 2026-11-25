using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Game.Core
{
    public enum ActorKind
    {
        Player,
        Grunt,
        Brute,
        Kicker,
        Boss,
    }

    public enum MoveKind
    {
        /// <summary>地上の打撃。持続の間だけ攻撃箱がある。</summary>
        Strike,

        /// <summary>空中の打撃。跳んでいる間に 1 回だけ出せる。</summary>
        Air,

        /// <summary>掴み中の膝。発生の Tick に掴んだ相手へ当たる（箱を使わない）。</summary>
        Knee,

        /// <summary>掴み中の投げ。発生の Tick に掴んだ相手を投げる（箱を使わない）。</summary>
        Throw,

        /// <summary>体力を払う全方向の緊急技。発生と持続の間は無敵。</summary>
        Special,
    }

    /// <summary>技表の 1 技。状態機械はここを読むだけで、技ごとのコードを持たない。</summary>
    public sealed class MoveDef
    {
        internal MoveDef(
            string id,
            string name,
            MoveKind kind,
            int startup,
            int active,
            int recovery,
            int damage,
            int hitStun,
            int hitStop,
            int knockback,
            int stagger,
            bool down,
            int launchX,
            int launchY,
            int advance,
            string next,
            int window,
            int invuln,
            int cost,
            BoxShape box,
            bool hasBox)
        {
            Id = id;
            Name = name;
            Kind = kind;
            Startup = startup;
            Active = active;
            Recovery = recovery;
            Damage = damage;
            HitStun = hitStun;
            HitStop = hitStop;
            Knockback = knockback;
            Stagger = stagger;
            Down = down;
            LaunchX = launchX;
            LaunchY = launchY;
            Advance = advance;
            Next = next;
            Window = window;
            Invuln = invuln;
            Cost = cost;
            Box = box;
            HasBox = hasBox;
        }

        public string Id { get; }

        public string Name { get; }

        public MoveKind Kind { get; }

        public int Startup { get; }

        public int Active { get; }

        public int Recovery { get; }

        public int Damage { get; }

        public int HitStun { get; }

        public int HitStop { get; }

        public int Knockback { get; }

        /// <summary>怯ませる強さ。相手の Poise 以上なら怯む。</summary>
        public int Stagger { get; }

        public bool Down { get; }

        public int LaunchX { get; }

        public int LaunchY { get; }

        /// <summary>発生と持続の間、毎 Tick 前へ進む量。</summary>
        public int Advance { get; }

        /// <summary>ヒットしたときに繋がる次段の技（無ければ空）。</summary>
        public string Next { get; }

        /// <summary>硬直の始めから次段を受け付ける Tick 数。</summary>
        public int Window { get; }

        /// <summary>出した Tick から無敵の Tick 数。</summary>
        public int Invuln { get; }

        public int Cost { get; }

        public BoxShape Box { get; }

        public bool HasBox { get; }

        public int Total => Startup + Active + Recovery;

        public bool HasNext => Next.Length > 0;

        public MovePhase PhaseAt(int tick)
        {
            if (tick < 0 || tick >= Total) { return MovePhase.None; }
            if (tick < Startup) { return MovePhase.Startup; }
            return tick < Startup + Active ? MovePhase.Active : MovePhase.Recovery;
        }
    }

    public enum MovePhase
    {
        None,
        Startup,
        Active,
        Recovery,
    }

    /// <summary>1 種類の登場者の能力と技。</summary>
    public sealed class ActorDef
    {
        private readonly MoveDef[] _moves;

        internal ActorDef(ActorKind kind, string name, Dictionary<string, int> stats, BoxShape body, int pushWidth, int pushDepth, MoveDef[] moves, string opener, string far, string close)
        {
            Kind = kind;
            Name = name;
            Health = stats["health"];
            Walk = stats["walk"];
            WalkZ = stats["walkz"];
            JumpX = stats["jumpx"];
            JumpY = stats["jumpy"];
            Gravity = stats["gravity"];
            Poise = stats["poise"];
            DownTicks = stats["downticks"];
            WakeupTicks = stats["wakeup"];
            Score = stats["score"];
            Reach = stats["reach"];
            Keep = stats["keep"];
            FarRange = stats["farrange"];
            CloseRange = stats["closerange"];
            Rage = stats["rage"];
            Body = body;
            PushWidth = pushWidth;
            PushDepth = pushDepth;
            _moves = moves;
            Moves = Array.AsReadOnly(moves);
            Opener = opener;
            Far = far;
            Close = close;
        }

        public ActorKind Kind { get; }

        public string Name { get; }

        public int Health { get; }

        public int Walk { get; }

        public int WalkZ { get; }

        public int JumpX { get; }

        public int JumpY { get; }

        public int Gravity { get; }

        /// <summary>怯みにくさ。技の Stagger がこれ未満なら怯まない（ダメージは受ける）。</summary>
        public int Poise { get; }

        public int DownTicks { get; }

        public int WakeupTicks { get; }

        public int Score { get; }

        /// <summary>敵が攻撃を始める横の距離。</summary>
        public int Reach { get; }

        /// <summary>攻撃権を待つ敵が保つ横の距離。</summary>
        public int Keep { get; }

        public int FarRange { get; }

        public int CloseRange { get; }

        /// <summary>体力が半分以下になった後の歩く速さ（百分率）。100 なら変わらない。</summary>
        public int Rage { get; }

        /// <summary>喰らい箱（足元中央）。</summary>
        public BoxShape Body { get; }

        public int PushWidth { get; }

        public int PushDepth { get; }

        public string Opener { get; }

        public string Far { get; }

        public string Close { get; }

        public IReadOnlyList<MoveDef> Moves { get; }

        public MoveDef Find(string id)
        {
            for (int i = 0; i < _moves.Length; i++)
            {
                if (_moves[i].Id == id) { return _moves[i]; }
            }

            throw new ArgumentException($"技 {id} が {Kind} にありません。", nameof(id));
        }

        public bool Has(string id)
        {
            for (int i = 0; i < _moves.Length; i++)
            {
                if (_moves[i].Id == id) { return true; }
            }

            return false;
        }
    }

    /// <summary>Moves.txt の読み込みと検証。</summary>
    public sealed class MoveTable
    {
        private static readonly string[] _statKeys =
        {
            "health", "walk", "walkz", "jumpx", "jumpy", "gravity", "poise", "downticks", "wakeup", "score",
            "reach", "keep", "farrange", "closerange", "rage",
        };

        private readonly ActorDef[] _actors;

        private MoveTable(ActorDef[] actors)
        {
            _actors = actors;
            Actors = Array.AsReadOnly(actors);
        }

        public IReadOnlyList<ActorDef> Actors { get; }

        public ActorDef this[ActorKind kind] => _actors[(int)kind];

        public static MoveTable Parse(string text)
        {
            if (text == null) { throw new ArgumentNullException(nameof(text)); }
            var lines = text.Replace("\r", string.Empty).Split('\n');
            var actors = new ActorDef?[Enum.GetValues(typeof(ActorKind)).Length];
            ActorDraft? actor = null;
            MoveDraft? move = null;

            void FlushMove(int lineNo)
            {
                if (move == null || actor == null) { return; }
                actor.Moves.Add(move.Build(actor.Kind, lineNo));
                move = null;
            }

            void FlushActor(int lineNo)
            {
                FlushMove(lineNo);
                if (actor == null) { return; }
                int index = (int)actor.Kind;
                if (actors[index] != null) { throw new FormatException($"{lineNo} 行目: {actor.Kind} が 2 回あります。"); }
                actors[index] = actor.Build();
                actor = null;
            }

            for (int lineNo = 1; lineNo <= lines.Length; lineNo++)
            {
                var raw = lines[lineNo - 1];
                int comment = raw.IndexOf('#', StringComparison.Ordinal);
                if (comment >= 0) { raw = raw.Substring(0, comment); }
                raw = raw.Trim();
                if (raw.Length == 0) { continue; }
                var parts = raw.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                var key = parts[0];
                if (key == "actor")
                {
                    FlushActor(lineNo);
                    if (parts.Length != 3) { throw new FormatException($"{lineNo} 行目: actor には種類と名前が必要です。"); }
                    actor = new ActorDraft(ParseKind(parts[1], lineNo), parts[2]);
                    continue;
                }

                if (actor == null) { throw new FormatException($"{lineNo} 行目: actor より前です。"); }
                if (key == "move")
                {
                    FlushMove(lineNo);
                    if (parts.Length != 3) { throw new FormatException($"{lineNo} 行目: move には id と名前が必要です。"); }
                    move = new MoveDraft(parts[1], parts[2]);
                    continue;
                }

                if (move != null)
                {
                    move.Set(key, parts, lineNo);
                    continue;
                }

                actor.Set(key, parts, lineNo);
            }

            FlushActor(lines.Length + 1);
            var result = new ActorDef[actors.Length];
            for (int i = 0; i < actors.Length; i++)
            {
                result[i] = actors[i] ?? throw new FormatException($"{(ActorKind)i} がありません。");
            }

            Require(result[(int)ActorKind.Player], "jab1", "jab2", "jab3", "jumpkick", "knee", "throw", "special");
            return new MoveTable(result);
        }

        /// <summary>README の技表。Moves.txt から作る。</summary>
        public string ToMarkdown()
        {
            var sb = new StringBuilder();
            sb.Append("| 登場者 | 技 | 種類 | 発生 | 持続 | 硬直 | ダメージ | ヒット硬直 | ヒットストップ | 怯み | ダウン | 奥行き | 次段 | 受付窓 |\n");
            sb.Append("| --- | --- | --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | --- | ---: | --- | ---: |\n");
            for (int a = 0; a < _actors.Length; a++)
            {
                var actor = _actors[a];
                var moves = actor.Moves;
                for (int m = 0; m < moves.Count; m++)
                {
                    var move = moves[m];
                    sb.Append("| ").Append(actor.Name)
                        .Append(" | ").Append(move.Name)
                        .Append(" | ").Append(KindName(move.Kind))
                        .Append(" | ").Append(Num(move.Startup))
                        .Append(" | ").Append(Num(move.Active))
                        .Append(" | ").Append(Num(move.Recovery))
                        .Append(" | ").Append(Num(move.Damage))
                        .Append(" | ").Append(Num(move.HitStun))
                        .Append(" | ").Append(Num(move.HitStop))
                        .Append(" | ").Append(Num(move.Stagger))
                        .Append(" | ").Append(move.Down ? "あり" : "—")
                        .Append(" | ").Append(move.HasBox ? Num(move.Box.Depth / Px.One) : "—")
                        .Append(" | ").Append(move.HasNext ? actor.Find(move.Next).Name : "—")
                        .Append(" | ").Append(move.HasNext ? Num(move.Window) : "—")
                        .Append(" |\n");
                }
            }

            return sb.ToString();
        }

        private static string Num(int value) => value.ToString(CultureInfo.InvariantCulture);

        private static string KindName(MoveKind kind)
        {
            switch (kind)
            {
                case MoveKind.Strike: return "打撃";
                case MoveKind.Air: return "空中";
                case MoveKind.Knee: return "膝";
                case MoveKind.Throw: return "投げ";
                case MoveKind.Special: return "緊急";
                default: throw new ArgumentOutOfRangeException(nameof(kind));
            }
        }

        private static void Require(ActorDef actor, params string[] ids)
        {
            for (int i = 0; i < ids.Length; i++)
            {
                if (!actor.Has(ids[i])) { throw new FormatException($"{actor.Kind} に技 {ids[i]} がありません。"); }
            }
        }

        private static ActorKind ParseKind(string text, int lineNo)
        {
            switch (text)
            {
                case "player": return ActorKind.Player;
                case "grunt": return ActorKind.Grunt;
                case "brute": return ActorKind.Brute;
                case "kicker": return ActorKind.Kicker;
                case "boss": return ActorKind.Boss;
                default: throw new FormatException($"{lineNo} 行目: 未知の登場者 {text} です。");
            }
        }

        private static int Int(string[] parts, int index, int lineNo)
        {
            if (index >= parts.Length || !int.TryParse(parts[index], NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
            {
                throw new FormatException($"{lineNo} 行目: 整数が必要です。");
            }

            return value;
        }

        private static BoxShape Shape(int x, int y, int w, int h, int depth, int lineNo)
        {
            try
            {
                return new BoxShape(x, y, w, h, depth);
            }
            catch (ArgumentOutOfRangeException e)
            {
                throw new FormatException($"{lineNo} 行目: {e.Message}", e);
            }
        }

        private sealed class ActorDraft
        {
            private readonly Dictionary<string, int> _stats = new(StringComparer.Ordinal);
            private BoxShape _body;
            private bool _hasBody;
            private int _pushWidth;
            private int _pushDepth;
            private string _opener = string.Empty;
            private string _far = string.Empty;
            private string _close = string.Empty;

            public ActorDraft(ActorKind kind, string name)
            {
                Kind = kind;
                Name = name;
            }

            public ActorKind Kind { get; }

            public string Name { get; }

            public List<MoveDef> Moves { get; } = new();

            public void Set(string key, string[] parts, int lineNo)
            {
                switch (key)
                {
                    case "body":
                        if (parts.Length != 4) { throw new FormatException($"{lineNo} 行目: body は w h depth です。"); }
                        int w = Int(parts, 1, lineNo);
                        _body = Shape(-(w / 2), 0, w, Int(parts, 2, lineNo), Int(parts, 3, lineNo), lineNo);
                        _hasBody = true;
                        return;
                    case "push":
                        if (parts.Length != 3) { throw new FormatException($"{lineNo} 行目: push は w depth です。"); }
                        _pushWidth = Int(parts, 1, lineNo);
                        _pushDepth = Int(parts, 2, lineNo);
                        return;
                    case "opener":
                    case "far":
                    case "close":
                        if (parts.Length != 2) { throw new FormatException($"{lineNo} 行目: {key} には技の id が必要です。"); }
                        if (key == "opener") { _opener = parts[1]; }
                        else if (key == "far") { _far = parts[1]; }
                        else { _close = parts[1]; }
                        return;
                }

                if (Array.IndexOf(_statKeys, key) < 0) { throw new FormatException($"{lineNo} 行目: 未知の項目 {key} です。"); }
                if (parts.Length != 2) { throw new FormatException($"{lineNo} 行目: {key} の形が違います。"); }
                if (_stats.ContainsKey(key)) { throw new FormatException($"{lineNo} 行目: {key} が 2 回あります。"); }
                _stats[key] = Int(parts, 1, lineNo);
            }

            public ActorDef Build()
            {
                var stats = new Dictionary<string, int>(StringComparer.Ordinal);
                for (int i = 0; i < _statKeys.Length; i++)
                {
                    stats[_statKeys[i]] = _stats.TryGetValue(_statKeys[i], out var value) ? value : 0;
                }

                if (!_stats.ContainsKey("rage")) { stats["rage"] = 100; }
                if (!_hasBody) { throw new FormatException($"{Kind} に body がありません。"); }
                if (_pushWidth <= 0 || _pushDepth <= 0) { throw new FormatException($"{Kind} の push が不正です。"); }
                if (stats["health"] <= 0 || stats["walk"] <= 0 || stats["walkz"] <= 0) { throw new FormatException($"{Kind} の体力か歩く速さが 0 以下です。"); }
                if (stats["poise"] < 1 || stats["downticks"] < 1 || stats["wakeup"] < 1) { throw new FormatException($"{Kind} の怯みかダウンが不正です。"); }
                if (stats["score"] < 0 || stats["rage"] < 100 || stats["rage"] > 300) { throw new FormatException($"{Kind} の得点か Rage が範囲外です。"); }
                if (Kind == ActorKind.Player)
                {
                    if (stats["jumpx"] < 0 || stats["jumpy"] <= 0 || stats["gravity"] <= 0) { throw new FormatException("プレイヤーのジャンプが不正です。"); }
                }
                else
                {
                    if (_opener.Length == 0) { throw new FormatException($"{Kind} に opener がありません。"); }
                    if (stats["reach"] <= 0 || stats["keep"] <= stats["reach"]) { throw new FormatException($"{Kind} の reach と keep が不正です（0 < reach < keep）。"); }
                    if (_far.Length > 0 && stats["farrange"] <= stats["reach"]) { throw new FormatException($"{Kind} の farrange は reach より大きくしてください。"); }
                    if (_close.Length > 0 && (stats["closerange"] <= 0 || stats["closerange"] > stats["reach"])) { throw new FormatException($"{Kind} の closerange は 1 以上 reach 以下です。"); }
                }

                var moves = Moves.ToArray();
                for (int i = 0; i < moves.Length; i++)
                {
                    for (int j = 0; j < i; j++)
                    {
                        if (moves[i].Id == moves[j].Id) { throw new FormatException($"{Kind} の技 {moves[i].Id} が 2 つあります。"); }
                    }
                }

                var def = new ActorDef(Kind, Name, stats, _body, _pushWidth, _pushDepth, moves, _opener, _far, _close);
                for (int i = 0; i < moves.Length; i++)
                {
                    var move = moves[i];
                    if (move.HasNext)
                    {
                        if (!def.Has(move.Next)) { throw new FormatException($"{Kind} の {move.Id} の次段 {move.Next} がありません。"); }
                        if (def.Find(move.Next).Kind != move.Kind) { throw new FormatException($"{Kind} の {move.Id} の次段は同じ種類の技にしてください。"); }
                    }

                    bool playerOnly = move.Kind == MoveKind.Air || move.Kind == MoveKind.Knee || move.Kind == MoveKind.Throw || move.Kind == MoveKind.Special;
                    if (playerOnly && Kind != ActorKind.Player) { throw new FormatException($"{Kind} の {move.Id} はプレイヤーだけの種類です。"); }
                }

                if (_opener.Length > 0 && !def.Has(_opener)) { throw new FormatException($"{Kind} の opener {_opener} がありません。"); }
                if (_far.Length > 0 && !def.Has(_far)) { throw new FormatException($"{Kind} の far {_far} がありません。"); }
                if (_close.Length > 0 && !def.Has(_close)) { throw new FormatException($"{Kind} の close {_close} がありません。"); }
                return def;
            }
        }

        private sealed class MoveDraft
        {
            private static readonly string[] _numberKeys =
            {
                "startup", "active", "recovery", "damage", "hitstun", "hitstop", "knockback", "stagger", "advance", "window", "invuln", "cost",
            };

            private readonly Dictionary<string, int> _numbers = new(StringComparer.Ordinal);
            private string _kind = "strike";
            private string _next = string.Empty;
            private bool _down;
            private int _launchX;
            private int _launchY;
            private BoxShape _box;
            private bool _hasBox;

            public MoveDraft(string id, string name)
            {
                Id = id;
                Name = name;
            }

            public string Id { get; }

            public string Name { get; }

            public void Set(string key, string[] parts, int lineNo)
            {
                switch (key)
                {
                    case "kind":
                        if (parts.Length != 2) { throw new FormatException($"{lineNo} 行目: kind の形が違います。"); }
                        _kind = parts[1];
                        return;
                    case "next":
                        if (parts.Length != 2) { throw new FormatException($"{lineNo} 行目: next の形が違います。"); }
                        _next = parts[1];
                        return;
                    case "down":
                        if (parts.Length != 3) { throw new FormatException($"{lineNo} 行目: down は横と縦の吹き飛びです。"); }
                        _down = true;
                        _launchX = Int(parts, 1, lineNo);
                        _launchY = Int(parts, 2, lineNo);
                        return;
                    case "box":
                        if (parts.Length != 6) { throw new FormatException($"{lineNo} 行目: box は x y w h depth です。"); }
                        _box = Shape(Int(parts, 1, lineNo), Int(parts, 2, lineNo), Int(parts, 3, lineNo), Int(parts, 4, lineNo), Int(parts, 5, lineNo), lineNo);
                        _hasBox = true;
                        return;
                }

                if (Array.IndexOf(_numberKeys, key) < 0) { throw new FormatException($"{lineNo} 行目: 未知の項目 {key} です。"); }
                if (parts.Length != 2) { throw new FormatException($"{lineNo} 行目: {key} の形が違います。"); }
                _numbers[key] = Int(parts, 1, lineNo);
            }

            public MoveDef Build(ActorKind owner, int lineNo)
            {
                int Num(string key, bool required)
                {
                    if (_numbers.TryGetValue(key, out var value)) { return value; }
                    if (required) { throw new FormatException($"{owner} の {Id} に {key} がありません（{lineNo} 行目まで）。"); }
                    return 0;
                }

                MoveKind kind;
                switch (_kind)
                {
                    case "strike": kind = MoveKind.Strike; break;
                    case "air": kind = MoveKind.Air; break;
                    case "knee": kind = MoveKind.Knee; break;
                    case "throw": kind = MoveKind.Throw; break;
                    case "special": kind = MoveKind.Special; break;
                    default: throw new FormatException($"{owner} の {Id} の種類 {_kind} は未知です。");
                }

                int startup = Num("startup", true);
                int active = Num("active", true);
                int recovery = Num("recovery", true);
                if (startup < 1 || active < 1 || recovery < 1) { throw new FormatException($"{owner} の {Id} の発生・持続・硬直は 1 以上です。"); }
                int damage = Num("damage", true);
                int hitStun = Num("hitstun", kind != MoveKind.Throw);
                int hitStop = Num("hitstop", false);
                int knockback = Num("knockback", false);
                int stagger = Num("stagger", true);
                int advance = Num("advance", false);
                int window = Num("window", false);
                int invuln = Num("invuln", false);
                int cost = Num("cost", false);
                if (damage < 0 || hitStun < 0 || hitStop < 0 || knockback < 0 || stagger < 1 || advance < 0 || window < 0 || invuln < 0 || cost < 0)
                {
                    throw new FormatException($"{owner} の {Id} の数値が範囲外です。");
                }

                if (_launchX < 0 || _launchY < 0) { throw new FormatException($"{owner} の {Id} の吹き飛びが負です。"); }
                bool needsBox = kind == MoveKind.Strike || kind == MoveKind.Air || kind == MoveKind.Special;
                if (needsBox != _hasBox) { throw new FormatException(needsBox ? $"{owner} の {Id} に box がありません。" : $"{owner} の {Id} は box を持てません。"); }
                if (_next.Length > 0 && (window < 1 || window > recovery)) { throw new FormatException($"{owner} の {Id} の受付窓は 1 以上で硬直以下です。"); }
                if (_next.Length == 0 && window != 0) { throw new FormatException($"{owner} の {Id} は次段が無いので受付窓を持てません。"); }
                if (kind == MoveKind.Special && (cost < 1 || invuln < startup + active)) { throw new FormatException($"{owner} の {Id} は代償 1 以上、発生と持続の間は無敵にしてください。"); }
                if (kind != MoveKind.Special && (cost != 0 || invuln != 0)) { throw new FormatException($"{owner} の {Id} は代償と無敵を持てません。"); }
                if (kind == MoveKind.Throw && !_down) { throw new FormatException($"{owner} の {Id} は投げなのでダウンが必要です。"); }
                return new MoveDef(Id, Name, kind, startup, active, recovery, damage, hitStun, hitStop, knockback, stagger, _down, _launchX, _launchY, advance, _next, window, invuln, cost, _box, _hasBox);
            }
        }
    }
}
