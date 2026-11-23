using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Game.App;
using Game.Core;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using MouseButton = UnityEngine.InputSystem.LowLevel.MouseButton;
using Object = UnityEngine.Object;

namespace Game.Unity.Tests
{
    /// <summary>
    /// 実シーンを読み込み、仮想のキーボードとマウスで操作する。シーンは実行ごとに別のシードで試合を作るので、
    /// 偶然の出来事を待たず、助走（障害物のない最初の 30 m）の中で確かめるか、必ずぶつかる操縦役を使う。
    /// </summary>
    public sealed class CinderLanesSceneTests
    {
        private const float M = CinderLanesStage.UnitsPerMeter;
        private static readonly ObstacleKind[] _kinds = { ObstacleKind.Wall, ObstacleKind.Hurdle, ObstacleKind.Beam };

        private TestKeyboard _keyboard;
        private Mouse _mouse;
        private CinderLanesGame _game;

        [UnitySetUp]
        public IEnumerator Load()
        {
            _keyboard = new TestKeyboard();
            _mouse = InputSystem.AddDevice<Mouse>();
            _mouse.MakeCurrent();
            EditorUtility.audioMasterMute = true;
            yield return SceneManager.LoadSceneAsync("CinderLanes");
            yield return null;
            _game = Object.FindAnyObjectByType<CinderLanesGame>();
            for (var i = 0; i < 30 && _game.HudRoot == null; i++) { yield return null; }
            Assert.That(_game.HudRoot, Is.Not.Null);
        }

        [UnityTearDown]
        public IEnumerator Unload()
        {
            Time.timeScale = 1;
            EditorUtility.audioMasterMute = false;
            InputSystem.RemoveDevice(_mouse);
            _keyboard.Dispose();
            yield return null;
        }

        private RunnerRun R => _game.Flow.Run;

        private RunConfig Config => _game.Flow.Rules.Config;

        private CinderLanesStage Stage => _game.Stage;

        private string Text(string name) => _game.HudRoot.Q<Label>(name).text;

        private bool IsShown(string name) => _game.HudRoot.Q<Button>(name).style.display.value == DisplayStyle.Flex;

        private IEnumerator StartGame()
        {
            yield return _keyboard.Tap(Key.Enter);
            Assert.That(_game.Flow.Screen, Is.EqualTo(AppScreen.Playing));
            yield return WaitUntil(() => R.Ticks > 0);
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

        /// <summary>実時間を止め、テストから Tick を進める（015 の知見）。</summary>
        private void StepTicks(int ticks)
        {
            Time.timeScale = 0;
            for (var i = 0; i < ticks && _game.Flow.Screen == AppScreen.Playing; i++)
            {
                _game.Flow.StepTick();
                _game.ConsumeEvents();
            }

            _game.Render();
        }

        [UnityTest]
        public IEnumerator Title_ShowsTheRunnerOnAnEmptyRoad_AndEnterStarts()
        {
            Assert.That(_game.Flow.Screen, Is.EqualTo(AppScreen.Title));
            Assert.That(_game.HudRoot.Q<Label>("headline").text, Is.EqualTo("CINDER LANES"));
            Assert.That(_game.HudRoot.Q<Label>("message").text, Does.Contain("ジャンプ").And.Contain("スライド"));
            foreach (var kind in _kinds) { Assert.That(Stage.Shown(kind), Is.Zero); }
            Assert.That(Stage.Runner.localPosition, Is.EqualTo(Vector3.zero));
            Assert.That(Stage.Motion.State, Is.EqualTo(CinderLanesStage.IdleState));
            Assert.That(_game.HudRoot.Q<Label>("credit").text, Is.EqualTo("© Unity Technologies Japan/UCL"), "ユニティちゃんライセンスの表記");
            yield return StartGame();
            yield return null;
            Assert.That(Stage.Motion.State, Is.EqualTo(CinderLanesStage.RunState));
            Assert.That(Stage.Shown(ObstacleKind.Wall) + Stage.Shown(ObstacleKind.Hurdle) + Stage.Shown(ObstacleKind.Beam), Is.GreaterThan(0), "前方のチャンクが描かれる");
            Assert.That(Text("distance"), Does.EndWith(" m"));
        }

        [UnityTest]
        public IEnumerator SpaceStartsTheRun_WithoutJumping()
        {
            yield return _keyboard.Tap(Key.Space);
            Assert.That(_game.Flow.Screen, Is.EqualTo(AppScreen.Playing));
            yield return WaitSeconds(.3f);
            Assert.That(R.Body.Vertical.Y, Is.Zero, "決定の Space ではジャンプしない");
            yield return _keyboard.Tap(Key.Space);
            yield return WaitUntil(() => R.Body.Vertical.Y > 0);
        }

        [UnityTest]
        public IEnumerator ArrowKeys_SteerJumpAndSlide_InTheRunway()
        {
            yield return StartGame();

            yield return _keyboard.Tap(Key.RightArrow);
            yield return WaitUntil(() => R.Body.Lane.To == 2 && !R.Body.Lane.IsMoving);
            yield return null;
            Assert.That(Stage.Runner.localPosition.x, Is.EqualTo(Config.LaneX(2) / M).Within(1e-4));

            yield return _keyboard.Tap(Key.UpArrow);
            yield return WaitUntil(() => R.Body.Vertical.Y > Config.HurdleHeight);
            yield return null;
            Assert.That(Stage.Runner.localPosition.y, Is.GreaterThan(0));
            Assert.That(Stage.Motion.State, Is.EqualTo(CinderLanesStage.JumpState));
            yield return WaitUntil(() => R.Body.Vertical.IsGrounded);

            yield return _keyboard.Tap(Key.DownArrow);
            yield return WaitUntil(() => R.Body.Vertical.IsSliding);
            yield return null;
            Assert.That(Stage.Motion.State, Is.EqualTo(CinderLanesStage.SlideState));
            yield return WaitUntil(() => !R.Body.Vertical.IsSliding);
            yield return null;
            Assert.That(Stage.Motion.State, Is.EqualTo(CinderLanesStage.RunState));

            Assert.That(R.Z, Is.LessThan(Config.RunwayMeters * (long)M), "ここまで助走の中");
            Assert.That(R.IsOver, Is.False);
        }

        [UnityTest]
        public IEnumerator Crash_ShowsTheResult_AndRetryStartsANewSeed()
        {
            yield return StartGame();
            var seed = R.Seed;
            _game.Flow.Pilot = new SeekerPilot();
            StepTicks(100_000);
            Assert.That(_game.Flow.Screen, Is.EqualTo(AppScreen.Result));
            yield return null;
            Assert.That(_game.HudRoot.Q("panel").style.display.value, Is.EqualTo(DisplayStyle.None), "倒れる間は結果のパネルを出さない");
            Assert.That(_game.HudRoot.Q<Label>("headline").text, Is.EqualTo("ぶつかった！"));
            Assert.That(Stage.Motion.State, Is.EqualTo(CinderLanesStage.CrashState));

            // 倒れるモーションは結果画面の間に実時間で進み、倒れたところで止まる。
            Time.timeScale = 1;
            var start = Stage.Motion.Time;
            yield return WaitUntil(() => Stage.Motion.Time > start + .05f);
            yield return WaitSeconds(4f);
            var end = Stage.Motion.Time;
            yield return WaitSeconds(.2f);
            Assert.That(Stage.Motion.Time, Is.EqualTo(end), "倒れたところで止まる");
            Assert.That(Stage.Runner.localPosition.y, Is.Zero, "空中でぶつかっても地面に落ちる");
            Assert.That(_game.HudRoot.Q("panel").style.display.value, Is.EqualTo(DisplayStyle.Flex), "倒れた後に結果のパネルを出す");
            Assert.That(_game.HudRoot.Q<Label>("message").text, Does.Contain($"{R.Meters} m"));
            Assert.That(IsShown("restart"), Is.True);
            Assert.That(IsShown("pause"), Is.False);

            // 自機は触れた時刻の位置で描く：触れた障害物は自機の当たり箱と重なる位置に描かれている。
            var crashed = ShownObstacles(R.CrashKind).Any(t => Mathf.Abs(t.localPosition.z) < (t.localScale.z / 2) + (Config.RunnerDepth / M / 2) + .01f);
            Assert.That(crashed, Is.True);

            _game.Flow.Pilot = null;
            yield return _keyboard.Tap(Key.R);
            Assert.That(_game.Flow.Screen, Is.EqualTo(AppScreen.Playing));
            Assert.That(R.Seed, Is.EqualTo(seed + 1));
            Assert.That(R.Meters, Is.LessThan(5));
        }

        [UnityTest]
        public IEnumerator BackToTitle_LooksLikeStartup()
        {
            yield return null;
            var runner = Stage.Runner.localPosition;
            var segment = Stage.Segment(0).localPosition;
            yield return StartGame();
            _game.Flow.Pilot = new Autopilot();
            StepTicks(3000);
            Assert.That(R.IsOver, Is.False);
            Assert.That(R.Meters, Is.GreaterThan(100));
            _game.Flow.Pilot = null;
            Time.timeScale = 1;
            yield return _keyboard.Tap(Key.Escape);
            yield return _keyboard.Tap(Key.T);
            Assert.That(_game.Flow.Screen, Is.EqualTo(AppScreen.Title));
            yield return null;
            Assert.That(Stage.Runner.localPosition, Is.EqualTo(runner));
            Assert.That(Stage.Model.localRotation, Is.EqualTo(Quaternion.identity));
            Assert.That(Stage.Motion, Is.EqualTo((CinderLanesStage.IdleState, 0f)));
            Assert.That(Stage.Segment(0).localPosition, Is.EqualTo(segment));
            foreach (var kind in _kinds) { Assert.That(Stage.Shown(kind), Is.Zero); }
            Assert.That(_game.HudRoot.Q("stats").style.display.value, Is.EqualTo(DisplayStyle.None));
        }

        [UnityTest]
        public IEnumerator LongRun_ChunkViewsDoNotAccumulate_AndMatchTheCore()
        {
            // 完了判定「長時間移動でチャンクが累積しない」の Unity 側：自動操縦で 6 万 Tick（約 17 分）走り、
            // シーンの物の数が変わらず、描いている障害物がいつも Core の生きているチャンクと一致する。
            yield return StartGame();
            var children = Stage.transform.childCount;
            var objects = Stage.GetComponentsInChildren<Transform>(true).Length;
            var roots = SceneManager.GetActiveScene().rootCount;
            _game.Flow.Pilot = new Autopilot();
            for (var i = 0; i < 120; i++)
            {
                StepTicks(500);
                Assert.That(R.IsOver, Is.False);
                Assert.That(R.Chunks.Count, Is.LessThanOrEqualTo(R.Chunks.Capacity));
                AssertObstaclesMatchCore();
            }

            Assert.That(R.Chunks.Recycled, Is.GreaterThan(100));
            Assert.That(R.Speed, Is.EqualTo(Config.TopSpeed));
            Assert.That(Stage.transform.childCount, Is.EqualTo(children));
            Assert.That(Stage.GetComponentsInChildren<Transform>(true).Length, Is.EqualTo(objects), "プールの外で作った物がない");
            Assert.That(SceneManager.GetActiveScene().rootCount, Is.EqualTo(roots));
            yield return null;
        }

        [UnityTest]
        public IEnumerator RunMotion_FollowsTheDistance_NotTheClock()
        {
            // 撮影で同じ絵になるよう、走りのモーションの位置は進んだ距離だけで決まる（実時間で進めない）。
            yield return StartGame();
            StepTicks(10);
            var motion = Stage.Motion;
            var ticks = R.Ticks;
            yield return WaitSeconds(.2f);
            Assert.That(R.Ticks, Is.EqualTo(ticks));
            Assert.That(Stage.Motion, Is.EqualTo(motion), "Tick が進まなければモーションも止まる");
            StepTicks(1);
            Assert.That(Stage.Motion.State, Is.EqualTo(CinderLanesStage.RunState));
            Assert.That(Stage.Motion.Time, Is.Not.EqualTo(motion.Time));
        }

        [UnityTest]
        public IEnumerator AfterASlide_RunBlendsBackGradually()
        {
            // スライドが終わった次の Tick から、スライドの姿勢から走りへ数 Tick かけて移る（いきなり戻らない）。
            yield return StartGame();
            _game.Flow.Pilot = new SlideOncePilot();
            Time.timeScale = 0;
            var recoveries = new List<float>();
            for (var i = 0; i < Config.SlideTicks + 30; i++)
            {
                _game.Flow.StepTick();
                _game.Render();
                if (!R.Body.Vertical.IsSliding) { recoveries.Add(Stage.Recovery); }
            }

            Assert.That(R.IsOver, Is.False);
            Assert.That(recoveries.Count, Is.EqualTo(30), "スライドは設定の Tick 数だけ続いた");
            Assert.That(recoveries[0], Is.GreaterThan(0).And.LessThan(.2f), "戻り始めはスライドの姿勢に近い");
            for (var i = 1; i < recoveries.Count; i++) { Assert.That(recoveries[i], Is.GreaterThanOrEqualTo(recoveries[i - 1])); }
            Assert.That(recoveries[recoveries.Count - 1], Is.EqualTo(1), "しばらくすると走りに戻りきる");
            Assert.That(recoveries.Count(r => r < 1), Is.GreaterThan(5), "数 Tick かけて戻る");
        }

        [UnityTest]
        public IEnumerator Pause_StopsTheClock()
        {
            yield return StartGame();
            yield return _keyboard.Tap(Key.Escape);
            Assert.That(_game.Flow.Screen, Is.EqualTo(AppScreen.Paused));
            var ticks = R.Ticks;
            yield return WaitSeconds(.2f);
            Assert.That(R.Ticks, Is.EqualTo(ticks));
            Assert.That(_game.HudRoot.Q<Label>("headline").text, Is.EqualTo("一時停止"));
            yield return _keyboard.Tap(Key.Enter);
            yield return WaitUntil(() => R.Ticks > ticks);
        }

        [UnityTest]
        public IEnumerator FocusLoss_PausesAndDropsPresses()
        {
            yield return StartGame();
            _game.SendMessage("OnApplicationFocus", false);
            Assert.That(_game.Flow.Screen, Is.EqualTo(AppScreen.Paused));
            yield return _keyboard.Tap(Key.RightArrow);
            yield return _keyboard.Tap(Key.Enter);
            Assert.That(_game.Flow.Screen, Is.EqualTo(AppScreen.Paused), "フォーカスのない間の入力は捨てる");
            _game.SendMessage("OnApplicationFocus", true);
            yield return null;
            yield return null;
            yield return _keyboard.Tap(Key.Enter);
            Assert.That(_game.Flow.Screen, Is.EqualTo(AppScreen.Playing));
            yield return WaitSeconds(.2f);
            Assert.That(R.Body.Lane.To, Is.EqualTo(1), "フォーカスのない間の → は持ち越さない");
        }

        [UnityTest]
        public IEnumerator HudButtons_StartPauseRestartAndBackToTitle()
        {
            yield return ClickButton("submit");
            Assert.That(_game.Flow.Screen, Is.EqualTo(AppScreen.Playing));
            yield return ClickButton("pause");
            Assert.That(_game.Flow.Screen, Is.EqualTo(AppScreen.Paused));
            yield return ClickButton("restart");
            Assert.That(_game.Flow.Screen, Is.EqualTo(AppScreen.Playing));
            Assert.That(R.Ticks, Is.LessThan(30));
            yield return ClickButton("pause");
            yield return ClickButton("title");
            Assert.That(_game.Flow.Screen, Is.EqualTo(AppScreen.Title));
        }

        [UnityTest]
        public IEnumerator ClickThatRestoresFocus_DoesNotQuit_ButNextClickDoes()
        {
            var requests = 0;
            _game.QuitHandler = () => requests++;
            yield return null;
            _game.SendMessage("OnApplicationFocus", false);
            yield return null;
            _game.SendMessage("OnApplicationFocus", true);
            SendButtonClick("quit");
            yield return null;
            yield return null;
            Assert.That(requests, Is.Zero, "フォーカスを戻すクリックでは終了しない");
            yield return ClickButton("quit");
            Assert.That(requests, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator SettingsAsset_MatchesTheCodeDefaults()
        {
            // 生成スクリプトはアセットを上書きしないので、コードの既定値を変えたらアセットも直す（024 の知見）。
            Assert.That(JsonUtility.ToJson(_game.Settings.Rules), Is.EqualTo(JsonUtility.ToJson(new RunConfig())));
            Assert.That(_game.Stage.Mismatch(_game.Settings.Rules), Is.Null);
            yield return null;
        }

        private IEnumerable<Transform> ShownObstacles(ObstacleKind kind)
        {
            for (var i = 0; i < Stage.Shown(kind); i++) { yield return Stage.Pooled(kind, i); }
        }

        /// <summary>描いている障害物の位置・大きさの集合が、Core の生きているチャンクの障害物と一致する。</summary>
        private void AssertObstaclesMatchCore()
        {
            foreach (var kind in _kinds)
            {
                var expected = new List<string>();
                for (var i = 0; i < R.Chunks.Count; i++)
                {
                    foreach (var obstacle in R.Chunks.ChunkAt(i).Obstacles)
                    {
                        if (obstacle.Kind != kind) { continue; }
                        var box = obstacle.Box(Config);
                        var z = R.Chunks.StartAt(i) - R.Z + box.Z;
                        expected.Add(Describe(new Vector3((box.X + (box.Width / 2f)) / M, (box.Y + (box.Height / 2f)) / M, (z + (box.Depth / 2f)) / M), new Vector3(box.Width, box.Height, box.Depth) / M));
                    }
                }

                var actual = ShownObstacles(kind).Select(t => Describe(t.localPosition, t.localScale)).ToList();
                Assert.That(ShownObstacles(kind).All(t => t.gameObject.activeSelf), Is.True);
                for (var i = 0; i < Stage.Shown(kind); i++)
                {
                    // 奥の面が自機の中心より後ろなら半透明、前なら元の見た目。
                    var item = Stage.Pooled(kind, i);
                    var back = item.localPosition.z + (item.localScale.z / 2);
                    Assert.That(Stage.IsPassedLook(kind, i), Is.EqualTo(back <= 1e-4f), $"{kind} z {item.localPosition.z}");
                    var lane = Mathf.RoundToInt(item.localPosition.x / (Config.LaneWidth / M)) + 1;
                    var blocksView = kind == ObstacleKind.Beam || lane == R.Body.Lane.To;
                    var visible = blocksView ? back > 1e-4f : back > -Stage.HideBehind + 1e-4f;
                    Assert.That(Stage.IsVisible(kind, i), Is.EqualTo(visible), $"{kind} レーン {lane} z {item.localPosition.z}：通り過ぎた Beam・自機のレーンのもの・後ろへ流れたものは描かない");
                }
                Assert.That(actual, Is.EquivalentTo(expected), kind.ToString());
                Assert.That(Stage.Shown(kind), Is.LessThanOrEqualTo(Stage.PoolSize(kind)));
            }
        }

        private static string Describe(Vector3 position, Vector3 scale) => $"{position.x:F3},{position.y:F3},{position.z:F3}/{scale.x:F3},{scale.y:F3},{scale.z:F3}";

        /// <summary>最初に呼ばれた Tick にだけスライドする（助走の中）。</summary>
        private sealed class SlideOncePilot : IPilot
        {
            private bool _done;

            public Command CommandFor(RunnerRun run)
            {
                var leap = _done ? Leap.None : Leap.Slide;
                _done = true;
                return new Command(Steer.None, leap);
            }
        }

        /// <summary>何もせず、いちばん近い前方の障害物のレーンへ移り続ける（必ずぶつかる）。</summary>
        private sealed class SeekerPilot : IPilot
        {
            public Command CommandFor(RunnerRun run)
            {
                var best = long.MaxValue;
                var lane = run.Body.Lane.To;
                for (var i = 0; i < run.Chunks.Count; i++)
                {
                    foreach (var obstacle in run.Chunks.ChunkAt(i).Obstacles)
                    {
                        var z = run.Chunks.StartAt(i) + obstacle.Box(run.Config).Z;
                        if (z < run.Z + (3 * (long)M) || z >= best) { continue; }
                        best = z;
                        lane = obstacle.Lane;
                    }
                }

                var current = run.Body.Lane.To;
                return new Command(lane < current ? Steer.Left : lane > current ? Steer.Right : Steer.None, Leap.None);
            }
        }

        private IEnumerator ClickButton(string name)
        {
            yield return null;
            yield return null;
            var button = _game.HudRoot.Q<Button>(name);
            Assert.That(IsShown(name), Is.True, name + " が表示されていない");
            var panelPoint = button.worldBound.center;
            var panel = _game.HudRoot.panel;
            var rootSize = panel.visualTree.worldBound.size;
            var point = new Vector2(panelPoint.x / rootSize.x * Screen.width, Screen.height - (panelPoint.y / rootSize.y * Screen.height));
            var delivered = false;
            void OnClicked() => delivered = true;
            button.clicked += OnClicked;
            try
            {
                SetMouse(point);
                yield return null;
                SetMouse(point, MouseButton.Left);
                yield return null;
                SetMouse(point);
                yield return null;

                // バッチ実行では UI イベントが届かないので、届かなかったときだけ直接送る（022 の知見）。
                if (!delivered) { SendButtonClick(name); }
                yield return null;
                yield return null;
            }
            finally
            {
                button.clicked -= OnClicked;
            }
        }

        private void SetMouse(Vector2 position, MouseButton? pressed = null)
        {
            var state = new MouseState { position = position };
            if (pressed.HasValue) { state = state.WithButton(pressed.Value); }
            InputSystem.QueueStateEvent(_mouse, state);
        }

        // ボタンへポインタの押下と離しを送る（ClickEvent では Button.clicked が呼ばれない。013 の知見）。
        private void SendButtonClick(string name)
        {
            var button = _game.HudRoot.Q<Button>(name);
            var panelPoint = button.worldBound.center;
            using (var down = PointerDownEvent.GetPooled(new Event { type = EventType.MouseDown, mousePosition = panelPoint, button = 0 }))
            {
                down.target = button;
                button.SendEvent(down);
            }

            using (var up = PointerUpEvent.GetPooled(new Event { type = EventType.MouseUp, mousePosition = panelPoint, button = 0 }))
            {
                up.target = button;
                button.SendEvent(up);
            }
        }
    }
}
