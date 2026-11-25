using NUnit.Framework;

namespace Game.Core.Tests
{
    /// <summary>レビュー対応：止まりの間の緊急技の持ち越し、Tick の中の押下、ハッシュの指紋、読み取り専用の窓口。</summary>
    public sealed class InputHoldTests
    {
        private static Brawl InHitStop(out Actor foe)
        {
            var brawl = Lab.Arena();
            foe = Lab.Foe(brawl, ActorKind.Grunt, 18);
            Assert.That(brawl.ForceMove(foe, "punch1"), Is.True);
            Assert.That(Lab.Until(brawl, b => b.Player.HitStop > 1, 30), Is.True, "被弾してヒットストップ中");
            return brawl;
        }

        [Test]
        public void SpecialPressedDuringHitStopFiresWhenItEnds()
        {
            var brawl = InHitStop(out _);
            var p = brawl.Player;
            brawl.Step(Lab.Special);
            Assert.That(brawl.SpecialPending, Is.True, "止まりの間は持ち越す");
            Assert.That(p.Move, Is.Null);
            for (int i = 0; i < 15; i++) { brawl.Step(Lab.Special); }
            Assert.That(p.Move?.Id, Is.EqualTo("special"), "止まり明けに出る（押したままでよい）");
            Assert.That(brawl.SpecialCount, Is.EqualTo(1));
            Assert.That(brawl.SpecialPending, Is.False);
            Lab.Until(brawl, b => p.Move == null, 60, Lab.Special);
            Lab.Run(brawl, Lab.Special, 30);
            Assert.That(brawl.SpecialCount, Is.EqualTo(1), "1 回の押下で 1 回だけ");
        }

        [Test]
        public void PendingSpecialIsDroppedWhenItCannotFire()
        {
            var brawl = InHitStop(out _);
            var p = brawl.Player;
            var cost = p.Def!.Find("special").Cost;
            brawl.SetHealth(p, cost);
            Lab.Tap(brawl, Lab.Special);
            Lab.Idle(brawl, 20);
            Assert.That(brawl.SpecialCount, Is.Zero, "体力が代償以下なら捨てる");
            Assert.That(brawl.SpecialPending, Is.False);

            var down = Lab.Arena();
            var brute = Lab.Foe(down, ActorKind.Brute, 20);
            Assert.That(down.ForceMove(brute, "smash"), Is.True);
            Assert.That(Lab.Until(down, b => b.Player.State == ActorState.Knockdown && b.Player.HitStop > 0, 60), Is.True);
            Lab.Tap(down, Lab.Special);
            Lab.Idle(down, 120);
            Assert.That(down.SpecialCount, Is.Zero, "ダウン中に押した緊急技は起き上がっても出ない");

            var paused = InHitStop(out _);
            paused.Step(Lab.Special);
            paused.ClearInput();
            Assert.That(paused.SpecialPending, Is.False, "フォーカス喪失・一時停止で捨てる");
            Lab.Idle(paused, 20);
            Assert.That(paused.SpecialCount, Is.Zero);
        }

        [Test]
        public void PressedFlagsCatchTapsInsideOneTick()
        {
            var tap = Lab.Arena();
            tap.Step(new Pad(0, 0, false, false, false, true, false, false));
            Assert.That(tap.Player.Move?.Id, Is.EqualTo("jab1"), "押して同じ Tick に離した攻撃");

            var jump = Lab.Arena();
            jump.Step(new Pad(0, 0, false, false, false, false, true, false));
            Assert.That(jump.Player.State, Is.EqualTo(ActorState.Jump), "押して同じ Tick に離したジャンプ");

            var special = Lab.Arena();
            special.Step(new Pad(0, 0, false, false, false, false, false, true));
            Assert.That(special.Player.Move?.Id, Is.EqualTo("special"), "押して同じ Tick に離した緊急技");

            var repress = Lab.Arena();
            Lab.Foe(repress, ActorKind.Grunt, 18);
            repress.Step(Lab.Attack);
            long activation = repress.Player.Activation;
            Lab.Until(repress, b => b.Player.MoveTick == 6, 30, Lab.Attack);
            Assert.That(repress.Player.Activation, Is.EqualTo(activation), "押したままでは次段に繋がらない");
            repress.Step(new Pad(0, 0, true, false, false, true, false, false));
            Assert.That(repress.Player.Move?.Id, Is.EqualTo("jab2"), "離して押し直した押下は押したままでも数える");
        }

        [Test]
        public void HashSeesRulesAndAiAndPendingSpecial()
        {
            var a = Lab.Arena();
            var b = a.Clone();
            Assert.That(b.Hash(), Is.EqualTo(a.Hash()));
            b.AiEnabled = true;
            Assert.That(b.Hash(), Is.Not.EqualTo(a.Hash()), "敵が考えるかで以後が分かれる");

            var heal = Lab.Arena(new BrawlConfig { HealAmount = 150 });
            Assert.That(heal.Hash(), Is.Not.EqualTo(Lab.Arena().Hash()), "規則の値が違えば別のハッシュ");

            var stage = new Brawl(Lab.Table(), new BrawlConfig(), StageData.Arena(100 * Lab.Px), 1) { AiEnabled = false };
            var other = new Brawl(Lab.Table(), new BrawlConfig(), StageData.Arena(100 * Lab.Px), 1) { AiEnabled = false };
            Assert.That(stage.Hash(), Is.EqualTo(other.Hash()), "同じ規則・同じ状態は同じハッシュ");

            var x = InHitStop(out _);
            var y = x.Clone();
            x.Step(Lab.Special);
            y.Step(Pad.Neutral);
            y.Step(Pad.Neutral);
            x.Step(Pad.Neutral);
            Assert.That(x.SpecialPending, Is.Not.EqualTo(y.SpecialPending));
            Assert.That(x.Hash(), Is.Not.EqualTo(y.Hash()), "持ち越している緊急技もハッシュに入る");
            Assert.That(x.Clone().SpecialPending, Is.True, "複製も持ち越す");
        }

        [Test]
        public void ReadOnlyViewsAreBuiltOnce()
        {
            var stage = Lab.Stage();
            Assert.That(stage.Sections, Is.SameAs(stage.Sections), "毎 Tick 参照する窓口を作り直さない");
            Assert.That(stage.Sections[0].Waves, Is.SameAs(stage.Sections[0].Waves));
            var table = Lab.Table();
            Assert.That(table.Actors, Is.SameAs(table.Actors));
            Assert.That(table[ActorKind.Player].Moves, Is.SameAs(table[ActorKind.Player].Moves));
        }
    }
}
