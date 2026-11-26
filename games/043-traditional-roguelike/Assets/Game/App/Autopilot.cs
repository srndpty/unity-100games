using System;
using System.Collections.Generic;
using Game.Core;
using IntGeometry;

namespace Game.App
{
    /// <summary>
    /// 自動の操縦役。<see cref="PlayerView"/>（既知マップ・視界の中の敵・見えた品）だけで決めるので、人が画面から得られる以上の情報を使わない。
    /// <list type="number">
    /// <item>HP が少なければ回復薬、なければ（隣に敵がいれば）跳躍の巻物で逃げる。</item>
    /// <item>空腹ならパンを食べる。</item>
    /// <item>隣の起きている敵を攻撃する（HP の少ない方から）。</item>
    /// <item>直線上の起きている射手・岩人形・大蛇には石・毒薬を投げる（向きが違えば先に向きを変える）。</item>
    /// <item>見えた・覚えている品を拾いに行く。</item>
    /// <item>未知のマスに接する既知の床（境界）へ探索する。空腹で食べ物がなければ探索を打ち切る。</item>
    /// <item>探索し終えたら階段へ行って降りる。地図の巻物は階段を知らなければ読む。</item>
    /// </list>
    /// 眠っている敵の隣は通らない（起こさない）。経路は既知の床だけの幅優先探索（8 方向・角抜けなし）。
    /// </summary>
    public sealed class Autopilot : IPilot
    {
        private static readonly Direction[] _all =
        {
            Direction.North, Direction.NorthEast, Direction.East, Direction.SouthEast,
            Direction.South, Direction.SouthWest, Direction.West, Direction.NorthWest,
        };

        private readonly CrawlConfig _config;
        private int[] _dist = Array.Empty<int>();
        private int[] _first = Array.Empty<int>();
        private readonly Queue<int> _queue = new();
        private readonly Dictionary<int, Vec2i> _sleepers = new();
        private int _floor;

        public Autopilot(CrawlConfig config)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
        }

        /// <summary>HP の割合（%）がこれ以下なら回復薬を飲む。</summary>
        public int HealPercent { get; set; } = 40;

        /// <summary>満腹度がこれ以下ならパンを食べる。</summary>
        public int EatAt { get; set; } = 8;

        /// <summary>試合と階が変わったら、覚えた眠っている敵を捨てる（023）。</summary>
        public void Reset()
        {
            _sleepers.Clear();
            _floor = 0;
        }

        public Command Decide(PlayerView view)
        {
            if (view == null) { throw new ArgumentNullException(nameof(view)); }
            var me = view.Position;
            RememberSleepers(view);
            var adjacent = AdjacentEnemy(view);

            if (view.Hp * 100 <= view.MaxHp * HealPercent)
            {
                if (TryFind(view, ItemKind.HealPotion, out var heal)) { return Command.Use(heal); }
                if (adjacent.HasValue && view.Hp * 100 <= view.MaxHp * 25 && TryFind(view, ItemKind.WarpScroll, out var warp)) { return Command.Use(warp); }
            }

            if (view.Satiety <= EatAt)
            {
                if (TryFind(view, ItemKind.Bread, out var bread)) { return Command.Use(bread); }
                if (TryFind(view, ItemKind.BigBread, out var big)) { return Command.Use(big); }
            }

            if (adjacent.HasValue) { return Command.Move(adjacent.Value); }

            var throwing = ThrowAt(view);
            if (throwing.HasValue) { return throwing.Value; }

            if (view.TileAt(view.Position) == null) { return Command.Wait(); }
            if (view.ItemAt(me).HasValue && view.Inventory.Count < view.InventoryCapacity) { return Command.Pickup(); }

            var stairsKnown = IsKnownStairs(view, out var stairs);
            if (!stairsKnown && TryFind(view, ItemKind.MapScroll, out var map)) { return Command.Use(map); }

            // 眠っている敵の隣を避けて計画し、行き先がなければ避けずに計画し直す（詰まって餓死しない）。
            for (int pass = 0; pass < 2; pass++)
            {
                Search(view, avoidSleepers: pass == 0);
                var plan = Plan(view, stairsKnown, stairs);
                if (plan.HasValue) { return plan.Value; }
            }

            // 行き先が全て眠っている敵の向こうなら、起こしてでも倒して道を空ける。
            foreach (var m in view.Monsters)
            {
                if (Directions.TryBetween(me, m.Position, out var d) && CanStep(view, me, d)) { return Command.Move(d); }
            }

            var sleeper = Nearest(view, p => NextToMonster(view, p));
            if (sleeper >= 0) { return Step(sleeper); }

            return Command.Wait();
        }

        /// <summary>探索の結果から行き先を決める：起きている敵 → 品 → 未知の境界 → 階段。</summary>
        private Command? Plan(PlayerView view, bool stairsKnown, Vec2i stairs)
        {
            // 見えている起きた敵へ向かって戦う（離れた所から射つ射手に射られ続けない）。
            var foe = Nearest(view, p => NextToAwakeEnemy(view, p));
            if (foe >= 0) { return Step(foe); }

            if (view.Inventory.Count < view.InventoryCapacity)
            {
                var item = Nearest(view, p => view.ItemAt(p).HasValue);
                if (item >= 0) { return Step(item); }
            }

            var starving = view.Satiety <= 15 && !Has(view, ItemKind.Bread) && !Has(view, ItemKind.BigBread);
            if (!(starving && stairsKnown))
            {
                var frontier = Nearest(view, p => IsFrontier(view, p));
                if (frontier >= 0) { return Step(frontier); }
            }

            if (stairsKnown)
            {
                if (view.Position == stairs) { return Command.Descend(); }
                if (_dist[Index(view, stairs)] > 0) { return Step(Index(view, stairs)); }
            }

            return null;
        }

        private static bool NextToMonster(PlayerView view, Vec2i p)
        {
            foreach (var m in view.Monsters)
            {
                if (Directions.TryBetween(p, m.Position, out var d) && CanStep(view, p, d)) { return true; }
            }

            return false;
        }

        private static bool NextToAwakeEnemy(PlayerView view, Vec2i p)
        {
            foreach (var m in view.Monsters)
            {
                if (m.Sleeping) { continue; }
                if (Directions.TryBetween(p, m.Position, out var d) && CanStep(view, p, d)) { return true; }
            }

            return false;
        }

        private static bool TryFind(PlayerView view, ItemKind kind, out int slot)
        {
            for (int i = 0; i < view.Inventory.Count; i++)
            {
                if (view.Inventory[i] == kind)
                {
                    slot = i;
                    return true;
                }
            }

            slot = -1;
            return false;
        }

        private static bool Has(PlayerView view, ItemKind kind) => TryFind(view, kind, out _);

        private static int Index(PlayerView view, Vec2i p) => (p.Y * view.Width) + p.X;

        private static bool Walkable(PlayerView view, Vec2i p)
        {
            var t = view.TileAt(p);
            return t.HasValue && t.Value != Tile.Wall;
        }

        /// <summary>既知の地形で、隣のマスへ動けるか（角抜けなし）。</summary>
        private static bool CanStep(PlayerView view, Vec2i from, Direction d)
        {
            var o = d.Offset();
            if (!Walkable(view, from + o)) { return false; }
            if (!d.IsDiagonal()) { return true; }
            return Walkable(view, new Vec2i(from.X + o.X, from.Y)) && Walkable(view, new Vec2i(from.X, from.Y + o.Y));
        }

        private static Direction? AdjacentEnemy(PlayerView view)
        {
            Direction? best = null;
            var bestHp = int.MaxValue;
            foreach (var m in view.Monsters)
            {
                if (m.Sleeping) { continue; }
                if (!Directions.TryBetween(view.Position, m.Position, out var d) || !CanStep(view, view.Position, d)) { continue; }
                if (m.Hp < bestHp)
                {
                    bestHp = m.Hp;
                    best = d;
                }
            }

            return best;
        }

        /// <summary>直線上の起きている強い敵・射手へ投げる。向きが違えば向きを変える（時間を使わない）。</summary>
        private Command? ThrowAt(PlayerView view)
        {
            foreach (var m in view.Monsters)
            {
                if (m.Sleeping || m.Kind == MonsterKind.Skitter || m.Kind == MonsterKind.Thief) { continue; }
                var d = m.Position - view.Position;
                var dist = Directions.Distance(view.Position, m.Position);
                if (dist < 2 || dist > _config.ThrowRange) { continue; }
                if (d.X != 0 && d.Y != 0 && Math.Abs(d.X) != Math.Abs(d.Y)) { continue; }
                if (!Directions.TryBetween(Vec2i.Zero, new Vec2i(Math.Sign(d.X), Math.Sign(d.Y)), out var dir)) { continue; }
                if (!ClearLine(view, dir, dist)) { continue; }
                var slot = -1;
                if (m.Kind != MonsterKind.Archer && TryFind(view, ItemKind.PoisonPotion, out var poison)) { slot = poison; }
                else if (TryFind(view, ItemKind.Stone, out var stone)) { slot = stone; }
                if (slot < 0) { continue; }
                return view.Facing == dir ? Command.Throw(slot) : Command.Face(dir);
            }

            return null;
        }

        private static bool ClearLine(PlayerView view, Direction dir, int dist)
        {
            var p = view.Position;
            for (int i = 1; i < dist; i++)
            {
                if (!CanStep(view, p, dir)) { return false; }
                p += dir.Offset();
                if (view.MonsterAt(p).HasValue) { return false; }
            }

            return CanStep(view, p, dir);
        }

        private static bool IsKnownStairs(PlayerView view, out Vec2i stairs)
        {
            for (int y = 0; y < view.Height; y++)
            {
                for (int x = 0; x < view.Width; x++)
                {
                    var p = new Vec2i(x, y);
                    if (view.TileAt(p) == Tile.Stairs)
                    {
                        stairs = p;
                        return true;
                    }
                }
            }

            stairs = default;
            return false;
        }

        private static bool IsFrontier(PlayerView view, Vec2i p)
        {
            for (int d = 0; d < Directions.Count; d++)
            {
                if (!view.IsKnown(p + ((Direction)d).Offset())) { return true; }
            }

            return false;
        }

        /// <summary>
        /// 自機から既知の床を幅優先で探し、各マスへの距離と最初の 1 歩を記録する。
        /// 見えている敵のマスと、眠っている敵の隣のマスは通らない。
        /// </summary>
        private void Search(PlayerView view, bool avoidSleepers)
        {
            var size = view.Width * view.Height;
            if (_dist.Length != size)
            {
                _dist = new int[size];
                _first = new int[size];
            }

            for (int i = 0; i < size; i++) { _dist[i] = -1; }
            var start = Index(view, view.Position);
            _dist[start] = 0;
            _first[start] = -1;
            _queue.Clear();
            _queue.Enqueue(start);
            while (_queue.Count > 0)
            {
                var c = _queue.Dequeue();
                var p = new Vec2i(c % view.Width, c / view.Width);
                foreach (var dir in _all)
                {
                    if (!CanStep(view, p, dir)) { continue; }
                    var q = p + dir.Offset();
                    var qi = Index(view, q);
                    if (_dist[qi] >= 0 || Blocked(view, q, avoidSleepers)) { continue; }
                    _dist[qi] = _dist[c] + 1;
                    _first[qi] = c == start ? (int)dir : _first[c];
                    _queue.Enqueue(qi);
                }
            }
        }

        private bool Blocked(PlayerView view, Vec2i p, bool avoidSleepers)
        {
            if (view.MonsterAt(p).HasValue) { return true; }
            if (!avoidSleepers) { return false; }
            foreach (var s in _sleepers.Values)
            {
                if (Directions.Distance(s, p) <= 1) { return true; }
            }

            return false;
        }

        /// <summary>
        /// 見た眠っている敵の位置を覚える（見えなくなっても避け続け、見える・見えないで行き先が往復しないように）。
        /// 起きているのを見たか、その位置が見えていて眠った敵がいなければ忘れる。階が変わったら全て忘れる。
        /// </summary>
        private void RememberSleepers(PlayerView view)
        {
            if (view.Floor != _floor)
            {
                _sleepers.Clear();
                _floor = view.Floor;
            }

            foreach (var m in view.Monsters)
            {
                if (m.Sleeping) { _sleepers[m.Id] = m.Position; }
                else { _sleepers.Remove(m.Id); }
            }

            var gone = new List<int>();
            foreach (var pair in _sleepers)
            {
                var seen = view.MonsterAt(pair.Value);
                if (view.IsVisible(pair.Value) && (!seen.HasValue || seen.Value.Id != pair.Key)) { gone.Add(pair.Key); }
            }

            foreach (var id in gone) { _sleepers.Remove(id); }
        }

        /// <summary>条件に合う最も近いマスの番号（探索の距離で）。なければ -1。</summary>
        private int Nearest(PlayerView view, Func<Vec2i, bool> match)
        {
            var best = -1;
            var bestDist = int.MaxValue;
            for (int i = 0; i < _dist.Length; i++)
            {
                if (_dist[i] <= 0 || _dist[i] >= bestDist) { continue; }
                if (!match(new Vec2i(i % view.Width, i / view.Width))) { continue; }
                best = i;
                bestDist = _dist[i];
            }

            return best;
        }

        private Command Step(int target) => Command.Move((Direction)_first[target]);
    }
}
