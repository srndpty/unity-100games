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
    public sealed class HalcyonSceneTests
    {
        private HalcyonGame _game;

        [UnitySetUp]
        public IEnumerator Load()
        {
            yield return SceneManager.LoadSceneAsync("HalcyonSortie");
            yield return null;
            _game = Object.FindAnyObjectByType<HalcyonGame>();
            for (var i = 0; i < 30 && _game.HudRoot == null; i++) { yield return null; }
            _game.enabled = false;
        }

        [UnityTest]
        public IEnumerator TheTitleShowsTheControlsAndHidesTheFlightHud()
        {
            _game.Render();
            yield return null;
            var title = _game.HudRoot.Q("title-screen");
            Assert.That(title.resolvedStyle.display, Is.EqualTo(DisplayStyle.Flex));
            Assert.That(_game.HudRoot.Q("flight-hud").resolvedStyle.display, Is.EqualTo(DisplayStyle.None));
            var words = title.Query<Label>().ToList().ConvertAll(label => label.text);
            Assert.That(words, Has.Member("HALCYON SORTIE"));
            Assert.That(words.Exists(text => text.Contains("Space", StringComparison.Ordinal)), Is.True, "操作説明がある");

            _game.Flow.Apply(new Intent(IntentKind.Submit, 0));
            _game.Render();
            yield return null;
            Assert.That(title.resolvedStyle.display, Is.EqualTo(DisplayStyle.None));
            Assert.That(_game.HudRoot.Q("flight-hud").resolvedStyle.display, Is.EqualTo(DisplayStyle.Flex));
        }

        [UnityTest]
        public IEnumerator TheStageMatchesTheMissionRunway()
        {
            var (_, mission) = _game.Settings.Validate();
            Assert.That(_game.Stage.Mismatch(mission), Is.Null);
            var runway = _game.Stage.transform.Find("Runway");
            var original = runway.localPosition;
            try
            {
                runway.localPosition = original + new Vector3(0f, 0f, 5f);
                Assert.That(_game.Stage.Mismatch(mission), Does.Contain("滑走路"));
            }
            finally
            {
                runway.localPosition = original;
            }

            Assert.That(_game.Stage.Mismatch(mission), Is.Null);
            var longer = mission.Copy();
            longer.RunwayLength += 100;
            Assert.That(_game.Stage.Mismatch(longer), Is.Not.Null, "長さの違う滑走路とは一致しない");
            Assert.That(_game.Stage.MissileCapacity, Is.EqualTo(mission.MissileCapacity));
            yield return null;
        }

        [UnityTest]
        public IEnumerator ThrottleAndPitchKeysTakeTheAircraftOff()
        {
            using var keyboard = new TestKeyboard();
            var flow = _game.Flow;
            var flight = flow.Mission.Flight;
            _game.enabled = true; // キーを読む Update を動かす
            yield return keyboard.Tap(Key.Enter);
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Playing));

            keyboard.Hold(Key.LeftShift);
            yield return WaitUntil(() => flow.Mission.Player.View.Throttle >= 1000, 5f);
            keyboard.Hold();
            yield return WaitUntil(() => flow.Mission.Player.View.Air.Speed >= flight.RotateSpeed, 15f);
            Assert.That(flow.Mission.Player.View.Throttle, Is.EqualTo(1000), "離してもスロットルは保つ");
            Assert.That(flow.Mission.Player.View.Grounded, Is.True, "機首を上げるまでは浮かない");

            keyboard.Hold(Key.S);
            yield return WaitUntil(() => !flow.Mission.Player.View.Grounded, 5f);
            keyboard.Hold();
            yield return null;
            Assert.That(flow.Mission.Player.View.Crashed, Is.False);
            Assert.That(flow.Mission.Runway.Contains(flow.Mission.Player.View.Position), Is.True);
        }

        [UnityTest]
        public IEnumerator SpaceWithoutALockIsRejectedOnTheGroundAndInTheAir()
        {
            using var keyboard = new TestKeyboard();
            var flow = _game.Flow;
            _game.enabled = true;
            yield return keyboard.Tap(Key.Enter);
            yield return keyboard.Tap(Key.Space);
            yield return WaitUntil(() => flow.LastFire != FireResult.None, 2f);
            Assert.That(flow.LastFire, Is.EqualTo(FireResult.Grounded));
            _game.Render();
            Assert.That(_game.Hud.LockText, Is.EqualTo("地上では撃てない"));

            // 操縦役で離陸させ、まだ誰も円錐に入っていない空中で Space を押す。
            _game.enabled = false;
            flow.Pilot = new Autopilot();
            for (var i = 0; i < 6000 && (flow.Mission.Player.View.Grounded || flow.Mission.Player.View.Position.Y < 200 * Units.PerMeter); i++) { flow.StepTick(); }
            Assert.That(flow.Mission.Player.LockTarget, Is.EqualTo(-1));
            flow.Pilot = null;
            _game.enabled = true;
            yield return keyboard.Tap(Key.Space);
            yield return WaitUntil(() => flow.LastFire == FireResult.NotLocked, 2f);
            Assert.That(flow.Mission.Missiles, Is.Empty);
            Assert.That(flow.Mission.Player.Ammo, Is.EqualTo(flow.Mission.Config.Missiles));
            _game.Render();
            Assert.That(_game.Hud.LockText, Is.EqualTo("ロックしていない"));
        }

        [UnityTest]
        public IEnumerator TheAutopilotWinsAndRetryStartsANewSeed()
        {
            var flow = _game.Flow;
            flow.Pilot = new Autopilot();
            flow.Apply(new Intent(IntentKind.Submit, 0));
            var seed = flow.Mission.Seed;
            for (var i = 0; i < 20000 && flow.Screen != AppScreen.Result; i++)
            {
                flow.StepTick();

                // 途中も描いて、目標の枠を一度は出す（撃墜した敵の枠が残らないことを確かめるため）。
                if (i % 30 == 0) { _game.Render(); }
            }
            _game.Render();
            yield return null;
            Assert.That(flow.Won, Is.True);
            Assert.That(_game.HudRoot.Q("result-screen").resolvedStyle.display, Is.EqualTo(DisplayStyle.Flex));
            Assert.That(_game.HudRoot.Q<Label>("result-title").text, Is.EqualTo("MISSION COMPLETE"));
            Assert.That(_game.HudRoot.Q<Label>("result-sub").text, Does.Contain("撃墜 3/3"));
            for (var i = 1; i <= MissionConfig.EnemyCount; i++)
            {
                Assert.That(_game.Hud.TargetBox(i).resolvedStyle.display, Is.EqualTo(DisplayStyle.None), $"撃墜した敵 {i} の枠を残さない");
            }

            flow.Apply(new Intent(IntentKind.Submit, 0));
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Playing));
            Assert.That(flow.Mission.Tick, Is.Zero);
            Assert.That(flow.Mission.Seed, Is.Not.EqualTo(seed));
            _game.Render();
            yield return null;
            Assert.That(_game.HudRoot.Q("result-screen").resolvedStyle.display, Is.EqualTo(DisplayStyle.None));
            for (var i = 0; i < HalcyonStage.AircraftCount; i++) { Assert.That(_game.Stage.ExplosionAt(i).gameObject.activeSelf, Is.False, "前の試合の爆発を残さない"); }
        }

        [UnityTest]
        public IEnumerator TheDrawingFollowsTheCoreState()
        {
            var flow = _game.Flow;
            flow.Pilot = new Autopilot();
            flow.Apply(new Intent(IntentKind.Submit, 0));
            for (var i = 0; i < 20000 && Active(flow.Mission) == 0; i++) { flow.StepTick(); }
            for (var i = 0; i < 20; i++)
            {
                flow.StepTick();
                _game.PresentWorld();
            }

            _game.Render();
            yield return null;

            var mission = flow.Mission;
            for (var i = 0; i < HalcyonStage.AircraftCount; i++)
            {
                var fighter = mission.Fighters[i];
                var plane = _game.Stage.AircraftAt(i);
                Assert.That(plane.gameObject.activeSelf, Is.EqualTo(!fighter.Down));
                Assert.That(Vector3.Distance(plane.position, HalcyonSpace.Position(fighter.View.Position)), Is.LessThan(0.01f));
                Assert.That(Quaternion.Angle(plane.rotation, HalcyonSpace.Rotation(fighter.View.Attitude)), Is.LessThan(0.1f));
            }

            var drawn = 0;
            for (var i = 0; i < _game.Stage.MissileCapacity; i++)
            {
                var missile = _game.Stage.MissileAt(i);
                if (!missile.gameObject.activeSelf) { continue; }
                drawn++;
                Assert.That(Vector3.Distance(missile.position, HalcyonSpace.Position(mission.Missiles[i].Position)), Is.LessThan(0.01f));
            }

            Assert.That(drawn, Is.EqualTo(Active(mission)));
            Assert.That(_game.Stage.Trails.Alive, Is.GreaterThan(0), "飛んでいるミサイルから煙が出る");
        }

        [UnityTest]
        public IEnumerator TheHudShowsTheNoseAboveTheVelocityAfterRotating()
        {
            var flow = _game.Flow;
            flow.Pilot = new Autopilot();
            flow.Apply(new Intent(IntentKind.Submit, 0));
            for (var i = 0; i < 6000 && flow.Mission.Player.View.Grounded; i++) { flow.StepTick(); }
            for (var i = 0; i < 10; i++) { flow.StepTick(); }
            _game.Render();
            yield return null;
            _game.Render();
            yield return null;

            Assert.That(flow.Mission.Player.View.Air.Alpha, Is.GreaterThan(Units.Degrees(3)), "離陸直後は迎角が大きい");
            var nose = _game.Hud.Boresight.resolvedStyle.top;
            var path = _game.Hud.FlightPathMarker.resolvedStyle.top;
            Assert.That(nose, Is.LessThan(path - 20f), "機首（十字）が速度の向き（円）より上に描かれる");
        }

        [UnityTest]
        public IEnumerator ReturningToTheTitleRestoresTheStartingView()
        {
            _game.Render();
            yield return null;
            var camera = _game.View.transform;
            var startCamera = (camera.position, camera.rotation);
            var startPlane = _game.Stage.AircraftAt(0).position;

            var flow = _game.Flow;
            flow.Pilot = new Autopilot();
            flow.Apply(new Intent(IntentKind.Submit, 0));
            for (var i = 0; i < 20000 && flow.Mission.EnemiesLeft == MissionConfig.EnemyCount; i++) { flow.StepTick(); }
            _game.Render();
            yield return null;
            flow.Apply(new Intent(IntentKind.Cancel, 0));
            flow.Apply(new Intent(IntentKind.Back, 0));
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Title));
            _game.Render();
            yield return null;

            Assert.That(Vector3.Distance(camera.position, startCamera.position), Is.LessThan(0.001f));
            Assert.That(Quaternion.Angle(camera.rotation, startCamera.rotation), Is.LessThan(0.01f));
            Assert.That(Vector3.Distance(_game.Stage.AircraftAt(0).position, startPlane), Is.LessThan(0.001f));
            for (var i = 0; i < HalcyonStage.AircraftCount; i++)
            {
                Assert.That(_game.Stage.AircraftAt(i).gameObject.activeSelf, Is.True);
                Assert.That(_game.Stage.ExplosionAt(i).gameObject.activeSelf, Is.False);
            }

            for (var i = 0; i < _game.Stage.MissileCapacity; i++) { Assert.That(_game.Stage.MissileAt(i).gameObject.activeSelf, Is.False); }
            Assert.That(_game.Stage.Trails.Alive, Is.Zero);
            Assert.That(_game.HudRoot.Q("title-screen").resolvedStyle.display, Is.EqualTo(DisplayStyle.Flex));
        }

        [UnityTest]
        public IEnumerator TheTrailsAreTheSameForTheSameTicks()
        {
            var first = TrailsAfter(seed: 21, ticks: 40);
            yield return null;
            var second = TrailsAfter(seed: 21, ticks: 40);
            Assert.That(first.Length, Is.GreaterThan(0));
            Assert.That(second, Is.EqualTo(first));
        }

        [UnityTest]
        public IEnumerator LosingFocusPausesTheMission()
        {
            var flow = _game.Flow;
            flow.Apply(new Intent(IntentKind.Submit, 0));
            flow.Apply(new Intent(IntentKind.ThrottleUpDown, 0));
            _game.SendMessage("OnApplicationFocus", false);
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Paused));
            Assert.That(flow.Held, Is.EqualTo(Controls.None));
            _game.Render();
            yield return null;
            Assert.That(_game.HudRoot.Q("pause-screen").resolvedStyle.display, Is.EqualTo(DisplayStyle.Flex));
            _game.SendMessage("OnApplicationFocus", true);
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Paused), "復帰は手動");
        }

        [UnityTest]
        public IEnumerator SettingsRejectNonFiniteVolume()
        {
            var settings = ScriptableObject.CreateInstance<HalcyonSettings>();
            try
            {
                settings.Volume = float.NaN;
                Assert.Throws<ArgumentOutOfRangeException>(() => settings.Validate());
                settings.Volume = 0.1f;
                settings.Mission.LockTicks = 0;
                Assert.Throws<ArgumentOutOfRangeException>(() => settings.Validate());
            }
            finally
            {
                Object.Destroy(settings);
            }

            Assert.DoesNotThrow(() => _game.Settings.Validate(), "シーンの設定アセットは検証を通る");
            yield return null;
        }

        [UnityTest]
        public IEnumerator AMissionStepAndTheWorldViewAreFastAndRenderingDoesNotAllocate()
        {
            // 撮影と同じシード 2 を操縦役で進め、ミサイルと煙が飛んでいる場面を計る（毎回同じ場面になる）。
            var (flight, config) = _game.Settings.Validate();
            var mission = new Mission(flight, config, 2);
            var pilot = new Autopilot();
            var stage = _game.Stage;
            stage.Trails.Clear();
            for (var i = 0; i < 20000 && Active(mission) == 0; i++) { mission.Step(pilot.CommandFor(mission)); }
            for (var i = 0; i < 30; i++)
            {
                mission.Step(pilot.CommandFor(mission));
                stage.Show(mission);
            }

            // 時間：Step と描画の更新を 300 Tick 分ずつ計る。
            const int Samples = 300;
            var step = new double[Samples];
            var render = new double[Samples];
            var watch = new System.Diagnostics.Stopwatch();
            var missiles = 0;
            var puffs = 0;
            for (var i = 0; i < Samples; i++)
            {
                var command = pilot.CommandFor(mission);
                watch.Restart();
                mission.Step(command);
                step[i] = watch.Elapsed.TotalMilliseconds;
                watch.Restart();
                stage.Show(mission);
                render[i] = watch.Elapsed.TotalMilliseconds;
                missiles = Math.Max(missiles, Active(mission));
                puffs = Math.Max(puffs, stage.Trails.Alive);
            }

            // 割り当て：Step だけのフレームと描画の更新だけのフレームを交互に作り、フレームごとの量を読む（027）。
            // 読んだ値は先に用意した配列に入れ、ログは計った後に出す（ログ自体が割り当てる）。
            const int Frames = 60;
            var stepFrames = new long[Frames];
            var renderFrames = new long[Frames];
            using var recorder = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame");
            yield return null;
            yield return null;
            yield return null;
            var baseline = recorder.LastValue;
            var idleFrames = new long[Frames];
            for (var i = 0; i < Frames; i++)
            {
                mission.Step(pilot.CommandFor(mission));
                yield return null;
                stepFrames[i] = recorder.LastValue - baseline;
                stage.Show(mission);
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
            long stepSum = 0;
            var renderNonZero = 0;
            var idleNonZero = 0;
            for (var i = 0; i < Frames; i++)
            {
                renderMax = Math.Max(renderMax, renderFrames[i]);
                stepSum += stepFrames[i];
                if (renderFrames[i] != 0) { renderNonZero++; }
                if (idleFrames[i] != 0) { idleNonZero++; }
            }

            var report = $"Core の Step p50 {step[Samples / 2]:F3} ms / p95 {step[Samples * 95 / 100]:F3} ms / 最大 {step[Samples - 1]:F3} ms、" +
                $"描画の更新 p50 {render[Samples / 2]:F3} ms / p95 {render[Samples * 95 / 100]:F3} ms / 最大 {render[Samples - 1]:F3} ms、" +
                $"描画のフレームの割り当て 最大 {renderMax} バイト（{Frames} 中 {renderNonZero} フレーム、何もしないフレームは {idleNonZero} フレーム）、Step のフレームの割り当て 平均 {stepSum / Frames} バイト（確認 {probe - baseline}）、" +
                $"ミサイル最大 {missiles} 発・煙最大 {puffs} 粒";
            Debug.Log("[Performance] " + report);
            TestContext.WriteLine(report);
            for (var i = 0; i < Frames; i++)
            {
                if (renderFrames[i] != 0) { TestContext.WriteLine($"描画のフレーム {i}：{renderFrames[i]} バイト"); }
                if (idleFrames[i] != 0) { TestContext.WriteLine($"何もしないフレーム {i}：{idleFrames[i]} バイト"); }
            }

            Assert.That(missiles, Is.GreaterThan(0), "ミサイルが飛んでいる場面を計った");
            Assert.That(puffs, Is.GreaterThan(0), "煙が出ている場面を計った");
            Assert.That(recorder.Valid && probe >= baseline + 4096, Is.True, "割り当ての計数が働いている");
            Assert.That(step[Samples * 95 / 100], Is.LessThan(4));
            Assert.That(render[Samples * 95 / 100], Is.LessThan(4));

            // 何もしないフレームでも、エンジンが時々 52〜56 バイトを数える（Unity 6000.6、バッチ実行）。
            // 描画の更新が割り当てるなら毎フレーム数えるので、ほとんどのフレームが 0 で、0 でないフレームもその大きさまでなら割り当てていない。
            Assert.That(renderNonZero, Is.LessThanOrEqualTo(Frames / 10), "描画の更新で割り当てをしない（ほとんどのフレームが 0）");
            Assert.That(renderMax, Is.LessThanOrEqualTo(BackgroundBytes), "0 でないフレームもエンジンの背景の分まで");
        }

        /// <summary>何もしないフレームでもエンジンが時々数える割り当ての上限（観測は 52〜56 バイト）。</summary>
        private const long BackgroundBytes = 64;

        /// <summary>飛んでいるミサイルの数（番号で回す。foreach はインターフェース越しの列挙子を箱に入れて割り当てる）。</summary>
        private static int Active(Mission mission)
        {
            var count = 0;
            var missiles = mission.Missiles;
            for (var i = 0; i < missiles.Count; i++) { if (missiles[i].Active) { count++; } }
            return count;
        }

        /// <summary>操縦役で最初のミサイルが出てから <paramref name="ticks"/> Tick 飛び、7 Tick ごとに描いた後の煙の粒の位置。</summary>
        private string TrailsAfter(int seed, int ticks)
        {
            var (flight, config) = _game.Settings.Validate();
            var mission = new Mission(flight, config, seed);
            var pilot = new Autopilot();
            var trails = _game.Stage.Trails;
            trails.Clear();
            var after = -1;
            for (var i = 0; i < 20000 && !mission.IsOver && after != 0; i++)
            {
                mission.Step(pilot.CommandFor(mission));
                if (i % 7 == 0) { trails.Show(mission); }
                if (after < 0 && Active(mission) > 0) { after = ticks; }
                if (after > 0) { after--; }
            }

            trails.Show(mission);
            var text = new System.Text.StringBuilder();
            foreach (Transform puff in trails.transform)
            {
                if (!puff.gameObject.activeSelf) { continue; }
                text.Append(puff.localPosition.ToString("F3")).Append(puff.localScale.x.ToString("F3")).Append(';');
            }

            trails.Clear();
            return text.ToString();
        }

        private static IEnumerator WaitUntil(Func<bool> condition, float seconds)
        {
            var limit = Time.realtimeSinceStartup + seconds;
            while (!condition() && Time.realtimeSinceStartup < limit) { yield return null; }
            Assert.That(condition(), Is.True, $"{seconds} 秒待っても条件が成り立たなかった");
        }
    }
}
