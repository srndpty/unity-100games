using System;
using Game.App;
using Game.Core;
using Game.Unity.Evidence;
using UnityEngine;

namespace Game.Unity
{
    public sealed class FortCapture : MonoBehaviour, ICaptureScenario
    {
        public async Awaitable RunAsync(CaptureContext capture)
        {
            var game = FindAnyObjectByType<FortGame>();
            if (game == null) { throw new InvalidOperationException("戦闘画面がありません。"); }
            await capture.WaitUntilAsync(() => game.Root != null, 10f);
            game.ResetBattle((uint)EvidenceCapture.Seed);
            var flow = game.Flow;
            var tick = 0;
            async Awaitable Shot(string name, string note)
            {
                game.Render();
                await capture.ShootAsync(name, $"{note} (tick={tick})");
            }

            await Shot("01_title", "タイトル");
            flow.Help();
            await Shot("02_help", "操作説明");
            flow.Start();
            flow.ClickCell(49);
            tick++;
            await Shot("03_movement", "前衛の移動候補");
            var firstAttack = false;
            var firstEnemyTurn = false;
            while (flow.Screen == Game.App.Screen.Battle && flow.Battle.Round <= 40)
            {
                if (flow.Battle.Turn == Side.Ally)
                {
                    for (var id = 0; id < Battle.AllyCount && flow.Screen == Game.App.Screen.Battle; id++)
                    {
                        BattleDemo.Act(flow, id);
                        tick++;
                        if (!firstAttack && flow.Battle.Units[id].Done && flow.Screen == Game.App.Screen.Battle)
                        {
                            firstAttack = true;
                            await Shot("04_attack", "味方の攻撃結果");
                        }
                    }

                    if (flow.EndTurn() && !firstEnemyTurn)
                    {
                        firstEnemyTurn = true;
                        await Shot("05_enemy", "敵の手番");
                    }
                }

                while (flow.StepEnemy()) { tick++; }
            }

            if (!firstAttack || !firstEnemyTurn || flow.Battle.Result != Result.Won)
            {
                throw new InvalidOperationException("撮影台本が勝利に到達できません。");
            }

            await Shot("06_won", "敵指揮官を撃破");
            flow.Start();
            while (flow.Screen == Game.App.Screen.Battle && flow.Battle.Round <= 60)
            {
                flow.EndTurn();
                while (flow.StepEnemy()) { tick++; }
            }

            if (flow.Battle.Result != Result.Lost) { throw new InvalidOperationException("撮影台本が敗北に到達できません。"); }
            await Shot("07_lost", "部隊全滅");
        }
    }
}
