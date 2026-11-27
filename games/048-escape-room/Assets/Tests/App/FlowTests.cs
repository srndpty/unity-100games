using System;
using System.Linq;
using Game.Core.Geometry;
using Game.Core.Mechanisms;
using Game.Core.Play;
using Game.Core.Rooms;
using NUnit.Framework;

namespace Game.App.Tests
{
    public sealed class FlowTests
    {
        private static RoomDef? _study;

        private static RoomDef Study => _study ??= StudyRoom.Create();

        private static int Part(string id) => Study.Parts.First(p => p.Id == id).Index;

        private static int View(string id) => Study.Views.First(v => v.Id == id).Index;

        private static int Mechanism(string id) => Study.Mechanisms.First(m => m.Id == id).Index;

        private static EscapeFlow NewGame(int seed = 1, FlowSettings? settings = null)
        {
            var flow = new EscapeFlow(Study, seed, settings);
            flow.Press(IntentKind.Confirm);
            flow.FlushIntents();
            Assert.That(flow.Screen, Is.EqualTo(FlowScreen.Playing));
            return flow;
        }

        /// <summary>演出が終わるまで Tick を進める。</summary>
        private static void Settle(EscapeFlow flow)
        {
            for (int i = 0; i < 600 && flow.InTransition; i++) { flow.StepTicks(1); }
            Assert.That(flow.InTransition, Is.False);
        }

        /// <summary>操縦役で、条件を満たすまで進める。</summary>
        private static void DriveUntil(EscapeFlow flow, Func<EscapeFlow, bool> done, Autopilot? pilot = null)
        {
            pilot ??= new Autopilot();
            for (int i = 0; i < 20000 && !done(flow); i++)
            {
                pilot.Drive(flow);
                flow.StepTicks(1);
            }

            Assert.That(done(flow), Is.True);
            Settle(flow);
        }

        /// <summary>視点へ移る（向き直りのキーとクリックで）。</summary>
        private static void GoTo(EscapeFlow flow, int view)
        {
            var w = flow.Snapshot()!;
            foreach (var c in Navigation.PathTo(w, view))
            {
                switch (c.Kind)
                {
                    case CommandKind.Turn:
                        flow.Press(IntentKind.Turn, c.A);
                        break;
                    case CommandKind.Back:
                        flow.Press(IntentKind.Back);
                        break;
                    default:
                        var p = ClickSearch.PointFor(flow.Snapshot()!, c, -1, flow.Region)!.Value;
                        flow.ClickWorld(p.X, p.Y);
                        break;
                }

                flow.FlushIntents();
                Settle(flow);
            }

            Assert.That(flow.View, Is.EqualTo(view));
        }

        private static (int X, int Y) PointOf(EscapeFlow flow, Command c, int selected = -1) => ClickSearch.PointFor(flow.Snapshot()!, c, selected, flow.Region)!.Value;

        // ---- 状態遷移：タイトル → 遊ぶ → 結果 → タイトル → 再戦 ----

        [Test]
        public void ScreenFlow_TitleHowToPlayResultTitleRematch()
        {
            var flow = new EscapeFlow(Study, 5);
            flow.Press(IntentKind.Down);
            flow.Press(IntentKind.Confirm);
            flow.FlushIntents();
            Assert.That(flow.Screen, Is.EqualTo(FlowScreen.HowTo));
            flow.Press(IntentKind.Confirm);
            flow.FlushIntents();
            Assert.That(flow.Screen, Is.EqualTo(FlowScreen.Title));
            flow.Press(IntentKind.Confirm);
            flow.FlushIntents();
            Assert.That(flow.Screen, Is.EqualTo(FlowScreen.Playing));
            var firstSeed = flow.Seed;

            new Autopilot().RunToResult(flow);
            Assert.That(flow.Screen, Is.EqualTo(FlowScreen.Result));
            Assert.That(flow.Escaped, Is.True);
            Assert.That(flow.Actions, Is.GreaterThan(0));
            Assert.That(flow.PlaySeconds, Is.GreaterThan(0));

            // 結果の後の入力（部屋のクリック・向き直り・ヒント）は無視する。
            var hash = flow.WorldHash;
            flow.ClickWorld(0, 0);
            flow.Press(IntentKind.Turn, 1);
            flow.Press(IntentKind.Hint);
            flow.Press(IntentKind.Back);
            flow.StepTicks(30);
            Assert.That(flow.Screen, Is.EqualTo(FlowScreen.Result));
            Assert.That(flow.WorldHash, Is.EqualTo(hash));
            var seconds = flow.PlaySeconds;
            Assert.That(flow.PlaySeconds, Is.EqualTo(seconds), "結果の間は時計が進まない");

            flow.Press(IntentKind.Confirm);
            flow.FlushIntents();
            Assert.That(flow.Screen, Is.EqualTo(FlowScreen.Title));
            flow.Press(IntentKind.Confirm);
            flow.FlushIntents();
            Assert.That(flow.Screen, Is.EqualTo(FlowScreen.Playing));
            Assert.That(flow.Seed, Is.Not.EqualTo(firstSeed), "再戦は新しいシード");
            Assert.That(flow.Actions, Is.Zero);
            Assert.That(flow.PlayTicks, Is.Zero);
        }

        [Test]
        public void Autopilot_EscapesInManySeeds_ByClicksAndKeysOnly([Values(1, 2, 3, 4, 5, 6, 7, 8)] int seed)
        {
            var flow = NewGame(seed);
            var pilot = new Autopilot();
            pilot.RunToResult(flow);
            Assert.That(flow.SolvedCount, Is.EqualTo(Study.Mechanisms.Count));
            Assert.That(flow.HintsUsed, Is.Zero);
        }

        [Test]
        public void Autopilot_FinalHash_IsFixed()
        {
            var flow = NewGame(1);
            new Autopilot().RunToResult(flow);
            TestContext.WriteLine($"シード {flow.Seed}・操作 {flow.Actions}・Tick {flow.PlayTicks}・ハッシュ 0x{flow.WorldHash:X8}");
            Assert.That(flow.WorldHash, Is.EqualTo(0x8503582Eu), "整数だけの規則なので、.NET と Mono で同じ値になる");
        }

        // ---- 完了判定：リセットで初期状態へ ----

        [Test]
        public void Reset_FromPause_RestoresInitialState()
        {
            var flow = NewGame(3);
            var fresh = NewGame(3);
            DriveUntil(flow, f => f.PhaseOf(Mechanism("globe")) == Phase.Active);
            flow.Press(IntentKind.Hint);
            flow.FlushIntents();
            flow.StepTicks(100);
            flow.Press(IntentKind.Pause);
            flow.FlushIntents();
            if (flow.Screen != FlowScreen.Paused)
            {
                // 近景にいたら Esc は一段戻る。壁まで戻ってから一時停止する。
                Settle(flow);
                flow.Press(IntentKind.Pause);
                flow.FlushIntents();
            }

            Assert.That(flow.Screen, Is.EqualTo(FlowScreen.Paused));
            flow.Press(IntentKind.Reset);
            flow.FlushIntents();
            Assert.That(flow.Screen, Is.EqualTo(FlowScreen.Playing));
            Assert.That(flow.WorldHash, Is.EqualTo(fresh.WorldHash), "仕掛け・持ち物・視点・ヒントの段階・操作の回数が始めに戻る");
            Assert.That(flow.PlayTicks, Is.Zero, "経過時間も戻る");
            Assert.That(flow.HintText, Is.Empty);
            Assert.That(flow.SelectedItem, Is.EqualTo(-1));
            Assert.That(flow.Seed, Is.EqualTo(fresh.Seed), "リセットは同じシード");
        }

        // ---- 題材固有：同時操作で状態破綻なし（同じフレームの意図を 1 本の列で順に当てはめる） ----

        [Test]
        public void SameFrame_TwoPartClicks_ApplyInOrder()
        {
            var flow = NewGame();
            GoTo(flow, View("desk"));
            var a = PointOf(flow, Command.Operate(Part("desk.dial0")));
            var b = PointOf(flow, Command.Operate(Part("desk.dial2")));
            flow.ClickWorld(a.X, a.Y);
            flow.ClickWorld(b.X, b.Y);
            flow.ClickWorld(a.X, a.Y);
            flow.FlushIntents();
            var dial = Mechanism("dial");
            Assert.That(flow.ValueOf(dial, 0), Is.EqualTo(2), "ダブルクリックは 2 回の操作");
            Assert.That(flow.ValueOf(dial, 1), Is.Zero);
            Assert.That(flow.ValueOf(dial, 2), Is.EqualTo(1));
            Assert.That(flow.Actions, Is.EqualTo(3));
        }

        [Test]
        public void SameFrame_TurnThenClick_DropsClickDuringCameraMove()
        {
            var flow = NewGame();
            var p = PointOf(flow, Command.Focus(View("desk")));
            flow.Press(IntentKind.Turn, 1);
            flow.ClickWorld(p.X, p.Y);
            flow.FlushIntents();
            Assert.That(flow.View, Is.EqualTo(View("east")), "向き直りだけが効き、北の壁で押したクリックは捨てる");
            Assert.That(flow.InTransition, Is.True);

            // 演出の間のクリック（今の版で押しても）も捨てる。
            var shelf = PointOf(flow, Command.Focus(View("bookshelf")));
            flow.ClickWorld(shelf.X, shelf.Y);
            flow.Press(IntentKind.Turn, 1);
            flow.FlushIntents();
            Assert.That(flow.View, Is.EqualTo(View("east")));
            Settle(flow);
            flow.ClickWorld(shelf.X, shelf.Y);
            flow.FlushIntents();
            Assert.That(flow.View, Is.EqualTo(View("bookshelf")), "演出が終われば受け付ける");
        }

        [Test]
        public void SameFrame_DoubleClickOnFurniture_FocusesOnce()
        {
            var flow = NewGame();
            var p = PointOf(flow, Command.Focus(View("desk")));
            flow.ClickWorld(p.X, p.Y);
            flow.ClickWorld(p.X, p.Y);
            flow.FlushIntents();
            Settle(flow);
            Assert.That(flow.View, Is.EqualTo(View("desk")));
            Assert.That(flow.Actions, Is.Zero, "2 回目は壁の視点で押したクリックなので、近景の部品を操作しない");
            Assert.That(flow.ValueOf(Mechanism("dial"), 0) + flow.ValueOf(Mechanism("dial"), 1) + flow.ValueOf(Mechanism("dial"), 2), Is.Zero);
        }

        [Test]
        public void SameFrame_HintAndClick_BothApply()
        {
            var flow = NewGame();
            GoTo(flow, View("desk"));
            var a = PointOf(flow, Command.Operate(Part("desk.dial1")));
            flow.Press(IntentKind.Hint);
            flow.ClickWorld(a.X, a.Y);
            flow.Press(IntentKind.Hint);
            flow.FlushIntents();
            Assert.That(flow.ValueOf(Mechanism("dial"), 1), Is.EqualTo(1));
            Assert.That(flow.HintsUsed, Is.EqualTo(2));
            Assert.That(flow.HintLevel, Is.EqualTo(1), "同じ一手なので 2 回目は手がかりの段階");
        }

        [Test]
        public void SameFrame_ResetAndClick_DropsClickPressedBeforeReset()
        {
            var flow = NewGame(2);
            var fresh = NewGame(2);
            var p = PointOf(flow, Command.Focus(View("desk")));
            flow.Press(IntentKind.Pause);
            flow.FlushIntents();
            Assert.That(flow.Screen, Is.EqualTo(FlowScreen.Paused));
            var stale = flow.Version;
            flow.Press(IntentKind.Reset);
            flow.Enqueue(Intent.ClickWorld(p.X, p.Y, stale));
            flow.FlushIntents();
            Assert.That(flow.Screen, Is.EqualTo(FlowScreen.Playing));
            Assert.That(flow.WorldHash, Is.EqualTo(fresh.WorldHash), "リセット前の画面で押したクリックは効かない");
        }

        [Test]
        public void SameFrame_UseItemThenSecondClick_DoesNotUseTwice()
        {
            var flow = NewGame(4);
            DriveUntil(flow, f => f.Inventory.Count == 1);
            GoTo(flow, View("cabinet"));
            var keyhole = Part("cabinet.keyhole");
            var key = flow.Inventory[0];
            flow.Click(ClickTarget.Item, 0);
            var p = PointOf(flow, Command.UseItem(key, keyhole), key);
            flow.ClickWorld(p.X, p.Y);
            flow.ClickWorld(p.X, p.Y);
            flow.FlushIntents();
            Assert.That(flow.PhaseOf(Mechanism("cabinet")), Is.EqualTo(Phase.Solved));
            Assert.That(flow.Inventory, Is.Empty);
            Assert.That(flow.SelectedItem, Is.EqualTo(-1), "使って手放した持ち物の選択は外れる");
            Assert.That(flow.LastOutcome.Reason, Is.EqualTo(Refusal.AlreadySolved), "2 回目は選択のない「触る」になり、表のとおり拒否");
        }

        // ---- 入力の受け付け ----

        [Test]
        public void Esc_GoesBackOneStep_AndPausesOnlyOnWall()
        {
            var flow = NewGame();
            GoTo(flow, View("desk"));
            flow.Press(IntentKind.Pause);
            flow.FlushIntents();
            Assert.That(flow.Screen, Is.EqualTo(FlowScreen.Playing));
            Assert.That(flow.View, Is.EqualTo(View("north")));
            Settle(flow);
            flow.Press(IntentKind.Pause);
            flow.FlushIntents();
            Assert.That(flow.Screen, Is.EqualTo(FlowScreen.Paused));

            // 一時停止の間は時計が止まる。
            var ticks = flow.PlayTicks;
            flow.StepTicks(120);
            Assert.That(flow.PlayTicks, Is.EqualTo(ticks));
            flow.Press(IntentKind.Pause);
            flow.FlushIntents();
            flow.StepTicks(10);
            Assert.That(flow.PlayTicks, Is.EqualTo(ticks + 10));
        }

        [Test]
        public void RightClick_DeselectsThenPutsDownThenLeavesCloseUp()
        {
            var flow = NewGame(8);
            DriveUntil(flow, f => f.Inventory.Count == 1 && f.PhaseOf(Mechanism("cabinet")) == Phase.Solved);
            DriveUntil(flow, f => f.Inventory.Count == 1 && Study.Items[f.Inventory[0]].Id == "star_disc");
            flow.Click(ClickTarget.Item, 0);
            flow.FlushIntents();
            Assert.That(flow.SelectedItem, Is.EqualTo(flow.Inventory[0]));
            flow.Click(ClickTarget.Item, 0);
            flow.FlushIntents();
            Assert.That(flow.Held, Is.GreaterThanOrEqualTo(0), "選んだ枠をもう一度で手に取る");
            Assert.That(flow.SelectedItem, Is.EqualTo(-1));
            flow.Click(ClickTarget.Item, 0);
            flow.FlushIntents();
            Assert.That(flow.SelectedItem, Is.EqualTo(-1), "手に取っている間は選ばない");
            flow.Press(IntentKind.Back);
            flow.FlushIntents();
            Assert.That(flow.Held, Is.EqualTo(-1));
            Assert.That(flow.View, Is.EqualTo(View("cabinet")));
            flow.Click(ClickTarget.Item, 0);
            flow.Press(IntentKind.Back);
            flow.FlushIntents();
            Assert.That(flow.SelectedItem, Is.EqualTo(-1));
            Assert.That(flow.View, Is.EqualTo(View("cabinet")), "選択を外すのが先");
            flow.Press(IntentKind.Back);
            flow.FlushIntents();
            Assert.That(flow.View, Is.EqualTo(View("east")));
        }

        [Test]
        public void Drag_RotatesHeldObject_UntilClueFaces()
        {
            var flow = NewGame();
            GoTo(flow, View("desk"));
            var p = PointOf(flow, Command.Inspect(Part("desk.cylinder")));
            flow.ClickWorld(p.X, p.Y);
            flow.FlushIntents();
            Assert.That(flow.Held, Is.GreaterThanOrEqualTo(0));
            Assert.That(flow.ClueFacing, Is.False);
            flow.ClickWorld(0, 0);
            flow.Drag(0, 100);
            flow.FlushIntents();
            Assert.That(flow.ClueFacing, Is.False, "1 段に足りないドラッグでは回らない");
            flow.Drag(0, 100);
            flow.FlushIntents();
            Assert.That(flow.ClueFacing, Is.True, "上へドラッグすると底がこちらを向く");
            Assert.That(flow.Cues.Clue, Is.EqualTo(1));
            flow.Drag(-1000, 0);
            flow.FlushIntents();
            Assert.That(flow.Orientation.Yaw, Is.EqualTo(5), "左へ 1000 は 5 段（ヨーは正）");
            Assert.That(flow.ClueFacing, Is.False, "底は手前を向いたままでも、斜めに回すと正立していないので読めない");
        }

        [Test]
        public void Refusals_ShowMessage_AndKeepState()
        {
            var flow = NewGame();
            GoTo(flow, View("cabinet"));
            var hash = flow.WorldHash;
            var p = PointOf(flow, Command.Operate(Part("cabinet.keyhole")));
            flow.ClickWorld(p.X, p.Y);
            flow.FlushIntents();
            Assert.That(flow.LastOutcome.Reason, Is.EqualTo(Refusal.Locked));
            Assert.That(flow.Message, Is.EqualTo("鍵がかかっている。"));
            Assert.That(flow.Cues.Reject, Is.EqualTo(1));
            Assert.That(flow.WorldHash, Is.EqualTo(hash));
            flow.StepTicks(new FlowSettings().MessageTicks);
            Assert.That(flow.Message, Is.Empty, "知らせは時間で消える");
        }

        [Test]
        public void FocusLoss_DropsPendingAndLaterIntents()
        {
            var flow = NewGame();
            var p = PointOf(flow, Command.Focus(View("desk")));
            flow.ClickWorld(p.X, p.Y);
            flow.OnFocusChanged(false);
            flow.Press(IntentKind.Turn, 1);
            flow.FlushIntents();
            Assert.That(flow.View, Is.EqualTo(View("north")), "喪失の前の保留も、喪失の後に届いた入力も当てはめない");
            flow.OnFocusChanged(true);
            flow.Press(IntentKind.Turn, 1);
            flow.FlushIntents();
            Assert.That(flow.View, Is.EqualTo(View("east")));
        }

        [Test]
        public void Hint_ShowsAnswerOfThisSeed_AtThirdPress()
        {
            var flow = NewGame(11);
            for (int i = 0; i < 3; i++) { flow.Press(IntentKind.Hint); }
            flow.FlushIntents();
            var answer = flow.Room.Mechanisms[Mechanism("dial")].AnswerText;
            Assert.That(flow.HintText.Contains(answer, StringComparison.Ordinal), Is.True, flow.HintText);
            Assert.That(flow.Cues.Hint, Is.EqualTo(3));
            Assert.That(flow.Actions, Is.Zero);
        }

        [Test]
        public void Escape_ShowsResultAfterDelay_WithCounts()
        {
            var settings = new FlowSettings { ResultDelayTicks = 30 };
            var flow = NewGame(6, settings);
            flow.Press(IntentKind.Hint);
            flow.FlushIntents();
            DriveUntil(flow, f => f.Escaped);
            Assert.That(flow.Screen, Is.EqualTo(FlowScreen.Playing), "扉が開く演出の間は結果の前");
            var ticks = flow.PlayTicks;
            flow.StepTicks(30);
            Assert.That(flow.Screen, Is.EqualTo(FlowScreen.Result));
            Assert.That(flow.PlayTicks, Is.EqualTo(ticks), "脱出した後は時計が止まる");
            Assert.That(flow.HintsUsed, Is.EqualTo(1));
        }

        [Test]
        public void Settings_Validation_RejectsOutOfRange()
        {
            Assert.Throws<ArgumentException>(() => _ = new EscapeFlow(Study, 1, new FlowSettings { TickRate = 0 }));
            Assert.Throws<ArgumentException>(() => _ = new EscapeFlow(Study, 1, new FlowSettings { DragStepUnits = 10 }));
            Assert.Throws<ArgumentException>(() => _ = new EscapeFlow(Study, 1, new FlowSettings { TransitionTicks = -1 }));
            Assert.That(new FlowSettings().Validate(), Is.Empty);
        }

        [Test]
        public void ClickSearch_FindsPointInsideGuaranteedRegion_ForEveryReachableCloseUpCommand()
        {
            var flow = NewGame();
            var w = flow.Snapshot()!;
            foreach (var view in Study.Views)
            {
                if (view.Kind != ViewKind.CloseUp) { continue; }
                var at = w.Clone();
                foreach (var c in Navigation.PathTo(at, view.Index)) { at.Apply(c); }
                foreach (var p in Study.Parts)
                {
                    if (p.View != view.Index || p.Kind == PartKind.Body || !at.IsReachable(p.Index)) { continue; }
                    var c = p.Kind switch
                    {
                        PartKind.Examinable => Command.Inspect(p.Index),
                        _ => Command.Operate(p.Index),
                    };
                    var point = ClickSearch.PointFor(at, c, -1, ScreenRegion.Guaranteed);
                    Assert.That(point, Is.Not.Null, p.Id);
                    Assert.That(ScreenRegion.Guaranteed.Contains(point!.Value.X, point.Value.Y), Is.True, "見つけた点は範囲の中");
                }
            }

            Assert.That(ClickSearch.PointFor(w, Command.Operate(Part("desk.dial0")), -1, ScreenRegion.Guaranteed), Is.Null, "壁の視点では部品を操作する点はない");
            Assert.That(CameraPose.HalfHeight, Is.EqualTo(1000));
        }

        // ---- レビュー P2-1：操縦役は命令が実際に実行されたことを確かめる ----

        [Test]
        public void Perform_Throws_WhenInputIsDroppedByFocusLoss()
        {
            var flow = NewGame();
            Autopilot.Perform(flow, Command.Turn(1));
            Assert.That(flow.LastOutcome.Accepted, Is.True, "前の命令は受け付けられている（古い結果が残る）");
            Settle(flow);
            flow.OnFocusChanged(false);
            var e = Assert.Throws<InvalidOperationException>(() => Autopilot.Perform(flow, Command.Turn(1)));
            Assert.That(e!.Message.Contains("実行されませんでした", StringComparison.Ordinal), Is.True, e.Message);
            Assert.That(flow.View, Is.EqualTo(View("east")));
        }

        [Test]
        public void Perform_Throws_WhenClickIsDroppedDuringCameraMove()
        {
            var flow = NewGame();
            Autopilot.Perform(flow, Command.Focus(View("desk")));
            Assert.That(flow.InTransition, Is.True);
            var e = Assert.Throws<InvalidOperationException>(() => Autopilot.Perform(flow, Command.Operate(Part("desk.dial0"))));
            Assert.That(e!.Message.Contains("実行されませんでした", StringComparison.Ordinal), Is.True, e.Message);
        }

        [Test]
        public void Verify_Throws_WhenAnotherCommandWasExecuted()
        {
            var flow = NewGame();
            GoTo(flow, View("desk"));
            var before = flow.Executed;
            var p = PointOf(flow, Command.Operate(Part("desk.dial1")));
            flow.ClickWorld(p.X, p.Y);
            flow.FlushIntents();
            var e = Assert.Throws<InvalidOperationException>(() => Autopilot.Verify(flow, Command.Operate(Part("desk.dial0")), before));
            Assert.That(e!.Message.Contains("として実行されました", StringComparison.Ordinal), Is.True, e.Message);
            Assert.DoesNotThrow(() => Autopilot.Verify(flow, Command.Operate(Part("desk.dial1")), before));
        }

        // ---- レビュー P2-2：持ち物を選んだ途中の状態からでも操縦役で脱出できる ----

        [Test]
        public void Autopilot_FromMidGameWithItemSelected_Escapes([Values(1, 2)] int which)
        {
            var flow = NewGame(12);
            DriveUntil(flow, f => f.PhaseOf(Mechanism("globe")) == Phase.Active);
            DriveUntil(flow, f => f.Inventory.Count == 1);

            // プレイヤーが持ち物（歯車）を選び、近景にいる途中の状態から任せる。
            flow.Click(ClickTarget.Item, 0);
            flow.FlushIntents();
            Assert.That(flow.SelectedItem, Is.GreaterThanOrEqualTo(0));
            if (which == 2)
            {
                flow.Press(IntentKind.Hint);
                flow.FlushIntents();
            }

            new Autopilot().RunToResult(flow);
            Assert.That(flow.Escaped, Is.True);
        }

        [Test]
        public void Perform_DeselectsBeforeBack_SoBackReallyLeavesTheCloseUp()
        {
            var flow = NewGame(4);
            DriveUntil(flow, f => f.Inventory.Count == 1);
            flow.Click(ClickTarget.Item, 0);
            flow.FlushIntents();
            Assert.That(flow.View, Is.EqualTo(View("desk")));
            Autopilot.Perform(flow, Command.Back);
            Assert.That(flow.View, Is.EqualTo(View("north")), "選択を外すだけでは戻ったことにしない");
            Assert.That(flow.SelectedItem, Is.EqualTo(-1));
        }

        // ---- レビュー P2-3：クリックが届く画面の範囲 ----

        [Test]
        public void ClickOutsideRegion_IsNotPassedToTheRoom()
        {
            var flow = NewGame();
            GoTo(flow, View("desk"));
            var dial = PointOf(flow, Command.Operate(Part("desk.dial0")));
            var before = flow.Executed;
            flow.SetRegion(new ScreenRegion(1600, dial.Y + 1));
            flow.ClickWorld(dial.X, dial.Y);
            flow.FlushIntents();
            Assert.That(flow.Executed, Is.EqualTo(before), "HUD の帯の下（範囲の外）のクリックは部屋に届かない");
            flow.SetRegion(new ScreenRegion(Math.Abs(dial.X) - 1, -700));
            flow.ClickWorld(dial.X, dial.Y);
            flow.FlushIntents();
            Assert.That(flow.Executed, Is.EqualTo(before), "画面の横の外のクリックも届かない");
            flow.SetRegion(ScreenRegion.Guaranteed);
            flow.ClickWorld(dial.X, dial.Y);
            flow.FlushIntents();
            Assert.That(flow.Executed, Is.EqualTo(before + 1));
        }

        // ---- レビュー P2-4：ドラッグの寿命 ----

        [Test]
        public void Drag_ResidueIsDiscarded_WhenHeldObjectChanges()
        {
            var flow = NewGame();
            GoTo(flow, View("desk"));
            var p = PointOf(flow, Command.Inspect(Part("desk.cylinder")));
            flow.ClickWorld(p.X, p.Y);
            flow.FlushIntents();
            flow.Drag(0, 100);
            flow.FlushIntents();
            flow.Press(IntentKind.Back);
            flow.FlushIntents();
            Assert.That(flow.Held, Is.EqualTo(-1));
            flow.ClickWorld(p.X, p.Y);
            flow.FlushIntents();
            flow.Drag(0, 100);
            flow.FlushIntents();
            Assert.That(flow.Orientation.Pitch, Is.Zero, "置いた物のドラッグの端数を、次に手に取った物へ持ち越さない");
        }

        [Test]
        public void Drag_StartedBeforePutDown_IsRejected()
        {
            var flow = NewGame();
            GoTo(flow, View("desk"));
            var p = PointOf(flow, Command.Inspect(Part("desk.cylinder")));
            flow.ClickWorld(p.X, p.Y);
            flow.FlushIntents();
            var started = flow.Version;

            // 左ボタンを押したまま右クリックで置き、もう一度手に取ってから、前のドラッグが届く。
            flow.Press(IntentKind.Back);
            flow.FlushIntents();
            flow.ClickWorld(p.X, p.Y);
            flow.FlushIntents();
            flow.Enqueue(Intent.Drag(0, 400, started));
            flow.FlushIntents();
            Assert.That(flow.Held, Is.GreaterThanOrEqualTo(0));
            Assert.That(flow.Orientation, Is.EqualTo(default(Orientation)), "前の物のドラッグは新しく手に取った物を回さない");
            flow.Enqueue(Intent.Drag(0, 200, flow.Version));
            flow.FlushIntents();
            Assert.That(flow.Orientation.Pitch, Is.EqualTo(1));
        }
    }
}
