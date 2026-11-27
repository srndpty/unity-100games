using System;
using System.Linq;
using Game.Core.Geometry;
using Game.Core.Mechanisms;
using Game.Core.Play;
using Game.Core.Rooms;
using NUnit.Framework;

namespace Game.Core.Tests
{
    public sealed class WorldTests
    {
        private static readonly int[] _hintLevels = { 0, 1, 2, 2 };
        private static readonly EventKind[] _installEvents = { EventKind.ItemUsed, EventKind.Installed };

        /// <summary>拒否され、状態（ハッシュ・操作の回数）も出来事も変わらないことを確かめる。</summary>
        private static void AssertRejected(World w, Command c, Refusal reason)
        {
            var hash = w.Hash();
            var actions = w.Actions;
            var o = w.Apply(c);
            Assert.That(o.Reason, Is.EqualTo(reason), c.ToString());
            Assert.That(w.Hash(), Is.EqualTo(hash), $"{c} の拒否で状態が変わりました");
            Assert.That(w.Actions, Is.EqualTo(actions));
            Assert.That(w.Events, Is.Empty, "拒否では出来事が起きない");
        }

        [Test]
        public void Start_IsNorthWall_DialActive_OthersLocked()
        {
            var w = Lab.NewWorld();
            Assert.That(w.View, Is.EqualTo(Lab.View("north")));
            Assert.That(w.OnWall, Is.True);
            Assert.That(w.PhaseOf(Lab.Mechanism("dial")), Is.EqualTo(Phase.Active));
            foreach (var m in new[] { "cabinet", "globe", "clock", "safe", "door" })
            {
                Assert.That(w.PhaseOf(Lab.Mechanism(m)), Is.EqualTo(Phase.Locked), m);
            }

            Assert.That(w.Inventory, Is.Empty);
            Assert.That(w.Held, Is.EqualTo(-1));
        }

        // ---- 題材固有：未解除ロックを操作で迂回できない（個別の負例。全ての到達状態は StateSpaceTests） ----

        [Test]
        public void Bypass_KeyholeWithoutKey_IsLocked()
        {
            var w = Lab.NewWorld();
            Lab.GoTo(w, Lab.View("cabinet"));
            AssertRejected(w, Command.Operate(Lab.Part("cabinet.keyhole")), Refusal.Locked);
            AssertRejected(w, Command.Take(Lab.Part("cabinet.disc")), Refusal.NotHere);
        }

        [Test]
        public void Bypass_SafeGemsWithoutHandle_AreLocked()
        {
            var w = Lab.NewWorld();
            Lab.GoTo(w, Lab.View("safe"));
            for (int i = 0; i < 4; i++) { AssertRejected(w, Command.Operate(Lab.Part($"safe.gem{i}")), Refusal.Locked); }
            AssertRejected(w, Command.Operate(Lab.Part("safe.socket")), Refusal.Locked);
            AssertRejected(w, Command.Take(Lab.Part("safe.key")), Refusal.NotHere);
        }

        [Test]
        public void Bypass_ClockHandsWithoutGear_AreLocked()
        {
            var w = Lab.NewWorld();
            Lab.GoTo(w, Lab.View("clock"));
            AssertRejected(w, Command.Operate(Lab.Part("clock.hour")), Refusal.Locked);
            AssertRejected(w, Command.Operate(Lab.Part("clock.minute")), Refusal.Locked);
            AssertRejected(w, Command.Take(Lab.Part("clock.handle")), Refusal.NotHere);
        }

        [Test]
        public void Bypass_GlobeRingsWithoutDisc_AreLocked()
        {
            var w = Lab.NewWorld();
            Lab.GoTo(w, Lab.View("globe"));
            for (int i = 0; i < 3; i++) { AssertRejected(w, Command.Operate(Lab.Part($"globe.ring{i}")), Refusal.Locked); }
            AssertRejected(w, Command.Take(Lab.Part("globe.gear")), Refusal.NotHere);
            AssertRejected(w, Command.Inspect(Lab.Part("globe.disc")), Refusal.NotHere);
        }

        [Test]
        public void Bypass_KeyInClosedDrawer_ClickHitsFrontPlate()
        {
            var w = Lab.NewWorld();
            Lab.GoTo(w, Lab.View("desk"));
            var key = Lab.Study.Parts[Lab.Part("desk.key")];
            Assert.That(w.Def.Views[w.View].Pose.TryProject(key.Box.Center, out var sx, out var sy), Is.True);
            Assert.That(w.HitTest(sx, sy), Is.EqualTo(Lab.Part("desk.front")), "閉じた前板が中の鍵を遮る");
            Assert.That(w.CommandAt(sx, sy, -1), Is.EqualTo(Command.None), "前板は操作がない");
            AssertRejected(w, Command.Take(key.Index), Refusal.NotHere);

            // 解くと前板が倒れ、同じ点で鍵に当たる。
            Lab.AdvanceUntil(w, x => x.IsSolved(Lab.Mechanism("dial")));
            Lab.GoTo(w, Lab.View("desk"));
            Assert.That(w.HitTest(sx, sy), Is.EqualTo(key.Index));
            Assert.That(w.CommandAt(sx, sy, -1), Is.EqualTo(Command.Take(key.Index)));
        }

        [Test]
        public void Bypass_ExitDoorWithoutKey_AndWrongItem_AreRejected()
        {
            var w = Lab.NewWorld();
            Lab.GoTo(w, Lab.View("door"));
            var keyhole = Lab.Part("door.keyhole");
            AssertRejected(w, Command.Operate(keyhole), Refusal.Locked);
            Lab.AdvanceUntil(w, x => x.Has(Lab.Item("brass_key")));
            Lab.GoTo(w, Lab.View("door"));
            AssertRejected(w, Command.UseItem(Lab.Item("brass_key"), keyhole), Refusal.WrongItem);
            AssertRejected(w, Command.UseItem(Lab.Item("exit_key"), keyhole), Refusal.NotHeld);
            Assert.That(w.Escaped, Is.False);
        }

        [Test]
        public void Bypass_PartsOfAnotherViewOrFromWall_AreRejected()
        {
            var w = Lab.NewWorld();
            AssertRejected(w, Command.Operate(Lab.Part("desk.dial0")), Refusal.NotInView);
            Lab.GoTo(w, Lab.View("safe"));
            AssertRejected(w, Command.Operate(Lab.Part("desk.dial0")), Refusal.NotInView);
            AssertRejected(w, Command.Focus(Lab.View("desk")), Refusal.NotOnWall);
        }

        // ---- 操作の受け付け ----

        [Test]
        public void Navigation_TurnFocusBack()
        {
            var w = Lab.NewWorld();
            AssertRejected(w, Command.Back, Refusal.NothingToBack);
            AssertRejected(w, Command.Focus(Lab.View("globe")), Refusal.NotOnWall);
            AssertRejected(w, Command.Focus(Lab.View("north")), Refusal.NotOnWall);
            Lab.Do(w, Command.Turn(-1));
            Assert.That(w.View, Is.EqualTo(Lab.View("west")));
            Lab.Do(w, Command.Turn(1));
            Lab.Do(w, Command.Turn(1));
            Assert.That(w.View, Is.EqualTo(Lab.View("east")));
            Lab.Do(w, Command.Focus(Lab.View("bookshelf")));
            Assert.That(w.Events.Single().Kind, Is.EqualTo(EventKind.Focused));
            AssertRejected(w, Command.Turn(1), Refusal.NotOnWall);
            Lab.Do(w, Command.Back);
            Assert.That(w.View, Is.EqualTo(Lab.View("east")));
            Assert.That(w.Actions, Is.Zero, "視点の移動は操作の回数に数えない");
        }

        [Test]
        public void Inspect_RotateUntilClueFaces_ThenPutDown()
        {
            var w = Lab.NewWorld();
            Lab.GoTo(w, Lab.View("bookshelf"));
            Lab.Do(w, Command.Inspect(Lab.Part("shelf.astronomy")));
            Assert.That(w.Held, Is.EqualTo(0 + Lab.Study.Examinables.First(e => e.Id == "book").Index));
            Assert.That(w.ClueFacing, Is.False);
            AssertRejected(w, Command.Turn(1), Refusal.Holding);
            AssertRejected(w, Command.Inspect(Lab.Part("shelf.astronomy")), Refusal.Holding);
            for (int i = 0; i < 3; i++)
            {
                Lab.Do(w, Command.Rotate(1, 0));
                Assert.That(w.ClueFacing, Is.False);
            }

            Lab.Do(w, Command.Rotate(1, 0));
            Assert.That(w.ClueFacing, Is.True, "180° 回すと裏表紙が見える");
            Assert.That(w.Events.Any(e => e.Kind == EventKind.ClueShown), Is.True);
            Lab.Do(w, Command.Back);
            Assert.That(w.Held, Is.EqualTo(-1));
            Assert.That(w.View, Is.EqualTo(Lab.View("bookshelf")), "置くだけで近景のまま");
            AssertRejected(w, Command.Rotate(1, 0), Refusal.NotHolding);
            Assert.That(w.Actions, Is.EqualTo(1), "手に取るのは数え、回す・置くは数えない");
        }

        [Test]
        public void Inspect_UpsideDownClue_IsNotReadable()
        {
            var w = Lab.NewWorld();
            Lab.GoTo(w, Lab.View("bookshelf"));
            Lab.Do(w, Command.Inspect(Lab.Part("shelf.astronomy")));
            Lab.Do(w, Command.Rotate(0, 1));
            Lab.Do(w, Command.Rotate(0, 1));
            Assert.That(w.Orientation.Faces(Lab.Study.Examinables[w.Held].ClueFace), Is.True, "裏表紙は手前を向いている");
            Assert.That(w.ClueFacing, Is.False, "上下が逆さの文字盤は読めない（時刻を逆に読み違えないように）");
            Assert.That(w.Events.Any(e => e.Kind == EventKind.ClueShown), Is.False);
        }

        [Test]
        public void Negative_OutOfRangeCommands()
        {
            var w = Lab.NewWorld();
            var parts = Lab.Study.Parts.Count;
            AssertRejected(w, Command.Operate(-1), Refusal.OutOfRange);
            AssertRejected(w, Command.Operate(parts), Refusal.OutOfRange);
            AssertRejected(w, Command.Take(parts), Refusal.OutOfRange);
            AssertRejected(w, Command.UseItem(Lab.Study.Items.Count, 0), Refusal.OutOfRange);
            AssertRejected(w, Command.UseItem(0, -2), Refusal.OutOfRange);
            AssertRejected(w, Command.InspectItem(-1), Refusal.OutOfRange);
            AssertRejected(w, Command.Focus(Lab.Study.Views.Count), Refusal.OutOfRange);
            AssertRejected(w, Command.Turn(0), Refusal.OutOfRange);
            AssertRejected(w, Command.Turn(2), Refusal.OutOfRange);
            AssertRejected(w, Command.Rotate(0, 0), Refusal.OutOfRange);
            AssertRejected(w, Command.Rotate(2, 0), Refusal.OutOfRange);
            AssertRejected(w, Command.None, Refusal.NotAllowed);
        }

        [Test]
        public void Negative_DuplicateTakeAndUse_AreRejected()
        {
            var w = Lab.NewWorld();
            Lab.AdvanceUntil(w, x => x.Has(Lab.Item("brass_key")));
            AssertRejected(w, Command.Take(Lab.Part("desk.key")), Refusal.NotHere);
            Lab.Advance(w);
            Assert.That(w.IsSolved(Lab.Mechanism("cabinet")), Is.True);
            Assert.That(w.Has(Lab.Item("brass_key")), Is.False, "差した鍵は手放す");
            AssertRejected(w, Command.UseItem(Lab.Item("brass_key"), Lab.Part("cabinet.keyhole")), Refusal.NotHeld);
            AssertRejected(w, Command.Operate(Lab.Part("cabinet.keyhole")), Refusal.AlreadySolved);
            Lab.GoTo(w, Lab.View("desk"));
            AssertRejected(w, Command.Operate(Lab.Part("desk.dial0")), Refusal.AlreadySolved);
        }

        [Test]
        public void Install_ThenOperate_ThenSolvedIsFinal()
        {
            var w = Lab.NewWorld();
            Lab.AdvanceUntil(w, x => x.Has(Lab.Item("gear")));
            var clock = Lab.Mechanism("clock");
            Lab.GoTo(w, Lab.View("clock"));
            AssertRejected(w, Command.UseItem(Lab.Item("gear"), Lab.Part("clock.hour")), Refusal.WrongItem);
            Lab.Do(w, Command.UseItem(Lab.Item("gear"), Lab.Part("clock.axle")));
            Assert.That(w.PhaseOf(clock), Is.EqualTo(Phase.Active));
            Assert.That(w.Events.Select(e => e.Kind), Is.EqualTo(_installEvents));
            AssertRejected(w, Command.Operate(Lab.Part("clock.axle")), Refusal.NotAllowed);
            Lab.Do(w, Command.Operate(Lab.Part("clock.hour")));
            Assert.That(w.ValueOf(clock, 0), Is.EqualTo(1));
            Lab.Advance(w);
            Assert.That(w.PhaseOf(clock), Is.EqualTo(Phase.Solved));
            Assert.That(w.Events[w.Events.Count - 1].Kind, Is.EqualTo(EventKind.Solved));
            AssertRejected(w, Command.Operate(Lab.Part("clock.hour")), Refusal.AlreadySolved);
        }

        [Test]
        public void Safe_WrongGem_RecordsMistake()
        {
            var w = Lab.NewWorld();
            Lab.AdvanceUntil(w, x => Lab.Active(x, "safe"));
            var safe = Lab.Mechanism("safe");
            var answer = w.Room.Mechanisms[safe].Answer;
            Lab.GoTo(w, Lab.View("safe"));
            Lab.Do(w, Command.Operate(Lab.Part($"safe.gem{answer[0]}")));
            Lab.Do(w, Command.Operate(Lab.Part($"safe.gem{answer[2]}")));
            Assert.That(w.Events.Any(e => e.Kind == EventKind.Mistake), Is.True);
            Assert.That(w.ValueOf(safe, 0), Is.Zero);
        }

        // ---- 完了判定：5 つの仕掛けを解いて脱出・リセットで初期状態へ ----

        [Test]
        public void Acceptance_SolveFiveMechanisms_AndEscape([Values(1, 2, 3, 77, 2026)] int seed)
        {
            var w = Lab.NewWorld(seed);
            Lab.Escape(w);
            Assert.That(w.Escaped, Is.True);
            Assert.That(w.SolvedCount, Is.EqualTo(6), "5 つの仕掛けと出口の扉");
            Assert.That(w.Events[w.Events.Count - 1].Kind, Is.EqualTo(EventKind.Escaped));
            AssertRejected(w, Command.Hint, Refusal.Escaped);
            AssertRejected(w, Command.Back, Refusal.Escaped);
            AssertRejected(w, Command.Turn(1), Refusal.Escaped);
            Assert.That(w.NextStep(), Is.Null);
        }

        [Test]
        public void Acceptance_ResetRestoresInitialState([Values(0, 3, 7, 12)] int steps)
        {
            var w = Lab.NewWorld(9);
            for (int i = 0; i < steps; i++) { Lab.Advance(w); }
            Lab.Do(w, Command.Hint);
            Lab.Do(w, Command.Hint);
            Lab.GoTo(w, Lab.View("bookshelf"));
            Lab.Do(w, Command.Inspect(Lab.Part("shelf.astronomy")));
            Lab.Do(w, Command.Rotate(1, 1));
            Lab.Do(w, Command.Reset);
            Assert.That(w.Events.Single().Kind, Is.EqualTo(EventKind.Reset));
            var fresh = Lab.NewWorld(9);
            Assert.That(w.Hash(), Is.EqualTo(fresh.Hash()), "視点・持ち物・仕掛け・ヒントの段階・操作の回数まで最初に戻る");
            Assert.That(w.Inventory, Is.Empty);
            Assert.That(w.HintsUsed, Is.Zero);
            Assert.That(w.View, Is.EqualTo(fresh.View));
        }

        [Test]
        public void Reset_IsAcceptedEvenAfterEscape()
        {
            var w = Lab.NewWorld(4);
            Lab.Escape(w);
            Lab.Do(w, Command.Reset);
            Assert.That(w.Hash(), Is.EqualTo(Lab.NewWorld(4).Hash()));
        }

        // ---- ヒントの段階 ----

        [Test]
        public void Hint_LevelsAdvanceForSameStep_AndRestartWhenStepChanges()
        {
            var w = Lab.NewWorld(6);
            var dial = w.Room.Mechanisms[Lab.Mechanism("dial")];
            var levels = new int[4];
            for (int i = 0; i < 4; i++)
            {
                Lab.Do(w, Command.Hint);
                levels[i] = w.Events.Single().Index;
            }

            Assert.That(levels, Is.EqualTo(_hintLevels));
            Assert.That(w.Events.Single().Text.Contains(dial.AnswerText, StringComparison.Ordinal), Is.True, "答えの段階は今のシードの答え");
            Assert.That(w.HintsUsed, Is.EqualTo(4));
            Assert.That(w.Actions, Is.Zero, "ヒントは操作の回数に数えない");
            Lab.Advance(w);
            Lab.Do(w, Command.Hint);
            Assert.That(w.Events.Single().Index, Is.Zero, "次の一手が変わったら方向付けから");
            Assert.That(w.NextStep()!.Value.Kind, Is.EqualTo(HintKind.Take));
        }

        /// <summary>リングの答えの段階は、今の位置からのクリックの回数を添える（星座の名前は画面に出ないため）。</summary>
        [Test]
        public void Hint_RingAnswer_TellsClicksFromCurrentPosition()
        {
            var w = Lab.NewWorld(7);
            Lab.AdvanceUntil(w, x => Lab.Active(x, "globe"));
            var globe = Lab.Mechanism("globe");
            Lab.GoTo(w, Lab.View("globe"));
            Lab.Do(w, Command.Operate(Lab.Part("globe.ring0")));
            for (int i = 0; i < 3; i++) { Lab.Do(w, Command.Hint); }
            var text = w.Events.Single().Text;
            var answer = w.Room.Mechanisms[globe].Answer;
            for (int ring = 0; ring < 3; ring++)
            {
                var name = Lab.Study.Parts[Lab.Part($"globe.ring{ring}")].Name;
                var n = (((answer[ring] - w.ValueOf(globe, ring)) % 8) + 8) % 8;
                var expected = n == 0 ? $"{name}はそのまま" : $"{name}をあと {n} 回";
                Assert.That(text.Contains(expected, StringComparison.Ordinal), Is.True, $"{expected} / {text}");
            }
        }

        [Test]
        public void Hint_AnswerText_ChangesWithSeed()
        {
            string Answer(int seed)
            {
                var w = Lab.NewWorld(seed);
                for (int i = 0; i < 3; i++) { Lab.Do(w, Command.Hint); }
                return w.Events.Single().Text;
            }

            Assert.That(Answer(1), Is.Not.EqualTo(Answer(2)));
        }

        // ---- クリックとヒットテスト ----

        [Test]
        public void CommandAt_MapsClicksToCommands()
        {
            var w = Lab.NewWorld();
            var dial0 = Lab.Study.Parts[Lab.Part("desk.dial0")];
            var wall = w.Def.Views[w.View].Pose;
            Assert.That(wall.TryProject(dial0.Box.Center, out var wx, out var wy), Is.True);
            Assert.That(w.CommandAt(wx, wy, -1), Is.EqualTo(Command.Focus(Lab.View("desk"))), "壁の視点では近景へ寄る");
            Lab.GoTo(w, Lab.View("desk"));
            var pose = w.Def.Views[w.View].Pose;
            Assert.That(pose.TryProject(dial0.Box.Center, out var sx, out var sy), Is.True);
            Assert.That(w.CommandAt(sx, sy, -1), Is.EqualTo(Command.Operate(dial0.Index)));
            Assert.That(w.CommandAt(sx, sy, 2), Is.EqualTo(Command.UseItem(2, dial0.Index)), "持ち物を選んでいれば使う");
            var cyl = Lab.Study.Parts[Lab.Part("desk.cylinder")];
            Assert.That(pose.TryProject(cyl.Box.Center, out sx, out sy), Is.True);
            Assert.That(w.CommandAt(sx, sy, -1), Is.EqualTo(Command.Inspect(cyl.Index)));
            Assert.That(w.CommandAt(0, CameraPose.HalfHeight + 1, -1), Is.EqualTo(Command.None), "画面の外");
            Lab.Do(w, Command.Inspect(cyl.Index));
            Assert.That(w.CommandAt(sx, sy, -1), Is.EqualTo(Command.None), "手に取っている間はクリックしない");
        }

        [Test]
        public void EveryPart_IsHitAtItsFrontCenter_InItsCloseUp_WhenReachable()
        {
            // 全ての部品が、触れられる状態のどこかで、近景で手前の面の中心をクリックすると当たる（部屋の配置の検証）。
            var report = Analysis.StateSpace.Explore(new Room(Lab.Study, 1), 10000);
            var hitOnce = new bool[Lab.Study.Parts.Count];
            var blockers = new string[Lab.Study.Parts.Count];
            foreach (var rep in report.Worlds)
            {
                if (rep.Escaped) { continue; }
                foreach (var p in Lab.Study.Parts)
                {
                    if (p.Kind == PartKind.Body || !rep.IsReachable(p.Index)) { continue; }
                    var w = rep.Clone();
                    Lab.GoTo(w, p.View);
                    var pose = w.Def.Views[p.View].Pose;
                    Assert.That(pose.TryProject(p.Box.FrontCenter(pose.Wall), out var sx, out var sy), Is.True, p.Id);
                    var hit = w.HitTest(sx, sy);
                    if (hit == p.Index) { hitOnce[p.Index] = true; }
                    else { blockers[p.Index] = hit < 0 ? "なし" : Lab.Study.Parts[hit].Id; }
                }
            }

            var missing = Lab.Study.Parts.Where(p => p.Kind != PartKind.Body && !hitOnce[p.Index]).Select(p => $"{p.Id}（{blockers[p.Index]} に当たる）").ToList();
            Assert.That(missing, Is.Empty, "中心をクリックしても当たらない部品");
        }

        [Test]
        public void HitTest_NoUnreachablePartIsEverClicked_AndWallClicksStayOnWall()
        {
            // 到達しうる全ての状態・全ての視点で画面を格子に刻み、当たる部品が「触れられる」こと（遮りが効くこと）と、
            // 壁の視点で当たる部品の近景がその壁にあることを確かめる。
            var report = Analysis.StateSpace.Explore(new Room(Lab.Study, 1), 10000);
            const int step = 60;
            int clicks = 0;
            foreach (var rep in report.Worlds)
            {
                if (rep.Escaped) { continue; }
                foreach (var view in Lab.Study.Views)
                {
                    var w = rep.Clone();
                    Lab.GoTo(w, view.Index);
                    for (int sy = -CameraPose.HalfHeight; sy <= CameraPose.HalfHeight; sy += step)
                    {
                        for (int sx = -1800; sx <= 1800; sx += step)
                        {
                            var hit = w.HitTest(sx, sy);
                            if (hit < 0) { continue; }
                            clicks++;
                            var part = Lab.Study.Parts[hit];
                            Assert.That(w.IsPresent(hit), Is.True);
                            if (view.Kind == ViewKind.Wall)
                            {
                                // 壁の視点で当たる部品は、その壁の近景へ寄る命令になるか、ほかの壁の物なら何もしない。
                                var c = w.CommandAt(sx, sy, -1);
                                if (Lab.Study.Views[part.View].Wall == view.Wall) { Assert.That(w.Check(c).Accepted, Is.True, $"{view.Id} で {part.Id}"); }
                                else { Assert.That(c.IsNone, Is.True); }
                            }
                            else if (part.View == view.Index && part.Kind != PartKind.Body)
                            {
                                Assert.That(w.IsReachable(hit), Is.True, $"{view.Id} で触れられない {part.Id} に当たりました（{sx}, {sy}）");
                                var c = w.CommandAt(sx, sy, -1);
                                Assert.That(c.IsNone, Is.False);
                            }
                        }
                    }
                }
            }

            Assert.That(clicks, Is.GreaterThan(10000));
        }
    }
}
