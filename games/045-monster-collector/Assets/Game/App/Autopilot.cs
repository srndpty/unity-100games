using System;
using System.Collections.Generic;
using Game.Core;
using IntGeometry;

namespace Game.App
{
    /// <summary>
    /// 操縦役。Flow にキー入力（意図）だけを送り、タイトルから結果まで遊ぶ（撮影台本・App テスト・PlayMode で共有）。
    /// 1 回の <see cref="Step"/> で多くとも 1 回押す。判断の状態は試合（World）が変わったら捨てる（023）。
    /// 方針：最初の草むらで仲間を増やし、トレーナーより弱ければ草むらで鍛え、弱ったら回復所へ戻り、強くなったらトレーナーに話しかける。
    /// </summary>
    public sealed class Autopilot
    {
        private World? _world;
        private RandomSeries _rng = new(1);
        private BattleCommand? _plan;
        private int _lastSwitchTurn = -1;
        private int _wanderDir = 1;

        /// <summary>回復所に寄ったら記録する。</summary>
        public bool SaveAtCenter { get; set; }

        /// <summary>この数の種族を捕まえるまで、捕まえていない種族を狙う。</summary>
        public int WantSpecies { get; set; } = 6;

        /// <summary>トレーナーに挑む前の余裕（レベル）。</summary>
        public int LevelMargin { get; set; } = 1;

        public void Step(KeeperFlow flow)
        {
            if (flow == null) { throw new ArgumentNullException(nameof(flow)); }
            if (!ReferenceEquals(flow.World, _world))
            {
                _world = flow.World;
                _rng = new RandomSeries(_world?.Seed ?? 1);
                _plan = null;
                _lastSwitchTurn = -1;
            }

            if (flow.IsPaused || flow.InDialogue)
            {
                flow.Tap(InputButton.Confirm);
                return;
            }

            switch (flow.Screen)
            {
                case FlowScreen.Title:
                    Select(flow, 0);
                    break;
                case FlowScreen.HowTo:
                case FlowScreen.FieldMenu:
                case FlowScreen.Party:
                case FlowScreen.Dex:
                case FlowScreen.Storage:
                    flow.Tap(InputButton.Cancel);
                    break;
                case FlowScreen.StarterPick:
                    Select(flow, (int)((uint)flow.World!.Seed % (uint)flow.Items.Count));
                    break;
                case FlowScreen.CenterMenu:
                    if (SaveAtCenter && !_savedHere)
                    {
                        if (flow.Cursor == 0) { _savedHere = true; }
                        Select(flow, 0);
                    }
                    else
                    {
                        Select(flow, 2);
                    }

                    break;
                case FlowScreen.Field:
                    _savedHere = false;
                    DriveField(flow);
                    break;
                case FlowScreen.Battle:
                    DriveBattle(flow);
                    break;
                case FlowScreen.LearnMove:
                    Select(flow, ChooseForget(flow));
                    break;
                case FlowScreen.Result:
                    flow.Tap(InputButton.Confirm);
                    break;
            }
        }

        private bool _savedHere;

        private static void Select(KeeperFlow flow, int index)
        {
            if (flow.Items.Count == 0) { return; }
            index = Math.Max(0, Math.Min(index, flow.Items.Count - 1));
            if (flow.Cursor == index)
            {
                flow.Tap(InputButton.Confirm);
                return;
            }

            var down = (index - flow.Cursor + flow.Items.Count) % flow.Items.Count;
            flow.Tap(down <= flow.Items.Count - down ? InputButton.Down : InputButton.Up);
        }

        // --- 戦闘 ---

        private void DriveBattle(KeeperFlow flow)
        {
            var battle = flow.World!.Battle;
            if (flow.BattleMenu == BattleMenu.Playing || battle == null)
            {
                _plan = null;
                flow.Tap(InputButton.Confirm);
                return;
            }

            _plan ??= Decide(flow.World!, battle);
            var plan = _plan.Value;
            switch (flow.BattleMenu)
            {
                case BattleMenu.ForcedSwitch:
                case BattleMenu.Switch:
                    if (plan.Kind != CommandKind.Switch) { Back(flow); }
                    else { Select(flow, plan.Index); }
                    break;
                case BattleMenu.Root:
                    Select(flow, plan.Kind switch
                    {
                        CommandKind.Fight => 0,
                        CommandKind.Switch => 1,
                        CommandKind.Run => 3,
                        _ => 2,
                    });
                    break;
                case BattleMenu.Moves:
                    if (plan.Kind != CommandKind.Fight) { Back(flow); }
                    else { Select(flow, plan.Index); }
                    break;
                case BattleMenu.Items:
                    if (plan.Kind == CommandKind.Potion) { Select(flow, 0); }
                    else if (plan.Kind == CommandKind.Ball) { Select(flow, 1); }
                    else { Back(flow); }
                    break;
                case BattleMenu.PotionTarget:
                    if (plan.Kind != CommandKind.Potion) { Back(flow); }
                    else { Select(flow, plan.Index); }
                    break;
            }

            // 拒否されたら（Notice が出たら）考え直す。
            if (flow.Notice.Length > 0) { _plan = null; }
        }

        private void Back(KeeperFlow flow)
        {
            _plan = null;
            flow.Tap(InputButton.Cancel);
        }

        private BattleCommand Decide(World world, Battle b)
        {
            var data = world.Data;
            if (b.Phase == BattlePhase.ForcedSwitch) { return BattleCommand.Switch(BestMember(data, b, -1)); }
            var me = b.PlayerMonster;
            var foe = b.FoeMonster;
            if (me.Hp * 10 < me.MaxHp * 3 && b.Potions > 0 && foe.Hp * 4 > foe.MaxHp) { return BattleCommand.Potion(b.Active); }

            // 相性が悪ければ一度だけ入れかえる。
            var mine = BestScore(data, me, foe);
            if (b.Turn != _lastSwitchTurn + 1 && b.HasHealthyBench())
            {
                var best = BestMember(data, b, b.Active);
                if (best >= 0 && BestScore(data, b.Party[best], foe) > mine * 2 && b.Party[best].Level + 2 >= me.Level)
                {
                    _lastSwitchTurn = b.Turn;
                    return BattleCommand.Switch(best);
                }
            }

            if (b.Kind == BattleKind.Wild)
            {
                var want = (world.Caught & (1 << foe.Species.Index)) == 0 && world.CaughtCount < WantSpecies;
                if (want && b.Balls > 0 && b.HasRoom)
                {
                    if (foe.Hp * 2 <= foe.MaxHp || foe.Status != null || foe.Species.CatchRate >= 150) { return BattleCommand.Ball(); }
                    return BattleCommand.Fight(GentlestMove(data, me, foe));
                }
            }

            return BattleCommand.Fight(BestMove(data, me, foe));
        }

        private static long BestScore(MonsterData data, Monster me, Monster foe)
        {
            long best = 0;
            foreach (var s in me.Moves)
            {
                if (s.Pp > 0 && s.Move.Power > 0) { best = Math.Max(best, TrainerAi.Score(data, me, foe, _zero, _zero, s.Move)); }
            }

            return best;
        }

        private static readonly IReadOnlyList<int> _zero = Array.AsReadOnly(new int[3]);

        private static int BestMove(MonsterData data, Monster me, Monster foe)
        {
            var best = 0;
            var bestScore = long.MinValue;
            for (int i = 0; i < me.Moves.Count; i++)
            {
                var s = me.Moves[i];
                if (s.Pp <= 0) { continue; }
                var score = s.Move.Power > 0 ? TrainerAi.Score(data, me, foe, _zero, _zero, s.Move) : -1;
                if (score > bestScore)
                {
                    bestScore = score;
                    best = i;
                }
            }

            return best;
        }

        /// <summary>捕まえたい相手には、眠らせる技か、いちばん弱い攻撃で削る。</summary>
        private static int GentlestMove(MonsterData data, Monster me, Monster foe)
        {
            var best = -1;
            var bestScore = long.MaxValue;
            for (int i = 0; i < me.Moves.Count; i++)
            {
                var s = me.Moves[i];
                if (s.Pp <= 0) { continue; }
                foreach (var e in s.Move.Effects)
                {
                    if (e.Kind == EffectKind.Inflict && e.Status!.BlocksAction && foe.Status == null && s.Move.Power == 0) { return i; }
                }

                if (s.Move.Power == 0) { continue; }
                var score = TrainerAi.Score(data, me, foe, _zero, _zero, s.Move);
                if (score < bestScore)
                {
                    bestScore = score;
                    best = i;
                }
            }

            return best >= 0 ? best : BestMove(data, me, foe);
        }

        private static int BestMember(MonsterData data, Battle b, int exclude)
        {
            var best = -1;
            long bestScore = long.MinValue;
            for (int i = 0; i < b.Party.Count; i++)
            {
                var m = b.Party[i];
                if (i == exclude || i == b.Active || m.IsFainted) { continue; }
                var score = BestScore(data, m, b.FoeMonster) * 100 + m.Level * 10 + m.Hp * 10 / Math.Max(1, m.MaxHp);
                if (score > bestScore)
                {
                    bestScore = score;
                    best = i;
                }
            }

            return best;
        }

        private static int ChooseForget(KeeperFlow flow)
        {
            var w = flow.World!;
            if (w.PendingLearns.Count == 0) { return flow.Items.Count - 1; }
            var p = w.PendingLearns[0];
            var m = w.Roster.Party[p.Member];
            var worst = -1;
            var worstValue = MoveValue(p.Move);
            for (int i = 0; i < m.Moves.Count; i++)
            {
                var v = MoveValue(m.Moves[i].Move);
                if (v < worstValue)
                {
                    worstValue = v;
                    worst = i;
                }
            }

            return worst >= 0 ? worst : flow.Items.Count - 1;
        }

        private static int MoveValue(MoveDef move)
        {
            var v = move.Power * move.Accuracy / 100 * (move.Type != null ? 3 : 2) / 2;
            foreach (var e in move.Effects)
            {
                if (e.Kind == EffectKind.Inflict && e.Status!.BlocksAction) { v += 50; }
            }

            return v;
        }

        // --- フィールド ---

        private void DriveField(KeeperFlow flow)
        {
            var w = flow.World!;
            var e = w.Explorer;
            if (e.IsMoving) { return; }
            var data = w.Data;
            var rules = data.Rules;

            if (NeedsCenter(w))
            {
                GoAndFace(flow, w, rules.CenterMapId, rules.CenterX, rules.CenterY, Direction.Up);
                return;
            }

            var trainer = NextTrainer(w);
            if (trainer == null) { return; }
            if (Ready(w, trainer))
            {
                GoAndFace(flow, w, trainer.MapId, trainer.X - 1, trainer.Y, Direction.Right);
                return;
            }

            Grind(flow, w, Tiles.GrassChars[trainer.Index], trainer.MapId);
        }

        private static bool NeedsCenter(World w)
        {
            var party = w.Roster.Party;
            var hurt = 0;
            foreach (var m in party)
            {
                if (m.IsFainted || m.Hp * 2 < m.MaxHp) { hurt++; }
            }

            return party[0].IsFainted || hurt * 2 > party.Count || (w.Balls == 0 && w.CaughtCount < 6);
        }

        private static TrainerDef? NextTrainer(World w)
        {
            foreach (var t in w.Data.Trainers)
            {
                if (!w.IsDefeated(t)) { return t; }
            }

            return null;
        }

        /// <summary>手持ちの強い順の平均レベルが、トレーナーの平均レベル + 余裕 以上。</summary>
        private bool Ready(World w, TrainerDef t)
        {
            var levels = new List<int>();
            foreach (var m in w.Roster.Party) { levels.Add(m.Level); }
            levels.Sort();
            levels.Reverse();
            var k = Math.Min(levels.Count, t.Party.Count);
            if (k == 0) { return false; }
            var mine = 0;
            for (int i = 0; i < k; i++) { mine += levels[i]; }
            var theirs = 0;
            foreach (var m in t.Party) { theirs += m.Level; }
            return mine * t.Party.Count >= (theirs + LevelMargin * t.Party.Count) * k && levels.Count >= Math.Min(3, t.Party.Count);
        }

        private void Grind(KeeperFlow flow, World w, char grass, string mapId)
        {
            var e = w.Explorer;
            var map = w.Data.Map(e.MapId);
            if (string.Equals(e.MapId, mapId, StringComparison.Ordinal) && map.TileAt(e.X, e.Y) == grass)
            {
                // 草むらの中を左右に往復する（端や壁で向きを変え、ときどき上下にずれる）。
                var dir = _wanderDir > 0 ? Direction.Right : Direction.Left;
                if (_rng.Next(6) == 0) { dir = _rng.Next(2) == 0 ? Direction.Up : Direction.Down; }
                DirectionMath.Delta(dir, out var dx, out var dy);
                if (map.TileAt(e.X + dx, e.Y + dy) != grass)
                {
                    if (dir == Direction.Left || dir == Direction.Right) { _wanderDir = -_wanderDir; }
                    dir = _wanderDir > 0 ? Direction.Right : Direction.Left;
                    DirectionMath.Delta(dir, out dx, out dy);
                    if (map.TileAt(e.X + dx, e.Y + dy) != grass) { dir = map.TileAt(e.X, e.Y - 1) == grass ? Direction.Up : Direction.Down; }
                }

                TapDirection(flow, dir);
                return;
            }

            var target = NearestTile(w, mapId, grass);
            if (target.HasValue) { GoAndFace(flow, w, mapId, target.Value.X, target.Value.Y, null); }
        }

        private static (int X, int Y)? NearestTile(World w, string mapId, char tile)
        {
            var map = w.Data.Map(mapId);
            var e = w.Explorer;
            var sx = string.Equals(e.MapId, mapId, StringComparison.Ordinal) ? e.X : 0;
            var sy = string.Equals(e.MapId, mapId, StringComparison.Ordinal) ? e.Y : 0;
            (int, int)? best = null;
            var bestD = int.MaxValue;
            for (int y = 0; y < map.Height; y++)
            {
                for (int x = 0; x < map.Width; x++)
                {
                    if (map.TileAt(x, y) != tile) { continue; }
                    var d = Math.Abs(x - sx) + Math.Abs(y - sy);
                    if (d < bestD)
                    {
                        bestD = d;
                        best = (x, y);
                    }
                }
            }

            return best;
        }

        /// <summary>目的のマスへ 1 歩近づき、着いたら向きを合わせて話しかける。</summary>
        private static void GoAndFace(KeeperFlow flow, World w, string mapId, int gx, int gy, Direction? face)
        {
            var e = w.Explorer;
            if (!string.Equals(e.MapId, mapId, StringComparison.Ordinal))
            {
                var portal = PortalToward(w.Data, e.MapId, mapId);
                if (portal == null) { return; }
                var step = Path(w, e.MapId, e.X, e.Y, portal.X, portal.Y);
                if (step.HasValue) { TapDirection(flow, step.Value); }
                return;
            }

            if (e.X == gx && e.Y == gy)
            {
                if (face == null) { return; }
                if (e.Facing != face.Value) { TapDirection(flow, face.Value); }
                else { flow.Tap(InputButton.Confirm); }
                return;
            }

            var next = Path(w, mapId, e.X, e.Y, gx, gy);
            if (next.HasValue) { TapDirection(flow, next.Value); }
        }

        private static void TapDirection(KeeperFlow flow, Direction dir) => flow.Tap((InputButton)(int)dir);

        private static PortalDef? PortalToward(MonsterData data, string from, string to)
        {
            foreach (var p in data.Map(from).Portals)
            {
                if (string.Equals(p.TargetMapId, to, StringComparison.Ordinal)) { return p; }
            }

            return data.Map(from).Portals.Count > 0 ? data.Map(from).Portals[0] : null;
        }

        /// <summary>幅優先探索で、目的のマスへの最初の 1 歩の向き。出入口は目的地のときだけ踏む。</summary>
        private static Direction? Path(World w, string mapId, int sx, int sy, int gx, int gy)
        {
            var data = w.Data;
            var map = data.Map(mapId);
            var prev = new Dictionary<int, int>();
            var queue = new Queue<int>();
            var start = sy * map.Width + sx;
            var goal = gy * map.Width + gx;
            prev[start] = -1;
            queue.Enqueue(start);
            while (queue.Count > 0)
            {
                var cur = queue.Dequeue();
                if (cur == goal) { break; }
                var cx = cur % map.Width;
                var cy = cur / map.Width;
                for (int d = 0; d < 4; d++)
                {
                    DirectionMath.Delta((Direction)d, out var dx, out var dy);
                    var nx = cx + dx;
                    var ny = cy + dy;
                    if (!map.InBounds(nx, ny)) { continue; }
                    var id = ny * map.Width + nx;
                    if (prev.ContainsKey(id)) { continue; }
                    if (!Explorer.CanEnter(data, w.TrainersDefeated, mapId, nx, ny)) { continue; }
                    if (id != goal && map.PortalAt(nx, ny) != null) { continue; }
                    prev[id] = cur;
                    queue.Enqueue(id);
                }
            }

            if (!prev.ContainsKey(goal)) { return null; }
            var node = goal;
            while (prev[node] != start)
            {
                if (prev[node] < 0) { return null; }
                node = prev[node];
            }

            var tx = node % map.Width - sx;
            var ty = node / map.Width - sy;
            return tx > 0 ? Direction.Right : tx < 0 ? Direction.Left : ty > 0 ? Direction.Down : Direction.Up;
        }
    }
}
