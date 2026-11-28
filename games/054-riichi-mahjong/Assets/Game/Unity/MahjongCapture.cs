using System;
using System.Collections.Generic;
using System.Linq;
using Game.App;
using Game.Core;
using Game.Unity.Evidence;
using UnityEngine;

namespace Game.Unity
{
    public sealed class MahjongCapture : MonoBehaviour, ICaptureScenario
    {
        public async Awaitable RunAsync(CaptureContext capture)
        {
            var game = FindAnyObjectByType<MahjongGame>() ?? throw new InvalidOperationException("対局画面がありません。");
            await capture.WaitUntilAsync(() => game.Root != null, 10f);
            var seen = new HashSet<string>();
            var activeSeed = EvidenceCapture.Seed;
            capture.Record("rules", "東風戦・赤牌なし・25,000点");
            async Awaitable Shot(string name, string description)
            {
                if (!seen.Add(name)) { return; }

                game.Render();
                var match = game.Flow.Match;
                var position = match == null ? "タイトル" : $"局通番={match.Serial}, 操作番号={match.Revision}";
                await capture.ShootAsync(name, $"{description} (seed={activeSeed}, {position})");
            }

            await Shot("01_title", "タイトル");
            for (var seed = 0; seed < 24; seed++)
            {
                activeSeed = unchecked(EvidenceCapture.Seed + seed);
                var flow = new TableFlow(activeSeed, 1f);
                flow.Push(Intent.Start);
                flow.Step(0f);
                game.UseDemonstration(flow);
                var match = flow.Match!;
                await Shot("02_table", "東1局の配牌");
                for (var step = 0; step < 10000 && match.Phase != Phase.MatchResult; step++)
                {
                    var legal = match.LegalActions(0);
                    if (legal.Any(m => m.Kind is MoveKind.Chi or MoveKind.Pon or MoveKind.OpenKan))
                    {
                        await Shot("03_call", "打牌への応答と鳴き候補");
                    }

                    if (match.Seats[0].Riichi && match.Phase == Phase.Drawn)
                    {
                        await Shot("04_riichi", "リーチ成立後の卓");
                    }

                    if (match.Phase == Phase.HandResult)
                    {
                        var won = match.Result!.Wins.Count > 0;
                        await Shot(won ? "05_win" : "06_draw", won ? "和了の符翻と点数移動" : "流局の聴牌と点数移動");
                    }

                    if (!TableFlow.PlayCpu(match, 0)) { throw new InvalidOperationException("台本が進行できません。"); }
                }

                if (match.Phase != Phase.MatchResult) { throw new InvalidOperationException("台本が終局しません。"); }

                await Shot("07_ranking", "東風戦の最終順位");
                if (seen.Count == 7) { return; }
            }

            throw new InvalidOperationException("必要な場面をすべて撮影できませんでした。");
        }
    }
}
