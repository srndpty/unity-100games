using NUnit.Framework;

namespace Game.Core.Tests
{
    public sealed class DuelRulesTests
    {
        [Test]
        public void EveryMove_PhaseTicksMatchTheSheet()
        {
            var table = FightLab.Table();
            for (int c = 0; c < table.Characters.Count; c++)
            {
                var character = table[c];
                for (int m = 0; m < character.Moves.Count; m++)
                {
                    var move = character.Moves[m];
                    var duel = FightLab.NewDuel(c, 1 - c);
                    FightLab.Place(duel, 420);
                    duel.Step(Pad.Neutral, Pad.Neutral);
                    FightLab.Tap(duel, 0, move.Id);
                    int startup = 0;
                    int active = 0;
                    int recovery = 0;
                    bool seen = false;
                    for (int t = 0; t < 80; t++)
                    {
                        if (t > 0 || duel.At(0).State != FighterState.Attack) { duel.Step(Pad.Neutral, Pad.Neutral); }
                        if (duel.Tick == 0) { continue; }
                        var row = duel.LogRow(0, duel.Tick - 1);
                        if (row.Frozen) { continue; }
                        if (row.State != FighterState.Attack)
                        {
                            if (seen) { break; }
                            continue;
                        }

                        seen = true;
                        if (row.Phase == MovePhase.Startup) { startup++; }
                        else if (row.Phase == MovePhase.Active) { active++; }
                        else if (row.Phase == MovePhase.Recovery) { recovery++; }
                    }

                    Assert.That(startup, Is.EqualTo(move.Startup), move.Id + " startup " + character.Id);
                    Assert.That(active, Is.EqualTo(move.Active), move.Id + " active");
                    Assert.That(recovery, Is.EqualTo(move.Recovery), move.Id + " recovery");
                }
            }
        }

        [Test]
        public void HitAndBlockAdvantage_MatchTheSheet()
        {
            var table = FightLab.Table();
            for (int c = 0; c < table.Characters.Count; c++)
            {
                foreach (var move in table[c].Moves)
                {
                    if (move.IsThrow) { continue; }
                    int hit = Measure(c, move, false);
                    int block = Measure(c, move, true);
                    Assert.That(hit, Is.EqualTo(move.HitAdvantage(table[c])), character(c) + " " + move.Id + " hit");
                    Assert.That(block, Is.EqualTo(move.BlockAdvantage), character(c) + " " + move.Id + " block");
                }
            }

            static string character(int index) => index == 0 ? "cinder" : "kiln";
        }

        [Test]
        public void HitStop_FreezesStateAndHoldsBufferedCommand()
        {
            var duel = FightLab.NewDuel();
            FightLab.Place(duel, 30);
            FightLab.Tap(duel, 0, "light");
            while (duel.HitStop == 0 && duel.Tick < 30) { duel.Step(Pad.Neutral, Pad.Neutral); }
            Assert.That(duel.HitStop, Is.EqualTo(8));
            int frame = duel.At(0).Frame;
            int x = duel.At(0).X;
            int y = duel.At(1).Y;
            int stop = duel.HitStop;
            for (int i = 0; i < stop - 3; i++) { duel.Step(Pad.Neutral, Pad.Neutral); }
            Assert.That(duel.At(0).Frame, Is.EqualTo(frame));
            Assert.That(duel.At(0).X, Is.EqualTo(x));
            FightLab.Step(duel, 0, new Pad(2, false, false, false));
            FightLab.Step(duel, 0, new Pad(3, false, false, false));
            FightLab.Step(duel, 0, new Pad(6, true, false, false));
            Assert.That(duel.At(0).Frame, Is.EqualTo(frame), "1 Tick 手前まで止まっている");
            Assert.That(duel.At(1).Y, Is.EqualTo(y));
            duel.Step(Pad.Neutral, Pad.Neutral);
            Assert.That(duel.HitStop, Is.EqualTo(0));
            Assert.That(duel.At(0).Move, Is.Not.Null);
            Assert.That(duel.At(0).Move!.Id, Is.EqualTo("special"));
            Assert.That(duel.At(0).Frame, Is.EqualTo(0));
        }

        [Test]
        public void Guard_ChangesDamageByStanceAndHeight_AndChipDoesNotKo()
        {
            Expect("light", false, true, 0, FighterState.BlockStun);
            Expect("heavy", false, true, 0, FighterState.BlockStun);
            Expect("sweep", false, false, 1000, FighterState.Knockdown);
            Expect("light", true, true, 0, FighterState.BlockStun);
            Expect("heavy", true, false, 1200, FighterState.HitStun);
            Expect("sweep", true, true, 0, FighterState.BlockStun);

            var chip = FightLab.NewDuel();
            FightLab.Place(chip, 36);
            chip.At(1).Health = 1;
            var guard = FightLab.Guard(chip.At(1), false);
            FightLab.Motion(chip, 0, AttackButton.Light);
            for (int i = 0; i < 40 && chip.At(1).State != FighterState.BlockStun; i++)
            {
                guard = FightLab.Guard(chip.At(1), false);
                chip.Step(Pad.Neutral, guard);
            }

            Assert.That(chip.At(1).Health, Is.EqualTo(1));
            Assert.That(chip.At(1).State, Is.EqualTo(FighterState.BlockStun));
        }

        [Test]
        public void OneSwing_HitsOnce_UntilTheNextActivation()
        {
            var light = OverlapAll("light", 0);
            Assert.That(light, Is.EqualTo(700));
            var rush = OverlapAll("special", 1);
            Assert.That(rush, Is.EqualTo(1500));
        }

        [Test]
        public void HitThrowAndHurt_StaySeparate()
        {
            var air = FightLab.NewDuel();
            FightLab.Place(air, 28);
            air.At(1).Y = 80 * 256;
            air.At(1).State = FighterState.Air;
            int health = air.At(1).Health;
            FightLab.Tap(air, 0, "light");
            for (int i = 0; i < 20; i++) { air.Step(Pad.Neutral, Pad.Neutral); }
            Assert.That(air.At(1).Health, Is.EqualTo(health), "喰らい箱が上なら当たらない");

            var stun = FightLab.NewDuel();
            FightLab.Place(stun, 24);
            stun.At(1).State = FighterState.HitStun;
            stun.At(1).Remaining = 40;
            health = stun.At(1).Health;
            FightLab.Tap(stun, 0, "throw");
            for (int i = 0; i < 20; i++) { stun.Step(Pad.Neutral, Pad.Neutral); }
            Assert.That(stun.At(1).Health, Is.EqualTo(health), "硬直中は投げられず、投げ箱は攻撃にならない");
        }

        [Test]
        public void Throw_BeatsGuard_TradesOff_AndMissesAir()
        {
            var guard = FightLab.NewDuel();
            FightLab.Place(guard, 22);
            guard.Step(Pad.Neutral, Pad.Neutral);
            var held = FightLab.Guard(guard.At(1), false);
            int before = guard.At(1).Health;
            guard.Step(new Pad(5, true, true, false), held);
            for (int i = 0; i < 8; i++)
            {
                held = FightLab.Guard(guard.At(1), false);
                guard.Step(Pad.Neutral, held);
            }

            Assert.That(guard.At(1).Health, Is.LessThan(before));
            Assert.That(guard.At(1).State, Is.EqualTo(FighterState.Thrown).Or.EqualTo(FighterState.Knockdown));

            var both = FightLab.NewDuel();
            FightLab.Place(both, 22);
            both.Step(Pad.Neutral, Pad.Neutral);
            int gap = System.Math.Abs(both.At(0).X - both.At(1).X);
            both.Step(new Pad(5, true, true, false), new Pad(5, true, true, false));
            for (int i = 0; i < 8; i++) { both.Step(Pad.Neutral, Pad.Neutral); }
            Assert.That(both.At(0).Health, Is.EqualTo(both.At(0).MaxHealth));
            Assert.That(both.At(1).Health, Is.EqualTo(both.At(1).MaxHealth));
            Assert.That(System.Math.Abs(both.At(0).X - both.At(1).X), Is.GreaterThan(gap));

            var air = FightLab.NewDuel();
            FightLab.Place(air, 22);
            air.At(1).State = FighterState.Air;
            air.At(1).Y = 4000;
            FightLab.Tap(air, 0, "throw");
            for (int i = 0; i < 10; i++) { air.Step(Pad.Neutral, Pad.Neutral); }
            Assert.That(air.At(1).Health, Is.EqualTo(air.At(1).MaxHealth));
        }

        [Test]
        public void Command_WindowFacingHitStopAndBuffer()
        {
            var config = new DuelConfig { StepWindow = 4, ButtonWindow = 4, HistoryTicks = 24, BufferTicks = 30 };
            var exact = FightLab.NewDuel(config: config);
            FightLab.Place(exact, 200);
            PlayMotion(exact, 4, true);
            Assert.That(exact.At(0).Move, Is.Not.Null);
            Assert.That(exact.At(0).Move!.Id, Is.EqualTo("special"));

            var late = FightLab.NewDuel(config: config);
            FightLab.Place(late, 200);
            PlayMotion(late, 5, true);
            Assert.That(late.At(0).Move == null || late.At(0).Move!.Id != "special", Is.True);

            var mirror = FightLab.NewDuel(config: config);
            mirror.At(0).X = mirror.At(1).X + (80 * 256);
            PlayMotion(mirror, 4, false);
            Assert.That(mirror.At(0).Move, Is.Not.Null);
            Assert.That(mirror.At(0).Move!.Id, Is.EqualTo("special"));

            var buffer = FightLab.NewDuel(config: config);
            FightLab.Place(buffer, 200);
            FightLab.Tap(buffer, 0, "heavy");
            buffer.Step(new Pad(5, true, false, false), Pad.Neutral);
            int lights = 0;
            string previous = string.Empty;
            for (int i = 0; i < 80; i++)
            {
                buffer.Step(Pad.Neutral, Pad.Neutral);
                var armed = buffer.At(0).Move;
                string id = armed == null ? string.Empty : armed.Id;
                if (id == "light" && previous != "light") { lights++; }
                previous = id;
            }

            Assert.That(lights, Is.EqualTo(1));
        }

        [Test]
        public void Trade_PushAtWall_AndTurn()
        {
            var trade = FightLab.NewDuel(0, 0);
            FightLab.Place(trade, 30);
            trade.Step(new Pad(5, true, false, false), new Pad(5, true, false, false));
            for (int i = 0; i < 12; i++) { trade.Step(Pad.Neutral, Pad.Neutral); }
            Assert.That(trade.At(0).Health, Is.LessThan(trade.At(0).MaxHealth));
            Assert.That(trade.At(1).Health, Is.LessThan(trade.At(1).MaxHealth));

            var wall = FightLab.NewDuel();
            wall.At(0).X = 3072;
            wall.At(1).X = 5000;
            int right = wall.At(1).X;
            wall.Step(Pad.Neutral, Pad.Neutral);
            Assert.That(wall.At(1).X, Is.GreaterThan(right));
            Assert.That(wall.At(0).X, Is.GreaterThanOrEqualTo(3072));

            var turn = FightLab.NewDuel();
            turn.At(0).X = turn.At(1).X + 8000;
            turn.Step(Pad.Neutral, Pad.Neutral);
            Assert.That(turn.At(0).Facing, Is.EqualTo(-1));
            turn.At(0).X = turn.At(1).X - 8000;
            turn.Step(Pad.Neutral, Pad.Neutral);
            Assert.That(turn.At(0).Facing, Is.EqualTo(1));
        }

        [Test]
        public void Projectiles_CancelEachOther()
        {
            var duel = FightLab.NewDuel(0, 0);
            FightLab.Place(duel, 220);
            duel.Step(Pad.Neutral, Pad.Neutral);
            duel.Step(new Pad(2, false, false, false), new Pad(2, false, false, false));
            duel.Step(new Pad(3, false, false, false), new Pad(1, false, false, false));
            duel.Step(new Pad(6, true, false, false), new Pad(4, true, false, false));
            bool saw = false;
            for (int i = 0; i < 80; i++)
            {
                duel.Step(Pad.Neutral, Pad.Neutral);
                if (duel.ShotCount > 0) { saw = true; }
                if (saw && duel.ShotCount == 0) { break; }
            }

            Assert.That(saw, Is.True);
            Assert.That(duel.ShotCount, Is.EqualTo(0));
            Assert.That(duel.At(0).Health, Is.EqualTo(duel.At(0).MaxHealth));
            Assert.That(duel.At(1).Health, Is.EqualTo(duel.At(1).MaxHealth));
        }

        [Test]
        public void Hash_IncludesBuffer_AndCloneDoesNotShare()
        {
            var first = Script();
            var second = Script();
            Assert.That(first, Is.EqualTo(second));

            var duel = FightLab.NewDuel();
            FightLab.Place(duel, 30);
            FightLab.Tap(duel, 0, "light");
            while (duel.HitStop == 0) { duel.Step(Pad.Neutral, Pad.Neutral); }
            var copy = duel.Clone();
            int x = duel.At(0).X;
            duel.Step(Pad.Neutral, new Pad(5, true, false, false));
            copy.Step(Pad.Neutral, Pad.Neutral);
            Assert.That(duel.At(0).X, Is.EqualTo(x));
            Assert.That(copy.At(0).X, Is.EqualTo(x));
            Assert.That(duel.Hash(), Is.Not.EqualTo(copy.Hash()));

            var origin = duel.Hash();
            copy.Step(Pad.Neutral, Pad.Neutral);
            Assert.That(duel.Hash(), Is.EqualTo(origin));
        }

        [Test]
        public void Ko_IgnoresLaterInput()
        {
            var duel = FightLab.NewDuel();
            FightLab.Place(duel, 30);
            duel.At(1).Health = 1;
            FightLab.Tap(duel, 0, "light");
            for (int i = 0; i < 20 && duel.At(1).State != FighterState.Ko; i++) { duel.Step(Pad.Neutral, Pad.Neutral); }
            Assert.That(duel.At(1).State, Is.EqualTo(FighterState.Ko));
            int x = duel.At(1).X;
            for (int i = 0; i < 10; i++) { duel.Step(new Pad(6, true, true, true), new Pad(4, true, true, true)); }
            Assert.That(duel.At(1).State, Is.EqualTo(FighterState.Ko));
            Assert.That(duel.At(1).Health, Is.EqualTo(0));
            Assert.That(duel.At(1).Move, Is.Null);
            Assert.That(duel.At(1).X, Is.EqualTo(x));
        }

        [TestCase(true, true, false, false, "special")]
        [TestCase(true, false, true, false, "heavy")]
        [TestCase(true, false, false, true, null)]
        [TestCase(false, true, false, false, "special")]
        [TestCase(false, false, true, false, "heavy")]
        [TestCase(false, false, false, true, null)]
        [TestCase(true, true, true, false, "throw")]
        [TestCase(false, true, true, false, "throw")]
        public void Motion_UsesLightOnly_AndSimultaneousThrowWins(bool right, bool light, bool heavy, bool sweep, string? expected)
        {
            var duel = FightLab.NewDuel();
            FightLab.Place(duel, 180);
            if (!right)
            {
                int x = duel.At(0).X;
                duel.At(0).X = duel.At(1).X;
                duel.At(1).X = x;
            }

            StepDir(duel, 2);
            StepDir(duel, right ? 3 : 1);
            duel.Step(new Pad(right ? 6 : 4, light, heavy, sweep), Pad.Neutral);
            Assert.That(duel.At(0).Move?.Id, Is.EqualTo(expected));
        }

        [TestCase(1, false)]
        [TestCase(80, false)]
        [TestCase(1, true)]
        public void AirHitStun_AlwaysReachesTheGround(int stun, bool hitStop)
        {
            var duel = FightLab.NewDuel();
            FightLab.Place(duel, hitStop ? 30 : 180);
            if (hitStop)
            {
                FightLab.Tap(duel, 0, "light");
                for (int i = 0; i < 20 && duel.HitStop == 0; i++) { duel.Step(Pad.Neutral, Pad.Neutral); }
                Assert.That(duel.HitStop, Is.GreaterThan(0));
            }

            var fighter = duel.At(1);
            fighter.State = FighterState.HitStun;
            fighter.Pose = BodyPose.Air;
            fighter.Y = 40 * 256;
            fighter.Vy = 0;
            fighter.Remaining = stun;
            int stop = duel.HitStop;
            for (int i = 0; i < stop; i++)
            {
                duel.Step(Pad.Neutral, Pad.Neutral);
                Assert.That(fighter.Y, Is.EqualTo(40 * 256));
                Assert.That(fighter.Remaining, Is.EqualTo(stun));
            }

            duel.Step(Pad.Neutral, Pad.Neutral);
            if (stun == 1)
            {
                Assert.That(fighter.State, Is.EqualTo(FighterState.Air));
                Assert.That(fighter.Pose, Is.EqualTo(BodyPose.Air));
                Assert.That(duel.CanAct(1), Is.False);
            }

            var attack = new Pad(5, true, true, false);
            for (int i = 0; i < 100 && fighter.Y > 0; i++)
            {
                Assert.That(duel.CanAct(1), Is.False, "空中で地上技を使えない");
                int y = fighter.Y;
                duel.Step(Pad.Neutral, attack);
                Assert.That(fighter.Y, Is.LessThan(y));
                Assert.That(fighter.State, Is.Not.EqualTo(FighterState.Attack));
            }

            Assert.That(fighter.Y, Is.EqualTo(0));
            Assert.That(fighter.State, Is.EqualTo(stun == 1 ? FighterState.Land : FighterState.Knockdown));
            for (int i = 0; i < 120 && !duel.CanAct(1); i++) { duel.Step(Pad.Neutral, Pad.Neutral); }
            Assert.That(duel.CanAct(1), Is.True);
        }

        [TestCase(true, false, false)]
        [TestCase(false, true, false)]
        [TestCase(false, false, true)]
        public void Hash_DistinguishesHeldAndReleasedButtons_AndClonePreservesEdges(bool light, bool heavy, bool sweep)
        {
            var held = FightLab.NewDuel();
            FightLab.Place(held, 180);
            var press = new Pad(sweep ? 2 : 5, light, heavy, sweep);
            held.Step(press, Pad.Neutral);
            var released = held.Clone();
            held.Step(press, Pad.Neutral);
            released.Step(new Pad(press.Direction, false, false, false), Pad.Neutral);
            Assert.That(held.At(0).Frame, Is.EqualTo(released.At(0).Frame));
            Assert.That(held.Hash(), Is.Not.EqualTo(released.Hash()));
            var heldCopy = held.Clone();
            var releasedCopy = released.Clone();
            held.Step(press, Pad.Neutral);
            released.Step(press, Pad.Neutral);
            heldCopy.Step(press, Pad.Neutral);
            releasedCopy.Step(press, Pad.Neutral);
            Assert.That(heldCopy.Hash(), Is.EqualTo(held.Hash()));
            Assert.That(releasedCopy.Hash(), Is.EqualTo(released.Hash()));
            int activation = held.At(0).Activation;
            for (int i = 0; i < 50 && held.At(0).Activation == activation; i++)
            {
                held.Step(press, Pad.Neutral);
                heldCopy.Step(press, Pad.Neutral);
            }

            Assert.That(held.At(0).Activation, Is.EqualTo(activation), "保持では押下エッジを作らない");
            Assert.That(heldCopy.Hash(), Is.EqualTo(held.Hash()));
        }

        private static int Measure(int character, Move move, bool block)
        {
            var duel = FightLab.NewDuel(character, character);
            FightLab.Place(duel, move.IsProjectile ? 36 : 32);
            duel.Step(Pad.Neutral, Pad.Neutral);
            FightLab.Tap(duel, 0, move.Id);
            FighterState seen = FighterState.Idle;
            bool crouch = move.Height != StrikeHeight.Mid;
            for (int i = 0; i < 80; i++)
            {
                var guard = block ? FightLab.Guard(duel.At(1), crouch) : Pad.Neutral;
                duel.Step(Pad.Neutral, guard);
                seen = duel.At(1).State;
                if (seen == FighterState.HitStun || seen == FighterState.BlockStun || seen == FighterState.Knockdown || seen == FighterState.Ko)
                {
                    break;
                }
            }

            Assert.That(seen, Is.Not.EqualTo(FighterState.Idle), move + (block ? " block" : " hit"));
            int attacker = 0;
            int defender = 0;
            for (int i = 0; i < 80; i++)
            {
                bool aBusy = !duel.CanAct(0);
                bool dBusy = !duel.CanAct(1);
                if (!aBusy && !dBusy) { break; }
                if (aBusy) { attacker++; }
                if (dBusy) { defender++; }
                duel.Step(Pad.Neutral, Pad.Neutral);
            }

            return defender - attacker;
        }

        private static void Expect(string move, bool crouch, bool blocked, int damage, FighterState state)
        {
            var duel = FightLab.NewDuel();
            FightLab.Place(duel, 30);
            duel.Step(Pad.Neutral, Pad.Neutral);
            int health = duel.At(1).Health;
            FightLab.Tap(duel, 0, move);
            FighterState seen = FighterState.Idle;
            for (int i = 0; i < 30; i++)
            {
                duel.Step(Pad.Neutral, FightLab.Guard(duel.At(1), crouch));
                seen = duel.At(1).State;
                if (seen == FighterState.HitStun || seen == FighterState.BlockStun || seen == FighterState.Knockdown)
                {
                    break;
                }
            }

            Assert.That(health - duel.At(1).Health, Is.EqualTo(damage), move + " crouch " + crouch);
            Assert.That(seen, Is.EqualTo(blocked ? FighterState.BlockStun : state), move + " crouch " + crouch);
        }

        private static int OverlapAll(string move, int character)
        {
            var duel = FightLab.NewDuel(character, 1 - character);
            FightLab.Place(duel, 28);
            duel.Step(Pad.Neutral, Pad.Neutral);
            int health = duel.At(1).Health;
            FightLab.Tap(duel, 0, move);
            for (int i = 0; i < 80; i++)
            {
                var current = duel.At(0).Move;
                if (current != null && current.Id == move) { break; }
                KeepClose(duel);
                duel.Step(Pad.Neutral, Pad.Neutral);
            }

            while (true)
            {
                var current = duel.At(0).Move;
                if (current == null || current.Id != move) { break; }
                KeepClose(duel);
                duel.Step(Pad.Neutral, Pad.Neutral);
            }

            int dropped = health - duel.At(1).Health;
            health = duel.At(1).Health;
            FightLab.Tap(duel, 0, move);
            for (int i = 0; i < 90; i++)
            {
                KeepClose(duel);
                duel.Step(Pad.Neutral, Pad.Neutral);
                if (duel.At(1).Health < health) { break; }
            }

            Assert.That(health - duel.At(1).Health, Is.GreaterThan(0), "次の発動では再び当たる");
            return dropped;
        }

        private static void KeepClose(Duel duel)
        {
            int gap = 28 * 256;
            int mid = (duel.At(0).X + duel.At(1).X) / 2;
            duel.At(0).X = mid - (gap / 2);
            duel.At(1).X = mid + (gap / 2);
            duel.At(1).Y = 0;
        }

        private static void PlayMotion(Duel duel, int buttonDelay, bool facingRight)
        {
            StepDir(duel, 2);
            for (int i = 1; i < buttonDelay; i++) { StepDir(duel, 5); }
            StepDir(duel, facingRight ? 3 : 1);
            for (int i = 1; i < buttonDelay; i++) { StepDir(duel, 5); }
            StepDir(duel, facingRight ? 6 : 4);
            for (int i = 1; i < buttonDelay; i++) { StepDir(duel, facingRight ? 6 : 4); }
            duel.Step(new Pad(facingRight ? 6 : 4, true, false, false), Pad.Neutral);
        }

        private static void StepDir(Duel duel, int direction) => duel.Step(new Pad(direction, false, false, false), Pad.Neutral);

        private static uint Script()
        {
            var duel = FightLab.NewDuel();
            FightLab.Place(duel, 40);
            duel.Step(new Pad(6, false, false, false), Pad.Neutral);
            duel.Step(new Pad(5, true, false, false), Pad.Neutral);
            duel.Step(Pad.Neutral, new Pad(4, false, false, false));
            return duel.Hash();
        }
    }
}
