using System;
using NUnit.Framework;

namespace Game.Core.Tests
{
    /// <summary>技表の読み込み・検証と、実際の動きのログとの照合。</summary>
    public sealed class MoveTableTests
    {
        [Test]
        public void ReadmeTableIsGeneratedFromMovesTxt()
        {
            var table = Lab.Table();
            Assert.That(Lab.ReadmeText().Replace("\r", string.Empty, StringComparison.Ordinal).Contains(table.ToMarkdown(), StringComparison.Ordinal), Is.True,
                "README の技表は MoveTable.ToMarkdown() の出力と一致させる：\n" + table.ToMarkdown());
        }

        [Test]
        public void EveryGroundStrikeRunsExactlyItsFrameData()
        {
            var table = Lab.Table();
            foreach (var def in table.Actors)
            {
                foreach (var move in def.Moves)
                {
                    if (move.Kind != MoveKind.Strike) { continue; }
                    var brawl = Lab.Arena();
                    var actor = def.Kind == ActorKind.Player ? brawl.Player : brawl.Spawn(def.Kind, brawl.Player.X + (200 * Lab.Px), brawl.Player.Z, -1);
                    Assert.That(brawl.ForceMove(actor, move.Id), Is.True);
                    int startup = 0;
                    int active = 0;
                    int recovery = 0;
                    int boxes = 0;
                    var phase = actor.Phase;
                    for (int tick = 0; tick < 400 && phase != MovePhase.None; tick++)
                    {
                        if (phase == MovePhase.Startup) { startup++; }
                        else if (phase == MovePhase.Active) { active++; }
                        else { recovery++; }
                        if (phase == MovePhase.Active && actor.Move!.HasBox) { boxes++; }
                        brawl.Step(Pad.Neutral);
                        phase = actor.Phase;
                    }

                    string name = $"{def.Kind} {move.Id}";
                    Assert.That((startup, active, recovery), Is.EqualTo((move.Startup, move.Active, move.Recovery)), name);
                    Assert.That(boxes, Is.EqualTo(move.Active), name + " の攻撃箱は持続の間だけ");
                    Assert.That(actor.State, Is.EqualTo(ActorState.Idle), name + " の硬直が解けると動ける");
                }
            }
        }

        [Test]
        public void PlayerHasTheRequiredMoves()
        {
            var player = Lab.Table()[ActorKind.Player];
            foreach (var id in new[] { "jab1", "jab2", "jab3", "jumpkick", "knee", "throw", "special" })
            {
                Assert.That(player.Has(id), Is.True, id);
            }

            Assert.That(player.Find("jab1").Next, Is.EqualTo("jab2"));
            Assert.That(player.Find("jab2").Next, Is.EqualTo("jab3"));
            Assert.That(player.Find("jab3").Down, Is.True, "3 段目はダウンを奪う");
        }

        [TestCase("box 1024 7168 5120 3072 2048", "box 1024 7168 0 3072 2048", "大きさ 0 の箱")]
        [TestCase("next jab2", "next nothing", "存在しない次段")]
        [TestCase("window 8", "window 99", "硬直より長い受付窓")]
        [TestCase("startup 4", "startup 0", "発生 0")]
        [TestCase("cost 80", "cost 0", "代償の無い緊急技")]
        [TestCase("opener punch1", "opener special", "存在しない opener")]
        [TestCase("keep 16384", "keep 1024", "reach 以下の keep")]
        [TestCase("health 1000", "health 1000\nmystery 3", "未知の項目")]
        [TestCase("actor boss ガラン", "actor brute ガラン", "同じ種類が 2 回")]
        public void BrokenTablesAreRejected(string from, string to, string why)
        {
            var text = Lab.DataText("Moves.txt");
            Assert.That(text.Contains(from, StringComparison.Ordinal), Is.True, "置き換え元がある：" + from);
            var broken = ReplaceFirst(text, from, to);
            Assert.Throws<FormatException>(() => MoveTable.Parse(broken), why);
        }

        [Test]
        public void BoxRequirementMatchesTheMoveKind()
        {
            var text = Lab.DataText("Moves.txt");
            var noBox = ReplaceFirst(text, "box 1024 7168 5120 3072 2048\n", string.Empty);
            Assert.Throws<FormatException>(() => MoveTable.Parse(noBox), "打撃に箱が無い");
            var kneeBox = ReplaceFirst(text, "kind knee\n", "kind knee\nbox 0 0 10 10 10\n");
            Assert.Throws<FormatException>(() => MoveTable.Parse(kneeBox), "膝は箱を持てない");
            var enemyAir = ReplaceFirst(text, "opener flykick\n\nmove flykick 跳び蹴り\n", "opener flykick\n\nmove flykick 跳び蹴り\nkind air\n");
            Assert.Throws<FormatException>(() => MoveTable.Parse(enemyAir), "敵は空中技を持てない");
        }

        private static string ReplaceFirst(string text, string from, string to)
        {
            text = text.Replace("\r", string.Empty, StringComparison.Ordinal);
            int index = text.IndexOf(from, StringComparison.Ordinal);
            Assert.That(index, Is.GreaterThanOrEqualTo(0), from);
            return text.Remove(index, from.Length).Insert(index, to);
        }
    }
}
