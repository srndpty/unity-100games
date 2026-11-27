using System;
using System.Collections.Generic;
using System.Linq;
using Game.Core;
using IntGeometry;
using NUnit.Framework;

namespace Game.Core.Tests
{
    /// <summary>テストの道具。</summary>
    internal static class Lab
    {
        /// <summary>ぶれのない設定（狙いどおりに蹴る）。</summary>
        public static SoccerConfig Exact => new() { ShotSpread = 0, PassSpread = 0 };

        public static PlayerCommand[] Idle() => new PlayerCommand[Match.PlayerCount];

        /// <summary>インプレーにした試合（全員を遠くへ置き、球はセンターで止める）。</summary>
        public static Match Open(SoccerConfig? config = null, int seed = 1)
        {
            var m = new Match(config ?? Exact, seed);
            m.ForcePlay();
            for (int i = 0; i < Match.PlayerCount; i++)
            {
                if (i % 3 == 0) { continue; }
                m.PlacePlayer(i, new Vec2i(i < 3 ? -12000 : 12000, i % 3 == 1 ? -8000 : 8000));
            }

            // GK は自分の範囲の隅に置く（場面の球に届かない）。
            m.PlacePlayer(0, new Vec2i(-Pitch.HalfLength + 500, -4900));
            m.PlacePlayer(3, new Vec2i(Pitch.HalfLength - 500, 4900));

            m.PlaceBall(Vec2i.Zero, Vec2i.Zero);
            return m;
        }

        /// <summary>全員を止めたまま進め、出来事を集める。</summary>
        public static List<MatchEvent> Run(Match m, int ticks, Func<Match, int, PlayerCommand>? command = null)
        {
            var all = new List<MatchEvent>();
            var c = Idle();
            for (int t = 0; t < ticks; t++)
            {
                for (int i = 0; i < Match.PlayerCount; i++) { c[i] = command?.Invoke(m, i) ?? PlayerCommand.Idle; }
                m.Step(c);
                all.AddRange(m.Events);
            }

            return all;
        }

        public static int Count(IEnumerable<MatchEvent> events, MatchEventKind kind) => events.Count(e => e.Kind == kind);

        /// <summary>シード付きの乱択の命令（移動・行動）。長い通しの検査に使う。</summary>
        public static Func<Match, int, PlayerCommand> Random(int seed)
        {
            var rng = new SeededRandom(seed);
            return (m, i) =>
            {
                var move = new Vec2i(rng.Range(-1000, 1000), rng.Range(-1000, 1000));
                var roll = rng.Next(100);
                var action = roll < 3 ? PlayerAction.Pass : roll < 5 ? PlayerAction.Shoot : roll < 6 ? PlayerAction.Lob : roll < 9 ? PlayerAction.Tackle : PlayerAction.None;
                var target = new Vec2i(rng.Range(-Pitch.HalfLength, Pitch.HalfLength), rng.Range(-Pitch.HalfWidth, Pitch.HalfWidth));

                // 球へ寄る命令を混ぜ、球に触れる場面を増やす。
                if (rng.Next(3) == 0) { move = m.Ball.Position - m.Players[i].Position; }
                return new PlayerCommand(move, action, target);
            };
        }
    }
}
