using System;
using Game.App;
using Game.Core;
using Game.Unity.Evidence;
using UnityEngine;

namespace Game.Unity
{
    /// <summary>
    /// 証跡撮影の台本。タイトル → 発射待ち → プレイ中 → 効果が有効な場面 → 面クリア → 結果を撮る。
    /// ゲームは Tick 単位で進め、パドルは固定シードの <see cref="Autopilot"/> が動かす。シードは固定なので毎回同じ画面になる。
    /// </summary>
    [RequireComponent(typeof(BreakoutGame))]
    public sealed class BreakoutCaptureScenario : MonoBehaviour, ICaptureScenario
    {
        private const int AutopilotSeed = 5;

        // 発射から、ボールが最初のブロックへ向かっている途中。
        private const int PlayingShotTicks = 40;

        // 効果が始まってから、カプセルが消えて効果の表示が落ち着くまで。
        private const int EffectShotDelayTicks = 30;
        private const int MaxTicks = 60 * 60 * 30;

        public async Awaitable RunAsync(CaptureContext capture)
        {
            var game = GetComponent<BreakoutGame>();
            var flow = game.Flow;
            var autopilot = new Autopilot(AutopilotSeed);
            capture.Record("settings", JsonUtility.ToJson(game.Settings));
            capture.Record("autopilotSeed", AutopilotSeed.ToString());

            await capture.ShootAsync("title", "起動直後");

            flow.Submit();
            capture.Record("gameSeed", flow.GameSeed.ToString());
            await capture.ShootAsync("serving", $"tick={flow.Game!.Ticks}"); // 撮影中の Update が最新の Tick を描く

            var launchedAt = flow.Game.Ticks;
            Require(game.AdvanceUntil(s => s.Ticks >= launchedAt + PlayingShotTicks, MaxTicks, autopilot.Decide), "プレイ中");
            await capture.ShootAsync("playing", $"tick={flow.Game.Ticks}");

            Require(game.AdvanceUntil(s => s.Effects.IsActive(EffectKind.Pierce) || s.Effects.IsActive(EffectKind.Expand), MaxTicks, autopilot.Decide), "効果の発動");
            var effectAt = flow.Game.Ticks;
            game.AdvanceUntil(s => s.Ticks >= effectAt + EffectShotDelayTicks, MaxTicks, autopilot.Decide);
            await capture.ShootAsync("power-up", $"tick={flow.Game.Ticks}, pierce={flow.Game.Effects.Remaining(EffectKind.Pierce)}, expand={flow.Game.Effects.Remaining(EffectKind.Expand)}");

            Require(game.AdvanceUntil(s => s.Phase == GamePhase.StageClear, MaxTicks, autopilot.Decide), "面クリア");
            await capture.ShootAsync("stage-clear", $"tick={flow.Game.Ticks}, stage={flow.Game.StageIndex + 1}");

            game.AdvanceUntil(_ => false, MaxTicks, autopilot.Decide);
            if (flow.Screen != AppScreen.Result)
            {
                throw new InvalidOperationException($"{MaxTicks} Tick 以内に決着しませんでした");
            }

            await capture.ShootAsync("result", $"tick={flow.Game.Ticks}, phase={flow.Game.Phase}, score={flow.Game.Score}");
        }

        private static void Require(bool reached, string what)
        {
            if (!reached)
            {
                throw new InvalidOperationException($"{MaxTicks} Tick 以内に「{what}」に達しませんでした");
            }
        }
    }
}
