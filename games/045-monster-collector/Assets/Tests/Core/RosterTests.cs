using IntGeometry;
using NUnit.Framework;

namespace Game.Core.Tests
{
    /// <summary>題材固有テスト：手持ちの上限を超えない（超えた分はボックス、ボックスも満杯なら捕獲できない）。</summary>
    public sealed class RosterTests
    {
        private static World FieldWorld(int seed = 1)
        {
            var w = World.New(Lab.Data, seed);
            w.ChooseStarter(0);
            return w;
        }

        [Test]
        public void SeventhGoesToTheBox_AndTheBoxFillsUp()
        {
            var r = new Roster(6, 2);
            for (int i = 0; i < 6; i++) { Assert.That(r.Receive(Lab.Mon("wickmoth", 3)), Is.EqualTo(RosterPlace.Party)); }
            var hurt = Lab.Mon("brinecrab", 3);
            hurt.Hp = 1;
            Assert.That(r.Receive(hurt), Is.EqualTo(RosterPlace.Box));
            Assert.That(r.Box[0].Hp, Is.EqualTo(r.Box[0].MaxHp), "ボックスに入ると全快");
            Assert.That(r.Receive(Lab.Mon("brinecrab", 3)), Is.EqualTo(RosterPlace.Box));
            Assert.That(r.HasRoom, Is.False);
            Assert.That(r.Receive(Lab.Mon("brinecrab", 3)), Is.EqualTo(RosterPlace.Rejected));
            Assert.That(r.Party.Count, Is.EqualTo(6));
            Assert.That(r.Box.Count, Is.EqualTo(2));
        }

        [Test]
        public void DepositAndWithdrawKeepTheLimits()
        {
            var r = new Roster(2, 1);
            r.Receive(Lab.Mon("wickmoth", 3));
            Assert.That(r.Deposit(0), Is.EqualTo(RosterError.LastHealthy), "手持ちを 0 匹にできない");
            r.Receive(Lab.Mon("brinecrab", 3));
            r.Party[1].Hp = 0;
            Assert.That(r.Deposit(0), Is.EqualTo(RosterError.LastHealthy), "戦える個体のいない手持ちにできない");
            Assert.That(r.Deposit(1), Is.EqualTo(RosterError.None));
            Assert.That(r.Box[0].IsFainted, Is.False, "預けると全快");
            r.Receive(Lab.Mon("burrowroot", 3));
            Assert.That(r.Deposit(1), Is.EqualTo(RosterError.BoxFull));
            Assert.That(r.Withdraw(0), Is.EqualTo(RosterError.PartyFull), "手持ちを上限より多くできない");
            Assert.That(r.Withdraw(5), Is.EqualTo(RosterError.NoSuchSlot));
            Assert.That(r.Deposit(-1), Is.EqualTo(RosterError.NoSuchSlot));
            Assert.That(r.Party.Count, Is.EqualTo(2));
            Assert.That(r.Box.Count, Is.EqualTo(1));
        }

        [Test]
        public void RandomOperationsNeverBreakTheLimits()
        {
            var species = Lab.Data.Species;
            for (int seed = 1; seed <= 50; seed++)
            {
                var rng = new RandomSeries(seed);
                var r = new Roster(6, 30);
                r.Receive(Lab.Mon("mossling", 5));
                for (int step = 0; step < 400; step++)
                {
                    switch (rng.Next(5))
                    {
                        case 0:
                            r.Receive(Monster.Create(species[rng.Next(species.Count)], rng.Range(1, 20), Lab.Data.Rules));
                            break;
                        case 1:
                            r.Deposit(rng.Range(-1, 7));
                            break;
                        case 2:
                            r.Withdraw(rng.Range(-1, 31));
                            break;
                        case 3:
                            r.SwapParty(rng.Range(-1, 6), rng.Range(-1, 6));
                            break;
                        default:
                            if (r.Party.Count > 0) { r.Party[rng.Next(r.Party.Count)].Hp = 0; }
                            if (!r.HasHealthy) { r.RestoreAll(); }
                            break;
                    }

                    Assert.That(r.Party.Count, Is.InRange(1, 6), $"シード {seed} 手 {step}");
                    Assert.That(r.Box.Count, Is.InRange(0, 30));
                    Assert.That(r.HasHealthy, Is.True);
                }
            }
        }

        [Test]
        public void CaptureWithAFullPartyGoesToTheBox()
        {
            var w = FieldWorld();
            for (int i = 0; i < 5; i++) { w.Roster.Receive(Lab.Mon("brinecrab", 3)); }
            var wild = Lab.Mon("wickmoth", 3);
            wild.Hp = 1;
            w.StartWildForTest(wild);
            w.Battle!.FoeMonster.SetStatus(Lab.Data.Status("sleep"), 99);
            w.Battle.Submit(BattleCommand.Ball());
            Assert.That(w.Battle.Outcome, Is.EqualTo(BattleOutcome.Captured));
            var after = w.FinishBattle();
            Assert.That(after.Place, Is.EqualTo(RosterPlace.Box));
            Assert.That(w.Roster.Party.Count, Is.EqualTo(6));
            Assert.That(w.Roster.Box.Count, Is.EqualTo(1));
            Assert.That(w.Caught & (1 << Lab.Data.SpeciesOf("wickmoth").Index), Is.Not.Zero);
        }

        [Test]
        public void FullPartyAndBoxRejectTheBall_AndKeepTheBall()
        {
            var w = FieldWorld();
            var small = new Roster(1, 0);
            Assert.That(small.HasRoom, Is.True);
            for (int i = 0; i < 5; i++) { w.Roster.Receive(Lab.Mon("brinecrab", 3)); }
            for (int i = 0; i < Lab.Data.Rules.BoxMax; i++) { w.Roster.Receive(Lab.Mon("brinecrab", 3)); }
            Assert.That(w.Roster.HasRoom, Is.False);
            w.StartWildForTest(Lab.Mon("wickmoth", 3));
            var balls = w.Battle!.Balls;
            var result = w.Battle.Submit(BattleCommand.Ball());
            Assert.That(result.Reason, Is.EqualTo(RejectReason.NoRoom));
            Assert.That(w.Battle.Balls, Is.EqualTo(balls));
        }
    }
}
