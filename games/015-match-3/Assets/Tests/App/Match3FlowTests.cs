using System.Collections.Generic;
using Game.Core;
using NUnit.Framework;

namespace Game.App.Tests
{
    public sealed class Match3FlowTests
    {
        private const int MaxSteps = 20000;

        private static Match3Config InstantConfig() => new()
        {
            SwapTicks = 0,
            ClearTicks = 0,
            FallTicksPerRow = 0,
            ReshuffleTicks = 0,
        };

        private static Match3Flow TwoChainFlow(Match3Config? config = null)
        {
            var flow = new Match3Flow(config ?? new Match3Config(), 1);
            flow.MatchFactory = seed => new Match3Game(flow.Config, seed, DemoBoards.TwoChain());
            return flow;
        }

        private static void RunUntilIdle(Match3Flow flow)
        {
            for (var i = 0; i < MaxSteps && flow.Screen == AppScreen.Playing && !flow.Game!.AcceptsSwap; i++) { flow.StepTick(); }
        }

        [Test]
        public void StartsAtTitleAndSubmitBeginsAMatch()
        {
            var flow = new Match3Flow(new Match3Config(), 10);

            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Title));
            Assert.That(flow.Game, Is.Null);
            flow.Submit();
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Playing));
            Assert.That(flow.MatchSeed, Is.EqualTo(10));
            Assert.That(flow.Game!.MovesLeft, Is.EqualTo(new Match3Config().Moves));
        }

        [Test]
        public void PauseStopsTimeAndSubmitResumes()
        {
            var flow = TwoChainFlow();
            flow.Submit();
            flow.TogglePause();
            var ticks = flow.Game!.TickCount;
            flow.Advance(1);
            flow.StepTick();

            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Paused));
            Assert.That(flow.Game.TickCount, Is.EqualTo(ticks));
            flow.Submit();
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Playing));
            flow.TogglePause();
            flow.TogglePause();
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Playing));
        }

        [Test]
        public void SwapInputIsIgnoredOutsidePlay()
        {
            var flow = TwoChainFlow();
            flow.PointerDown(0.5, 2.5);
            flow.Move(1, 0);
            flow.ToggleSelect();
            Assert.That(flow.LastVerdict, Is.Null);

            flow.Submit();
            flow.TogglePause();
            flow.PointerDown(0.5, 2.5);
            flow.PointerDown(1.5, 2.5);
            Assert.That(flow.LastVerdict, Is.Null);
            Assert.That(flow.Input.Selected, Is.Null);
            Assert.That(flow.Game!.Phase, Is.EqualTo(GamePhase.Idle));
        }

        [Test]
        public void TwoClicksSwapThroughTheGame()
        {
            var flow = TwoChainFlow();
            flow.Submit();
            flow.PointerDown(0.5, 2.5);
            flow.PointerUp();
            flow.PointerDown(1.5, 2.5);

            Assert.That(flow.LastVerdict, Is.EqualTo(SwapVerdict.Legal));
            Assert.That(flow.Game!.Phase, Is.EqualTo(GamePhase.Swapping));
            Assert.That(flow.TakeEvents() & GameEvents.Swapped, Is.EqualTo(GameEvents.Swapped));
        }

        [Test]
        public void DragSwapsThroughTheGame()
        {
            var flow = TwoChainFlow();
            flow.Submit();
            flow.PointerDown(0.5, 2.5);
            flow.PointerMove(1.2, 2.6);

            Assert.That(flow.LastVerdict, Is.EqualTo(SwapVerdict.Legal));
        }

        [Test]
        public void KeyboardSwapsThroughTheGame()
        {
            var flow = TwoChainFlow();
            flow.Submit();
            var cursor = flow.Input.Cursor;
            for (var i = 0; i < cursor.X; i++) { flow.Move(-1, 0); }
            for (var i = 0; i < cursor.Y - 2; i++) { flow.Move(0, -1); }
            flow.ToggleSelect();
            flow.Move(1, 0);

            Assert.That(flow.LastVerdict, Is.EqualTo(SwapVerdict.Legal));
        }

        [Test]
        public void NoMatchSwapIsRevertedAndKeepsTheMove()
        {
            var flow = TwoChainFlow();
            flow.Submit();
            flow.PointerDown(4.5, 5.5);
            flow.PointerDown(5.5, 5.5);
            RunUntilIdle(flow);

            Assert.That(flow.LastVerdict, Is.EqualTo(SwapVerdict.NoMatch));
            Assert.That(flow.TakeEvents() & GameEvents.Rejected, Is.EqualTo(GameEvents.Rejected));
            Assert.That(flow.Game!.MovesLeft, Is.EqualTo(flow.Config.Moves));
            Assert.That(flow.Game.Board, Is.EqualTo(DemoBoards.TwoChain()));
        }

        // 2連鎖の入れ替えを始め、解決の演出に入った状態を作る。
        private static Match3Flow ResolvingFlow()
        {
            var flow = TwoChainFlow();
            flow.Submit();
            flow.PointerDown(0.5, 2.5);
            flow.PointerUp();
            flow.PointerDown(1.5, 2.5);
            flow.PointerUp();
            flow.StepTick();
            Assert.That(flow.Game!.AcceptsSwap, Is.False);
            return flow;
        }

        // 解決を終えた後、(3,3) の隣の (4,3) をクリックしても入れ替えにならず、そこが新しく選ばれるだけであること。
        private static void AssertNothingCarriedOver(Match3Flow flow)
        {
            Assert.That(flow.Input.Selected, Is.Null, "演出中の選択");
            RunUntilIdle(flow);
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Playing));
            Assert.That(flow.Input.Selected, Is.Null, "解決後の選択");
            var moves = flow.Game!.MovesLeft;
            var verdict = flow.LastVerdict;
            flow.PointerDown(4.5, 3.5);
            flow.PointerUp();

            Assert.That(flow.LastVerdict, Is.EqualTo(verdict), "演出中の操作と組み合わせた入れ替え");
            Assert.That(flow.Game.Phase, Is.EqualTo(GamePhase.Idle));
            Assert.That(flow.Game.MovesLeft, Is.EqualTo(moves));
            Assert.That(flow.Input.Selected, Is.EqualTo(new Point(4, 3)));
        }

        [Test]
        public void SwapDuringResolutionIsDropped()
        {
            var flow = ResolvingFlow();
            flow.PointerDown(4.5, 5.5);
            flow.PointerDown(5.5, 5.5);

            Assert.That(flow.LastVerdict, Is.EqualTo(SwapVerdict.Legal), "解決中の入れ替えはゲームへ渡さない");
            RunUntilIdle(flow);
            Assert.That(flow.Game!.MovesLeft, Is.EqualTo(flow.Config.Moves - 1));
        }

        [Test]
        public void ClickDuringResolutionDoesNotStaySelected()
        {
            var flow = ResolvingFlow();
            flow.PointerDown(3.5, 3.5);
            flow.PointerUp();

            AssertNothingCarriedOver(flow);
        }

        [Test]
        public void PressHeldFromResolutionDoesNotDragAfterIt()
        {
            var flow = ResolvingFlow();
            flow.PointerDown(3.5, 3.5);
            RunUntilIdle(flow);
            flow.PointerMove(4.4, 3.5);
            Assert.That(flow.Game!.Phase, Is.EqualTo(GamePhase.Idle), "演出中に押したマスからのドラッグ");
            flow.PointerUp();

            AssertNothingCarriedOver(flow);
        }

        [Test]
        public void KeyboardSelectDuringResolutionIsDropped()
        {
            var flow = ResolvingFlow();
            var cursor = flow.Input.Cursor;
            flow.ToggleSelect();
            flow.Move(1, 0);

            Assert.That(flow.Input.Cursor, Is.EqualTo(cursor), "解決中はカーソルも動かさない");
            AssertNothingCarriedOver(flow);
        }

        [Test]
        public void GameOverShowsTheResultAndRestartUsesANewSeed()
        {
            var config = InstantConfig();
            config.Moves = 1;
            config.TargetScore = 10_000_000;
            var flow = TwoChainFlow(config);
            flow.Submit();
            var firstSeed = flow.MatchSeed;
            flow.PointerDown(0.5, 2.5);
            flow.PointerDown(1.5, 2.5);
            flow.StepTick();

            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Result));
            Assert.That(flow.Game!.Phase, Is.EqualTo(GamePhase.Lost));
            flow.PointerDown(0.5, 2.5);
            Assert.That(flow.Input.Selected, Is.Null, "結果画面では盤面を操作できない");
            flow.Restart();
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Playing));
            Assert.That(flow.MatchSeed, Is.Not.EqualTo(firstSeed));
            Assert.That(flow.Game!.MovesLeft, Is.EqualTo(1));
        }

        [Test]
        public void SeedsNeverRepeatAcrossRestarts()
        {
            var flow = new Match3Flow(new Match3Config(), int.MaxValue - 1);
            var seeds = new HashSet<int>();
            flow.Submit();
            seeds.Add(flow.MatchSeed);
            for (var i = 0; i < 5; i++)
            {
                flow.TogglePause();
                flow.Restart();
                Assert.That(seeds.Add(flow.MatchSeed), Is.True);
            }
        }

        [Test]
        public void BackToTitleOnlyFromPauseOrResult()
        {
            var flow = TwoChainFlow();
            flow.BackToTitle();
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Title));
            flow.Submit();
            flow.BackToTitle();
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Playing));
            flow.Restart();
            Assert.That(flow.MatchSeed, Is.EqualTo(1), "プレイ中のやり直しは受け付けない");

            flow.TogglePause();
            flow.BackToTitle();
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Title));
            Assert.That(flow.Game, Is.Null);
        }

        [Test]
        public void LosingFocusPausesAndDropsTheSelection()
        {
            var flow = TwoChainFlow();
            flow.Submit();
            flow.PointerDown(0.5, 2.5);
            flow.SetFocus(false);

            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Paused));
            Assert.That(flow.Input.Selected, Is.Null);
            flow.Submit();
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Paused), "フォーカスがない間は再開しない");
            flow.SetFocus(true);
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Paused), "戻っても自動では再開しない");
            flow.Submit();
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Playing));
        }

        [Test]
        public void FocusLossWhileDraggingDoesNotSwapAfterReturn()
        {
            var flow = TwoChainFlow();
            flow.Submit();
            flow.PointerDown(0.5, 2.5);
            flow.SetFocus(false);
            flow.SetFocus(true);
            flow.Submit();
            flow.PointerMove(1.5, 2.5);

            Assert.That(flow.LastVerdict, Is.Null);
        }

        [Test]
        public void AdvanceConvertsSecondsToTicks()
        {
            var flow = TwoChainFlow();
            flow.Submit();
            for (var i = 0; i < 60; i++) { flow.Advance(1.0 / 60); }
            Assert.That(flow.Game!.TickCount, Is.EqualTo(60));

            for (var i = 0; i < 100; i++) { flow.Advance(0.01); }
            Assert.That(flow.Game.TickCount, Is.EqualTo(120));

            for (var i = 0; i < 20; i++) { flow.Advance(0.05); }
            Assert.That(flow.Game.TickCount, Is.EqualTo(180));
        }

        [Test]
        public void AdvanceIsCappedAndRejectsBadValues()
        {
            var flow = TwoChainFlow();
            flow.Submit();
            flow.Advance(10);
            Assert.That(flow.Game!.TickCount, Is.EqualTo(Match3Flow.MaxTicksPerFrame));

            flow.Advance(double.NaN);
            flow.Advance(double.PositiveInfinity);
            flow.Advance(-1);
            flow.Advance(0);
            Assert.That(flow.Game.TickCount, Is.EqualTo(Match3Flow.MaxTicksPerFrame), "有限でない値と0以下は無視する");

            flow.Advance(double.MaxValue);
            Assert.That(flow.Game.TickCount, Is.EqualTo(Match3Flow.MaxTicksPerFrame * 2), "Tick に直すと桁あふれする値も1フレームの上限まで");
        }

        [Test]
        public void AutopilotFinishesEveryMatch()
        {
            for (var seed = 0; seed < 50; seed++)
            {
                var flow = new Match3Flow(InstantConfig(), seed);
                var pilot = new Autopilot(seed);
                flow.Submit();
                for (var i = 0; i < MaxSteps && flow.Screen == AppScreen.Playing; i++)
                {
                    if (flow.Game!.AcceptsSwap)
                    {
                        var swap = pilot.Choose(flow.Game.Board);
                        flow.PointerDown(swap.From.X + 0.5, swap.From.Y + 0.5);
                        flow.PointerDown(swap.To.X + 0.5, swap.To.Y + 0.5);
                        Assert.That(flow.LastVerdict, Is.EqualTo(SwapVerdict.Legal));
                    }

                    flow.StepTick();
                }

                Assert.That(flow.Screen, Is.EqualTo(AppScreen.Result), $"seed {seed}");
                Assert.That(flow.Game!.IsOver, Is.True, $"seed {seed}");
            }
        }
    }
}
