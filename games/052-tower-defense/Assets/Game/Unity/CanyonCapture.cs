using System;
using Game.App;
using Game.Core;
using Game.Unity.Evidence;
using UnityEngine;
using UnityEngine.UIElements;
using Screen = Game.App.Screen;

namespace Game.Unity
{
    public sealed class CanyonCapture : MonoBehaviour, ICaptureScenario
    {
        public async Awaitable RunAsync(CaptureContext capture)
        {
            var game = FindAnyObjectByType<CanyonGame>();
            if (game == null) { throw new InvalidOperationException("防衛画面がありません。"); }
            await capture.WaitUntilAsync(() => game.Root != null, 10f);
            capture.Record("rules", game.CaptureRules);
            var flow = game.Flow;
            flow.Focus(true);
            flow.PointerIdle();
            async Awaitable Shot(string name, string description)
            {
                game.Render();
                await capture.ShootAsync(name, $"{description} (tick={flow.Defense.Tick})");
            }

            await Shot("01_title", "タイトル");
            Demonstration.Send(flow, Intent.Help);
            await Shot("02_help", "操作説明");
            Demonstration.Send(flow, Intent.Start);
            Demonstration.Prepare(flow);
            Demonstration.Send(flow, Intent.Slot, 2);
            await Shot("03_preparation", "準備と射程");
            flow.Focus(false);
            await Shot("08_pause", "一時停止と再開");
            flow.Focus(true);
            flow.PointerIdle();
            Demonstration.Send(flow, Intent.Resume);
            var attackCaptured = false;
            var hitCaptured = false;
            for (var wave = 1; wave <= Defense.WaveCount && flow.Screen == Screen.Play; wave++)
            {
                if (wave > 1) { Demonstration.Prepare(flow); }
                Demonstration.Send(flow, Intent.Wave);
                for (var tick = 1; tick < 100000 && flow.Defense.Phase == Phase.Running; tick++)
                {
                    flow.Step();
                    if (wave == 1 && !attackCaptured && flow.Defense.Towers[2]?.ShotTick == flow.Defense.Tick - 4)
                    {
                        await Shot("04_attack", "第1Waveの攻撃");
                        attackCaptured = true;
                    }
                    if (wave == 1 && !hitCaptured && flow.Defense.Towers[2]?.ShotTick == flow.Defense.Tick - 8)
                    {
                        await Shot("10_hit", "矢の着弾と被弾明滅");
                        hitCaptured = true;
                    }

                    if (wave == 10 && tick == 125) { await Shot("05_final_wave", "第10Wave"); }
                }
                if (wave == 1 && flow.Defense.Phase == Phase.Preparation)
                {
                    await Shot("09_between_waves", "第1Wave終了後の準備");
                }
            }

            if (!attackCaptured || !hitCaptured || flow.Defense.Phase != Phase.Won) { throw new InvalidOperationException("勝利台本が決着しません。"); }
            VerifyResult("峡谷を守り抜いた");
            await Shot("06_won", "10Waveを防衛");
            Demonstration.Send(flow, Intent.Start);
            for (var wave = 1; wave <= Defense.WaveCount && flow.Screen == Screen.Play; wave++)
            {
                Demonstration.Send(flow, Intent.Wave);
                for (var tick = 0; tick < 100000 && flow.Defense.Phase == Phase.Running; tick++) { flow.Step(); }
            }

            if (flow.Defense.Phase != Phase.Lost) { throw new InvalidOperationException("敗北台本が決着しません。"); }
            VerifyResult("関門が突破された");
            await Shot("07_lost", "ライフを失い敗北");

            void VerifyResult(string heading)
            {
                game.Render();
                var root = game.Root;
                var expected = $"Wave {flow.Defense.Wave} / 10　残りライフ {flow.Defense.Lives}";
                if (root?.Q<UnityEngine.UIElements.Label>("page-heading")?.text != heading ||
                    root?.Q<UnityEngine.UIElements.Label>("page-summary")?.text != expected)
                {
                    throw new InvalidOperationException("撮影する結果画面が防衛結果と一致しません。");
                }
            }
        }
    }
}
