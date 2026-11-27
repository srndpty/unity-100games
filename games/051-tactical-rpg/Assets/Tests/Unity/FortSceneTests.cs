#nullable enable
using System.Collections;
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
    public sealed class FortSceneTests
    {
        [UnityTest]
        public IEnumerator VirtualMouseMovesAttacksEndsTurnAndRestarts()
        {
            using var keyboard = new TestKeyboard();
            var mouse = InputSystem.AddDevice<Mouse>();
            try
            {
                yield return SceneManager.LoadSceneAsync("Fort");
                var game = Object.FindAnyObjectByType<FortGame>();
                Assert.That(game, Is.Not.Null);
                var deadline = Time.realtimeSinceStartup + 10f;
                while (game.Root?.Q<Button>("start") is not { worldBound: { width: > 0 } } &&
                    Time.realtimeSinceStartup < deadline)
                {
                    yield return null;
                }

                game.ResetBattle(1);
                Assert.That(game.Root?.Q<Button>("start"), Is.Not.Null);
                yield return null;
                yield return Click(game, mouse, "start");
                yield return Click(game, mouse, "cell-49");
                Assert.That(game.Root!.Q<Button>("cell-24").ClassListContains("reachable"), Is.True);
                yield return Hover(game, mouse, "cell-24");
                Assert.That(game.Root.Q<Button>("cell-24").ClassListContains("path"), Is.True);
                yield return Hover(game, mouse, "end");
                Assert.That(game.Root.Q<Button>("cell-24").ClassListContains("path"), Is.False);
                Assert.That(game.Root.Q<Label>("preview").text, Is.Empty);
                yield return Click(game, mouse, "cell-24");
                Assert.That(game.Flow.Battle.Units[0].Cell, Is.EqualTo(24));
                yield return Click(game, mouse, "end");
                Assert.That(game.Flow.Battle.Turn, Is.EqualTo(Side.Enemy));
                Assert.That(game.Root.Q<Button>("end").enabledInHierarchy, Is.False);
                yield return WaitForEnemy(game);
                Assert.That(game.Flow.Battle.Round, Is.EqualTo(2));
                var order = Tactics.Choose(game.Flow.Battle, 0);
                Assert.That(order.Target, Is.GreaterThanOrEqualTo(0));
                yield return Click(game, mouse, $"cell-{game.Flow.Battle.Units[0].Cell}");
                if (order.Cell != game.Flow.Battle.Units[0].Cell) { yield return Click(game, mouse, $"cell-{order.Cell}"); }
                var targetCell = game.Flow.Battle.Units[order.Target].Cell;
                Assert.That(game.Root.Q<Button>($"cell-{targetCell}").ClassListContains("target"), Is.True);
                yield return Hover(game, mouse, $"cell-{targetCell}");
                Assert.That(game.Root.Q<Label>("preview").text, Does.Contain("予測"));
                yield return Hover(game, mouse, "end");
                Assert.That(game.Root.Q<Label>("preview").text, Is.Empty);
                yield return Click(game, mouse, $"cell-{targetCell}");
                Assert.That(game.Flow.Battle.Units[0].Done, Is.True);
                game.ResetBattle(1);
                yield return null;
                yield return Click(game, mouse, "start");
                while (game.Flow.Battle.Result == Result.Playing && game.Flow.Battle.Round <= 40)
                {
                    for (var id = 0; id < Battle.AllyCount && game.Flow.Battle.Result == Result.Playing; id++)
                    {
                        var battle = game.Flow.Battle;
                        if (!battle.CanAct(id, Side.Ally)) { continue; }
                        var next = Tactics.Choose(battle, id);
                        yield return Click(game, mouse, $"cell-{battle.Units[id].Cell}");
                        if (next.Cell != battle.Units[id].Cell) { yield return Click(game, mouse, $"cell-{next.Cell}"); }
                        if (next.Target >= 0) { yield return Click(game, mouse, $"cell-{battle.Units[next.Target].Cell}"); }
                    }

                    if (game.Flow.Battle.Result != Result.Playing) { break; }
                    yield return Click(game, mouse, "end");
                    yield return WaitForEnemy(game);
                }

                Assert.That(game.Flow.Battle.Result, Is.EqualTo(Result.Won));
                Assert.That(game.Root.Q<Button>("start").text, Is.EqualTo("再戦"));
                yield return Click(game, mouse, "start");
                Assert.That(game.Flow.Battle.Units[0].Cell, Is.EqualTo(49));
                Assert.That(game.Flow.Battle.Round, Is.EqualTo(1));
            }
            finally
            {
                InputSystem.RemoveDevice(mouse);
            }
        }

        private static IEnumerator WaitForEnemy(FortGame game)
        {
            var deadline = Time.realtimeSinceStartup + 10f;
            while (game.Flow.Battle.Turn == Side.Enemy && game.Flow.Battle.Result == Result.Playing)
            {
                Assert.That(Time.realtimeSinceStartup, Is.LessThan(deadline), "敵の手番が終わりません。");
                yield return null;
            }

            yield return null;
        }

        private static IEnumerator Hover(FortGame game, Mouse mouse, string name)
        {
            InputSystem.QueueStateEvent(mouse, new MouseState { position = ScreenPoint(game, name) });
            yield return null;
            yield return null;
        }

        private static IEnumerator Click(FortGame game, Mouse mouse, string name)
        {
            var screen = ScreenPoint(game, name);
            yield return Hover(game, mouse, name);
            InputSystem.QueueStateEvent(mouse, new MouseState { position = screen }.WithButton(UnityEngine.InputSystem.LowLevel.MouseButton.Left));
            yield return null;
            InputSystem.QueueStateEvent(mouse, new MouseState { position = screen });
            yield return null;
            yield return null;
        }

        private static Vector2 ScreenPoint(FortGame game, string name)
        {
            var root = game.Root!;
            var button = root.Q<Button>(name);
            Assert.That(button, Is.Not.Null, name);
            var center = button.worldBound.center;
            var bounds = root.worldBound;
            var screen = new Vector2((center.x - bounds.xMin) / bounds.width * Screen.width,
                (bounds.yMax - center.y) / bounds.height * Screen.height);
            var picked = root.panel.Pick(center);
            Assert.That(picked == button || (picked != null && button.Contains(picked)), Is.True, name);
            return screen;
        }
    }
}
