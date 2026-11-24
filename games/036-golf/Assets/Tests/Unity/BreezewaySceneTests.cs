using System;
using System.Collections;
using Game.App;
using Game.Core;
using IntGeometry;
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
    public sealed class BreezewaySceneTests
    {
        private const int TickLimit = 60 * 60 * 30;

        private TestKeyboard _keyboard;
        private BreezewayGame _game;

        [UnitySetUp]
        public IEnumerator Load()
        {
            _keyboard = new TestKeyboard();
            yield return SceneManager.LoadSceneAsync("Breezeway");
            yield return null;
            _game = Object.FindAnyObjectByType<BreezewayGame>();
            for (var i = 0; i < 30 && _game.HudRoot == null; i++) { yield return null; }
            Assert.That(_game.HudRoot, Is.Not.Null);
        }

        [UnityTearDown]
        public IEnumerator Unload()
        {
            Time.timeScale = 1;
            _keyboard.Hold();
            _keyboard.Dispose();
            yield return null;
        }

        private GolfRound Round => _game.Flow.Round;

        private BreezewayStage Stage => _game.Stage;

        private float HeightScale => _game.Settings.HeightScale;

        private string Text(string name) => _game.HudRoot.Q<Label>(name).text;

        private bool Shown(string name) => _game.HudRoot.Q(name).resolvedStyle.display == DisplayStyle.Flex;

        /// <summary>時計を止めて（timeScale 0）ラウンドを始める。Tick はテストが進める。</summary>
        private IEnumerator StartPaused()
        {
            Time.timeScale = 0;
            yield return _keyboard.Tap(Key.Enter);
            Assert.That(_game.Flow.Screen, Is.EqualTo(AppScreen.Playing));
        }

        private void Step(int ticks)
        {
            for (var i = 0; i < ticks && _game.Flow.Screen == AppScreen.Playing; i++)
            {
                _game.Flow.StepTick();
                _game.ConsumeEvents();
            }

            _game.Render();
        }

        private void StepUntil(Func<bool> done)
        {
            for (var i = 0; i < TickLimit && !done(); i++)
            {
                _game.Flow.StepTick();
                _game.ConsumeEvents();
            }

            _game.Render();
            Assert.That(done(), Is.True, "Tick の上限までに条件を満たさなかった");
        }

        /// <summary>操縦役と同じ操作で、クラブ・向きを決めてメーターの Tick で打つ（Tick はテストが進める）。</summary>
        private void Shoot(int club, int aim, int powerTick, int error = 0)
        {
            Assert.That(Round.SelectClub(club), Is.True);
            Round.SetAim(aim);
            Round.Press();
            StepUntil(() => Round.MeterState?.T == powerTick);
            Round.Press();
            StepUntil(() => Round.MeterState?.T == Round.MeterState?.IdealTick + error);
            Round.Press();
            Assert.That(Round.Phase, Is.EqualTo(RoundPhase.Moving));
        }

        // バッチ実行はフレームが極端に短いので、フレーム数ではなく実時間の上限で待つ（011 の知見）。
        private static IEnumerator WaitUntil(Func<bool> condition, float seconds = 5f)
        {
            var limit = Time.realtimeSinceStartup + seconds;
            while (!condition() && Time.realtimeSinceStartup < limit) { yield return null; }
            Assert.That(condition(), Is.True, "時間内に条件を満たさなかった");
        }

        private static IEnumerator WaitSeconds(float seconds)
        {
            var limit = Time.realtimeSinceStartup + seconds;
            while (Time.realtimeSinceStartup < limit) { yield return null; }
        }

        private static void Near(Vector3 actual, Vector3 expected, string what) =>
            Assert.That((actual - expected).magnitude, Is.LessThan(1e-3f), $"{what}：{actual} と {expected}");

        [UnityTest]
        public IEnumerator Stage_IsComplete_AndTheTitleExplainsTheControls()
        {
            yield return null;
            Assert.That(Object.FindObjectsByType<BreezewayStage>(FindObjectsInactive.Include), Has.Length.EqualTo(1), "Stage のインスタンスは 1 つ");
            Assert.That(Stage.Mismatch(_game.Course, _game.Flow.Config), Is.Null);
            Assert.That(Text("headline"), Is.EqualTo("BREEZEWAY LINKS"));
            Assert.That(Shown("panel"), Is.True);
            Assert.That(Text("controls").Contains("Space", StringComparison.Ordinal), Is.True);
            Assert.That(Text("message").Contains("予測線", StringComparison.Ordinal), Is.True);
            Assert.That(Stage.Hole(0).Root.activeSelf, Is.True);
            Assert.That(Stage.Hole(1).Root.activeSelf, Is.False);
            Near(Stage.Ball.transform.localPosition, BreezewayStage.ToLocal(_game.Course.Holes[0].Tee), "タイトルの背景はティーの球");
            Near(Stage.Flag.transform.localPosition, BreezewayStage.ToLocal(_game.Course.Holes[0].Cup), "旗はカップ");
            Assert.That(Stage.PredictedDot(0).enabled, Is.False, "タイトルでは予測線を出さない");
        }

        /// <summary>実時間で遊ぶ：Space の 3 回押し（振り始め・パワー・インパクト）で打ち、描画の球は Core の位置から高さだけ上にずらし、影は地面に描く。</summary>
        [UnityTest]
        public IEnumerator SpaceKey_StartsPowersAndHits_AndTheBallIsDrawnWhereTheCoreSays()
        {
            yield return _keyboard.Tap(Key.Enter);
            yield return WaitUntil(() => Round != null);
            yield return _keyboard.Tap(Key.Space);
            yield return WaitUntil(() => Round.Phase == RoundPhase.Swinging);
            var half = _game.Flow.Config.MeterHalfTicks;
            yield return WaitUntil(() => Round.MeterState?.T >= half - 4);
            yield return _keyboard.Tap(Key.Space);
            yield return WaitUntil(() => Round.MeterState?.PowerSet == true);
            Assert.That(Shown("meter"), Is.True);
            yield return WaitUntil(() => Round.MeterState?.T >= Round.MeterState?.IdealTick - 2);
            yield return _keyboard.Tap(Key.Space);
            yield return WaitUntil(() => Round.Phase == RoundPhase.Moving);
            Assert.That(Round.Strokes, Is.EqualTo(1));
            yield return WaitUntil(() => Round.Ball.Z > Units.PerMeter);

            Time.timeScale = 0;
            yield return null;
            var ball = Round.Ball;
            Near(Stage.Ball.transform.localPosition, BreezewayStage.Lifted(ball.Position, ball.Z, HeightScale), "球は高さの分だけ上");
            Near(Stage.Shadow.transform.localPosition, BreezewayStage.ToLocal(ball.Position), "影は地面");
            Assert.That(Stage.TrailDot(0).enabled, Is.True, "実際の軌跡を描く");
            Assert.That(Shown("meter"), Is.False, "球が動いている間はメーターを出さない");
        }

        /// <summary>予測線の点と着地の印は Core の予測と同じ値、打った後の軌跡は Core の打ち出しから進め直した点。</summary>
        [UnityTest]
        public IEnumerator PreviewAndTrail_AreTheCorePaths()
        {
            yield return StartPaused();
            _game.Render();
            var points = new Vec2i[Round.Config.MaxShotTicks + 1];
            var heights = new int[points.Length];
            var preview = Round.Preview(points, heights);
            Assert.That(Stage.PreviewCount, Is.EqualTo(preview.Count));
            for (var i = 0; i < preview.Count; i++) { Assert.That(Stage.PreviewPoint(i), Is.EqualTo(points[i]), $"予測線の点 {i}"); }
            var stride = Mathf.Max(1, Mathf.CeilToInt((float)preview.Count / BreezewayStage.DotCount));
            Near(Stage.PredictedDot(1).transform.localPosition, BreezewayStage.Lifted(points[stride], heights[stride], HeightScale), "予測線の 2 個目の点");
            Assert.That(Stage.PredictedLanding.enabled, Is.True);
            Near(Stage.PredictedLanding.transform.localPosition, BreezewayStage.ToLocal(preview.Landing), "着地の予測の印");

            Shoot(Clubs.Iron, Round.Aim, _game.Flow.Config.MeterHalfTicks, 3);
            Step(70);
            var trail = Round.TraceLastShot(false, points, heights, Round.Ball.Ticks);
            Assert.That(points[trail.Count - 1], Is.EqualTo(Round.Ball.Position), "軌跡の最後は今の球");
            stride = Mathf.Max(1, Mathf.CeilToInt((float)trail.Count / BreezewayStage.DotCount));
            var drawn = 0;
            for (var k = 0; k < BreezewayStage.DotCount && Stage.TrailDot(k).enabled; k++)
            {
                Near(Stage.TrailDot(k).transform.localPosition, BreezewayStage.Lifted(points[k * stride], heights[k * stride], HeightScale), $"軌跡の点 {k}");
                drawn++;
            }

            Assert.That(drawn, Is.EqualTo(Mathf.CeilToInt((float)trail.Count / stride)));
            Assert.That(Stage.ActualLanding.enabled, Is.EqualTo(Round.Ball.Landed));
        }

        [UnityTest]
        public IEnumerator AimClubAndImpactKeys_ChangeTheSetup()
        {
            yield return _keyboard.Tap(Key.Enter);
            var aim = Round.Aim;
            var step = _game.Flow.Config.AimStep;
            _keyboard.Hold(Key.LeftArrow);
            yield return WaitSeconds(.7f);
            _keyboard.Hold();
            yield return null;
            var turned = Trig.Normalize(Round.Aim - aim) / step;
            Assert.That(turned, Is.GreaterThan(3), "押し続けると繰り返す");
            var held = Round.Aim;
            yield return WaitSeconds(.2f);
            Assert.That(Round.Aim, Is.EqualTo(held), "離したら止まる");

            var club = Round.Club;
            yield return _keyboard.Tap(Key.DownArrow);
            Assert.That(Round.Club, Is.EqualTo(club + 1));
            yield return _keyboard.Tap(Key.UpArrow);
            Assert.That(Round.Club, Is.EqualTo(club));

            yield return _keyboard.Tap(Key.W);
            yield return _keyboard.Tap(Key.D);
            Assert.That((Round.ImpactX, Round.ImpactY), Is.EqualTo((1, 1)));
            yield return null;
            var dot = _game.HudRoot.Q("impact-dot");
            Assert.That(dot.style.left.value.value, Is.EqualTo(39 + 18).Within(.01f));
            Assert.That(dot.style.top.value.value, Is.EqualTo(39 - 18).Within(.01f));
        }

        [UnityTest]
        public IEnumerator Tab_TogglesTheGreenView()
        {
            yield return StartPaused();
            yield return null;
            var hole = Round.Hole;
            var overview = BreezewayStage.Frame(hole, false, Round.Lie, _game.ViewCamera.aspect);
            Assert.That(_game.GreenView, Is.False);
            Assert.That(_game.ViewCamera.orthographicSize, Is.EqualTo(overview.Size).Within(1e-3f));
            Assert.That(Stage.Hole(0).Slopes.activeSelf, Is.False);

            yield return _keyboard.Tap(Key.Tab);
            yield return null;
            var green = BreezewayStage.Frame(hole, true, Round.Lie, _game.ViewCamera.aspect);
            Assert.That(_game.GreenView, Is.True);
            Assert.That(_game.ViewCamera.orthographicSize, Is.EqualTo(green.Size).Within(1e-3f));
            Assert.That(Stage.Hole(0).Slopes.activeSelf, Is.True, "グリーンの表示では傾きの矢印を出す");

            yield return _keyboard.Tap(Key.Tab);
            yield return null;
            Assert.That(_game.GreenView, Is.False);
            Assert.That(Stage.Hole(0).Slopes.activeSelf, Is.False);
        }

        [UnityTest]
        public IEnumerator HoleOutAndRoundEnd_ShowTheScorecard_AndEnterReplays()
        {
            yield return StartPaused();
            var first = Round;
            var pilot = new Autopilot();
            pilot.Reset(first.Seed);
            _game.Flow.Pilot = pilot;
            StepUntil(() => Round.Phase == RoundPhase.HoleOut);
            yield return null;
            Assert.That(Shown("panel"), Is.True, "ホールを終えたらスコアカード");
            Assert.That(Text("headline").StartsWith("1番", StringComparison.Ordinal), Is.True);
            Assert.That(Text("message").Contains("合計", StringComparison.Ordinal), Is.True);
            Assert.That(Text("banner").Contains("カップイン", StringComparison.Ordinal), Is.True);
            Assert.That(Stage.Ball.enabled, Is.False, "カップに入った球は描かない");

            StepUntil(() => Round.HoleIndex == 1 && Round.Phase == RoundPhase.Aiming);
            Assert.That(Stage.Hole(0).Root.activeSelf, Is.False);
            Assert.That(Stage.Hole(1).Root.activeSelf, Is.True);
            Near(Stage.Flag.transform.localPosition, BreezewayStage.ToLocal(Round.Hole.Cup), "旗は今のホールのカップ");

            StepUntil(() => _game.Flow.Screen == AppScreen.Result);
            yield return null;
            Assert.That(Text("headline"), Is.EqualTo("ラウンド終了"));
            Assert.That(Text("message").Contains($"合計 {first.Total}", StringComparison.Ordinal), Is.True);

            _game.Flow.Pilot = null;
            yield return _keyboard.Tap(Key.Enter);
            Assert.That(_game.Flow.Screen, Is.EqualTo(AppScreen.Playing));
            Assert.That(Round, Is.Not.SameAs(first));
            Assert.That(Round.Seed, Is.Not.EqualTo(first.Seed));
            Assert.That(Round.HoleIndex, Is.EqualTo(0));
        }

        /// <summary>打った後にタイトルへ戻ると、起動直後と同じ表示（球・影・旗・カメラ・ホール・HUD）。</summary>
        [UnityTest]
        public IEnumerator ReturningToTitle_LooksLikeStartup()
        {
            yield return null;
            var ball = Stage.Ball.transform.localPosition;
            var shadow = Stage.Shadow.transform.localPosition;
            var flag = Stage.Flag.transform.localPosition;
            var camera = _game.ViewCamera.transform.position;
            var size = _game.ViewCamera.orthographicSize;
            var texts = new[] { Text("hole"), Text("strokes"), Text("total"), Text("wind-speed"), Text("headline"), Text("message") };

            yield return StartPaused();
            Shoot(Clubs.Wood, Round.Aim, 40);
            Step(120);
            yield return _keyboard.Tap(Key.Tab);
            Time.timeScale = 1;
            yield return _keyboard.Tap(Key.Escape);
            yield return _keyboard.Tap(Key.Backspace);
            Assert.That(_game.Flow.Screen, Is.EqualTo(AppScreen.Title));
            yield return null;

            Assert.That(Round, Is.Null);
            Near(Stage.Ball.transform.localPosition, ball, "球");
            Near(Stage.Shadow.transform.localPosition, shadow, "影");
            Near(Stage.Flag.transform.localPosition, flag, "旗");
            Near(_game.ViewCamera.transform.position, camera, "カメラの位置");
            Assert.That(_game.ViewCamera.orthographicSize, Is.EqualTo(size).Within(1e-3f));
            Assert.That(Stage.Ball.enabled, Is.True);
            for (var k = 0; k < BreezewayStage.DotCount; k++)
            {
                Assert.That(Stage.PredictedDot(k).enabled, Is.False, "予測線が残らない");
                Assert.That(Stage.TrailDot(k).enabled, Is.False, "軌跡が残らない");
            }

            Assert.That(Stage.PredictedLanding.enabled || Stage.ActualLanding.enabled, Is.False);
            Assert.That(Stage.Hole(0).Slopes.activeSelf, Is.False);
            Assert.That(new[] { Text("hole"), Text("strokes"), Text("total"), Text("wind-speed"), Text("headline"), Text("message") }, Is.EqualTo(texts));
            Assert.That(Shown("shot-panel"), Is.False);
        }

        [UnityTest]
        public IEnumerator FocusLoss_PausesAndCancelsTheSwing()
        {
            yield return _keyboard.Tap(Key.Enter);
            yield return _keyboard.Tap(Key.Space);
            yield return WaitUntil(() => Round.Phase == RoundPhase.Swinging);
            _game.SendMessage("OnApplicationFocus", false);
            Assert.That(_game.Flow.Screen, Is.EqualTo(AppScreen.Paused));
            Assert.That(Round.Phase, Is.EqualTo(RoundPhase.Aiming), "振っている途中を取り消す");
            Assert.That(Round.Strokes, Is.EqualTo(0));
            yield return null;
            Assert.That(Text("headline"), Is.EqualTo("一時停止"));

            // フォーカスがない間のキーは捨て、戻っても自動では再開しない。
            yield return _keyboard.Tap(Key.Escape);
            Assert.That(_game.Flow.Screen, Is.EqualTo(AppScreen.Paused));
            _game.SendMessage("OnApplicationFocus", true);
            yield return WaitSeconds(.1f);
            Assert.That(_game.Flow.Screen, Is.EqualTo(AppScreen.Paused));
            yield return _keyboard.Tap(Key.Escape);
            Assert.That(_game.Flow.Screen, Is.EqualTo(AppScreen.Playing));
            Assert.That(Round.Phase, Is.EqualTo(RoundPhase.Aiming));
        }

        [UnityTest]
        public IEnumerator SettingsAsset_IsValid_AndRejectsBadValues()
        {
            yield return null;
            Assert.DoesNotThrow(() => _game.Settings.Validate());
            var defaults = new GolfConfig();
            Assert.That(_game.Settings.Rules.Drag, Is.EqualTo(defaults.Drag), "コードの既定値とアセットがそろっている（024）");
            Assert.That(_game.Settings.Rules.ClubSpeed, Is.EqualTo(defaults.ClubSpeed));
            Assert.That(_game.Settings.Rules.RollDecel, Is.EqualTo(defaults.RollDecel));
            Assert.That(_game.Settings.Rules.CupMaxSpeed, Is.EqualTo(defaults.CupMaxSpeed));
            var settings = Object.Instantiate(_game.Settings);
            try
            {
                settings.HeightScale = float.NaN;
                Assert.Throws<ArgumentOutOfRangeException>(() => settings.Validate());
                settings.HeightScale = .5f;
                settings.BannerSeconds = 0;
                Assert.Throws<ArgumentOutOfRangeException>(() => settings.Validate());
                settings.BannerSeconds = 2;
                settings.Rules.ClubSpeed[Clubs.Wood] = Units.MaxTickMove;
                Assert.That(() => settings.Validate(), Throws.InstanceOf<ArgumentOutOfRangeException>(), "掃引の上限を超えるクラブの速さ");
                settings.Rules.ClubSpeed[Clubs.Wood] = defaults.ClubSpeed[Clubs.Wood];
                Assert.DoesNotThrow(() => settings.Validate());
            }
            finally
            {
                Object.DestroyImmediate(settings);
            }
        }

        /// <summary>Stage Prefab の照合は、コースのデータと同じ関数で決めた位置・向き・大きさ・絵まで見る（023・027）。</summary>
        [UnityTest]
        public IEnumerator StagePrefabMismatch_DetectsMovedOrWrongParts()
        {
            yield return null;
            var course = _game.Course;
            var config = _game.Flow.Config;
            var copy = Object.Instantiate(Stage);
            try
            {
                Assert.That(copy.Mismatch(course, config), Is.Null);
                Check(c => c.Hole(1).Trees[2].transform.localPosition += new Vector3(.5f, 0, 0), c => c.Hole(1).Trees[2].transform.localPosition -= new Vector3(.5f, 0, 0), "木の位置");
                Check(c => c.Hole(0).Trees[0].transform.localScale *= 1.2f, c => c.Hole(0).Trees[0].transform.localScale /= 1.2f, "木の大きさ");
                Check(c => c.Hole(2).Cup.transform.localPosition += new Vector3(0, .3f, 0), c => c.Hole(2).Cup.transform.localPosition -= new Vector3(0, .3f, 0), "カップの位置");
                Check(c => c.Hole(0).Cup.transform.localScale *= 2, c => c.Hole(0).Cup.transform.localScale /= 2, "カップの大きさ");
                Check(c => c.Hole(1).Tee.transform.localPosition += new Vector3(1, 0, 0), c => c.Hole(1).Tee.transform.localPosition -= new Vector3(1, 0, 0), "ティーの位置");
                var ground = copy.Hole(0).Ground.sprite;
                Check(c => c.Hole(0).Ground.sprite = c.Hole(1).Ground.sprite, c => c.Hole(0).Ground.sprite = ground, "地面の絵（別のホール）");
                Check(c => c.Hole(2).Ground.transform.localPosition = new Vector3(0, 1, 0), c => c.Hole(2).Ground.transform.localPosition = Vector3.zero, "地面の位置");
                Check(c => c.Hole(1).Root.transform.localScale = new Vector3(1.1f, 1.1f, 1), c => c.Hole(1).Root.transform.localScale = Vector3.one, "ホールの親の大きさ");
                Check(c => c.Hole(0).Arrows[3].transform.localRotation *= Quaternion.Euler(0, 0, 30), c => c.Hole(0).Arrows[3].transform.localRotation *= Quaternion.Euler(0, 0, -30), "傾きの矢印の向き");
                Check(c => c.Hole(2).Arrows[0].transform.localScale *= 1.5f, c => c.Hole(2).Arrows[0].transform.localScale /= 1.5f, "傾きの矢印の大きさ");
                var tree = copy.Hole(1).Trees[0].sprite;
                Check(c => c.Hole(1).Trees[0].sprite = null, c => c.Hole(1).Trees[0].sprite = tree, "木の絵");
                var dotParent = copy.PredictedDot(5).transform.parent;
                Check(c => c.PredictedDot(5).transform.SetParent(c.Hole(0).Root.transform, true), c => c.PredictedDot(5).transform.SetParent(dotParent, true), "予測線の点の親");
            }
            finally
            {
                Object.DestroyImmediate(copy.gameObject);
            }

            void Check(Action<BreezewayStage> breakIt, Action<BreezewayStage> fix, string what)
            {
                breakIt(copy);
                Assert.That(copy.Mismatch(course, config), Is.Not.Null, what);
                fix(copy);
                Assert.That(copy.Mismatch(course, config), Is.Null, what + "（戻した後）");
            }
        }

        /// <summary>
        /// 予測線の計算（構えを変えるたび）と、打った後の 1 Tick（Core の Step と描画の更新）の時間、割り当て 0。
        /// </summary>
        [UnityTest]
        public IEnumerator PreviewAndFlight_AreFast_AndDoNotAllocate()
        {
            yield return null;
            var config = _game.Flow.Config;
            var course = _game.Course;
            var aiming = new GolfRound(config, course, 1);
            var flying = new GolfRound(config, course, 1);
            Assert.That(flying.SelectClub(Clubs.Wood), Is.True);
            flying.Press();

            // 9 割弱のパワー：1 番ホールの中に落ち、計測の 300 Tick の間は止まらない。
            while (flying.MeterState?.T != config.MeterHalfTicks - 5) { flying.Step(); }
            flying.Press();
            while (flying.MeterState?.T != config.IdealImpactTick) { flying.Step(); }
            flying.Press();
            Assert.That(flying.Phase, Is.EqualTo(RoundPhase.Moving));
            var points = new Vec2i[config.MaxShotTicks + 1];
            var heights = new int[points.Length];
            var size = _game.ViewCamera.orthographicSize;
            for (var i = 0; i < 5; i++)
            {
                aiming.Preview(points, heights);
                Stage.Render(flying, course, 0, size);
            }

            const int Samples = 300;
            var preview = new double[Samples];
            var step = new double[Samples];
            var render = new double[Samples];
            var watch = new System.Diagnostics.Stopwatch();
            var baseAim = aiming.Aim;

            // Mono では GC.GetAllocatedBytesForCurrentThread が常に 0 を返すので、フレームごとの割り当ての計数で測る（027 の知見）。
            _game.enabled = false;
            using var recorder = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame");
            yield return null;
            yield return null;
            yield return null;
            var baseline = recorder.LastValue;
            for (var i = 0; i < Samples; i++)
            {
                aiming.SetAim(baseAim + ((i % 41) - 20) * config.AimStep);
                watch.Restart();
                aiming.Preview(points, heights);
                preview[i] = watch.Elapsed.TotalMilliseconds;
                watch.Restart();
                flying.Step();
                step[i] = watch.Elapsed.TotalMilliseconds;
                watch.Restart();
                Stage.Render(flying, course, 0, size);
                render[i] = watch.Elapsed.TotalMilliseconds;
            }

            yield return null;
            var measured = recorder.LastValue;
            GC.KeepAlive(new byte[4096]);
            yield return null;
            var probe = recorder.LastValue;
            _game.enabled = true;
            Assert.That(flying.Phase, Is.EqualTo(RoundPhase.Moving), "計測の間は打球が終わらない（記録の追加を含めない）");
            Array.Sort(preview);
            Array.Sort(step);
            Array.Sort(render);
            var report = $"予測線（ウッド・最初の着地まで）p50 {preview[Samples / 2]:F3} ms / p95 {preview[Samples * 95 / 100]:F3} ms / 最大 {preview[Samples - 1]:F3} ms、" +
                $"飛んでいる球の Core の Step p95 {step[Samples * 95 / 100]:F3} ms、描画の更新（実際の軌跡を打ち出しから進め直す）p50 {render[Samples / 2]:F3} ms / p95 {render[Samples * 95 / 100]:F3} ms、" +
                $"割り当て {measured - baseline} バイト（確認 {probe}）";
            Debug.Log("[Performance] " + report);
            TestContext.WriteLine(report);
            Assert.That(recorder.Valid && probe >= baseline + 4096, Is.True, "割り当ての計数が働いている");
            Assert.That(measured - baseline, Is.LessThanOrEqualTo(0), "予測線・Step・描画の更新で割り当てをしない");
            Assert.That(preview[Samples * 95 / 100], Is.LessThan(8));
            Assert.That(render[Samples * 95 / 100], Is.LessThan(8));
        }
    }
}
