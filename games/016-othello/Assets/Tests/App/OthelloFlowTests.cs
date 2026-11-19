using System.Collections.Generic;
using System.Linq;
using Game.Core;
using NUnit.Framework;

namespace Game.App.Tests
{
    public sealed class OthelloFlowTests
    {
        private const int MaxSteps = 50_000;

        private static OthelloConfig InstantConfig() => new()
        {
            FlipTicks = 0,
            PassTicks = 0,
            CpuThinkTicks = 0,
        };

        private static Square S(string name) => Square.Parse(name);

        /// <summary>マスの中心をクリックする。</summary>
        private static void Click(OthelloFlow flow, string name)
        {
            var square = S(name);
            flow.PointerDown(square.X + 0.5, square.Y + 0.5);
        }

        /// <summary>プレイヤーの手番か結果画面まで進める。</summary>
        private static void RunUntilPlayerTurn(OthelloFlow flow)
        {
            for (var i = 0; i < MaxSteps && flow.Screen == AppScreen.Playing && !flow.IsPlayerTurn; i++) { flow.StepTick(); }
        }

        /// <summary>黒の手番だが黒は打てず、白は c1 に打てる局面。白が c1 に打つと黒の石がなくなり終局する。</summary>
        private static Position BlackMustPass() => BoardText.Parse(Side.Black,
            "WB......",
            "........",
            "........",
            "........",
            "........",
            "........",
            "........",
            "........");

        [Test]
        public void StartsAtTitleWithNormalLevelAndBlack()
        {
            var flow = new OthelloFlow(new OthelloConfig(), 1);
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Title));
            Assert.That(flow.Game, Is.Null);
            Assert.That(flow.Level, Is.EqualTo(CpuLevel.Normal));
            Assert.That(flow.PlayerSide, Is.EqualTo(Side.Black));
        }

        [Test]
        public void SubmitStartsAMatchWithTheChosenOptions()
        {
            var flow = new OthelloFlow(new OthelloConfig(), 1);
            flow.SelectLevel(CpuLevel.Strong);
            flow.SelectSide(Side.White);
            flow.Submit();
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Playing));
            Assert.That(flow.Cpu!.Side, Is.EqualTo(Side.Black));
            Assert.That(flow.Cpu.Depth, Is.EqualTo(5));
            Assert.That(flow.IsCpuTurn, Is.True);
            Assert.That(flow.IsPlayerTurn, Is.False);
        }

        [TestCase(CpuLevel.Weak, 1)]
        [TestCase(CpuLevel.Normal, 3)]
        [TestCase(CpuLevel.Strong, 5)]
        public void LevelSelectsTheSearchDepth(CpuLevel level, int depth)
        {
            var flow = new OthelloFlow(new OthelloConfig(), 1);
            flow.SelectLevel(level);
            flow.Submit();
            Assert.That(flow.Cpu!.Depth, Is.EqualTo(depth));
        }

        [Test]
        public void LevelShiftStopsAtTheEnds()
        {
            var flow = new OthelloFlow(new OthelloConfig(), 1);
            flow.ShiftLevel(1);
            flow.ShiftLevel(1);
            Assert.That(flow.Level, Is.EqualTo(CpuLevel.Strong));
            flow.ShiftLevel(-1);
            flow.ShiftLevel(-1);
            flow.ShiftLevel(-1);
            Assert.That(flow.Level, Is.EqualTo(CpuLevel.Weak));
        }

        [Test]
        public void OptionsCannotChangeDuringAMatch()
        {
            var flow = new OthelloFlow(new OthelloConfig(), 1);
            flow.Submit();
            flow.SelectLevel(CpuLevel.Weak);
            flow.ShiftLevel(1);
            flow.ToggleSide();
            Assert.That(flow.Level, Is.EqualTo(CpuLevel.Normal));
            Assert.That(flow.PlayerSide, Is.EqualTo(Side.Black));
            flow.TogglePause();
            flow.ToggleSide();
            Assert.That(flow.PlayerSide, Is.EqualTo(Side.Black), "一時停止中も変えない");
        }

        [Test]
        public void ClickPlaysThePlayersMoveAndTheCpuAnswers()
        {
            var flow = new OthelloFlow(new OthelloConfig(), 1);
            flow.Submit();
            Click(flow, "d3");
            Assert.That(flow.LastVerdict, Is.EqualTo(PlayVerdict.Ok));
            Assert.That(flow.Game!.LastMove, Is.EqualTo(S("d3")));
            Assert.That(flow.TakeEvents(), Is.EqualTo(FlowEvents.Placed));
            Assert.That(flow.IsPlayerTurn, Is.False);

            RunUntilPlayerTurn(flow);
            Assert.That(flow.Game.ToMove, Is.EqualTo(Side.Black));
            Assert.That(flow.Game.MovesPlayed, Is.EqualTo(2));
            Assert.That(flow.Game.Position.At(flow.Game.LastMove!.Value), Is.EqualTo(Disc.White));
        }

        [Test]
        public void CpuWaitsTheThinkTicksBeforePlaying()
        {
            var config = new OthelloConfig { FlipTicks = 0, CpuThinkTicks = 4 };
            var flow = new OthelloFlow(config, 1);
            flow.SelectSide(Side.White);
            flow.Submit();
            for (var i = 0; i < 4; i++)
            {
                flow.StepTick();
                Assert.That(flow.Game!.MovesPlayed, Is.EqualTo(0), $"tick {i}");
                Assert.That(flow.Cpu!.PlannedMove, Is.Not.Null, "最初の Tick で手を決める");
            }

            flow.StepTick();
            Assert.That(flow.Game!.MovesPlayed, Is.EqualTo(1));
            Assert.That(flow.Cpu!.PlannedMove, Is.Null);
            Assert.That(flow.IsPlayerTurn, Is.True);
        }

        [Test]
        public void IllegalClickIsRejectedAndChangesNothing()
        {
            var flow = new OthelloFlow(new OthelloConfig(), 1);
            flow.Submit();
            flow.TakeEvents();
            Click(flow, "a1");
            Assert.That(flow.LastVerdict, Is.EqualTo(PlayVerdict.NoFlip));
            Assert.That(flow.TakeEvents(), Is.EqualTo(FlowEvents.Rejected));
            Assert.That(flow.Game!.Position, Is.EqualTo(Position.Initial));
            Assert.That(flow.IsPlayerTurn, Is.True);

            Click(flow, "d4");
            Assert.That(flow.LastVerdict, Is.EqualTo(PlayVerdict.Occupied));
        }

        [TestCase(-0.5, 3.5)]
        [TestCase(8.0, 3.5)]
        [TestCase(3.5, -0.01)]
        [TestCase(3.5, double.NaN)]
        public void ClicksOutsideTheBoardAreIgnored(double column, double row)
        {
            var flow = new OthelloFlow(new OthelloConfig(), 1);
            flow.Submit();
            flow.PointerDown(column, row);
            Assert.That(flow.LastVerdict, Is.Null);
            Assert.That(flow.Cursor.Position, Is.EqualTo(BoardCursor.Home));
            Assert.That(flow.TakeEvents(), Is.EqualTo(FlowEvents.None));
        }

        [Test]
        public void KeyboardMovesTheCursorAndPlaces()
        {
            var flow = new OthelloFlow(new OthelloConfig(), 1);
            flow.Submit();
            flow.MoveCursor(0, -1);
            Assert.That(flow.Cursor.Position, Is.EqualTo(S("d3")));
            flow.Submit();
            Assert.That(flow.Game!.LastMove, Is.EqualTo(S("d3")));
        }

        [Test]
        public void CursorIsShownForKeysAndHiddenForTheMouse()
        {
            var flow = new OthelloFlow(new OthelloConfig { FlipTicks = 0, CpuThinkTicks = 0 }, 1);
            flow.Submit();
            Assert.That(flow.Cursor.IsActive, Is.False);
            flow.MoveCursor(1, 0);
            Assert.That(flow.Cursor.IsActive, Is.True);
            Click(flow, "d3");
            Assert.That(flow.Cursor.IsActive, Is.False);
            Assert.That(flow.Cursor.Position, Is.EqualTo(S("d3")));
        }

        [Test]
        public void NavigateChoosesOptionsOnTheTitleAndMovesTheCursorInPlay()
        {
            var flow = new OthelloFlow(new OthelloConfig(), 1);
            flow.Navigate(1, 0);
            Assert.That(flow.Level, Is.EqualTo(CpuLevel.Strong));
            flow.Navigate(0, 1);
            Assert.That(flow.PlayerSide, Is.EqualTo(Side.White));
            flow.Navigate(0, -1);
            Assert.That(flow.PlayerSide, Is.EqualTo(Side.Black));

            flow.Submit();
            flow.Navigate(1, 0);
            Assert.That(flow.Level, Is.EqualTo(CpuLevel.Strong));
            Assert.That(flow.Cursor.Position, Is.EqualTo(S("e4")));
        }

        [Test]
        public void CursorStaysOnTheBoard()
        {
            var flow = new OthelloFlow(new OthelloConfig(), 1);
            flow.Submit();
            for (var i = 0; i < 10; i++) { flow.MoveCursor(-1, -1); }
            Assert.That(flow.Cursor.Position, Is.EqualTo(S("a1")));
            for (var i = 0; i < 10; i++) { flow.MoveCursor(1, 1); }
            Assert.That(flow.Cursor.Position, Is.EqualTo(S("h8")));
        }

        [Test]
        public void ClicksDuringTheCpuTurnAreDropped()
        {
            var flow = new OthelloFlow(new OthelloConfig { FlipTicks = 0, CpuThinkTicks = 10 }, 1);
            flow.SelectSide(Side.White);
            flow.Submit();
            flow.StepTick();
            Click(flow, "c5");
            flow.PlaceAtCursor();
            Assert.That(flow.LastVerdict, Is.Null, "CPU の手番の操作は Core へ渡さない");
            Assert.That(flow.Game!.MovesPlayed, Is.EqualTo(0));

            RunUntilPlayerTurn(flow);
            Assert.That(flow.Game.MovesPlayed, Is.EqualTo(1), "持ち越した操作で打っていない");
        }

        [Test]
        public void ClicksDuringTheFlipAnimationAreDropped()
        {
            var flow = new OthelloFlow(new OthelloConfig { FlipTicks = 10, CpuThinkTicks = 0 }, 1);
            flow.Submit();
            Click(flow, "d3");
            flow.TakeEvents();
            Click(flow, "c3");
            Assert.That(flow.LastVerdict, Is.EqualTo(PlayVerdict.Ok), "演出中のクリックは判定もしない");
            Assert.That(flow.TakeEvents(), Is.EqualTo(FlowEvents.None));
            Assert.That(flow.Game!.MovesPlayed, Is.EqualTo(1));
        }

        [Test]
        public void PauseStopsTheCpuAndSubmitResumes()
        {
            var flow = new OthelloFlow(new OthelloConfig { FlipTicks = 0, CpuThinkTicks = 0 }, 1);
            flow.SelectSide(Side.White);
            flow.Submit();
            flow.TogglePause();
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Paused));
            flow.StepTick();
            flow.Advance(1);
            Assert.That(flow.Game!.MovesPlayed, Is.EqualTo(0));
            Assert.That(flow.Game.TickCount, Is.EqualTo(0));

            flow.Submit();
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Playing));
            flow.StepTick();
            Assert.That(flow.Game.MovesPlayed, Is.EqualTo(1));
        }

        [Test]
        public void ClicksWhilePausedAreIgnored()
        {
            var flow = new OthelloFlow(new OthelloConfig(), 1);
            flow.Submit();
            flow.TogglePause();
            Click(flow, "d3");
            flow.MoveCursor(1, 0);
            Assert.That(flow.Game!.MovesPlayed, Is.EqualTo(0));
            Assert.That(flow.Cursor.Position, Is.EqualTo(BoardCursor.Home));
        }

        [Test]
        public void LosingFocusPausesAndBlocksInputUntilItReturns()
        {
            var flow = new OthelloFlow(new OthelloConfig(), 1);
            flow.Submit();
            flow.SetFocus(false);
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Paused));
            flow.Submit();
            flow.TogglePause();
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Paused), "フォーカスがない間は再開しない");

            flow.SetFocus(true);
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Paused), "戻っても自動では再開しない");
            flow.Submit();
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Playing));
        }

        [Test]
        public void PauseThenFocusLossInTheSameFrameStaysPaused()
        {
            var flow = new OthelloFlow(new OthelloConfig(), 1);
            flow.Submit();
            flow.TogglePause();
            flow.SetFocus(false);
            flow.TogglePause();
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Paused));
        }

        [Test]
        public void PassIsReportedAndTheGameContinues()
        {
            var flow = new OthelloFlow(InstantConfig(), 1);
            flow.SelectSide(Side.White);
            flow.GameFactory = _ => new OthelloGame(flow.Config, BlackMustPass());
            flow.Submit();
            Assert.That(flow.TakeEvents(), Is.EqualTo(FlowEvents.Passed));
            Assert.That(flow.IsPlayerTurn, Is.True, "黒（CPU）がパスして白の手番");

            Click(flow, "c1");
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Result));
            Assert.That(flow.Outcome, Is.EqualTo(Outcome.Win));
            Assert.That(flow.TakeEvents(), Is.EqualTo(FlowEvents.Placed | FlowEvents.GameOver));
        }

        [Test]
        public void ResultScreenIgnoresBoardInputAndAllowsNewOptions()
        {
            var flow = new OthelloFlow(InstantConfig(), 1);
            flow.SelectSide(Side.White);
            flow.GameFactory = _ => new OthelloGame(flow.Config, BlackMustPass());
            flow.Submit();
            Click(flow, "c1");
            var finalPosition = flow.Game!.Position;

            Click(flow, "a2");
            flow.PlaceAtCursor();
            flow.StepTick();
            Assert.That(flow.Game.Position, Is.EqualTo(finalPosition));

            flow.SelectLevel(CpuLevel.Weak);
            flow.SelectSide(Side.Black);
            Assert.That(flow.Level, Is.EqualTo(CpuLevel.Weak));
            Assert.That(flow.PlayerSide, Is.EqualTo(Side.Black));

            // 選び直しは次の試合のもの。終わった試合の色・強さ・勝敗は変わらない。
            Assert.That(flow.MatchPlayerSide, Is.EqualTo(Side.White));
            Assert.That(flow.MatchLevel, Is.EqualTo(CpuLevel.Normal));
            Assert.That(flow.Outcome, Is.EqualTo(Outcome.Win));

            flow.Submit();
            Assert.That(flow.MatchPlayerSide, Is.EqualTo(Side.Black));
            Assert.That(flow.Cpu!.Depth, Is.EqualTo(1));
        }

        [Test]
        public void RestartUsesANewSeedEveryTime()
        {
            var flow = new OthelloFlow(new OthelloConfig(), 5);
            flow.Submit();
            var seeds = new List<int> { flow.MatchSeed };
            for (var i = 0; i < 5; i++)
            {
                flow.TogglePause();
                flow.Restart();
                Assert.That(flow.Screen, Is.EqualTo(AppScreen.Playing));
                Assert.That(flow.Game!.Position, Is.EqualTo(Position.Initial));
                seeds.Add(flow.MatchSeed);
            }

            Assert.That(seeds.Distinct().Count(), Is.EqualTo(seeds.Count));
        }

        [Test]
        public void RestartAndBackToTitleOnlyFromPauseOrResult()
        {
            var flow = new OthelloFlow(new OthelloConfig(), 1);
            flow.Restart();
            flow.BackToTitle();
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Title));

            flow.Submit();
            var seed = flow.MatchSeed;
            flow.Restart();
            flow.BackToTitle();
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Playing));
            Assert.That(flow.MatchSeed, Is.EqualTo(seed));

            flow.TogglePause();
            flow.BackToTitle();
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Title));
            Assert.That(flow.Game, Is.Null);
        }

        [Test]
        public void AdvanceConvertsSecondsToTicks()
        {
            var flow = new OthelloFlow(new OthelloConfig(), 1);
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
            var flow = new OthelloFlow(new OthelloConfig(), 1);
            flow.Submit();
            flow.Advance(10);
            Assert.That(flow.Game!.TickCount, Is.EqualTo(OthelloFlow.MaxTicksPerFrame));

            flow.Advance(double.NaN);
            flow.Advance(double.PositiveInfinity);
            flow.Advance(-1);
            flow.Advance(0);
            Assert.That(flow.Game.TickCount, Is.EqualTo(OthelloFlow.MaxTicksPerFrame), "有限でない値と0以下は無視する");

            flow.Advance(double.MaxValue);
            Assert.That(flow.Game.TickCount, Is.EqualTo(OthelloFlow.MaxTicksPerFrame * 2), "Tick に直すと桁あふれする値も1フレームの上限まで");
        }

        [Test]
        public void CpuMatchesFinishForEveryLevelAndSide()
        {
            // CPU対戦の完走：プレイヤー側も別の CPU（深さ2）が同じクリックの入口から打ち、結果画面まで進む。
            foreach (var level in new[] { CpuLevel.Weak, CpuLevel.Normal, CpuLevel.Strong })
            {
                foreach (var side in new[] { Side.Black, Side.White })
                {
                    for (var seed = 0; seed < 3; seed++)
                    {
                        var flow = new OthelloFlow(InstantConfig(), seed);
                        flow.SelectLevel(level);
                        flow.SelectSide(side);
                        flow.Submit();
                        PlayToTheEnd(flow, new SeededRandom(seed + 100));
                        Assert.That(flow.Screen, Is.EqualTo(AppScreen.Result), $"{level} {side} {seed}");
                        Assert.That(flow.Game!.Position.IsTerminal, Is.True);
                        var expected = flow.Game.Count(side).CompareTo(flow.Game.Count(side.Opponent())) switch
                        {
                            > 0 => Outcome.Win,
                            < 0 => Outcome.Loss,
                            _ => Outcome.Draw,
                        };
                        Assert.That(flow.Outcome, Is.EqualTo(expected));
                    }
                }
            }
        }

        [Test]
        public void StrongerCpuBeatsTheWeakestMoreOften()
        {
            // 深さの選択が強さに効いていること。プレイヤー側に深さ1の CPU を置き、強い CPU の勝ち越しを見る。
            var strongWins = 0;
            const int games = 10;
            for (var seed = 0; seed < games; seed++)
            {
                var flow = new OthelloFlow(InstantConfig(), seed);
                flow.SelectLevel(CpuLevel.Strong);
                flow.SelectSide(seed % 2 == 0 ? Side.Black : Side.White);
                flow.Submit();
                PlayToTheEnd(flow, new SeededRandom(seed + 500), playerDepth: 1);
                if (flow.Outcome == Outcome.Loss) { strongWins++; }
            }

            Assert.That(strongWins, Is.GreaterThan(games / 2));
        }

        [Test]
        public void SameSeedAndMovesReplayTheSameMatch()
        {
            Position Play()
            {
                var flow = new OthelloFlow(InstantConfig(), 42);
                flow.Submit();
                PlayToTheEnd(flow, new SeededRandom(7));
                return flow.Game!.Position;
            }

            Assert.That(Play(), Is.EqualTo(Play()));
        }

        // プレイヤーの手番ではクリックで打ち、それ以外は Tick を進めて、結果画面まで遊ぶ。
        private static void PlayToTheEnd(OthelloFlow flow, SeededRandom random, int playerDepth = 2)
        {
            for (var i = 0; i < MaxSteps && flow.Screen == AppScreen.Playing; i++)
            {
                if (flow.IsPlayerTurn)
                {
                    var move = Searcher.Search(flow.Game!.Position, playerDepth, random).Move;
                    flow.PointerDown(move.X + 0.5, move.Y + 0.5);
                    Assert.That(flow.LastVerdict, Is.EqualTo(PlayVerdict.Ok));
                }
                else
                {
                    flow.StepTick();
                }
            }
        }
    }
}
