using System;
using System.Collections;
using Game.App;
using Game.Core;
using NUnit.Framework;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Game.Unity.Tests
{
    public sealed class EmberSceneTests
    {
        private const long BackgroundBytes = 64;

        private TestKeyboard _keyboard;
        private EmberGame _game;

        [UnitySetUp]
        public IEnumerator Load()
        {
            _keyboard = new TestKeyboard();
            yield return SceneManager.LoadSceneAsync("EmberDuel");
            yield return null;
            _game = Object.FindAnyObjectByType<EmberGame>();
            for (var i = 0; i < 30 && _game.HudRoot == null; i++) { yield return null; }
            Assert.That(_game.HudRoot, Is.Not.Null);
            Time.timeScale = 0f;
        }

        [UnityTearDown]
        public IEnumerator Unload()
        {
            Time.timeScale = 1f;
            _keyboard.Dispose();
            yield return null;
        }

        [UnityTest]
        public IEnumerator KeysProduceLightSpecialAndThrow()
        {
            yield return StartMatch();
            _keyboard.Hold(Key.J);
            yield return null;
            _game.Flow.StepTick();
            Assert.That(_game.Flow.Match.Duel.At(0).Move, Is.Not.Null);
            Assert.That(_game.Flow.Match.Duel.At(0).Move.Id, Is.EqualTo("light"));
            _keyboard.Hold();
            yield return null;

            yield return UntilIdle();
            _keyboard.Hold(Key.S);
            yield return null;
            _game.Flow.StepTick();
            _keyboard.Hold(Key.S, Key.D);
            yield return null;
            _game.Flow.StepTick();
            _keyboard.Hold(Key.D, Key.J);
            yield return null;
            _game.Flow.StepTick();
            Assert.That(_game.Flow.Match.Duel.At(0).Move, Is.Not.Null);
            Assert.That(_game.Flow.Match.Duel.At(0).Move.Id, Is.EqualTo("special"));
            _keyboard.Hold();
            yield return null;

            yield return UntilIdle();
            var thrown = false;
            for (var i = 0; i < 900 && !thrown; i++)
            {
                var before = _game.Flow.Match.Duel;
                if (before.CanAct(0) && Gap() <= 40 * 256) { _keyboard.Hold(Key.J, Key.K); }
                else { _keyboard.Hold(Key.D); }
                yield return null;
                _game.Flow.StepTick();
                var duel = _game.Flow.Match.Duel;
                if (duel.At(0).State == FighterState.Thrown || duel.At(1).State == FighterState.Thrown) { thrown = true; }
                if (duel.At(0).Move != null && duel.At(0).Move.Id == "throw") { thrown = true; }
            }

            Assert.That(thrown, Is.True);
        }

        [UnityTest]
        public IEnumerator DrawnPoseMatchesTheDuelAndTitleRestoresTheBootPicture()
        {
            var bootLeft = _game.Stage.Left.transform.localPosition;
            var bootRight = _game.Stage.Right.transform.localPosition;
            var bootFlip = _game.Stage.Left.flipX;
            Assert.That(_game.Stage.Mismatch(_game.Flow.Config), Is.Null);
            _game.Stage.Left.transform.localPosition += new Vector3(8f, 0f, 0f);
            Assert.That(_game.Stage.Mismatch(_game.Flow.Config), Does.Contain("左"));
            _game.Render();

            yield return StartMatch();
            _keyboard.Hold(Key.D);
            yield return null;
            _game.Flow.StepTick();
            _game.Render();
            var duel = _game.Flow.Match.Duel;
            Assert.That(_game.Stage.Left.transform.localPosition.x, Is.EqualTo(duel.At(0).X / 256f).Within(0.01f));
            Assert.That(_game.Stage.Left.transform.localPosition.y, Is.EqualTo(duel.At(0).Y / 256f).Within(0.01f));
            Assert.That(_game.Stage.Left.flipX, Is.EqualTo(duel.At(0).Facing < 0));
            Assert.That(_game.Stage.Right.flipX, Is.EqualTo(duel.At(1).Facing < 0));

            _game.Flow.Apply(new Intent(IntentKind.Overlay, 0));
            _game.Render();
            var mesh = _game.Stage.BoxMesh.vertices;
            var drawn = false;
            for (var i = 0; i < mesh.Length; i++)
            {
                if (mesh[i].sqrMagnitude > 1f) { drawn = true; }
            }

            Assert.That(drawn, Is.True);

            _keyboard.Hold(Key.Backspace);
            yield return null;
            Assert.That(_game.Flow.Screen, Is.EqualTo(DuelScreen.Title));
            Assert.That(_game.Stage.Left.transform.localPosition, Is.EqualTo(bootLeft));
            Assert.That(_game.Stage.Right.transform.localPosition, Is.EqualTo(bootRight));
            Assert.That(_game.Stage.Left.flipX, Is.EqualTo(bootFlip));
        }

        [UnityTest]
        public IEnumerator PilotFinishesAndFocusDropsInput()
        {
            _game.Flow.PlayerPilot = new CpuPilot(0, 4, PilotStyle.Cpu);
            yield return StartMatch();
            var guard = 0;
            while (_game.Flow.Screen != DuelScreen.Result && guard++ < 40000) { _game.Flow.StepTick(); }
            Assert.That(_game.Flow.Screen, Is.EqualTo(DuelScreen.Result));

            _game.Flow.Apply(new Intent(IntentKind.Submit, 0));
            for (var i = 0; i < _game.Flow.Config.IntroTicks; i++) { _game.Flow.StepTick(); }
            Assert.That(_game.Flow.Screen, Is.EqualTo(DuelScreen.Fighting));
            var tick = _game.Flow.Match.Duel.Tick;
            _game.SetFocus(false);
            Assert.That(_game.Flow.Screen, Is.EqualTo(DuelScreen.Paused));
            _game.Flow.StepTick();
            Assert.That(_game.Flow.Match.Duel.Tick, Is.EqualTo(tick));
        }

        [UnityTest]
        public IEnumerator StepAndDrawStayCheap()
        {
            yield return StartMatch();
            const int Samples = 120;
            var step = new double[Samples];
            var render = new double[Samples];
            var watch = new System.Diagnostics.Stopwatch();
            for (var i = 0; i < Samples; i++)
            {
                watch.Restart();
                _game.Flow.StepTick();
                step[i] = watch.Elapsed.TotalMilliseconds;
                watch.Restart();
                _game.Stage.Show(_game.Flow.Match.Duel, false);
                render[i] = watch.Elapsed.TotalMilliseconds;
            }

            const int Frames = 30;
            var renderFrames = new long[Frames];
            var idleFrames = new long[Frames];
            using var recorder = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame");
            for (var i = 0; i < 8; i++) { yield return null; }
            var baseline = recorder.LastValue;
            for (var i = 0; i < Frames; i++)
            {
                _game.Stage.Show(_game.Flow.Match.Duel, false);
                yield return null;
                renderFrames[i] = recorder.LastValue - baseline;
                yield return null;
                idleFrames[i] = recorder.LastValue - baseline;
            }

            GC.KeepAlive(new byte[4096]);
            yield return null;
            var probe = recorder.LastValue;
            Array.Sort(step);
            Array.Sort(render);
            long renderMax = 0;
            var renderNonZero = 0;
            var idleNonZero = 0;
            for (var i = 0; i < Frames; i++)
            {
                var extra = renderFrames[i] < 0 ? 0 : renderFrames[i];
                var idle = idleFrames[i] < 0 ? 0 : idleFrames[i];
                renderMax = Math.Max(renderMax, extra);
                if (extra > BackgroundBytes) { renderNonZero++; }
                if (idle > BackgroundBytes) { idleNonZero++; }
            }

            TestContext.WriteLine(
                $"Step p95 {step[Samples * 95 / 100]:F3} ms、描画 p95 {render[Samples * 95 / 100]:F3} ms、" +
                $"描画の割り当て 最大 {renderMax}（{renderNonZero}/{Frames}、無処理 {idleNonZero}、基準 {baseline}、確認 {probe}）");
            Assert.That(recorder.Valid, Is.True);
            Assert.That(probe >= 4096 || probe - baseline >= 4096, Is.True, "probe " + probe + " baseline " + baseline);
            Assert.That(step[Samples * 95 / 100], Is.LessThan(4));
            Assert.That(render[Samples * 95 / 100], Is.LessThan(4));
            Assert.That(renderNonZero, Is.LessThanOrEqualTo(Frames / 5));
            Assert.That(renderMax, Is.LessThanOrEqualTo(BackgroundBytes));
        }

        private IEnumerator StartMatch()
        {
            yield return _keyboard.Tap(Key.Enter);
            Assert.That(_game.Flow.Screen, Is.EqualTo(DuelScreen.Select));
            yield return _keyboard.Tap(Key.Enter);
            Assert.That(_game.Flow.Screen, Is.EqualTo(DuelScreen.Intro));
            for (var i = 0; i < _game.Flow.Config.IntroTicks; i++)
            {
                yield return null;
                _game.Flow.StepTick();
            }

            Assert.That(_game.Flow.Screen, Is.EqualTo(DuelScreen.Fighting));
        }

        private IEnumerator UntilIdle()
        {
            _keyboard.Hold();
            for (var i = 0; i < 80 && !_game.Flow.Match.Duel.CanAct(0); i++)
            {
                yield return null;
                _game.Flow.StepTick();
            }
        }

        private int Gap()
        {
            var duel = _game.Flow.Match.Duel;
            return Math.Abs(duel.At(0).X - duel.At(1).X);
        }
    }
}
