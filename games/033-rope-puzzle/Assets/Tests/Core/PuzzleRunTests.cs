using System;
using IntGeometry;
using NUnit.Framework;
using static Game.Core.Tests.TestKit;

namespace Game.Core.Tests
{
    public sealed class PuzzleRunTests
    {
        private const string Hanging = "body 128 100\nrope 128 136 40 10\ngoal 200 24 10";
        private const string TwoRopes = "body 128 96\nrope 80 136 64 14\nrope 176 136 64 14\ngoal 32 20 10";

        // ---- 切ったロープは拘束を失う ----

        [Test]
        public void CutRopeReleasesBodyIntoFreeFall()
        {
            var run = Run(Hanging);
            Ticks(run, 240);
            var rest = run.Body;
            run.Tick();
            Assert.That(run.Body, Is.EqualTo(rest), "切る前に露玉が止まっている（速さ 0）");

            CutRope(run, 0);
            Assert.That(run.Holds(0), Is.False);

            // 素朴な参照：速さ 0 から、重力と減衰（0 方向へ切り捨て）だけで落ちる点。
            var config = new RopeConfig();
            long y = rest.Y;
            long vy = 0;
            for (var i = 0; i < 40 && run.Status == RunStatus.Playing; i++)
            {
                vy = (vy * config.Damping / RopeConfig.RatioDenominator) - config.Gravity;
                if (-vy > config.MaxStep) { vy = -config.MaxStep; }
                y += vy;
                run.Tick();
                Assert.That(run.Body, Is.EqualTo(new Vec2i(rest.X, (int)y)), $"切ってから {i + 1} Tick");
            }
        }

        [Test]
        public void CutRopeNoLongerLimitsDistanceFromItsAnchor()
        {
            var run = Run(TwoRopes);
            var right = run.Puzzle.Ropes[1];
            CutRope(run, 1);
            var farthest = 0.0;
            for (var i = 0; i < 300; i++)
            {
                run.Tick();
                farthest = Math.Max(farthest, Math.Sqrt(Vec2i.DistanceSquared(run.Body, right.Anchor)));
                Assert.That(Stretch(run, 0), Is.LessThan(1.08), "残ったロープは伸びすぎない");
            }

            Assert.That(farthest, Is.GreaterThan(right.Length * 1.2), "切ったロープの長さを超えて離れる");
        }

        [Test]
        public void FragmentsSplitAtTheCutLink()
        {
            // 露玉は台で受け止める（落ちて失敗すると試合が止まる）。ロープの断片は台を通り抜けて落ちる。
            var run = Run(Hanging + "\nwall 64 40 192 48");
            Ticks(run, 120);
            CutRope(run, 0);
            var cut = run.CutLinkOf(0);
            Assert.That(cut, Is.InRange(0, run.LinksOf(0) - 1));
            var anchor = run.Puzzle.Ropes[0].Anchor;
            var rest = run.Puzzle.Ropes[0].RestLength;
            for (var i = 0; i < 600 && !run.IsFragmentGone(0); i++)
            {
                run.Tick();
                var end = run.RopePoint(0, cut);
                Assert.That(Math.Sqrt(Vec2i.DistanceSquared(end, anchor)), Is.LessThanOrEqualTo(rest * cut * 1.1 + 1), "支点側の断片は支点から垂れる");
            }

            Assert.That(run.IsFragmentGone(0), Is.True, "露玉側の断片は落ちて消える");
            var frozen = run.RopePoint(0, cut + 1);
            Ticks(run, 10);
            Assert.That(run.RopePoint(0, cut + 1), Is.EqualTo(frozen), "消えた断片は動かない");
        }

        // ---- 同一ロープの重複切断安全 ----

        [Test]
        public void SwipeAlongWholeRopeCutsItOnce()
        {
            var run = Run(Hanging);
            Assert.That(run.Cut(P(128, 140), P(128, 90)), Is.EqualTo(1), "全ての節に重なるなぞりでも 1 本");
            Assert.That(run.Moves, Is.EqualTo(1));
            Assert.That(run.CutLinkOf(0), Is.EqualTo(0), "最初に横切った節（支点側）で切る");
        }

        [Test]
        public void SwipingCutRopeAgainChangesNothing()
        {
            var run = Run(Hanging);
            Ticks(run, 60);
            var swipe = CutPlan.SwipeAcross(run, 0)!.Value;
            Assert.That(run.Cut(swipe.From, swipe.To), Is.EqualTo(1));
            var hash = run.Hash();
            Assert.That(run.Cut(swipe.From, swipe.To), Is.Zero, "同じ Tick に同じなぞり");
            Assert.That(run.Cut(P(100, 130), P(156, 130)), Is.Zero, "支点側の断片をなぞる");
            Assert.That(run.Moves, Is.EqualTo(1));
            Assert.That(run.Hash(), Is.EqualTo(hash));
            Assert.That(CutPlan.SwipeAcross(run, 0), Is.Null);

            run.Tick();
            Assert.That(run.Cut(P(100, 120), P(156, 60)), Is.Zero, "次の Tick に断片をなぞる");
            Assert.That(run.Moves, Is.EqualTo(1));
        }

        [Test]
        public void OneSwipeCanCutSeveralRopesCountingEach()
        {
            var run = Run(TwoRopes);
            Assert.That(run.Cut(P(40, 120), P(220, 120)), Is.EqualTo(2));
            Assert.That(run.Moves, Is.EqualTo(2));
            Assert.That(run.Holds(0) || run.Holds(1), Is.False);
        }

        [Test]
        public void OtherRopesAreCutIndependently()
        {
            var run = Run(TwoRopes);
            CutRope(run, 0);
            Assert.That(run.Holds(1), Is.True);
            Ticks(run, 5);
            CutRope(run, 1);
            Assert.That(run.Moves, Is.EqualTo(2));
        }

        [Test]
        public void CutsAfterTheEndAreIgnored()
        {
            // 支点 A を切ると、たるんでいた B で振れて最下点の目標に触れる（B は切らずに残る）。
            var run = Run("body 128 100\nrope 128 136 40 10\nrope 100 136 60 12\ngoal 100 72 6");
            CutRope(run, 0);
            Ticks(run, 240);
            Assert.That(run.Status, Is.Not.EqualTo(RunStatus.Playing));
            var hash = run.Hash();
            var moves = run.Moves;
            Assert.That(run.Holds(1), Is.True);
            Assert.That(run.Cut(P(60, 120), P(140, 120)), Is.Zero, "残ったロープも切れない");
            run.Tick();
            Assert.That(run.Hash(), Is.EqualTo(hash), "終わった後の Tick と切断は状態を変えない");
            Assert.That(run.Moves, Is.EqualTo(moves));
        }

        [Test]
        public void ZeroLengthSwipeCutsNothing()
        {
            var run = Run(Hanging);
            Assert.That(run.Cut(P(128, 120), P(128, 120)), Is.Zero);
            Assert.That(run.Moves, Is.Zero);
        }

        [Test]
        public void SwipeOutsideAcceptedRangeThrows()
        {
            var run = Run(Hanging);
            Assert.Throws<ArgumentOutOfRangeException>(() => run.Cut(new Vec2i(-Board.Width - 1, 0), P(128, 120)));
            Assert.Throws<ArgumentOutOfRangeException>(() => run.Cut(P(128, 120), new Vec2i(0, (2 * Board.Height) + 1)));
            Assert.That(run.Moves, Is.Zero);
        }

        // ---- 成功判定は物体到達時のみ ----

        [Test]
        public void HangingAboveGoalNeverSucceeds()
        {
            var run = Run("body 128 100\nrope 128 136 40 10\ngoal 128 70 10");
            Ticks(run, 3000);
            Assert.That(run.Status, Is.EqualTo(RunStatus.Playing), "吊られたまま届かない");
        }

        [Test]
        public void CuttingEverythingAwayFromGoalFails()
        {
            var run = Run(TwoRopes.Replace("goal 32 20 10", "goal 230 120 10"));
            run.Cut(P(40, 120), P(220, 120));
            Ticks(run, 600);
            Assert.That(run.Status, Is.EqualTo(RunStatus.Failed));
            Assert.That(run.Reason, Is.EqualTo(FailReason.Fell));
            Assert.That(run.TakeEvents() & RunEvents.Succeeded, Is.EqualTo(RunEvents.None));
        }

        [Test]
        public void TouchingGoalWhileStillHangingSucceeds()
        {
            // 振り子の最下点に目標がある。何も切らなくても、触れた Tick に成功する。
            var run = Run("body 60 120\nrope 100 136 48 12\ngoal 100 84 6");
            var ticks = 0;
            while (run.Status == RunStatus.Playing && ticks++ < 200)
            {
                run.Tick();
            }

            Assert.That(run.Status, Is.EqualTo(RunStatus.Succeeded));
            Assert.That(run.Moves, Is.Zero);
            Assert.That(run.Holds(0), Is.True);
            Assert.That(Math.Sqrt(Vec2i.DistanceSquared(run.Body, run.Puzzle.Goal.Center)), Is.LessThan(run.Puzzle.Goal.Radius + new RopeConfig().BodyRadius + new RopeConfig().MaxStep));
        }

        [Test]
        public void SuccessIsDecidedOnTheTickOfArrival()
        {
            var run = Run("body 128 100\nrope 128 136 40 10\ngoal 128 40 10");
            CutRope(run, 0);
            var config = new RopeConfig();
            var touch = (long)run.Puzzle.Goal.Radius + config.BodyRadius;
            while (run.Status == RunStatus.Playing)
            {
                var before = run.Body;
                run.Tick();
                var distance = Math.Sqrt(Vec2i.DistanceSquared(run.Body, run.Puzzle.Goal.Center));
                if (run.Status == RunStatus.Playing)
                {
                    Assert.That(distance, Is.GreaterThanOrEqualTo(touch), $"Tick {run.Ticks}：触れる前に成功しない");
                }
                else
                {
                    Assert.That(Math.Sqrt(Vec2i.DistanceSquared(before, run.Puzzle.Goal.Center)), Is.GreaterThanOrEqualTo(touch), "前の Tick では触れていない");
                }
            }

            Assert.That(run.Status, Is.EqualTo(RunStatus.Succeeded));
            Assert.That(run.TakeEvents() & RunEvents.Succeeded, Is.EqualTo(RunEvents.Succeeded));
        }

        /// <summary>速く落ちて 1 Tick で目標を飛び越えても、Tick 中の軌跡で触れたら成功する。</summary>
        [Test]
        public void FastFallThroughGoalIsCaughtBySweep()
        {
            var config = Fast();
            var puzzle = One("body 128 120\nrope 128 136 20 4\ngoal 128 60 1");
            var run = new PuzzleRun(config, puzzle);
            CutRope(run, 0);
            var crossed = false;
            while (run.Status == RunStatus.Playing && run.Ticks < 120)
            {
                var before = run.Body;
                run.Tick();
                var after = run.Body;
                if (before.Y > puzzle.Goal.Center.Y && after.Y < puzzle.Goal.Center.Y)
                {
                    var reach = puzzle.Goal.Radius + config.BodyRadius;
                    crossed = Math.Abs(before.Y - puzzle.Goal.Center.Y) >= reach && Math.Abs(after.Y - puzzle.Goal.Center.Y) >= reach;
                }
            }

            Assert.That(crossed, Is.True, "始めと終わりの位置はどちらも目標に触れていない（飛び越えた）");
            Assert.That(run.Status, Is.EqualTo(RunStatus.Succeeded));
        }

        /// <summary>同じ Tick に目標とトゲの両方に触れたら、目標が先でも失敗。</summary>
        [Test]
        public void GoalAndSpikeInSameTickFails()
        {
            var config = Fast();
            var puzzle = One("body 128 120\nrope 128 136 20 4\ngoal 128 60 1\nspike 128 56 1");
            var run = new PuzzleRun(config, puzzle);
            CutRope(run, 0);
            Ticks(run, 120);
            Assert.That(run.Status, Is.EqualTo(RunStatus.Failed));
            Assert.That(run.Reason, Is.EqualTo(FailReason.Spike));

            var safe = new PuzzleRun(config, One("body 128 120\nrope 128 136 20 4\ngoal 128 60 1"));
            CutRope(safe, 0);
            Ticks(safe, 120);
            Assert.That(safe.Status, Is.EqualTo(RunStatus.Succeeded), "トゲがなければ同じ軌跡で成功する（同じ Tick に両方へ触れていた）");
            Assert.That(safe.Ticks, Is.EqualTo(run.Ticks));
        }

        [Test]
        public void SpikeFails()
        {
            var run = Run("body 128 100\nrope 128 136 40 10\nspike 128 40 8\ngoal 30 20 10");
            CutRope(run, 0);
            Ticks(run, 300);
            Assert.That(run.Status, Is.EqualTo(RunStatus.Failed));
            Assert.That(run.Reason, Is.EqualTo(FailReason.Spike));
        }

        [Test]
        public void LeavingSidewaysFails()
        {
            var run = Run("body 30 120\nrope 60 136 40 10\ngoal 200 20 10");
            Ticks(run, 20);
            CutRope(run, 0);
            Ticks(run, 600);
            Assert.That(run.Status, Is.EqualTo(RunStatus.Failed));
            Assert.That(run.Reason, Is.EqualTo(FailReason.Fell));
        }

        // ---- 壁 ----

        [Test]
        public void BodyRestsOnWallWithoutSinking()
        {
            var run = Run("body 128 100\nrope 128 136 40 10\nwall 64 40 192 48\ngoal 30 120 10");
            CutRope(run, 0);
            var r = new RopeConfig().BodyRadius;
            for (var i = 0; i < 600; i++)
            {
                run.Tick();
                Assert.That(run.Body.Y, Is.GreaterThanOrEqualTo((48 * U) + r - 2), $"Tick {run.Ticks}：壁の中に残らない");
            }

            var rest = run.Body;
            Ticks(run, 10);
            Assert.That(run.Body, Is.EqualTo(rest), "壁の上で止まる");
            Assert.That(run.Status, Is.EqualTo(RunStatus.Playing));
        }

        [Test]
        public void BodySlidesOffLedgeEdge()
        {
            // 斜めに落ちた露玉は、棚の上を滑って端から落ちる。
            var run = Run("body 104 128\nrope 150 136 48 12\nwall 120 56 200 64\ngoal 216 24 10");
            Ticks(run, 30);
            CutRope(run, 0);
            var landed = false;
            for (var i = 0; i < 400 && run.Status == RunStatus.Playing; i++)
            {
                run.Tick();
                landed |= run.Body.Y < 72 * U && run.Body.X > 120 * U;
            }

            Assert.That(landed, Is.True);
            Assert.That(run.Status, Is.EqualTo(RunStatus.Succeeded));
        }

        // ---- 再現性・リセット・複製 ----

        [Test]
        public void ResetRestoresInitialState()
        {
            var fresh = Run(TwoRopes);
            var run = Run(TwoRopes);
            Ticks(run, 30);
            CutRope(run, 0);
            Ticks(run, 30);
            run.Reset();
            Assert.That(run.Hash(), Is.EqualTo(fresh.Hash()));
            Assert.That(run.Moves, Is.Zero);
            Assert.That(run.Ticks, Is.Zero);
            Assert.That(run.Holds(0) && run.Holds(1), Is.True);
        }

        [Test]
        public void CloneIsIndependent()
        {
            var run = Run(TwoRopes);
            Ticks(run, 10);
            var copy = run.Clone();
            CutRope(copy, 0);
            Ticks(copy, 10);
            Assert.That(run.Holds(0), Is.True);
            Assert.That(run.Ticks, Is.EqualTo(10));

            var again = run.Clone();
            CutRope(again, 0);
            Ticks(again, 10);
            Assert.That(again.Hash(), Is.EqualTo(copy.Hash()), "同じ操作の列なら同じ状態");
        }

        [Test]
        public void HashDistinguishesWhichLinkWasCut()
        {
            // 同じ Tick・同じ位置で、切った節だけが違う。以後の動き（断片の分かれ方）が変わるのでハッシュも違う。
            var a = Run(Hanging);
            var b = Run(Hanging);
            Ticks(a, 60);
            Ticks(b, 60);
            Assert.That(a.Cut(P(120, 130), P(136, 130)), Is.EqualTo(1));
            Assert.That(b.Cut(P(120, 110), P(136, 110)), Is.EqualTo(1));
            Assert.That(a.CutLinkOf(0), Is.Not.EqualTo(b.CutLinkOf(0)));
            Assert.That(a.Body, Is.EqualTo(b.Body));
            Assert.That(a.Hash(), Is.Not.EqualTo(b.Hash()));
        }

        /// <summary>
        /// レビュー：今の位置が同じでも、設定や問の定義が違えば次の Tick で分かれる組はハッシュが違う。
        /// 名前・案内・目安の手数だけが違う問は動きが同じなので、ハッシュも同じ。
        /// </summary>
        [Test]
        public void HashIncludesConfigAndPuzzleDefinition()
        {
            var puzzle = One(Hanging);
            var a = new PuzzleRun(new RopeConfig(), puzzle);
            var b = new PuzzleRun(new RopeConfig { Gravity = new RopeConfig().Gravity + 1 }, puzzle);
            Assert.That(a.Body, Is.EqualTo(b.Body));
            Assert.That(a.Hash(), Is.Not.EqualTo(b.Hash()), "重力だけが違う");
            a.Tick();
            b.Tick();
            Assert.That(a.Body, Is.Not.EqualTo(b.Body), "次の Tick で分かれる");

            var config = new RopeConfig();
            var basis = new PuzzleRun(config, puzzle).Hash();
            Assert.That(new PuzzleRun(config, One(Hanging.Replace("goal 200 24 10", "goal 200 24 11"))).Hash(), Is.Not.EqualTo(basis), "目標の大きさだけが違う");
            Assert.That(new PuzzleRun(config, One(Hanging + "\nspike 30 20 6")).Hash(), Is.Not.EqualTo(basis), "トゲだけが違う");
            Assert.That(new PuzzleRun(config, One(Hanging + "\nwall 10 10 40 20")).Hash(), Is.Not.EqualTo(basis), "壁だけが違う");
            Assert.That(new PuzzleRun(new RopeConfig { WallFriction = 1 }, puzzle).Hash(), Is.Not.EqualTo(basis), "壁の摩擦だけが違う");
            var renamed = PuzzleBook.Parse($"puzzle 別名\nhint 別の案内\npar 1\n{Hanging}\nend\n").Puzzles[0];
            Assert.That(new PuzzleRun(config, renamed).Hash(), Is.EqualTo(basis), "名前と案内だけが違う問は同じ");
        }

        [Test]
        public void LongRunStaysBounded()
        {
            var run = Run("body 60 110\nrope 100 136 48 12\nrope 180 136 128 20\ngoal 240 140 4");
            for (var i = 0; i < 20000; i++)
            {
                run.Tick();
                Assert.That(Board.Contains(run.Body), Is.True);
                if (i % 500 == 0)
                {
                    Assert.That(Stretch(run, 0), Is.LessThan(1.08));
                    Assert.That(Stretch(run, 1), Is.LessThan(1.08));
                }
            }

            Assert.That(run.Status, Is.EqualTo(RunStatus.Playing));
        }

        [Test]
        public void ConfigIsCopied()
        {
            var config = new RopeConfig();
            var run = new PuzzleRun(config, One(Hanging));
            config.Gravity = 0;
            var copy = run.Config;
            copy.Gravity = 0;
            Assert.That(run.Config.Gravity, Is.EqualTo(new RopeConfig().Gravity));
        }

        [Test]
        public void InvalidConfigIsRejected()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new PuzzleRun(new RopeConfig { Iterations = 0 }, One(Hanging)));
            Assert.Throws<ArgumentOutOfRangeException>(() => new PuzzleRun(new RopeConfig { Damping = RopeConfig.RatioDenominator + 1 }, One(Hanging)));
            Assert.Throws<ArgumentOutOfRangeException>(() => new PuzzleRun(new RopeConfig { MaxStep = new RopeConfig().Gravity }, One(Hanging)));
            Assert.Throws<ArgumentOutOfRangeException>(() => new PuzzleRun(new RopeConfig { BodyRadius = 0 }, One(Hanging)));
        }

        [Test]
        public void RopePointRejectsOutOfRange()
        {
            var run = Run(Hanging);
            Assert.Throws<ArgumentOutOfRangeException>(() => run.RopePoint(1, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => run.RopePoint(0, run.LinksOf(0) + 1));
            Assert.That(run.RopePoint(0, run.LinksOf(0)), Is.EqualTo(run.Body), "最後の点は露玉");
        }

        /// <summary>小さな露玉と大きな移動量の設定（Tick 中の軌跡の判定を確かめる）。</summary>
        private static RopeConfig Fast() => new()
        {
            BodyRadius = Board.Unit / 16,
            Gravity = 4096,
            MaxStep = Board.Unit,
        };
    }
}
