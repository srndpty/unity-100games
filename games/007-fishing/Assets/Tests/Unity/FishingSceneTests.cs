using System.Collections;
using System.Reflection;
using Game.App;
using Game.Core;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace Game.Unity.Tests
{
    public sealed class FishingSceneTests
    {
        private TestKeyboard _keyboard;
        private FishingGame _game;
        [UnitySetUp]
        public IEnumerator Load()
        {
            _keyboard = new TestKeyboard(); yield return SceneManager.LoadSceneAsync("Fishing"); yield return null;
            _game = Object.FindAnyObjectByType<FishingGame>(); _game.ManualTicks = true;
            typeof(FishingGame).GetProperty(nameof(FishingGame.Flow)).SetValue(_game, new FishingFlow(_game.Settings.Game, 1));
            for (var i = 0; i < 30 && _game.HudRoot == null; i++) { yield return null; }
            Assert.That(_game.HudRoot, Is.Not.Null);
        }
        [UnityTearDown]
        public IEnumerator Unload() { _keyboard.Dispose(); yield return null; }
        [UnityTest]
        public IEnumerator Keyboard_SelectCastHookPauseResume()
        {
            Assert.That(_game.HudRoot.Q<Label>("headline").text, Is.EqualTo("STILLWATER"));
            yield return _keyboard.Tap(Key.Enter); yield return _keyboard.Tap(Key.RightArrow);
            Assert.That(_game.Flow.Selected, Is.EqualTo(FishKind.Bass));
            yield return _keyboard.Tap(Key.Space); Assert.That(_game.Flow.Screen, Is.EqualTo(AppScreen.Playing));
            while (_game.Flow.Game.Phase != FishingPhase.Biting) { _game.Flow.StepOneTick(); }
            yield return _keyboard.Tap(Key.Space); _game.Flow.StepOneTick(); _game.Render();
            Assert.That(_game.Flow.Game.Phase, Is.EqualTo(FishingPhase.Fighting));
            Assert.That(_game.HudRoot.Q<Label>("fishname").text, Is.EqualTo("スズキ"));
            yield return _keyboard.Tap(Key.P); Assert.That(_game.Flow.Screen, Is.EqualTo(AppScreen.Paused));
            yield return _keyboard.Tap(Key.Escape); Assert.That(_game.Flow.Screen, Is.EqualTo(AppScreen.Playing));
        }
        [UnityTest]
        public IEnumerator HeldSpace_DoesNotLeakAcrossCast()
        {
            yield return _keyboard.Tap(Key.Enter); _keyboard.Hold(Key.Space); yield return null;
            for (var i = 0; i < 5; i++) { _game.Flow.StepOneTick(); yield return null; }
            Assert.That(_game.Flow.Game.Phase, Is.EqualTo(FishingPhase.Casting));
            Assert.That(_game.Flow.Game.Finished, Is.False);
        }
        [UnityTest]
        public IEnumerator UiClick_StartSelectCast_WithoutFishingInput()
        {
            yield return Click("submit"); Assert.That(_game.Flow.Screen, Is.EqualTo(AppScreen.Select));
            yield return Click("point2"); Assert.That(_game.Flow.Selected, Is.EqualTo(FishKind.Carp));
            Assert.That(_game.Flow.Screen, Is.EqualTo(AppScreen.Playing));
            Assert.That(_game.HudRoot.Q<Button>("cast"), Is.Null);
            _game.Flow.StepOneTick();
            Assert.That(_game.Flow.Game.Phase, Is.EqualTo(FishingPhase.Casting));
        }
        [UnityTest]
        public IEnumerator HeldHook_ReelsOnFirstFightTick_WithoutRelease()
        {
            yield return _keyboard.Tap(Key.Enter); yield return _keyboard.Tap(Key.Space);
            while (_game.Flow.Game.Phase != FishingPhase.Biting) { _game.Flow.StepOneTick(); }
            _keyboard.Hold(Key.Space); yield return null; _game.Flow.StepOneTick();
            var tension = _game.Flow.Game.Tension;
            yield return null; _game.Flow.StepOneTick();
            Assert.That(_game.Flow.Game.Tension, Is.GreaterThan(tension));
            Assert.That(_game.Flow.Game.Phase, Is.EqualTo(FishingPhase.Fighting));
        }
        [UnityTest]
        public IEnumerator Fish_PatrolsApproachesAndBitesActualSurfaceBait()
        {
            var view = Object.FindAnyObjectByType<FishingView>();
            _game.Flow.Submit(); _game.Flow.Cast();
            while (_game.Flow.Game.Phase != FishingPhase.Roaming) { _game.Flow.StepOneTick(); }
            _game.Render(); var start = view.FishMouth;
            for (var i = 0; i < 20; i++) { _game.Flow.StepOneTick(); }
            _game.Render(); Assert.That(Vector2.Distance(start, view.FishMouth), Is.GreaterThan(.2f));
            while (_game.Flow.Game.Phase != FishingPhase.Biting) { _game.Flow.StepOneTick(); }
            _game.Render(); Assert.That(Vector2.Distance(view.FishMouth, view.BaitPosition), Is.LessThan(.001f));
            Assert.That(view.FishMouth.y, Is.EqualTo(view.SurfaceY - .16f).Within(.001));
            Assert.That(view.Angler.position.y, Is.GreaterThan(view.SurfaceY));
            _game.Flow.SetInput(false, false); _game.Flow.SetInput(true, true); _game.Flow.StepOneTick();
            _game.Render(); Assert.That(Vector2.Distance(view.FishMouth, view.BaitPosition), Is.LessThan(.001f));
            yield return null;
        }
        private IEnumerator Click(string name)
        {
            var button = _game.HudRoot.Q<Button>(name); var point = button.worldBound.center;
            using (var down = PointerDownEvent.GetPooled(new Event { type = EventType.MouseDown, mousePosition = point, button = 0 }))
            { down.target = button; button.SendEvent(down); }
            using (var up = PointerUpEvent.GetPooled(new Event { type = EventType.MouseUp, mousePosition = point, button = 0 }))
            { up.target = button; button.SendEvent(up); }
            yield return null; yield return null;
        }
        [UnityTest]
        public IEnumerator MissedBite_ReportsFailure_AndRetryStartsNewSeed()
        {
            _game.Flow.Submit(); _game.Flow.Cast(); var seed = _game.Flow.Game.Seed;
            while (_game.Flow.Screen == AppScreen.Playing) { _game.Flow.StepOneTick(); }
            _game.Render(); Assert.That(_game.HudRoot.Q<Label>("message").text, Does.Contain("見逃した"));
            yield return _keyboard.Tap(Key.Enter); yield return _keyboard.Tap(Key.Space);
            Assert.That(_game.Flow.Game.Seed, Is.Not.EqualTo(seed));
        }
        [UnityTest]
        public IEnumerator AllSpecies_ShowCatchAndCompleteHud()
        {
            _game.Flow.Back(); _game.Flow.Submit();
            for (var kind = 0; kind < 3; kind++)
            {
                _game.Flow.Select((FishKind)kind); _game.Flow.Cast();
                for (var tick = 0; tick < 3600 && _game.Flow.Screen == AppScreen.Playing; tick++) { Autopilot.Step(_game.Flow); }
                Assert.That(_game.Flow.Game.Phase, Is.EqualTo(FishingPhase.Caught));
                _game.Render(); Assert.That(_game.HudRoot.Q<Label>("headline").text, Does.Contain(kind == 2 ? "3種" : "釣れた"));
                if (kind < 2) { _game.Flow.Submit(); }
            }
            Assert.That(_game.Flow.Screen, Is.EqualTo(AppScreen.Complete)); yield return null;
        }
        [UnityTest]
        public IEnumerator FocusLoss_ClearsPendingPause_InBothOrders()
        {
            var focus = typeof(FishingGame).GetMethod("OnApplicationFocus", BindingFlags.NonPublic | BindingFlags.Instance);
            _game.Flow.Submit(); _game.Flow.Cast(); _keyboard.Hold(Key.P); yield return null;
            focus.Invoke(_game, new object[] { false }); yield return null;
            Assert.That(_game.Flow.Screen, Is.EqualTo(AppScreen.Paused));
            focus.Invoke(_game, new object[] { true }); _keyboard.Hold(); yield return null;
            yield return _keyboard.Tap(Key.P); Assert.That(_game.Flow.Screen, Is.EqualTo(AppScreen.Playing));
            _keyboard.Hold(Key.P); focus.Invoke(_game, new object[] { false }); yield return null;
            Assert.That(_game.Flow.Screen, Is.EqualTo(AppScreen.Paused)); focus.Invoke(_game, new object[] { true });
        }
        [UnityTest]
        public IEnumerator DangerAndStaminaHud_MatchesState()
        {
            _game.Flow.Submit(); _game.Flow.Cast();
            while (_game.Flow.Game.Phase != FishingPhase.Biting) { _game.Flow.StepOneTick(); }
            _game.Flow.SetInput(false, false); _game.Flow.SetInput(true, true); _game.Flow.StepOneTick();
            typeof(FishingState).GetProperty(nameof(FishingState.HighTicks)).SetValue(_game.Flow.Game, 30);
            _game.Render(); Assert.That(_game.HudRoot.Q<Label>("warning").text, Does.Contain("糸切れ"));
            Assert.That(_game.HudRoot.Q<ProgressBar>("stamina").value, Is.EqualTo(100).Within(.1)); yield return null;
            typeof(FishingState).GetProperty(nameof(FishingState.Stamina)).SetValue(_game.Flow.Game, 0d);
            _game.Render(); Assert.That(_game.HudRoot.Q<Label>("stamina-label").text, Does.Contain("0 / 60"));
        }
        [UnityTest]
        public IEnumerator LandscapeFraming_ContainsField()
        {
            var camera = Camera.main; var original = camera.aspect;
            foreach (var aspect in new[] { 1.6f, 16f / 9, 21f / 9 })
            {
                camera.aspect = aspect; _game.Render();
                var point = camera.WorldToViewportPoint(new Vector3(13, -7, 0));
                Assert.That(point.x, Is.InRange(0, 1)); Assert.That(point.y, Is.InRange(0, 1));
            }
            camera.aspect = original; yield return null;
        }
        [Test]
        public void SavedStagePrefab_HasVisibleSpritesAndEditableAnchors()
        {
            var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Unity/Art/FishingStage.prefab");
            Assert.That(prefab, Is.Not.Null);
            Assert.That(prefab.GetComponentsInChildren<SpriteRenderer>().Length, Is.GreaterThanOrEqualTo(5));
            Assert.That(prefab.transform.Find("Angler"), Is.Not.Null);
            Assert.That(prefab.transform.Find("RodHand"), Is.Not.Null);
            Assert.That(prefab.transform.Find("RodTip"), Is.Not.Null);
            foreach (var renderer in prefab.GetComponentsInChildren<SpriteRenderer>())
            { Assert.That(UnityEditor.AssetDatabase.Contains(renderer.sprite), Is.True); }
        }
        [UnityTest]
        public IEnumerator Reload_KeepsImportedSpritesAndFixedRendererCount()
        {
            var stage = Object.FindAnyObjectByType<FishingView>();
            var count = stage.GetComponentsInChildren<SpriteRenderer>().Length;
            var sprite = stage.GetComponentInChildren<SpriteRenderer>().sprite;
            var path = UnityEditor.AssetDatabase.GetAssetPath(sprite);
            yield return SceneManager.LoadSceneAsync("Fishing"); yield return null; yield return null;
            var reloaded = Object.FindAnyObjectByType<FishingView>();
            Assert.That(path, Is.Not.Empty);
            Assert.That(UnityEditor.AssetDatabase.GetAssetPath(reloaded.GetComponentInChildren<SpriteRenderer>().sprite), Is.EqualTo(path));
            Assert.That(reloaded.GetComponentsInChildren<SpriteRenderer>().Length, Is.EqualTo(count));
        }
    }
}
