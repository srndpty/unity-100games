using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using IntGeometry;
using TileMotion;

namespace Game.Core
{
    public enum RunStatus
    {
        Playing,

        /// <summary>番人を倒した。<see cref="DelveRun.IsOver"/> になるまで少し動ける。</summary>
        Won,
    }

    /// <summary>描画・音に渡す出来事（前回の取り出し以降の和）。</summary>
    [Flags]
    public enum RunEvents
    {
        None = 0,
        Swing = 1 << 0,
        Spin = 1 << 1,
        FoeHit = 1 << 2,
        FoeKilled = 1 << 3,
        DropNormal = 1 << 4,
        DropMagic = 1 << 5,
        DropRare = 1 << 6,
        PickedUp = 1 << 7,
        PotionUsed = 1 << 8,
        Hurt = 1 << 9,
        Died = 1 << 10,
        Respawned = 1 << 11,
        NextFloor = 1 << 12,
        BossKilled = 1 << 13,
        InventoryFull = 1 << 14,
        Equipped = 1 << 15,
        FoeShot = 1 << 16,
        Critical = 1 << 17,
    }

    /// <summary>試合の記録（結果画面）。</summary>
    public sealed class RunRecord
    {
        public int Kills { get; internal set; }

        public int Deaths { get; internal set; }

        public int ItemsPicked { get; internal set; }

        public int PotionsUsed { get; internal set; }

        /// <summary>敵が落とした装備品の数・ドロップを判定した回数（敵ごとに 1 回のはず）。</summary>
        public int ItemsDropped { get; internal set; }

        public int DropRolls { get; internal set; }

        /// <summary>地面の上限で消えた品の数。</summary>
        public int ItemsLost { get; internal set; }

        public Rarity BestRarity { get; internal set; }

        public RunRecord Clone() => (RunRecord)MemberwiseClone();
    }

    /// <summary>
    /// 1 試合（入口から番人まで）。Core の全ての規則はここから呼ばれる。固定 Tick・整数だけで進み、
    /// 乱数は戦闘の系列（試合シードから派生）と、敵の配置ごとの戦利品の系列（<see cref="LootRng"/>）に分ける。
    /// </summary>
    public sealed partial class DelveRun
    {
        private readonly DelveConfig _config;
        private readonly LootData _loot;
        private readonly FloorSet _floors;
        private readonly LootGenerator _generator;
        private readonly uint _rulesFingerprint;
        private readonly List<Foe> _foes = new();
        private readonly List<Projectile> _projectiles = new();
        private readonly List<GroundItem> _ground = new();
        private readonly List<Item> _inventory = new();
        private readonly Equipment _equipment = new();
        private readonly SeededRandom _combat;
        private readonly Hero _hero = new();
        private readonly RunRecord _record = new();
        private StatBlock _stats;
        private RunEvents _events;
        private long _nextGroundId = 1;

        /// <param name="config">検証済みの設定（複製して使う）。</param>
        public DelveRun(DelveConfig config, LootData loot, FloorSet floors, int seed)
        {
            if (config == null) { throw new ArgumentNullException(nameof(config)); }
            config.Validate(loot);
            _config = config.Copy();
            Foes = _foes.AsReadOnly();
            Projectiles = _projectiles.AsReadOnly();
            Ground = _ground.AsReadOnly();
            Inventory = _inventory.AsReadOnly();
            _loot = loot ?? throw new ArgumentNullException(nameof(loot));
            _floors = floors ?? throw new ArgumentNullException(nameof(floors));
            _generator = new LootGenerator(loot);
            Seed = seed;
            _combat = new SeededRandom(unchecked(seed ^ 0x5BD1E995));
            var hash = StateHash.Start();
            hash.Mix(_config.Fingerprint());
            hash.Mix(loot.Fingerprint);
            hash.Mix(floors.Fingerprint);
            _rulesFingerprint = hash.Value;

            var weapon = loot.Base(_config.StartWeapon);
            _equipment.Equip(new Item(weapon, Rarity.Normal, Array.Empty<RolledAffix>()));
            _stats = StatSheet.Compute(loot.Hero, _equipment);
            _hero.Life = _stats.Life;
            _hero.Potions = _config.StartPotions;
            EnterFloor(1);
        }

        private DelveRun(DelveRun source)
        {
            _config = source._config;
            _loot = source._loot;
            _floors = source._floors;
            _generator = source._generator;
            _rulesFingerprint = source._rulesFingerprint;
            Seed = source.Seed;
            _combat = source._combat.Clone();
            _stats = source._stats;
            _hero = source._hero.Clone();
            _record = source._record.Clone();
            _equipment = source._equipment.Clone();
            _inventory = new List<Item>(source._inventory);
            _ground = new List<GroundItem>(source._ground);
            _foes = new List<Foe>(source._foes.Count);
            foreach (var f in source._foes) { _foes.Add(f.Clone()); }
            _projectiles = new List<Projectile>(source._projectiles.Count);
            foreach (var p in source._projectiles) { _projectiles.Add(p.Clone()); }
            Foes = _foes.AsReadOnly();
            Projectiles = _projectiles.AsReadOnly();
            Ground = _ground.AsReadOnly();
            Inventory = _inventory.AsReadOnly();
            _events = source._events;
            _nextGroundId = source._nextGroundId;
            Tick = source.Tick;
            FloorNumber = source.FloorNumber;
            Status = source.Status;
            VictoryTimer = source.VictoryTimer;
            Map = source.Map;
        }

        public int Seed { get; }

        public DelveConfig Config => _config.Copy();

        public LootData Loot => _loot;

        public long Tick { get; private set; }

        public int FloorNumber { get; private set; }

        public FloorMap Map { get; private set; } = null!;

        public RunStatus Status { get; private set; }

        public int VictoryTimer { get; private set; }

        /// <summary>番人を倒し、結果へ移るまでの待ちも終わった。</summary>
        public bool IsOver => Status == RunStatus.Won && VictoryTimer == 0;

        public Hero Hero => _hero;

        public StatBlock Stats => _stats;

        /// <summary>その枠に付けている品（空なら null）。装備の変更は <see cref="Equip"/>・<see cref="Unequip"/> だけで行い、能力値を必ず計算し直す。</summary>
        public Item? EquippedAt(Slot slot) => _equipment[slot];

        /// <summary>装備一式の複製（試算用。書き換えても試合には効かない）。</summary>
        public Equipment CopyEquipment() => _equipment.Clone();

        public RunRecord Record => _record;

        /// <summary>この階の敵（倒れた敵も残る。<see cref="Foe.Dead"/> で見分ける）。</summary>
        public IReadOnlyList<Foe> Foes { get; }

        public IReadOnlyList<Projectile> Projectiles { get; }

        public IReadOnlyList<GroundItem> Ground { get; }

        public IReadOnlyList<Item> Inventory { get; }

        /// <summary>攻撃速度から決まる基本攻撃の間隔（Tick、最低 1）。</summary>
        public int AttackInterval => Math.Max(1, (int)((long)_config.AttackInterval * 100 / _stats.AttackSpeed));

        /// <summary>移動速度から決まる 1 Tick の歩み（単位、最低 1）。</summary>
        public int StepLength => Math.Max(1, (int)((long)_config.HeroSpeed * _stats.MoveSpeed / 100));

        public DelveRun Clone() => new(this);

        public RunEvents TakeEvents()
        {
            var e = _events;
            _events = RunEvents.None;
            return e;
        }

        /// <summary>1 Tick 進める。結果へ移った後は何もしない。</summary>
        public void Step(HeroCommand command)
        {
            if (IsOver) { return; }
            Tick++;
            if (Status == RunStatus.Won && VictoryTimer > 0) { VictoryTimer--; }
            StepHero(command);
            ResolveHeroHits();
            StepFoes();
            StepProjectiles();
        }

        private void StepHero(HeroCommand command)
        {
            var h = _hero;
            if (h.Dead)
            {
                h.DeathTimer--;
                if (h.DeathTimer <= 0) { Respawn(); }
                return;
            }

            if (h.AttackCooldown > 0) { h.AttackCooldown--; }
            if (h.SkillCooldown > 0) { h.SkillCooldown--; }
            if (h.PotionCooldown > 0) { h.PotionCooldown--; }
            if (h.Invuln > 0) { h.Invuln--; }

            var aim = command.Aim - h.Position;
            if (!aim.IsZero) { h.Facing = aim; }

            if (command.Potion) { DrinkPotion(); }
            if (command.Pick) { PickUpNearest(); }

            bool rooted = h.SwingHitTick >= Tick || h.SpinHitTick >= Tick;
            if (command.Skill && h.SkillCooldown == 0 && h.SpinHitTick < Tick)
            {
                h.SpinStartTick = Tick;
                h.SpinHitTick = Tick + _config.SkillWindup;
                h.SkillCooldown = _config.SkillCooldown;
                _events |= RunEvents.Spin;
                rooted = true;
            }

            if (command.Attack && h.AttackCooldown == 0 && h.SwingHitTick < Tick)
            {
                h.SwingStartTick = Tick;
                h.SwingHitTick = Tick + _config.AttackWindup;
                h.SwingDirection = h.Facing;
                h.AttackCooldown = AttackInterval;
                _events |= RunEvents.Swing;
                rooted = true;
            }

            if (!rooted && (command.MoveX != 0 || command.MoveY != 0)) { MoveHero(command.MoveX, command.MoveY); }

            TouchPotions();
            if (Map.OnStairs(HeroBox()) && FloorNumber < LootData.MaxFloor) { EnterFloor(FloorNumber + 1); }
        }

        private void MoveHero(int mx, int my)
        {
            int step = StepLength;
            int dx = mx * step;
            int dy = my * step;
            if (mx != 0 && my != 0)
            {
                // 斜めは 1/√2（181/256）。小さい値でも 0 にしない（022）。
                int diagonal = Math.Max(1, step * 181 / 256);
                dx = mx * diagonal;
                dy = my * diagonal;
            }

            var box = HeroBox();
            BodyMotion.MoveX(Map, ref box, dx);
            BodyMotion.MoveY(Map, ref box, dy);
            _hero.Position = new Vec2i(box.X + (box.Width / 2), box.Y + (box.Height / 2));
        }

        /// <summary>自機の壁との当たりの箱。</summary>
        public Box HeroBox()
        {
            int half = _config.HeroBox / 2;
            return new Box(_hero.Position.X - half, _hero.Position.Y - half, _config.HeroBox, _config.HeroBox);
        }

        private void ResolveHeroHits()
        {
            var h = _hero;
            if (h.Dead) { return; }
            if (h.SwingHitTick == Tick)
            {
                var center = h.Position + IntMath.Scale(h.SwingDirection, _config.AttackReach);
                foreach (var foe in _foes)
                {
                    if (foe.Dead) { continue; }
                    long reach = _config.AttackRadius + FoeRadius(foe);
                    if (Vec2i.DistanceSquared(center, foe.Position) < reach * reach) { HitFoe(foe, 100); }
                }
            }

            if (h.SpinHitTick == Tick)
            {
                foreach (var foe in _foes)
                {
                    if (foe.Dead) { continue; }
                    long reach = _config.SkillRadius + FoeRadius(foe);
                    if (Vec2i.DistanceSquared(h.Position, foe.Position) < reach * reach) { HitFoe(foe, _config.SkillPercent); }
                }
            }
        }

        /// <summary>自機の 1 撃。攻撃力から引き、会心・吸収を足す。倒れた敵には何もしない。</summary>
        internal void HitFoe(Foe foe, int percent)
        {
            if (foe.Dead) { return; }
            long damage = _combat.Range(_stats.DamageMin, _stats.DamageMax);
            if (_combat.Next(100) < _stats.CritChance)
            {
                damage = damage * _stats.CritDamage / 100;
                _events |= RunEvents.Critical;
            }

            damage = Math.Max(1, damage * percent / 100);
            int dealt = (int)Math.Min(damage, foe.Life);
            foe.Life -= dealt;
            foe.Aggro = true;
            foe.LastHitTick = Tick;
            _events |= RunEvents.FoeHit;
            if (_stats.Leech > 0)
            {
                long heal = (long)dealt * _stats.Leech / 100;
                _hero.Life = (int)Math.Min(_stats.Life, _hero.Life + heal);
            }

            if (foe.Life <= 0) { Kill(foe); }
        }

        private void Kill(Foe foe)
        {
            foe.Dead = true;
            foe.Life = 0;
            _record.Kills++;
            _events |= RunEvents.FoeKilled;
            if (!foe.Dropped)
            {
                foe.Dropped = true;
                DropLoot(foe);
            }

            if (foe.Kind == FoeKind.Boss)
            {
                Status = RunStatus.Won;
                VictoryTimer = _config.VictoryDelay;
                _projectiles.Clear();
                _events |= RunEvents.BossKilled;
            }
        }

        internal void HurtHero(int raw, Element element)
        {
            var h = _hero;
            if (h.Dead || h.Invuln > 0 || Status == RunStatus.Won) { return; }
            long damage = element switch
            {
                Element.Physical => (long)raw * 100 / (100 + _stats.Armor),
                Element.Fire => (long)raw * (100 - _stats.FireResist) / 100,
                _ => (long)raw * (100 - _stats.ColdResist) / 100,
            };
            damage = Math.Max(1, damage);
            h.Life = (int)Math.Max(0, h.Life - damage);
            h.Invuln = _config.InvulnTicks;
            _events |= RunEvents.Hurt;
            if (h.Life == 0)
            {
                h.Dead = true;
                h.DeathTimer = _config.DeathTicks;
                h.SwingHitTick = -1;
                h.SpinHitTick = -1;
                _record.Deaths++;
                _events |= RunEvents.Died;
            }
        }

        private void Respawn()
        {
            var h = _hero;
            h.Dead = false;
            h.Life = _stats.Life;
            h.Position = Map.Entrance;
            h.Invuln = _config.InvulnTicks;
            h.AttackCooldown = 0;
            h.SkillCooldown = 0;
            foreach (var foe in _foes)
            {
                if (!foe.Dead) { ResetFoe(foe); }
            }

            _projectiles.Clear();
            _events |= RunEvents.Respawned;
        }

        private void DrinkPotion()
        {
            var h = _hero;
            if (h.Potions <= 0 || h.PotionCooldown > 0 || h.Life >= _stats.Life) { return; }
            h.Potions--;
            h.PotionCooldown = _config.PotionCooldown;
            long heal = Math.Max(1, (long)_stats.Life * _config.PotionHealPercent / 100);
            h.Life = (int)Math.Min(_stats.Life, h.Life + heal);
            _record.PotionsUsed++;
            _events |= RunEvents.PotionUsed;
        }

        private void EnterFloor(int number)
        {
            FloorNumber = number;
            Map = _floors[number];
            _hero.Position = Map.Entrance;
            _hero.SwingHitTick = -1;
            _hero.SpinHitTick = -1;
            _projectiles.Clear();
            _ground.Clear();
            _foes.Clear();
            foreach (var spawn in Map.Spawns)
            {
                var stats = _config.Foe(spawn.Kind);
                bool boss = spawn.Kind == FoeKind.Boss;
                long life = boss ? stats.Life : (long)stats.Life * _config.FloorLifePercent[number - 1] / 100;
                long damage = boss ? stats.Damage : (long)stats.Damage * _config.FloorDamagePercent[number - 1] / 100;
                var foe = new Foe(spawn, (int)Math.Max(1, Math.Min(int.MaxValue / 2, life)), (int)Math.Min(int.MaxValue / 2, damage));
                ResetFoe(foe);
                _foes.Add(foe);
            }

            if (number > 1) { _events |= RunEvents.NextFloor; }
        }

        private void ResetFoe(Foe foe)
        {
            foe.Reset();
            if (foe.Kind == FoeKind.Boss)
            {
                foe.FanTimer = _config.BossFanInterval;
                foe.ChargeTimer = _config.BossChargeInterval;
            }
        }

        private int FoeRadius(Foe foe) => _config.Foe(foe.Kind).Radius;
    }
}
