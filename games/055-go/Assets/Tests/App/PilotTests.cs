using System.Collections.Generic;
using Game.Core;
using NUnit.Framework;

namespace Game.App.Tests
{
    /// <summary>操縦役：キー入力だけで CPU と 1 局を終局まで打つ。</summary>
    public sealed class PilotTests
    {
        private const int PilotPlayouts = 60;

        /// <summary>操縦役（黒）と CPU（白、弱い）で終局し、死石を推定のまま確定して結果まで進める。</summary>
        private static GoFlow PlayOut(int seed)
        {
            var mcts = AppLab.FastMcts();
            var flow = AppLab.Flow(mcts: mcts, seed: seed);
            flow.Press(InputButton.Down);
            flow.Press(InputButton.Left); // 弱い
            flow.Press(InputButton.Up);
            flow.Press(InputButton.Confirm);
            flow.FlushIntents();
            for (var i = 0; i < 100_000 && flow.Screen == FlowScreen.Playing; i++)
            {
                Pilot.Act(flow, mcts, PilotPlayouts);
                flow.StepTicks(1);
            }

            Assert.That(flow.Screen == FlowScreen.Scoring || flow.Screen == FlowScreen.Result, Is.True, $"終局しない（{flow.Screen}）");
            if (flow.Screen == FlowScreen.Scoring)
            {
                flow.Press(InputButton.Cancel);
                flow.FlushIntents();
            }

            Assert.That(flow.Screen, Is.EqualTo(FlowScreen.Result));
            Assert.That(flow.Outcome, Is.Not.Null);
            return flow;
        }

        [Test]
        public void KeysOnly_FinishAGame()
        {
            var flow = PlayOut(42);
            Assert.That(flow.Game!.MoveCount, Is.GreaterThan(20));
            Assert.That(flow.Cues.Place, Is.GreaterThan(20));
        }

        [Test]
        public void SameSeed_SameGame_WithAFixedFinalHash()
        {
            // 整数だけで決まるので .NET と Mono（Unity）で同じ値になる。
            var a = PlayOut(2026);
            var b = PlayOut(2026);
            Assert.That(b.Game!.StateHash(), Is.EqualTo(a.Game!.StateHash()));
            Assert.That(a.Game.StateHash(), Is.EqualTo(0x15B11646u), $"最終ハッシュ 0x{a.Game.StateHash():X8}（{a.Game.MoveCount} 手、{a.Game.End}、{a.Outcome}）");
        }

        [Test]
        public void KeysTo_ReachesTheTarget()
        {
            for (var from = 0; from < Points.Count; from += 7)
            {
                for (var to = 0; to < Points.Count; to += 5)
                {
                    var cursor = new BoardCursor();
                    cursor.MoveTo(from);
                    foreach (var key in Pilot.KeysTo(from, to))
                    {
                        var (dx, dy) = key switch
                        {
                            InputButton.Up => (0, 1),
                            InputButton.Down => (0, -1),
                            InputButton.Left => (-1, 0),
                            _ => (1, 0),
                        };
                        cursor.Move(dx, dy);
                    }

                    Assert.That(cursor.Point, Is.EqualTo(to));
                }
            }
        }

        [Test]
        public void Cursor_StopsAtTheEdges()
        {
            var cursor = new BoardCursor();
            Assert.That(cursor.Visible, Is.False);
            for (var i = 0; i < 20; i++) { cursor.Move(-1, -1); }
            Assert.That(cursor.Point, Is.EqualTo(Points.Parse("A1")));
            Assert.That(cursor.Visible, Is.True);
            var seen = new HashSet<int>();
            for (var i = 0; i < 20; i++)
            {
                cursor.Move(1, 1);
                seen.Add(cursor.Point);
            }

            Assert.That(cursor.Point, Is.EqualTo(Points.Parse("J9")));
            Assert.That(seen.Count, Is.EqualTo(8));
        }
    }
}
