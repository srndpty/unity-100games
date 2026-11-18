using System.Collections;
using Game.App;
using Game.Core;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace Game.Unity.Tests
{
    public sealed class StrataDropSceneTests
    {
        private const float WaitLimitSeconds = 5f;
        private const int MaxPieces = 400;

        private TestKeyboard _keyboard;
        private StrataDropGame _game;

        [UnitySetUp]
        public IEnumerator Load()
        {
            _keyboard = new TestKeyboard();
            yield return SceneManager.LoadSceneAsync("StrataDrop");
            yield return null;
            _game = Object.FindAnyObjectByType<StrataDropGame>();
            for (var i = 0; i < 30 && _game.HudRoot == null; i++) { yield return null; }
            Assert.That(_game.HudRoot, Is.Not.Null);
        }

        [UnityTearDown]
        public IEnumerator Unload()
        {
            _keyboard.Dispose();
            yield return null;
        }

        private StrataFlow Flow => _game.Flow;

        private StrataGame Game => _game.Flow.Game;

        private ActivePiece Active => Game.Active.Value;

        private string Text(string name) => _game.HudRoot.Q<Label>(name).text;

        private bool IsShown(string name) => _game.HudRoot.Q(name).style.display.value == DisplayStyle.Flex;

        private IEnumerator Begin()
        {
            yield return _keyboard.Tap(Key.Enter);
            Assert.That(Flow.Screen, Is.EqualTo(AppScreen.Playing));
        }

        private void LoseGame()
        {
            for (var i = 0; i < MaxPieces && Flow.Screen == AppScreen.Playing; i++)
            {
                while (Flow.Screen == AppScreen.Playing && Game.Phase == GamePhase.Clearing) { Flow.StepTick(); }
                Flow.Perform(GameCommand.HardDrop);
            }

            Assert.That(Flow.Screen, Is.EqualTo(AppScreen.Result));
        }

        private void PlayUntilClearing()
        {
            for (var i = 0; i < MaxPieces && Game.Phase != GamePhase.Clearing; i++)
            {
                foreach (var command in Autopilot.PlanPiece(Game)) { Flow.Perform(command); }
            }

            Assert.That(Game.Phase, Is.EqualTo(GamePhase.Clearing));
        }

        private void SetFocus(bool focus) => _game.SendMessage("OnApplicationFocus", focus);

        private static void SendClick(Button button)
        {
            var point = button.worldBound.center;
            using (var down = PointerDownEvent.GetPooled(new Event { type = EventType.MouseDown, mousePosition = point, button = 0 }))
            {
                down.target = button;
                button.SendEvent(down);
            }

            using (var up = PointerUpEvent.GetPooled(new Event { type = EventType.MouseUp, mousePosition = point, button = 0 }))
            {
                up.target = button;
                button.SendEvent(up);
            }
        }

        private void AssertViewMatchesGame()
        {
            _game.Render();
            var view = _game.View;
            var locked = 0;
            for (var y = 0; y < StrataDropView.DrawnRows; y++)
            {
                for (var x = 0; x < Board.Width; x++)
                {
                    var kind = Game.Board.At(x, y);
                    if (kind.HasValue) { locked++; }
                    var expected = kind.HasValue ? view.OreSprite(kind.Value) : null;
                    Assert.That(view.LockedSpriteAt(x, y), Is.SameAs(expected), $"({x}, {y})");
                }
            }

            Assert.That(view.ShownLockedCells, Is.EqualTo(locked));
            if (Game.Active is not ActivePiece piece) { return; }
            for (var i = 0; i < PieceShapes.CellCount; i++)
            {
                var cell = piece.CellAt(i);
                var distance = (view.ActiveCellPosition(i) - view.CellPosition(cell.X, cell.Y)).magnitude;
                Assert.That(distance, Is.LessThan(.001f), $"{i} 番目のマス");
            }
        }

        [Test]
        public void SettingsAssetIsValid()
        {
            Assert.DoesNotThrow(_game.Settings.Validate);
        }

        [UnityTest]
        public IEnumerator Title_ShowsNameAndControls()
        {
            yield return null;
            Assert.That(Flow.Screen, Is.EqualTo(AppScreen.Title));
            Assert.That(Text("headline"), Is.EqualTo("STRATA DROP"));
            Assert.That(Text("message"), Does.Contain("Space"));
            Assert.That(Text("message"), Does.Contain("ホールド"));
            Assert.That(IsShown("overlay"), Is.True);
            Assert.That(IsShown("quit"), Is.True);
            Assert.That(_game.View.ShownActiveCells, Is.EqualTo(0));
        }

        [UnityTest]
        public IEnumerator Enter_StartsTheGameAndHidesTheOverlay()
        {
            yield return Begin();
            yield return null;
            Assert.That(IsShown("overlay"), Is.False);
            Assert.That(IsShown("stats"), Is.True);
            Assert.That(_game.View.ShownActiveCells, Is.EqualTo(PieceShapes.CellCount));
            Assert.That(_game.View.ShownGhostCells, Is.EqualTo(PieceShapes.CellCount));
            Assert.That(_game.View.ShownNextCells, Is.EqualTo(StrataGame.PreviewCount * PieceShapes.CellCount));
        }

        [UnityTest]
        public IEnumerator ArrowKeys_MoveThePiece()
        {
            yield return Begin();
            var startX = Active.X;
            yield return _keyboard.Tap(Key.LeftArrow);
            Assert.That(Active.X, Is.EqualTo(startX - 1));
            yield return _keyboard.Tap(Key.D);
            Assert.That(Active.X, Is.EqualTo(startX));
            AssertViewMatchesGame();
        }

        [UnityTest]
        public IEnumerator HeldKey_RepeatsUntilTheWall()
        {
            yield return Begin();
            _keyboard.Hold(Key.LeftArrow);
            var deadline = Time.realtimeSinceStartup + WaitLimitSeconds;
            while (Game.Apply(GameCommand.MoveLeft) && Time.realtimeSinceStartup < deadline) { yield return null; }
            _keyboard.Hold();
            yield return null;
            Assert.That(Game.Apply(GameCommand.MoveLeft), Is.False);
        }

        [UnityTest]
        public IEnumerator RotateKeys_TurnThePieceBothWays()
        {
            yield return Begin();
            if (Active.Kind == PieceKind.O) { Flow.Perform(GameCommand.HardDrop); }
            var start = Active.Rotation;

            yield return _keyboard.Tap(Key.UpArrow);
            Assert.That(Active.Rotation, Is.EqualTo(PieceShapes.Turn(start, true)));
            yield return _keyboard.Tap(Key.Z);
            Assert.That(Active.Rotation, Is.EqualTo(start));
            yield return _keyboard.Tap(Key.X);
            Assert.That(Active.Rotation, Is.EqualTo(PieceShapes.Turn(start, true)));
        }

        [UnityTest]
        public IEnumerator Space_LocksThePieceAndShowsIt()
        {
            yield return Begin();
            yield return _keyboard.Tap(Key.Space);
            Assert.That(_game.View.ShownLockedCells, Is.EqualTo(PieceShapes.CellCount));
            AssertViewMatchesGame();
        }

        [UnityTest]
        public IEnumerator C_HoldsThePieceOnce()
        {
            yield return Begin();
            var kind = Active.Kind;
            yield return _keyboard.Tap(Key.C);
            Assert.That(Game.HoldKind, Is.EqualTo(kind));
            Assert.That(_game.View.ShownHoldCells, Is.EqualTo(PieceShapes.CellCount));

            var next = Active.Kind;
            yield return _keyboard.Tap(Key.C);
            Assert.That(Active.Kind, Is.EqualTo(next));
        }

        [UnityTest]
        public IEnumerator Time_MakesThePieceFall()
        {
            yield return Begin();
            var startY = Active.Y;
            var deadline = Time.realtimeSinceStartup + WaitLimitSeconds;
            while (Active.Y == startY && Time.realtimeSinceStartup < deadline) { yield return null; }
            Assert.That(Active.Y, Is.LessThan(startY));
        }

        [UnityTest]
        public IEnumerator Clearing_FlashesTheRowsThenRemovesThem()
        {
            yield return Begin();
            PlayUntilClearing();
            var rows = Game.ClearingRows.Count;
            _game.Render();
            Assert.That(_game.View.ShownFlashes, Is.EqualTo(rows));

            var deadline = Time.realtimeSinceStartup + WaitLimitSeconds;
            while (Game.Phase == GamePhase.Clearing && Time.realtimeSinceStartup < deadline) { yield return null; }
            yield return null;
            Assert.That(_game.View.ShownFlashes, Is.EqualTo(0));
            AssertViewMatchesGame();
        }

        [UnityTest]
        public IEnumerator Escape_PausesAndEnterResumes()
        {
            yield return Begin();
            yield return _keyboard.Tap(Key.Escape);
            Assert.That(Flow.Screen, Is.EqualTo(AppScreen.Paused));
            Assert.That(Text("headline"), Is.EqualTo("一時停止"));
            Assert.That(IsShown("restart"), Is.True);

            var ticks = Game.TickCount;
            yield return null;
            yield return null;
            Assert.That(Game.TickCount, Is.EqualTo(ticks));

            yield return _keyboard.Tap(Key.Enter);
            Assert.That(Flow.Screen, Is.EqualTo(AppScreen.Playing));
        }

        [UnityTest]
        public IEnumerator PauseScreen_RestartsAndReturnsToTitle()
        {
            yield return Begin();
            var seed = Flow.GameSeed;
            yield return _keyboard.Tap(Key.P);
            yield return _keyboard.Tap(Key.R);
            Assert.That(Flow.Screen, Is.EqualTo(AppScreen.Playing));
            Assert.That(Flow.GameSeed, Is.Not.EqualTo(seed));

            yield return _keyboard.Tap(Key.P);
            yield return _keyboard.Tap(Key.T);
            Assert.That(Flow.Screen, Is.EqualTo(AppScreen.Title));
            yield return null;
            Assert.That(_game.View.ShownLockedCells, Is.EqualTo(0));
            Assert.That(_game.View.ShownNextCells, Is.EqualTo(0));
        }

        [UnityTest]
        public IEnumerator FullCycle_StartLoseResultAndReplay()
        {
            yield return Begin();
            var seed = Flow.GameSeed;
            LoseGame();
            yield return null;
            Assert.That(Text("headline"), Is.EqualTo("採掘終了"));
            Assert.That(Text("message"), Does.Contain("スコア"));
            Assert.That(IsShown("overlay"), Is.True);

            yield return _keyboard.Tap(Key.Space);
            Assert.That(Flow.Screen, Is.EqualTo(AppScreen.Result));

            yield return _keyboard.Tap(Key.Enter);
            Assert.That(Flow.Screen, Is.EqualTo(AppScreen.Playing));
            Assert.That(Flow.GameSeed, Is.Not.EqualTo(seed));
            Assert.That(Game.Board, Is.EqualTo(Board.Empty));
            Assert.That(IsShown("overlay"), Is.False);
        }

        [UnityTest]
        public IEnumerator FocusLoss_PausesAndStaysPausedAfterReturning()
        {
            yield return Begin();
            SetFocus(false);
            yield return null;
            Assert.That(Flow.Screen, Is.EqualTo(AppScreen.Paused));

            SetFocus(true);
            yield return null;
            yield return null;
            Assert.That(Flow.Screen, Is.EqualTo(AppScreen.Paused));
        }

        [UnityTest]
        public IEnumerator FocusLoss_DropsAPauseKeyPressedInTheSameFrame()
        {
            yield return Begin();
            _keyboard.Hold(Key.Escape);
            yield return null;
            SetFocus(false);
            _keyboard.Hold();
            yield return null;
            SetFocus(true);
            yield return null;
            yield return null;
            Assert.That(Flow.Screen, Is.EqualTo(AppScreen.Paused));
        }

        [UnityTest]
        public IEnumerator FocusReturn_IgnoresTheClickThatSelectedTheWindow()
        {
            var quits = 0;
            _game.QuitHandler = () => quits++;
            yield return null;
            yield return null;
            var quit = _game.HudRoot.Q<Button>("quit");

            SetFocus(false);
            yield return null;
            SetFocus(true);
            SendClick(quit);
            yield return null;
            yield return null;
            Assert.That(quits, Is.EqualTo(0));

            SendClick(quit);
            yield return null;
            yield return null;
            Assert.That(quits, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator QuitButton_IsHiddenOutsideOfTheTitle()
        {
            yield return Begin();
            yield return null;
            Assert.That(IsShown("quit"), Is.False);

            Flow.TogglePause();
            yield return null;
            Assert.That(IsShown("quit"), Is.False);
        }

        [UnityTest]
        public IEnumerator Buttons_FollowTheSameRulesAsTheKeys()
        {
            yield return null;
            yield return null;
            SendClick(_game.HudRoot.Q<Button>("submit"));
            yield return null;
            yield return null;
            Assert.That(Flow.Screen, Is.EqualTo(AppScreen.Playing));

            Flow.TogglePause();
            yield return null;
            yield return null;
            yield return null;
            SendClick(_game.HudRoot.Q<Button>("title"));
            yield return null;
            yield return null;
            Assert.That(Flow.Screen, Is.EqualTo(AppScreen.Title));
        }

        [UnityTest]
        public IEnumerator View_FollowsALongerGame()
        {
            yield return Begin();
            for (var i = 0; i < 30 && Flow.Screen == AppScreen.Playing; i++)
            {
                while (Game.Phase == GamePhase.Clearing) { Flow.StepTick(); }
                foreach (var command in Autopilot.PlanPiece(Game)) { Flow.Perform(command); }
                AssertViewMatchesGame();
                yield return null;
            }
        }
    }
}
