using System;
using TileMotion;

namespace Game.Core
{
    public enum Outcome
    {
        Running,
        Cleared,
        Lost,
    }

    /// <summary>
    /// 1 面の世界。置き場 0 がプレイヤー、1 以降が敵。1 Tick の順序は
    /// ①入力 ②出現 ③攻撃権 ④各実体の状態 ⑤押し合いと端 ⑥掴み ⑦攻撃（全組を集めてから同時に適用） ⑧死亡・区画・面。
    /// 乱数は敵の行動だけで、出現ごとに試合の系列から分ける。
    /// </summary>
    public sealed partial class Brawl
    {
        private readonly MoveTable _table;
        private readonly BrawlConfig _config;
        private readonly Actor[] _actors;
        private readonly HitRecord[] _hits;
        private Pad _held;
        private bool _attackEdge;
        private bool _jumpEdge;
        private bool _specialEdge;
        private readonly uint _fingerprint;
        private uint _rng;

        public Brawl(MoveTable table, BrawlConfig config, StageData stage, int seed)
        {
            _table = table ?? throw new ArgumentNullException(nameof(table));
            _config = (config ?? throw new ArgumentNullException(nameof(config))).Copy().Validate();
            Stage = stage ?? throw new ArgumentNullException(nameof(stage));
            Stage.Check(_table, _config);
            _actors = new Actor[_config.Capacity + 1];
            for (int i = 0; i < _actors.Length; i++) { _actors[i] = new Actor(i); }
            _hits = new HitRecord[_actors.Length * _actors.Length];
            Seed = seed;
            _rng = Scramble(unchecked((uint)seed));
            if (_rng == 0) { _rng = 0x9E3779B9u; }
            _queue = new SpawnSlot[Stage.MaxWaveCount];
            Lives = _config.Lives;
            var player = _actors[0];
            player.Def = _table[ActorKind.Player];
            player.Serial = ++Serials;
            player.State = ActorState.Idle;
            player.Health = player.Def.Health;
            player.X = Stage.Sections[0].CameraX + Stage.StartX;
            player.Z = (_config.ZMin + _config.ZMax) / 2;
            CameraX = Stage.Sections[0].CameraX;
            _fingerprint = Fingerprint.Of(_table, _config, Stage);
            EnterSection(0);
        }

        public MoveTable Table => _table;

        public BrawlConfig Config => _config.Copy();

        public StageData Stage { get; }

        public int Seed { get; }

        public long Tick { get; private set; }

        public Outcome Outcome { get; private set; }

        public int Lives { get; private set; }

        public long Score { get; private set; }

        public int CameraX { get; private set; }

        /// <summary>
        /// 緊急技の押下を出せるまで持ち越しているか。プレイヤーのヒットストップの間だけ残り、動ける最初の Tick に 1 回だけ使う
        /// （出せない状態・体力不足なら捨てる）。フォーカス喪失・一時停止（<see cref="ClearInput"/>）でも捨てる。
        /// </summary>
        public bool SpecialPending { get; private set; }

        /// <summary>先行入力の残り Tick。</summary>
        public int AttackBuffer { get; private set; }

        public Pad Held => _held;

        /// <summary>敵が考えて動くか（テストで止められる）。</summary>
        public bool AiEnabled { get; set; } = true;

        public int SlotCount => _actors.Length;

        public Actor Player => _actors[0];

        public long Serials { get; private set; }

        public long Activations { get; private set; }

        // 出来事の累計（効果音・テスト用）
        public int AttacksStarted { get; private set; }

        public int HitCount { get; private set; }

        public int PlayerHurtCount { get; private set; }

        public int Knockdowns { get; private set; }

        public int Defeats { get; private set; }

        public int GrabCount { get; private set; }

        public int SpecialCount { get; private set; }

        public int Deaths { get; private set; }

        public Actor At(int slot)
        {
            if (slot < 0 || slot >= _actors.Length) { throw new ArgumentOutOfRangeException(nameof(slot)); }
            return _actors[slot];
        }

        /// <summary>出ている敵の数。</summary>
        public int ActiveEnemies
        {
            get
            {
                int count = 0;
                for (int i = 1; i < _actors.Length; i++)
                {
                    if (_actors[i].Active) { count++; }
                }

                return count;
            }
        }

        public int PlayerMinX => CameraX + _config.EdgeMargin;

        public int PlayerMaxX => CameraX + _config.ViewWidth - _config.EdgeMargin;

        public int EnemyMinX => CameraX - _config.SpawnMargin;

        public int EnemyMaxX => CameraX + _config.ViewWidth + _config.SpawnMargin;

        /// <summary>敵を空いた置き場に出す。置き場が無ければ例外。</summary>
        public Actor Spawn(ActorKind kind, int x, int z, int facing, bool food = false)
        {
            if (kind == ActorKind.Player) { throw new ArgumentException("プレイヤーは出せません。", nameof(kind)); }
            if (z < _config.ZMin || z > _config.ZMax) { throw new ArgumentOutOfRangeException(nameof(z), "奥行きの帯の外です。"); }
            for (int slot = 1; slot < _actors.Length; slot++)
            {
                var a = _actors[slot];
                if (a.Active) { continue; }
                a.Clear();
                a.Def = _table[kind];
                a.Serial = ++Serials;
                a.State = ActorState.Idle;
                a.Health = a.Def.Health;
                a.X = x;
                a.Z = z;
                a.Food = food;
                a.Facing = facing >= 0 ? 1 : -1;
                a.Rng = NextSeed();
                a.WaitOffset = RandomRange(a, -_config.GrabDepth * 3, _config.GrabDepth * 3);
                return a;
            }

            throw new InvalidOperationException("敵の置き場が空いていません。");
        }

        /// <summary>場面を作る（テスト・撮影の入口）。位置は帯と端に収める。</summary>
        public void Place(Actor actor, int x, int z, int facing)
        {
            if (actor == null) { throw new ArgumentNullException(nameof(actor)); }
            if (!Owns(actor) || !actor.Active) { throw new InvalidOperationException("この世界の登場者ではありません。"); }
            actor.X = x;
            actor.Z = Math.Clamp(z, _config.ZMin, _config.ZMax);
            actor.Facing = facing >= 0 ? 1 : -1;
        }

        /// <summary>体力を変える（テスト・撮影の入口）。1 以上、最大以下。</summary>
        public void SetHealth(Actor actor, int health)
        {
            if (actor == null) { throw new ArgumentNullException(nameof(actor)); }
            if (!Owns(actor) || !actor.Active || actor.Def == null) { throw new InvalidOperationException("この世界の登場者ではありません。"); }
            if (health < 1 || health > actor.Def.Health) { throw new ArgumentOutOfRangeException(nameof(health)); }
            actor.Health = health;
        }

        /// <summary>技を出させる（敵の考えとテストの入口）。動けない状態なら false。</summary>
        public bool ForceMove(Actor actor, string moveId)
        {
            if (actor == null) { throw new ArgumentNullException(nameof(actor)); }
            if (!Owns(actor)) { throw new InvalidOperationException("この世界の登場者ではありません。"); }
            if (!actor.Active || actor.Def == null) { return false; }
            if (actor.State != ActorState.Idle && actor.State != ActorState.Walk) { return false; }
            if (actor.HitStop > 0 || actor.Defeated) { return false; }
            var move = actor.Def.Find(moveId);
            if (move.Kind != MoveKind.Strike) { throw new ArgumentException("地上の打撃だけ出せます。", nameof(moveId)); }
            StartMove(actor, move);
            return true;
        }

        public void Step(Pad pad)
        {
            if (Outcome != Outcome.Running) { return; }
            Tick++;
            _attackEdge = pad.AttackPressed || (pad.Attack && !_held.Attack);
            _jumpEdge = pad.JumpPressed || (pad.Jump && !_held.Jump);
            _specialEdge = pad.SpecialPressed || (pad.Special && !_held.Special);
            _held = new Pad(pad.X, pad.Z, pad.Attack, pad.Jump, pad.Special);
            if (_attackEdge) { AttackBuffer = _config.BufferTicks; }
            if (_specialEdge) { SpecialPending = true; }

            SpawnFromQueue();
            UpdateTokens();
            for (int slot = 0; slot < _actors.Length; slot++) { StepActor(_actors[slot]); }
            PushAndBound();
            PickUp();
            TryGrab();
            ResolveHits();
            Settle();
            if (AttackBuffer > 0 && Player.HitStop == 0) { AttackBuffer--; }
        }

        /// <summary>フォーカス喪失などで、押しているボタンと先行入力を捨てる。</summary>
        public void ClearInput()
        {
            _held = Pad.Neutral;
            AttackBuffer = 0;
            SpecialPending = false;
        }

        private void StepActor(Actor a)
        {
            if (!a.Active) { return; }
            if (a.HitStop > 0)
            {
                a.HitStop--;
                return;
            }

            if (a.Invuln > 0) { a.Invuln--; }
            if (a.Cooldown > 0) { a.Cooldown--; }
            a.StateTick++;
            bool player = a.Slot == 0;
            if (player && SpecialPending)
            {
                // 止まりの間に押した緊急技は止まり明けまで持ち越す。出せない状態・体力不足なら捨てる（1 回の押下で 1 回まで）。
                SpecialPending = false;
                if (CanSpecial(a) && TryStartSpecial(a)) { return; }
            }
            switch (a.State)
            {
                case ActorState.Idle:
                case ActorState.Walk:
                    Neutral(a);
                    break;
                case ActorState.Attack:
                    AdvanceMove(a);
                    break;
                case ActorState.Jump:
                    AdvanceAir(a);
                    break;
                case ActorState.Land:
                case ActorState.Wakeup:
                    a.Timer--;
                    if (a.Timer <= 0)
                    {
                        if (a.State == ActorState.Wakeup) { a.Invuln = _config.WakeupInvuln; }
                        Neutral(a);
                    }

                    break;
                case ActorState.Grab:
                    AdvanceGrab(a);
                    break;
                case ActorState.Grabbed:
                    break;
                case ActorState.HitStun:
                    a.X += a.VX;
                    a.VX /= 2;
                    a.Timer--;
                    if (a.Timer <= 0)
                    {
                        a.VX = 0;
                        Neutral(a);
                    }

                    break;
                case ActorState.Knockdown:
                    AdvanceKnockdown(a);
                    break;
                case ActorState.Dead:
                    a.Timer--;
                    if (a.Timer <= 0) { Expire(a); }
                    break;
                default:
                    throw new InvalidOperationException($"未定義の状態です：{a.State}");
            }
        }

        private void Neutral(Actor a)
        {
            if (a.Slot == 0) { PlayerNeutral(a); }
            else { EnemyNeutral(a); }
        }

        private void PlayerNeutral(Actor a)
        {
            var def = a.Def!;
            if (_jumpEdge)
            {
                SetState(a, ActorState.Jump);
                a.Move = null;
                a.AirUsed = false;
                a.VY = def.JumpY;
                a.VX = _held.X * def.JumpX;
                if (_held.X != 0) { a.Facing = _held.X; }
                return;
            }

            if (AttackBuffer > 0)
            {
                AttackBuffer = 0;
                StartMove(a, def.Find("jab1"));
                return;
            }

            if (_held.X == 0 && _held.Z == 0)
            {
                SetState(a, ActorState.Idle);
                return;
            }

            SetState(a, ActorState.Walk);
            a.X += _held.X * def.Walk;
            a.Z += _held.Z * def.WalkZ;
            if (_held.X != 0) { a.Facing = _held.X; }
        }

        private void StartMove(Actor a, MoveDef move)
        {
            switch (move.Kind)
            {
                case MoveKind.Air:
                    SetState(a, ActorState.Jump);
                    break;
                case MoveKind.Knee:
                case MoveKind.Throw:
                    SetState(a, ActorState.Grab);
                    break;
                default:
                    SetState(a, ActorState.Attack);
                    break;
            }

            a.Move = move;
            a.MoveTick = 0;
            a.Activation = ++Activations;
            a.HitMask = 0;
            if (move.Kind == MoveKind.Strike || move.Kind == MoveKind.Air || move.Kind == MoveKind.Special) { AttacksStarted++; }
        }

        private void AdvanceMove(Actor a)
        {
            var move = a.Move!;
            a.MoveTick++;
            int recovery = a.MoveTick - move.Startup - move.Active;
            if (move.HasNext && a.HitMask != 0 && recovery >= 0 && recovery < move.Window)
            {
                bool chain = a.Slot != 0 || AttackBuffer > 0;
                if (chain)
                {
                    if (a.Slot == 0) { AttackBuffer = 0; }
                    StartMove(a, a.Def!.Find(move.Next));
                    return;
                }
            }

            if (a.MoveTick >= move.Total)
            {
                a.Move = null;
                if (a.Slot != 0) { FinishEnemyAttack(a); }
                Neutral(a);
                return;
            }

            var phase = move.PhaseAt(a.MoveTick);
            if (move.Advance > 0 && (phase == MovePhase.Startup || phase == MovePhase.Active)) { a.X += a.Facing * move.Advance; }
        }

        private void AdvanceAir(Actor a)
        {
            var def = a.Def!;
            if (a.Move != null)
            {
                a.MoveTick++;
                if (a.MoveTick >= a.Move.Total) { a.Move = null; }
            }
            else if (a.Slot == 0 && AttackBuffer > 0 && !a.AirUsed)
            {
                AttackBuffer = 0;
                a.AirUsed = true;
                StartMove(a, def.Find("jumpkick"));
            }

            a.X += a.VX;
            a.Y += a.VY;
            a.VY -= def.Gravity;
            if (a.Y <= 0)
            {
                a.Y = 0;
                a.VX = 0;
                a.VY = 0;
                a.Move = null;
                SetState(a, ActorState.Land);
                a.Timer = _config.LandTicks;
            }
        }

        private void AdvanceKnockdown(Actor a)
        {
            if (a.Timer == 0)
            {
                a.X += a.VX;
                a.Y += a.VY;
                a.VY -= _config.FallGravity;
                if (a.Y <= 0)
                {
                    a.Y = 0;
                    a.VX = 0;
                    a.VY = 0;
                    a.Timer = a.Def!.DownTicks;
                }

                return;
            }

            a.Timer--;
            if (a.Timer > 0) { return; }
            if (a.Defeated)
            {
                SetState(a, ActorState.Dead);
                a.Timer = _config.DeadTicks;
                return;
            }

            SetState(a, ActorState.Wakeup);
            a.Timer = a.Def!.WakeupTicks;
        }

        private void AdvanceGrab(Actor a)
        {
            a.Timer--;
            if (a.Move != null)
            {
                var move = a.Move;
                a.MoveTick++;
                if (a.MoveTick == move.Startup && a.Partner >= 0)
                {
                    var held = _actors[a.Partner];
                    if (move.Kind == MoveKind.Throw)
                    {
                        Release(a, false);
                        ApplyHit(a, held, move, a.Facing);
                    }
                    else
                    {
                        ApplyHit(a, held, move, a.Facing);
                    }
                }

                if (a.MoveTick >= move.Total)
                {
                    a.Move = null;
                    if (move.Kind == MoveKind.Throw || a.Partner < 0) { PlayerNeutral(a); }
                }

                if (a.Partner >= 0) { PlaceHeld(a); }
                return;
            }

            if (a.Partner < 0)
            {
                PlayerNeutral(a);
                return;
            }

            if (AttackBuffer > 0)
            {
                AttackBuffer = 0;
                if (_held.X != 0 || a.Knees >= _config.MaxKnees)
                {
                    if (_held.X != 0) { a.Facing = _held.X; }
                    StartMove(a, a.Def!.Find("throw"));
                }
                else
                {
                    a.Knees++;
                    StartMove(a, a.Def!.Find("knee"));
                }

                PlaceHeld(a);
                return;
            }

            if (a.Timer <= 0)
            {
                Release(a, true);
                PlayerNeutral(a);
                return;
            }

            PlaceHeld(a);
        }

        private void PlaceHeld(Actor a)
        {
            var held = _actors[a.Partner];
            held.X = a.X + (a.Facing * _config.GrabOffset);
            held.Y = 0;
            held.Z = a.Z;
            held.Facing = -a.Facing;
        }

        private static bool CanSpecial(Actor a)
        {
            switch (a.State)
            {
                case ActorState.Idle:
                case ActorState.Walk:
                case ActorState.HitStun:
                case ActorState.Land:
                case ActorState.Grab:
                    return true;
                case ActorState.Attack:
                    return a.Move != null && a.Move.Kind != MoveKind.Special;
                default:
                    return false;
            }
        }

        private bool TryStartSpecial(Actor a)
        {
            var move = a.Def!.Find("special");
            if (a.Health <= move.Cost) { return false; }
            if (a.Partner >= 0) { Release(a, true); }
            a.Health -= move.Cost;
            a.VX = 0;
            SetState(a, ActorState.Attack);
            StartMove(a, move);
            a.Invuln = Math.Max(a.Invuln, move.Invuln);
            SpecialCount++;
            return true;
        }

        private void PushAndBound()
        {
            for (int i = 0; i < _actors.Length; i++)
            {
                var a = _actors[i];
                if (!Pushable(a)) { continue; }
                for (int j = i + 1; j < _actors.Length; j++)
                {
                    var b = _actors[j];
                    if (!Pushable(b) || a.Partner == j) { continue; }
                    var da = a.Def!;
                    var db = b.Def!;
                    if (Math.Abs(a.Z - b.Z) >= da.PushDepth + db.PushDepth) { continue; }
                    int reach = (da.PushWidth + db.PushWidth) / 2;
                    int dx = b.X - a.X;
                    if (Math.Abs(dx) >= reach) { continue; }
                    int overlap = reach - Math.Abs(dx);
                    int dir = dx > 0 || (dx == 0 && a.Facing > 0) ? 1 : -1;
                    int half = overlap / 2;
                    a.X -= dir * half;
                    b.X += dir * (overlap - half);
                }
            }

            for (int i = 0; i < _actors.Length; i++)
            {
                var a = _actors[i];
                if (!a.Active) { continue; }
                a.Z = Math.Clamp(a.Z, _config.ZMin, _config.ZMax);
                if (i == 0) { a.X = Math.Clamp(a.X, PlayerMinX, PlayerMaxX); }
                else { a.X = Math.Clamp(a.X, EnemyMinX, EnemyMaxX); }
            }

            var player = Player;
            if (player.State == ActorState.Grab && player.Partner >= 0) { PlaceHeld(player); }
        }

        private static bool Pushable(Actor a)
        {
            switch (a.State)
            {
                case ActorState.Idle:
                case ActorState.Walk:
                case ActorState.Attack:
                case ActorState.Land:
                case ActorState.HitStun:
                case ActorState.Grab:
                case ActorState.Wakeup:
                    return true;
                default:
                    return false;
            }
        }

        private void TryGrab()
        {
            var p = Player;
            if (p.State != ActorState.Walk || p.HitStop > 0 || _held.X != p.Facing) { return; }
            int best = -1;
            int bestDx = int.MaxValue;
            for (int slot = 1; slot < _actors.Length; slot++)
            {
                var e = _actors[slot];
                if (!Grabbable(e)) { continue; }
                if (Math.Abs(e.Z - p.Z) > _config.GrabDepth) { continue; }
                int dx = (e.X - p.X) * p.Facing;
                int reach = ((p.Def!.PushWidth + e.Def!.PushWidth) / 2) + _config.GrabReach;
                if (dx <= 0 || dx > reach) { continue; }
                if (dx < bestDx)
                {
                    best = slot;
                    bestDx = dx;
                }
            }

            if (best < 0) { return; }
            var held = _actors[best];
            SetState(p, ActorState.Grab);
            p.Move = null;
            p.Timer = _config.GrabTicks;
            p.Knees = 0;
            p.Partner = best;
            ReleaseToken(held, true);
            SetState(held, ActorState.Grabbed);
            held.Move = null;
            held.VX = 0;
            held.Partner = 0;
            PlaceHeld(p);
            GrabCount++;
        }

        private static bool Grabbable(Actor e)
        {
            if (!e.Active || e.Defeated || e.Invuln > 0 || e.Y != 0) { return false; }
            return e.State == ActorState.Idle || e.State == ActorState.Walk || e.State == ActorState.HitStun;
        }

        private void Release(Actor holder, bool stun)
        {
            if (holder.Partner < 0) { return; }
            var held = _actors[holder.Partner];
            holder.Partner = -1;
            held.Partner = -1;
            if (stun && held.State == ActorState.Grabbed)
            {
                SetState(held, ActorState.HitStun);
                held.Timer = _config.ReleaseStun;
                held.VX = holder.Facing * 512;
            }
        }

        private void ResolveHits()
        {
            int count = 0;
            for (int i = 0; i < _actors.Length; i++)
            {
                var a = _actors[i];
                if (!HasLiveBox(a)) { continue; }
                var box = a.AttackBox;
                for (int j = 0; j < _actors.Length; j++)
                {
                    if (i == j || (i == 0) == (j == 0)) { continue; }
                    var t = _actors[j];
                    if (!Hurtable(t) || (a.HitMask & Bit(j)) != 0) { continue; }
                    if (!box.Overlaps(t.Body)) { continue; }
                    int dir = a.Move!.Kind == MoveKind.Special ? (t.X > a.X ? 1 : t.X < a.X ? -1 : a.Facing) : a.Facing;
                    _hits[count++] = new HitRecord(i, j, a.Move!, dir);
                }
            }

            for (int k = 0; k < count; k++)
            {
                var hit = _hits[k];
                ApplyHit(_actors[hit.Attacker], _actors[hit.Target], hit.Move, hit.Dir);
            }
        }

        private static bool HasLiveBox(Actor a)
        {
            if (!a.Active || a.HitStop > 0 || a.Move == null || !a.Move.HasBox) { return false; }
            if (a.State != ActorState.Attack && a.State != ActorState.Jump) { return false; }
            return a.Phase == MovePhase.Active;
        }

        /// <summary>喰らい箱があるか。ダウン・起き上がり・掴まれ中・無敵の間は無い。</summary>
        public static bool Hurtable(Actor t)
        {
            if (t == null) { throw new ArgumentNullException(nameof(t)); }
            if (!t.Active || t.Defeated || t.Invuln > 0) { return false; }
            switch (t.State)
            {
                case ActorState.Idle:
                case ActorState.Walk:
                case ActorState.Attack:
                case ActorState.Jump:
                case ActorState.Land:
                case ActorState.Grab:
                case ActorState.HitStun:
                    return true;
                default:
                    return false;
            }
        }

        private void ApplyHit(Actor attacker, Actor target, MoveDef move, int dir)
        {
            attacker.HitMask |= Bit(target.Slot);
            HitCount++;
            if (target.Slot == 0) { PlayerHurtCount++; }
            int before = target.Health;
            target.Health = Math.Max(0, before - move.Damage);
            if (attacker.Slot == 0)
            {
                Score += move.Damage;
                if (before > 0 && target.Health == 0)
                {
                    Score += target.Def!.Score;
                    Defeats++;
                }
            }

            attacker.HitStop = Math.Max(attacker.HitStop, move.HitStop);
            target.HitStop = move.HitStop;
            if (move.Kind == MoveKind.Knee && target.State == ActorState.Grabbed && target.Health > 0) { return; }
            if (target.Partner >= 0)
            {
                if (target.State == ActorState.Grab) { Release(target, true); }
                else
                {
                    _actors[target.Partner].Partner = -1;
                    target.Partner = -1;
                }
            }

            bool airborne = target.State == ActorState.Jump || target.Y > 0;
            if (move.Down || target.Health == 0 || airborne)
            {
                int lx = move.Down ? move.LaunchX : _config.FallLaunchX;
                int ly = move.Down ? move.LaunchY : _config.FallLaunchY;
                ReleaseToken(target, true);
                KnockDown(target, dir, lx, ly);
                return;
            }

            // 怯まない打撃（Stagger < Poise）では攻撃を続けるので、攻撃権も持ったまま（返すと上限を超えて別の敵が攻める）。
            if (move.Stagger < target.Def!.Poise) { return; }
            ReleaseToken(target, true);
            SetState(target, ActorState.HitStun);
            target.Move = null;
            target.Timer = move.HitStun;
            target.VX = dir * move.Knockback;
            target.Facing = -dir;
        }

        private void KnockDown(Actor target, int dir, int launchX, int launchY)
        {
            SetState(target, ActorState.Knockdown);
            target.Move = null;
            target.Timer = 0;
            target.VX = dir * launchX;
            target.VY = Math.Max(1, launchY);
            target.Facing = -dir;
            Knockdowns++;
        }

        private void Expire(Actor a)
        {
            if (a.Slot != 0)
            {
                ulong mask = ~Bit(a.Slot);
                for (int i = 0; i < _actors.Length; i++) { _actors[i].HitMask &= mask; }
                if (a.Food) { Drop(a.X, a.Z); }
                a.Clear();
                return;
            }

            Deaths++;
            Lives--;
            if (Lives <= 0)
            {
                Outcome = Outcome.Lost;
                return;
            }

            a.Health = a.Def!.Health;
            a.Invuln = _config.RespawnInvuln;
            a.Y = 0;
            a.VX = 0;
            a.VY = 0;
            SetState(a, ActorState.Idle);
        }

        private static void SetState(Actor a, ActorState state)
        {
            if (a.State == state) { return; }
            a.State = state;
            a.StateTick = 0;
        }

        private static ulong Bit(int slot) => 1UL << slot;

        private bool Owns(Actor actor) => actor.Slot >= 0 && actor.Slot < _actors.Length && ReferenceEquals(_actors[actor.Slot], actor);

        public uint Hash()
        {
            var hash = StateHash.Start();
            hash.Mix(Tick);
            hash.Mix(Seed);
            hash.Mix((long)Outcome);
            hash.Mix(Lives);
            hash.Mix(Score);
            hash.Mix(CameraX);
            hash.Mix(_fingerprint);
            hash.Mix(AiEnabled);
            hash.Mix(AttackBuffer);
            hash.Mix(SpecialPending);
            hash.Mix(_held.X);
            hash.Mix(_held.Z);
            hash.Mix(_held.Attack);
            hash.Mix(_held.Jump);
            hash.Mix(_held.Special);
            hash.Mix(_rng);
            hash.Mix(Serials);
            hash.Mix(Activations);
            hash.Mix(TokensGranted);
            MixStage(ref hash);
            MixItems(ref hash);
            for (int i = 0; i < _actors.Length; i++)
            {
                var a = _actors[i];
                hash.Mix((long)a.State);
                if (!a.Active) { continue; }
                hash.Mix((long)a.Kind);
                hash.Mix(a.Serial);
                hash.Mix(a.StateTick);
                hash.Mix(a.X);
                hash.Mix(a.Y);
                hash.Mix(a.Z);
                hash.Mix(a.VX);
                hash.Mix(a.VY);
                hash.Mix(a.Facing);
                hash.Mix(a.Health);
                hash.Mix(MoveIndex(a));
                hash.Mix(a.MoveTick);
                hash.Mix(a.Activation);
                hash.Mix((long)a.HitMask);
                hash.Mix(a.HitStop);
                hash.Mix(a.Invuln);
                hash.Mix(a.AirUsed);
                hash.Mix(a.Partner);
                hash.Mix(a.Knees);
                hash.Mix(a.Timer);
                hash.Mix((long)a.Mind);
                hash.Mix(a.HasToken);
                hash.Mix(a.TokenHeld);
                hash.Mix(a.Waited);
                hash.Mix(a.Cooldown);
                hash.Mix(a.Tokens);
                hash.Mix(a.WaitOffset);
                hash.Mix(a.Rng);
                hash.Mix(a.Food);
            }

            return hash.Value;
        }

        private static int MoveIndex(Actor a)
        {
            if (a.Move == null || a.Def == null) { return -1; }
            var moves = a.Def.Moves;
            for (int i = 0; i < moves.Count; i++)
            {
                if (ReferenceEquals(moves[i], a.Move)) { return i; }
            }

            return -2;
        }

        /// <summary>以後の動きが元と同じになる複製（共有しない）。</summary>
        public Brawl Clone() => new(this);

        private Brawl(Brawl other)
        {
            _table = other._table;
            _config = other._config;
            Stage = other.Stage;
            _actors = new Actor[other._actors.Length];
            for (int i = 0; i < _actors.Length; i++)
            {
                _actors[i] = new Actor(i);
                _actors[i].CopyFrom(other._actors[i]);
            }

            _hits = new HitRecord[other._hits.Length];
            _queue = (SpawnSlot[])other._queue.Clone();
            _queueCount = other._queueCount;
            _held = other._held;
            _rng = other._rng;
            Seed = other.Seed;
            Tick = other.Tick;
            Outcome = other.Outcome;
            Lives = other.Lives;
            Score = other.Score;
            CameraX = other.CameraX;
            AttackBuffer = other.AttackBuffer;
            SpecialPending = other.SpecialPending;
            _fingerprint = other._fingerprint;
            AiEnabled = other.AiEnabled;
            Serials = other.Serials;
            Activations = other.Activations;
            AttacksStarted = other.AttacksStarted;
            HitCount = other.HitCount;
            PlayerHurtCount = other.PlayerHurtCount;
            Knockdowns = other.Knockdowns;
            Defeats = other.Defeats;
            GrabCount = other.GrabCount;
            SpecialCount = other.SpecialCount;
            Deaths = other.Deaths;
            HealCount = other.HealCount;
            Array.Copy(other._items, _items, _items.Length);
            _itemSerial = other._itemSerial;
            TokensGranted = other.TokensGranted;
            SectionIndex = other.SectionIndex;
            Locked = other.Locked;
            SectionTick = other.SectionTick;
            BossSpawned = other.BossSpawned;
        }

        private uint NextSeed()
        {
            _rng = XorShift(_rng);
            uint seed = Scramble(_rng);
            return seed == 0 ? 0x9E3779B9u : seed;
        }

        private static int RandomRange(Actor a, int min, int max)
        {
            a.Rng = XorShift(a.Rng);
            return min + (int)(a.Rng % (uint)(max - min + 1));
        }

        private static uint XorShift(uint x)
        {
            x ^= x << 13;
            x ^= x >> 17;
            x ^= x << 5;
            return x;
        }

        private static uint Scramble(uint value)
        {
            unchecked
            {
                value ^= value >> 16;
                value *= 0x85EBCA6Bu;
                value ^= value >> 13;
                value *= 0xC2B2AE35u;
                value ^= value >> 16;
                return value;
            }
        }

        private readonly struct HitRecord
        {
            public HitRecord(int attacker, int target, MoveDef move, int dir)
            {
                Attacker = attacker;
                Target = target;
                Move = move;
                Dir = dir;
            }

            public int Attacker { get; }

            public int Target { get; }

            public MoveDef Move { get; }

            public int Dir { get; }
        }
    }
}
