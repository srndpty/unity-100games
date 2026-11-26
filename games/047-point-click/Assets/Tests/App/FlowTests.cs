using System;
using System.Linq;
using Game.Core.Play;
using Game.Core.Rules;
using NUnit.Framework;

namespace Game.App.Tests
{
    /// <summary>画面の流れ・命令の組み立て・台詞・会話。</summary>
    public sealed class FlowTests
    {
        // ---- 状態遷移 ----

        [Test]
        public void TitleToPlayToResultToTitle_AndReplayStartsFresh()
        {
            var f = AppLab.Playing();
            new Autopilot(AppLab.Walkthrough).RunToResult(f);
            Assert.That(f.Screen, Is.EqualTo(FlowScreen.Result));
            Assert.That(f.Escaped, Is.True);
            Assert.That(f.SolvedCount, Is.EqualTo(5));
            Assert.That(f.ActionCount, Is.EqualTo(AppLab.Walkthrough.Count(m => m.Kind != Game.Core.Analysis.MoveKind.EndTalk)), "「さようなら」は行為に数えない");
            Assert.That(f.ElapsedSeconds, Is.GreaterThan(0));

            f.Press(IntentKind.Confirm);
            f.FlushIntents();
            Assert.That(f.Screen, Is.EqualTo(FlowScreen.Title));
            f.Press(IntentKind.Confirm);
            f.FlushIntents();
            Assert.That(f.Screen, Is.EqualTo(FlowScreen.Playing));
            Assert.That(f.Inventory, Is.Empty, "再プレイは最初から");
            Assert.That(f.SolvedCount, Is.EqualTo(0));
            Assert.That(f.Room, Is.EqualTo(AppLab.Book.StartRoom));
            Assert.That(f.Visible(AppLab.H("cottage.knife")), Is.True);
        }

        [Test]
        public void Walkthrough_ThroughFlow_FinalHash_IsFixed()
        {
            var f = AppLab.Playing();
            new Autopilot(AppLab.Walkthrough).RunToResult(f);
            // 台詞を出している間も Tick は進むので、Core だけの通し（ProductionTests）とは Tick が違う。Flow を通した通しの固定値。
            Assert.That(f.WorldHash, Is.EqualTo(0xCDF6223Eu), "整数と Tick だけなので .NET と Mono で同じ値になる");
        }

        [Test]
        public void AfterEscape_LinesAndCuesDoNotRepeatWhileWaiting()
        {
            // 人が遊ぶときのように、最後の灯をともした後は入力せずに時間だけ進める。
            var f = AppLab.After(AppLab.Walkthrough.Length - 1);
            var pilot = new Autopilot(AppLab.Walkthrough.Skip(AppLab.Walkthrough.Length - 1).ToArray());
            pilot.Drive(f);
            for (int i = 0; i < 3000 && f.CurrentLine == null; i++) { f.StepTicks(1); }
            Assert.That(f.Escaped, Is.True);
            Assert.That(f.CurrentLine!.Text, Is.EqualTo("灯がともった！ 光が嵐の海を照らす。"));
            var solve = f.Cues.Solve;
            var escape = f.Cues.Escape;
            f.StepTicks(100);
            Assert.That(f.Cues.Solve, Is.EqualTo(solve), "謎を解いた音を鳴らし直さない");
            Assert.That(f.Cues.Escape, Is.EqualTo(escape));
            Assert.That(escape, Is.EqualTo(1));
            f.StepTicks(2000);
            Assert.That(f.Screen, Is.EqualTo(FlowScreen.Result), "台詞は時間で送られて結果画面になる");
        }

        [Test]
        public void AfterEscape_OneClickPerLineReachesResult()
        {
            var f = AppLab.After(AppLab.Walkthrough.Length - 1);
            new Autopilot(AppLab.Walkthrough.Skip(AppLab.Walkthrough.Length - 1).ToArray()).Drive(f);
            for (int i = 0; i < 3000 && f.CurrentLine == null; i++) { f.StepTicks(1); }
            f.StepTicks(30);
            f.Click(ClickTarget.Background, -1);
            f.StepTicks(1);
            Assert.That(f.Screen, Is.EqualTo(FlowScreen.Result), "最後の台詞を 1 回のクリックで送ると結果画面");
        }

        [Test]
        public void ResultScreen_IgnoresGameInput()
        {
            var f = AppLab.Playing();
            new Autopilot(AppLab.Walkthrough).RunToResult(f);
            var hash = f.WorldHash;
            f.Press(IntentKind.VerbKey, 0);
            f.Press(IntentKind.Deselect);
            f.Press(IntentKind.Up);
            f.StepTicks(30);
            Assert.That(f.Screen, Is.EqualTo(FlowScreen.Result));
            Assert.That(f.WorldHash, Is.EqualTo(hash));
            f.Click(ClickTarget.Background, -1);
            f.FlushIntents();
            Assert.That(f.Screen, Is.EqualTo(FlowScreen.Title));
        }

        [Test]
        public void HowToAndQuit_FromTitle()
        {
            var f = AppLab.NewFlow();
            f.Press(IntentKind.Down);
            f.Press(IntentKind.Confirm);
            f.FlushIntents();
            Assert.That(f.Screen, Is.EqualTo(FlowScreen.HowTo));
            f.Press(IntentKind.Pause);
            f.FlushIntents();
            Assert.That(f.Screen, Is.EqualTo(FlowScreen.Title));
            Assert.That(f.MenuCursor, Is.EqualTo(AdventureFlow.TitleNew));
            f.Click(ClickTarget.Menu, AdventureFlow.TitleQuit);
            f.FlushIntents();
            Assert.That(f.QuitRequested, Is.True);
        }

        [Test]
        public void Pause_StopsWorldAndClock()
        {
            var f = AppLab.Playing();
            AppLab.ClickHotspot(f, "cottage.stove");
            f.StepTicks(3);
            var x = f.PlayerX;
            var ticks = f.Ticks;
            f.Press(IntentKind.Pause);
            f.StepTicks(60);
            Assert.That(f.Screen, Is.EqualTo(FlowScreen.Paused));
            Assert.That(f.PlayerX, Is.EqualTo(x));
            Assert.That(f.Ticks, Is.EqualTo(ticks));
            AppLab.ClickHotspot(f, "cottage.knife");
            f.FlushIntents();
            Assert.That(f.Screen, Is.EqualTo(FlowScreen.Paused), "一時停止中のクリックは世界に届かない");
            f.Press(IntentKind.Pause);
            f.StepTicks(1);
            Assert.That(f.PlayerX, Is.Not.EqualTo(x));
        }

        [Test]
        public void PauseMenu_BackToTitle()
        {
            var f = AppLab.After(2);
            f.Press(IntentKind.Pause);
            f.Press(IntentKind.Down);
            f.Press(IntentKind.Confirm);
            f.FlushIntents();
            Assert.That(f.Screen, Is.EqualTo(FlowScreen.Title));
            Assert.That(f.InGame, Is.False);
        }

        [Test]
        public void FocusLoss_DropsPendingAndLaterInput()
        {
            var f = AppLab.Playing();
            AppLab.ClickHotspot(f, "cottage.knife");
            f.OnFocusChanged(false);
            AppLab.ClickHotspot(f, "cottage.knife");
            f.StepTicks(200);
            Assert.That(f.IsWalking, Is.False);
            Assert.That(f.Inventory, Is.Empty);
            f.OnFocusChanged(true);
            AppLab.ClickVerb(f, Verb.Take);
            AppLab.ClickHotspot(f, "cottage.knife");
            AppLab.Settle(f);
            Assert.That(f.Inventory, Is.EqualTo(new[] { AppLab.I("knife") }));
        }

        // ---- 命令の組み立て ----

        [Test]
        public void VerbThenTarget_WalksAndResolves()
        {
            var f = AppLab.Playing();
            AppLab.ClickVerb(f, Verb.Take);
            f.FlushIntents();
            Assert.That(f.SelectedVerb, Is.EqualTo(Verb.Take));
            AppLab.ClickHotspot(f, "cottage.knife");
            f.FlushIntents();
            Assert.That(f.IsWalking, Is.True);
            Assert.That(f.SelectedVerb, Is.EqualTo(Verb.Walk), "命じたら選択を外す");
            AppLab.Settle(f);
            Assert.That(f.Inventory, Is.EqualTo(new[] { AppLab.I("knife") }));
            Assert.That(f.CurrentLine!.Text, Is.EqualTo("包丁を手に取った。"));
            Assert.That(f.Cues.Pickup, Is.EqualTo(1));
        }

        [Test]
        public void VerbKeys_SelectAndToggle()
        {
            var f = AppLab.Playing();
            f.Press(IntentKind.VerbKey, 0);
            f.FlushIntents();
            Assert.That(f.SelectedVerb, Is.EqualTo(Verb.Look));
            f.Press(IntentKind.VerbKey, 3);
            f.FlushIntents();
            Assert.That(f.SelectedVerb, Is.EqualTo(Verb.Talk));
            f.Press(IntentKind.VerbKey, 3);
            f.FlushIntents();
            Assert.That(f.SelectedVerb, Is.EqualTo(Verb.Walk), "同じ動詞をもう一度で外す");
            f.Press(IntentKind.VerbKey, 9);
            f.Press(IntentKind.VerbKey, -1);
            f.FlushIntents();
            Assert.That(f.SelectedVerb, Is.EqualTo(Verb.Walk));
        }

        [Test]
        public void ItemThenTarget_BuildsUseSentence()
        {
            var f = AppLab.After(5);
            Assert.That(f.Inventory.Select(i => AppLab.Book.Items[i].Id), Is.EqualTo(AppLab.Of("knife", "cup", "tea_leaves", "bottle")));
            AppLab.ClickItem(f, "cup");
            f.FlushIntents();
            Assert.That(f.SelectedVerb, Is.EqualTo(Verb.Use));
            Assert.That(f.SelectedTool, Is.EqualTo(AppLab.I("cup")));
            f.SetHover(AppLab.H("cottage.stove"), -1);
            Assert.That(f.Sentence, Is.EqualTo("使う カップ → ストーブ"));
            f.SetHover(AppLab.H("dock.net"), -1);
            Assert.That(f.Sentence, Is.EqualTo("使う カップ →"), "別の部屋の物は出さない");
            AppLab.ClickHotspot(f, "cottage.stove");
            AppLab.Settle(f);
            Assert.That(f.Inventory, Has.Member(AppLab.I("hot_water")));
            Assert.That(f.Inventory, Has.No.Member(AppLab.I("cup")));
        }

        [Test]
        public void CombiningItems_InEitherOrder()
        {
            foreach (var (tool, target) in new[] { ("tea_leaves", "hot_water"), ("hot_water", "tea_leaves") })
            {
                var f = AppLab.After(6);
                AppLab.ClickItem(f, tool);
                f.FlushIntents();
                AppLab.ClickItem(f, target);
                f.FlushIntents();
                Assert.That(f.Inventory, Has.Member(AppLab.I("tea")), $"{tool} → {target}");
                Assert.That(f.IsWalking, Is.False, "持ち物どうしは歩かずにすぐ");
                Assert.That(f.CurrentLine!.Text, Is.EqualTo("茶葉を入れて、温かいお茶ができた。"));
            }
        }

        [Test]
        public void ClickingSelectedToolAgain_Deselects()
        {
            var f = AppLab.After(2);
            AppLab.ClickItem(f, "knife");
            AppLab.ClickItem(f, "knife");
            f.FlushIntents();
            Assert.That(f.SelectedVerb, Is.EqualTo(Verb.Use));
            Assert.That(f.SelectedTool, Is.EqualTo(-1));
            f.Press(IntentKind.Deselect);
            f.FlushIntents();
            Assert.That(f.SelectedVerb, Is.EqualTo(Verb.Walk));
        }

        [Test]
        public void LookAtItem_ResolvesImmediately()
        {
            var f = AppLab.After(2);
            AppLab.ClickVerb(f, Verb.Look);
            AppLab.ClickItem(f, "knife");
            f.FlushIntents();
            Assert.That(f.CurrentLine!.Text, Is.EqualTo("よく研いだ包丁だ。"));
        }

        [Test]
        public void TakingAnItem_IsRefusedWithLine()
        {
            var f = AppLab.After(2);
            var hash = f.WorldHash;
            AppLab.ClickVerb(f, Verb.Take);
            AppLab.ClickItem(f, "knife");
            f.FlushIntents();
            Assert.That(f.CurrentLine!.Text, Is.EqualTo("それはできない。"));
            Assert.That(f.CurrentLine.IsDefault, Is.True);
            Assert.That(f.Cues.Reject, Is.EqualTo(1));
            Assert.That(f.WorldHash, Is.EqualTo(hash));
        }

        [Test]
        public void ItemSlotOutOfRange_IsIgnored()
        {
            var f = AppLab.Playing();
            var hash = f.WorldHash;
            f.Click(ClickTarget.Item, 0);
            f.Click(ClickTarget.Item, -1);
            f.FlushIntents();
            Assert.That(f.SelectedTool, Is.EqualTo(-1));
            Assert.That(f.WorldHash, Is.EqualTo(hash));
        }

        [Test]
        public void ClickOnFloor_WalksThereKeepingVerb()
        {
            var f = AppLab.Playing();
            f.Press(IntentKind.VerbKey, 0);
            f.ClickWorld(1300, 250);
            AppLab.Settle(f);
            Assert.That(f.PlayerX, Is.EqualTo(1300));
            Assert.That(f.SelectedVerb, Is.EqualTo(Verb.Look));
            f.ClickWorld(99999, 250);
            AppLab.Settle(f);
            Assert.That(f.PlayerX, Is.EqualTo(1920), "部屋の端で止める");
        }

        [Test]
        public void Sentence_ShowsVerbAndHover()
        {
            var f = AppLab.Playing();
            Assert.That(f.Sentence, Is.EqualTo("歩く"));
            f.SetHover(AppLab.H("cottage.knife"), -1);
            Assert.That(f.Sentence, Is.EqualTo("歩く 包丁"));
            f.Press(IntentKind.VerbKey, 1);
            f.FlushIntents();
            Assert.That(f.Sentence, Is.EqualTo("取る 包丁"));
            f.SetHover(-1, -1);
            Assert.That(f.Sentence, Is.EqualTo("取る"));
        }

        // ---- 台詞 ----

        [Test]
        public void ClickDuringLine_OnlyAdvancesLine()
        {
            var f = AppLab.Playing();
            AppLab.ClickVerb(f, Verb.Look);
            AppLab.ClickHotspot(f, "cottage.window");
            AppLab.Settle(f);
            Assert.That(f.CurrentLine, Is.Not.Null);
            var hash = f.WorldHash;
            AppLab.ClickHotspot(f, "cottage.knife");
            AppLab.ClickHotspot(f, "cottage.knife");
            f.FlushIntents();
            Assert.That(f.CurrentLine, Is.Null, "クリックで台詞を送る");
            Assert.That(f.IsWalking, Is.False, "台詞を送るクリックは世界に届かない。同じフレームの 2 回目は版が古いので捨てる");
            Assert.That(f.WorldHash, Is.EqualTo(hash));
        }

        [Test]
        public void Lines_AdvanceByTime()
        {
            var settings = new FlowSettings { LineBaseTicks = 10, LineTicksPerChar = 1 };
            var f = AppLab.Playing(settings);
            AppLab.ClickVerb(f, Verb.Look);
            AppLab.ClickHotspot(f, "cottage.window");
            AppLab.Settle(f);
            var text = f.CurrentLine!.Text;
            f.StepTicks(10 + text.Length - 1);
            Assert.That(f.CurrentLine, Is.Not.Null);
            f.StepTicks(1);
            Assert.That(f.CurrentLine, Is.Null);
        }

        [Test]
        public void MultipleLines_ShowInOrder()
        {
            var f = AppLab.After(14);
            Assert.That(f.ShowingTopics, Is.True);
            f.Click(ClickTarget.Topic, 0);
            f.FlushIntents();
            Assert.That(f.CurrentLine!.Text, Is.EqualTo("町へ戻れませんか？"));
            Assert.That(f.CurrentLine.Speaker, Is.EqualTo(""));
            f.Press(IntentKind.Confirm);
            f.FlushIntents();
            Assert.That(f.CurrentLine!.Text, Is.EqualTo("灯台の灯が消えてちゃ、岩場を抜けられん。"));
            Assert.That(f.CurrentLine.Speaker, Is.EqualTo("船頭"));
            f.Press(IntentKind.Confirm);
            f.FlushIntents();
            Assert.That(f.CurrentLine!.Text, Is.EqualTo("灯がともれば、すぐ船を出すさ。"));
            f.Press(IntentKind.Confirm);
            f.FlushIntents();
            Assert.That(f.ShowingTopics, Is.True);
        }

        // ---- 会話 ----

        [Test]
        public void Conversation_TopicsAndGoodbye()
        {
            var f = AppLab.After(14);
            Assert.That(f.IsTalking, Is.True);
            Assert.That(f.TopicOptions, Is.EqualTo(AppLab.Of("町へ戻れませんか？", "ひどい嵐ですね。", "さようなら")));
            Assert.That(f.Sentence, Is.EqualTo(""));
            var hash = f.WorldHash;
            f.Press(IntentKind.VerbKey, 0);
            AppLab.ClickHotspot(f, "dock.net");
            f.Click(ClickTarget.Topic, 9);
            f.FlushIntents();
            Assert.That(f.WorldHash, Is.EqualTo(hash), "会話中は会話以外の操作が届かない");
            Assert.That(f.SelectedVerb, Is.EqualTo(Verb.Walk));

            f.Press(IntentKind.Up);
            f.FlushIntents();
            Assert.That(f.TopicCursor, Is.EqualTo(2));
            f.Press(IntentKind.Confirm);
            f.FlushIntents();
            Assert.That(f.IsTalking, Is.False);
            Assert.That(f.TopicOptions, Is.Empty);
        }

        [Test]
        public void Conversation_UnlocksNextTopic()
        {
            var f = AppLab.After(15);
            Assert.That(f.TopicOptions, Is.EqualTo(AppLab.Of("灯台のことですが……", "火を持っていませんか？", "ひどい嵐ですね。", "さようなら")));
        }

        // ---- 完了判定：アイテム状態が移動後も保持（画面の流れを通して） ----

        [Test]
        public void ItemsAndObjects_PersistAcrossRooms()
        {
            var f = AppLab.After(8);
            Assert.That(f.Room, Is.EqualTo(AppLab.Book.RoomIndex("dock")));
            var inventory = f.Inventory.ToArray();
            AppLab.ClickHotspot(f, "dock.path");
            AppLab.Settle(f);
            Assert.That(f.Room, Is.EqualTo(AppLab.Book.StartRoom));
            Assert.That(f.Inventory, Is.EqualTo(inventory));
            foreach (var id in new[] { "cottage.knife", "cottage.cup", "cottage.tea_tin", "cottage.bottle" })
            {
                Assert.That(f.Visible(AppLab.H(id)), Is.False, id);
            }
        }

        [Test]
        public void Settings_AreValidatedAndCopied()
        {
            Assert.Throws<ArgumentException>(() => AppLab.NewFlow(new FlowSettings { TickRate = 5 }));
            Assert.Throws<ArgumentException>(() => AppLab.NewFlow(new FlowSettings { LineBaseTicks = 0 }));
            Assert.Throws<ArgumentException>(() => new AdventureFlow(AppLab.Book, new WorldSettings { WalkSpeed = 0 }));
            var s = new FlowSettings { LineBaseTicks = 10, LineTicksPerChar = 0 };
            var f = AppLab.Playing(s);
            s.LineBaseTicks = 1000;
            AppLab.ClickVerb(f, Verb.Look);
            AppLab.ClickHotspot(f, "cottage.window");
            AppLab.Settle(f);
            f.StepTicks(10);
            Assert.That(f.CurrentLine, Is.Null, "渡した後に設定を書き換えても効かない");
        }

        [Test]
        public void Autopilot_RejectsWalkthroughThatDoesNotFit()
        {
            var f = AppLab.Playing();
            var wrong = AppLab.Walkthrough.Skip(16).ToArray();
            var pilot = new Autopilot(wrong);
            Assert.Throws<InvalidOperationException>(() => pilot.RunToResult(f, 5000));
        }
    }
}
