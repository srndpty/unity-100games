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
        public IEnumerator TheTitleScreenNamesTheCircuitAndHidesTheRaceHud()
        {
            _game.Render();
            yield return null;
            var title = _game.HudRoot.Q("title-screen");
            Assert.That(title.resolvedStyle.display, Is.EqualTo(DisplayStyle.Flex));
            Assert.That(_game.HudRoot.Q("race-hud").resolvedStyle.display, Is.EqualTo(DisplayStyle.None));
            var words = title.Query<Label>().ToList().ConvertAll(label => label.text);
            Assert.That(words, Has.Member("THISTLE"));
            Assert.That(words, Has.Member("CIRCUIT"));
            Assert.That(title.Q<Label>("title-start").text, Does.StartWith("Enter"));

            _game.Flow.Apply(new Intent(IntentKind.Submit, 0));
            _game.Render();
            yield return null;
            Assert.That(title.resolvedStyle.display, Is.EqualTo(DisplayStyle.None));
            Assert.That(_game.HudRoot.Q("race-hud").resolvedStyle.display, Is.EqualTo(DisplayStyle.Flex));
        }

        [UnityTest]
        public IEnumerator TheTitleCameraShowsTheWholeCircuitBesideTheTitleCard()
        {
            _game.Render();
            yield return null;
            var track = Track.Parse(_game.Settings.Track.text, _game.Settings.Validate());
            ThistleTrackMesh.Bounds(track, out var min, out var max);
            var cardShare = 1f - ThistleGame.TitleViewShare;
            foreach (var corner in new[] { new Vector3(min.x, min.y), new Vector3(max.x, min.y), new Vector3(min.x, max.y), new Vector3(max.x, max.y) })
            {
                var viewport = _game.View.WorldToViewportPoint(corner);
                Assert.That(viewport.x, Is.InRange(cardShare, 1f), $"{corner} が札に隠れるか画面の外");
                Assert.That(viewport.y, Is.InRange(0f, 1f), $"{corner} が画面の外");
            }
        }

        [UnityTest]
        public IEnumerator TheStageMatchesTheTrack()
        {
            var track = Track.Parse(_game.Settings.Track.text, _game.Settings.Validate());
            Assert.That(_game.Stage.Mismatch(track), Is.Null);
            var filter = _game.Stage.transform.Find(ThistleStage.CircuitName).GetComponent<MeshFilter>();
            var original = filter.sharedMesh;
            var moved = Object.Instantiate(original);
            try
            {
                var vertices = moved.vertices;
                vertices[vertices.Length / 2] += Vector3.right * 0.5f;
                moved.vertices = vertices;
                filter.sharedMesh = moved;
                Assert.That(_game.Stage.Mismatch(track), Does.Contain("頂点"));
            }
            finally
            {
                filter.sharedMesh = original;
                Object.Destroy(moved);
            }

            Assert.That(_game.Stage.Mismatch(track), Is.Null);
            var text = _game.Settings.Track.text.Replace("runoff 3000", "runoff 2500", StringComparison.Ordinal);
            Assert.That(_game.Stage.Mismatch(Track.Parse(text, _game.Settings.Validate())), Is.Not.Null, "芝の幅を変えたコースとは一致しない");
            yield return null;
        }

        [UnityTest]
        public IEnumerator TheMinimapPutsEveryKartInsideItsBox()
        {
            var flow = _game.Flow;
            flow.Pilot = new Autopilot();
            flow.Apply(new Intent(IntentKind.Submit, 0));
            for (var i = 0; i < 1200; i++) { flow.StepTick(); }
            _game.Render();
            yield return null;
            var minimap = _game.Hud.Minimap;
            Assert.That(minimap.resolvedStyle.width, Is.EqualTo(ThistleMinimap.MapWidth).Within(2f));
            Assert.That(minimap.resolvedStyle.height, Is.EqualTo(ThistleMinimap.MapHeight).Within(2f));
            for (var i = 0; i < KartConfig.KartCount; i++)
            {
                var point = minimap.KartPoint(i);
                var mapped = minimap.Frame.Map(flow.Race.KartAt(i).Position);
                Assert.That(point.x, Is.EqualTo(mapped.X));
                Assert.That(point.x, Is.InRange(0f, ThistleMinimap.MapWidth), $"車 {i}");
                Assert.That(point.y, Is.InRange(0f, ThistleMinimap.MapHeight), $"車 {i}");
            }
        }

        [UnityTest]
        public IEnumerator TheResultOpensWhenThePlayerFinishesAndListsRunningCpus()
        {
            var flow = _game.Flow;
            flow.Pilot = new Autopilot();
            flow.Apply(new Intent(IntentKind.Submit, 0));
            for (var i = 0; i < 20000 && flow.Screen != AppScreen.Result; i++) { flow.StepTick(); }
            Assert.That(flow.Race.Tick, Is.EqualTo(flow.Race.KartAt(0).FinishTick));
            _game.Render();
            yield return null;
            Assert.That(_game.HudRoot.Q("result-screen").resolvedStyle.display, Is.EqualTo(DisplayStyle.Flex));
            Assert.That(_game.HudRoot.Q<Label>("result-title").text, Is.EqualTo("勝利"));
            var rows = _game.HudRoot.Q("result-rows").Query<Label>(className: "result-time").ToList();
            Assert.That(rows, Has.Count.EqualTo(KartConfig.KartCount));
            Assert.That(rows[0].text, Is.EqualTo(ThistleHud.Time(flow.Race.KartAt(0).FinishTick)));
            Assert.That(rows.Exists(label => label.text.StartsWith("走行中", StringComparison.Ordinal)), Is.True, "CPU の完走を待たずに出る");
        }

        [UnityTest]
        public IEnumerator TheLaunchSmokesAndTheTitleClearsIt()
        {
            var flow = _game.Flow;
            flow.Apply(new Intent(IntentKind.Submit, 0));
            flow.Apply(new Intent(IntentKind.AccelDown, 0));
            _game.Render();
            Assert.That(_game.Stage.Smoke.Alive, Is.EqualTo(0), "スタート前は出ない");
            for (var i = 0; i < _game.Settings.Rules.StartDelayTicks + 6; i++) { flow.StepTick(); }
            Assert.That(TyreSmoke.Of(flow.Race.KartAt(0)) & Smoke.Launch, Is.EqualTo(Smoke.Launch));
            _game.Render();
            Assert.That(_game.Stage.Smoke.Alive, Is.GreaterThan(0), "スタートの加速で煙が出る");

            flow.Apply(new Intent(IntentKind.Cancel, 0));
            flow.Apply(new Intent(IntentKind.Back, 0));
            _game.Render();
            Assert.That(_game.Stage.Smoke.Alive, Is.EqualTo(0), "タイトルに戻ると消える");
            yield return null;
        }

        [UnityTest]
        public IEnumerator TheSmokeIsTheSameForTheSameTicks()
        {
            var first = SmokeAfter(_game, 24);
            _game.Flow.Apply(new Intent(IntentKind.Cancel, 0));
            _game.Flow.Apply(new Intent(IntentKind.Back, 0));
            _game.Render();
            var second = SmokeAfter(_game, 24);
            Assert.That(second, Is.EqualTo(first));
            Assert.That(first, Is.Not.Empty);
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
        public IEnumerator AfterResumeTheArrowKeysWorkWhileWAndAAreStillHeld()
        {
            using var keyboard = new TestKeyboard();
            var flow = _game.Flow;
            _game.enabled = true; // キーを読む Update を動かす（他のテストは Flow を直接進めるので止めている）
            yield return keyboard.Tap(Key.Enter);
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Playing));
            keyboard.Hold(Key.W, Key.A);
            yield return null;
            yield return null;
            Assert.That(flow.Held.Accelerate, Is.True);
            Assert.That(flow.Held.Steer, Is.EqualTo(1));

            keyboard.Hold(Key.W, Key.A, Key.Escape);
            yield return null;
            keyboard.Hold(Key.W, Key.A);
            yield return null;
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Paused));
            keyboard.Hold(Key.W, Key.A, Key.Enter);
            yield return null;
            keyboard.Hold(Key.W, Key.A);
            yield return null;
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Playing));
            Assert.That(flow.Held.Accelerate, Is.False, "押したままの W は効かない");
            Assert.That(flow.Held.Steer, Is.EqualTo(0), "押したままの A は効かない");

            keyboard.Hold(Key.W, Key.A, Key.UpArrow, Key.LeftArrow);
            yield return null;
            yield return null;
            Assert.That(flow.Held.Accelerate, Is.True, "新しく押した ↑ は効く");
            Assert.That(flow.Held.Steer, Is.EqualTo(1), "新しく押した ← は効く");

            keyboard.Hold(Key.W, Key.A);
            yield return null;
            yield return null;
            Assert.That(flow.Held.Accelerate, Is.False, "↑ を離すと止まる");
            Assert.That(flow.Held.Steer, Is.EqualTo(0), "← を離すと止まる");
            keyboard.Hold();
            yield return null;
        }

        [UnityTest]
        public IEnumerator AKeyTappedBetweenFramesArrivesWithItsOwnTimes()
        {
            // フレームの間に押して離したキーも、押下と解放の 2 つの意図として、入力が起きた時刻のまま溜まる。
            using var keyboard = new TestKeyboard();
            using var input = new ThistleInput();
            input.Enable();
            yield return null;
            var now = ThistleInput.Now;
            var down = now - 0.2;
            var up = now - 0.1;
            keyboard.HoldAt(down, Key.W);
            keyboard.HoldAt(up);
            keyboard.HoldAt(up, Key.LeftArrow);
            yield return null;

            Assert.That(input.Pending, Has.Count.EqualTo(3));
            AssertIntent(input.Pending[0], IntentKind.AccelDown, 4, down);
            AssertIntent(input.Pending[1], IntentKind.AccelUp, 4, up);
            AssertIntent(input.Pending[2], IntentKind.SteerLeftDown, 1, up);
            keyboard.Hold();
            yield return null;
        }

        [UnityTest]
        public IEnumerator TheTappedKeyDrivesTheTicksBetweenItsTimes()
        {
            // フレームの間の短い押下（2.5 Tick）を、押下・解放を 1 フレームでまとめて渡しても、別々のフレームで渡しても同じ状態になる。
            // フレームの時刻で反映していたら、まとめて渡したほうは加速しない。
            var config = _game.Settings.Validate();
            var track = Track.Parse(_game.Settings.Track.text, config);
            using var keyboard = new TestKeyboard();
            using var input = new ThistleInput();
            input.Enable();
            yield return null;
            const double tick = 1.0 / RaceFlow.TickRate;
            var start = ThistleInput.Now - 4.0;
            var down = start + ((config.StartDelayTicks + 5.5) * tick);
            var up = down + (2.5 * tick);
            Assert.That(up + (10 * tick), Is.LessThan(ThistleInput.Now), "入力の時刻は今より前に置く");
            var oneFrame = Started(config, track, start, down - tick);
            var perKey = Started(config, track, start, down - tick);
            var none = Started(config, track, start, down - tick);

            keyboard.HoldAt(down, Key.W);
            keyboard.HoldAt(up);
            yield return null;
            Assert.That(input.Pending, Has.Count.EqualTo(2));
            perKey.Apply(input.Pending[0]);
            perKey.Advance(down + tick);
            perKey.Apply(input.Pending[1]);
            input.ApplyTo(oneFrame);

            var end = up + (10 * tick);
            AdvanceByTicks(oneFrame, down, end);
            AdvanceByTicks(perKey, down + tick, end);
            AdvanceByTicks(none, down - tick, end);
            Assert.That(oneFrame.Race.KartAt(0).Position, Is.Not.EqualTo(none.Race.KartAt(0).Position), "押していた Tick だけ加速して進んだ");
            Assert.That(oneFrame.Race.Hash(), Is.EqualTo(perKey.Race.Hash()));
            Assert.That(oneFrame.Race.Hash(), Is.Not.EqualTo(none.Race.Hash()));
        }

        /// <summary>Input System は時刻をわずかに丸めて持つので、時刻は 1 µs 以内で比べる。</summary>
        private static void AssertIntent(Intent intent, IntentKind kind, int key, double time)
        {
            Assert.That(intent.Kind, Is.EqualTo(kind));
            Assert.That(intent.Key, Is.EqualTo(key));
            Assert.That(intent.Time, Is.EqualTo(time).Within(1e-6));
        }

        private static RaceFlow Started(KartConfig config, Track track, double start, double until)
        {
            var flow = new RaceFlow(config, track);
            flow.Apply(new Intent(IntentKind.Submit, start));
            AdvanceByTicks(flow, start, until);
            return flow;
        }

        /// <summary>1 フレームの Tick 上限に掛からないよう、1 Tick ずつ時計を進める。</summary>
        private static void AdvanceByTicks(RaceFlow flow, double from, double until)
        {
            const double tick = 1.0 / RaceFlow.TickRate;
            for (var time = from + tick; time < until; time += tick) { flow.Advance(time); }
            flow.Advance(until);
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
                flow.Race.Step(flow.Race.PlanFor(0, 1000));
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
                var drive = flow.Race.PlanFor(0, 1000);
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

        /// <summary>操縦役で <paramref name="ticks"/> Tick 走り、10 Tick ごとに描いた後の煙の粒の位置。</summary>
        private static string SmokeAfter(ThistleGame game, int ticks)
        {
            var flow = game.Flow;
            flow.Pilot = new Autopilot();
            flow.Apply(new Intent(IntentKind.Submit, 0));
            for (var i = 0; i < game.Settings.Rules.StartDelayTicks + ticks; i++)
            {
                flow.StepTick();
                if ((i % 10) == 0) { game.Render(); }
            }

            game.Render();
            var text = new System.Text.StringBuilder();
            foreach (Transform puff in game.Stage.Smoke.transform)
            {
                var sprite = puff.GetComponent<SpriteRenderer>();
                if (!sprite.enabled) { continue; }
                text.Append(puff.localPosition.ToString("F4")).Append(sprite.color.a.ToString("F4")).Append(';');
            }

            flow.Pilot = null;
            return text.ToString();
        }
    }
}
