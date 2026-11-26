using System;
using TileMotion;

namespace Game.Core
{
    /// <summary>
    /// データの指紋。状態のハッシュとセーブに混ぜ、規則やモンスターの中身が違えば同じ盤面でも別のハッシュにし、
    /// 違うデータで作ったセーブを読み込まない（040・041）。世界を作るときに 1 回だけ計算する。
    /// </summary>
    public static class Fingerprint
    {
        public static uint Of(MonsterData data)
        {
            if (data == null) { throw new ArgumentNullException(nameof(data)); }
            var h = StateHash.Start();
            foreach (var a in data.Types)
            {
                Text(ref h, a.Id);
                foreach (var d in data.Types) { h.Mix(data.Effectiveness(a, d)); }
            }

            foreach (var s in data.Statuses)
            {
                Text(ref h, s.Id);
                h.Mix(s.BlocksAction);
                h.Mix(s.DotDivisor);
                h.Mix(s.MinTurns);
                h.Mix(s.MaxTurns);
                h.Mix(s.CatchPercent);
            }

            foreach (var m in data.Moves)
            {
                Text(ref h, m.Id);
                h.Mix(m.Type?.Index ?? -1);
                h.Mix(m.Power);
                h.Mix(m.Accuracy);
                h.Mix(m.Pp);
                h.Mix(m.Priority);
                foreach (var e in m.Effects)
                {
                    h.Mix((int)e.Kind);
                    h.Mix((int)e.Target);
                    h.Mix(e.Status?.Index ?? -1);
                    h.Mix(e.Percent);
                    h.Mix((int)e.Stat);
                    h.Mix(e.Stages);
                }
            }

            foreach (var s in data.Species)
            {
                Text(ref h, s.Id);
                h.Mix(s.Type.Index);
                h.Mix(s.BaseHp);
                h.Mix(s.BaseAttack);
                h.Mix(s.BaseDefense);
                h.Mix(s.BaseSpeed);
                h.Mix(s.CatchRate);
                h.Mix(s.ExpYield);
                foreach (var l in s.Learnset)
                {
                    h.Mix(l.Level);
                    h.Mix(l.Move.Index);
                }
            }

            var r = data.Rules;
            foreach (var v in new long[]
            {
                r.StepTicks, r.GraceSteps, r.PartyMax, r.BoxMax, r.LevelCap, r.StarterLevel, r.Balls, r.Potions, r.PotionHeal,
                r.ExpCurveNumerator, r.ExpCurveDenominator, r.ExpYieldDivisor, r.TrainerExpPercent, r.StartX, r.StartY, r.CenterX, r.CenterY,
            })
            {
                h.Mix(v);
            }

            Text(ref h, r.StartMapId);
            Text(ref h, r.CenterMapId);
            foreach (var s in r.Starters) { h.Mix(s.Index); }
            foreach (var map in data.Maps)
            {
                Text(ref h, map.Id);
                for (int y = 0; y < map.Height; y++)
                {
                    for (int x = 0; x < map.Width; x++) { h.Mix(map.TileAt(x, y)); }
                }

                foreach (var p in map.Portals)
                {
                    h.Mix(p.X);
                    h.Mix(p.Y);
                    Text(ref h, p.TargetMapId);
                    h.Mix(p.TargetX);
                    h.Mix(p.TargetY);
                    h.Mix((int)p.Facing);
                }

                foreach (var g in map.Grass)
                {
                    h.Mix(g.Tile);
                    h.Mix(g.RatePercent);
                    foreach (var w in g.Wild)
                    {
                        h.Mix(w.Species.Index);
                        h.Mix(w.MinLevel);
                        h.Mix(w.MaxLevel);
                        h.Mix(w.Weight);
                    }
                }
            }

            foreach (var t in data.Trainers)
            {
                Text(ref h, t.Id);
                Text(ref h, t.MapId);
                h.Mix(t.X);
                h.Mix(t.Y);
                foreach (var m in t.Party)
                {
                    h.Mix(m.Species.Index);
                    h.Mix(m.Level);
                }
            }

            return h.Value;
        }

        private static void Text(ref StateHash h, string text)
        {
            h.Mix(text.Length);
            foreach (var ch in text) { h.Mix(ch); }
        }
    }
}
