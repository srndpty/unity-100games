using System;
using IntGeometry;

namespace Game.Core
{
    /// <summary>戦闘と敵の考え。</summary>
    public sealed partial class CrawlRun
    {
        private void PlayerAttack(Monster target)
        {
            var stats = Rules.StatsOf(target.Kind);
            if (target.Asleep)
            {
                target.Asleep = false;
                Emit(EventKind.MonsterWoke, target.Id, -1, target.Kind, default, target.Position, 0);
            }

            if (!_actionRng.Chance(Rules.HitPercent))
            {
                Emit(EventKind.Attack, 0, target.Id, target.Kind, default, target.Position, -1);
                return;
            }

            var damage = RollDamage(Player.Attack, stats.Defense);
            Emit(EventKind.Attack, 0, target.Id, target.Kind, default, target.Position, damage);
            DamageMonster(target, damage);
        }

        /// <summary>敵に自機の攻撃のダメージを与え、倒れたら経験値と盗んだ品を落とす。</summary>
        private void DamageMonster(Monster target, int damage)
        {
            target.Hp -= damage;
            if (target.Hp > 0) { return; }
            _monsters.Remove(target);
            Scheduler.Remove(target.Id);
            Kills++;
            Emit(EventKind.Killed, target.Id, -1, target.Kind, default, target.Position, 0);
            if (target.Held.HasValue) { PlaceItem(target.Held.Value, target.Position, EventKind.Dropped, target.Id); }
            GainExp(Rules.StatsOf(target.Kind).Exp);
        }

        private void GainExp(int exp)
        {
            var p = Player;
            p.Exp += exp;
            while (p.Level < Rules.MaxLevel && p.Exp >= Rules.LevelExp[p.Level - 1])
            {
                p.Level++;
                p.MaxHp += Rules.LevelHp;
                p.Hp += Rules.LevelHp;
                p.Attack += Rules.LevelAttack;
                if (p.Level % Rules.LevelDefenseEvery == 0) { p.Defense++; }
                Emit(EventKind.LevelUp, 0, -1, default, default, p.Position, p.Level);
            }
        }

        private void HurtPlayer(Monster attacker, int damage)
        {
            Player.Hp -= damage;
            if (Player.Hp <= 0) { Die(DeathCause.Monster, attacker.Kind); }
        }

        /// <summary>敵の 1 行動。</summary>
        private void MonsterAct(Monster m)
        {
            if (m.SleepTurns > 0)
            {
                m.SleepTurns--;
                if (m.SleepTurns == 0 && !m.Asleep) { Emit(EventKind.MonsterWoke, m.Id, -1, m.Kind, default, m.Position, 0); }
                return;
            }

            var player = Player.Position;
            if (m.Asleep)
            {
                if (Directions.Distance(m.Position, player) <= 1)
                {
                    m.Asleep = false;
                    Emit(EventKind.MonsterWoke, m.Id, -1, m.Kind, default, m.Position, 0);
                }

                return;
            }

            // 視界は対称なので、自機から見えるマスにいる敵は自機を見ている。
            var sees = IsVisible(m.Position);
            var chases = !(m.Kind == MonsterKind.Thief && m.Held.HasValue);
            if (sees && chases)
            {
                m.LastSeen = player;
                m.WanderTarget = null;
                if (Directions.TryBetween(m.Position, player, out var toPlayer) && Grid.CanStep(m.Position, toPlayer))
                {
                    MeleeOrSteal(m);
                    return;
                }

                if (m.Kind == MonsterKind.Archer && CanShoot(m))
                {
                    Shoot(m);
                    return;
                }

                StepToward(m, player);
                return;
            }

            if (chases && m.LastSeen.HasValue)
            {
                if (m.Position == m.LastSeen.Value || !StepToward(m, m.LastSeen.Value)) { m.LastSeen = null; }
                return;
            }

            Wander(m);
        }

        private void MeleeOrSteal(Monster m)
        {
            var stats = Rules.StatsOf(m.Kind);
            var hit = _actionRng.Chance(Rules.HitPercent);
            if (m.Kind == MonsterKind.Thief && Player.Inventory.Count > 0)
            {
                if (!hit)
                {
                    Emit(EventKind.Attack, m.Id, 0, m.Kind, default, m.Position, -1);
                    return;
                }

                var slot = _actionRng.Next(Player.Inventory.Count);
                var item = Player.Inventory[slot];
                Player.Inventory.RemoveAt(slot);
                m.Held = item;
                m.LastSeen = null;
                Emit(EventKind.Stolen, m.Id, 0, m.Kind, item, m.Position, 0);
                var from = m.Position;

                // 自機から見えないマスへ跳ぶ（なければ空いているどこか）。
                if (TryRandomFreeTile(p => !IsVisible(p), out var to) || TryRandomFreeTile(_ => true, out to))
                {
                    m.Position = to;
                }

                Emit(EventKind.Vanished, m.Id, -1, m.Kind, item, from, 0);
                return;
            }

            if (!hit)
            {
                Emit(EventKind.Attack, m.Id, 0, m.Kind, default, m.Position, -1);
                return;
            }

            var damage = RollDamage(stats.Attack, Player.Defense);
            Emit(EventKind.Attack, m.Id, 0, m.Kind, default, m.Position, damage);
            HurtPlayer(m, damage);
        }

        /// <summary>射手：同じ直線（8 方向）・射程内・間の全てのマスを地形と敵が遮らない。</summary>
        private bool CanShoot(Monster m)
        {
            var d = Player.Position - m.Position;
            var dist = Directions.Distance(m.Position, Player.Position);
            if (dist < 2 || dist > Rules.StatsOf(m.Kind).Range) { return false; }
            if (d.X != 0 && d.Y != 0 && Math.Abs(d.X) != Math.Abs(d.Y)) { return false; }
            var step = new Vec2i(Math.Sign(d.X), Math.Sign(d.Y));
            if (!Directions.TryBetween(Vec2i.Zero, step, out var dir)) { return false; }
            var p = m.Position;
            for (int i = 0; i < dist; i++)
            {
                if (!Grid.CanStep(p, dir)) { return false; }
                p += step;
                if (p != Player.Position && MonsterAt(p) != null) { return false; }
            }

            return true;
        }

        private void Shoot(Monster m)
        {
            var stats = Rules.StatsOf(m.Kind);
            if (!_actionRng.Chance(Rules.HitPercent))
            {
                Emit(EventKind.Shot, m.Id, 0, m.Kind, default, m.Position, -1);
                return;
            }

            var damage = RollDamage(stats.ArrowDamage, Player.Defense);
            Emit(EventKind.Shot, m.Id, 0, m.Kind, default, m.Position, damage);
            HurtPlayer(m, damage);
        }

        /// <summary>
        /// 目的地へ 1 歩近づく（地形の距離が最も縮む空きマス、同じなら方向の順）。動けなければ false。
        /// </summary>
        private bool StepToward(Monster m, Vec2i target)
        {
            var dist = DistancesTo(target);
            var here = dist[Grid.Index(m.Position)];
            if (here < 0) { return false; }
            var best = -1;
            var bestDist = here;
            for (int d = 0; d < Directions.Count; d++)
            {
                var dir = (Direction)d;
                if (!Grid.CanStep(m.Position, dir)) { continue; }
                var q = m.Position + dir.Offset();
                if (q == Player.Position || MonsterAt(q) != null) { continue; }
                var qd = dist[Grid.Index(q)];
                if (qd >= 0 && qd < bestDist)
                {
                    bestDist = qd;
                    best = d;
                }
            }

            if (best < 0) { return false; }
            m.Position += ((Direction)best).Offset();
            return true;
        }

        /// <summary>巡回：無作為に選んだ部屋の中のマスへ向かう。着いたか進めなければ行き先を選び直す。</summary>
        private void Wander(Monster m)
        {
            if (!m.WanderTarget.HasValue || m.WanderTarget.Value == m.Position)
            {
                var room = Grid.Rooms[_actionRng.Next(Grid.Rooms.Count)];
                m.WanderTarget = new Vec2i(_actionRng.Range(room.X0, room.X1), _actionRng.Range(room.Y0, room.Y1));
            }

            if (!StepToward(m, m.WanderTarget.Value)) { m.WanderTarget = null; }
        }
    }
}
