using System;
using System.Collections.Generic;
using Game.Core;
using TileMotion;

namespace Game.App
{
    /// <summary>
    /// 全編を進める操縦役（撮影・テスト用）。乱数は使わない。
    /// <list type="bullet">
    /// <item>目標：今いる部屋から開いた出入口でたどれる部屋のうち、閉じた宝箱 → 取っていない全滅の小鍵 → 鍵を持っている閉じた扉 → ボス → 秘宝 の順に選ぶ（<see cref="Goal"/>）</item>
    /// <item>移動：部屋の間は開いた出入口の幅優先探索、部屋の中はマスの幅優先探索で隣のマスの中央へ歩く</item>
    /// <item>戦い：近くの敵（全滅させる部屋・ボスでは目標の敵）の隣のマスへ行き、剣が届けば向いて振る</item>
    /// <item>回避：弾・突進・近くの敵があるときだけ、<see cref="Decision"/> Tick ごとに数通りの手（止まる・4 方向へ歩く・4 方向へ転がる）を
    /// 試合の複製で「その手 → ふだんの方針」を <see cref="Horizon"/> Tick 続けて先読みし、ふだんの方針より被弾が少ない手があれば選ぶ</item>
    /// </list>
    /// 判断の状態（選んだ手と残り Tick）は 1 回の部屋の滞在の中だけで使い、別の試合・入り直した部屋では捨てる（023 の知見）。
    /// </summary>
    public sealed class Autopilot : IPilot
    {
        public const int Decision = 4;
        public const int Horizon = 30;

        private const int Dead = 24;

        private static readonly HeroInput[] _options =
        {
            new(0, 0, false, false),
            new(1, 0, false, false),
            new(-1, 0, false, false),
            new(0, 1, false, false),
            new(0, -1, false, false),
            new(1, 0, false, true),
            new(-1, 0, false, true),
            new(0, 1, false, true),
            new(0, -1, false, true),
        };

        private Dungeon? _dungeon;
        private int _entry = -1;
        private HeroInput? _committed;
        private int _left;

        public HeroInput InputFor(Dungeon dungeon)
        {
            if (dungeon == null) { throw new ArgumentNullException(nameof(dungeon)); }
            if (!ReferenceEquals(dungeon, _dungeon) || dungeon.RoomEntries != _entry)
            {
                _dungeon = dungeon;
                _entry = dungeon.RoomEntries;
                _left = 0;
            }

            if (dungeon.Status != DungeonStatus.Playing) { return HeroInput.None; }
            if (_left > 0)
            {
                _left--;
                return _committed ?? Policy(dungeon);
            }

            if (!Threatened(dungeon)) { return Policy(dungeon); }

            // 選んだ手（ふだんの方針のままを含む）を Decision Tick 続ける。
            _committed = Dodge(dungeon);
            _left = Decision - 1;
            return _committed ?? Policy(dungeon);
        }

        /// <summary>ふだんの方針（状態を持たない。先読みの中でも使う）。</summary>
        public static HeroInput Policy(Dungeon d)
        {
            if (d == null) { throw new ArgumentNullException(nameof(d)); }
            if (d.Status != DungeonStatus.Playing) { return HeroInput.None; }
            var run = d.Run;
            var goal = Goal(d);
            if (goal.Room != d.RoomIndex)
            {
                var side = NextSide(d, goal.Room);
                if (Near(d, 3) is Enemy blocker) { return Fight(d, blocker); }
                return Approach(d, Boxes.CellOf(run.Room.Arrival(side)), Push(side));
            }

            switch (goal.Kind)
            {
                case GoalKind.Chest:
                    var chest = run.Room.Chests[goal.Index].Cell;
                    if (Near(d, 3) is Enemy guard) { return Fight(d, guard); }
                    return Beside(d, chest, attack: true);
                case GoalKind.Clear:
                    if (run.RewardCell is Cell reward) { return Approach(d, reward, HeroInput.None); }
                    var target = Nearest(d);
                    return target == null ? HeroInput.None : Fight(d, target);
                case GoalKind.Door:
                    if (Near(d, 3) is Enemy keeper) { return Fight(d, keeper); }
                    return Approach(d, Boxes.CellOf(run.Room.Arrival(goal.Side)), Push(goal.Side));
                case GoalKind.Boss:
                    return run.Boss is Enemy boss ? Fight(d, boss) : HeroInput.None;
                default:
                    if (Near(d, 2) is Enemy pest) { return Fight(d, pest); }
                    return run.TreasureCell is Cell treasure ? Approach(d, treasure, HeroInput.None) : HeroInput.None;
            }
        }

        public enum GoalKind
        {
            Chest,
            Clear,
            Door,
            Boss,
            Treasure,
        }

        /// <summary>今の目標（部屋と、その部屋ですること）。</summary>
        public readonly struct Target
        {
            public Target(GoalKind kind, int room, int index = 0, Side side = Side.Left)
            {
                Kind = kind;
                Room = room;
                Index = index;
                Side = side;
            }

            public GoalKind Kind { get; }

            public int Room { get; }

            public int Index { get; }

            public Side Side { get; }
        }

        /// <summary>今の目標を選ぶ。</summary>
        public static Target Goal(Dungeon d)
        {
            if (d == null) { throw new ArgumentNullException(nameof(d)); }
            var world = d.World;
            var state = d.State;
            var order = ReachableRooms(d);
            foreach (var room in order)
            {
                for (var i = 0; i < world.Rooms[room].Chests.Count; i++)
                {
                    if (!state.IsChestOpen(room, i)) { return new Target(GoalKind.Chest, room, i); }
                }
            }

            foreach (var room in order)
            {
                if (world.Rooms[room].Reward.HasValue && !state.IsRewardTaken(room)) { return new Target(GoalKind.Clear, room); }
            }

            foreach (var room in order)
            {
                foreach (var side in _sides)
                {
                    var kind = world.Rooms[room].Door(side);
                    if (state.IsOpen(room, side)) { continue; }
                    if ((kind == DoorKind.Small && state.SmallKeys > 0) || (kind == DoorKind.Boss && state.HasBossKey)) { return new Target(GoalKind.Door, room, side: side); }
                }
            }

            return state.BossDefeated ? new Target(GoalKind.Treasure, world.BossRoom) : new Target(GoalKind.Boss, world.BossRoom);
        }

        /// <summary>今の部屋から開いた出入口でたどれる部屋（近い順）。</summary>
        private static List<int> ReachableRooms(Dungeon d)
        {
            var order = new List<int> { d.RoomIndex };
            var seen = new bool[d.World.Rooms.Count];
            seen[d.RoomIndex] = true;
            for (var i = 0; i < order.Count; i++)
            {
                foreach (var side in _sides)
                {
                    if (!d.State.IsOpen(order[i], side)) { continue; }
                    var next = d.World.Neighbor(order[i], side);
                    if (seen[next]) { continue; }
                    seen[next] = true;
                    order.Add(next);
                }
            }

            return order;
        }

        /// <summary>目標の部屋へ向かうとき、今の部屋で出る辺。</summary>
        private static Side NextSide(Dungeon d, int goal)
        {
            var count = d.World.Rooms.Count;
            var from = new int[count];
            var via = new Side[count];
            for (var i = 0; i < count; i++) { from[i] = -2; }
            from[goal] = -1;
            var queue = new Queue<int>();
            queue.Enqueue(goal);

            // 目標から逆にたどり、今の部屋から目標へ 1 歩近づく辺を求める。
            while (queue.Count > 0)
            {
                var room = queue.Dequeue();
                foreach (var side in _sides)
                {
                    if (!d.State.IsOpen(room, side)) { continue; }
                    var next = d.World.Neighbor(room, side);
                    if (from[next] != -2) { continue; }
                    from[next] = room;
                    via[next] = World.Opposite(side);
                    if (next == d.RoomIndex) { return via[next]; }
                    queue.Enqueue(next);
                }
            }

            throw new InvalidOperationException($"部屋 {goal} へたどれません。");
        }

        private static HeroInput Push(Side side) => side switch
        {
            Side.Left => new HeroInput(-1, 0, false, false),
            Side.Right => new HeroInput(1, 0, false, false),
            Side.Top => new HeroInput(0, 1, false, false),
            _ => new HeroInput(0, -1, false, false),
        };

        /// <summary>剣を押す Tick か（押しっぱなしにしないよう、1 Tick おきに押す）。</summary>
        private static bool AttackTick(Dungeon d) => d.RoomTicks % 2 == 0;

        /// <summary>
        /// マス <paramref name="cell"/> へ歩き、着いたら <paramref name="then"/> を入力する。着いたマスでは、<paramref name="then"/> が押す軸はそのまま押し
        /// （出入口・扉・宝箱へ押し当てる）、もう一方の軸だけ中央へ寄せる。寄せ終わるまで剣とロールは押さない。
        /// </summary>
        private static HeroInput Approach(Dungeon d, Cell cell, HeroInput then) => ApproachAny(d, new[] { cell }, _ => then);

        /// <summary><paramref name="cells"/> のうち歩数の最も少ないマスへ歩き、着いたら <paramref name="then"/>（着いたマスを受け取る）を入力する。</summary>
        private static HeroInput ApproachAny(Dungeon d, Cell[] cells, Func<Cell, HeroInput> then)
        {
            var here = Boxes.CellOf(d.Hero.Body);
            if (Array.IndexOf(cells, here) < 0) { return Walk(d, cells); }
            var step = Center(d, here);
            var action = then(here);
            var x = action.X != 0 ? action.X : step.X;
            var y = action.Y != 0 ? action.Y : step.Y;
            var aligned = (action.X != 0 || step.X == 0) && (action.Y != 0 || step.Y == 0);
            return new HeroInput(x, y, aligned && action.Attack, aligned && action.Roll);
        }

        /// <summary>固体のマス <paramref name="cell"/>（宝箱）の隣へ歩き、着いたらそちらを向いて剣のキーを押す。</summary>
        private static HeroInput Beside(Dungeon d, Cell cell, bool attack)
        {
            return ApproachAny(d, Around(cell), at => new HeroInput(Math.Sign(cell.Column - at.Column), Math.Sign(cell.Row - at.Row), attack && AttackTick(d), false));
        }

        /// <summary>敵を斬る。剣が届くならそちらを向いて振り、届かなければ隣のマスへ歩く。</summary>
        private static HeroInput Fight(Dungeon d, Enemy enemy)
        {
            var hero = d.Hero.Body;
            var facing = Facings.Toward((long)enemy.Body.CenterX - hero.CenterX, (long)Boxes.CenterY(enemy.Body) - Boxes.CenterY(hero), d.Hero.Facing);
            if (d.Hero.SwordFacing(facing).Overlaps(enemy.Body))
            {
                // 向いていれば動かずに振る。向いていなければ 1 Tick だけそちらへ押して向きを変える。
                var turn = facing == d.Hero.Facing ? (0, 0) : (Facings.Dx(facing), Facings.Dy(facing));
                return new HeroInput(turn.Item1, turn.Item2, AttackTick(d), false);
            }

            return Walk(d, Around(Boxes.CellOf(enemy.Body)));
        }

        private static Cell[] Around(Cell cell)
        {
            var cells = new Cell[_four.Length];
            for (var i = 0; i < _four.Length; i++) { cells[i] = new Cell(cell.Column + _four[i].Item1, cell.Row + _four[i].Item2); }
            return cells;
        }

        /// <summary>中心どうしの距離が <paramref name="tiles"/> タイル以内の、生きている敵のうち最も近いもの。</summary>
        private static Enemy? Near(Dungeon d, int tiles)
        {
            var enemy = Nearest(d);
            if (enemy == null) { return null; }
            return Distance(d.Hero.Body, enemy.Body) <= (long)tiles * Units.Tile ? enemy : null;
        }

        private static Enemy? Nearest(Dungeon d)
        {
            Enemy? best = null;
            var bestDistance = long.MaxValue;
            foreach (var enemy in d.Run.Enemies)
            {
                if (!enemy.IsAlive) { continue; }
                var distance = Distance(d.Hero.Body, enemy.Body);
                if (distance < bestDistance)
                {
                    best = enemy;
                    bestDistance = distance;
                }
            }

            return best;
        }

        private static long Distance(Box a, Box b) => Math.Max(Math.Abs((long)a.CenterX - b.CenterX), Math.Abs((long)Boxes.CenterY(a) - Boxes.CenterY(b)));

        private static readonly (int, int)[] _four = { (1, 0), (-1, 0), (0, 1), (0, -1) };

        private static readonly Side[] _sides = { Side.Left, Side.Right, Side.Top, Side.Bottom };

        /// <summary>通れるマスか。出入口のマスは通らない（うっかり部屋を出ない）。</summary>
        private static bool Walkable(Dungeon d, Cell cell)
        {
            var map = d.Run.Room.Map;
            if (!map.Contains(cell.Column, cell.Row)) { return false; }
            return !d.Run.Terrain.IsSolid(cell.Column, cell.Row) && !TileMap.IsDoorTile(map[cell.Column, cell.Row]);
        }

        /// <summary>今のマスの中央へ寄せる入力（寄っていれば両方 0）。</summary>
        private static HeroInput Center(Dungeon d, Cell cell)
        {
            var hero = d.Hero.Body;
            var center = cell.Centered(Hero.Size);
            var cx = Math.Abs(center.X - hero.X) <= Dead ? 0 : Math.Sign(center.X - hero.X);
            var cy = Math.Abs(center.Y - hero.Y) <= Dead ? 0 : Math.Sign(center.Y - hero.Y);
            return new HeroInput(cx, cy, false, false);
        }

        /// <summary>
        /// <paramref name="targets"/> のどれかへ 1 歩。目標のマスすべてから一度に歩数を数え（幅優先探索 1 回）、今のマスの隣で最も近いマスへ進む。
        /// 今のマスが目標なら中央へ寄せる。
        /// </summary>
        private static HeroInput Walk(Dungeon d, Cell[] targets)
        {
            var hero = d.Hero.Body;
            var here = Boxes.CellOf(hero);
            if (Array.IndexOf(targets, here) >= 0) { return Center(d, here); }

            var map = d.Run.Room.Map;
            var distances = new int[map.Width * map.Height];
            for (var i = 0; i < distances.Length; i++) { distances[i] = int.MaxValue; }
            var queue = new Queue<Cell>();
            foreach (var target in targets)
            {
                if (!Walkable(d, target) || distances[(target.Row * map.Width) + target.Column] == 0) { continue; }
                distances[(target.Row * map.Width) + target.Column] = 0;
                queue.Enqueue(target);
            }

            while (queue.Count > 0)
            {
                var cell = queue.Dequeue();
                if (cell == here) { break; }
                var step = distances[(cell.Row * map.Width) + cell.Column] + 1;
                foreach (var (dx, dy) in _four)
                {
                    var next = new Cell(cell.Column + dx, cell.Row + dy);
                    if (!Walkable(d, next) || distances[(next.Row * map.Width) + next.Column] != int.MaxValue) { continue; }
                    distances[(next.Row * map.Width) + next.Column] = step;
                    queue.Enqueue(next);
                }
            }

            var best = here;
            var bestDistance = map.Contains(here.Column, here.Row) ? distances[(here.Row * map.Width) + here.Column] : int.MaxValue;
            foreach (var (dx, dy) in _four)
            {
                var next = new Cell(here.Column + dx, here.Row + dy);
                if (!map.Contains(next.Column, next.Row)) { continue; }
                var distance = distances[(next.Row * map.Width) + next.Column];
                if (distance < bestDistance)
                {
                    best = next;
                    bestDistance = distance;
                }
            }

            if (best == here)
            {
                // たどれない（壁際へ押されたなど）。最初の目標のほうへまっすぐ押す。
                var center = targets[0].Centered(Hero.Size);
                return new HeroInput(Math.Sign(center.X - hero.X), Math.Sign(center.Y - hero.Y), false, false);
            }

            // 進む軸に沿って押し、もう一方の軸は今のマスの中央へ寄せる（柱の角に引っかからない）。
            var align = Center(d, here);
            return best.Column != here.Column ? new HeroInput(best.Column - here.Column, align.Y, false, false) : new HeroInput(align.X, best.Row - here.Row, false, false);
        }

        /// <summary>近づいてくる弾か、自機の帯で構え・突進中の敵があるか（先読みで避けるか考える場面）。ふつうに歩く敵は斬って片づける。</summary>
        private static bool Threatened(Dungeon d)
        {
            var hero = d.Hero.Body;
            foreach (var projectile in d.Run.Projectiles)
            {
                var dx = (long)hero.CenterX - projectile.Body.CenterX;
                var dy = (long)Boxes.CenterY(hero) - Boxes.CenterY(projectile.Body);
                var approaching = (dx * projectile.VelocityX) + (dy * projectile.VelocityY) > 0;
                if (approaching && Distance(hero, projectile.Body) < 5 * Units.Tile) { return true; }
            }

            foreach (var enemy in d.Run.Enemies)
            {
                if (!enemy.IsAlive || (enemy.Phase != EnemyPhase.Windup && enemy.Phase != EnemyPhase.Charge)) { continue; }

                // 突進は向いた軸に沿うので、その帯（体の幅に余裕を足す）に自機がいるときだけ。
                var band = Units.Tile;
                var lane = enemy.Facing == Facing.Left || enemy.Facing == Facing.Right
                    ? hero.Bottom < enemy.Body.Top + band && enemy.Body.Bottom - band < hero.Top
                    : hero.Left < enemy.Body.Right + band && enemy.Body.Left - band < hero.Right;
                if (lane) { return true; }
            }

            return false;
        }

        /// <summary>先読みで、ふだんの方針より被弾の少ない手を探す。なければ null（ふだんの方針のまま）。</summary>
        private static HeroInput? Dodge(Dungeon d)
        {
            var baseline = Evaluate(d, null);
            HeroInput? best = null;
            var bestScore = baseline;
            foreach (var option in _options)
            {
                if (option.Roll && d.Hero.RollCooldownRemaining > 0) { continue; }
                var score = Evaluate(d, option);
                if (score > bestScore)
                {
                    best = option;
                    bestScore = score;
                }
            }

            return best;
        }

        private static long Evaluate(Dungeon source, HeroInput? option)
        {
            var e = source.Clone();
            var health = e.Hero.Health;
            for (var tick = 0; tick < Horizon; tick++)
            {
                var input = option is HeroInput first && tick < Decision ? first : Policy(e);
                e.Tick(input);
                if (e.Status == DungeonStatus.Down) { return long.MinValue + tick + 1; }
                if (e.Status != DungeonStatus.Playing) { break; }
            }

            // 被弾の少なさだけで比べる（同じならふだんの方針を選ぶ）。
            return -(health - e.Hero.Health);
        }
    }
}
