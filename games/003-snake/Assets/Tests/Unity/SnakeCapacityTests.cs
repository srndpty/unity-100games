using System;
using System.Collections;
using System.Globalization;
using System.Reflection;
using Game.App;
using Game.Core;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Stopwatch = System.Diagnostics.Stopwatch;

namespace Game.Unity.Tests
{
    public sealed class SnakeCapacityTests
    {
        [UnityTest]
        public IEnumerator MaximumBoard_CreatesGrowsAndRendersWithinBudget()
        {
            yield return SceneManager.LoadSceneAsync("Snake");
            var original = UnityEngine.Object.FindAnyObjectByType<SnakeGame>();
            var sourcePanel = original.GetComponent<PanelRenderer>();
            original.gameObject.SetActive(false);
            var settings = ScriptableObject.CreateInstance<SnakeSettings>();
            settings.Game = new SnakeConfig { Width = SnakeConfig.MaxWidth, Height = SnakeConfig.MaxHeight };
            var watch = Stopwatch.StartNew();
            var owner = new GameObject("MaximumBoard");
            owner.SetActive(false);
            try
            {
                var panel = owner.AddComponent<PanelRenderer>();
                panel.panelSettings = sourcePanel.panelSettings;
                panel.visualTreeAsset = sourcePanel.visualTreeAsset;
                var game = owner.AddComponent<SnakeGame>();
                SetField(game, "_settings", settings);
                SetField(game, "_hudRenderer", panel);
                SetField(game, "_camera", Camera.main);
                owner.SetActive(true);
                game.ManualTicks = true;
                // バッチ実行のウィンドウフォーカスとは切り離して測定する。
                game.SendMessage("OnApplicationFocus", true);
                game.Flow.Back();
                // 測定の入力列と餌配置を固定する。シーンの実行時シードは使用しない。
                typeof(SnakeGame).GetProperty(nameof(SnakeGame.Flow)).SetValue(game, new SnakeFlow(settings.Game, 1));
                game.Flow.Submit();
                game.Render();
                var creationMs = watch.Elapsed.TotalMilliseconds;
                watch.Restart();
                var cells = settings.Game.Width * settings.Game.Height;
                var lastLength = game.Flow.Game.Length;
                var ticks = 0;
                while (game.Flow.Screen == AppScreen.Playing && ticks++ < cells * (cells + 1))
                {
                    game.Flow.RequestDirection(Autopilot.Next(game.Flow.Game));
                    game.Flow.StepOneTick();
                    if (game.Flow.Game.Length != lastLength)
                    {
                        game.Render();
                        lastLength = game.Flow.Game.Length;
                    }
                }
                var growthMs = watch.Elapsed.TotalMilliseconds;
                Assert.That(game.Flow.Game.Phase, Is.EqualTo(GamePhase.Won));
                Assert.That(game.Flow.Game.Length, Is.EqualTo(cells));
                var renderers = owner.GetComponentsInChildren<SpriteRenderer>();
                Assert.That(renderers.Length, Is.EqualTo(cells + 2), "体節＋盤面＋餌の上限");
                Assert.That(owner.GetComponentsInChildren<Transform>().Length, Is.EqualTo(cells + 3));
                for (var i = 0; i < 30; i++) { game.Render(); }
                var samples = new double[300];
                for (var i = 0; i < samples.Length; i++)
                {
                    watch.Restart();
                    game.Render();
                    samples[i] = watch.Elapsed.TotalMilliseconds;
                }
                Array.Sort(samples);
                var renderP50 = samples[149];
                var renderP95 = samples[284];
                // 実フレームも通し、SpriteRendererの描画登録を含む壁時計時間を記録する。
                var frames = new double[120];
                for (var i = 0; i < 30; i++) { yield return null; }
                for (var i = 0; i < frames.Length; i++)
                {
                    watch.Restart();
                    yield return null;
                    frames[i] = watch.Elapsed.TotalMilliseconds;
                }
                Array.Sort(frames);
                TestContext.Out.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "CAPACITY width={0} height={1} cells={2} ticks={3} creationMs={4:F3} growthMs={5:F3} " +
                    "renderP50Ms={6:F3} renderP95Ms={7:F3} frameP50Ms={8:F3} frameP95Ms={9:F3} renderers={10} objects={11}",
                    settings.Game.Width, settings.Game.Height, cells, game.Flow.Game.TickCount, creationMs, growthMs,
                    renderP50, renderP95, frames[59], frames[113], renderers.Length, cells + 3));
                TestContext.Out.WriteLine($"ENV cpu={SystemInfo.processorType}; gpu={SystemInfo.graphicsDeviceName}; " +
                    $"os={SystemInfo.operatingSystem}; resolution={Screen.width}x{Screen.height}; unity={Application.unityVersion}");
                // GPU時間・FPSの保証ではなく、この対象機での生成・成長・CPU描画更新の予算。
                Assert.That(creationMs, Is.LessThan(1000d));
                Assert.That(growthMs, Is.LessThan(5000d));
                Assert.That(renderP95, Is.LessThan(1000d / 60d));
            }
            finally
            {
                UnityEngine.Object.Destroy(owner);
                UnityEngine.Object.Destroy(settings);
                original.gameObject.SetActive(true);
            }
            yield return null;
        }

        private static void SetField(SnakeGame game, string name, UnityEngine.Object value)
        {
            typeof(SnakeGame).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(game, value);
        }
    }
}
