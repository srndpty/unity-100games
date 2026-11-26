using System;
using System.Collections.Generic;

namespace Game.Core
{
    /// <summary>データの規則上の誤りを理由付きで並べる（空なら正しい）。</summary>
    public static class MonsterDataValidator
    {
        public const int TypeCount = 3;
        public const int StatusCount = 2;
        public const int SpeciesCount = 6;
        public const int TrainerCount = 3;
        public const int MaxMoves = 4;
        public const int MaxPartyLimit = 6;
        public const int MaxStage = 3;

        public static List<string> Validate(MonsterData data)
        {
            if (data == null) { throw new ArgumentNullException(nameof(data)); }
            var e = new List<string>();
            foreach (var d in data.Duplicates) { e.Add($"{d} が重複しています"); }
            ValidateTypes(data, e);
            ValidateStatuses(data, e);
            ValidateMoves(data, e);
            ValidateSpecies(data, e);
            ValidateRules(data, e);
            ValidateMaps(data, e);
            ValidateTrainers(data, e);

            // 形の誤りがあると歩けるかの判定ができないので、ここまでが正しいときだけ到達できるかを見る。
            if (e.Count == 0) { ValidateReachability(data, e); }
            return e;
        }

        private static void ValidateTypes(MonsterData data, List<string> e)
        {
            if (data.Types.Count != TypeCount) { e.Add($"属性は {TypeCount} 種です（{data.Types.Count} 種）"); }
            foreach (var a in data.Types)
            {
                foreach (var d in data.Types)
                {
                    var p = data.Effectiveness(a, d);
                    if (p != 50 && p != 100 && p != 200) { e.Add($"相性 {a.Id}→{d.Id} は 50・100・200 のどれかです（{p}）"); }
                }
            }
        }

        private static void ValidateStatuses(MonsterData data, List<string> e)
        {
            if (data.Statuses.Count != StatusCount) { e.Add($"状態異常は {StatusCount} 種です（{data.Statuses.Count} 種）"); }
            foreach (var s in data.Statuses)
            {
                if (s.MinTurns < 1 || s.MaxTurns < s.MinTurns || s.MaxTurns > 10) { e.Add($"状態異常 {s.Id} の持続は 1 ≤ 最短 ≤ 最長 ≤ 10 です"); }
                if (s.DotDivisor != 0 && (s.DotDivisor < 2 || s.DotDivisor > 64)) { e.Add($"状態異常 {s.Id} のダメージの分母は 0 か 2〜64 です"); }
                if (s.CatchPercent < 100 || s.CatchPercent > 400) { e.Add($"状態異常 {s.Id} の捕獲の補正は 100〜400% です"); }
                if (!s.BlocksAction && s.DotDivisor == 0) { e.Add($"状態異常 {s.Id} に効果がありません"); }
            }
        }

        private static void ValidateMoves(MonsterData data, List<string> e)
        {
            foreach (var m in data.Moves)
            {
                var where = $"技 {m.Id}";
                if (m.Accuracy < 1 || m.Accuracy > 100) { e.Add($"{where} の命中は 1〜100 です（{m.Accuracy}）"); }
                if (m.Power < 0 || m.Power > 250) { e.Add($"{where} の威力は 0〜250 です"); }
                if (m.Pp < 0 || m.Pp > 64) { e.Add($"{where} の PP は 0〜64 です"); }
                if (m.Pp == 0 && !ReferenceEquals(m, data.Rules.Struggle)) { e.Add($"{where} の PP 0 は わるあがき（rule struggle）だけです"); }
                if (m.Priority < -1 || m.Priority > 1) { e.Add($"{where} の優先度は -1〜1 です"); }
                if (m.Effects.Count < 1 || m.Effects.Count > 4) { e.Add($"{where} の効果は 1〜4 個です"); }
                var damage = 0;
                for (int i = 0; i < m.Effects.Count; i++)
                {
                    var f = m.Effects[i];
                    switch (f.Kind)
                    {
                        case EffectKind.Damage:
                            damage++;
                            if (i != 0) { e.Add($"{where} のダメージは最初の効果です"); }
                            break;
                        case EffectKind.Inflict:
                            if (f.Percent < 1 || f.Percent > 100) { e.Add($"{where} の状態異常の確率は 1〜100% です"); }
                            break;
                        case EffectKind.Heal:
                            if (f.Percent < 1 || f.Percent > 100) { e.Add($"{where} の回復の割合は 1〜100% です"); }
                            break;
                        case EffectKind.Stat:
                            if (f.Stages == 0 || Math.Abs(f.Stages) > MaxStage) { e.Add($"{where} の能力の段は ±1〜±{MaxStage} です"); }
                            break;
                    }
                }

                if (damage > 1) { e.Add($"{where} のダメージは 1 つまでです"); }
                if (m.Power > 0 && damage == 0) { e.Add($"{where} は威力があるのにダメージの効果がありません"); }
                if (m.Power == 0 && damage > 0) { e.Add($"{where} は威力 0 なのにダメージの効果があります"); }
            }
        }

        private static void ValidateSpecies(MonsterData data, List<string> e)
        {
            var cap = data.Rules.LevelCap;
            if (data.Species.Count != SpeciesCount) { e.Add($"種族は {SpeciesCount} 種です（{data.Species.Count} 種）"); }
            foreach (var t in data.Types)
            {
                var any = false;
                foreach (var s in data.Species) { any |= ReferenceEquals(s.Type, t); }
                if (!any) { e.Add($"属性 {t.Id} の種族がありません"); }
            }

            foreach (var s in data.Species)
            {
                var where = $"種族 {s.Id}";
                foreach (var stat in new[] { s.BaseHp, s.BaseAttack, s.BaseDefense, s.BaseSpeed })
                {
                    if (stat < 1 || stat > 255) { e.Add($"{where} の種族値は 1〜255 です"); }
                }

                if (s.CatchRate < 1 || s.CatchRate > 255) { e.Add($"{where} の捕獲率は 1〜255 です"); }
                if (s.ExpYield < 1 || s.ExpYield > 1000) { e.Add($"{where} の経験値の量は 1〜1000 です"); }
                if (s.Learnset.Count == 0 || s.Learnset[0].Level != 1) { e.Add($"{where} はレベル 1 で覚える技が必要です"); }
                var seen = new HashSet<string>(StringComparer.Ordinal);
                var prev = 0;
                foreach (var l in s.Learnset)
                {
                    if (l.Level < prev || l.Level < 1 || l.Level > cap) { e.Add($"{where} の覚えるレベルは 1〜{cap} の昇順です"); }
                    prev = l.Level;
                    if (!seen.Add(l.Move.Id)) { e.Add($"{where} が {l.Move.Id} を 2 回覚えます"); }
                    if (ReferenceEquals(l.Move, data.Rules.Struggle)) { e.Add($"{where} は わるあがき を覚えられません"); }
                }
            }
        }

        private static void ValidateRules(MonsterData data, List<string> e)
        {
            var r = data.Rules;
            Range(e, "step_ticks", r.StepTicks, 1, 60);
            Range(e, "grace_steps", r.GraceSteps, 0, 20);
            Range(e, "party_max", r.PartyMax, 1, MaxPartyLimit);
            Range(e, "box_max", r.BoxMax, 0, 100);
            Range(e, "level_cap", r.LevelCap, 2, 100);
            Range(e, "starter_level", r.StarterLevel, 1, r.LevelCap);
            Range(e, "balls", r.Balls, 0, 99);
            Range(e, "potions", r.Potions, 0, 99);
            Range(e, "potion_heal", r.PotionHeal, 1, 999);
            Range(e, "exp_curve 分子", r.ExpCurveNumerator, 1, 10);
            Range(e, "exp_curve 分母", r.ExpCurveDenominator, 1, 10);
            Range(e, "exp_yield_divisor", r.ExpYieldDivisor, 1, 100);
            Range(e, "trainer_exp_percent", r.TrainerExpPercent, 100, 500);
            if (r.Struggle == null) { e.Add("rule struggle がありません"); }
            else if (r.Struggle.Pp != 0) { e.Add("わるあがき の PP は 0（減らない）です"); }

            if (r.Starters.Count != TypeCount) { e.Add($"最初の 3 匹（starter）は {TypeCount} 匹です"); }
            var types = new HashSet<int>();
            foreach (var s in r.Starters)
            {
                if (!types.Add(s.Type.Index)) { e.Add($"最初の 3 匹は属性がすべて違う必要があります（{s.Id}）"); }
            }

            if (!data.TryGetMap(r.StartMapId, out var start)) { e.Add($"開始の地図 {r.StartMapId} がありません"); }
            else if (!Tiles.IsWalkableTerrain(start.TileAt(r.StartX, r.StartY))) { e.Add("開始の位置が歩けるマスではありません"); }

            if (!data.TryGetMap(r.CenterMapId, out var center)) { e.Add($"回復所の地図 {r.CenterMapId} がありません"); }
            else
            {
                if (!Tiles.IsWalkableTerrain(center.TileAt(r.CenterX, r.CenterY))) { e.Add("回復所の前の位置が歩けるマスではありません"); }
                if (center.TileAt(r.CenterX, r.CenterY - 1) != Tiles.Counter) { e.Add("回復所の前の位置の上にカウンター（C）がありません"); }
            }
        }

        private static void ValidateMaps(MonsterData data, List<string> e)
        {
            var cap = data.Rules.LevelCap;
            if (data.Maps.Count == 0) { e.Add("地図がありません"); }
            foreach (var map in data.Maps)
            {
                var where = $"地図 {map.Id}";
                var grassChars = new HashSet<char>();
                for (int y = 0; y < map.Height; y++)
                {
                    for (int x = 0; x < map.Width; x++)
                    {
                        var c = map.TileAt(x, y);
                        if (!Tiles.IsKnown(c)) { e.Add($"{where} の ({x},{y}) の {c} は使えないマスです"); }
                        if (Tiles.IsGrass(c)) { grassChars.Add(c); }
                    }
                }

                foreach (var c in grassChars)
                {
                    if (map.GrassFor(c) == null) { e.Add($"{where} の草むら {c} に遭遇表がありません"); }
                }

                foreach (var g in map.Grass)
                {
                    var gw = $"{where} の草むら {g.Tile}";
                    if (!grassChars.Contains(g.Tile)) { e.Add($"{gw} が地図にありません"); }
                    if (g.RatePercent < 1 || g.RatePercent > 100) { e.Add($"{gw} の遭遇率は 1〜100% です"); }
                    if (g.Wild.Count == 0) { e.Add($"{gw} に出現するモンスターがありません"); }
                    long total = 0;
                    foreach (var w in g.Wild)
                    {
                        if (w.Weight < 1) { e.Add($"{gw} の {w.Species.Id} の重みは 1 以上です"); }
                        total += w.Weight;
                        if (w.MinLevel < 1 || w.MaxLevel < w.MinLevel || w.MaxLevel > cap) { e.Add($"{gw} の {w.Species.Id} のレベルは 1 ≤ 最低 ≤ 最高 ≤ {cap} です"); }
                    }

                    if (total > int.MaxValue) { e.Add($"{gw} の重みの合計が大きすぎます"); }
                }

                foreach (var p in map.Portals)
                {
                    var pw = $"{where} の出入口 ({p.X},{p.Y})";
                    if (!Tiles.IsWalkableTerrain(map.TileAt(p.X, p.Y))) { e.Add($"{pw} が歩けるマスではありません"); }
                    if (!data.TryGetMap(p.TargetMapId, out var target)) { e.Add($"{pw} の行き先の地図 {p.TargetMapId} がありません"); continue; }
                    if (!Tiles.IsWalkableTerrain(target.TileAt(p.TargetX, p.TargetY))) { e.Add($"{pw} の行き先が歩けるマスではありません"); }
                    if (target.PortalAt(p.TargetX, p.TargetY) != null) { e.Add($"{pw} の行き先が出入口の上です"); }
                }
            }
        }

        private static void ValidateTrainers(MonsterData data, List<string> e)
        {
            var cap = data.Rules.LevelCap;
            if (data.Trainers.Count != TrainerCount) { e.Add($"トレーナーは {TrainerCount} 人です（{data.Trainers.Count} 人）"); }
            var places = new HashSet<string>(StringComparer.Ordinal);
            foreach (var t in data.Trainers)
            {
                var where = $"トレーナー {t.Id}";
                if (!data.TryGetMap(t.MapId, out var map)) { e.Add($"{where} の地図 {t.MapId} がありません"); }
                else
                {
                    if (!Tiles.IsWalkableTerrain(map.TileAt(t.X, t.Y))) { e.Add($"{where} の位置が歩けるマスではありません"); }
                    if (map.PortalAt(t.X, t.Y) != null) { e.Add($"{where} が出入口の上にいます"); }
                }

                if (!places.Add($"{t.MapId} {t.X} {t.Y}")) { e.Add($"{where} が他のトレーナーと同じマスにいます"); }
                if (t.Party.Count < 1 || t.Party.Count > MaxPartyLimit) { e.Add($"{where} の手持ちは 1〜{MaxPartyLimit} 匹です"); }
                foreach (var m in t.Party)
                {
                    if (m.Level < 1 || m.Level > cap) { e.Add($"{where} の {m.Species.Id} のレベルは 1〜{cap} です"); }
                }

                if (t.Intro.Length == 0 || t.Defeat.Length == 0) { e.Add($"{where} の台詞（intro・defeat）がありません"); }
            }
        }

        /// <summary>
        /// 開始地点から回復所と、トレーナー全員の前（隣のマス）へ行けるか。倒していないトレーナーは通れないので、
        /// 届くトレーナーから順に倒したことにし、そのマスが通れるようになってから次を探す。
        /// </summary>
        private static void ValidateReachability(MonsterData data, List<string> e)
        {
            var r = data.Rules;
            var defeated = 0;
            var reach = Reachable(data, defeated);
            if (!reach.Contains(Key(data.Map(r.CenterMapId), r.CenterX, r.CenterY))) { e.Add("開始地点から回復所の前へ行けません"); }
            var left = new List<TrainerDef>(data.Trainers);
            while (left.Count > 0)
            {
                var next = left.Find(t => InFront(data, reach, t));
                if (next == null)
                {
                    foreach (var t in left) { e.Add($"トレーナー {t.Id} の前へ開始地点から行けません（届くトレーナーを全員倒した後も）"); }
                    return;
                }

                defeated |= 1 << next.Index;
                left.Remove(next);
                reach = Reachable(data, defeated);
            }
        }

        private static bool InFront(MonsterData data, HashSet<long> reach, TrainerDef t)
        {
            var map = data.Map(t.MapId);
            for (int d = 0; d < 4; d++)
            {
                DirectionMath.Delta((Direction)d, out var dx, out var dy);
                if (reach.Contains(Key(map, t.X + dx, t.Y + dy))) { return true; }
            }

            return false;
        }

        /// <summary>開始地点から歩いて立てるマスの集合（出入口を踏むと行き先へ移る）。</summary>
        private static HashSet<long> Reachable(MonsterData data, int defeated)
        {
            var r = data.Rules;
            var seen = new HashSet<long>();
            var queue = new Queue<(MapDef Map, int X, int Y)>();
            var start = data.Map(r.StartMapId);
            seen.Add(Key(start, r.StartX, r.StartY));
            queue.Enqueue((start, r.StartX, r.StartY));
            while (queue.Count > 0)
            {
                var (map, x, y) = queue.Dequeue();
                for (int d = 0; d < 4; d++)
                {
                    DirectionMath.Delta((Direction)d, out var dx, out var dy);
                    int nx = x + dx, ny = y + dy;
                    if (!Explorer.CanEnter(data, defeated, map.Id, nx, ny)) { continue; }
                    var target = map;
                    var portal = map.PortalAt(nx, ny);
                    if (portal != null)
                    {
                        target = data.Map(portal.TargetMapId);
                        nx = portal.TargetX;
                        ny = portal.TargetY;
                    }

                    if (seen.Add(Key(target, nx, ny))) { queue.Enqueue((target, nx, ny)); }
                }
            }

            return seen;
        }

        private static long Key(MapDef map, int x, int y) => ((long)map.Index << 40) | ((long)(y & 0xFFFFF) << 20) | (uint)(x & 0xFFFFF);

        private static void Range(List<string> e, string name, int value, int min, int max)
        {
            if (value < min || value > max) { e.Add($"rule {name} は {min}〜{max} です（{value}）"); }
        }
    }
}
