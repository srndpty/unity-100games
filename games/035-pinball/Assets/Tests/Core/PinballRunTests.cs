using System;
using IntGeometry;
using NUnit.Framework;

namespace Game.Core.Tests
{
    /// <summary>試合のルール（題材固有のテストを含む）。</summary>
    public sealed class PinballRunTests
    {
        private static readonly PinballConfig _config = new();

        // ---- バンパー反射と得点が一致 ----

        [Test]
        public void ReflectionFormula()
        {
            Assert.That(PinballRun.Reflect(new Vec2i(0, -10000), new Vec2i(0, 1536), Vec2i.Zero, 2048), Is.EqualTo(new Vec2i(0, 5000)), "正面：法線成分を反発係数 0.5 で返す");
            Assert.That(PinballRun.Reflect(new Vec2i(3000, -4000), new Vec2i(0, 100), Vec2i.Zero, 4096), Is.EqualTo(new Vec2i(3000, 4000)), "斜め：接線成分はそのまま");
            Assert.That(PinballRun.Reflect(new Vec2i(0, -1000), new Vec2i(0, 10), new Vec2i(0, 2000), 0), Is.EqualTo(new Vec2i(0, 2000)), "動く面：相対速度で反射して面の速さを足す");
            Assert.That(PinballRun.Reflect(new Vec2i(500, 300), new Vec2i(0, 10), Vec2i.Zero, 2048), Is.EqualTo(new Vec2i(500, 300)), "離れていく球は変えない");
        }

        /// <summary>反射の後は、丸めがあっても面に近づかない（離れるか滑る）。</summary>
        [Test]
        public void ReflectionNeverLeavesTheBallApproaching()
        {
            var random = new SeededRandom(3);
            for (var i = 0; i < 20000; i++)
            {
                var velocity = new Vec2i(random.Range(-98304, 98304), random.Range(-98304, 98304));
                var normal = new Vec2i(random.Range(-2000, 2000), random.Range(-2000, 2000));
                var surface = new Vec2i(random.Range(-20000, 20000), random.Range(-20000, 20000));
                var result = PinballRun.Reflect(velocity, normal, surface, random.Range(0, 4096));
                Assert.That(Vec2i.Dot(result - surface, normal), Is.GreaterThanOrEqualTo(0), $"{velocity} {normal} {surface}");
            }
        }

        [Test]
        public void BumperHitScoresOnceAndKicksOutward()
        {
            var run = TestKit.LabRun();
            TestKit.Drop(run, TestKit.Cell(5, 43), new Vec2i(0, -10000));
            var hits = 0;
            var hitVelocity = Vec2i.Zero;
            for (var t = 0; t < 120 && run.Status == RunStatus.Playing; t++)
            {
                var before = run.BallAt(0).Velocity;
                var events = run.Step(TestKit.None);
                if (events.BumperHits == 0) { continue; }
                hits += events.BumperHits;
                Assert.That(events.BumpersLit, Is.EqualTo(1 << 1), "真ん中のバンパー");
                Assert.That(events.ScoreGained, Is.EqualTo(_config.BumperScore));
                hitVelocity = run.BallAt(0).Velocity;

                // 真上から落とした：横の速さは 0 のまま、上向きに「弾き + 反発」の速さ（その Tick の重力の分を除く）。
                var expected = _config.BumperKick + (-before.Y * _config.BumperRestitution / PinballConfig.RatioDenominator);
                Assert.That(hitVelocity.X, Is.Zero);
                Assert.That(hitVelocity.Y, Is.InRange(expected - (Board.Substeps * _config.Gravity) - 2000, expected + 2000));
            }

            Assert.That(hits, Is.EqualTo(1), "1 回の接触で得点は 1 回");
            Assert.That(run.Score, Is.EqualTo(_config.BumperScore));
            Assert.That(hitVelocity.Y, Is.GreaterThan(0));
        }

        /// <summary>本番の台で乱数の操作を長く続け、毎 Tick「出来事の数 × 点 = 得点の増分」と、当たったバンパーから球が離れていくことを確かめる。</summary>
        [Test]
        public void ScoreAlwaysMatchesTheEvents()
        {
            var random = new SeededRandom(11);
            var run = TestKit.Run(5);
            var table = run.Table;
            var bumperHits = 0;
            while (run.Status == RunStatus.Playing && run.Ticks < 30000)
            {
                var before = run.Score;
                var input = new PinballInput(random.Next(6) == 0, random.Next(6) == 0, TestKit.HoldPlunger(run, 50));
                var events = run.Step(input);
                var expected = ((long)events.BumperHits * _config.BumperScore) + ((long)events.TargetHits * _config.TargetScore) + ((long)events.TargetSets * _config.TargetSetScore);
                Assert.That(run.Score - before, Is.EqualTo(expected));
                Assert.That(events.ScoreGained, Is.EqualTo(expected));
                bumperHits += events.BumperHits;
                foreach (var collider in table.Colliders)
                {
                    if (collider.Kind != ColliderKind.Bumper || (events.BumpersLit & (1 << collider.Number)) == 0) { continue; }
                    Assert.That(AnyBallLeaving(run, collider), Is.True, $"Tick {run.Ticks} バンパー {collider.Number}");
                }
            }

            Assert.That(bumperHits, Is.GreaterThan(10), "バンパーに十分当たっている");
        }

        // ---- 球喪失は 1 度だけ計上 ----

        [Test]
        public void LosingTheBallCountsOnceAndServesTheNext()
        {
            var run = TestKit.LabRun();
            TestKit.Drop(run, TestKit.Cell(10, 0.8), new Vec2i(0, -20000));
            var ended = 0;
            var lost = 0;
            for (var t = 0; t < 200; t++)
            {
                var events = run.Step(TestKit.None);
                lost += events.BallsLost;
                ended += events.BallEnded ? 1 : 0;
            }

            Assert.That(lost, Is.EqualTo(1));
            Assert.That(ended, Is.EqualTo(1));
            Assert.That(run.BallsLost, Is.EqualTo(1));
            Assert.That(run.BallNumber, Is.EqualTo(2));
            Assert.That(run.BallAt(0).State, Is.EqualTo(BallState.Lane), "次の球はレーンへ");
            Assert.That(run.BallsActive, Is.EqualTo(1));
        }

        [Test]
        public void TwoBallsLostInTheSameTickEndOnlyOneBall()
        {
            var run = TestKit.LabRun();
            TestKit.Drop(run, TestKit.Cell(10, 0.8), new Vec2i(0, -20000));
            run.Place(1, TestKit.Cell(13, 0.8), new Vec2i(0, -20000));
            TickEvents events;
            do { events = run.Step(TestKit.None); }
            while (events.BallsLost == 0);

            Assert.That(events.BallsLost, Is.EqualTo(2), "同じ Tick に 2 個");
            Assert.That(events.BallEnded, Is.True);
            Assert.That(run.BallNumber, Is.EqualTo(2), "球の番号は 1 つだけ進む");
            Assert.That(run.BallsInReserve, Is.EqualTo(1));
        }

        [Test]
        public void LosingOneOfTwoBallsKeepsTheBall()
        {
            var run = TestKit.LabRun();
            TestKit.Drop(run, TestKit.Cell(10, 0.8), new Vec2i(0, -20000));
            run.Place(1, TestKit.Cell(11, 30), Vec2i.Zero);
            TickEvents events;
            do { events = run.Step(TestKit.None); }
            while (events.BallsLost == 0);

            Assert.That(events.BallEnded, Is.False);
            Assert.That(run.BallNumber, Is.EqualTo(1));
            Assert.That(run.BallsActive, Is.EqualTo(1));

            // 残った球もドレインへ落ちると、そこで 1 球が終わる。
            var ended = 0;
            for (var t = 0; t < 400; t++) { ended += run.Step(TestKit.None).BallEnded ? 1 : 0; }
            Assert.That(ended, Is.EqualTo(1));
            Assert.That(run.BallNumber, Is.EqualTo(2));
            Assert.That(run.BallsLost, Is.EqualTo(2));
        }

        [Test]
        public void LeavingTheTableSidewaysIsALoss()
        {
            var run = TestKit.LabRun();
            TestKit.Drop(run, TestKit.Cell(1, 20), new Vec2i(-60000, 0));
            var lost = 0;
            for (var t = 0; t < 120; t++) { lost += run.Step(TestKit.None).BallsLost; }
            Assert.That(lost, Is.EqualTo(1));
            Assert.That(run.BallNumber, Is.EqualTo(2));
        }

        [Test]
        public void LastBallEndsTheGameOnceAndLaterStepsDoNothing()
        {
            var config = new PinballConfig { Balls = 1 };
            var run = TestKit.LabRun(string.Empty, config);
            TestKit.Drop(run, TestKit.Cell(10, 0.8), new Vec2i(0, -20000));
            var overs = 0;
            for (var t = 0; t < 20; t++) { overs += run.Step(TestKit.None).GameOver ? 1 : 0; }
            Assert.That(overs, Is.EqualTo(1));
            Assert.That(run.Status, Is.EqualTo(RunStatus.Over));

            var hash = run.Hash();
            var ticks = run.Ticks;
            var events = run.Step(new PinballInput(true, true, true));
            Assert.That(events.BallsLost + events.Launches + events.FlipperHits, Is.Zero);
            Assert.That(run.Hash(), Is.EqualTo(hash), "終了後の入力は状態を変えない");
            Assert.That(run.Ticks, Is.EqualTo(ticks));
        }

        [Test]
        public void ThreeBallsThenGameOver()
        {
            var run = TestKit.LabRun();
            for (var ball = 1; ball <= 3; ball++)
            {
                Assert.That(run.BallNumber, Is.EqualTo(ball));
                TestKit.Drop(run, TestKit.Cell(10, 0.8), new Vec2i(0, -20000));
                for (var t = 0; t < 10; t++) { run.Step(TestKit.None); }
            }

            Assert.That(run.Status, Is.EqualTo(RunStatus.Over));
            Assert.That(run.BallsInReserve, Is.Zero);
        }

        // ---- 球を複製できない ----

        [Test]
        public void PlungerLaunchesOnlyTheLaneBallOnce()
        {
            var run = TestKit.Run();
            Assert.That(run.BallAt(0).State, Is.EqualTo(BallState.Lane));
            for (var t = 0; t < 30; t++) { run.Step(TestKit.None); }
            Assert.That(run.BallAt(0).State, Is.EqualTo(BallState.Lane), "引かずに離しても打たない");

            TestKit.Plunge(run, 50);
            Assert.That(run.LastEvents.Launches, Is.EqualTo(1));
            Assert.That(run.BallAt(0).State, Is.EqualTo(BallState.Free));

            // 打ち出した後に引いて離しても、何も打ち出さない。
            var launches = 0;
            for (var t = 0; t < 30; t++) { launches += run.Step(TestKit.Plunger).Launches; }
            launches += run.Step(TestKit.None).Launches;
            Assert.That(launches, Is.Zero);
            Assert.That(run.Pull, Is.Zero);
            Assert.That(run.BallsServed, Is.EqualTo(1));
        }

        [Test]
        public void ThreeTargetsStartMultiballOnce()
        {
            var run = TestKit.LabRun();
            TestKit.Drop(run, TestKit.Cell(1, 30), Vec2i.Zero);
            var served = run.BallsServed;
            var starts = 0;
            for (var target = 0; target < 3; target++)
            {
                run.Place(0, TestKit.Cell(12.5 + (2 * target), 40.5), new Vec2i(0, 20000));
                TickEvents events;
                var t = 0;
                do
                {
                    events = run.Step(TestKit.None);
                    starts += events.MultiballStarted ? 1 : 0;
                }
                while (events.TargetHits == 0 && ++t < 30);
                Assert.That(events.TargetHits, Is.EqualTo(1));
                Assert.That(run.TargetUp(target), Is.EqualTo(target == 2), "3 枚目で全部起き直す");
            }

            Assert.That(starts, Is.EqualTo(1));
            Assert.That(run.BallsServed, Is.EqualTo(served + 1), "球は 1 個だけ増える");
            Assert.That(run.BallsActive, Is.EqualTo(2));
            Assert.That(run.BallAt(1).State, Is.EqualTo(BallState.Free), "2 個目は自動で打ち出す");
            Assert.That(run.BallAt(1).Velocity.Y, Is.InRange(_config.AutoLaunchMin - 1000, _config.AutoLaunchMax));
            Assert.That(run.Score, Is.EqualTo((3 * _config.TargetScore) + _config.TargetSetScore));

            // マルチボール中にもう一度 3 枚倒しても、球は増えない（台の上は最大 2 個）。
            run.Place(1, TestKit.Cell(1, 40), Vec2i.Zero);
            for (var target = 0; target < 3; target++)
            {
                run.Place(0, TestKit.Cell(12.5 + (2 * target), 40.5), new Vec2i(0, 20000));
                for (var t = 0; t < 30 && run.Step(TestKit.None).TargetHits == 0; t++) { }
            }

            Assert.That(run.BallsServed, Is.EqualTo(served + 1));
            Assert.That(run.BallsActive, Is.EqualTo(2));
            Assert.That(run.Score, Is.EqualTo(2 * ((3 * _config.TargetScore) + _config.TargetSetScore)), "得点は入る");
        }

        [Test]
        public void TwoBallsFinishingTheSetTogetherAddNoBall()
        {
            var run = TestKit.LabRun();
            TestKit.Drop(run, TestKit.Cell(12.5, 40.5), new Vec2i(0, 20000));
            for (var t = 0; t < 30 && run.Step(TestKit.None).TargetHits == 0; t++) { }

            run.Place(0, TestKit.Cell(14.5, 40.5), new Vec2i(0, 20000));
            run.Place(1, TestKit.Cell(16.5, 40.5), new Vec2i(0, 20000));
            var served = run.BallsServed;
            TickEvents events;
            do { events = run.Step(TestKit.None); }
            while (events.TargetHits == 0);

            Assert.That(events.TargetHits, Is.EqualTo(2), "同じ Tick に 2 枚");
            Assert.That(events.TargetSets, Is.EqualTo(1));
            Assert.That(events.MultiballStarted, Is.False, "台の上に 2 個あるので増やさない");
            Assert.That(run.BallsServed, Is.EqualTo(served));
        }

        /// <summary>乱数の操作で長く遊んでも、毎 Tick「レーン + 台の上 = 出した数 − 失った数」で、台の上は 2 個以下・レーンは 1 個以下。</summary>
        [Test]
        public void BallCountIsConservedEveryTick([Values(1, 2, 3)] int seed)
        {
            var random = new SeededRandom(seed);
            var run = TestKit.Run(seed);
            var multiballs = 0;
            while (run.Status == RunStatus.Playing && run.Ticks < 40000)
            {
                var events = run.Step(new PinballInput(random.Next(5) == 0, random.Next(5) == 0, TestKit.HoldPlunger(run, 40 + random.Next(20))));
                multiballs += events.MultiballStarted ? 1 : 0;
                var lane = 0;
                var free = 0;
                for (var slot = 0; slot < run.BallSlots; slot++)
                {
                    var state = run.BallAt(slot).State;
                    lane += state == BallState.Lane ? 1 : 0;
                    free += state == BallState.Free ? 1 : 0;
                }

                Assert.That(lane + free, Is.EqualTo(run.BallsServed - run.BallsLost));
                Assert.That(lane, Is.LessThanOrEqualTo(1));
                Assert.That(lane + free, Is.LessThanOrEqualTo(PinballConfig.MaxBalls));
                Assert.That(run.BallsServed, Is.EqualTo(run.BallNumber + multiballs));
            }
        }

        // ---- プランジャー ----

        [Test]
        public void PullIsCappedAndLaunchSpeedFollowsThePull()
        {
            var run = TestKit.LabRun();
            for (var t = 0; t < 100; t++) { run.Step(TestKit.Plunger); }
            Assert.That(run.Pull, Is.EqualTo(_config.PlungerMaxTicks));

            foreach (var pull in new[] { 1, 30, _config.PlungerMaxTicks })
            {
                var lab = TestKit.LabRun();
                TestKit.Plunge(lab, pull);
                var speed = _config.PlungerMinSpeed + ((_config.PlungerMaxSpeed - _config.PlungerMinSpeed) * (pull - 1) / (_config.PlungerMaxTicks - 1));
                Assert.That(lab.BallAt(0).Velocity, Is.EqualTo(new Vec2i(0, speed - (Board.Substeps * _config.Gravity))), $"引き {pull}");
            }
        }

        [Test]
        public void WeakLaunchFallsBackIntoTheLane()
        {
            var run = TestKit.Run();
            TestKit.Plunge(run, 1);
            var returned = false;
            for (var t = 0; t < 300 && !returned; t++)
            {
                run.Step(TestKit.None);
                returned = run.BallAt(0).State == BallState.Lane;
            }

            Assert.That(returned, Is.True);
            Assert.That(run.BallsServed, Is.EqualTo(1));
            Assert.That(run.BallsLost, Is.Zero);
        }

        [Test]
        public void FullLaunchEntersThePlayfield()
        {
            var run = TestKit.Run();
            TestKit.Plunge(run, _config.PlungerMaxTicks);
            var top = 0;
            for (var t = 0; t < 200; t++)
            {
                run.Step(TestKit.None);
                top = Math.Max(top, run.BallAt(0).Position.Y);
            }

            Assert.That(top, Is.GreaterThan(38 * TestKit.U));
            Assert.That(run.BallAt(0).Position.X, Is.LessThan(21 * TestKit.U), "台の中へ");
        }

        [Test]
        public void GateKeepsPlayfieldBallsOutOfTheLane()
        {
            var run = TestKit.Run();
            TestKit.Drop(run, TestKit.Cell(22.44, 36.5), new Vec2i(0, -10000));
            var walls = 0;
            for (var t = 0; t < 240 && run.BallAt(0).Position.X > 20 * TestKit.U; t++)
            {
                walls += run.Step(TestKit.None).WallHits;
                var ball = run.BallAt(0);
                Assert.That(ball.State, Is.EqualTo(BallState.Free), $"Tick {t}：レーンへ戻らない");
                Assert.That(ball.Position.Y, Is.GreaterThan(33 * TestKit.U), $"Tick {t}：扉より下へ入らない");
            }

            Assert.That(walls, Is.GreaterThan(0), "扉に当たった");
            Assert.That(run.BallAt(0).Position.X, Is.LessThan(20 * TestKit.U), "扉の上を転がって台へ出る");
        }

        // ---- 連続衝突検出 ----

        /// <summary>最大の速さで薄い壁に色々な角度から当てても、壁の向こうへ抜けない。</summary>
        [Test]
        public void MaxSpeedBallBouncesOffAThinWall([Range(-60, 60, 8)] int degrees)
        {
            var run = TestKit.LabRun("wall 6 20 16 20 0.125\n");
            var angle = Trig.Down + (degrees * Trig.FullTurn / 360);
            var velocity = IntMath.Scale(new Vec2i(Trig.Cos(angle), Trig.Sin(angle)), _config.MaxSpeed);
            TestKit.Drop(run, TestKit.Cell(11, 21.5), velocity);
            var walls = 0;
            for (var t = 0; t < 20; t++)
            {
                walls += run.Step(TestKit.None).WallHits;
                var ball = run.BallAt(0);
                if (ball.State != BallState.Free) { break; }
                if (ball.Position.X > (6 * TestKit.U) - 640 && ball.Position.X < (16 * TestKit.U) + 640)
                {
                    Assert.That(ball.Position.Y, Is.GreaterThan(20 * TestKit.U), $"Tick {t}");
                }
            }

            Assert.That(walls, Is.GreaterThan(0));
        }

        /// <summary>レビュー P1：残り時間は区間全体に対する割合で減る（2 回目以降の反射で進みすぎない）。</summary>
        [Test]
        public void RemainingTimeIsAFractionOfTheWholeSubstep()
        {
            var remaining = PinballRun.TimeUnits;
            remaining -= PinballRun.ConsumedTime(remaining, 50, 100);
            Assert.That(remaining, Is.EqualTo(PinballRun.TimeUnits / 2), "1 回目の経路の半分で反射：残りは半分");
            remaining -= PinballRun.ConsumedTime(remaining, 25, 50);
            Assert.That(remaining, Is.EqualTo(PinballRun.TimeUnits / 4), "残りの経路の半分で反射：残りは 1/4（1/2 ではない）");
            Assert.That(PinballRun.ConsumedTime(1, 1, 1000), Is.EqualTo(1), "進めば必ず 1 以上減る（繰り返しが終わる）");
            Assert.That(PinballRun.ConsumedTime(100, 7, 7), Is.EqualTo(100));
        }

        /// <summary>
        /// 1 区間に何度も反射する狭い溝：反発係数 1 の 2 枚の縦の壁の間を横に動く球の位置が、折り返しの厳密な位置と一致する。
        /// 1 区間に約 6 回反射するので、残り時間の数え方の誤りや接触の回数の上限（以前は 4 回で残りを捨てた）があると大きくずれる。
        /// </summary>
        [Test]
        public void ManyBouncesInOneSubstepFollowTheExactReflection([Values(0, 17, 41)] int offset)
        {
            const int Play = 60;
            const int Speed = 350;
            var left = 10 * TestKit.U;
            var right = left + 640 + Play + 640;
            var config = new PinballConfig { WallRestitution = PinballConfig.RatioDenominator };
            var walls = $"wall 10 15 10 25 0.125\nwall {Cells(right)} 15 {Cells(right)} 25 0.125\n";
            var run = TestKit.LabRun(walls, config);
            var low = left + 640;
            var high = right - 640;
            TestKit.Drop(run, new Vec2i(low + offset, 20 * TestKit.U), new Vec2i(Speed * Board.VelocityScale, 0));
            var hits = 0;
            for (var tick = 1; tick <= 3; tick++)
            {
                hits += run.Step(TestKit.None).WallHits;
                var unfolded = offset + (Speed * Board.Substeps * tick);
                var period = 2 * Play;
                var phase = unfolded % period;
                var expected = low + (phase <= Play ? phase : period - phase);
                Assert.That(run.BallAt(0).Position.X, Is.InRange(expected - 2, expected + 2), $"Tick {tick}");
                Assert.That(Math.Abs(run.BallAt(0).Velocity.X), Is.EqualTo(Speed * Board.VelocityScale), "反発係数 1 なので横の速さは変わらない");
            }

            Assert.That(hits, Is.GreaterThan(3 * Board.Substeps * 4), "1 区間に 4 回を超えて反射しても移動を捨てない");
        }

        /// <summary>
        /// レビュー P1：壁の間に球の中心が 1 単位しか動けない溝（x = 10 と 11.251 マスの壁）でも、最大の速さの球の移動を捨てずに終わる。
        /// </summary>
        [Test]
        public void NarrowChannelResolvesWithoutDroppingMotion()
        {
            // 反発係数 1（速さが落ちないので、毎区間たくさん反射し続ける）。
            var run = TestKit.LabRun("wall 10 15 10 25 0.125\nwall 11.251 15 11.251 25 0.125\n", new PinballConfig { WallRestitution = PinballConfig.RatioDenominator });
            var low = (10 * TestKit.U) + 640;
            var high = (int)Math.Round(11.251 * TestKit.U) - 640;
            TestKit.Drop(run, new Vec2i(low, 20 * TestKit.U), new Vec2i(new PinballConfig().MaxSpeed, 0));
            var hits = 0;
            for (var tick = 0; tick < 30; tick++)
            {
                hits += run.Step(TestKit.None).WallHits;
                var x = run.BallAt(0).Position.X;
                Assert.That(x, Is.InRange(low - 1, high + 1), $"Tick {tick}：溝から出ない");
            }

            Assert.That(hits, Is.GreaterThan(30 * Board.Substeps * 4), "1 区間に 4 回を超えて反射している（上限で切らない）");
            Assert.That(run.BallAt(0).Position.Y, Is.LessThan(20 * TestKit.U), "縦には重力で落ちていく");
        }

        /// <summary>球がちょうど挟まる溝（遊び 0）：横へ押しても進めない接触が両側で起きるが、射影して縦に落ち、止まらずに終わる。</summary>
        [Test]
        public void PinchedBallSlidesAlongTheGap()
        {
            var run = TestKit.LabRun("wall 10 15 10 25 0.125\nwall 11.25 15 11.25 25 0.125\n");
            var center = (10 * TestKit.U) + 640;
            TestKit.Drop(run, new Vec2i(center, 20 * TestKit.U), new Vec2i(50000, 0));
            for (var tick = 0; tick < 40; tick++)
            {
                run.Step(TestKit.None);
                Assert.That(run.BallAt(0).Position.X, Is.InRange(center - 1, center + 1), $"Tick {tick}");
            }

            Assert.That(run.BallAt(0).Position.Y, Is.LessThan(19 * TestKit.U), "挟まっても縦には落ちる");
        }

        private static string Cells(int units) => (units / (decimal)TestKit.U).ToString(System.Globalization.CultureInfo.InvariantCulture);

        /// <summary>最大の速さで、ポストへ 24 方向から当てる。どの Tick の終わりにも内側に入っていない。</summary>
        [Test]
        public void MaxSpeedBallNeverEndsInsideAPost()
        {
            for (var direction = 0; direction < 24; direction++)
            {
                var run = TestKit.LabRun("wall 11 22 11 22 1\n");
                var angle = direction * Trig.FullTurn / 24;
                var unit = new Vec2i(Trig.Cos(angle), Trig.Sin(angle));
                var start = TestKit.Cell(11, 22) + IntMath.Scale(unit, 4 * TestKit.U);
                TestKit.Drop(run, start, -IntMath.Scale(unit, _config.MaxSpeed));
                var walls = 0;
                for (var t = 0; t < 6; t++)
                {
                    walls += run.Step(TestKit.None).WallHits;
                    var ball = run.BallAt(0);
                    if (ball.State == BallState.Free)
                    {
                        Assert.That(Capsule.Inside(ball.Position, TestKit.Cell(11, 22), TestKit.Cell(11, 22), TestKit.U + _config.BallRadius), Is.False, $"方向 {direction} Tick {t}");
                    }
                }

                Assert.That(walls, Is.GreaterThan(0), $"方向 {direction}");
            }
        }

        /// <summary>
        /// 最大の速さで振るフリッパーを球がすり抜けない：腕の中ほど（10〜90%）の近く（腕の線から半径の和 + 1 マス以内）にいる間、
        /// 続く Tick で球が腕の反対側へ移らない。どの Tick の終わりにも、丸めの 1 単位を超えてフリッパーに食い込んでいない。
        /// 位置・速さ・押す時機を乱数で 300 通り（先端を回り込んで下へ出るのは正しい動きなので数えない）。
        /// </summary>
        [Test]
        public void FlipperNeverLetsTheBallThrough([Values(FlipperSide.Left, FlipperSide.Right)] FlipperSide side)
        {
            var random = new SeededRandom(side == FlipperSide.Left ? 21 : 22);
            var table = TestKit.Production();
            var spec = table.Flipper(side);
            var reach = spec.Radius + _config.BallRadius;
            var nearChecks = 0;
            for (var i = 0; i < 300; i++)
            {
                var run = new PinballRun(_config, table, 1);
                var along = random.Range(spec.Length / 5, spec.Length);
                var tip = spec.TipAt(spec.RestAngle);
                var onArm = spec.Pivot + new Vec2i((int)((long)(tip.X - spec.Pivot.X) * along / spec.Length), (int)((long)(tip.Y - spec.Pivot.Y) * along / spec.Length));
                var start = onArm + new Vec2i(0, reach + random.Range(1, 3 * TestKit.U));
                var velocity = new Vec2i(random.Range(-20000, 20000), -random.Range(0, _config.MaxSpeed / 2));
                TestKit.Drop(run, start, velocity);
                var pressAt = random.Range(0, 6);
                var releaseAt = pressAt + random.Range(1, 30);
                var previousSide = 0;
                for (var t = 0; t < 40; t++)
                {
                    var held = t >= pressAt && t < releaseAt;
                    run.Step(new PinballInput(held && side == FlipperSide.Left, held && side == FlipperSide.Right, false));
                    var ball = run.BallAt(0);
                    if (ball.State != BallState.Free) { break; }
                    var flipper = run.FlipperOf(side);
                    // 接して滑る球は経路の点の丸めで 1 単位まで食い込みうる（次の区間で押し出す）。それより深く入らない。
                    Assert.That(Capsule.Inside(ball.Position, spec.Pivot, flipper.Tip, reach - 1), Is.False, $"case {i} Tick {t}");

                    var arm = flipper.Tip - spec.Pivot;
                    var offset = ball.Position - spec.Pivot;
                    var dot = Vec2i.Dot(offset, arm);
                    var cross = Capsule.Cross(arm, offset);
                    var length = Math.Sqrt(arm.LengthSquared);
                    var near = dot > arm.LengthSquared / 10 && dot < arm.LengthSquared * 9 / 10 && Math.Abs(cross) < (reach + TestKit.U) * length;
                    if (!near)
                    {
                        previousSide = 0;
                        continue;
                    }

                    var sideNow = Math.Sign(cross);
                    if (previousSide != 0)
                    {
                        nearChecks++;
                        Assert.That(sideNow, Is.EqualTo(previousSide), $"case {i} Tick {t}：腕の反対側へ抜けた");
                    }

                    previousSide = sideNow;
                }
            }

            Assert.That(nearChecks, Is.GreaterThan(300), "腕の近くを十分に調べた");
        }

        [Test]
        public void FlippedBallIsLaunchedUpward([Values(FlipperSide.Left, FlipperSide.Right)] FlipperSide side)
        {
            var run = TestKit.Run();
            var spec = run.Table.Flipper(side);
            var tip = spec.TipAt(spec.RestAngle);
            var onArm = spec.Pivot + new Vec2i((tip.X - spec.Pivot.X) * 3 / 4, (tip.Y - spec.Pivot.Y) * 3 / 4);
            TestKit.Drop(run, onArm + new Vec2i(0, spec.Radius + _config.BallRadius + 4), Vec2i.Zero);
            var press = new PinballInput(side == FlipperSide.Left, side == FlipperSide.Right, false);
            var top = 0;
            for (var t = 0; t < 40; t++)
            {
                run.Step(press);
                top = Math.Max(top, run.BallAt(0).Position.Y);
            }

            Assert.That(top, Is.GreaterThan(spec.Pivot.Y + (10 * TestKit.U)), "打ち上がる");
        }

        // ---- 入力の即応・再現性 ----

        [Test]
        public void FlipperMovesInTheTickTheKeyIsPressed()
        {
            var run = TestKit.Run();
            run.Step(new PinballInput(true, false, false));
            var left = run.FlipperOf(FlipperSide.Left);
            Assert.That(left.Angle, Is.GreaterThan(run.Table.Left.RestAngle));
            Assert.That(run.FlipperOf(FlipperSide.Right).Angle, Is.EqualTo(run.Table.Right.RestAngle));
        }

        [Test]
        public void SameInputsGiveTheSameHash()
        {
            var a = TestKit.Run(9);
            var b = TestKit.Run(9);
            var random = new SeededRandom(9);
            for (var t = 0; t < 3000; t++)
            {
                var input = new PinballInput(random.Next(5) == 0, random.Next(5) == 0, TestKit.HoldPlunger(a, 45));
                a.Step(input);
                b.Step(input);
            }

            Assert.That(a.Hash(), Is.EqualTo(b.Hash()));
            Assert.That(TestKit.Run(10).Hash(), Is.Not.EqualTo(TestKit.Run(9).Hash()), "シードもハッシュに入る");
        }

        [Test]
        public void CloneIsIndependent()
        {
            var run = TestKit.Run();
            TestKit.Plunge(run, 50);
            var hash = run.Hash();
            var copy = run.Clone();
            Assert.That(copy.Hash(), Is.EqualTo(hash));
            for (var t = 0; t < 200; t++) { copy.Step(new PinballInput(true, true, false)); }
            Assert.That(run.Hash(), Is.EqualTo(hash));
            Assert.That(copy.Hash(), Is.Not.EqualTo(hash));
        }

        /// <summary>今の位置が同じでも以後が分かれる状態（引き・フリッパーの角速度・押下）は、違うハッシュになる（022）。</summary>
        [Test]
        public void HashSeesStateThatChangesTheFuture()
        {
            var a = TestKit.Run();
            var b = TestKit.Run();
            a.Step(TestKit.None);
            b.Step(TestKit.Plunger);
            Assert.That(a.BallAt(0).Position, Is.EqualTo(b.BallAt(0).Position));
            Assert.That(a.Hash(), Is.Not.EqualTo(b.Hash()), "引き");

            // 押して 1 Tick 進めた後と、同じ角度・違う角速度の状態。
            var c = TestKit.Run();
            var d = TestKit.Run();
            c.Step(new PinballInput(true, false, false));
            d.Step(new PinballInput(true, false, false));
            d.Step(new PinballInput(false, false, false));
            Assert.That(c.Hash(), Is.Not.EqualTo(d.Hash()));

            var config = new PinballConfig { BumperScore = 101 };
            Assert.That(new PinballRun(config, TestKit.Production(), 1).Hash(), Is.Not.EqualTo(TestKit.Run().Hash()), "設定の指紋");
        }

        private static bool AnyBallLeaving(PinballRun run, Collider bumper)
        {
            for (var slot = 0; slot < run.BallSlots; slot++)
            {
                var ball = run.BallAt(slot);
                if (ball.State != BallState.Free) { continue; }
                var offset = ball.Position - bumper.A;
                var near = (long)(bumper.Radius + (4 * TestKit.U));
                if (offset.LengthSquared < near * near && Vec2i.Dot(ball.Velocity, offset) > 0) { return true; }
            }

            return false;
        }
    }
}
