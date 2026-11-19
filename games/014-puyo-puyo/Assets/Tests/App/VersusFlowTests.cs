using System;
using Game.Core;
using NUnit.Framework;
using static Game.App.Tests.AppTestKit;

namespace Game.App.Tests
{
    public sealed class VersusFlowTests
    {
        [Test]
        public void タイトルから試合を始められる()
        {
            var flow = NewFlow();
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Title));
            Assert.That(flow.Match, Is.Null);
            flow.Submit();
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Playing));
            Assert.That(flow.Match, Is.Not.Null);
        }

        [Test]
        public void 負けると結果画面になり再戦できる()
        {
            var flow = FlowAboutToLose();
            PlayToResult(flow);
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Result));
            Assert.That(flow.Match!.Outcome, Is.EqualTo(MatchOutcome.CpuWin));

            var seed = flow.MatchSeed;
            flow.Submit();
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Playing));
            Assert.That(flow.MatchSeed, Is.Not.EqualTo(seed), "再戦は新しいシード");
        }

        [Test]
        public void 結果画面の操作は試合に届かない()
        {
            var flow = FlowAboutToLose();
            PlayToResult(flow);
            Assert.That(flow.Perform(GameCommand.MoveLeft), Is.False);
            var ticks = flow.Match!.Player.TickCount;
            flow.StepTick();
            flow.Advance(1);
            Assert.That(flow.Match.Player.TickCount, Is.EqualTo(ticks));
        }

        [Test]
        public void 一時停止中は時間も操作も進まない()
        {
            var flow = NewFlow();
            flow.Submit();
            flow.TogglePause();
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Paused));
            var active = flow.Match!.Player.Active;
            Assert.That(flow.Perform(GameCommand.MoveLeft), Is.False);
            flow.Advance(1);
            Assert.That(flow.Match.Player.TickCount, Is.EqualTo(0));
            Assert.That(flow.Match.Player.Active, Is.EqualTo(active));

            flow.TogglePause();
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Playing));
        }

        [Test]
        public void やり直しとタイトルへは一時停止中と結果画面でだけ受け付ける()
        {
            var flow = NewFlow();
            flow.Restart();
            flow.BackToTitle();
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Title), "タイトルでは何も起きない");

            flow.Submit();
            var match = flow.Match;
            flow.Restart();
            flow.BackToTitle();
            Assert.That(flow.Match, Is.SameAs(match), "プレイ中は受け付けない");

            flow.TogglePause();
            flow.Restart();
            Assert.That(flow.Match, Is.Not.SameAs(match), "一時停止中はやり直せる");
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Playing));

            flow.TogglePause();
            flow.BackToTitle();
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Title));
            Assert.That(flow.Match, Is.Null);
        }

        [Test]
        public void フォーカスを失うと一時停止し戻っても自動で再開しない()
        {
            var flow = NewFlow();
            flow.Submit();
            flow.SetFocus(false);
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Paused));
            flow.Submit();
            flow.TogglePause();
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Paused), "フォーカスがない間の入力は無視する");

            flow.SetFocus(true);
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Paused));
            flow.Submit();
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Playing));
        }

        [Test]
        public void 長押しは開始遅延の後に繰り返す()
        {
            var flow = NewFlow();
            flow.Submit();
            flow.Press(PlayerAction.Left);
            var field = flow.Match!.Player;
            Assert.That(field.Active!.Value.X, Is.EqualTo(Board.SpawnX - 1), "押した瞬間に1マス動く");

            for (var i = 0; i < RepeatDelay - 1; i++) { flow.StepTick(); }
            Assert.That(field.Active!.Value.X, Is.EqualTo(Board.SpawnX - 1));
            flow.StepTick();
            Assert.That(field.Active!.Value.X, Is.EqualTo(Board.SpawnX - 2));
        }

        [Test]
        public void 離すと繰り返しが止まる()
        {
            var flow = NewFlow();
            flow.Submit();
            flow.Press(PlayerAction.Right);
            flow.Release(PlayerAction.Right);
            for (var i = 0; i < RepeatDelay * 2; i++) { flow.StepTick(); }
            Assert.That(flow.Match!.Player.Active!.Value.X, Is.EqualTo(Board.SpawnX + 1));
        }

        [Test]
        public void 押下フレームの経過時間を長押しに数えない()
        {
            var flow = NewFlow();
            flow.Submit();
            flow.Press(PlayerAction.Left);

            // 処理落ちで長いフレームになっても、押した1回が2手にならない。
            flow.Advance(TickSeconds * VersusFlow.MaxTicksPerFrame * 4);
            Assert.That(flow.Match!.Player.Active!.Value.X, Is.EqualTo(Board.SpawnX - 1));
        }

        [Test]
        public void 一時停止をはさむと押しっぱなしを持ち越さない()
        {
            var flow = NewFlow();
            flow.Submit();
            flow.Press(PlayerAction.Left);
            flow.TogglePause();
            flow.TogglePause();
            for (var i = 0; i < RepeatDelay * 2; i++) { flow.StepTick(); }
            Assert.That(flow.Match!.Player.Active!.Value.X, Is.EqualTo(Board.SpawnX - 1));
        }

        [Test]
        public void 一フレームに進むTick数には上限がある()
        {
            var flow = NewFlow();
            flow.Submit();
            flow.Advance(100);
            Assert.That(flow.Match!.Player.TickCount, Is.EqualTo(VersusFlow.MaxTicksPerFrame));
        }

        [TestCase(double.NaN)]
        [TestCase(double.PositiveInfinity)]
        [TestCase(-1.0)]
        [TestCase(0.0)]
        public void 不正な経過時間では進まない(double seconds)
        {
            var flow = NewFlow();
            flow.Submit();
            flow.Advance(seconds);
            Assert.That(flow.Match!.Player.TickCount, Is.EqualTo(0));
        }

        [Test]
        public void 小さい時間の積み重ねでもTickを取りこぼさない()
        {
            var flow = NewFlow();
            flow.Submit();
            for (var i = 0; i < 6000; i++) { flow.Advance(0.001); }
            Assert.That(flow.Match!.Player.TickCount, Is.EqualTo(360).Within(1));
        }

        [Test]
        public void 不正なCPU設定は起動時に拒否する()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new VersusFlow(new SporeConfig(), 0, 0, RepeatDelay, RepeatInterval, 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => new VersusFlow(new SporeConfig(), 1, 101, RepeatDelay, RepeatInterval, 1));
        }
    }
}
