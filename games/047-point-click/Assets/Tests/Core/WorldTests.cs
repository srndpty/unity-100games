using System;
using System.Linq;
using Game.Core.Play;
using Game.Core.Rules;
using IntGeometry;
using NUnit.Framework;

namespace Game.Core.Tests
{
    /// <summary>世界の状態と行為の解決。</summary>
    public sealed class WorldTests
    {
        // ---- 題材固有：所持しないアイテムを使用できない ----

        [Test]
        public void UsingItemNotYetObtained_IsRejectedWithoutChange()
        {
            var w = Lab.NewWorld();
            var before = w.Hash();
            var o = w.Act(Verb.Use, Lab.HT("dock.net"), Lab.I("knife"));
            Assert.That(o.Reason, Is.EqualTo(Refusal.NotHeld));
            Assert.That(w.Hash(), Is.EqualTo(before));
            Assert.That(w.IsWalking, Is.False);

            Lab.Do(w, Verb.Take, "dock.knife");
            var events = Lab.Do(w, Verb.Use, "dock.net", "knife");
            Assert.That(events.Any(e => e.Kind == EventKind.ItemGained && e.Index == Lab.I("key")), Is.True);
        }

        [Test]
        public void UsingItemAlreadyLost_IsRejected()
        {
            var w = Lab.IntoShed();
            Assert.That(w.Has(Lab.I("key")), Is.False, "扉を開けると鍵を失う");
            Assert.That(w.Obtained(Lab.I("key")), Is.True);
            Lab.Do(w, Verb.Walk, "shed.out");
            var before = w.Hash();
            Assert.That(w.Act(Verb.Use, Lab.HT("dock.door"), Lab.I("key")).Reason, Is.EqualTo(Refusal.NotHeld));
            Assert.That(w.Hash(), Is.EqualTo(before));
        }

        [Test]
        public void CombiningWithOnlyOneHeldItem_IsRejected()
        {
            var w = Lab.NewWorld();
            Lab.Do(w, Verb.Take, "dock.cloth");
            var before = w.Hash();
            Assert.That(w.Act(Verb.Use, Lab.IT("stick"), Lab.I("cloth")).Reason, Is.EqualTo(Refusal.NotHeld), "対象の棒を持っていない");
            Assert.That(w.Act(Verb.Use, Lab.IT("cloth"), Lab.I("stick")).Reason, Is.EqualTo(Refusal.NotHeld), "道具の棒を持っていない");
            Assert.That(w.Act(Verb.Look, Lab.IT("stick")).Reason, Is.EqualTo(Refusal.NotHeld));
            Assert.That(w.Hash(), Is.EqualTo(before));
        }

        [Test]
        public void ToolNotHeld_IsRejectedEvenWhileWalking()
        {
            var w = Lab.NewWorld();
            Assert.That(w.Act(Verb.Look, Lab.HT("dock.net")).Accepted, Is.True);
            w.Step();
            var before = w.Hash();
            Assert.That(w.Act(Verb.Use, Lab.HT("dock.net"), Lab.I("knife")).Reason, Is.EqualTo(Refusal.NotHeld));
            Assert.That(w.Hash(), Is.EqualTo(before), "前の命令（見る）のまま歩き続ける");
            Assert.That(Lab.Said(Lab.Arrive(w)), Is.EqualTo(Lab.Of("古い漁網に何か絡まっている。")));
        }

        // ---- 題材固有：解決済みギミックを重複実行しない ----

        [Test]
        public void SolvedPuzzle_IsNotSolvedAgain()
        {
            var w = Lab.NewWorld();
            Lab.Do(w, Verb.Take, "dock.knife");
            var first = Lab.Do(w, Verb.Use, "dock.net", "knife");
            Assert.That(first.Count(e => e.Kind == EventKind.PuzzleSolved), Is.EqualTo(1));
            var inventory = w.Inventory.ToArray();

            var second = Lab.Do(w, Verb.Use, "dock.net", "knife");
            Assert.That(second.Any(e => e.Kind == EventKind.PuzzleSolved || e.Kind == EventKind.ItemGained), Is.False);
            Assert.That(second.Single().IsDefault, Is.True);
            Assert.That(second.Single().Text, Is.EqualTo(DefaultLines.UseWith));
            Assert.That(w.Inventory, Is.EqualTo(inventory));
            Assert.That(w.SolvedCount, Is.EqualTo(1));
        }

        [Test]
        public void PickedUpObject_CannotBeTakenTwice()
        {
            var w = Lab.NewWorld();
            Lab.Do(w, Verb.Take, "dock.knife");
            Assert.That(w.Visible(Lab.H("dock.knife")), Is.False);
            Assert.That(w.Act(Verb.Take, Lab.HT("dock.knife")).Reason, Is.EqualTo(Refusal.NotHere));
            Assert.That(w.Inventory.Count(i => i == Lab.I("knife")), Is.EqualTo(1));
        }

        [Test]
        public void CombinationInEitherOrder_HappensOnce()
        {
            var w = Lab.IntoShed();
            Lab.Do(w, Verb.Take, "shed.stick");
            Lab.Do(w, Verb.Walk, "shed.out");
            Lab.Do(w, Verb.Take, "dock.cloth");
            Assert.That(w.Act(Verb.Use, Lab.IT("cloth"), Lab.I("stick")).Accepted, Is.True);
            Assert.That(w.Has(Lab.I("torch")), Is.True);
            Assert.That(w.Events.Count(e => e.Kind == EventKind.ItemGained), Is.EqualTo(1));
            Assert.That(w.Act(Verb.Use, Lab.IT("stick"), Lab.I("cloth")).Reason, Is.EqualTo(Refusal.NotHeld), "材料はもうない");
        }

        [Test]
        public void SolveGuard_AlsoAppliesToTopics()
        {
            var src = Lab.Small.Replace("@puzzle cut_net 網を切る", "@puzzle cut_net 網を切る\n@puzzle chat 話す", StringComparison.Ordinal)
                .Replace("「嵐が来るぞ。」", "solve chat ; 「嵐が来るぞ。」", StringComparison.Ordinal)
                .Replace("use bottle on dock.man when asked :", "use bottle on dock.man when asked and solved chat :", StringComparison.Ordinal)
                .Replace("after open_door", "after open_door chat", StringComparison.Ordinal);
            var r = Lab.Compile(src);
            Assert.That(r.Ok, Is.True, Lab.Dump(r));
            var book = r.Book!;
            var w = new World(book, new WorldSettings());
            w.Act(Verb.Talk, Target.Hotspot(book.HotspotIndex("dock.man")));
            Lab.Arrive(w);
            var weather = book.TopicIndex("dock.man", "weather");
            Assert.That(w.AvailableTopics(), Has.Member(weather));
            Assert.That(w.Ask(weather).Accepted, Is.True);
            Assert.That(w.AvailableTopics(), Has.No.Member(weather));
            Assert.That(w.Ask(weather).Reason, Is.EqualTo(Refusal.Unavailable));
        }

        // ---- 完了判定：アイテム状態が移動後も保持 ----

        [Test]
        public void State_PersistsAcrossRooms()
        {
            var w = Lab.NewWorld();
            Lab.Do(w, Verb.Take, "dock.bottle");
            Lab.Do(w, Verb.Take, "dock.knife");
            Lab.Do(w, Verb.Use, "dock.net", "knife");
            Lab.Do(w, Verb.Use, "dock.door", "key");
            Lab.Do(w, Verb.Walk, "dock.door");
            Lab.Do(w, Verb.Take, "shed.stick");
            Lab.Do(w, Verb.Walk, "shed.out");
            Assert.That(w.Room, Is.EqualTo(Lab.Book.RoomIndex("dock")));
            Assert.That(w.X, Is.EqualTo(960));
            Assert.That(w.Visible(Lab.H("dock.bottle")), Is.False);
            Assert.That(w.Visible(Lab.H("dock.knife")), Is.False);
            Assert.That(w.Visible(Lab.H("shed.stick")), Is.False);
            Assert.That(w.Unlocked(Lab.H("dock.door")), Is.True);
            Assert.That(w.Inventory.Select(i => Lab.Book.Items[i].Id), Is.EqualTo(Lab.Of("bottle", "knife", "stick")));
            Lab.Do(w, Verb.Walk, "dock.door");
            Assert.That(w.Room, Is.EqualTo(Lab.Book.RoomIndex("shed")));
            Assert.That(w.Visible(Lab.H("shed.stick")), Is.False, "戻っても拾った棒は出てこない");
            Assert.That(w.HotspotAt(new Vec2i(315, 50)), Is.EqualTo(-1));
        }

        [Test]
        public void Clone_SharesNothing()
        {
            var w = Lab.NewWorld();
            var c = w.Clone();
            Lab.Do(w, Verb.Take, "dock.knife");
            Assert.That(c.Inventory, Is.Empty);
            Assert.That(c.Visible(Lab.H("dock.knife")), Is.True);
            Assert.That(c.Hash(), Is.Not.EqualTo(w.Hash()));
            Lab.Do(c, Verb.Take, "dock.knife");
            Assert.That(c.Hash(), Is.EqualTo(w.Hash()), "同じ命令の列なら同じハッシュ");
        }

        // ---- 歩く ----

        [Test]
        public void Walking_ResolvesOnArrivalTick()
        {
            var w = Lab.NewWorld(walkSpeed: 10);
            Assert.That(w.X, Is.EqualTo(100));
            w.Act(Verb.Take, Lab.HT("dock.knife"));
            for (int i = 0; i < 12; i++)
            {
                w.Step();
                Assert.That(w.Has(Lab.I("knife")), Is.False, $"Tick {i + 1}");
            }

            Assert.That(w.X, Is.EqualTo(220));
            w.Step();
            Assert.That(w.X, Is.EqualTo(230));
            Assert.That(w.Has(Lab.I("knife")), Is.True, "着いた Tick（13 Tick 目）に解決");
            Assert.That(w.Tick, Is.EqualTo(13));
        }

        [Test]
        public void ActingWhereAlreadyStanding_ResolvesOnNextStep()
        {
            var w = Lab.NewWorld();
            Lab.Do(w, Verb.Look, "dock.knife");
            w.Act(Verb.Take, Lab.HT("dock.knife"));
            Assert.That(w.Has(Lab.I("knife")), Is.False);
            w.Step();
            Assert.That(w.Has(Lab.I("knife")), Is.True);
        }

        [Test]
        public void NewCommandWhileWalking_ReplacesPending()
        {
            var w = Lab.NewWorld();
            w.Act(Verb.Take, Lab.HT("dock.knife"));
            w.Step();
            w.Act(Verb.Take, Lab.HT("dock.bottle"));
            Lab.Arrive(w);
            Assert.That(w.Has(Lab.I("bottle")), Is.True);
            Assert.That(w.Has(Lab.I("knife")), Is.False, "置き換えた前の命令は捨てる");
            Assert.That(w.Facing, Is.EqualTo(1));
        }

        [Test]
        public void ItemOnlyAction_StopsWalkingAndResolvesNow()
        {
            var w = Lab.NewWorld();
            Lab.Do(w, Verb.Take, "dock.knife");
            w.Act(Verb.Take, Lab.HT("dock.bottle"));
            w.Step();
            var x = w.X;
            Assert.That(w.Act(Verb.Look, Lab.IT("knife")).Accepted, Is.True);
            Assert.That(Lab.Said(w.Events.ToArray()), Is.EqualTo(Lab.Of("錆びている。")));
            Assert.That(w.IsWalking, Is.False);
            w.Step();
            Assert.That(w.X, Is.EqualTo(x));
            Assert.That(w.Has(Lab.I("bottle")), Is.False);
        }

        [Test]
        public void WalkTo_ClampsNothingAndRejectsOutsideRoom()
        {
            var w = Lab.NewWorld(walkSpeed: 64);
            Assert.That(w.WalkTo(-1).Reason, Is.EqualTo(Refusal.OutOfRange));
            Assert.That(w.WalkTo(1001).Reason, Is.EqualTo(Refusal.OutOfRange));
            Assert.That(w.WalkTo(0).Accepted, Is.True);
            Lab.Arrive(w);
            Assert.That(w.X, Is.EqualTo(0));
            Assert.That(w.Facing, Is.EqualTo(-1));
            Assert.That(w.WalkTo(1000).Accepted, Is.True);
            Lab.Arrive(w);
            Assert.That(w.X, Is.EqualTo(1000));
            Assert.That(w.Actions, Is.EqualTo(0), "歩いただけは行為に数えない");
        }

        [Test]
        public void LockedExit_DoesNotMove()
        {
            var w = Lab.NewWorld();
            var events = Lab.Do(w, Verb.Walk, "dock.door");
            Assert.That(w.Room, Is.EqualTo(Lab.Book.RoomIndex("dock")));
            Assert.That(Lab.Said(events), Is.EqualTo(Lab.Of(DefaultLines.Locked)));
            Lab.Do(w, Verb.Use, "dock.door");
            Assert.That(w.Room, Is.EqualTo(Lab.Book.RoomIndex("dock")), "持ち物なしの「使う」も移動と同じ");
        }

        [Test]
        public void Settings_AreValidated()
        {
            Assert.Throws<ArgumentException>(() => Lab.NewWorld(walkSpeed: 0));
            Assert.Throws<ArgumentException>(() => Lab.NewWorld(walkSpeed: WorldSettings.MaxWalkSpeed + 1));
            Assert.DoesNotThrow(() => Lab.NewWorld(walkSpeed: WorldSettings.MaxWalkSpeed));
        }

        // ---- 当たり ----

        [Test]
        public void HitTest_PicksFrontmostVisibleInCurrentRoom()
        {
            var w = Lab.NewWorld();
            Assert.That(w.HotspotAt(new Vec2i(230, 30)), Is.EqualTo(Lab.H("dock.knife")));
            Assert.That(w.HotspotAt(new Vec2i(200, 0)), Is.EqualTo(Lab.H("dock.knife")), "矩形は両端を含む");
            Assert.That(w.HotspotAt(new Vec2i(199, 0)), Is.EqualTo(-1));
            Assert.That(w.HotspotAt(new Vec2i(315, 50)), Is.EqualTo(-1), "別の部屋の物は当たらない");
            Lab.Do(w, Verb.Take, "dock.knife");
            Assert.That(w.HotspotAt(new Vec2i(230, 30)), Is.EqualTo(-1), "隠した物は当たらない");
        }

        [Test]
        public void HitTest_OverlapGoesToLaterDeclaration()
        {
            var src = Lab.Small.Replace("@object dock.net 漁網 rect 400 100 600 300 stand 500",
                "@object dock.net 漁網 rect 400 100 600 300 stand 500\n@object dock.crab 蟹 rect 450 150 470 170 stand 460", StringComparison.Ordinal)
                + "look dock.crab : 「蟹だ。」\n";
            var r = Lab.Compile(src);
            Assert.That(r.Ok, Is.True, Lab.Dump(r));
            var w = new World(r.Book!, new WorldSettings());
            Assert.That(w.HotspotAt(new Vec2i(460, 160)), Is.EqualTo(r.Book!.HotspotIndex("dock.crab")));
            Assert.That(w.HotspotAt(new Vec2i(440, 160)), Is.EqualTo(r.Book!.HotspotIndex("dock.net")));
        }

        // ---- 拒否 ----

        [Test]
        public void InvalidVerbTargetPairs_AreRejected()
        {
            var w = Lab.NewWorld();
            Lab.Do(w, Verb.Take, "dock.knife");
            Lab.Do(w, Verb.Take, "dock.bottle");
            var before = w.Hash();
            Assert.That(w.Act(Verb.Take, Lab.IT("knife")).Reason, Is.EqualTo(Refusal.NotAllowed));
            Assert.That(w.Act(Verb.Talk, Lab.IT("knife")).Reason, Is.EqualTo(Refusal.NotAllowed));
            Assert.That(w.Act(Verb.Use, Lab.IT("knife")).Reason, Is.EqualTo(Refusal.NotAllowed));
            Assert.That(w.Act(Verb.Use, Lab.IT("knife"), Lab.I("knife")).Reason, Is.EqualTo(Refusal.NotAllowed));
            Assert.That(w.Act(Verb.Walk, Lab.IT("knife")).Reason, Is.EqualTo(Refusal.NotAllowed));
            Assert.That(w.Act(Verb.Look, Lab.HT("dock.net"), Lab.I("knife")).Reason, Is.EqualTo(Refusal.NotAllowed));
            Assert.That(w.Act(Verb.Look, Target.None).Reason, Is.EqualTo(Refusal.NotAllowed));
            Assert.That(w.Act(Verb.Look, Target.Hotspot(999)).Reason, Is.EqualTo(Refusal.OutOfRange));
            Assert.That(w.Act(Verb.Look, Target.Item(-1)).Reason, Is.EqualTo(Refusal.OutOfRange));
            Assert.That(w.Act(Verb.Use, Lab.HT("dock.net"), 999).Reason, Is.EqualTo(Refusal.OutOfRange));
            Assert.That(w.Act(Verb.Use, Lab.HT("dock.net"), -2).Reason, Is.EqualTo(Refusal.OutOfRange));
            Assert.That(w.Act(Verb.Look, Lab.HT("shed.lamp")).Reason, Is.EqualTo(Refusal.NotHere));
            Assert.That(w.Hash(), Is.EqualTo(before));
        }

        [Test]
        public void DefaultLines_ForEachVerb()
        {
            var w = Lab.NewWorld();
            Assert.That(Lab.Said(Lab.Do(w, Verb.Take, "dock.net")), Is.EqualTo(Lab.Of(DefaultLines.Take)));
            Assert.That(Lab.Said(Lab.Do(w, Verb.Use, "dock.net")), Is.EqualTo(Lab.Of(DefaultLines.Use)));
            Assert.That(Lab.Said(Lab.Do(w, Verb.Talk, "dock.net")), Is.EqualTo(Lab.Of(DefaultLines.Talk)));
            Assert.That(w.TalkingTo, Is.EqualTo(-1), "人物でない物とは会話にならない");
        }

        // ---- 会話 ----

        [Test]
        public void Conversation_ShowsTopicsAndBlocksOtherActions()
        {
            var w = Lab.NewWorld();
            Assert.That(w.Ask(0).Reason, Is.EqualTo(Refusal.NotTalking));
            Assert.That(w.EndTalk().Reason, Is.EqualTo(Refusal.NotTalking));
            var events = Lab.Do(w, Verb.Talk, "dock.man");
            Assert.That(Lab.Said(events), Is.EqualTo(Lab.Of("こんにちは。")));
            Assert.That(events.Last().Kind, Is.EqualTo(EventKind.TalkStarted));
            Assert.That(w.TalkingTo, Is.EqualTo(Lab.H("dock.man")));

            var oil = Lab.Book.TopicIndex("dock.man", "oil");
            var weather = Lab.Book.TopicIndex("dock.man", "weather");
            Assert.That(w.AvailableTopics(), Is.EqualTo(Lab.Ints(oil, weather)));
            var before = w.Hash();
            Assert.That(w.Act(Verb.Look, Lab.HT("dock.net")).Reason, Is.EqualTo(Refusal.Talking));
            Assert.That(w.WalkTo(500).Reason, Is.EqualTo(Refusal.Talking));
            Assert.That(w.Ask(999).Reason, Is.EqualTo(Refusal.OutOfRange));
            Assert.That(w.Hash(), Is.EqualTo(before));

            Assert.That(w.Ask(oil).Accepted, Is.True);
            Assert.That(w.Events.Select(e => (e.Text, e.Speaker)), Is.EqualTo(new[] { ("油はありますか？", -1), ("瓶があれば分けてやる。", Lab.H("dock.man")) }));
            Assert.That(w.AvailableTopics(), Is.EqualTo(Lab.Ints(weather)), "一度聞いた話題は条件で消える");
            Assert.That(w.Ask(oil).Reason, Is.EqualTo(Refusal.Unavailable));
            Assert.That(w.EndTalk().Accepted, Is.True);
            Assert.That(w.TalkingTo, Is.EqualTo(-1));
            Assert.That(w.AvailableTopics(), Is.Empty);
        }

        [Test]
        public void GivingItemToNpc_DependsOnConversation()
        {
            var w = Lab.NewWorld();
            Lab.Do(w, Verb.Take, "dock.bottle");
            var events = Lab.Do(w, Verb.Use, "dock.man", "bottle");
            Assert.That(events.Single().IsDefault, Is.True, "話を聞く前は渡せない");
            Lab.Do(w, Verb.Talk, "dock.man");
            w.Ask(Lab.Book.TopicIndex("dock.man", "oil"));
            w.EndTalk();
            Lab.Do(w, Verb.Use, "dock.man", "bottle");
            Assert.That(w.Has(Lab.I("oil")), Is.True);
            Assert.That(w.Has(Lab.I("bottle")), Is.False);
        }

        // ---- 終了 ----

        [Test]
        public void Escape_EndsAndRejectsEverything()
        {
            var w = Lab.IntoShed();
            Lab.Do(w, Verb.Take, "shed.stick");
            Lab.Do(w, Verb.Walk, "shed.out");
            Lab.Do(w, Verb.Take, "dock.cloth");
            Lab.Do(w, Verb.Take, "dock.bottle");
            Lab.Do(w, Verb.Talk, "dock.man");
            w.Ask(Lab.Book.TopicIndex("dock.man", "oil"));
            w.EndTalk();
            Lab.Do(w, Verb.Use, "dock.man", "bottle");
            w.Act(Verb.Use, Lab.IT("stick"), Lab.I("cloth"));
            Lab.Do(w, Verb.Walk, "dock.door");
            Assert.That(Lab.Said(Lab.Do(w, Verb.Use, "shed.lamp", "torch")), Is.EqualTo(Lab.Of("油がない。")));
            Lab.Do(w, Verb.Use, "shed.lamp", "oil");
            var events = Lab.Do(w, Verb.Use, "shed.lamp", "torch");
            Assert.That(events.Last().Kind, Is.EqualTo(EventKind.Escaped));
            Assert.That(w.Escaped, Is.True);
            Assert.That(w.SolvedCount, Is.EqualTo(3));

            var hash = w.Hash();
            Assert.That(hash, Is.EqualTo(0xEF08657Du), "整数と Tick だけの規則なので、.NET と Mono で同じ値になる");
            var tick = w.Tick;
            Assert.That(w.Act(Verb.Look, Lab.HT("shed.lamp")).Reason, Is.EqualTo(Refusal.Escaped));
            Assert.That(w.WalkTo(10).Reason, Is.EqualTo(Refusal.Escaped));
            Assert.That(w.Ask(0).Reason, Is.EqualTo(Refusal.Escaped));
            Assert.That(w.EndTalk().Reason, Is.EqualTo(Refusal.Escaped));
            w.Step();
            Assert.That(w.Tick, Is.EqualTo(tick));
            Assert.That(w.Hash(), Is.EqualTo(hash));
        }
    }
}
