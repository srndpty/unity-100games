using System.Linq;
using Game.Core;
using NUnit.Framework;

namespace Game.App.Tests
{
    /// <summary>画面遷移と対象選択。</summary>
    public sealed class FlowTests
    {
        private static Intent I(IntentKind kind, int value = 0) => new(kind, value);

        [Test]
        public void TitleHowToAndStartWithANewSeedEachRun()
        {
            var flow = AppLab.NewFlow(10);
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Title));
            flow.Apply(I(IntentKind.HowTo));
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.HowTo));
            flow.Apply(I(IntentKind.Cancel));
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Title));
            flow.Apply(I(IntentKind.Confirm));
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Route));
            Assert.That(flow.Seed, Is.EqualTo(10));
            flow.Apply(I(IntentKind.Cancel));
            Assert.That(flow.Paused, Is.True);
            flow.Apply(I(IntentKind.ToTitle));
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Title));
            Assert.That(flow.Run, Is.Null);
            flow.Apply(I(IntentKind.Confirm));
            Assert.That(flow.Seed, Is.EqualTo(11));
            Assert.That(flow.RunNumber, Is.EqualTo(2));
            flow.Apply(I(IntentKind.Quit));
            Assert.That(flow.QuitRequested, Is.False, "タイトル以外では終了しない");
        }

        [Test]
        public void ChoosingARouteOptionStartsThatBattle()
        {
            var flow = AppLab.NewFlow();
            flow.Apply(I(IntentKind.Confirm));
            var hard = flow.Run!.Options[1].Encounter;
            flow.Apply(I(IntentKind.Number, 5));
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Route), "範囲外の数字は何もしない");
            flow.Apply(I(IntentKind.Number, 1));
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Battle));
            Assert.That(flow.Run.Battle!.Encounter, Is.SameAs(hard));
            Assert.That(flow.BattleNumber, Is.EqualTo(1));
        }

        [Test]
        public void InputDuringPlaybackIsDroppedAndClearsTheSelection()
        {
            var flow = AppLab.NewFlow();
            flow.Apply(I(IntentKind.Confirm));
            flow.Apply(I(IntentKind.Option, 0));
            Assert.That(flow.Playback!.Busy, Is.True, "ターンの始めの演出中");
            Assert.That(flow.AcceptsBattleInput, Is.False);
            var hash = flow.Run!.Hash();
            var card = flow.Run.Battle!.Hand[0];
            flow.Apply(I(IntentKind.SelectCard, card.Id));
            flow.Apply(I(IntentKind.EndTurn));
            Assert.That(flow.Run.Hash(), Is.EqualTo(hash));
            Assert.That(flow.Targeting.HasCard, Is.False);

            flow.FinishPlayback();
            var strike = flow.Run.Battle.Hand.FirstOrDefault(c => c.Def.NeedsTarget);
            if (strike == null) { Assert.Inconclusive("手札に対象を選ぶカードがない"); }
            flow.Apply(I(IntentKind.SelectCard, strike!.Id));
            Assert.That(flow.Targeting.CardId, Is.EqualTo(strike.Id));

            // 選んだ後で演出が始まる（ターン終了）と、届いた操作は捨てられ選択も消える。
            flow.Run.EndTurn();
            flow.Apply(I(IntentKind.ClickEnemy, 1));
            Assert.That(flow.Targeting.HasCard, Is.False);
            Assert.That(flow.Run.Battle.Events.Any(e => e.Kind == EventKind.CardPlayed && e.Detail == strike.Id), Is.False);
        }

        [Test]
        public void TargetCardWaitsForATargetAndCancelClearsIt()
        {
            var flow = AppLab.InBattleWithStrikeAndGuard();
            var battle = flow.Run!.Battle!;
            var strike = AppLab.InHand(flow, "strike");
            flow.Apply(I(IntentKind.SelectCard, strike.Id));
            Assert.That(flow.Targeting.CardId, Is.EqualTo(strike.Id));
            Assert.That(flow.Targeting.Cursor, Is.EqualTo(battle.Enemies.First(e => e.Alive).Id));
            Assert.That(battle.Hand, Has.Member(strike), "選んだだけでは使わない");

            flow.Apply(I(IntentKind.Cancel));
            Assert.That(flow.Targeting.HasCard, Is.False);
            Assert.That(flow.Paused, Is.False, "選択中の取り消しは一時停止にしない");
            flow.Apply(I(IntentKind.SelectCard, strike.Id));
            flow.Apply(I(IntentKind.SelectCard, strike.Id));
            Assert.That(flow.Targeting.HasCard, Is.False, "同じカードをもう一度で選択を外す");

            flow.Apply(I(IntentKind.SelectCard, strike.Id));
            var energy = battle.Energy;
            flow.Apply(I(IntentKind.Confirm));
            Assert.That(battle.Energy, Is.EqualTo(energy - 1));
            Assert.That(battle.Hand, Has.No.Member(strike));
            Assert.That(flow.Targeting.HasCard, Is.False);
        }

        [Test]
        public void CardWithoutTargetIsPlayedImmediately()
        {
            var flow = AppLab.InBattleWithStrikeAndGuard();
            var battle = flow.Run!.Battle!;
            var guard = AppLab.InHand(flow, "guard");
            flow.Apply(I(IntentKind.SelectCard, guard.Id));
            Assert.That(battle.Player.Block, Is.EqualTo(5));
            Assert.That(flow.Targeting.HasCard, Is.False);
        }

        [Test]
        public void NumberKeySelectsByShownHandPositionAndClickPicksTheEnemy()
        {
            var flow = AppLab.InBattle(1, 1);
            var battle = flow.Run!.Battle!;
            var hand = flow.Playback!.Table.Hand;
            var index = hand.ToList().FindIndex(c => c.Def.NeedsTarget);
            if (index < 0) { Assert.Inconclusive("手札に対象を選ぶカードがない"); }
            var card = hand[index];
            flow.Apply(I(IntentKind.Number, index));
            Assert.That(flow.Targeting.CardId, Is.EqualTo(card.Id));
            var last = battle.Enemies.Last(e => e.Alive);
            flow.Apply(I(IntentKind.ClickEnemy, last.Id));
            var played = battle.Events.Last(e => e.Kind == EventKind.CardPlayed);
            Assert.That(played.Detail, Is.EqualTo(card.Id));
            Assert.That(played.Target, Is.EqualTo(last.Id));
        }

        [Test]
        public void CycleTargetWrapsOverLivingEnemies()
        {
            DeckFlow? flow = null;
            for (int seed = 1; seed < 60 && flow == null; seed++)
            {
                var f = AppLab.InBattle(0, seed);
                if (f.Run!.Battle!.Enemies.Count >= 2 && f.Run.Battle.Hand.Any(c => c.Def.NeedsTarget)) { flow = f; }
            }

            Assert.That(flow, Is.Not.Null);
            var battle = flow!.Run!.Battle!;
            flow.Apply(I(IntentKind.SelectCard, battle.Hand.First(c => c.Def.NeedsTarget).Id));
            var ids = battle.Enemies.Where(e => e.Alive).Select(e => e.Id).ToList();
            Assert.That(flow.Targeting.Cursor, Is.EqualTo(ids[0]));
            flow.Apply(I(IntentKind.CycleTarget, 1));
            Assert.That(flow.Targeting.Cursor, Is.EqualTo(ids[1]));
            flow.Apply(I(IntentKind.CycleTarget, -1));
            flow.Apply(I(IntentKind.CycleTarget, -1));
            Assert.That(flow.Targeting.Cursor, Is.EqualTo(ids[ids.Count - 1]));
        }

        [Test]
        public void UnaffordableCardIsNotSelectedAndSaysWhy()
        {
            var flow = AppLab.InBattleWithStrikeAndGuard();
            var battle = flow.Run!.Battle!;
            var cards = battle.Hand.Where(c => c.Def.Cost == 1).Take(3).ToList();
            foreach (var c in cards) { flow.Apply(I(IntentKind.SelectCard, c.Id)); flow.Apply(I(IntentKind.Confirm)); flow.FinishPlayback(); }
            Assume.That(battle.Energy, Is.EqualTo(3 - cards.Count));
            var rest = battle.Hand.FirstOrDefault(c => c.Def.Cost > battle.Energy);
            Assume.That(rest, Is.Not.Null);
            var hash = flow.Run.Hash();
            flow.Apply(I(IntentKind.SelectCard, rest!.Id));
            Assert.That(flow.Targeting.HasCard, Is.False);
            Assert.That(flow.Notice, Is.EqualTo("エネルギーが足りない"));
            Assert.That(flow.Run.Hash(), Is.EqualTo(hash));
            for (int i = 0; i < DeckFlow.NoticeTicks; i++) { flow.Step(); }
            Assert.That(flow.Notice, Is.Empty);
        }

        [Test]
        public void NoticeDoesNotExpireWhilePaused()
        {
            var flow = AppLab.NewFlow();
            flow.Apply(I(IntentKind.Confirm));
            flow.Apply(I(IntentKind.Option, 0));
            flow.FinishPlayback();
            var battle = flow.Run!.Battle!;
            foreach (var c in battle.Hand.Where(c => c.Def.Cost > 0).ToList())
            {
                if (!battle.Affordable(c)) { break; }
                flow.Apply(I(IntentKind.SelectCard, c.Id));
                if (flow.Targeting.HasCard) { flow.Apply(I(IntentKind.Confirm)); }
                flow.FinishPlayback();
            }

            var expensive = battle.Hand.FirstOrDefault(c => !battle.Affordable(c));
            Assume.That(expensive, Is.Not.Null);
            flow.Apply(I(IntentKind.SelectCard, expensive!.Id));
            Assert.That(flow.Notice, Is.Not.Empty);
            flow.Apply(I(IntentKind.Cancel));
            Assert.That(flow.Paused, Is.True);
            for (int i = 0; i < DeckFlow.NoticeTicks * 5; i++) { flow.Step(); }
            Assert.That(flow.Notice, Is.Not.Empty, "一時停止の間に通知の期限が過ぎない");
            flow.Apply(I(IntentKind.Cancel));
            for (int i = 0; i < DeckFlow.NoticeTicks; i++) { flow.Step(); }
            Assert.That(flow.Notice, Is.Empty);
        }

        [Test]
        public void PauseStopsThePlaybackAndResumes()
        {
            var flow = AppLab.NewFlow();
            flow.Apply(I(IntentKind.Confirm));
            flow.Apply(I(IntentKind.Option, 0));
            flow.Apply(I(IntentKind.Cancel));
            Assert.That(flow.Paused, Is.True);
            var cursor = flow.Playback!.Cursor;
            var age = flow.Playback.Age;
            var tick = flow.Tick;
            for (int i = 0; i < 500; i++) { flow.Step(); }
            flow.Advance(3);
            Assert.That(flow.Playback.Cursor, Is.EqualTo(cursor));
            Assert.That(flow.Playback.Age, Is.EqualTo(age));
            Assert.That(flow.Tick, Is.EqualTo(tick), "一時停止の間は時計も止まる");
            flow.Apply(I(IntentKind.EndTurn));
            Assert.That(flow.Paused, Is.True, "一時停止中の操作は効かない");
            flow.Apply(I(IntentKind.Cancel));
            Assert.That(flow.Paused, Is.False);
            for (int i = 0; i < 10; i++) { flow.Step(); }
            Assert.That(flow.Playback.Cursor, Is.GreaterThan(cursor));
        }

        [Test]
        public void QueuedIntentsAreProcessedInOrderAndTicksAreCappedPerFrame()
        {
            var flow = AppLab.InBattleWithStrikeAndGuard();
            var battle = flow.Run!.Battle!;
            var strike = AppLab.InHand(flow, "strike");
            var target = battle.Enemies.First(e => e.Alive).Id;
            flow.Submit(I(IntentKind.SelectCard, strike.Id));
            flow.Submit(I(IntentKind.ClickEnemy, target));
            flow.Advance(0);
            Assert.That(battle.Hand, Has.No.Member(strike));

            var tick = flow.Tick;
            flow.Advance(1.0);
            Assert.That(flow.Tick - tick, Is.EqualTo(DeckFlow.MaxTicksPerFrame), "1 秒（60 Tick ぶん）でも 1 フレームは上限まで");
            tick = flow.Tick;
            flow.Advance(10);
            Assert.That(flow.Tick - tick, Is.EqualTo(DeckFlow.MaxTicksPerFrame), "超えた遅れは持ち越さない");
            tick = flow.Tick;
            flow.Advance(0);
            Assert.That(flow.Tick, Is.EqualTo(tick), "捨てた遅れは次のフレームで追いつかない");
            for (int i = 0; i < 5; i++) { flow.Advance(7.9 / DeckFlow.TickRate); }
            Assert.That(flow.Tick - tick, Is.LessThanOrEqualTo(5 * DeckFlow.MaxTicksPerFrame));
            tick = flow.Tick;
            tick = flow.Tick;
            flow.Advance(double.NaN);
            Assert.That(flow.Tick, Is.EqualTo(tick));
        }

        [Test]
        public void ClickPressedOnAScreenLeftByAnEarlierKeyInTheSameFrameIsDropped()
        {
            var flow = AppLab.InBattle(0, 2);
            var run = flow.Run!;
            WinCurrentBattle(flow);
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Reward));
            var hp = run.Hp;
            var maxHp = run.MaxHp;

            // 同じフレーム：Enter で報酬を去って休憩へ移り、その後に報酬のカードのクリックが届く。
            flow.Submit(I(IntentKind.Confirm));
            flow.SubmitPointer(I(IntentKind.Option, 1), AppScreen.Reward, false);
            flow.Advance(0);
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Rest));
            Assert.That(run.Stage, Is.EqualTo(RunStage.Rest), "報酬のクリックが休憩の選択にならない");
            Assert.That((run.Hp, run.MaxHp), Is.EqualTo((hp, maxHp)));

            // 押した画面のままなら効く（対の確認）。
            flow.SubmitPointer(I(IntentKind.Option, 1), AppScreen.Rest, false);
            flow.Advance(0);
            Assert.That(run.MaxHp, Is.GreaterThan(maxHp));
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Route));

            // 一時停止の状態が変わったクリックも捨てる。
            flow.Submit(I(IntentKind.Cancel));
            flow.SubmitPointer(I(IntentKind.Option, 0), AppScreen.Route, false);
            flow.Advance(0);
            Assert.That(flow.Paused, Is.True);
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Route));
            Assert.That(run.Stage, Is.EqualTo(RunStage.ChooseEncounter));
        }

        [Test]
        public void RewardRestAndResultScreensFollowTheRun()
        {
            var flow = AppLab.InBattle(0, 2);
            var run = flow.Run!;
            WinCurrentBattle(flow);
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Reward));

            flow.Apply(I(IntentKind.Number, 1));
            Assert.That(run.RewardTaken, Is.True);
            flow.Apply(I(IntentKind.Option, 0));
            Assert.That(flow.Notice, Is.EqualTo("カードはもう選んだ"));
            Assert.That(run.Deck.Count, Is.EqualTo(11));

            var strike = run.Deck.First(c => c.Def.Id == "strike");
            flow.Apply(I(IntentKind.PickRemoval, strike.Id));
            Assert.That(flow.RemovalCandidate, Is.EqualTo(strike.Id));
            flow.Apply(I(IntentKind.Cancel));
            Assert.That(flow.RemovalCandidate, Is.Null);
            Assert.That(flow.Paused, Is.False);
            flow.Apply(I(IntentKind.PickRemoval, strike.Id));
            flow.Apply(I(IntentKind.ConfirmRemoval));
            Assert.That(run.Deck, Has.No.Member(strike));
            flow.Apply(I(IntentKind.PickRemoval, run.Deck[0].Id));
            Assert.That(flow.Notice, Is.EqualTo("除去はもう使った"));

            flow.Apply(I(IntentKind.Leave));
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Rest));
            flow.Apply(I(IntentKind.Option, 1));
            Assert.That(run.MaxHp, Is.EqualTo(76));
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Route));

            flow.Apply(I(IntentKind.Option, 0));
            flow.FinishPlayback();
            run.Battle!.Player.Hp = 1;
            for (int i = 0; i < 50 && flow.Screen == AppScreen.Battle; i++)
            {
                run.Battle.Player.Hp = 1;
                flow.Apply(I(IntentKind.EndTurn));
                flow.FinishPlayback();
            }

            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Result));
            Assert.That(run.Stage, Is.EqualTo(RunStage.Defeat));
            flow.Apply(I(IntentKind.EndTurn));
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Result));
            flow.Apply(I(IntentKind.Confirm));
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Title));
        }

        [Test]
        public void ResultWaitsForTheLastPlaybackBeforeLeavingTheBattle()
        {
            var flow = AppLab.InBattle(0, 2);
            var run = flow.Run!;
            foreach (var e in run.Battle!.Enemies) { e.Hp = 1; }
            var attack = run.Battle.Hand.FirstOrDefault(c => c.Def.Type == CardType.Attack);
            Assume.That(attack, Is.Not.Null);
            for (int i = 0; i < 10 && !run.Battle.IsOver; i++)
            {
                var card = run.Battle.Hand.FirstOrDefault(c => c.Def.Type == CardType.Attack && run.Battle.Affordable(c));
                if (card == null) { break; }
                flow.Apply(I(IntentKind.SelectCard, card.Id));
                flow.Apply(I(IntentKind.ClickEnemy, run.Battle.Enemies.First(e => e.Alive).Id));
                if (!run.Battle.IsOver) { flow.FinishPlayback(); }
            }

            Assume.That(run.Battle.IsOver, Is.True);
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Battle), "勝利の演出の間は戦闘の画面のまま");
            for (int i = 0; i < 2000 && flow.Screen == AppScreen.Battle; i++) { flow.Step(); }
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Reward));
        }

        private static void WinCurrentBattle(DeckFlow flow)
        {
            var battle = flow.Run!.Battle!;
            for (int i = 0; i < 100 && flow.Screen == AppScreen.Battle; i++)
            {
                foreach (var e in battle.Enemies.Where(e => e.Alive)) { e.Hp = 1; }
                battle.Player.Hp = battle.Player.MaxHp;
                var card = battle.Hand.FirstOrDefault(c => c.Def.Type == CardType.Attack && battle.Affordable(c));
                if (card == null) { flow.Apply(I(IntentKind.EndTurn)); }
                else if (card.Def.NeedsTarget)
                {
                    flow.Apply(I(IntentKind.SelectCard, card.Id));
                    flow.Apply(I(IntentKind.ClickEnemy, battle.Enemies.First(e => e.Alive).Id));
                }
                else { flow.Apply(I(IntentKind.SelectCard, card.Id)); }

                flow.FinishPlayback();
            }
        }
    }
}
