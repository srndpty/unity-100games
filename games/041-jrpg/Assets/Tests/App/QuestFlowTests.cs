using System;
using Game.Core;
using NUnit.Framework;

namespace Game.App.Tests
{
    public sealed class QuestFlowTests
    {
        [Test]
        public void AutopilotClearsAllTwentySeedsAndMaintainsDeterministicSeedOneHash()
        {
            var data = DefaultGameData.Create();
            uint seedOneClearHash = 0;

            for (var seed = 1; seed <= 20; seed++)
            {
                var store = new MemorySaveStore();
                var flow = new QuestFlow(data, store, initialSeed: seed, eventDurationTicks: 2);
                flow.StartNewGameWithSeed(seed);

                var maxTicks = 120_000;
                var ticks = 0;
                while (flow.Screen != QuestScreen.Result && ticks < maxTicks)
                {
                    Autopilot.Step(flow);
                    ticks++;
                }

                Assert.That(
                    flow.Screen,
                    Is.EqualTo(QuestScreen.Result),
                    $"Seed {seed} stuck on {flow.Screen} map={flow.World?.Explorer.MapId} ({flow.World?.Explorer.X},{flow.World?.Explorer.Y}) G={flow.World?.Gold} Lvs={flow.World?.Party[0].Level}/{flow.World?.Party[1].Level}/{flow.World?.Party[2].Level} HPs={flow.World?.Party[0].Hp}/{flow.World?.Party[1].Hp}/{flow.World?.Party[2].Hp}");
                Assert.That(flow.World, Is.Not.Null);
                Assert.That(flow.World!.IsCleared, Is.True);

                if (seed == 1)
                {
                    seedOneClearHash = flow.World.Hash();
                }
            }

            // 途中で中断した別試合の後でも、シード 1 の最終ハッシュが完全に一致する
            var partialFlow = new QuestFlow(data, new MemorySaveStore(), initialSeed: 999, eventDurationTicks: 2);
            partialFlow.StartNewGameWithSeed(999);
            for (var i = 0; i < 400; i++)
            {
                Autopilot.Step(partialFlow);
            }

            var rerunFlow = new QuestFlow(data, new MemorySaveStore(), initialSeed: 1, eventDurationTicks: 2);
            rerunFlow.StartNewGameWithSeed(1);
            var guard = 0;
            while (rerunFlow.Screen != QuestScreen.Result && guard < 120_000)
            {
                Autopilot.Step(rerunFlow);
                guard++;
            }

            Assert.That(rerunFlow.World!.Hash(), Is.EqualTo(seedOneClearHash));

            // 既定演出刻み (eventDurationTicks=18) のシード 1 完走ハッシュが .NET / Mono / 実機撮影と完全一致すること（013・020 の知見）
            var canonicalFlow = new QuestFlow(data, new MemorySaveStore(), initialSeed: 1, eventDurationTicks: 18);
            canonicalFlow.StartNewGameWithSeed(1);
            var canonicalGuard = 0;
            while (canonicalFlow.Screen != QuestScreen.Result && canonicalGuard < 120_000)
            {
                Autopilot.Step(canonicalFlow);
                canonicalGuard++;
            }

            Assert.That(canonicalFlow.Screen, Is.EqualTo(QuestScreen.Result));
            Assert.That(canonicalFlow.World!.Hash(), Is.EqualTo(0x87AC4C76u));
        }

        [Test]
        public void BattleFastForwardProducesIdenticalWorldStateAsNaturalPlayback()
        {
            var data = DefaultGameData.Create();
            var flowSlow = new QuestFlow(data, new MemorySaveStore(), initialSeed: 5, eventDurationTicks: 20);
            var flowFast = new QuestFlow(data, new MemorySaveStore(), initialSeed: 5, eventDurationTicks: 20);
            flowSlow.StartNewGameWithSeed(5);
            flowFast.StartNewGameWithSeed(5);

            // 両方をダンジョン B1F の同じ場所へ移し、最初の戦闘発生まで同じ入力で進める
            flowSlow.World!.Explorer.WarpTo("dungeon_f1", 4, 5, Direction.Right);
            flowFast.World!.Explorer.WarpTo("dungeon_f1", 4, 5, Direction.Right);

            var fieldGuard = 0;
            while (flowSlow.Screen == QuestScreen.Field && fieldGuard < 5000)
            {
                fieldGuard++;
                if (!flowSlow.World.Explorer.IsMoving)
                {
                    var btn = (flowSlow.World.Explorer.TotalSteps % 2 == 0) ? InputButton.Right : InputButton.Left;
                    flowSlow.TapButton(btn);
                    flowFast.TapButton(btn);
                }

                flowSlow.StepTicks(1);
                flowFast.StepTicks(1);
            }

            Assert.That(flowSlow.Screen, Is.EqualTo(QuestScreen.Battle));
            Assert.That(flowFast.Screen, Is.EqualTo(QuestScreen.Battle));

            // 戦闘中、同じコマンド（全員たたかう）を選びつつ、片方は Tick 自然経過、もう片方は決定連打で早送りする
            while (flowSlow.Screen == QuestScreen.Battle)
            {
                if (flowSlow.ActiveBattle!.Step != BattleInputStep.Animating)
                {
                    flowSlow.TapButton(InputButton.Confirm);
                }

                flowSlow.StepTicks(1);
            }

            while (flowFast.Screen == QuestScreen.Battle)
            {
                flowFast.TapButton(InputButton.Confirm);
                flowFast.StepTicks(1);
            }

            // PlayTicks 以外の世界状態（HP/MP/経験値/所持金/乱数）を揃えて比較するため PlayTicks 差を合わせてハッシュ照合
            var tickDiff = flowSlow.World.PlayTicks - flowFast.World.PlayTicks;
            if (tickDiff > 0)
            {
                flowFast.World.AdvancePlayTicks(tickDiff);
            }

            Assert.That(flowFast.World.Hash(), Is.EqualTo(flowSlow.World.Hash()));
        }

        [Test]
        public void TimestampedInputMatchesAcrossFrameRatesAndSmallTimeSteps()
        {
            var data = DefaultGameData.Create();

            // 0.01 秒 × 10 回で丸め落ちなく 6 Tick 進むこと
            var clockFlow = new QuestFlow(data, new MemorySaveStore());
            clockFlow.StartNewGameWithSeed(10);
            var t = 1.0;
            clockFlow.UpdateFrame(t, 0.0);
            for (var i = 0; i < 10; i++)
            {
                t += 0.01;
                clockFlow.UpdateFrame(t, 0.01);
            }

            Assert.That(clockFlow.World!.PlayTicks, Is.EqualTo(6L));

            // 同一時刻列のキー操作を 30fps / 60fps / 240fps / 不規則間隔で届けて World.Hash が一致する
            Intent[] script =
            {
                new(InputButton.Right, 0, pressed: true, time: 0.05),
                new(InputButton.Right, 0, pressed: false, time: 0.35),
                new(InputButton.Up, 0, pressed: true, time: 0.42),
                new(InputButton.Up, 0, pressed: false, time: 0.70),
                new(InputButton.Left, 0, pressed: true, time: 0.80),
                new(InputButton.Left, 0, pressed: false, time: 0.98),
            };

            var hash60 = RunWithFrameStep(data, script, 1.0 / 60.0);
            var hash30 = RunWithFrameStep(data, script, 1.0 / 30.0);
            var hash240 = RunWithFrameStep(data, script, 1.0 / 240.0);
            var hashIrregular = RunWithIrregularFrames(data, script);

            Assert.That(hash30, Is.EqualTo(hash60));
            Assert.That(hash240, Is.EqualTo(hash60));
            Assert.That(hashIrregular, Is.EqualTo(hash60));
        }

        [Test]
        public void SaveStoreIoFailuresAreReportedOnScreenInsteadOfThrowing()
        {
            var data = DefaultGameData.Create();

            // 読み込みに失敗したら、タイトルの構築は続けて「つづきから」を出さず、理由を知らせる
            var unreadable = new MemorySaveStore { ReadFailure = "access denied" };
            var titleFlow = new QuestFlow(data, unreadable);
            Assert.That(titleFlow.Screen, Is.EqualTo(QuestScreen.Title));
            Assert.That(titleFlow.HasValidSave, Is.False);
            Assert.That(titleFlow.TitleOptions, Has.No.Member(TitleOptionKind.Continue));
            Assert.That(titleFlow.CorruptSaveNotice.Contains("access denied", StringComparison.Ordinal), Is.True);

            // 教会での書き込みに失敗したら、セーブ完了とせず理由を表示する。直れば同じ操作で保存できる
            var store = new MemorySaveStore { WriteFailure = "disk full" };
            var flow = new QuestFlow(data, store, initialSeed: 4);
            flow.StartNewGameWithSeed(4);
            flow.World!.Explorer.WarpTo("town", 14, 5, Direction.Up);
            flow.TapButton(InputButton.Confirm);
            flow.StepTicks(1);
            Assert.That(flow.Screen, Is.EqualTo(QuestScreen.Church));

            var rejectBefore = flow.AudioCues.Reject;
            flow.TapButton(InputButton.Confirm);
            flow.StepTicks(1);
            Assert.That(flow.MenuFeedback.Contains("disk full", StringComparison.Ordinal), Is.True);
            Assert.That(flow.AudioCues.Reject, Is.EqualTo(rejectBefore + 1));
            Assert.That(flow.AudioCues.Save, Is.EqualTo(0));
            Assert.That(store.Content, Is.Null);

            store.WriteFailure = null;
            flow.TapButton(InputButton.Confirm);
            flow.StepTicks(1);
            Assert.That(flow.AudioCues.Save, Is.EqualTo(1));
            Assert.That(store.Content, Is.Not.Null);
        }

        [Test]
        public void FocusLossClearsPendingInputsPausesRegardlessOfEventOrderAndCorruptSaveDisablesContinue()
        {
            var data = DefaultGameData.Create();
            var store = new MemorySaveStore();
            Assert.That(store.TryWrite("version=1\nbroken_save_without_checksum", out _), Is.True);

            // 破損セーブ時はエラー通知を出し、「つづきから」を選べないようにする
            var flow = new QuestFlow(data, store);
            Assert.That(flow.HasValidSave, Is.False);
            Assert.That(flow.CorruptSaveNotice.Length, Is.GreaterThan(0));
            Assert.That(flow.TitleOptions, Has.No.Member(TitleOptionKind.Continue));
            Assert.That(flow.TitleOptions, Has.Member(TitleOptionKind.NewGame));

            // 順序 1: Pause キーキュー -> フォーカス喪失 -> UpdateFrame でも勝手に再開しない
            flow.StartNewGameWithSeed(3);
            flow.UpdateFrame(1.0, 0.0);
            flow.SendButton(InputButton.Right, pressed: true, keySlot: 0);
            flow.SendButton(InputButton.Right, pressed: true, keySlot: 1);
            flow.SendButton(InputButton.Right, pressed: false, keySlot: 0); // slot 1 がまだ保持中
            flow.EnqueueIntent(new Intent(InputButton.Pause, 0, pressed: true, time: 1.01));
            flow.OnFocusChanged(false);
            flow.UpdateFrame(1.05, 0.05);
            Assert.That(flow.IsPaused, Is.True);

            // 復帰後も手動再開するまで停止のまま、保持キーも消えている
            flow.OnFocusChanged(true);
            flow.UpdateFrame(1.20, 0.05);
            Assert.That(flow.IsPaused, Is.True);
            var xBefore = flow.World!.Explorer.X;
            flow.TapButton(InputButton.Pause);
            Assert.That(flow.IsPaused, Is.False);
            flow.StepTicks(16);
            Assert.That(flow.World.Explorer.X, Is.EqualTo(xBefore));

            // 順序 2: 先に手動 Pause -> その後フォーカス喪失しても再開してしまわない
            flow.TapButton(InputButton.Pause);
            Assert.That(flow.IsPaused, Is.True);
            flow.OnFocusChanged(false);
            Assert.That(flow.IsPaused, Is.True);
            flow.OnFocusChanged(true);
            Assert.That(flow.IsPaused, Is.True);
        }

        [Test]
        public void PartyCanRecoverFromWipeoutAtChurchAndStillClearGame()
        {
            var data = DefaultGameData.Create();
            var flow = new QuestFlow(data, new MemorySaveStore(), initialSeed: 2, eventDurationTicks: 2);
            flow.StartNewGameWithSeed(2);

            // ダンジョンへ入り、HP 1 の状態で強敵に全滅させられる状況を作る
            flow.World!.Explorer.WarpTo("dungeon_f3", 8, 8, Direction.Right);
            for (var i = 0; i < flow.World.Party.Count; i++)
            {
                flow.World.Party[i].SetVitals(1, 0);
            }

            var wipeStepGuard = 0;
            while (flow.Screen == QuestScreen.Field && wipeStepGuard < 5000)
            {
                wipeStepGuard++;
                if (!flow.World.Explorer.IsMoving)
                {
                    var btn = (flow.World.Explorer.TotalSteps % 2 == 0) ? InputButton.Right : InputButton.Left;
                    flow.TapButton(btn);
                }

                flow.StepTicks(1);
            }

            Assert.That(flow.Screen, Is.EqualTo(QuestScreen.Battle));
            var wipeBattleGuard = 0;
            while (flow.Screen == QuestScreen.Battle && wipeBattleGuard < 5000)
            {
                wipeBattleGuard++;
                if (flow.ActiveBattle!.Step == BattleInputStep.RootCommand)
                {
                    // 全員ぼうぎょで敵の攻撃を受けて全滅するまで進める
                    if (flow.ActiveBattle.RootCursor != 3)
                    {
                        flow.TapButton(InputButton.Down);
                    }
                    else
                    {
                        flow.TapButton(InputButton.Confirm);
                    }
                }
                else
                {
                    flow.TapButton(InputButton.Confirm);
                }

                flow.StepTicks(1);
            }

            // 教会の神父の台詞から復帰し、そのまま自動操縦でクリアまで到達できる
            Assert.That(flow.Screen, Is.EqualTo(QuestScreen.Dialogue));
            Assert.That(flow.World.Explorer.MapId, Is.EqualTo("town"));
            Assert.That(flow.World.Party.LivingCount, Is.EqualTo(3));

            var guard = 0;
            while (flow.Screen != QuestScreen.Result && guard < 120_000)
            {
                Autopilot.Step(flow);
                guard++;
            }

            Assert.That(flow.Screen, Is.EqualTo(QuestScreen.Result));
            Assert.That(flow.World.IsCleared, Is.True);
        }

        private static uint RunWithFrameStep(GameData data, Intent[] script, double dt)
        {
            var flow = new QuestFlow(data, new MemorySaveStore());
            flow.StartNewGameWithSeed(42);
            var t = 0.0;
            flow.UpdateFrame(t, 0.0);

            var nextIntent = 0;
            while (t < 1.20 - 1e-9)
            {
                var nextT = Math.Min(1.20, t + dt);
                while (nextIntent < script.Length && script[nextIntent].Time <= nextT + 1e-12)
                {
                    flow.EnqueueIntent(script[nextIntent]);
                    nextIntent++;
                }

                flow.UpdateFrame(nextT, nextT - t);
                t = nextT;
            }

            return flow.World!.Hash();
        }

        private static uint RunWithIrregularFrames(GameData data, Intent[] script)
        {
            double[] dts = { 0.013, 0.041, 0.007, 0.029, 0.019, 0.052, 0.011 };
            var flow = new QuestFlow(data, new MemorySaveStore());
            flow.StartNewGameWithSeed(42);
            var t = 0.0;
            flow.UpdateFrame(t, 0.0);

            var nextIntent = 0;
            var stepIdx = 0;
            while (t < 1.20 - 1e-9)
            {
                var dt = dts[stepIdx % dts.Length];
                stepIdx++;
                var nextT = Math.Min(1.20, t + dt);
                while (nextIntent < script.Length && script[nextIntent].Time <= nextT + 1e-12)
                {
                    flow.EnqueueIntent(script[nextIntent]);
                    nextIntent++;
                }

                flow.UpdateFrame(nextT, nextT - t);
                t = nextT;
            }

            return flow.World!.Hash();
        }
    }
}
