using System;
using System.Collections.Generic;
using Game.Core;
using IntGeometry;
using TileMotion;

namespace Game.App
{
    /// <summary>
    /// 自動の操縦役（撮影・テスト用）。近くの敵と戦い、良い装備を拾って付け替え、階段へ、最後は番人へ向かう。
    /// 経路はタイルの幅優先探索（<see cref="TileGrid"/>）。乱数を使わないので、試合のシードだけで結果が決まる。
    /// 判断の状態（目標の経路）は試合が変わったら捨てる（023）。
    /// </summary>
    public sealed class Autopilot : IPilot
    {
        /// <summary>戦う相手を探す距離（単位）。</summary>
        private const int FightRange = 7 * Units.Tile;

        private DelveRun? _run;
        private DelveConfig _config = new();
        private int[]? _field;
        private int _fieldTarget = -1;
        private int _fieldFloor;
        private Foe? _target;

        public HeroCommand CommandFor(DelveRun run)
        {
            if (run == null) { throw new ArgumentNullException(nameof(run)); }
            if (!ReferenceEquals(run, _run))
            {
                _run = run;
                _config = run.Config;
                _field = null;
                _fieldTarget = -1;
                _target = null;
            }

            ManageInventory(run);
            var hero = run.Hero;
            if (hero.Dead || run.IsOver) { return HeroCommand.Idle(hero.Position); }

            bool potion = hero.Potions > 0 && hero.PotionCooldown == 0 && (long)hero.Life * 100 < (long)run.Stats.Life * 45;

            // 狙った敵は、倒すか十分に離れるまで追い続ける（境界で狙いが行き来して止まらないように）。
            if (_target != null && (_target.Dead || !Contains(run, _target) || Vec2i.DistanceSquared(_target.Position, hero.Position) > Square(LeaveRange)))
            {
                _target = null;
            }

            _target ??= NearestFoe(run);
            var foe = _target;
            if (foe != null) { return Fight(run, foe, potion); }

            var loot = WantedLoot(run);
            if (loot != null)
            {
                // 装備品は狙った品を番号で拾う（名前札のクリックと同じ）。回復薬は触れて拾うので上を歩く。
                bool near = !loot.IsPotion && Vec2i.DistanceSquared(loot.Position, hero.Position) < Square(_config.PickupRange - 32);
                if (near && run.PickUp(loot.Id) == ItemResult.Done)
                {
                    ManageInventory(run);
                    return new HeroCommand(0, 0, loot.Position, false, false, potion);
                }

                var (mx, my) = Toward(run, loot.Position);
                return new HeroCommand(mx, my, loot.Position, false, false, potion);
            }

            var goal = Goal(run);
            var (gx, gy) = Toward(run, goal);
            return new HeroCommand(gx, gy, goal, false, false, potion);
        }

        /// <summary>能力値の評価：平均の攻撃力 × 攻撃速度 × 会心の期待値 × 実効の HP（整数の decimal で決定的に比べる）。</summary>
        public static decimal Score(StatBlock s)
        {
            if (s == null) { throw new ArgumentNullException(nameof(s)); }
            decimal dps = (s.DamageMin + s.DamageMax) * (decimal)s.AttackSpeed * (10000 + (s.CritChance * (s.CritDamage - 100)));
            decimal ehp = s.Life * (decimal)(100 + s.Armor) * (400 + s.FireResist + s.ColdResist) * (100 + (s.Leech * 5));
            return dps * ehp;
        }

        private static long Square(long v) => v * v;

        private static void ManageInventory(DelveRun run)
        {
            bool changed = true;
            while (changed)
            {
                changed = false;
                var current = Score(run.Stats);
                for (int i = 0; i < run.Inventory.Count; i++)
                {
                    if (Better(run, run.Inventory[i], current))
                    {
                        run.Equip(i);
                        changed = true;
                        break;
                    }
                }
            }
        }

        private static bool Better(DelveRun run, Item item, decimal current)
        {
            var trial = run.Equipment.Clone();
            trial.Equip(item);
            return Score(StatSheet.Compute(run.Loot.Hero, trial)) > current;
        }

        /// <summary>狙いをやめる距離（単位）。</summary>
        private const int LeaveRange = 12 * Units.Tile;

        private static bool Contains(DelveRun run, Foe foe)
        {
            foreach (var f in run.Foes)
            {
                if (ReferenceEquals(f, foe)) { return true; }
            }

            return false;
        }

        private static Foe? NearestFoe(DelveRun run)
        {
            Foe? best = null;
            long bestDistance = long.MaxValue;
            foreach (var f in run.Foes)
            {
                if (f.Dead) { continue; }
                long d = Vec2i.DistanceSquared(f.Position, run.Hero.Position);
                bool interested = f.Aggro || d <= Square(FightRange);
                if (f.Kind == FoeKind.Boss) { interested = true; }
                if (interested && d < bestDistance)
                {
                    best = f;
                    bestDistance = d;
                }
            }

            // 番人は他の敵がいない（近くにいない）ときに向かう。
            if (best != null && best.Kind == FoeKind.Boss)
            {
                foreach (var f in run.Foes)
                {
                    if (!f.Dead && f.Kind != FoeKind.Boss && (f.Aggro || Vec2i.DistanceSquared(f.Position, run.Hero.Position) <= Square(FightRange)))
                    {
                        return f;
                    }
                }
            }

            return best;
        }

        private HeroCommand Fight(DelveRun run, Foe foe, bool potion)
        {
            var hero = run.Hero;
            var stats = _config.Foe(foe.Kind);
            long d = IntMath.Sqrt(Vec2i.DistanceSquared(foe.Position, hero.Position));
            long reach = _config.AttackReach + _config.AttackRadius + stats.Radius - 48;
            int near = 0;
            foreach (var f in run.Foes)
            {
                if (!f.Dead && Vec2i.DistanceSquared(f.Position, hero.Position) < Square(_config.SkillRadius + _config.Foe(f.Kind).Radius - 32)) { near++; }
            }

            bool skill = hero.SkillCooldown == 0 && (near >= 2 || (foe.Kind == FoeKind.Boss && near >= 1));
            if (d <= reach) { return new HeroCommand(0, 0, foe.Position, true, skill, potion); }
            var (mx, my) = Toward(run, foe.Position);
            return new HeroCommand(mx, my, foe.Position, false, skill, potion);
        }

        private GroundItem? WantedLoot(DelveRun run)
        {
            if (run.Inventory.Count >= _config.InventorySize) { return null; }
            var current = Score(run.Stats);
            GroundItem? best = null;
            long bestDistance = long.MaxValue;
            foreach (var g in run.Ground)
            {
                if (g.IsPotion)
                {
                    if (run.Hero.Potions >= _config.MaxPotions) { continue; }
                }
                else if (!Better(run, g.Item!, current))
                {
                    continue;
                }

                long d = Vec2i.DistanceSquared(g.Position, run.Hero.Position);
                if (d < bestDistance)
                {
                    best = g;
                    bestDistance = d;
                }
            }

            return best;
        }

        /// <summary>行き先：まだ生きている（番人以外の）最も近い敵、いなければ階段か番人。階を一掃して装備を集める。</summary>
        private static Vec2i Goal(DelveRun run)
        {
            Foe? nearest = null;
            long best = long.MaxValue;
            foreach (var f in run.Foes)
            {
                if (f.Dead || f.Kind == FoeKind.Boss) { continue; }
                long d = Vec2i.DistanceSquared(f.Position, run.Hero.Position);
                if (d < best)
                {
                    best = d;
                    nearest = f;
                }
            }

            if (nearest != null) { return nearest.Position; }
            var map = run.Map;
            if (map.HasStairs) { return FloorMap.TileCenter(map.StairsColumn, map.StairsRow); }
            foreach (var f in run.Foes)
            {
                if (f.Kind == FoeKind.Boss) { return f.Position; }
            }

            return run.Hero.Position;
        }

        /// <summary>目標へ向かう 1 Tick の向き。目標のタイルからの歩数の場を作り、歩数の減る隣のタイルの中心へ寄る。</summary>
        private (int X, int Y) Toward(DelveRun run, Vec2i target)
        {
            var map = run.Map;
            int tc = FloorMap.ColumnOf(target.X);
            int tr = FloorMap.RowOf(target.Y);
            int targetCell = (tr * map.Width) + tc;
            if (_field == null || _fieldTarget != targetCell || _fieldFloor != run.FloorNumber)
            {
                _field = TileGrid.Distances(map, tc, tr);
                _fieldTarget = targetCell;
                _fieldFloor = run.FloorNumber;
            }

            var pos = run.Hero.Position;
            int hc = FloorMap.ColumnOf(pos.X);
            int hr = FloorMap.RowOf(pos.Y);
            int here = _field[(hr * map.Width) + hc];
            Vec2i point = target;
            if (here > 0)
            {
                foreach (var (dc, dr) in _neighbors)
                {
                    int nc = hc + dc;
                    int nr = hr + dr;
                    if (map.IsSolid(nc, nr)) { continue; }
                    int v = _field[(nr * map.Width) + nc];
                    if (v >= 0 && v < here)
                    {
                        point = FloorMap.TileCenter(nc, nr);
                        break;
                    }
                }
            }

            var delta = point - pos;
            const int deadZone = 6;
            int mx = Math.Abs(delta.X) > deadZone ? Math.Sign(delta.X) : 0;
            int my = Math.Abs(delta.Y) > deadZone ? Math.Sign(delta.Y) : 0;
            return (mx, my);
        }

        private static readonly (int, int)[] _neighbors = { (1, 0), (-1, 0), (0, 1), (0, -1) };
    }

    /// <summary>
    /// 人に近い操縦役：歩く向きは毎 Tick 決めるが、攻撃・薙ぎ払い・回復薬・照準は <see cref="ReactionTicks"/> 前の判断を使う
    /// （敵の動きに遅れて反応する）。
    /// </summary>
    public sealed class HumanlikePilot : IPilot
    {
        private readonly Autopilot _brain = new();
        private readonly Queue<HeroCommand> _late = new();
        private DelveRun? _run;

        public HumanlikePilot(int reactionTicks)
        {
            if (reactionTicks < 1) { throw new ArgumentOutOfRangeException(nameof(reactionTicks)); }
            ReactionTicks = reactionTicks;
        }

        public int ReactionTicks { get; }

        public HeroCommand CommandFor(DelveRun run)
        {
            if (run == null) { throw new ArgumentNullException(nameof(run)); }
            if (!ReferenceEquals(run, _run))
            {
                _run = run;
                _late.Clear();
            }

            var now = _brain.CommandFor(run);
            _late.Enqueue(now);
            if (_late.Count <= ReactionTicks) { return new HeroCommand(now.MoveX, now.MoveY, now.Aim, false); }
            var old = _late.Dequeue();
            return new HeroCommand(now.MoveX, now.MoveY, old.Aim, old.Attack, old.Skill, old.Potion);
        }
    }
}
