#nullable enable
using System.Collections;
using Game.Core;
using Game.App;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace Game.Unity.Tests
{
    public sealed class CanyonSceneTests
    {
        [UnityTest]
        public IEnumerator MouseBuildsUpgradesSellsAndRestartsAfterLoss()
        {
            using var keyboard = new TestKeyboard();
            var mouse = InputSystem.AddDevice<Mouse>();
            try
            {
                yield return SceneManager.LoadSceneAsync("Canyon");
                var game = Object.FindAnyObjectByType<CanyonGame>();
                Assert.That(game, Is.Not.Null);
                var deadline = Time.realtimeSinceStartup + 10f;
                while (game.Root?.Q<Button>("start") is not { worldBound: { width: > 0 } } &&
                    Time.realtimeSinceStartup < deadline)
                {
                    yield return null;
                }

                game.Flow.Focus(true);
                game.Flow.PointerIdle();
                yield return Click(game, mouse, "start");
                yield return Click(game, mouse, "slot-0");
                yield return Click(game, mouse, "build");
                Assert.That(game.Flow.Defense.Towers[0]?.Kind, Is.EqualTo(TowerKind.Bow));
                yield return Click(game, mouse, "upgrade");
                Assert.That(game.Flow.Defense.Towers[0]?.Level, Is.EqualTo(2));
                yield return Click(game, mouse, "sell");
                Assert.That(game.Flow.Defense.Towers[0], Is.Null);
                Assert.That(game.Flow.Defense.Money, Is.EqualTo(139));

                yield return Click(game, mouse, "wave");
                Assert.That(game.Flow.Defense.Phase, Is.EqualTo(Phase.Running));
                game.Flow.Focus(false);
                game.Flow.Focus(true);
                game.Flow.PointerIdle();
                game.Render();
                yield return null;
                Assert.That(game.Root!.Q("pause-overlay").resolvedStyle.display, Is.EqualTo(DisplayStyle.Flex));
                Assert.That(game.Root.Q<Button>("build").enabledInHierarchy, Is.False);
                var pausedTick = game.Flow.Defense.Tick;
                yield return null;
                Assert.That(game.Flow.Defense.Tick, Is.EqualTo(pausedTick));
                yield return Click(game, mouse, "resume");
                Assert.That(game.Flow.Paused, Is.False);
                Assert.That(game.Root.Q("pause-overlay").resolvedStyle.display, Is.EqualTo(DisplayStyle.None));
                FastForwardWave(game);
                yield return null;
                Assert.That(game.Flow.Defense.Wave, Is.EqualTo(2));
                yield return Click(game, mouse, "wave");
                FastForwardWave(game);
                yield return null;
                Assert.That(game.Flow.Defense.Phase, Is.EqualTo(Phase.Lost));
                yield return Click(game, mouse, "start");
                Assert.That(game.Flow.Defense.Wave, Is.EqualTo(1));
                Assert.That(game.Flow.Defense.Lives, Is.EqualTo(10));
            }
            finally
            {
                InputSystem.RemoveDevice(mouse);
            }
        }

        private static void FastForwardWave(CanyonGame game)
        {
            for (var i = 0; i < 3000 && game.Flow.Defense.Phase == Phase.Running; i++) { game.Flow.Step(); }
            game.Render();
        }

        private static IEnumerator Click(CanyonGame game, Mouse mouse, string name)
        {
            var root = game.Root!;
            var button = root.Q<Button>(name);
            Assert.That(button, Is.Not.Null, name);
            Assert.That(button.enabledInHierarchy, Is.True, name);
            var center = button.worldBound.center;
            var bounds = root.worldBound;
            var point = new Vector2((center.x - bounds.xMin) / bounds.width * UnityEngine.Screen.width,
                (bounds.yMax - center.y) / bounds.height * UnityEngine.Screen.height);
            var picked = root.panel.Pick(center);
            Assert.That(picked == button || (picked != null && button.Contains(picked)), Is.True, name);
            InputSystem.QueueStateEvent(mouse, new MouseState { position = point });
            yield return null;
            InputSystem.QueueStateEvent(mouse, new MouseState { position = point }.WithButton(UnityEngine.InputSystem.LowLevel.MouseButton.Left));
            yield return null;
            InputSystem.QueueStateEvent(mouse, new MouseState { position = point });
            yield return null;
            yield return null;
        }

        [UnityTest]
        public IEnumerator ResultRedrawsAcrossUnrenderedRestartsInBothDirections()
        {
            yield return SceneManager.LoadSceneAsync("Canyon");
            var game = Object.FindAnyObjectByType<CanyonGame>();
            Assert.That(game, Is.Not.Null);
            var deadline = Time.realtimeSinceStartup + 10f;
            while (game.Root == null && Time.realtimeSinceStartup < deadline) { yield return null; }
            Assert.That(game.Root, Is.Not.Null);
            game.Flow.Focus(true);
            game.Flow.PointerIdle();

            Finish(true);
            Finish(false);
            Finish(true);

            void Finish(bool defend)
            {
                Demonstration.Send(game.Flow, Intent.Start);
                for (var wave = 0; wave < 10 && game.Flow.Defense.Phase is not (Phase.Won or Phase.Lost); wave++)
                {
                    if (defend) { Demonstration.Prepare(game.Flow); }
                    Demonstration.Send(game.Flow, Intent.Wave);
                    for (var tick = 0; tick < 3000 && game.Flow.Defense.Phase == Phase.Running; tick++) { game.Flow.Step(); }
                }

                Assert.That(game.Flow.Defense.Phase, Is.EqualTo(defend ? Phase.Won : Phase.Lost));
                game.Render();
                Assert.That(game.Root!.Q<Label>("page-heading").text, Is.EqualTo(defend ? "峡谷を守り抜いた" : "関門が突破された"));
                Assert.That(game.Root.Q<Label>("page-summary").text, Does.Contain($"残りライフ {game.Flow.Defense.Lives}"));
            }
        }
    }
}
