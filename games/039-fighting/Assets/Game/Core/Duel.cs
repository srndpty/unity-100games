using System;
using System.Collections.Generic;
using IntGeometry;
using TileMotion;

namespace Game.Core
{
    /// <summary>
    /// 1 ラウンドの対戦。1 Tick の順は、入力、ヒットストップ、両者の状態、押し合い、投げ、攻撃、KO。
    /// ヒットストップ中は状態・位置・飛び道具を進めず、入力の履歴と先行入力だけ積む。
    /// </summary>
    public sealed class Duel
    {
        private readonly Fighter[] _fighters = new Fighter[2];
        private readonly CommandBuffer[] _commands = { new(), new() };
        private readonly List<LiveShot> _shots = new();
        private readonly List<FrameRow> _rows = new();
        private readonly List<WorldBox> _boxScratch = new();
        private Pad _pad0 = Pad.Neutral;
        private Pad _pad1 = Pad.Neutral;

        public Duel(MoveTable table, DuelConfig config, int left, int right)
        {
            if (table == null) { throw new ArgumentNullException(nameof(table)); }
            if (config == null) { throw new ArgumentNullException(nameof(config)); }
            Table = table;
            Config = config.Validate();
            _fighters[0] = new Fighter(0, table[left]) { X = Config.StartLeft, Facing = 1 };
            _fighters[1] = new Fighter(1, table[right]) { X = Config.StartRight, Facing = -1 };
        }

        public MoveTable Table { get; }

        public DuelConfig Config { get; }

        public int Tick { get; private set; }

        public int HitStop { get; private set; }

        public bool RecordLog { get; set; }

        public int LogTicks => _rows.Count / 2;

        public Fighter At(int index)
        {
            if ((uint)index > 1) { throw new ArgumentOutOfRangeException(nameof(index)); }
            return _fighters[index];
        }

        public int ShotCount => _shots.Count;

        public Shot ShotAt(int index)
        {
            var shot = _shots[index];
            return new Shot(shot.Owner, shot.X, shot.Y, shot.Facing, shot.Hit.At(shot.X, shot.Y, shot.Facing));
        }

        public bool CanAct(int index)
        {
            var state = _fighters[index].State;
            return state == FighterState.Idle || state == FighterState.Walk || state == FighterState.Crouch;
        }

        public FrameRow LogRow(int player, int tick) => _rows[(tick * 2) + player];

        public void ClearCommands()
        {
            _commands[0].Clear();
            _commands[1].Clear();
        }

        public void CopyBoxes(int player, List<WorldBox> into)
        {
            if (into == null) { throw new ArgumentNullException(nameof(into)); }
            into.Clear();
            AppendBoxes(_fighters[player], into, false);
        }

        public void Step(Pad left, Pad right)
        {
            _pad0 = left;
            _pad1 = right;
            _commands[0].Observe(Tick, left, Config);
            _commands[1].Observe(Tick, right, Config);
            if (HitStop > 0)
            {
                HitStop--;
                WriteLog(true);
                Tick++;
                return;
            }

            MoveShots();
            Face(0);
            Face(1);
            bool began0 = TryBegin(0, left);
            bool began1 = TryBegin(1, right);
            if (!began0) { Progress(_fighters[0], left); }
            if (!began1) { Progress(_fighters[1], right); }
            Push();
            ResolveThrows();
            Stick();
            ResolveStrikes();
            Clamp(0, false);
            Clamp(1, false);
            _commands[0].TickBuffer();
            _commands[1].TickBuffer();
            DropShots();
            WriteLog(false);
            Tick++;
        }

        public uint Hash()
        {
            var hash = StateHash.Start();
            hash.Mix(Tick);
            hash.Mix(HitStop);
            for (int i = 0; i < 2; i++)
            {
                var f = _fighters[i];
                hash.Mix((int)f.State);
                hash.Mix(f.X);
                hash.Mix(f.Y);
                hash.Mix(f.Vx);
                hash.Mix(f.Vy);
                hash.Mix(f.Facing);
                hash.Mix(f.Health);
                hash.Mix(f.Remaining);
                hash.Mix(f.Frame);
                hash.Mix(f.Activation);
                hash.Mix(f.LandedActivation);
                hash.Mix(f.CancelOpen);
                hash.Mix((int)f.Pose);
                hash.Mix(f.Move == null ? 0 : Stable(f.Move.Id));
                _commands[i].Contribute(ref hash);
            }

            hash.Mix(_shots.Count);
            for (int i = 0; i < _shots.Count; i++)
            {
                hash.Mix(_shots[i].Owner);
                hash.Mix(_shots[i].X);
                hash.Mix(_shots[i].Y);
                hash.Mix(_shots[i].Facing);
                hash.Mix(_shots[i].Activation);
            }

            return hash.Value;
        }

        public Duel Clone()
        {
            var copy = new Duel(Table, Config, IndexOf(_fighters[0].Character), IndexOf(_fighters[1].Character))
            {
                Tick = Tick,
                HitStop = HitStop,
                RecordLog = false,
            };
            copy._fighters[0] = _fighters[0].Copy();
            copy._fighters[1] = _fighters[1].Copy();
            copy._commands[0] = _commands[0].Clone();
            copy._commands[1] = _commands[1].Clone();
            for (int i = 0; i < _shots.Count; i++) { copy._shots.Add(_shots[i].Copy()); }
            return copy;
        }

        private int IndexOf(Character character)
        {
            var all = Table.Characters;
            for (int i = 0; i < all.Count; i++)
            {
                if (ReferenceEquals(all[i], character)) { return i; }
            }

            throw new InvalidOperationException("キャラが技表にありません。");
        }

        private void Face(int index)
        {
            var f = _fighters[index];
            if (f.State != FighterState.Idle && f.State != FighterState.Walk && f.State != FighterState.Crouch) { return; }
            int dx = _fighters[1 - index].X - f.X;
            if (dx != 0) { f.Facing = dx > 0 ? 1 : -1; }
        }

        private bool TryBegin(int index, Pad pad)
        {
            var f = _fighters[index];
            if (f.State == FighterState.Ko) { return false; }
            var cmd = _commands[index];
            bool ready = CanAct(index);
            bool cancel = f.CancelOpen && f.State == FighterState.Attack;
            if (!ready && !cancel) { return false; }
            if (cmd.SpecialReady(f.Facing, Config))
            {
                Begin(f, "special");
                cmd.Consume();
                return true;
            }

            if (ready && cmd.ThrowReady(Tick))
            {
                Begin(f, "throw");
                cmd.Consume();
                return true;
            }

            if (!ready || !cmd.AttackReady) { return false; }
            bool down = Pad.AxisY(pad.Direction) < 0;
            if (cmd.Button == AttackButton.Sweep && down)
            {
                Begin(f, "sweep");
                cmd.Consume();
                return true;
            }

            if (!down && cmd.Button == AttackButton.Light)
            {
                Begin(f, "light");
                cmd.Consume();
                return true;
            }

            if (!down && cmd.Button == AttackButton.Heavy)
            {
                Begin(f, "heavy");
                cmd.Consume();
                return true;
            }

            return false;
        }

        private static void Begin(Fighter f, string id)
        {
            f.Move = f.Character.Find(id);
            f.State = FighterState.Attack;
            f.Frame = 0;
            f.Activation++;
            f.CancelOpen = false;
            f.Pose = id == "sweep" ? BodyPose.Crouch : BodyPose.Stand;
            f.Vx = 0;
            f.Vy = 0;
        }

        private void Progress(Fighter f, Pad pad)
        {
            if (f.State == FighterState.Ko) { return; }
            switch (f.State)
            {
                case FighterState.Idle:
                case FighterState.Walk:
                case FighterState.Crouch:
                    Ground(f, pad);
                    break;
                case FighterState.JumpPre:
                    f.Remaining--;
                    if (f.Remaining <= 0) { Launch(f, pad); }
                    break;
                case FighterState.Air:
                    Fly(f);
                    break;
                case FighterState.Land:
                    f.Remaining--;
                    if (f.Remaining <= 0)
                    {
                        f.State = FighterState.Idle;
                        f.Pose = BodyPose.Stand;
                    }

                    break;
                case FighterState.Attack:
                    ProgressAttack(f);
                    break;
                case FighterState.HitStun:
                case FighterState.BlockStun:
                    SpendStun(f);
                    break;
                case FighterState.Knockdown:
                    f.Remaining--;
                    if (f.Remaining <= 0)
                    {
                        f.State = FighterState.Wakeup;
                        f.Remaining = f.Character.Wakeup;
                        f.Pose = BodyPose.Down;
                    }

                    break;
                case FighterState.Wakeup:
                    f.Remaining--;
                    if (f.Remaining <= 0)
                    {
                        f.State = FighterState.Idle;
                        f.Pose = BodyPose.Stand;
                    }

                    break;
                case FighterState.Thrown:
                    break;
                default:
                    throw new InvalidOperationException($"未定義の状態です：{f.State}");
            }
        }

        private static void Ground(Fighter f, Pad pad)
        {
            int dx = Pad.AxisX(pad.Direction);
            int dy = Pad.AxisY(pad.Direction);
            if (dy > 0)
            {
                f.State = FighterState.JumpPre;
                f.Remaining = f.Character.Prejump;
                f.Pose = BodyPose.Stand;
                return;
            }

            if (dy < 0)
            {
                f.State = FighterState.Crouch;
                f.Pose = BodyPose.Crouch;
                return;
            }

            if (dx != 0)
            {
                int speed = dx == f.Facing ? f.Character.Walk : f.Character.Back;
                f.X += dx * speed;
                f.State = FighterState.Walk;
                f.Pose = BodyPose.Stand;
                return;
            }

            f.State = FighterState.Idle;
            f.Pose = BodyPose.Stand;
        }

        private static void Launch(Fighter f, Pad pad)
        {
            f.Vx = Pad.AxisX(pad.Direction) * f.Character.JumpX;
            f.Vy = f.Character.JumpY;
            f.State = FighterState.Air;
            f.Pose = BodyPose.Air;
        }

        private static void Fly(Fighter f)
        {
            f.X += f.Vx;
            f.Y += f.Vy;
            f.Vy -= f.Character.Gravity;
            if (f.Y <= 0 && f.Vy <= 0)
            {
                f.Y = 0;
                f.Vy = 0;
                f.Vx = 0;
                f.State = FighterState.Land;
                f.Remaining = f.Character.Land;
                f.Pose = BodyPose.Stand;
            }
        }

        private void ProgressAttack(Fighter f)
        {
            var move = f.Move;
            if (move == null)
            {
                f.State = FighterState.Idle;
                return;
            }

            int next = f.Frame + 1;
            if (next >= move.Total)
            {
                f.Move = null;
                f.CancelOpen = false;
                f.State = FighterState.Idle;
                f.Pose = BodyPose.Stand;
                return;
            }

            f.Frame = next;
            bool active = next >= move.Startup && next < move.Startup + move.Active;
            if (!active) { return; }
            if (move.Advance > 0) { f.X += f.Facing * move.Advance; }
            if (next == move.Startup) { Spawn(f, move); }
        }

        private void Spawn(Fighter f, Move move)
        {
            if (!move.IsProjectile || !move.HasShot) { return; }
            for (int i = 0; i < _shots.Count; i++)
            {
                if (_shots[i].Owner == f.Index) { return; }
            }

            _shots.Add(new LiveShot(f.Index, f.X, f.Y, f.Facing, move, f.Activation));
        }

        private static void SpendStun(Fighter f)
        {
            f.Remaining--;
            if (f.Y > 0)
            {
                f.Y += f.Vy;
                f.Vy -= f.Character.Gravity;
                if (f.Y <= 0)
                {
                    f.Y = 0;
                    f.Vy = 0;
                    f.State = FighterState.Knockdown;
                    f.Remaining = f.Character.Knockdown;
                    f.Pose = BodyPose.Down;
                    return;
                }
            }

            if (f.Remaining > 0) { return; }
            f.State = f.Pose == BodyPose.Crouch ? FighterState.Crouch : FighterState.Idle;
        }

        private void MoveShots()
        {
            for (int i = 0; i < _shots.Count; i++)
            {
                var shot = _shots[i];
                shot.X += shot.Facing * shot.Move.ProjectileSpeed;
                var hit = shot.Hit.At(shot.X, shot.Y, shot.Facing);
                if (hit.MaxX < Config.WallMin || hit.MinX > Config.WallMax) { shot.Alive = false; }
            }
        }

        private void Push()
        {
            if (_fighters[0].State == FighterState.Thrown || _fighters[1].State == FighterState.Thrown) { return; }
            if (!KindsOverlap(0, BoxKind.Push, 1, BoxKind.Push)) { return; }
            var a = PushRect(0);
            var b = PushRect(1);
            int pen = Math.Min(a.MaxX, b.MaxX) - Math.Max(a.MinX, b.MinX) + 1;
            if (pen <= 0) { return; }
            int left = _fighters[0].X <= _fighters[1].X ? 0 : 1;
            int half = pen / 2;
            _fighters[left].X -= half;
            _fighters[1 - left].X += pen - half;
            Clamp(left, true);
            Clamp(1 - left, true);
        }

        private void ResolveThrows()
        {
            bool left = ThrowConnects(0);
            bool right = ThrowConnects(1);
            if (left && right)
            {
                Whiff(_fighters[0]);
                Whiff(_fighters[1]);
                Separate();
                return;
            }

            if (left) { ConnectThrow(0); }
            else if (right) { ConnectThrow(1); }
        }

        private bool ThrowConnects(int attacker)
        {
            var f = _fighters[attacker];
            var move = f.Move;
            if (f.State != FighterState.Attack || move == null || !move.IsThrow) { return false; }
            if (f.Frame < move.Startup || f.Frame >= move.Startup + move.Active) { return false; }
            var victim = _fighters[1 - attacker];
            if (!IsThrowable(victim)) { return false; }
            return KindsOverlap(attacker, BoxKind.Throw, 1 - attacker, BoxKind.Throwable);
        }

        private static void Whiff(Fighter f)
        {
            if (f.Move == null) { return; }
            int recovery = f.Move.Startup + f.Move.Active;
            if (f.Frame < recovery) { f.Frame = recovery; }
        }

        private void Separate()
        {
            int left = _fighters[0].X <= _fighters[1].X ? 0 : 1;
            int gap = Math.Abs(_fighters[0].X - _fighters[1].X);
            int need = Config.ThrowWhiffGap - gap;
            if (need <= 0) { return; }
            int half = need / 2;
            _fighters[left].X -= half;
            _fighters[1 - left].X += need - half;
        }

        private void ConnectThrow(int attacker)
        {
            var atk = _fighters[attacker];
            var victim = _fighters[1 - attacker];
            var move = atk.Move;
            if (move == null) { return; }
            victim.Health -= move.Damage;
            victim.Move = null;
            victim.CancelOpen = false;
            victim.Y = 0;
            victim.Vy = 0;
            victim.Vx = 0;
            if (victim.Health <= 0)
            {
                victim.Health = 0;
                victim.State = FighterState.Ko;
                return;
            }

            victim.State = FighterState.Thrown;
            victim.Pose = BodyPose.Stand;
        }

        private void Stick()
        {
            for (int i = 0; i < 2; i++)
            {
                var victim = _fighters[i];
                if (victim.State != FighterState.Thrown) { continue; }
                var atk = _fighters[1 - i];
                if (atk.State == FighterState.Attack && atk.Move != null && atk.Move.IsThrow)
                {
                    victim.X = atk.X + (atk.Facing * Config.ThrowHold);
                    victim.Y = 0;
                    victim.Facing = -atk.Facing;
                    continue;
                }

                victim.State = FighterState.Knockdown;
                victim.Remaining = victim.Character.Knockdown;
                victim.Pose = BodyPose.Down;
            }
        }

        private void ResolveStrikes()
        {
            var left = FindHit(0);
            var right = FindHit(1);
            Clash(ref left, ref right);
            if (left.On) { Apply(1, left); }
            if (right.On) { Apply(0, right); }
        }

        private Pending FindHit(int attacker)
        {
            var f = _fighters[attacker];
            var victim = _fighters[1 - attacker];
            if (victim.State == FighterState.Thrown || victim.State == FighterState.Ko)
            {
                return default;
            }

            var move = f.Move;
            if (f.State == FighterState.Attack && move != null && !move.IsThrow && !f.AlreadyHit
                && f.Frame >= move.Startup && f.Frame < move.Startup + move.Active
                && KindsOverlap(attacker, BoxKind.Hit, 1 - attacker, BoxKind.Hurt))
            {
                return new Pending(true, move, f.Facing, attacker, f.Activation, true, f.Character);
            }

            for (int i = 0; i < _shots.Count; i++)
            {
                var shot = _shots[i];
                if (!shot.Alive || shot.Owner != attacker) { continue; }
                if (shot.Activation == f.Activation && f.AlreadyHit) { continue; }
                if (!ShotHits(shot, victim)) { continue; }
                return new Pending(true, shot.Move, shot.Facing, attacker, shot.Activation, false, f.Character);
            }

            return default;
        }

        private void Clash(ref Pending left, ref Pending right)
        {
            LiveShot? a = null;
            LiveShot? b = null;
            for (int i = 0; i < _shots.Count; i++)
            {
                if (!_shots[i].Alive) { continue; }
                if (_shots[i].Owner == 0) { a = _shots[i]; }
                else { b = _shots[i]; }
            }

            if (a == null || b == null) { return; }
            var ra = a.Hit.At(a.X, a.Y, a.Facing);
            var rb = b.Hit.At(b.X, b.Y, b.Facing);
            if (!Overlaps(ra, rb)) { return; }
            a.Alive = false;
            b.Alive = false;
            if (!left.FromBody) { left = default; }
            if (!right.FromBody) { right = default; }
        }

        private void Apply(int victim, Pending hit)
        {
            var def = _fighters[victim];
            var pad = victim == 0 ? _pad0 : _pad1;
            bool blocked = CanBlock(def, pad, hit.Move.Height);
            int stop = blocked ? hit.Move.BlockStop : hit.Move.HitStop;
            if (blocked)
            {
                if (hit.Move.Id == "special")
                {
                    int next = def.Health - hit.ChipFrom.Chip;
                    def.Health = next < 1 ? 1 : next;
                }

                def.State = FighterState.BlockStun;
                def.Remaining = hit.Move.BlockStun;
                def.Pose = Pad.AxisY(pad.Direction) < 0 ? BodyPose.Crouch : BodyPose.Stand;
                def.Move = null;
                def.CancelOpen = false;
            }
            else
            {
                def.Health -= hit.Move.Damage;
                def.Move = null;
                def.CancelOpen = false;
                if (def.Health <= 0)
                {
                    def.Health = 0;
                    def.State = FighterState.Ko;
                }
                else if (hit.Move.Height == StrikeHeight.Low)
                {
                    def.State = FighterState.Knockdown;
                    def.Remaining = def.Character.Knockdown;
                    def.Pose = BodyPose.Down;
                    def.Y = 0;
                    def.Vy = 0;
                }
                else
                {
                    def.State = FighterState.HitStun;
                    def.Remaining = hit.Move.HitStun;
                }
            }

            if (def.State != FighterState.Ko) { def.X += hit.Facing * hit.Move.Knockback; }
            var atk = _fighters[hit.Attacker];
            if (hit.FromBody || atk.Activation == hit.Activation) { atk.LandedActivation = hit.Activation; }
            if (hit.FromBody && hit.Move.CancelSpecial && atk.State == FighterState.Attack) { atk.CancelOpen = true; }
            if (!hit.FromBody) { KillShot(hit.Attacker, hit.Activation); }
            if (stop > HitStop) { HitStop = stop; }
        }

        private void KillShot(int owner, int activation)
        {
            for (int i = 0; i < _shots.Count; i++)
            {
                if (_shots[i].Owner == owner && _shots[i].Activation == activation) { _shots[i].Alive = false; }
            }
        }

        private static bool CanBlock(Fighter f, Pad pad, StrikeHeight height)
        {
            if (height == StrikeHeight.Throw || f.Y != 0) { return false; }
            switch (f.State)
            {
                case FighterState.Idle:
                case FighterState.Walk:
                case FighterState.Crouch:
                case FighterState.BlockStun:
                    break;
                default:
                    return false;
            }

            int back = f.Facing > 0 ? -1 : 1;
            if (Pad.AxisX(pad.Direction) != back) { return false; }
            bool crouch = Pad.AxisY(pad.Direction) < 0;
            if (crouch) { return height == StrikeHeight.High || height == StrikeHeight.Low; }
            return height == StrikeHeight.High || height == StrikeHeight.Mid;
        }

        private static bool IsThrowable(Fighter f)
        {
            if (f.Y != 0) { return false; }
            switch (f.State)
            {
                case FighterState.HitStun:
                case FighterState.BlockStun:
                case FighterState.Knockdown:
                case FighterState.Thrown:
                case FighterState.Wakeup:
                case FighterState.Ko:
                case FighterState.Air:
                    return false;
                default:
                    return true;
            }
        }

        private static bool ShotHits(LiveShot shot, Fighter victim)
        {
            var hit = shot.Hit.At(shot.X, shot.Y, shot.Facing);
            int count = BoxCount(victim);
            for (int i = 0; i < count; i++)
            {
                var box = BoxAt(victim, i);
                if (box.Kind != BoxKind.Hurt) { continue; }
                if (Overlaps(hit, box.At(victim.X, victim.Y, victim.Facing))) { return true; }
            }

            return false;
        }

        private bool KindsOverlap(int a, BoxKind ak, int b, BoxKind bk)
        {
            var fa = _fighters[a];
            var fb = _fighters[b];
            int aCount = BoxCount(fa);
            int bCount = BoxCount(fb);
            for (int i = 0; i < aCount; i++)
            {
                var left = BoxAt(fa, i);
                if (left.Kind != ak) { continue; }
                var ra = left.At(fa.X, fa.Y, fa.Facing);
                for (int j = 0; j < bCount; j++)
                {
                    var right = BoxAt(fb, j);
                    if (right.Kind != bk) { continue; }
                    if (Overlaps(ra, right.At(fb.X, fb.Y, fb.Facing))) { return true; }
                }
            }

            return false;
        }

        private static int BoxCount(Fighter f)
        {
            if (f.State == FighterState.Attack && f.Move != null && f.Frame < f.Move.Total) { return f.Move.BoxCount(f.Frame); }
            return f.Character.PoseCount(f.Pose);
        }

        private static LocalBox BoxAt(Fighter f, int index)
        {
            if (f.State == FighterState.Attack && f.Move != null && f.Frame < f.Move.Total) { return f.Move.BoxAt(f.Frame, index); }
            return f.Character.PoseAt(f.Pose, index);
        }

        private Rect PushRect(int index)
        {
            var f = _fighters[index];
            int count = BoxCount(f);
            int minX = 0;
            int minY = 0;
            int maxX = 0;
            int maxY = 0;
            bool any = false;
            for (int i = 0; i < count; i++)
            {
                var box = BoxAt(f, i);
                if (box.Kind != BoxKind.Push) { continue; }
                var rect = box.At(f.X, f.Y, f.Facing);
                if (!any)
                {
                    minX = rect.MinX;
                    minY = rect.MinY;
                    maxX = rect.MaxX;
                    maxY = rect.MaxY;
                    any = true;
                }
                else
                {
                    if (rect.MinX < minX) { minX = rect.MinX; }
                    if (rect.MinY < minY) { minY = rect.MinY; }
                    if (rect.MaxX > maxX) { maxX = rect.MaxX; }
                    if (rect.MaxY > maxY) { maxY = rect.MaxY; }
                }
            }

            if (!any) { throw new InvalidOperationException("押し合い箱がありません。"); }
            return new Rect(minX, minY, maxX, maxY);
        }

        private void Clamp(int index, bool pushOther)
        {
            var box = PushRect(index);
            if (box.MinX < Config.WallMin)
            {
                int delta = Config.WallMin - box.MinX;
                _fighters[index].X += delta;
                if (pushOther) { _fighters[1 - index].X += delta; }
            }

            box = PushRect(index);
            if (box.MaxX > Config.WallMax)
            {
                int delta = box.MaxX - Config.WallMax;
                _fighters[index].X -= delta;
                if (pushOther) { _fighters[1 - index].X -= delta; }
            }
        }

        private void DropShots()
        {
            for (int i = _shots.Count - 1; i >= 0; i--)
            {
                if (!_shots[i].Alive) { _shots.RemoveAt(i); }
            }
        }

        private static void AppendBoxes(Fighter f, List<WorldBox> into, bool activeOnly)
        {
            int count = BoxCount(f);
            var phase = PhaseOf(f);
            for (int i = 0; i < count; i++)
            {
                var box = BoxAt(f, i);
                if (box.Kind == BoxKind.Throwable && !IsThrowable(f)) { continue; }
                if (box.Kind == BoxKind.Hit && phase != MovePhase.Active) { continue; }
                if (box.Kind == BoxKind.Throw && phase != MovePhase.Active) { continue; }
                if (activeOnly && box.Kind == BoxKind.Hit && f.AlreadyHit) { continue; }
                into.Add(new WorldBox(box.Kind, box.At(f.X, f.Y, f.Facing)));
            }
        }

        private void WriteLog(bool frozen)
        {
            if (!RecordLog) { return; }
            _rows.Add(MakeRow(0, frozen));
            _rows.Add(MakeRow(1, frozen));
        }

        private FrameRow MakeRow(int index, bool frozen)
        {
            var f = _fighters[index];
            _boxScratch.Clear();
            AppendBoxes(f, _boxScratch, false);
            bool hit = false;
            bool hurt = false;
            bool push = false;
            bool throwable = false;
            for (int i = 0; i < _boxScratch.Count; i++)
            {
                switch (_boxScratch[i].Kind)
                {
                    case BoxKind.Hit: hit = true; break;
                    case BoxKind.Hurt: hurt = true; break;
                    case BoxKind.Push: push = true; break;
                    case BoxKind.Throwable: throwable = true; break;
                }
            }

            return new FrameRow(f.State, PhaseOf(f), hit, hurt, push, throwable, frozen);
        }

        private static MovePhase PhaseOf(Fighter f)
        {
            if (f.State != FighterState.Attack || f.Move == null) { return MovePhase.None; }
            if (f.Frame < f.Move.Startup) { return MovePhase.Startup; }
            if (f.Frame < f.Move.Startup + f.Move.Active) { return MovePhase.Active; }
            return MovePhase.Recovery;
        }

        private static bool Overlaps(Rect a, Rect b) =>
            a.MaxX >= b.MinX && b.MaxX >= a.MinX && a.MaxY >= b.MinY && b.MaxY >= a.MinY;

        private static int Stable(string text)
        {
            int hash = 23;
            for (int i = 0; i < text.Length; i++) { hash = unchecked((hash * 31) + text[i]); }
            return hash;
        }

        private struct Pending
        {
            public Pending(bool on, Move move, int facing, int attacker, int activation, bool fromBody, Character chipFrom)
            {
                On = on;
                Move = move;
                Facing = facing;
                Attacker = attacker;
                Activation = activation;
                FromBody = fromBody;
                ChipFrom = chipFrom;
            }

            public bool On;

            public Move Move;

            public int Facing;

            public int Attacker;

            public int Activation;

            public bool FromBody;

            public Character ChipFrom;
        }

        private sealed class LiveShot
        {
            public LiveShot(int owner, int x, int y, int facing, Move move, int activation)
            {
                Owner = owner;
                X = x;
                Y = y;
                Facing = facing;
                Move = move;
                Hit = move.Shot;
                Activation = activation;
                Alive = true;
            }

            public int Owner { get; }

            public int X { get; set; }

            public int Y { get; }

            public int Facing { get; }

            public Move Move { get; }

            public LocalBox Hit { get; }

            public int Activation { get; }

            public bool Alive { get; set; }

            public LiveShot Copy() => new(Owner, X, Y, Facing, Move, Activation) { Alive = Alive };
        }
    }
}
