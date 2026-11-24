using System;
using System.Collections;
using Game.App;
using Game.Core;
using IntGeometry;
using NUnit.Framework;
using Unity.Profiling;
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
    public sealed class DewdropSnipSceneTests
    {
        private const int TickLimit = 60 * 60;

        private TestKeyboard _keyboard;
        private Mouse _mouse;
        private DewdropSnipGame _game;

        [UnitySetUp]
        public IEnumerator Load()
        {
            _keyboard = new TestKeyboard();
            _mouse = InputSystem.AddDevice<Mouse>();
            _mouse.MakeCurrent();
            yield return SceneManager.LoadSceneAsync("DewdropSnip");
            yield return null;
            _game = Object.FindAnyObjectByType<DewdropSnipGame>();
            for (var i = 0; i < 30 && _game.HudRoot == null; i++) { yield return null; }
            Assert.That(_game.HudRoot, Is.Not.Null);
        }

        [UnityTearDown]
        public IEnumerator Unload()
        {
            Time.timeScale = 1;
            InputSystem.RemoveDevice(_mouse);
            _keyboard.Dispose();
            yield return null;
        }

        private PuzzleRun Run => _game.Flow.Run;

        private DewdropSnipStage Stage => _game.Stage;

        private string Text(string name) => _game.HudRoot.Q<Label>(name).text;

        private bool Shown(string name) => _game.HudRoot.Q(name).style.display.value == DisplayStyle.Flex;

        private static Vec2i P(int x, int y) => new(x * Board.DataUnit, y * Board.DataUnit);

        /// <summary>時計を止めて（timeScale 0）タイトルから問題選択を経て 1 問目を始める。Tick はテストが進める。</summary>
        private IEnumerator StartPaused()
        {
            Time.timeScale = 0;
            yield return _keyboard.Tap(Key.Enter);
            yield return _keyboard.Tap(Key.Enter);
            Assert.That(_game.Flow.Screen, Is.EqualTo(AppScreen.Playing));
        }

        private void SetMouse(Vec2i board, bool pressed)
        {
            var state = new MouseState { position = _game.BoardToScreen(board) };
            if (pressed) { state = state.WithButton(MouseButton.Left); }
            InputSystem.QueueStateEvent(_mouse, state);
        }

        /// <summary>盤の点をマウスで順にたどる（最初の点で押し、最後の点で離す）。1 点ごとに 1 フレーム。</summary>
        private IEnumerator Drag(params Vec2i[] points)
        {
            SetMouse(points[0], false);
            yield return null;
            for (var i = 0; i < points.Length; i++)
            {
                SetMouse(points[i], true);
                yield return null;
            }

            SetMouse(points[points.Length - 1], false);
            yield return null;
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

        [UnityTest]
        public IEnumerator Stage_IsComplete_AndTheTitleExplainsTheControls()
        {
            yield return null;
            Assert.That(Object.FindObjectsByType<DewdropSnipStage>(FindObjectsInactive.Include), Has.Length.EqualTo(1), "Stage のインスタンスは 1 つ");
            Assert.That(Stage.Mismatch(), Is.Null);
            Assert.That(Text("headline"), Is.EqualTo("DEWDROP SNIP"));
            Assert.That(Text("message").Contains("左ボタンを押したまま", StringComparison.Ordinal), Is.True);
            Assert.That(Shown("bar"), Is.False);
            Assert.That(Shown("tiles"), Is.False);
            Assert.That(Stage.Body.enabled, Is.True, "タイトルの背景に 1 問目の初期状態を描く");
        }

        [UnityTest]
        public IEnumerator MouseDrag_AcrossTheRope_CutsIt_AndTheBodyFallsIntoTheGoal()
        {
            yield return StartPaused();
            Step(30);
            yield return Drag(P(112, 118), P(120, 118), P(136, 118), P(144, 118));
            Assert.That(Run.Moves, Is.EqualTo(1), "なぞりがロープを横切って切れた");
            Assert.That(Run.Holds(0), Is.False);
            Assert.That(Text("moves"), Is.EqualTo("手数  1"));
            StepUntil(() => _game.Flow.Screen == AppScreen.Outcome);
            Assert.That(Run.Status, Is.EqualTo(RunStatus.Succeeded));
            yield return null;
            Assert.That(Shown("panel"), Is.True);
            Assert.That(Text("headline"), Is.EqualTo("とどいた！"));
            Assert.That(_game.Flow.Progress.Unlocked, Is.EqualTo(2));
        }

        [UnityTest]
        public IEnumerator FastDrag_InOneFrame_StillCuts()
        {
            yield return StartPaused();
            yield return Drag(P(40, 118), P(220, 118));
            Assert.That(Run.Moves, Is.EqualTo(1), "1 フレームで大きく横切っても切れる");
        }

        [UnityTest]
        public IEnumerator PressWithoutMoving_DoesNotCut_AndDraggingTheCutRopeAgainDoesNotCount()
        {
            yield return StartPaused();
            yield return Drag(P(128, 118));
            Assert.That(Run.Moves, Is.Zero, "押しただけでは切れない");
            yield return Drag(P(112, 118), P(144, 118));
            Assert.That(Run.Moves, Is.EqualTo(1));
            yield return Drag(P(112, 130), P(144, 130), P(112, 126));
            Assert.That(Run.Moves, Is.EqualTo(1), "切れたロープの断片をなぞっても手数は増えない");
        }

        [UnityTest]
        public IEnumerator RetryKey_RestoresThePuzzle()
        {
            yield return StartPaused();
            var fresh = Run.Hash();
            yield return Drag(P(112, 118), P(144, 118));
            Step(20);
            yield return _keyboard.Tap(Key.R);
            Assert.That(Run.Hash(), Is.EqualTo(fresh));
            Assert.That(Run.Moves, Is.Zero);
            yield return null;
            Assert.That(Text("moves"), Is.EqualTo("手数  0"));
        }

        [UnityTest]
        public IEnumerator RopeMesh_FollowsTheCoreNodes()
        {
            yield return StartPaused();
            Step(17);
            yield return Drag(P(112, 118), P(144, 118));
            Step(5);
            var vertices = Stage.RopeMesh.vertices;
            var run = Run;
            var cut = run.CutLinkOf(0);
            for (var j = 0; j < run.LinksOf(0); j++)
            {
                var v = DewdropSnipStage.SegmentIndex(0, j) * 4;
                var start = (vertices[v] + vertices[v + 1]) / 2;
                var end = (vertices[v + 2] + vertices[v + 3]) / 2;
                if (!run.IsLinkActive(0, j))
                {
                    Assert.That(start, Is.EqualTo(Vector3.zero), $"切った節 {j}（{cut}）と露玉へつながる節は描かない");
                    Assert.That(end, Is.EqualTo(Vector3.zero));
                    continue;
                }

                Assert.That(Vector3.Distance(start, DewdropSnipStage.ToLocal(run.RopePoint(0, j))), Is.LessThan(1e-4f), $"節 {j} の始め");
                Assert.That(Vector3.Distance(end, DewdropSnipStage.ToLocal(run.RopePoint(0, j + 1))), Is.LessThan(1e-4f), $"節 {j} の終わり");
            }

            Assert.That(Vector3.Distance(Stage.Body.transform.localPosition, DewdropSnipStage.ToLocal(run.Body)), Is.LessThan(1e-4f));
            Assert.That(Stage.Snip.enabled, Is.True, "切った直後の印");
        }

        [UnityTest]
        public IEnumerator ScreenAndBoardCoordinates_RoundTrip()
        {
            yield return null;
            foreach (var point in new[] { P(0, 0), P(256, 144), P(128, 72), P(37, 101) })
            {
                var back = _game.ScreenToBoard(_game.BoardToScreen(point));
                Assert.That(Math.Abs(back.X - point.X), Is.LessThan(Board.Unit / 50));
                Assert.That(Math.Abs(back.Y - point.Y), Is.LessThan(Board.Unit / 50));
            }

            // 盤の全体がカメラに収まる（落ちる前の露玉と、上の帯の下の支点が見える）。
            var bottomLeft = _game.ViewCamera.WorldToViewportPoint(Stage.transform.TransformPoint(DewdropSnipStage.ToLocal(P(0, 0))));
            var topRight = _game.ViewCamera.WorldToViewportPoint(Stage.transform.TransformPoint(DewdropSnipStage.ToLocal(P(256, 144))));
            Assert.That(bottomLeft.x, Is.GreaterThanOrEqualTo(0));
            Assert.That(bottomLeft.y, Is.GreaterThanOrEqualTo(0));
            Assert.That(topRight.x, Is.LessThanOrEqualTo(1));
            Assert.That(topRight.y, Is.LessThanOrEqualTo(1));
        }

        [UnityTest]
        public IEnumerator SelectScreen_LockedTilesDoNothing_ClickingAnUnlockedTileStarts()
        {
            yield return _keyboard.Tap(Key.Enter);
            Assert.That(_game.Flow.Screen, Is.EqualTo(AppScreen.Select));
            yield return null;
            Assert.That(Shown("tiles"), Is.True);
            var locked = _game.HudRoot.Q<Button>(DewdropSnipGame.TileName(1));
            Assert.That(locked.enabledSelf, Is.False, "未解放の札は押せない");
            Assert.That(locked.Q<Label>("tile-name").text, Is.EqualTo("？"));
            yield return ClickTile(1);
            Assert.That(_game.Flow.Screen, Is.EqualTo(AppScreen.Select));

            yield return ClickTile(0);
            Assert.That(_game.Flow.Screen, Is.EqualTo(AppScreen.Playing), "解放済みの札を押すと始まる");
            Assert.That(_game.Flow.Cursor, Is.Zero);
        }

        /// <summary>
        /// 試遊の指摘：タイトルでクリックすると問題選択へ移り、同じクリックを離したときにカーソルの下の札が押されて問が始まっていた。
        /// マウスの押下（タイトル）→ 離す → 札のクリック、の順で届けても問題選択に留まり、押し直したクリックで始まる。
        /// </summary>
        [UnityTest]
        public IEnumerator TitleClick_OverATile_DoesNotStartThatPuzzle()
        {
            yield return null;
            var point = TileScreenPoint(0);
            SetScreenMouse(point, false);
            yield return null;
            SetScreenMouse(point, true);
            yield return null;
            Assert.That(_game.Flow.Screen, Is.EqualTo(AppScreen.Select), "タイトルのクリックで問題選択へ");
            yield return null;
            yield return null;
            SetScreenMouse(point, false);
            yield return null;
            SendButtonClick(DewdropSnipGame.TileName(0));
            yield return null;
            yield return null;
            Assert.That(_game.Flow.Screen, Is.EqualTo(AppScreen.Select), "同じクリックの離しで札が押されても始まらない");

            yield return ClickTile(0);
            Assert.That(_game.Flow.Screen, Is.EqualTo(AppScreen.Playing), "問題選択で押し直すと始まる");
        }

        /// <summary>レビュー P2-1：最後の移動とボタンの解放が同じ入力の更新で届いても、離した位置までの線分で切れる。</summary>
        [UnityTest]
        public IEnumerator ReleaseAndMoveInOneMouseUpdate_StillCuts()
        {
            yield return StartPaused();
            SetMouse(P(112, 118), false);
            yield return null;
            SetMouse(P(112, 118), true);
            yield return null;
            Assert.That(_game.Flow.SwipePoint, Is.Not.Null);
            SetMouse(P(144, 118), false);
            yield return null;
            Assert.That(Run.Moves, Is.EqualTo(1));
            Assert.That(_game.Flow.SwipePoint, Is.Null);
        }

        [UnityTest]
        public IEnumerator SolvingUnlocksTheNextTile_AndArrowsSelectIt()
        {
            yield return StartPaused();
            var pilot = new Snipper(CutPlan.Parse(_game.Settings.Solutions.text));
            pilot.Reset(0);
            _game.Flow.Pilot = pilot;
            StepUntil(() => _game.Flow.Screen == AppScreen.Outcome);
            yield return _keyboard.Tap(Key.Escape);
            Assert.That(_game.Flow.Screen, Is.EqualTo(AppScreen.Select));
            yield return _keyboard.Tap(Key.RightArrow);
            Assert.That(_game.Flow.Cursor, Is.EqualTo(1));
            yield return _keyboard.Tap(Key.RightArrow);
            Assert.That(_game.Flow.Cursor, Is.EqualTo(1), "未解放の問へは進まない");
            yield return null;
            Assert.That(_game.HudRoot.Q<Button>(DewdropSnipGame.TileName(1)).enabledSelf, Is.True);
            Assert.That(_game.HudRoot.Q<Button>(DewdropSnipGame.TileName(0)).Q<Label>("tile-best").text, Is.EqualTo("最少 1 手"));
            yield return _keyboard.Tap(Key.Enter);
            Assert.That(Run.Puzzle, Is.SameAs(_game.Flow.Book.Puzzles[1]));
            yield return null;
            Assert.That(Text("puzzle").Contains(_game.Flow.Book.Puzzles[1].Name, StringComparison.Ordinal), Is.True);
        }

        [UnityTest]
        public IEnumerator ReturningToTitle_LooksLikeStartup()
        {
            yield return null;
            var body = Stage.Body.transform.localPosition;
            var headline = Text("headline");
            yield return StartPaused();
            yield return Drag(P(112, 118), P(144, 118));
            Step(20);
            yield return _keyboard.Tap(Key.Escape);
            yield return _keyboard.Tap(Key.Backspace);
            yield return _keyboard.Tap(Key.Escape);
            Assert.That(_game.Flow.Screen, Is.EqualTo(AppScreen.Title));
            yield return null;
            Assert.That(Text("headline"), Is.EqualTo(headline));
            Assert.That(Stage.Body.transform.localPosition, Is.EqualTo(body), "前の試合の露玉の位置が残らない");
            Assert.That(Shown("bar"), Is.False);
            Assert.That(Stage.Trail.enabled, Is.False);
            Assert.That(Stage.Snip.enabled, Is.False);
            Assert.That(Run, Is.Null, "タイトルでは試合がない");
        }

        [UnityTest]
        public IEnumerator FocusLoss_PausesAndEndsTheSwipe()
        {
            yield return StartPaused();
            SetMouse(P(112, 118), false);
            yield return null;
            SetMouse(P(112, 118), true);
            yield return null;
            Assert.That(_game.Flow.SwipePoint, Is.Not.Null);
            _game.SendMessage("OnApplicationFocus", false);
            Assert.That(_game.Flow.Screen, Is.EqualTo(AppScreen.Paused));
            Assert.That(_game.Flow.SwipePoint, Is.Null);
            _game.SendMessage("OnApplicationFocus", true);
            yield return _keyboard.Tap(Key.Enter);
            Assert.That(_game.Flow.Screen, Is.EqualTo(AppScreen.Playing));
            SetMouse(P(144, 118), true);
            yield return null;
            Assert.That(Run.Moves, Is.Zero, "フォーカスを失う前の押下からはなぞりを続けない");
            SetMouse(P(144, 118), false);
            yield return null;
        }

        [UnityTest]
        public IEnumerator TrailFollowsTheDrag_AndFades()
        {
            yield return StartPaused();
            SetMouse(P(100, 110), false);
            yield return null;
            SetMouse(P(100, 110), true);
            yield return null;
            SetMouse(P(110, 112), true);
            yield return null;
            _game.Render();
            Assert.That(Stage.Trail.enabled, Is.True);
            Assert.That(Stage.Trail.positionCount, Is.EqualTo(2));
            SetMouse(P(110, 112), false);
            yield return null;
            Step(_game.Settings.TrailTicks);
            Assert.That(Stage.Trail.enabled, Is.False, "数 Tick で消える");
        }

        [UnityTest]
        public IEnumerator SettingsAsset_IsValid_AndRejectsBadValues()
        {
            yield return null;
            Assert.DoesNotThrow(() => _game.Settings.Validate());
            var settings = Object.Instantiate(_game.Settings);
            try
            {
                settings.Volume = float.NaN;
                Assert.Throws<ArgumentOutOfRangeException>(() => settings.Validate());
                settings.Volume = .1f;
                settings.Rules.MaxLinks = DewdropSnipStage.MaxLinks + 1;
                Assert.Throws<ArgumentOutOfRangeException>(() => settings.Validate());
                settings.Rules.MaxLinks = DewdropSnipStage.MaxLinks;
                settings.TrailTicks = 0;
                Assert.Throws<ArgumentOutOfRangeException>(() => settings.Validate());
            }
            finally
            {
                Object.DestroyImmediate(settings);
            }
        }

        [UnityTest]
        public IEnumerator StagePrefabMismatch_DetectsMissingParts()
        {
            yield return null;
            var copy = Object.Instantiate(Stage);
            try
            {
                Assert.That(copy.Mismatch(), Is.Null);
                copy.Wall(2).drawMode = SpriteDrawMode.Simple;
                Assert.That(copy.Mismatch(), Is.Not.Null, "壁がタイル表示でない");
                copy.Wall(2).drawMode = SpriteDrawMode.Tiled;
                copy.Spike(9).sprite = null;
                Assert.That(copy.Mismatch(), Is.Not.Null, "トゲの絵がない");
                copy.Spike(9).sprite = Stage.Spike(9).sprite;
                Assert.That(copy.Mismatch(), Is.Null);

                // レビュー P3-1：ロープ・軌跡の置き場をずらすと、見えているロープと切断の判定がずれるので拒否する。
                copy.Ropes.transform.localPosition = new Vector3(1, 0, 0);
                Assert.That(copy.Mismatch(), Is.Not.Null, "ロープの Mesh の位置がずれている");
                copy.Ropes.transform.localPosition = Vector3.zero;
                copy.Ropes.transform.localScale = new Vector3(2, 1, 1);
                Assert.That(copy.Mismatch(), Is.Not.Null, "ロープの Mesh の大きさが違う");
                copy.Ropes.transform.localScale = Vector3.one;
                copy.Ropes.transform.localRotation = Quaternion.Euler(0, 0, 10);
                Assert.That(copy.Mismatch(), Is.Not.Null, "ロープの Mesh が回っている");
                copy.Ropes.transform.localRotation = Quaternion.identity;
                copy.Trail.transform.localPosition = new Vector3(0, -1, 0);
                Assert.That(copy.Mismatch(), Is.Not.Null, "軌跡の位置がずれている");
                copy.Trail.transform.localPosition = Vector3.zero;
                copy.Trail.useWorldSpace = true;
                Assert.That(copy.Mismatch(), Is.Not.Null, "軌跡が世界座標");
                copy.Trail.useWorldSpace = false;
                Assert.That(copy.Mismatch(), Is.Null);
            }
            finally
            {
                Object.DestroyImmediate(copy.gameObject);
            }
        }

        /// <summary>ロープの描画（Mesh の頂点の書き込み）は毎フレーム割り当てをしない。いちばん節の多い 10 問目で測る。</summary>
        [UnityTest]
        public IEnumerator RopeRendering_DoesNotAllocate()
        {
            yield return null;
            var run = new PuzzleRun(_game.Settings.Rules, _game.Flow.Book.Puzzles[9]);
            const int Samples = 300;
            var step = new double[Samples];
            var render = new double[Samples];
            var watch = new System.Diagnostics.Stopwatch();
            for (var i = 0; i < 60; i++)
            {
                run.Tick();
                Stage.Render(run, 18);
            }

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
                run.Tick();
                step[i] = watch.Elapsed.TotalMilliseconds;
                watch.Restart();
                Stage.Render(run, 18);
                render[i] = watch.Elapsed.TotalMilliseconds;
            }

            yield return null;
            var measured = recorder.LastValue;
            GC.KeepAlive(new byte[4096]);
            yield return null;
            var probe = recorder.LastValue;
            _game.enabled = true;
            Array.Sort(step);
            Array.Sort(render);
            var report = $"10 問目（ロープ 3 本・節 40）・{Samples} Tick：Core の Tick p50 {step[Samples / 2]:F3} ms / p95 {step[Samples * 95 / 100]:F3} ms、" +
                $"描画の更新 p50 {render[Samples / 2]:F3} ms / p95 {render[Samples * 95 / 100]:F3} ms、割り当て {measured - baseline} バイト（確認 {probe}）";
            Debug.Log("[Performance] " + report);
            TestContext.WriteLine(report);
            Assert.That(recorder.Valid && probe >= baseline + 4096, Is.True, "割り当ての計数が働いている");
            Assert.That(measured - baseline, Is.LessThanOrEqualTo(0), "Tick と描画の更新で割り当てをしない");
            Assert.That(step[Samples * 95 / 100], Is.LessThan(4));
            Assert.That(render[Samples * 95 / 100], Is.LessThan(4));
        }

        private void SetScreenMouse(Vector2 screen, bool pressed)
        {
            var state = new MouseState { position = screen };
            if (pressed) { state = state.WithButton(MouseButton.Left); }
            InputSystem.QueueStateEvent(_mouse, state);
        }

        /// <summary>札の中心の画面の位置（ピクセル、左下原点）。</summary>
        private Vector2 TileScreenPoint(int index)
        {
            var panel = _game.HudRoot.panel;
            var root = panel.visualTree.worldBound.size;
            var center = _game.HudRoot.Q<Button>(DewdropSnipGame.TileName(index)).worldBound.center;
            if (float.IsNaN(center.x) || root.x <= 0)
            {
                // タイトルでは札が隠れていて大きさが決まらないので、問題選択の画面と同じ配置（パネルの中央付近）を使う。
                return new Vector2(Screen.width / 2f, Screen.height / 2f);
            }

            return new Vector2(center.x / root.x * Screen.width, Screen.height - (center.y / root.y * Screen.height));
        }

        /// <summary>
        /// 札をマウスでクリックする：押して離し、UI の clicked が届かなければ（バッチ実行では UI イベントが配送されない）ポインタのイベントを直接送る。
        /// GUI のエディタでは届くので二重に押さない（022 の知見）。
        /// </summary>
        private IEnumerator ClickTile(int index)
        {
            yield return null;
            var button = _game.HudRoot.Q<Button>(DewdropSnipGame.TileName(index));
            var point = TileScreenPoint(index);
            var delivered = false;
            void OnClicked() => delivered = true;
            button.clicked += OnClicked;
            try
            {
                SetScreenMouse(point, false);
                yield return null;
                SetScreenMouse(point, true);
                yield return null;
                SetScreenMouse(point, false);
                yield return null;
                if (!delivered) { SendButtonClick(DewdropSnipGame.TileName(index)); }
                yield return null;
                yield return null;
            }
            finally
            {
                button.clicked -= OnClicked;
            }
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
