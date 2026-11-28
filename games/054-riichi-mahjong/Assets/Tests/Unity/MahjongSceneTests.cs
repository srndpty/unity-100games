#nullable enable
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Game.App;
using Game.Core;
using IntGeometry;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace Game.Unity.Tests
{
    public sealed class MahjongSceneTests
    {
        [UnityTest]
        public IEnumerator MouseStartsDeclaresRiichiAndRestarts()
        {
            using var keyboard = new TestKeyboard();
            var mouse = InputSystem.AddDevice<Mouse>();
            try
            {
                yield return SceneManager.LoadSceneAsync("Mahjong");
                var game = Object.FindAnyObjectByType<MahjongGame>();
                Assert.That(game, Is.Not.Null);
                var deadline = Time.realtimeSinceStartup + 10f;
                while (game.Root?.Q<Button>("start") is not { worldBound: { width: > 0 } } &&
                    Time.realtimeSinceStartup < deadline)
                {
                    yield return null;
                }

                var flow = new TableFlow(54, 100f, first => first == 1 ? Deal() : RandomDeal(first));
                game.UseDemonstration(flow);
                yield return null;
                yield return Click(game, mouse, "start");
                var match = flow.View!;
                for (var seat = 1; seat < 4; seat++)
                {
                    var rack = game.Root!.Q("cpu-rack-" + seat);
                    Assert.That(rack.childCount, Is.EqualTo(match.Seats[seat].HandCount));
                    Assert.That(rack.resolvedStyle.flexDirection,
                        Is.EqualTo(seat == 2 ? FlexDirection.Row : FlexDirection.Column));
                    foreach (var tile in rack.Children())
                    {
                        Assert.That(tile.tooltip, Is.Null.Or.Empty);
                        Assert.That(tile.resolvedStyle.backgroundImage.sprite, Is.Not.Null);
                    }
                }
                Assert.That(game.Root!.Q<Label>("discard-tip").text, Does.StartWith("打牌のおすすめ"));
                yield return Click(game, mouse, "riichi");
                yield return Click(game, mouse, "tile-132");
                Assert.That(match.Seats[0].Discards.Count, Is.EqualTo(1));
                Assert.That(match.Seats[0].Discards[0].Riichi, Is.True);
                var sawResult = false;
                var sawCall = false;
                for (var i = 0; i < 10000 && match.Phase != Phase.MatchResult; i++)
                {
                    if (match.Phase == Phase.HandResult && !sawResult)
                    {
                        sawResult = true;
                        game.Render();
                        yield return null;
                        yield return Click(game, mouse, "next");
                    }
                    else if (!sawCall && match.Phase == Phase.DiscardReaction && match.LegalActions.Any(
                        m => m.Kind is MoveKind.Chi or MoveKind.Pon or MoveKind.OpenKan))
                    {
                        sawCall = true;
                        game.Render();
                        yield return null;
                        var revision = match.Revision;
                        Assert.That(game.Root!.Q<Button>("pass-bottom").worldBound.center.y,
                            Is.GreaterThan(game.Root.Q<Button>("Pass--1").worldBound.center.y));
                        yield return Click(game, mouse, "pass-bottom");
                        Assert.That(match.Revision != revision || !match.LegalActions.Contains(new Move(MoveKind.Pass)), Is.True);
                    }
                    else { Assert.That(flow.StepDemonstration(), Is.True); }
                }

                Assert.That(sawResult, Is.True);
                Assert.That(sawCall, Is.True);
                Assert.That(match.Phase, Is.EqualTo(Phase.MatchResult));
                game.Render();
                yield return null;
                yield return Click(game, mouse, "restart");
                Assert.That(flow.View, Is.Not.SameAs(match));
                Assert.That(flow.View!.Round, Is.EqualTo(1));
            }
            finally { InputSystem.RemoveDevice(mouse); }
        }

        private static int[] Deal()
        {
            var hand = new[] { 0, 1, 2, 3, 4, 5, 9, 10, 11, 21, 22, 15, 15 };
            var remaining = Enumerable.Range(0, Tiles.Total).ToList();
            var wall = new int[Tiles.Total];
            var reserved = new HashSet<int>();
            for (var i = 0; i < hand.Length; i++)
            {
                var id = remaining.First(t => Tiles.Type(t) == hand[i]);
                remaining.Remove(id);
                wall[i * 4] = id;
                reserved.Add(i * 4);
            }

            wall[52] = 132;
            remaining.Remove(132);
            reserved.Add(52);
            var next = 0;
            for (var i = 0; i < wall.Length; i++)
            {
                if (!reserved.Contains(i)) { wall[i] = remaining[next++]; }
            }

            return wall;
        }

        private static int[] RandomDeal(int serial)
        {
            var wall = Enumerable.Range(0, Tiles.Total).ToArray();
            var random = new SeededRandom(54 + serial);
            for (var i = wall.Length - 1; i > 0; i--)
            {
                var other = random.Next(i + 1);
                (wall[i], wall[other]) = (wall[other], wall[i]);
            }

            return wall;
        }

        private static IEnumerator Click(MahjongGame game, Mouse mouse, string name)
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
    }
}
