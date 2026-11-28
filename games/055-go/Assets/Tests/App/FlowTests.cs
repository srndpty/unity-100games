using System;
using Game.Core;
using NUnit.Framework;

namespace Game.App.Tests
{
    /// <summary>画面の流れ：タイトル → 対局 → 終局 → 死石の確定 → 結果 → 再戦。拒否・捨てる入力・一時停止・フォーカス。</summary>
    public sealed class FlowTests
    {
        [Test]
        public void Title_StartsAMatch_WithTheDefaults()
        {
            var flow = AppLab.Flow();
            AppLab.Start(flow);
            Assert.That(flow.MatchPlayerSide, Is.EqualTo(Stone.Black));
            Assert.That(flow.CpuSide, Is.EqualTo(Stone.White));
            Assert.That(flow.MatchLevel, Is.EqualTo(CpuLevel.Normal));
            Assert.That(flow.IsPlayerTurn, Is.True);
            Assert.That(flow.Game!.MoveCount, Is.EqualTo(0));
        }

        [Test]
        public void Title_ChoosesLevelAndSide()
        {
            var flow = AppLab.Flow();
            flow.Press(InputButton.Down); // 強さ
            flow.Press(InputButton.Left);
            flow.Press(InputButton.Left); // 端で止まる
            flow.FlushIntents();
            Assert.That(flow.Level, Is.EqualTo(CpuLevel.Weak));
            flow.Press(InputButton.Confirm); // 決定は 1 段ずつ回る
            flow.FlushIntents();
            Assert.That(flow.Level, Is.EqualTo(CpuLevel.Normal));
            flow.Press(InputButton.Down); // 手番
            flow.Press(InputButton.Right);
            flow.FlushIntents();
            Assert.That(flow.PlayerSide, Is.EqualTo(Stone.White));
            flow.Click(GoFlow.TitleStart);
            flow.FlushIntents();
            Assert.That(flow.MatchPlayerSide, Is.EqualTo(Stone.White));
            Assert.That(flow.IsCpuThinking, Is.True, "白を選ぶと CPU（黒）が先に打つ");
            AppLab.UntilPlayerTurn(flow);
            Assert.That(flow.Game!.MoveCount, Is.EqualTo(1));
        }

        [Test]
        public void HowTo_AndQuit()
        {
            var flow = AppLab.Flow();
            flow.Click(GoFlow.TitleHowTo);
            flow.FlushIntents();
            Assert.That(flow.Screen, Is.EqualTo(FlowScreen.HowTo));
            flow.Press(InputButton.Cancel);
            flow.FlushIntents();
            Assert.That(flow.Screen, Is.EqualTo(FlowScreen.Title));
            flow.Click(GoFlow.TitleQuit);
            flow.FlushIntents();
            Assert.That(flow.QuitRequested, Is.True);
        }

        [Test]
        public void Click_PlacesAStone_ThenTheCpuReplies()
        {
            var flow = AppLab.Flow();
            AppLab.Start(flow);
            flow.Click(Points.Parse("E5"));
            flow.FlushIntents();
            Assert.That(flow.Game![Points.Parse("E5")], Is.EqualTo(Stone.Black));
            Assert.That(flow.Cues.Place, Is.EqualTo(1));
            Assert.That(flow.IsCpuThinking, Is.True);
            AppLab.UntilPlayerTurn(flow);
            Assert.That(flow.Game.MoveCount, Is.EqualTo(2));
            Assert.That(flow.Cues.Place, Is.EqualTo(2));
            Assert.That(flow.Game.StoneCount(Stone.White), Is.EqualTo(1));
        }

        [Test]
        public void MovesDuringTheAnimationAndTheCpuTurn_AreDiscarded()
        {
            var flow = AppLab.Flow();
            AppLab.Start(flow);
            flow.Click(Points.Parse("E5"));
            flow.FlushIntents();

            // 置いた直後（演出中・CPU の手番）のクリックと決定は捨てる。カーソルは動く。
            flow.Click(Points.Parse("C3"));
            flow.Press(InputButton.Left);
            flow.Press(InputButton.Confirm);
            flow.Press(InputButton.Pass);
            flow.StepTicks(1);
            Assert.That(flow.Game!.MoveCount, Is.EqualTo(1));
            Assert.That(flow.Game[Points.Parse("C3")], Is.EqualTo(Stone.Empty));
            Assert.That(flow.Cursor.Point, Is.EqualTo(Points.Parse("B3")), "カーソルはクリックの点（C3）から左へ動く");
            Assert.That(flow.Cursor.Visible, Is.True);
            AppLab.UntilPlayerTurn(flow);
            Assert.That(flow.Game.MoveCount, Is.EqualTo(2), "捨てた入力は先行入力にならない");
        }

        [Test]
        public void ClickFromAnOlderVersion_IsIgnored()
        {
            var flow = AppLab.Flow();
            AppLab.Start(flow);
            var stale = flow.Version;
            flow.Press(InputButton.Confirm); // カーソル（天元）に打つ
            flow.FlushIntents();
            AppLab.UntilPlayerTurn(flow);
            var moves = flow.Game!.MoveCount;
            flow.Enqueue(Intent.Click(Points.Parse("C3"), stale));
            flow.FlushIntents();
            Assert.That(flow.Game.MoveCount, Is.EqualTo(moves));
            Assert.That(flow.Game[Points.Parse("C3")], Is.EqualTo(Stone.Empty));
        }

        [Test]
        public void RejectedMove_ShowsTheReason_AndChangesNothing()
        {
            var flow = AppLab.FromText(@"
                . . . . . . . . .
                . . . . . . . . .
                . . . . . . . . .
                . . . . . . . . .
                . . . . . . . . .
                . . . . . . . . .
                . . . . . . . . .
                O . . . . . . . .
                . O . . . . . . .");
            AppLab.Start(flow);
            var before = flow.Game!.StateHash();
            flow.Click(Points.Parse("A1"));
            flow.FlushIntents();
            Assert.That(flow.LastRejection, Is.EqualTo(MoveVerdict.Suicide));
            Assert.That(flow.Notice, Does.Contain("自殺手"));
            Assert.That(flow.Cues.Reject, Is.EqualTo(1));
            Assert.That(flow.Game.StateHash(), Is.EqualTo(before));
            Assert.That(flow.IsPlayerTurn, Is.True);
            flow.Click(Points.Parse("E5"));
            flow.FlushIntents();
            Assert.That(flow.LastRejection, Is.Null);
            Assert.That(flow.Game[Points.Parse("E5")], Is.EqualTo(Stone.Black));
        }

        [Test]
        public void Pause_StopsTheCpuSearch()
        {
            var settings = AppLab.FastSettings();
            settings.PlayoutsPerTick = 1;
            var flow = AppLab.Flow(settings: settings);
            AppLab.Start(flow);
            flow.Click(Points.Parse("E5"));
            flow.StepTicks(10);
            Assert.That(flow.IsCpuThinking, Is.True);
            var done = flow.Cpu!.Search!.Done;
            Assert.That(done, Is.GreaterThan(0));

            flow.Press(InputButton.Cancel);
            flow.StepTicks(200);
            Assert.That(flow.Screen, Is.EqualTo(FlowScreen.Paused));
            Assert.That(flow.Think(1000), Is.EqualTo(0));
            Assert.That(flow.Cpu.Search!.Done, Is.EqualTo(done), "一時停止の間は探索も止まる");

            flow.Press(InputButton.Cancel);
            flow.StepTicks(5);
            Assert.That(flow.Screen, Is.EqualTo(FlowScreen.Playing));
            Assert.That(flow.Cpu.Search!.Done, Is.EqualTo(done + 5));
        }

        [Test]
        public void FocusLoss_PausesAndDropsPendingInput()
        {
            var flow = AppLab.Flow();
            AppLab.Start(flow);
            flow.Click(Points.Parse("E5"));
            flow.OnFocusChanged(false);
            flow.FlushIntents();
            Assert.That(flow.Screen, Is.EqualTo(FlowScreen.Paused));
            Assert.That(flow.Game!.MoveCount, Is.EqualTo(0), "喪失の前に積んだクリックも捨てる");

            flow.Press(InputButton.Cancel); // 喪失中の入力は当てはめない
            flow.FlushIntents();
            Assert.That(flow.Screen, Is.EqualTo(FlowScreen.Paused));

            flow.OnFocusChanged(true);
            flow.StepTicks(10);
            Assert.That(flow.Screen, Is.EqualTo(FlowScreen.Paused), "戻っても自動では再開しない");
            flow.Press(InputButton.Confirm); // 再開
            flow.FlushIntents();
            Assert.That(flow.Screen, Is.EqualTo(FlowScreen.Playing));
        }

        [Test]
        public void TwoPasses_LeadToScoring_ThenTheResult()
        {
            // コミ 10 で白の 1 目勝ちの局面。黒（プレイヤー）がパスすると、白（CPU）もパスして終局する。
            var config = new GoConfig { Komi = 10 };
            var flow = AppLab.FromText(AppLab.Walls, config);
            AppLab.Start(flow);
            flow.Press(InputButton.Pass);
            flow.FlushIntents();
            Assert.That(flow.Notice, Does.Contain("黒がパス"));
            Assert.That(flow.Cues.Pass, Is.EqualTo(1));
            for (var i = 0; i < 500 && flow.Screen == FlowScreen.Playing; i++) { flow.StepTicks(1); }
            Assert.That(flow.Screen, Is.EqualTo(FlowScreen.Scoring));
            Assert.That(flow.Game!.End, Is.EqualTo(GameEnd.TwoPasses));

            // 推定を回し終えるまでは、切り替えも確定も受け付けない。
            Assert.That(flow.IsEstimating, Is.True);
            flow.Press(InputButton.Cancel);
            flow.Click(Points.Parse("E5"));
            flow.FlushIntents();
            Assert.That(flow.Screen, Is.EqualTo(FlowScreen.Scoring));
            Assert.That(flow.DeadStones.IsEmpty, Is.True);
            AppLab.FinishEstimate(flow);
            Assert.That(flow.DeadStones, Is.EqualTo(Ownership.EstimateDead(flow.Game, AppLab.FastMcts(), flow.MatchSeed)), "一度に回した推定と同じ");
            Assert.That(flow.DeadStones.IsEmpty, Is.True, "壁はどちらも生き");
            Assert.That(flow.ScorePreview.Margin, Is.EqualTo(-1));

            // 盤の着手は受け付けず、連のクリックで死石を切り替える。
            var e5 = Points.Parse("E5");
            flow.Click(e5);
            flow.FlushIntents();
            Assert.That(flow.DeadStones.Count, Is.EqualTo(9), "連ごと");
            Assert.That(flow.ScorePreview.WhiteArea, Is.EqualTo(81 - 0), "黒の壁を死石にすると全て白");
            flow.Click(e5);
            flow.Click(Points.Parse("A1")); // 空点は切り替えない
            flow.FlushIntents();
            Assert.That(flow.DeadStones.IsEmpty, Is.True);
            Assert.That(flow.Game.StoneCount(Stone.Black), Is.EqualTo(9));

            flow.Click(GoFlow.ClickConfirmScore);
            flow.FlushIntents();
            Assert.That(flow.Screen, Is.EqualTo(FlowScreen.Result));
            Assert.That(flow.FinalScore!.Value.Margin, Is.EqualTo(-1));
            Assert.That(flow.Outcome, Is.EqualTo(Outcome.Loss));
        }

        [Test]
        public void Result_Again_StartsWithANewSeed_AndTitleClears()
        {
            var flow = AppLab.Flow();
            AppLab.Start(flow);
            var first = flow.MatchSeed;
            Resign(flow);
            Assert.That(flow.Screen, Is.EqualTo(FlowScreen.Result));
            flow.Click(GoFlow.ResultAgain);
            flow.FlushIntents();
            Assert.That(flow.Screen, Is.EqualTo(FlowScreen.Playing));
            Assert.That(flow.MatchSeed, Is.Not.EqualTo(first));
            Assert.That(flow.Game!.MoveCount, Is.EqualTo(0));
            Assert.That(flow.Outcome, Is.Null);
            Resign(flow);
            flow.Press(InputButton.Down);
            flow.Press(InputButton.Confirm);
            flow.FlushIntents();
            Assert.That(flow.Screen, Is.EqualTo(FlowScreen.Title));
            Assert.That(flow.Game, Is.Null);
        }

        [Test]
        public void ResignFromThePauseMenu_IsALoss_AndLaterClicksAreIgnored()
        {
            var flow = AppLab.Flow();
            AppLab.Start(flow);
            Resign(flow);
            Assert.That(flow.Outcome, Is.EqualTo(Outcome.Loss));
            Assert.That(flow.Game!.End, Is.EqualTo(GameEnd.Resignation));
            Assert.That(flow.FinalScore, Is.Null);
            var hash = flow.Game.StateHash();
            flow.Press(InputButton.Pass);
            flow.Click(Points.Parse("E5"));
            flow.StepTicks(30);
            Assert.That(flow.Game.StateHash(), Is.EqualTo(hash));
            Assert.That(flow.Screen, Is.EqualTo(FlowScreen.Result));
        }

        [Test]
        public void CpuResigns_WhenHopeless()
        {
            // 黒の面積 63・白 18。白（CPU）は投了する。
            var mcts = AppLab.FastMcts();
            mcts.ResignMinMove = 0;
            mcts.ResignPermille = 200;
            var row = ". . . . . . X O .\n";
            var flow = AppLab.FromText(string.Concat(System.Linq.Enumerable.Repeat(row, 9)), mcts: mcts);
            AppLab.Start(flow);
            flow.Click(Points.Parse("A1"));
            for (var i = 0; i < 500 && flow.Screen == FlowScreen.Playing; i++) { flow.StepTicks(1); }
            Assert.That(flow.Screen, Is.EqualTo(FlowScreen.Result));
            Assert.That(flow.Game!.Resigned, Is.EqualTo(Stone.White));
            Assert.That(flow.Outcome, Is.EqualTo(Outcome.Win));
        }

        [Test]
        public void CpuMove_DoesNotDependOnHowTheSearchIsSliced()
        {
            int CpuReply(int perTick, int thinkPerFrame)
            {
                var settings = AppLab.FastSettings();
                settings.PlayoutsPerTick = perTick;
                var flow = AppLab.Flow(settings: settings, seed: 7);
                AppLab.Start(flow);
                flow.Click(Points.Parse("C3"));
                flow.FlushIntents();
                for (var i = 0; i < 1000 && !flow.IsPlayerTurn; i++)
                {
                    flow.Think(thinkPerFrame);
                    flow.StepTicks(1);
                }

                return flow.Game!.Move(1);
            }

            var reference = CpuReply(50, 0);
            Assert.That(CpuReply(1, 0), Is.EqualTo(reference));
            Assert.That(CpuReply(3, 17), Is.EqualTo(reference));
            Assert.That(CpuReply(1, 1000), Is.EqualTo(reference));
        }

        [Test]
        public void WithoutTickSearch_OnlyThinkRunsTheSearch_AndTheMoveIsTheSame()
        {
            int CpuReply(bool inTicks, int thinkPerFrame)
            {
                var flow = AppLab.Flow(seed: 7);
                flow.SearchInTicks = inTicks;
                AppLab.Start(flow);
                flow.Click(Points.Parse("C3"));
                flow.FlushIntents();
                for (var i = 0; i < 1000 && !flow.IsPlayerTurn; i++)
                {
                    flow.Think(thinkPerFrame);
                    flow.StepTicks(1);
                }

                Assert.That(flow.IsPlayerTurn, Is.True, "CPU が打たない");
                return flow.Game!.Move(1);
            }

            Assert.That(CpuReply(false, 9), Is.EqualTo(CpuReply(true, 0)));

            // Tick だけでは探索が進まず、打たない（考える間は数える）。
            var idle = AppLab.Flow(seed: 7);
            idle.SearchInTicks = false;
            AppLab.Start(idle);
            idle.Click(Points.Parse("C3"));
            idle.FlushIntents();
            idle.StepTicks(500);
            Assert.That(idle.IsCpuThinking, Is.True);
            Assert.That(idle.Cpu!.Search!.Done, Is.EqualTo(0));
        }

        [Test]
        public void WithoutTickSearch_TheDeadStoneEstimateRunsOnlyInThink()
        {
            var flow = AppLab.FromText(AppLab.Walls, new GoConfig { Komi = 10 });
            flow.SearchInTicks = false;
            AppLab.Start(flow);
            flow.Press(InputButton.Pass);
            flow.FlushIntents();
            for (var i = 0; i < 2000 && flow.Screen == FlowScreen.Playing; i++)
            {
                flow.Think(13);
                flow.StepTicks(1);
            }

            Assert.That(flow.IsEstimating, Is.True);
            flow.StepTicks(100);
            Assert.That(flow.Estimate!.Done, Is.EqualTo(0), "Tick では回さない");
            while (flow.IsEstimating) { Assert.That(flow.Think(13), Is.GreaterThan(0)); }
            Assert.That(flow.DeadStones, Is.EqualTo(Ownership.EstimateDead(flow.Game!, AppLab.FastMcts(), flow.MatchSeed)));
            Assert.That(flow.Think(13), Is.EqualTo(0), "回し終えたら何もしない");
        }

        [Test]
        public void KeysLeftAfterAScreenChange_AreDiscarded()
        {
            // タイトルの Enter と同じフレームの Space（決定）が、始まった対局の初手にならない。
            var flow = AppLab.Flow();
            flow.Press(InputButton.Confirm);
            flow.Press(InputButton.Confirm);
            flow.FlushIntents();
            Assert.That(flow.Screen, Is.EqualTo(FlowScreen.Playing));
            Assert.That(flow.Game!.MoveCount, Is.EqualTo(0));

            // 一時停止の再開と同じフレームのパスも捨てる。
            flow.Press(InputButton.Cancel);
            flow.FlushIntents();
            flow.Press(InputButton.Cancel);
            flow.Press(InputButton.Pass);
            flow.FlushIntents();
            Assert.That(flow.Screen, Is.EqualTo(FlowScreen.Playing));
            Assert.That(flow.Game.MoveCount, Is.EqualTo(0));

            // 同じ画面の中のカーソル移動と決定は、まとめて積んでも全て効く（操縦役と同じ）。
            flow.Press(InputButton.Right);
            flow.Press(InputButton.Right);
            flow.Press(InputButton.Up);
            flow.Press(InputButton.Confirm);
            flow.FlushIntents();
            Assert.That(flow.Game.MoveCount, Is.EqualTo(1));
            Assert.That(flow.Game.Move(0), Is.EqualTo(Points.Parse("G6")));
        }

        [Test]
        public void ARejectionNotice_IsClearedByTheNextGoodMove_ButAPassNoticeIsNot()
        {
            var flow = AppLab.Flow();
            AppLab.Start(flow);
            flow.Click(Points.Parse("C3"));
            flow.FlushIntents();
            AppLab.UntilPlayerTurn(flow);
            flow.Click(Points.Parse("C3"));
            flow.FlushIntents();
            Assert.That(flow.Notice, Is.EqualTo(GoFlow.RejectionText(MoveVerdict.Occupied)));
            var free = flow.Game!.LegalPoints()[0];
            flow.Click(free);
            flow.FlushIntents();
            Assert.That(flow.Notice, Is.Null, "打てたら理由を消す");
            Assert.That(flow.LastRejection, Is.Null);

            AppLab.UntilPlayerTurn(flow);
            flow.Press(InputButton.Pass);
            flow.FlushIntents();
            Assert.That(flow.Notice, Does.Contain("パス"));
        }

        [Test]
        public void CpuPlayer_StartsANewSearch_ForAnotherGameWithTheSameMoveCount()
        {
            var cpu = new CpuPlayer(Stone.Black, AppLab.FastMcts(), 80, 5, 1, 3);
            var a = new GoGame(new GoConfig());
            var b = new GoGame(new GoConfig { Komi = 0 });
            cpu.Think(a, 30);
            Assert.That(cpu.Search!.Done, Is.EqualTo(30));
            cpu.Think(b, 10);
            Assert.That(cpu.Search!.Done, Is.EqualTo(10), "別の対局は別の探索");
            Assert.That(() => new CpuPlayer(Stone.Black, AppLab.FastMcts(), 80, 0, 1, 3), Throws.TypeOf<ArgumentOutOfRangeException>(), "Tick だけでも打てるよう 1 以上");
        }

        [Test]
        public void InvalidSettings_AreRejected()
        {
            Assert.That(() => AppLab.Flow(settings: new FlowSettings { PlayoutsPerTick = 0 }), Throws.TypeOf<ArgumentException>());
            Assert.That(() => AppLab.Flow(mcts: new MctsConfig { DeadPercent = 100 }), Throws.TypeOf<ArgumentException>());
            Assert.That(() => AppLab.Flow(config: new GoConfig { MoveLimit = 1 }), Throws.TypeOf<ArgumentException>());
        }

        private static void Resign(GoFlow flow)
        {
            flow.Press(InputButton.Cancel);
            flow.FlushIntents(); // 画面が変わると同じフレームの残りの入力は捨てる
            flow.Press(InputButton.Down);
            flow.Press(InputButton.Confirm);
            flow.FlushIntents();
        }
    }
}
