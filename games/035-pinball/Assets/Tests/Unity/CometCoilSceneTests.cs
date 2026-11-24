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
    public sealed class CometCoilSceneTests
    {
        private const int TickLimit = 60 * 60 * 20;

        private TestKeyboard _keyboard;
        private CometCoilGame _game;

        [UnitySetUp]
        public IEnumerator Load()
        {
            _keyboard = new TestKeyboard();
            yield return SceneManager.LoadSceneAsync("CometCoil");
            yield return null;
            _game = Object.FindAnyObjectByType<CometCoilGame>();
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

        private PinballRun Run => _game.Flow.Run;

        private CometCoilStage Stage => _game.Stage;

        private string Text(string name) => _game.HudRoot.Q<Label>(name).text;

        private bool Shown(string name) => _game.HudRoot.Q(name).resolvedStyle.display == DisplayStyle.Flex;

        /// <summary>時計を止めて（timeScale 0）試合を始める。Tick はテストが進める。</summary>
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

        [UnityTest]
        public IEnumerator Stage_IsComplete_AndTheTitleExplainsTheControls()
        {
            yield return null;
            Assert.That(Object.FindObjectsByType<CometCoilStage>(FindObjectsInactive.Include), Has.Length.EqualTo(1), "Stage のインスタンスは 1 つ");
            Assert.That(Stage.Mismatch(_game.Flow.Table, _game.Flow.Config), Is.Null);
            Assert.That(Text("headline"), Is.EqualTo("COMET COIL"));
            Assert.That(Shown("panel"), Is.True);
            Assert.That(Text("controls").Contains("Space", StringComparison.Ordinal), Is.True);
            Assert.That(Text("controls").Contains("左フリッパー", StringComparison.Ordinal), Is.True);
            Assert.That(Text("message").Contains("マルチボール", StringComparison.Ordinal), Is.True);
            Assert.That(Stage.Ball(0).enabled, Is.True, "タイトルの背景にレーンの球を描く");
            Assert.That(Stage.Ball(1).enabled, Is.False);
            for (var i = 0; i < Table.BumperCount; i++) { Assert.That(Stage.Bumper(i).color, Is.EqualTo(Color.white), "当たっていないバンパーは光らない"); }
        }

        /// <summary>試合の始め（まだ何にも当たっていない）にバンパーが光らない（光った Tick の初期値で引き算が桁あふれした不具合の回帰）。</summary>
        [UnityTest]
        public IEnumerator BumpersAreDarkUntilHit()
        {
            yield return StartPaused();
            for (var t = 0; t < 30; t++)
            {
                Step(1);
                for (var i = 0; i < Table.BumperCount; i++) { Assert.That(Stage.Bumper(i).color, Is.EqualTo(Color.white), $"Tick {Run.Ticks}"); }
            }

            _game.Flow.Pilot = new Autopilot();
            StepUntil(() => Run.LastEvents.BumperHits > 0);
            var lit = Run.LastEvents.BumpersLit;
            for (var i = 0; i < Table.BumperCount; i++)
            {
                Assert.That(Stage.Bumper(i).color == Color.white, Is.EqualTo((lit & (1 << i)) == 0), $"バンパー {i}：当たった Tick に光る");
            }

            // 光らせる長さだけ進める。その間にもう一度当たらなかったバンパーは消えている。
            var again = 0;
            for (var t = 0; t < _game.Settings.FlashTicks; t++)
            {
                Step(1);
                again |= Run.LastEvents.BumpersLit;
            }

            for (var i = 0; i < Table.BumperCount; i++)
            {
                if ((again & (1 << i)) == 0) { Assert.That(Stage.Bumper(i).color, Is.EqualTo(Color.white), $"バンパー {i}：光は数 Tick で消える"); }
            }
        }

        /// <summary>
        /// 実時間で遊ぶ：Z を押した後、最初に Tick が進んだフレームでフリッパーが上がり始めている（入力が即応）。
        /// 描画の回転も Core の角度と同じ。離すと戻る。
        /// </summary>
        [UnityTest]
        public IEnumerator FlipperKey_MovesTheFlipperInTheFirstTickAfterThePress()
        {
            yield return _keyboard.Tap(Key.Enter);
            yield return WaitUntil(() => Run != null && Run.Ticks > 5);
            var rest = _game.Flow.Table.Left.RestAngle;
            _keyboard.Hold(Key.Z);
            yield return WaitUntil(() => _game.Flow.Held.Left);

            // 押下を反映した時点の Tick の次の Tick で、もう上がり始めている。
            var applied = Run.Ticks;
            yield return WaitUntil(() => Run.Ticks > applied);
            Assert.That(Run.FlipperAngle(FlipperSide.Left), Is.GreaterThan(rest), "押した後の最初の Tick で動く");
            Assert.That(Mathf.DeltaAngle(Stage.LeftFlipper.transform.localEulerAngles.z, CometCoilStage.Degrees(Run.FlipperAngle(FlipperSide.Left))), Is.EqualTo(0).Within(.01f));

            yield return WaitUntil(() => Run.FlipperOf(FlipperSide.Left).IsUp);
            _keyboard.Hold();
            yield return WaitUntil(() => Run.FlipperOf(FlipperSide.Left).Angle == rest);
            Assert.That(Run.FlipperOf(FlipperSide.Right).Angle, Is.EqualTo(_game.Flow.Table.Right.RestAngle), "反対のフリッパーは動かない");
        }

        /// <summary>1 つの操作に束ねた複数のキー：どれでも上がり、片方を離してももう片方を押している間は上がったまま（032 の知見）。</summary>
        [UnityTest]
        public IEnumerator AnyBoundKeyHoldsTheFlipper_AndRolloverKeepsItUp()
        {
            yield return _keyboard.Tap(Key.Enter);
            foreach (var key in new[] { Key.Slash, Key.RightArrow, Key.RightShift })
            {
                _keyboard.Hold(key);
                yield return WaitUntil(() => Run.FlipperOf(FlipperSide.Right).IsUp);
                _keyboard.Hold();
                yield return WaitUntil(() => Run.FlipperOf(FlipperSide.Right).Angle == _game.Flow.Table.Right.RestAngle);
            }

            _keyboard.Hold(Key.Z);
            yield return null;
            _keyboard.Hold(Key.Z, Key.LeftShift);
            yield return null;
            _keyboard.Hold(Key.LeftShift);
            yield return WaitUntil(() => Run.FlipperOf(FlipperSide.Left).IsUp);
            yield return WaitSeconds(.2f);
            Assert.That(Run.FlipperOf(FlipperSide.Left).IsUp, Is.True, "Z を離しても左 Shift で上がったまま");
            Assert.That(_game.Flow.Held.Left, Is.True);
            _keyboard.Hold();
            yield return WaitUntil(() => !_game.Flow.Held.Left);
        }

        [UnityTest]
        public IEnumerator HoldingSpace_PullsThePlunger_AndReleasingLaunches()
        {
            yield return _keyboard.Tap(Key.Enter);
            yield return WaitUntil(() => Run != null);
            _keyboard.Hold(Key.Space);
            yield return WaitUntil(() => Run.Pull >= 30);
            _game.Render();
            Assert.That(Stage.Ball(0).transform.localPosition.y, Is.LessThan(CometCoilStage.ToLocal(_game.Flow.Table.LaneRest).y), "引くと球も下がって見える");
            Assert.That(Stage.Plunger.transform.localPosition.y, Is.LessThan(CometCoilStage.PlungerAt(_game.Flow.Table, _game.Flow.Config, 0).y));
            _keyboard.Hold();
            yield return WaitUntil(() => Run.BallAt(0).State == BallState.Free);
            Assert.That(Run.BallAt(0).Velocity.Y, Is.GreaterThan(0));
            Assert.That(Run.Pull, Is.Zero);
        }

        /// <summary>操縦役で遊ばせ、各 Tick の描画（球・フリッパー・ターゲット）が Core の状態と一致する。</summary>
        [UnityTest]
        public IEnumerator Rendering_FollowsTheCoreState()
        {
            yield return StartPaused();
            _game.Flow.Pilot = new Autopilot();
            var checkedFree = 0;
            for (var t = 0; t < 900 && _game.Flow.Screen == AppScreen.Playing; t++)
            {
                Step(1);
                for (var slot = 0; slot < Run.BallSlots; slot++)
                {
                    var ball = Run.BallAt(slot);
                    Assert.That(Stage.Ball(slot).enabled, Is.EqualTo(ball.State != BallState.Idle));
                    if (ball.State != BallState.Free) { continue; }
                    checkedFree++;
                    Assert.That(Vector3.Distance(Stage.Ball(slot).transform.localPosition, CometCoilStage.ToLocal(ball.Position)), Is.LessThan(1e-4f));
                }

                foreach (var side in new[] { FlipperSide.Left, FlipperSide.Right })
                {
                    var angle = CometCoilStage.Degrees(Run.FlipperOf(side).Angle);
                    Assert.That(Mathf.DeltaAngle(Stage.Flipper(side).transform.localEulerAngles.z, angle), Is.EqualTo(0).Within(.01f));
                }

                for (var i = 0; i < Table.TargetCount; i++) { Assert.That(Stage.Target(i).color.a, Is.EqualTo(Run.TargetUp(i) ? 1f : .22f).Within(1e-4f)); }
            }

            Assert.That(checkedFree, Is.GreaterThan(500));
        }

        [UnityTest]
        public IEnumerator ThreeBallsLost_ShowsTheResult_AndEnterReplays()
        {
            yield return StartPaused();
            _game.Flow.Pilot = new Autopilot();
            var first = Run;
            StepUntil(() => _game.Flow.Screen == AppScreen.Result);
            yield return null;
            Assert.That(Text("headline"), Is.EqualTo("GAME OVER"));
            Assert.That(Shown("panel"), Is.True);
            var score = first.Score.ToString("N0", System.Globalization.CultureInfo.InvariantCulture);
            Assert.That(Text("message").Contains(score, StringComparison.Ordinal), Is.True);
            Assert.That(Text("score"), Is.EqualTo(score));
            Assert.That(Text("best"), Is.EqualTo("BEST  " + score));

            _game.Flow.Pilot = null;
            yield return _keyboard.Tap(Key.Space);
            Assert.That(_game.Flow.Screen, Is.EqualTo(AppScreen.Playing));
            Assert.That(Run, Is.Not.SameAs(first));
            Assert.That(Run.Seed, Is.Not.EqualTo(first.Seed), "再戦は新しいシード");
            yield return null;
            Assert.That(Text("score"), Is.EqualTo("0"));
            Assert.That(Text("ball"), Is.EqualTo("BALL  1 / 3"));
            Assert.That(Shown("panel"), Is.False);
        }

        [UnityTest]
        public IEnumerator ReturningToTitle_LooksLikeStartup()
        {
            yield return null;
            var balls = new[] { Stage.Ball(0).transform.localPosition, Stage.Ball(1).enabled ? Stage.Ball(1).transform.localPosition : Vector3.zero };
            var left = Stage.LeftFlipper.transform.localRotation;
            var right = Stage.RightFlipper.transform.localRotation;
            var plunger = Stage.Plunger.transform.localPosition;
            var score = Text("score");
            var ball = Text("ball");

            yield return StartPaused();
            _game.Flow.Pilot = new Autopilot();
            StepUntil(() => Run.LastEvents.TargetHits > 0);
            Step(3);
            _game.Flow.Pilot = null;
            _game.Apply(new Intent(IntentKind.LeftDown, 0));
            Step(2);
            yield return _keyboard.Tap(Key.Escape);
            yield return _keyboard.Tap(Key.Backspace);
            Assert.That(_game.Flow.Screen, Is.EqualTo(AppScreen.Title));
            yield return null;

            Assert.That(Stage.Ball(0).transform.localPosition, Is.EqualTo(balls[0]), "前の試合の球の位置が残らない");
            Assert.That(Stage.Ball(1).enabled, Is.False);
            Assert.That(Stage.LeftFlipper.transform.localRotation, Is.EqualTo(left));
            Assert.That(Stage.RightFlipper.transform.localRotation, Is.EqualTo(right));
            Assert.That(Stage.Plunger.transform.localPosition, Is.EqualTo(plunger));
            for (var i = 0; i < Table.TargetCount; i++) { Assert.That(Stage.Target(i).color, Is.EqualTo(Color.white)); }
            for (var i = 0; i < Table.BumperCount; i++) { Assert.That(Stage.Bumper(i).color, Is.EqualTo(Color.white)); }
            Assert.That(Text("score"), Is.EqualTo(score));
            Assert.That(Text("ball"), Is.EqualTo(ball));
            Assert.That(Text("headline"), Is.EqualTo("COMET COIL"));
            Assert.That(Run, Is.Null);
        }

        [UnityTest]
        public IEnumerator FocusLoss_PausesAndReleasesTheFlipper()
        {
            yield return _keyboard.Tap(Key.Enter);
            _keyboard.Hold(Key.Z);
            yield return WaitUntil(() => Run.FlipperOf(FlipperSide.Left).IsUp);
            _game.SendMessage("OnApplicationFocus", false);
            Assert.That(_game.Flow.Screen, Is.EqualTo(AppScreen.Paused));
            Assert.That(_game.Flow.Held.Left, Is.False);
            yield return null;
            Assert.That(Text("headline"), Is.EqualTo("一時停止"));

            // 戻っても自動では再開しない。Enter で再開すると、押したままの Z は押していないとみなしてフリッパーは戻る。
            _game.SendMessage("OnApplicationFocus", true);
            yield return WaitSeconds(.1f);
            Assert.That(_game.Flow.Screen, Is.EqualTo(AppScreen.Paused));
            _keyboard.Hold(Key.Z, Key.Enter);
            yield return null;
            _keyboard.Hold(Key.Z);
            yield return WaitUntil(() => _game.Flow.Screen == AppScreen.Playing);
            yield return WaitUntil(() => Run.FlipperOf(FlipperSide.Left).Angle == _game.Flow.Table.Left.RestAngle);
            _keyboard.Hold();
            yield return null;
            _keyboard.Hold(Key.Z);
            yield return WaitUntil(() => Run.FlipperOf(FlipperSide.Left).IsUp, 2f);
        }

        [UnityTest]
        public IEnumerator SettingsAsset_IsValid_AndRejectsBadValues()
        {
            yield return null;
            Assert.DoesNotThrow(() => _game.Settings.Validate());
            Assert.That(_game.Settings.Rules.FlipMaxSpeed, Is.EqualTo(new PinballConfig().FlipMaxSpeed), "コードの既定値とアセットがそろっている（024）");
            var settings = Object.Instantiate(_game.Settings);
            try
            {
                settings.Volume = float.NaN;
                Assert.Throws<ArgumentOutOfRangeException>(() => settings.Validate());
                settings.Volume = .1f;
                settings.FlashTicks = 0;
                Assert.Throws<ArgumentOutOfRangeException>(() => settings.Validate());
                settings.FlashTicks = 8;
                settings.Rules.FlipMaxSpeed = 4000;
                Assert.That(() => settings.Validate(), Throws.ArgumentException, "すり抜けない条件を破るフリッパーの速さ");
                settings.Rules.FlipMaxSpeed = 600;
                Assert.DoesNotThrow(() => settings.Validate());
            }
            finally
            {
                Object.DestroyImmediate(settings);
            }
        }

        /// <summary>Stage Prefab の照合は、台のデータと同じ関数で決めた位置・向き・大きさ・絵まで見る（023・027）。</summary>
        [UnityTest]
        public IEnumerator StagePrefabMismatch_DetectsMovedOrWrongParts()
        {
            yield return null;
            var table = _game.Flow.Table;
            var config = _game.Flow.Config;
            var copy = Object.Instantiate(Stage);
            try
            {
                Assert.That(copy.Mismatch(table, config), Is.Null);
                Check(copy, c => c.Rail(3).transform.localPosition += new Vector3(.1f, 0, 0), c => c.Rail(3).transform.localPosition -= new Vector3(.1f, 0, 0), "壁の位置");
                Check(copy, c => c.Rail(5).transform.localRotation *= Quaternion.Euler(0, 0, 5), c => c.Rail(5).transform.localRotation *= Quaternion.Euler(0, 0, -5), "壁の向き");
                Check(copy, c => c.Rail(1).size += new Vector2(.5f, 0), c => c.Rail(1).size -= new Vector2(.5f, 0), "壁の長さ");
                var gateIndex = GateIndex(table);
                var gateSprite = copy.Rail(gateIndex).sprite;
                Check(copy, c => c.Rail(gateIndex).sprite = c.Rail(0).sprite, c => c.Rail(gateIndex).sprite = gateSprite, "扉の絵");
                Check(copy, c => c.Bumper(2).transform.localScale *= 1.2f, c => c.Bumper(2).transform.localScale /= 1.2f, "バンパーの大きさ");
                Check(copy, c => c.Target(1).transform.localPosition += new Vector3(0, .3f, 0), c => c.Target(1).transform.localPosition -= new Vector3(0, .3f, 0), "ターゲットの位置");
                Check(copy, c => c.RightFlipper.transform.localPosition += new Vector3(.2f, 0, 0), c => c.RightFlipper.transform.localPosition -= new Vector3(.2f, 0, 0), "フリッパーの軸");
                var flipperSprite = copy.LeftFlipper.sprite;
                Check(copy, c => c.LeftFlipper.sprite = c.Ball(0).sprite, c => c.LeftFlipper.sprite = flipperSprite, "フリッパーの絵");
                Check(copy, c => c.Ball(1).transform.localScale *= 2, c => c.Ball(1).transform.localScale /= 2, "球の大きさ");
                var background = copy.Background.sprite;
                Check(copy, c => c.Background.sprite = null, c => c.Background.sprite = background, "背景の絵");
            }
            finally
            {
                Object.DestroyImmediate(copy.gameObject);
            }

            void Check(CometCoilStage stage, Action<CometCoilStage> breakIt, Action<CometCoilStage> fix, string what)
            {
                breakIt(stage);
                Assert.That(stage.Mismatch(table, config), Is.Not.Null, what);
                fix(stage);
                Assert.That(stage.Mismatch(table, config), Is.Null, what + "（戻した後）");
            }
        }

        /// <summary>
        /// マルチボール中（台の上に 2 個）の 1 Tick の Core の処理と描画の更新の時間、割り当て 0。
        /// 計測の Tick は、複製した試合で操縦役が決めておいた入力を再生して進める（操縦役の先読みは計測に入れない）。
        /// </summary>
        [UnityTest]
        public IEnumerator MultiballTick_IsFast_AndDoesNotAllocate()
        {
            yield return null;
            var config = _game.Flow.Config;
            var table = _game.Flow.Table;
            var pilot = new Autopilot();
            pilot.Reset(12);
            var run = new PinballRun(config, table, 1);
            for (var t = 0; t < 60000 && !run.IsMultiball; t++) { run.Step(pilot.InputFor(run)); }
            Assert.That(run.IsMultiball, Is.True, "マルチボールになった");
            for (var i = 0; i < 30; i++)
            {
                run.Step(pilot.InputFor(run));
                Stage.Render(run, 8);
            }

            const int Samples = 300;

            // 操縦役の入力を、複製した試合で先に作っておく（先読みの割り当てと時間を計測に入れない）。同じ入力で本物も同じに進む。
            var inputs = new PinballInput[Samples];
            var rehearsal = run.Clone();
            for (var i = 0; i < Samples; i++)
            {
                inputs[i] = pilot.InputFor(rehearsal);
                rehearsal.Step(inputs[i]);
            }

            var step = new double[Samples];
            var render = new double[Samples];
            var twoBalls = 0;
            var watch = new System.Diagnostics.Stopwatch();

            // Mono では GC.GetAllocatedBytesForCurrentThread が常に 0 を返すので、フレームごとの割り当ての計数で測る（027 の知見）。
            _game.enabled = false;
            using var recorder = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame");
            yield return null;
            yield return null;
            yield return null;
            var baseline = recorder.LastValue;
            for (var i = 0; i < Samples; i++)
            {
                watch.Restart();
                run.Step(inputs[i]);
                step[i] = watch.Elapsed.TotalMilliseconds;
                watch.Restart();
                Stage.Render(run, 8);
                render[i] = watch.Elapsed.TotalMilliseconds;
                if (run.BallsActive == 2) { twoBalls++; }
            }

            yield return null;
            var measured = recorder.LastValue;
            GC.KeepAlive(new byte[4096]);
            yield return null;
            var probe = recorder.LastValue;
            _game.enabled = true;
            Array.Sort(step);
            Array.Sort(render);
            var report = $"マルチボール（{Samples} Tick のうち台の上 2 個が {twoBalls} Tick）：Core の Step p50 {step[Samples / 2]:F3} ms / p95 {step[Samples * 95 / 100]:F3} ms / 最大 {step[Samples - 1]:F3} ms、" +
                $"描画の更新 p50 {render[Samples / 2]:F3} ms / p95 {render[Samples * 95 / 100]:F3} ms、割り当て {measured - baseline} バイト（確認 {probe}）";
            Debug.Log("[Performance] " + report);
            TestContext.WriteLine(report);
            Assert.That(twoBalls, Is.GreaterThan(Samples / 2), "2 個の場面を十分に含む");
            Assert.That(run.Hash(), Is.EqualTo(rehearsal.Hash()), "再生した入力で複製と同じに進んだ");
            Assert.That(recorder.Valid && probe >= baseline + 4096, Is.True, "割り当ての計数が働いている");
            Assert.That(measured - baseline, Is.LessThanOrEqualTo(0), "Step と描画の更新で割り当てをしない");
            Assert.That(step[Samples * 95 / 100], Is.LessThan(2));
            Assert.That(render[Samples * 95 / 100], Is.LessThan(2));
        }

        private static int GateIndex(Table table)
        {
            var rail = 0;
            foreach (var collider in table.Colliders)
            {
                if (collider.Kind == ColliderKind.Gate) { return rail; }
                if (collider.Kind == ColliderKind.Wall) { rail++; }
            }

            throw new InvalidOperationException("扉がありません。");
        }
    }
}
