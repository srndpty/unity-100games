using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Game.Core
{
    /// <summary>技表の 1 技。状態機械はここを読むだけで、技ごとのコードを持たない。</summary>
    public sealed class Move
    {
        private readonly LocalBox[][] _frames;

        internal Move(
            string id,
            string name,
            int startup,
            int active,
            int recovery,
            int damage,
            int hitStun,
            int blockStun,
            int hitStop,
            int blockStop,
            int knockback,
            StrikeHeight height,
            bool cancelSpecial,
            int advance,
            int projectileSpeed,
            LocalBox shot,
            bool hasShot,
            LocalBox[][] frames)
        {
            Id = id;
            Name = name;
            Startup = startup;
            Active = active;
            Recovery = recovery;
            Damage = damage;
            HitStun = hitStun;
            BlockStun = blockStun;
            HitStop = hitStop;
            BlockStop = blockStop;
            Knockback = knockback;
            Height = height;
            CancelSpecial = cancelSpecial;
            Advance = advance;
            ProjectileSpeed = projectileSpeed;
            Shot = shot;
            HasShot = hasShot;
            _frames = frames;
        }

        public string Id { get; }

        public string Name { get; }

        public int Startup { get; }

        public int Active { get; }

        public int Recovery { get; }

        public int Damage { get; }

        public int HitStun { get; }

        public int BlockStun { get; }

        public int HitStop { get; }

        public int BlockStop { get; }

        public int Knockback { get; }

        public StrikeHeight Height { get; }

        public bool CancelSpecial { get; }

        public int Advance { get; }

        public int ProjectileSpeed { get; }

        public LocalBox Shot { get; }

        public bool HasShot { get; }

        public bool IsThrow => Height == StrikeHeight.Throw;

        public bool IsProjectile => ProjectileSpeed > 0;

        public int Total => Startup + Active + Recovery;

        public IReadOnlyList<LocalBox> Frame(int index) => Array.AsReadOnly(_frames[index]);

        internal int BoxCount(int frame) => _frames[frame].Length;

        internal LocalBox BoxAt(int frame, int index) => _frames[frame][index];

        /// <summary>成立 Tick の次以降の硬直差。ヒット硬直 − 持続 − 硬直。</summary>
        public int HitAdvantage(Character owner)
        {
            int stun = Height == StrikeHeight.Low || Height == StrikeHeight.Throw
                ? owner.Knockdown + owner.Wakeup
                : HitStun;
            return stun - Active - Recovery;
        }

        public int BlockAdvantage => BlockStun - Active - Recovery;
    }

    /// <summary>1 人分の技表。</summary>
    public sealed class Character
    {
        private readonly Dictionary<BodyPose, LocalBox[]> _poses;
        private readonly Move[] _moves;

        internal Character(string id, string name, int walk, int back, int jumpX, int jumpY, int gravity, int prejump, int land, int health, int chip, int knockdown, int wakeup, Dictionary<BodyPose, LocalBox[]> poses, Move[] moves)
        {
            Id = id;
            Name = name;
            Walk = walk;
            Back = back;
            JumpX = jumpX;
            JumpY = jumpY;
            Gravity = gravity;
            Prejump = prejump;
            Land = land;
            Health = health;
            Chip = chip;
            Knockdown = knockdown;
            Wakeup = wakeup;
            _poses = poses;
            _moves = moves;
        }

        public string Id { get; }

        public string Name { get; }

        public int Walk { get; }

        public int Back { get; }

        public int JumpX { get; }

        public int JumpY { get; }

        public int Gravity { get; }

        public int Prejump { get; }

        public int Land { get; }

        public int Health { get; }

        public int Chip { get; }

        public int Knockdown { get; }

        public int Wakeup { get; }

        public IReadOnlyList<Move> Moves => Array.AsReadOnly(_moves);

        public IReadOnlyList<LocalBox> Pose(BodyPose pose) => Array.AsReadOnly(_poses[pose]);

        internal int PoseCount(BodyPose pose) => _poses[pose].Length;

        internal LocalBox PoseAt(BodyPose pose, int index) => _poses[pose][index];

        public Move Find(string id)
        {
            for (int i = 0; i < _moves.Length; i++)
            {
                if (_moves[i].Id == id) { return _moves[i]; }
            }

            throw new ArgumentException($"技 {id} が {Id} にありません。");
        }
    }

    /// <summary>Moves.txt の読み込み。</summary>
    public sealed class MoveTable
    {
        private readonly Character[] _characters;

        private MoveTable(Character[] characters) => _characters = characters;

        public IReadOnlyList<Character> Characters => Array.AsReadOnly(_characters);

        public Character this[int index] => _characters[index];

        public int IndexOf(string id)
        {
            for (int i = 0; i < _characters.Length; i++)
            {
                if (_characters[i].Id == id) { return i; }
            }

            throw new ArgumentException($"キャラ {id} がありません。");
        }

        public static MoveTable Parse(string text)
        {
            if (text == null) { throw new ArgumentNullException(nameof(text)); }
            var lines = text.Replace("\r", string.Empty).Split('\n');
            var characters = new List<Character>();
            var poses = new Dictionary<BodyPose, LocalBox[]>();
            var moves = new List<Move>();
            string id = string.Empty;
            string name = string.Empty;
            var stats = new Dictionary<string, int>(StringComparer.Ordinal);
            bool inCharacter = false;
            BodyPose? pose = null;
            var poseBoxes = new List<LocalBox>();
            MoveDraft? draft = null;
            var repeatBoxes = new List<LocalBox>();
            int repeat = 0;
            bool inRepeat = false;

            void FlushPose()
            {
                if (pose == null) { return; }
                poses[pose.Value] = poseBoxes.ToArray();
                pose = null;
                poseBoxes = new List<LocalBox>();
            }

            void FlushRepeat(int lineNo)
            {
                if (!inRepeat || draft == null) { return; }
                if (repeat < 1) { throw new FormatException($"{lineNo} 行目: repeat は 1 以上です。"); }
                if (repeatBoxes.Count == 0) { throw new FormatException($"{lineNo} 行目: 空の Tick です。"); }
                var frame = repeatBoxes.ToArray();
                for (int i = 0; i < repeat; i++) { draft.Frames.Add(frame); }
                repeatBoxes = new List<LocalBox>();
                inRepeat = false;
            }

            void FlushMove(int lineNo)
            {
                FlushRepeat(lineNo);
                if (draft == null) { return; }
                moves.Add(draft.Build(id, lineNo));
                draft = null;
            }

            void FlushCharacter(int lineNo)
            {
                FlushPose();
                FlushMove(lineNo);
                if (!inCharacter) { return; }
                characters.Add(BuildCharacter(id, name, stats, poses, moves, lineNo));
                poses = new Dictionary<BodyPose, LocalBox[]>();
                moves = new List<Move>();
                stats = new Dictionary<string, int>(StringComparer.Ordinal);
                inCharacter = false;
            }

            for (int lineNo = 1; lineNo <= lines.Length; lineNo++)
            {
                var raw = lines[lineNo - 1].Trim();
                if (raw.Length == 0 || raw[0] == '#') { continue; }
                var parts = raw.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                var key = parts[0];
                if (key == "character")
                {
                    FlushCharacter(lineNo);
                    if (parts.Length < 3) { throw new FormatException($"{lineNo} 行目: character には id と名前が必要です。"); }
                    id = parts[1];
                    name = parts[2];
                    inCharacter = true;
                    continue;
                }

                if (!inCharacter) { throw new FormatException($"{lineNo} 行目: character より前です。"); }
                if (key == "pose")
                {
                    FlushPose();
                    FlushMove(lineNo);
                    if (parts.Length != 2) { throw new FormatException($"{lineNo} 行目: pose の形が違います。"); }
                    pose = ParsePose(parts[1], lineNo);
                    continue;
                }

                if (key == "move")
                {
                    FlushPose();
                    FlushMove(lineNo);
                    if (parts.Length < 3) { throw new FormatException($"{lineNo} 行目: move には id と名前が必要です。"); }
                    draft = new MoveDraft(parts[1], parts[2]);
                    continue;
                }

                if (key == "repeat")
                {
                    if (draft == null) { throw new FormatException($"{lineNo} 行目: repeat は技の中です。"); }
                    FlushRepeat(lineNo);
                    inRepeat = true;
                    repeat = Int(parts, 1, lineNo);
                    continue;
                }

                if (key == "end")
                {
                    FlushRepeat(lineNo);
                    continue;
                }

                if (key == "use")
                {
                    if (!inRepeat || draft == null) { throw new FormatException($"{lineNo} 行目: use は repeat の中です。"); }
                    if (parts.Length != 2) { throw new FormatException($"{lineNo} 行目: use の形が違います。"); }
                    var used = ParsePose(parts[1], lineNo);
                    if (!poses.TryGetValue(used, out var boxes)) { throw new FormatException($"{lineNo} 行目: 姿勢 {parts[1]} がまだありません。"); }
                    repeatBoxes.AddRange(boxes);
                    continue;
                }

                if (key == "box")
                {
                    var box = ParseBox(parts, lineNo);
                    if (inRepeat) { repeatBoxes.Add(box); }
                    else if (pose != null) { poseBoxes.Add(box); }
                    else { throw new FormatException($"{lineNo} 行目: box の場所が違います。"); }
                    continue;
                }

                if (draft != null)
                {
                    draft.Set(key, parts, lineNo);
                    continue;
                }

                if (parts.Length != 2) { throw new FormatException($"{lineNo} 行目: 数値の形が違います。"); }
                stats[key] = Int(parts, 1, lineNo);
            }

            FlushCharacter(lines.Length + 1);
            if (characters.Count != 2) { throw new FormatException("キャラは 2 人にしてください。"); }
            return new MoveTable(characters.ToArray());
        }

        public string ToMarkdown()
        {
            var sb = new StringBuilder();
            sb.Append("| キャラ | 技 | 発生 | 持続 | 硬直 | ダメージ | ヒット硬直 | ガード硬直 | ヒット時 | ガード時 | 段 |\n");
            sb.Append("| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | --- |\n");
            for (int c = 0; c < _characters.Length; c++)
            {
                var character = _characters[c];
                var moves = character.Moves;
                for (int m = 0; m < moves.Count; m++)
                {
                    var move = moves[m];
                    string hit = move.IsThrow ? "\u2014" : Adv(move.HitAdvantage(character));
                    string block = move.IsThrow ? "\u2014" : Adv(move.BlockAdvantage);
                    sb.Append("| ").Append(character.Name)
                        .Append(" | ").Append(move.Name)
                        .Append(" | ").Append(move.Startup.ToString(CultureInfo.InvariantCulture))
                        .Append(" | ").Append(move.Active.ToString(CultureInfo.InvariantCulture))
                        .Append(" | ").Append(move.Recovery.ToString(CultureInfo.InvariantCulture))
                        .Append(" | ").Append(move.Damage.ToString(CultureInfo.InvariantCulture))
                        .Append(" | ").Append(move.HitStun.ToString(CultureInfo.InvariantCulture))
                        .Append(" | ").Append(move.BlockStun.ToString(CultureInfo.InvariantCulture))
                        .Append(" | ").Append(hit)
                        .Append(" | ").Append(block)
                        .Append(" | ").Append(HeightName(move.Height))
                        .Append(" |\n");
                }
            }

            return sb.ToString();
        }

        private static string Adv(int value)
        {
            if (value > 0) { return "+" + value.ToString(CultureInfo.InvariantCulture); }
            return value.ToString(CultureInfo.InvariantCulture);
        }

        private static string HeightName(StrikeHeight height)
        {
            switch (height)
            {
                case StrikeHeight.High: return "上段";
                case StrikeHeight.Mid: return "中段";
                case StrikeHeight.Low: return "下段";
                case StrikeHeight.Throw: return "投げ";
                default: throw new ArgumentOutOfRangeException(nameof(height));
            }
        }

        private static Character BuildCharacter(string id, string name, Dictionary<string, int> stats, Dictionary<BodyPose, LocalBox[]> poses, List<Move> moves, int lineNo)
        {
            int Stat(string key)
            {
                if (!stats.TryGetValue(key, out var value)) { throw new FormatException($"{lineNo} 行目: {id} に {key} がありません。"); }
                return value;
            }

            var characterPoses = new Dictionary<BodyPose, LocalBox[]>();
            foreach (BodyPose pose in Enum.GetValues(typeof(BodyPose)))
            {
                if (!poses.TryGetValue(pose, out var boxes)) { throw new FormatException($"{id} の姿勢 {pose} がありません。"); }
                RequireKind(boxes, BoxKind.Push, id);
                RequireKind(boxes, BoxKind.Hurt, id);
                if (pose == BodyPose.Stand || pose == BodyPose.Crouch) { RequireKind(boxes, BoxKind.Throwable, id); }
                characterPoses[pose] = boxes;
            }

            if (moves.Count != 5) { throw new FormatException($"{id} の技は 5 つ（弱・強・足払い・必殺・投げ）にしてください。"); }
            RequireMove(moves, "light");
            RequireMove(moves, "heavy");
            RequireMove(moves, "sweep");
            RequireMove(moves, "special");
            RequireMove(moves, "throw");
            for (int i = 0; i < moves.Count; i++)
            {
                if (moves[i].CancelSpecial && !moves.Exists(m => m.Id == "special"))
                {
                    throw new FormatException($"{id} の {moves[i].Id} のキャンセル先がありません。");
                }
            }

            int health = Stat("health");
            int gravity = Stat("gravity");
            int walk = Stat("walk");
            int back = Stat("back");
            if (health <= 0 || gravity <= 0 || walk <= 0 || back <= 0) { throw new FormatException($"{id} の数値が 0 以下です。"); }
            if (Stat("jumpX") <= 0 || Stat("jumpY") <= 0 || Stat("prejump") < 1 || Stat("land") < 1)
            {
                throw new FormatException($"{id} のジャンプが不正です。");
            }

            if (Stat("chip") < 0 || Stat("knockdown") < 1 || Stat("wakeup") < 1) { throw new FormatException($"{id} の削りかダウンが不正です。"); }
            return new Character(
                id,
                name,
                walk,
                back,
                Stat("jumpX"),
                Stat("jumpY"),
                gravity,
                Stat("prejump"),
                Stat("land"),
                health,
                Stat("chip"),
                Stat("knockdown"),
                Stat("wakeup"),
                characterPoses,
                moves.ToArray());
        }

        private static void RequireKind(LocalBox[] boxes, BoxKind kind, string id)
        {
            for (int i = 0; i < boxes.Length; i++)
            {
                if (boxes[i].Kind == kind) { return; }
            }

            throw new FormatException($"{id} の姿勢に {kind} がありません。");
        }

        private static void RequireMove(List<Move> moves, string id)
        {
            for (int i = 0; i < moves.Count; i++)
            {
                if (moves[i].Id == id) { return; }
            }

            throw new FormatException($"技 {id} がありません。");
        }

        private static BodyPose ParsePose(string text, int lineNo)
        {
            switch (text)
            {
                case "stand": return BodyPose.Stand;
                case "crouch": return BodyPose.Crouch;
                case "air": return BodyPose.Air;
                case "down": return BodyPose.Down;
                default: throw new FormatException($"{lineNo} 行目: 未知の姿勢 {text} です。");
            }
        }

        private static LocalBox ParseBox(string[] parts, int lineNo)
        {
            if (parts.Length != 6) { throw new FormatException($"{lineNo} 行目: box は種類と x y w h です。"); }
            BoxKind kind;
            switch (parts[1])
            {
                case "push": kind = BoxKind.Push; break;
                case "hurt": kind = BoxKind.Hurt; break;
                case "hit": kind = BoxKind.Hit; break;
                case "throw": kind = BoxKind.Throw; break;
                case "throwable": kind = BoxKind.Throwable; break;
                default: throw new FormatException($"{lineNo} 行目: 未知の箱 {parts[1]} です。");
            }

            return new LocalBox(kind, Int(parts, 2, lineNo), Int(parts, 3, lineNo), Int(parts, 4, lineNo), Int(parts, 5, lineNo));
        }

        private static int Int(string[] parts, int index, int lineNo)
        {
            if (index >= parts.Length || !int.TryParse(parts[index], NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
            {
                throw new FormatException($"{lineNo} 行目: 整数が必要です。");
            }

            return value;
        }

        private sealed class MoveDraft
        {
            private readonly Dictionary<string, int> _numbers = new(StringComparer.Ordinal);
            private string _height = string.Empty;
            private string _cancel = string.Empty;
            private LocalBox _shot;
            private bool _hasShot;

            public MoveDraft(string id, string name)
            {
                Id = id;
                Name = name;
            }

            public string Id { get; }

            public string Name { get; }

            public List<LocalBox[]> Frames { get; } = new();

            public void Set(string key, string[] parts, int lineNo)
            {
                if (key == "height")
                {
                    if (parts.Length != 2) { throw new FormatException($"{lineNo} 行目: height の形が違います。"); }
                    _height = parts[1];
                    return;
                }

                if (key == "cancel")
                {
                    if (parts.Length != 2) { throw new FormatException($"{lineNo} 行目: cancel の形が違います。"); }
                    _cancel = parts[1];
                    return;
                }

                if (key == "shot")
                {
                    if (parts.Length != 5) { throw new FormatException($"{lineNo} 行目: shot は x y w h です。"); }
                    _shot = ParseBox(new[] { "box", "hit", parts[1], parts[2], parts[3], parts[4] }, lineNo);
                    _hasShot = true;
                    return;
                }

                if (parts.Length != 2) { throw new FormatException($"{lineNo} 行目: {key} の形が違います。"); }
                _numbers[key] = Int(parts, 1, lineNo);
            }

            public Move Build(string characterId, int lineNo)
            {
                int Num(string key)
                {
                    if (!_numbers.TryGetValue(key, out var value)) { throw new FormatException($"{characterId} の {Id} に {key} がありません。"); }
                    return value;
                }

                int startup = Num("startup");
                int active = Num("active");
                int recovery = Num("recovery");
                if (startup < 1) { throw new FormatException($"{characterId} の {Id} は発生を 1 以上にしてください。"); }
                if (active < 1 || recovery < 1) { throw new FormatException($"{characterId} の {Id} の持続か硬直が 1 未満です。"); }
                int total = startup + active + recovery;
                if (Frames.Count != total)
                {
                    throw new FormatException($"{characterId} の {Id} の Tick 数は {total} のはずが {Frames.Count} です。");
                }

                StrikeHeight height;
                switch (_height)
                {
                    case "high": height = StrikeHeight.High; break;
                    case "mid": height = StrikeHeight.Mid; break;
                    case "low": height = StrikeHeight.Low; break;
                    case "throw": height = StrikeHeight.Throw; break;
                    default: throw new FormatException($"{characterId} の {Id} の段 {_height} は未知です。");
                }

                if (_cancel != "none" && _cancel != "special") { throw new FormatException($"{characterId} の {Id} の cancel が不正です。"); }
                int projectile = _numbers.TryGetValue("projectile", out var speed) ? speed : 0;
                int advance = _numbers.TryGetValue("advance", out var step) ? step : 0;
                if (projectile < 0 || advance < 0 || Num("damage") < 0 || Num("knockback") < 0 || Num("hitstop") < 0)
                {
                    throw new FormatException($"{characterId} の {Id} の数値が負です。");
                }

                if (projectile > 0 && !_hasShot) { throw new FormatException($"{characterId} の {Id} に shot がありません。"); }
                int blockStop = _numbers.TryGetValue("blockstop", out var stop) ? stop : Math.Max(1, Num("hitstop") / 2);
                for (int frame = 0; frame < Frames.Count; frame++)
                {
                    bool inActive = frame >= startup && frame < startup + active;
                    bool hit = false;
                    bool throwBox = false;
                    bool push = false;
                    bool hurt = false;
                    var boxes = Frames[frame];
                    for (int b = 0; b < boxes.Length; b++)
                    {
                        if (boxes[b].Kind == BoxKind.Hit) { hit = true; }
                        if (boxes[b].Kind == BoxKind.Throw) { throwBox = true; }
                        if (boxes[b].Kind == BoxKind.Push) { push = true; }
                        if (boxes[b].Kind == BoxKind.Hurt) { hurt = true; }
                    }

                    if (!push || !hurt) { throw new FormatException($"{characterId} の {Id} の {frame} Tick に押し合い箱か喰らい箱がありません。"); }
                    if (!inActive && (hit || throwBox)) { throw new FormatException($"{characterId} の {Id} の発生・硬直に判定箱があります。"); }
                    if (inActive && height == StrikeHeight.Throw && !throwBox) { throw new FormatException($"{characterId} の {Id} の持続に投げ箱がありません。"); }
                    if (inActive && height != StrikeHeight.Throw && projectile == 0 && !hit)
                    {
                        throw new FormatException($"{characterId} の {Id} の持続に攻撃箱がありません。");
                    }
                }

                return new Move(
                    Id,
                    Name,
                    startup,
                    active,
                    recovery,
                    Num("damage"),
                    Num("hitstun"),
                    Num("blockstun"),
                    Num("hitstop"),
                    blockStop,
                    Num("knockback"),
                    height,
                    _cancel == "special",
                    advance,
                    projectile,
                    _shot,
                    _hasShot,
                    Frames.ToArray());
            }
        }
    }
}
