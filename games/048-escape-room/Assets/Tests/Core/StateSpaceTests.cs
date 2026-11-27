using System;
using System.Collections.Generic;
using System.Linq;
using Game.Core.Analysis;
using Game.Core.Mechanisms;
using Game.Core.Play;
using Game.Core.Rooms;
using IntGeometry;
using NUnit.Framework;

namespace Game.Core.Tests
{
    public sealed class StateSpaceTests
    {
        private static readonly HintKind[] _chain =
        {
            HintKind.Solve, HintKind.Take, HintKind.Use, HintKind.Take, HintKind.Use, HintKind.Solve, HintKind.Take,
            HintKind.Use, HintKind.Solve, HintKind.Take, HintKind.Use, HintKind.Solve, HintKind.Take, HintKind.Use,
        };

        private static StateSpaceReport? _report;

        private static StateSpaceReport Report => _report ??= StateSpace.Explore(new Room(Lab.Study, 1), 10000);

        [Test]
        public void Study_HasNoBypassDeadEndOrItemViolation()
        {
            TestContext.WriteLine($"状態 {Report.States}・遷移 {Report.Transitions}");
            Assert.That(Report.Errors, Is.Empty, string.Join("\n", Report.Errors));
            Assert.That(Report.Worlds.Any(w => w.Escaped), Is.True);
        }

        [Test]
        public void Study_StateCount_IsSmallAndStable()
        {
            // 一本の鍵の連鎖なので、抽象状態は「どこまで進んだか」の数だけになる（README に記録する値）。
            Assert.That(Report.States, Is.EqualTo(15));
        }

        [Test]
        public void Study_AnswersDoNotChangeTheStateSpace([Values(2, 3, 99)] int seed)
        {
            var other = StateSpace.Explore(new Room(Lab.Study, seed), 10000);
            Assert.That(other.Errors, Is.Empty);
            Assert.That(other.States, Is.EqualTo(Report.States));
        }

        /// <summary>探索が拒否する部屋：閉じた入れ物の中に、入れ物を開ける持ち物がある（詰み）。</summary>
        [Test]
        public void Explore_DetectsDeadEnd_WhenKeyIsInsideItsOwnLock()
        {
            var room = new Room(SmallRoom(Layout.KeyInsideOwnLock), 1);
            var report = StateSpace.Explore(room, 1000);
            Assert.That(report.Errors.Any(e => e.Contains("脱出に到達しません", StringComparison.Ordinal) || e.Contains("詰み", StringComparison.Ordinal)), Is.True, string.Join("\n", report.Errors));
        }

        [Test]
        public void Explore_AcceptsTheSameSmallRoom_WhenKeyIsOutside()
        {
            var report = StateSpace.Explore(new Room(SmallRoom(Layout.Ok), 1), 1000);
            Assert.That(report.Errors, Is.Empty, string.Join("\n", report.Errors));
        }

        // ---- レビュー P1-1：論理的に受け付ける命令が、画面でクリックして作れること ----

        [Test]
        public void Explore_RejectsGeometricallyUnclickableRooms([Values(Layout.KeyOccluded, Layout.EntranceOccluded, Layout.KeyOffScreen, Layout.KeyUnderHud)] Layout layout)
        {
            var report = StateSpace.Explore(new Room(SmallRoom(layout), 1), 1000);
            TestContext.WriteLine(string.Join("\n", report.Errors));
            Assert.That(report.Errors.Any(e => e.StartsWith("クリックできない", StringComparison.Ordinal)), Is.True, string.Join("\n", report.Errors));
            Assert.That(report.Errors.Any(e => e.Contains("脱出に到達しません", StringComparison.Ordinal)), Is.True, "クリックできない手を除くと脱出に届かない");

            // 論理だけ（World.Check）なら、次の一手を命令で直接当てはめて脱出に届く：遮蔽を見逃していたのはこの差。
            var w = new World(new Room(SmallRoom(layout), 1));
            for (int i = 0; i < 20 && !w.Escaped; i++) { Lab.Advance(w); }
            Assert.That(w.Escaped, Is.True);
        }

        [Test]
        public void Explore_AcceptsPartThatBecomesClickableWhenPlateOpens()
        {
            var report = StateSpace.Explore(new Room(SmallRoom(Layout.KeyBehindPlate), 1), 1000);
            Assert.That(report.Errors, Is.Empty, string.Join("\n", report.Errors));
            var start = report.Worlds[0];
            var w = start.Clone();
            foreach (var c in Navigation.PathTo(w, 4)) { w.Apply(c); }
            Assert.That(ClickSearch.PointFor(w, Command.Take(w.Def.PickupOf(0)), -1, ScreenRegion.Guaranteed), Is.Null, "閉じた前板の奥の鍵は（入れ物が開くまで）クリックできない");
        }

        [Test]
        public void Explore_Study_IsClickableInGuaranteedRegion_AndTimed()
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            var report = StateSpace.Explore(new Room(Lab.Study, 1), 10000, ScreenRegion.Guaranteed);
            watch.Stop();
            TestContext.WriteLine($"状態 {report.States}・遷移 {report.Transitions}・{watch.Elapsed.TotalMilliseconds:F1} ms");
            Assert.That(report.Errors, Is.Empty, string.Join("\n", report.Errors));
        }

        // ---- 題材固有：ヒント表示と実状態が一致 ----

        [Test]
        public void Hint_IsConsistent_InEveryReachableState()
        {
            foreach (var rep in Report.Worlds)
            {
                if (rep.Escaped) { continue; }
                var step = HintEngine.Next(rep);
                var def = rep.Def;
                switch (step.Kind)
                {
                    case HintKind.Solve:
                        Assert.That(rep.PhaseOf(step.Mechanism), Is.EqualTo(Phase.Active), "案内する仕掛けは操作できる");
                        break;
                    case HintKind.Take:
                        Assert.That(rep.IsReachable(step.Part), Is.True, "案内する物は触れられる（閉じた入れ物の中ではない）");
                        break;
                    default:
                        Assert.That(rep.Has(step.Item), Is.True);
                        Assert.That(rep.PhaseOf(step.Mechanism), Is.EqualTo(Phase.Locked));
                        break;
                }

                // 案内する操作は（近景へ移れば）受け付けられ、答えの段階のとおりに操作すると一手進む。
                var w = rep.Clone();
                Lab.GoTo(w, step.View(def));
                var answer = step.Answer(w);
                Assert.That(answer, Is.Not.Empty);
                Assert.That(w.Check(answer[0]).Accepted, Is.True, $"{step.Kind} の最初の操作 {answer[0]} を受け付けません");
                var before = w.AbstractKey();
                foreach (var c in answer) { Lab.Do(w, c); }
                Assert.That(w.AbstractKey(), Is.Not.EqualTo(before), "一手進む");
                switch (step.Kind)
                {
                    case HintKind.Solve:
                        Assert.That(w.IsSolved(step.Mechanism), Is.True, "答えの段階のとおりに操作すると解ける");
                        break;
                    case HintKind.Take:
                        Assert.That(w.Has(step.Item), Is.True);
                        break;
                    default:
                        Assert.That(w.PhaseOf(step.Mechanism), Is.Not.EqualTo(Phase.Locked));
                        break;
                }

                // 3 段階の文はどれも空でなく、答えの段階は今のシードの答えを含む。
                for (int level = 0; level <= HintEngine.MaxLevel; level++) { Assert.That(HintEngine.Text(rep, step, level), Is.Not.Empty); }
                if (step.Kind == HintKind.Solve)
                {
                    var text = HintEngine.Text(rep, step, HintEngine.MaxLevel);
                    Assert.That(text.Contains(rep.Room.Mechanisms[step.Mechanism].AnswerText, StringComparison.Ordinal), Is.True, text);
                }
            }
        }

        [Test]
        public void Hint_NeverPointsAtSolvedOrLockedBeyond_AlongWholePlaythrough([Values(1, 5, 31)] int seed)
        {
            var w = Lab.NewWorld(seed);
            var kinds = new List<HintKind>();
            while (!w.Escaped)
            {
                var step = HintEngine.Next(w);
                kinds.Add(step.Kind);
                if (step.Mechanism >= 0) { Assert.That(w.IsSolved(step.Mechanism), Is.False); }
                Lab.Advance(w);
            }

            // 解く・拾う・使うの順に連鎖をたどる：ダイヤル → 鍵 → キャビネット → 円盤 → はめる → リング → 歯車 → はめる → 針 → ハンドル → はめる → 宝石 → 出口の鍵 → 扉。
            Assert.That(kinds, Is.EqualTo(_chain));
        }

        // ---- 題材固有：同時操作で状態破綻なし（Core の部分：乱択の長い命令列で毎回の不変条件） ----

        [Test]
        public void RandomCommands_KeepInvariants([Values(1, 2, 3, 4)] int seed)
        {
            var r = new SeededRandom(seed * 7919);
            var w = Lab.NewWorld(seed);
            var def = w.Def;
            var solvedBefore = new bool[def.Mechanisms.Count];
            int accepted = 0;
            for (int n = 0; n < 6000; n++)
            {
                var c = RandomCommand(r, w);
                var hash = w.Hash();
                var o = w.Apply(c);
                if (!o.Accepted)
                {
                    Assert.That(w.Hash(), Is.EqualTo(hash), $"拒否 {c} で状態が変わりました");
                    continue;
                }

                accepted++;
                if (c.Kind == CommandKind.Reset)
                {
                    Array.Clear(solvedBefore, 0, solvedBefore.Length);
                    continue;
                }

                Assert.That(w.Inventory.Distinct().Count(), Is.EqualTo(w.Inventory.Count), "持ち物が重複しない");
                for (int m = 0; m < def.Mechanisms.Count; m++)
                {
                    if (solvedBefore[m]) { Assert.That(w.IsSolved(m), Is.True, "Solved は戻らない"); }
                    solvedBefore[m] = w.IsSolved(m);
                }

                if (w.Escaped) { Assert.That(w.SolvedCount, Is.EqualTo(def.Mechanisms.Count), "出口は全てを解いた後にだけ開く"); }
            }

            Assert.That(accepted, Is.GreaterThan(1000));
        }

        [Test]
        public void SameSeedAndCommands_GiveSameHash_AndFinalHashIsFixed()
        {
            var a = Lab.NewWorld(1);
            var b = Lab.NewWorld(1);
            Lab.Escape(a);
            Lab.Escape(b);
            Assert.That(a.Hash(), Is.EqualTo(b.Hash()));
            Assert.That(Lab.NewWorld(2).Hash(), Is.Not.EqualTo(Lab.NewWorld(1).Hash()), "シードが違えば答えが違い、ハッシュも違う");
            TestContext.WriteLine($"操作 {a.Actions}・ハッシュ 0x{a.Hash():X8}");
            Assert.That(a.Hash(), Is.EqualTo(0x9EB22E21u), "整数だけの規則なので、.NET と Mono で同じ値になる");
        }

        private static Command RandomCommand(SeededRandom r, World w)
        {
            var def = w.Def;
            int part = r.Range(-1, def.Parts.Count);
            int item = r.Range(-1, def.Items.Count);
            switch (r.Next(14))
            {
                case 0: return Command.Turn(r.Next(2) == 0 ? -1 : 1);
                case 1: return Command.Focus(r.Range(-1, def.Views.Count));
                case 2: return Command.Back;
                case 3:
                case 4: return Command.Operate(part);
                case 5: return Command.UseItem(item, part);
                case 6: return Command.Take(part);
                case 7: return Command.Inspect(part);
                case 8: return Command.InspectItem(item);
                case 9: return Command.Rotate(r.Range(-1, 1), r.Range(-1, 1));
                case 10: return Command.Hint;
                case 11: return r.Next(40) == 0 ? Command.Reset : Command.Hint;
                default:
                    {
                        // 次の一手の答えを混ぜて、奥まで進む列にする。
                        if (w.Escaped) { return Command.Back; }
                        var step = HintEngine.Next(w);
                        var path = Navigation.PathTo(w, step.View(def));
                        return path.Count > 0 ? path[0] : step.Answer(w)[0];
                    }
            }
        }

        public enum Layout
        {
            /// <summary>鍵は引き出し（ダイヤル）の中に見えている。</summary>
            Ok,

            /// <summary>鍵がその鍵で開くキャビネットの中（詰み）。</summary>
            KeyInsideOwnLock,

            /// <summary>鍵の手前に、いつもある箱（完全に遮る）。</summary>
            KeyOccluded,

            /// <summary>壁の視点で、近景へ寄る家具を別の近景の衝立が完全に遮る。</summary>
            EntranceOccluded,

            /// <summary>鍵が近景の画面の横の外。</summary>
            KeyOffScreen,

            /// <summary>鍵が HUD の帯の下にしか写らない。</summary>
            KeyUnderHud,

            /// <summary>鍵の手前に、ダイヤルが解けると消える前板（解ければ触れる）。</summary>
            KeyBehindPlate,
        }

        /// <summary>
        /// 小さな部屋：ダイヤルが解けると鍵が取れ、鍵でキャビネットを開けると出口の鍵がある。
        /// 近景の部品は画面の中央の高さ（y 1550〜1650）に並べ、配置の変え方で幾何的に触れない部屋を作る。
        /// </summary>
        internal static RoomDef SmallRoom(Layout layout)
        {
            static Geometry.Box3 B(int x0, int y0, int z0, int x1, int y1, int z1) => new(new Geometry.Vec3i(x0, y0, z0), new Geometry.Vec3i(x1, y1, z1));
            var b = new RoomBuilder("テスト", B(-2000, 0, -2000, 2000, 2800, 2000));
            var clue = b.Examinable("note", "メモ", new Geometry.Vec3i(0, 0, 1), new Geometry.Vec3i(0, 1, 0), 0);
            var key = b.Item("key", "鍵", "引き出し");
            var exitKey = b.Item("exit_key", "出口の鍵", "キャビネット");
            b.Mechanism("dial", "ダイヤル", MechanismKind.Dial, -1, "a", "b");
            b.Mechanism("cabinet", "キャビネット", MechanismKind.KeyLock, key, "a", "b");
            b.Mechanism("door", "扉", MechanismKind.KeyLock, exitKey, "a", "b");
            for (int w = 0; w < 4; w++) { b.WallView("wall" + w, "壁", new Geometry.CameraPose(new Geometry.Vec3i(0, 1600, 0), w, 0, 1500)); }
            var near = b.CloseUp("near", "近く", new Geometry.CameraPose(new Geometry.Vec3i(0, 1600, 500), 0, 0, 1500));
            var screenView = layout == Layout.EntranceOccluded ? b.CloseUp("screen", "衝立", new Geometry.CameraPose(new Geometry.Vec3i(0, 1600, 100), 0, 0, 1500)) : -1;
            b.Examine("note", "メモ", near, B(-900, 1550, 1500, -800, 1650, 1600), clue);
            for (int i = 0; i < 3; i++) { b.Slot($"dial{i}", "桁", near, B(-600 + (i * 100), 1550, 1500, -520 + (i * 100), 1650, 1600), 0, i); }
            var keyBox = layout switch
            {
                Layout.KeyOffScreen => B(1900, 1550, 1500, 1990, 1650, 1600),
                Layout.KeyUnderHud => B(0, 960, 1500, 100, 1000, 1600),
                _ => B(0, 1550, 1500, 100, 1650, 1600),
            };
            b.Pickup("key", "鍵", near, keyBox, key, layout == Layout.KeyInsideOwnLock ? 1 : 0);
            if (layout == Layout.KeyOccluded) { b.Body("crate", "箱", near, B(-20, 1530, 1300, 120, 1670, 1320)); }
            if (layout == Layout.KeyBehindPlate) { b.Body("plate", "前板", near, B(-20, 1530, 1300, 120, 1670, 1320), Presence.WhileClosed, 0); }
            if (layout == Layout.EntranceOccluded) { b.Body("screen", "衝立", screenView, B(-1900, 10, 200, 1900, 2700, 220)); }
            b.Keyhole("keyhole", "鍵穴", near, B(300, 1550, 1500, 400, 1650, 1600), 1);
            b.Pickup("exit_key", "出口の鍵", near, B(500, 1550, 1500, 600, 1650, 1600), exitKey, 1);
            b.Keyhole("door", "扉の鍵穴", near, B(700, 1550, 1500, 800, 1650, 1600), 2);
            return b.Build(2);
        }
    }
}
