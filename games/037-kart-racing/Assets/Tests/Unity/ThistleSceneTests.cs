using System;
using System.Collections;
using Game.App;
using Game.Core;
using NUnit.Framework;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace Game.Unity.Tests
{
    public sealed class ThistleSceneTests
    {
        private ThistleGame _game;

        [UnitySetUp]
        public IEnumerator Load()
        {
            yield return SceneManager.LoadSceneAsync("ThistleCircuit");
            yield return null;
            _game = Object.FindAnyObjectByType<ThistleGame>();
            for (var i = 0; i < 30 && _game.HudRoot == null; i++) { yield return null; }
            _game.enabled = false;
        }

        [UnityTest]
        public IEnumerator TheTitleNamesTheCircuit()
        {
            _game.Render();
            yield return null;
            Assert.That(_game.HudRoot.Q<Label>("body").text, Does.Contain("THISTLE CIRCUIT"));
        }

        [UnityTest]
        public IEnumerator TheStageMatchesTheTrack()
        {
            var track = Track.Parse(_game.Settings.Track.text, _game.Settings.Validate());
            Assert.That(_game.Stage.Mismatch(track), Is.Null);
            var marker = _game.Stage.transform.Find("Marker-0");
            var original = marker.localPosition;
            marker.localPosition += Vector3.right;
            Assert.That(_game.Stage.Mismatch(track), Is.Not.Null);
            marker.localPosition = original;
            Assert.That(_game.Stage.Mismatch(track), Is.Null);
            yield return null;
        }

        [UnityTest]
        public IEnumerator TheKartSpriteFollowsTheCorePose()
        {
            _game.Flow.Apply(new Intent(IntentKind.Submit, 0));
            _game.Flow.Apply(new Intent(IntentKind.AccelDown, 0));
            for (var i = 0; i < _game.Settings.Rules.StartDelayTicks + 20; i++) { _game.Flow.StepTick(); }
            _game.Render();
            var kart = _game.Flow.Race.KartAt(0);
            var view = _game.Stage.transform.Find("Kart-0");
            var expected = ThistleLayout.At(kart.Position);
            Assert.That((view.localPosition - expected).sqrMagnitude, Is.LessThan(0.0001f));
            var angle = Mathf.DeltaAngle(view.localEulerAngles.z, ThistleLayout.Degrees(kart.Heading));
            Assert.That(Mathf.Abs(angle), Is.LessThan(0.1f));
            yield return null;
        }

        [UnityTest]
        public IEnumerator FocusClearsADriftWithoutABoost()
        {
            var flow = _game.Flow;
            flow.Apply(new Intent(IntentKind.Submit, 0));
            flow.Apply(new Intent(IntentKind.AccelDown, 0));
            flow.Apply(new Intent(IntentKind.SteerLeftDown, 0));
            flow.Apply(new Intent(IntentKind.DriftDown, 0));
            for (var i = 0; i < 250 && flow.Race.KartAt(0).DriftTicks == 0; i++) { flow.StepTick(); }
            Assert.That(flow.Race.KartAt(0).DriftTicks, Is.GreaterThan(0));
            flow.SetFocus(false);
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Paused));
            Assert.That(flow.Race.KartAt(0).BoostLeft, Is.EqualTo(0));
            Assert.That(flow.Race.KartAt(0).DriftTicks, Is.EqualTo(0));
            yield return null;
        }

        [UnityTest]
        public IEnumerator SettingsRejectAMissingTrack()
        {
            var settings = Object.Instantiate(_game.Settings);
            settings.Track = null;
            Assert.Throws<InvalidOperationException>(() => settings.Validate());
            yield return null;
        }

        [UnityTest]
        public IEnumerator TheAutopilotFinishesAndAReplayStartsOver()
        {
            var flow = _game.Flow;
            flow.Pilot = new Autopilot();
            flow.Apply(new Intent(IntentKind.Submit, 0));
            for (var i = 0; i < 20000 && flow.Screen != AppScreen.Result; i++) { flow.StepTick(); }
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Result));
            Assert.That(flow.Won, Is.True);
            Assert.That(flow.Race.KartAt(1).Finished, Is.True);
            Assert.That(flow.Race.KartAt(2).Finished, Is.True);
            Assert.That(flow.Race.KartAt(3).Finished, Is.True);
            flow.Apply(new Intent(IntentKind.Submit, 0));
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Playing));
            Assert.That(flow.Race.Tick, Is.EqualTo(0));
            Assert.That(flow.Race.KartAt(0).Laps, Is.EqualTo(0));
            yield return null;
        }

        [UnityTest]
        public IEnumerator ReturningToTheTitleRestoresTheGrid()
        {
            var start = _game.Flow.Race.KartAt(0).Position;
            _game.Render();
            var boot = _game.Stage.transform.Find("Kart-0").localPosition;
            _game.Flow.Apply(new Intent(IntentKind.Submit, 0));
            _game.Flow.Apply(new Intent(IntentKind.AccelDown, 0));
            for (var i = 0; i < 150; i++) { _game.Flow.StepTick(); }
            _game.Flow.Apply(new Intent(IntentKind.Cancel, 0));
            _game.Flow.Apply(new Intent(IntentKind.Back, 0));
            Assert.That(_game.Flow.Screen, Is.EqualTo(AppScreen.Title));
            Assert.That(_game.Flow.Race.KartAt(0).Position, Is.EqualTo(start));
            Assert.That(_game.Flow.Race.Tick, Is.EqualTo(0));
            _game.Render();
            var back = _game.Stage.transform.Find("Kart-0").localPosition;
            Assert.That((back - boot).sqrMagnitude, Is.LessThan(0.0001f));
            yield return null;
        }

        [UnityTest]
        public IEnumerator ARaceStepAndTheWorldViewAreFastAndDoNotAllocate()
        {
            var flow = _game.Flow;
            var config = flow.Race.Config;
            var track = flow.Race.Track;
            flow.Apply(new Intent(IntentKind.Submit, 0));
            for (var i = 0; i < config.StartDelayTicks + 8; i++)
            {
                flow.Race.Step(CpuPlan.Intent(flow.Race.KartAt(0), track, config, 1000));
                _game.PresentWorld();
            }

            const int Samples = 300;
            var step = new double[Samples];
            var render = new double[Samples];
            var watch = new System.Diagnostics.Stopwatch();
            _game.enabled = false;
            using var recorder = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame");
            yield return null;
            yield return null;
            yield return null;
            var baseline = recorder.LastValue;
            for (var i = 0; i < Samples; i++)
            {
                var drive = CpuPlan.Intent(flow.Race.KartAt(0), track, config, 1000);
                watch.Restart();
                flow.Race.Step(drive);
                step[i] = watch.Elapsed.TotalMilliseconds;
                watch.Restart();
                _game.PresentWorld();
                render[i] = watch.Elapsed.TotalMilliseconds;
            }

            yield return null;
            var measured = recorder.LastValue;
            GC.KeepAlive(new byte[4096]);
            yield return null;
            var probe = recorder.LastValue;
            Array.Sort(step);
            Array.Sort(render);
            var report = $"Core の Step p50 {step[Samples / 2]:F3} ms / p95 {step[Samples * 95 / 100]:F3} ms / 最大 {step[Samples - 1]:F3} ms、" +
                $"描画の更新 p50 {render[Samples / 2]:F3} ms / p95 {render[Samples * 95 / 100]:F3} ms / 最大 {render[Samples - 1]:F3} ms、" +
                $"割り当て {measured - baseline} バイト（確認 {probe}）";
            Debug.Log("[Performance] " + report);
            TestContext.WriteLine(report);
            Assert.That(flow.Race.Phase, Is.EqualTo(RacePhase.Racing));
            Assert.That(recorder.Valid && probe >= baseline + 4096, Is.True, "割り当ての計数が働いている");
            Assert.That(measured - baseline, Is.LessThanOrEqualTo(0), "Step と描画の更新で割り当てをしない");
            Assert.That(step[Samples * 95 / 100], Is.LessThan(8));
            Assert.That(render[Samples * 95 / 100], Is.LessThan(8));
        }
    }
}
