#nullable enable
using System.Collections;
using Game.App;
using Game.Core;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace Game.Unity.Tests
{
    public sealed class MuseumSceneTests
    {
        private DeductionGame _game = null!;

        [UnitySetUp]
        public IEnumerator Setup()
        {
            yield return SceneManager.LoadSceneAsync("Museum");
            _game = Object.FindAnyObjectByType<DeductionGame>();
            Assert.That(_game, Is.Not.Null);
            var deadline = Time.realtimeSinceStartup + 10f;
            while (_game.Root?.Q<Button>("start") is not { worldBound: { width: > 0 } } &&
                Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            _game.Flow.Focus(true);
            _game.Flow.PointerIdle();
        }

        [UnityTest]
        public IEnumerator ClicksThroughTheCaseAndRetries()
        {
            yield return Click("help");
            Assert.That(_game.Flow.Screen, Is.EqualTo(Game.App.Screen.Help));
            yield return Click("start");
            Assert.That(_game.Root!.Q<Button>("statement-2").enabledInHierarchy, Is.False);
            yield return Click("question");
            yield return Click("present");
            Assert.That(_game.Flow.Case.Attempts, Is.EqualTo(2));
            yield return Click("evidence-2");
            yield return Click("present");
            Assert.That(_game.Root!.Q<Button>("statement-2").enabledInHierarchy, Is.True);
            yield return Click("statement-3");
            yield return Click("question");
            yield return Click("statement-2");
            yield return Click("evidence-6");
            yield return Click("present");
            yield return Click("statement-4");
            yield return Click("question");
            yield return Click("evidence-7");
            yield return Click("present");
            Assert.That(_game.Flow.Case.Outcome, Is.EqualTo(Outcome.Won));
            yield return Click("start");
            Assert.That(_game.Flow.Case.Attempts, Is.EqualTo(3));
            Assert.That(_game.Root!.Q<Button>("evidence-7"), Is.Null);
        }

        [UnityTest]
        public IEnumerator ChangingTestimonyAndEvidenceKeepsControlsInPlace()
        {
            yield return Click("start");
            var names = new[] { "statement-0", "statement-1", "question", "evidence-0", "evidence-1", "present" };
            var bounds = new Rect[names.Length];
            for (var i = 0; i < names.Length; i++)
            {
                bounds[i] = _game.Root!.Q<Button>(names[i]).worldBound;
            }

            var selections = new[] { "statement-1", "evidence-1", "statement-0", "evidence-0" };
            foreach (var selection in selections)
            {
                yield return Click(selection);
                Assert.That(_game.Root!.Q<Button>(selection).ClassListContains("selected"), Is.True, selection);
                for (var i = 0; i < names.Length; i++)
                {
                    Assert.That(_game.Root.Q<Button>(names[i]).worldBound, Is.EqualTo(bounds[i]), names[i]);
                }
            }
        }

        [UnityTest]
        public IEnumerator VirtualMouseCanLoseRetryAndWin()
        {
            using var keyboard = new TestKeyboard();
            var mouse = InputSystem.AddDevice<Mouse>();
            try
            {
                yield return MouseClick(mouse, "start");
                Assert.That(_game.Flow.Screen, Is.EqualTo(Game.App.Screen.Case));
                for (var attempt = 0; attempt < 3; attempt++) { yield return MouseClick(mouse, "present"); }
                Assert.That(_game.Flow.Case.Outcome, Is.EqualTo(Outcome.Lost));
                yield return MouseClick(mouse, "start");
                yield return MouseClick(mouse, "question");
                yield return MouseClick(mouse, "evidence-2");
                yield return MouseClick(mouse, "present");
                StringAssert.Contains("立証済み", _game.Root!.Q<Button>("statement-0").text);
                yield return MouseClick(mouse, "statement-3");
                yield return MouseClick(mouse, "question");
                yield return MouseClick(mouse, "statement-2");
                yield return MouseClick(mouse, "evidence-6");
                yield return MouseClick(mouse, "present");
                yield return MouseClick(mouse, "statement-4");
                yield return MouseClick(mouse, "question");
                yield return MouseClick(mouse, "evidence-7");
                yield return MouseClick(mouse, "present");
                Assert.That(_game.Flow.Case.Outcome, Is.EqualTo(Outcome.Won));
                yield return MouseClick(mouse, "start");
                Assert.That(_game.Flow.Case.ProofOf(0), Is.Null);
            }
            finally
            {
                InputSystem.RemoveDevice(mouse);
            }
        }

        private IEnumerator MouseClick(Mouse mouse, string name)
        {
            var root = _game.Root!;
            var button = root.Q<Button>(name);
            Assert.That(button, Is.Not.Null, name);
            Assert.That(button.enabledInHierarchy, Is.True, name);
            var center = button.worldBound.center;
            var bounds = root.worldBound;
            var screen = new Vector2(
                (center.x - bounds.xMin) / bounds.width * UnityEngine.Screen.width,
                (bounds.yMax - center.y) / bounds.height * UnityEngine.Screen.height);
            var picked = root.panel.Pick(RuntimePanelUtils.ScreenToPanel(root.panel, new Vector2(screen.x, UnityEngine.Screen.height - screen.y)));
            Assert.That(picked == button || (picked != null && button.Contains(picked)), Is.True, name);
            InputSystem.QueueStateEvent(mouse, new MouseState { position = screen });
            yield return null;
            InputSystem.QueueStateEvent(mouse, new MouseState { position = screen }.WithButton(UnityEngine.InputSystem.LowLevel.MouseButton.Left));
            yield return null;
            InputSystem.QueueStateEvent(mouse, new MouseState { position = screen });
            yield return null;
            yield return null;
        }

        private IEnumerator Click(string name)
        {
            var root = _game.Root!;
            var button = root.Q<Button>(name);
            Assert.That(button, Is.Not.Null, name);
            Assert.That(button.enabledInHierarchy, Is.True, name);
            var center = button.worldBound.center;
            Assert.That(root.worldBound.Contains(center), Is.True, name);
            var picked = root.panel.Pick(center);
            Assert.That(picked == button || (picked != null && button.Contains(picked)), Is.True, $"クリック地点で {name} を選べません。");
            using (var down = PointerDownEvent.GetPooled(new Event { type = EventType.MouseDown, mousePosition = center, button = 0 }))
            {
                down.target = button;
                button.SendEvent(down);
            }

            using (var up = PointerUpEvent.GetPooled(new Event { type = EventType.MouseUp, mousePosition = center, button = 0 }))
            {
                up.target = button;
                button.SendEvent(up);
            }

            yield return null;
            yield return null;
        }
    }
}
