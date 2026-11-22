using System;
using System.Collections;
using Game.App;
using Game.Core;
using NUnit.Framework;
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
    public sealed class BaselineBloomSceneTests
    {
        private const int TickLimit = 60 * 60 * 20;

        private TestKeyboard _keyboard;
        private Mouse _mouse;
        private BaselineBloomGame _game;

        [UnitySetUp]
        public IEnumerator Load()
        {
            _keyboard = new TestKeyboard();
            _mouse = InputSystem.AddDevice<Mouse>();
            yield return SceneManager.LoadSceneAsync("BaselineBloom");
            yield return null;
            _game = Object.FindAnyObjectByType<BaselineBloomGame>();
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

        private TennisMatch M => _game.Flow.Match;

        private BaselineBloomStage Stage => _game.Stage;

        private string Text(string name) => _game.HudRoot.Q<Label>(name).text;

        private bool IsShown(string name) => _game.HudRoot.Q(name).style.display.value == DisplayStyle.Flex;

        private IEnumerator StartGame()
        {
            yield return _keyboard.Tap(Key.Enter);
            Assert.That(_game.Flow.Screen, Is.EqualTo(AppScreen.Playing));
            yield return WaitUntil(() => M.Tick > 0);
        }

        /// <summary>時計を止め（timeScale 0）、操縦役を付けてから Enter で始める。</summary>
        private IEnumerator StartPiloted(IPilot pilot)
        {
            Time.timeScale = 0;
            _game.Flow.Pilot = pilot;
            yield return _keyboard.Tap(Key.Enter);
            Assert.That(_game.Flow.Screen, Is.EqualTo(AppScreen.Playing));
        }

        // バッチ実行はフレームが極端に短いので、フレーム数ではなく実時間の上限で待つ。
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

        /// <summary>時計を止め（timeScale 0）、Tick を直接進める。出来事は画面の側（音・案内）でも受け取る。</summary>
        private void StepUntil(Func<bool> done)
        {
            Time.timeScale = 0;
            for (var i = 0; i < TickLimit && !done(); i++)
            {
                _game.Flow.StepTick();
                _game.ConsumeEvents();
            }

            _game.Render();
            Assert.That(done(), Is.True, "Tick の上限までに条件を満たさなかった");
        }

        /// <summary>奥から手前の選手へ向かう打ち合いの場面を作る。球は手前の選手の正面（横に <paramref name="offsetX"/> ずらす）へ来る。</summary>
        private void StageIncoming(int offsetX = 0)
        {
            var config = M.Config;
            var from = new Vec3(offsetX, 100000, 9000);
            var velocity = ShotSolver.Solve(from, offsetX, -80000, config.StrokeSpeed, config.StrokeClearance, config.Gravity);
            M.StageRally(Side.Far, from, velocity, new Vec3(0, -112000, 0), new Vec3(0, 120000, 0));
        }

        [UnityTest]
        public IEnumerator Title_ShowsTheFirstPointPositions_AndNoMatch()
        {
            yield return null;
            Assert.That(Object.FindObjectsByType<BaselineBloomStage>(FindObjectsInactive.Include), Has.Length.EqualTo(1), "Stage のインスタンスは 1 つ");
            Assert.That(Stage.Mismatch(), Is.Null);
            Assert.That(_game.Flow.Match, Is.Null);
            Assert.That(Text("headline"), Is.EqualTo("BASELINE BLOOM"));
            Assert.That(Text("message"), Does.Contain("7 ポイント"));
            Assert.That(IsShown("submit") && IsShown("quit"), Is.True);
            Assert.That(IsShown("scoreboard"), Is.False);

            var config = _game.Flow.Config;
            Assert.That(Stage.Near.transform.position, Is.EqualTo(CourtProjection.ToWorld(Positions.Serve(Side.Near, ServeCourt.Deuce, config))));
            Assert.That(Stage.Far.transform.position, Is.EqualTo(CourtProjection.ToWorld(Positions.Receive(Side.Far, ServeCourt.Deuce, config))));
        }

        [UnityTest]
        public IEnumerator Camera_ShowsTheCourtAndTheLobHeightBelowTheHudBand()
        {
            yield return null;
            var camera = _game.ViewCamera;
            var y = CourtProjection.Meters(Court.HalfLength) + BaselineBloomGame.ViewBackMeters;
            var points = new[]
            {
                CourtProjection.ToWorld(-BaselineBloomGame.ViewSideMeters, -y, 0),
                CourtProjection.ToWorld(BaselineBloomGame.ViewSideMeters, -y, 0),
                CourtProjection.ToWorld(0, y, BaselineBloomGame.ViewHeightMeters) + new Vector3(0, BaselineBloomGame.HudBand, 0),
            };
            foreach (var point in points)
            {
                var viewport = camera.WorldToViewportPoint(point);
                Assert.That(viewport.x, Is.InRange(-1e-4f, 1 + 1e-4f), point.ToString());
                Assert.That(viewport.y, Is.InRange(-1e-4f, 1 + 1e-4f), point.ToString());
            }
        }

        [UnityTest]
        public IEnumerator ServeKeys_TossThenHitNearTheApex()
        {
            yield return StartGame();
            Assert.That(Text("guide"), Does.Contain("トス"));
            var server = M.Near.Position;
            _keyboard.Hold(Key.RightArrow, Key.UpArrow);
            yield return WaitUntil(() => M.Near.Position.X > server.X);
            _keyboard.Hold();
            Assert.That(M.Near.Position.Y, Is.EqualTo(server.Y), "サーブ待ちのサーバーは横にだけ動く（立ち位置を選ぶ）");
            yield return null;
            Assert.That(Stage.Ball.transform.position.x, Is.EqualTo(CourtProjection.ToWorld(M.Near.Position.WithZ(M.Config.TossZ)).x).Within(1e-4f), "球も手と一緒に動く");

            yield return _keyboard.Tap(Key.Z);
            yield return WaitUntil(() => M.Rally.Phase == RallyPhase.Toss);
            var tossed = M.Near.Position;
            _keyboard.Hold(Key.LeftArrow);
            yield return WaitSeconds(.05f);
            _keyboard.Hold();
            Assert.That(M.Near.Position, Is.EqualTo(tossed), "トス中は動かない");
            yield return WaitUntil(() => _game.HudRoot.Q<Label>("guide").text.Contains("もう一度"));
            yield return WaitUntil(() => M.Rally.TossTick >= M.Config.TossApexTick - 2);
            yield return _keyboard.Tap(Key.Z);
            yield return WaitUntil(() => M.Rally.Phase != RallyPhase.Toss);
            Assert.That(M.Rally.Shots, Is.EqualTo(1), "サーブが飛んだ");
            Assert.That(M.Rally.Phase, Is.EqualTo(RallyPhase.InPlay).Or.EqualTo(RallyPhase.Over).Or.EqualTo(RallyPhase.Ready));
        }

        [UnityTest]
        public IEnumerator ArrowKeys_MoveTheReceiver_AndReleaseStops()
        {
            yield return StartGame();
            StageIncoming();
            Time.timeScale = 1;
            var x = M.Near.Position.X;
            _keyboard.Hold(Key.LeftArrow);
            yield return WaitUntil(() => M.Near.Position.X < x - 5000);
            _keyboard.Hold();
            yield return null;
            yield return null;
            var stopped = M.Near.Position;
            yield return WaitSeconds(.1f);
            if (M.Phase == MatchPhase.Point) { Assert.That(M.Near.Position, Is.EqualTo(stopped), "離した後は止まる"); }
            Assert.That(Stage.Near.transform.position.x, Is.LessThan(CourtProjection.ToWorld(new Vec3(x, -112000, 0)).x), "描画も左へ");
        }

        [UnityTest]
        public IEnumerator StrokeKey_ReturnsAReachableBall_AndShowsTheTimingHint()
        {
            yield return StartGame();
            StageIncoming();

            // 球が来る前に押しておけば、構えて待って理想の打点で打つ。
            yield return _keyboard.Tap(Key.Z);
            yield return WaitUntil(() => M.Near.IsReady || M.Rally.LastHitter == Side.Near);
            yield return WaitUntil(() => M.Rally.LastHitter == Side.Near || M.Phase != MatchPhase.Point);
            Assert.That(M.Rally.LastHitter, Is.EqualTo(Side.Near), "届く球を返した");
            Assert.That(M.Ball.Velocity.Y, Is.GreaterThan(0));
            Assert.That(_game.TimingHint, Is.Not.Empty);
            yield return null;
            Assert.That(Text("guide"), Is.EqualTo(_game.TimingHint));
        }

        [UnityTest]
        public IEnumerator StrokeKey_WhiffsAnUnreachableBall()
        {
            yield return StartGame();
            StageIncoming(offsetX: 35000);
            yield return _keyboard.Tap(Key.Z);
            yield return WaitUntil(() => M.Phase != MatchPhase.Point || M.Ball.Position.Y < -125000);
            Assert.That(M.Rally.LastHitter, Is.EqualTo(Side.Far), "届かない球は返せない");
        }

        [UnityTest]
        public IEnumerator LobKey_FliesHigherThanTheNetByMeters()
        {
            yield return StartPiloted(null);
            StageIncoming();
            _game.Flow.Apply(Intent.Down(Control.Lob, 0));
            StepUntil(() => M.Rally.LastHitter == Side.Near);
            _game.Flow.Apply(Intent.Up(Control.Lob, 0));
            var apex = 0;
            for (var i = 0; i < 200 && M.Ball.Position.Y < 0 + 60000 && M.Phase == MatchPhase.Point; i++)
            {
                _game.Flow.StepTick();
                apex = Math.Max(apex, M.Ball.Position.Z);
            }

            Assert.That(apex, Is.GreaterThan(Court.NetHeight + 25000), "ロブは高い");
        }

        [UnityTest]
        public IEnumerator OutCall_IsShownWithTheBounceMark()
        {
            yield return StartPiloted(null);
            var config = M.Config;
            var from = new Vec3(0, -110000, 9000);
            M.StageRally(Side.Near, from, ShotSolver.Solve(from, 0, Court.HalfLength + 15000, config.StrokeSpeed, config.StrokeClearance, config.Gravity), from.WithZ(0), new Vec3(0, 120000, 0));
            StepUntil(() => M.Phase == MatchPhase.Gap);
            yield return null;
            Assert.That(Text("verdict"), Is.EqualTo("OUT  —  CPU のポイント"));
            Assert.That(IsShown("verdict"), Is.True);
            Assert.That(Stage.BounceMark.enabled, Is.True);
            Assert.That(Stage.BounceMark.transform.position, Is.EqualTo(CourtProjection.ToWorld(M.LastBounce!.Value)));
            Assert.That(Text("far-points"), Is.EqualTo("1"));

            StepUntil(() => M.Phase == MatchPhase.Point);
            yield return null;
            Assert.That(IsShown("verdict"), Is.False, "次のポイントで消える");
        }

        [UnityTest]
        public IEnumerator BallBehindTheNetIsDrawnBehindIt()
        {
            yield return StartPiloted(null);
            StageIncoming();
            _game.Render();
            Assert.That(M.Ball.Position.Y, Is.GreaterThan(0));
            Assert.That(Stage.Ball.sortingOrder, Is.LessThan(Stage.Net.sortingOrder), "奥の球はネットの後ろ");
            StepUntil(() => M.Ball.Position.Y < -20000);
            Assert.That(Stage.Ball.sortingOrder, Is.GreaterThan(Stage.Net.sortingOrder), "手前の球はネットの前");
            Assert.That(Stage.Shadow.transform.position, Is.EqualTo(CourtProjection.ToWorld(M.Ball.Position.WithZ(0))), "影は真下の地面");
        }

        [UnityTest]
        public IEnumerator Autopilot_FinishesTheMatch_AndTheResultShowsTheFinalScore()
        {
            yield return StartPiloted(new Autopilot());
            StepUntil(() => _game.Flow.Screen == AppScreen.Result);
            yield return null;
            Assert.That(Text("headline"), Is.EqualTo(M.Winner == Side.Near ? "YOU WIN!" : "CPU WINS"));
            var winner = M.Winner!.Value;
            Assert.That(Text(winner == Side.Near ? "near-points" : "far-points"), Is.EqualTo(M.Score.Points(winner).ToString()));
            Assert.That(M.Score.Points(winner), Is.GreaterThanOrEqualTo(7));
            Assert.That(Text("message"), Does.Contain("いちばん長いラリー"));
            Assert.That(Stage.Ball.enabled, Is.False, "試合が終わったら球は消える");
            Assert.That(IsShown("submit") && IsShown("restart") && IsShown("title"), Is.True);
        }

        [UnityTest]
        public IEnumerator BackToTitle_RestoresTheStartupView()
        {
            yield return null;
            var initial = (Stage.Near.transform.position, Stage.Near.sprite, Stage.Near.flipX, Stage.Far.transform.position, Stage.Far.sprite, Stage.Ball.transform.position, Stage.Ball.enabled, Stage.BounceMark.enabled);
            yield return StartPiloted(new Autopilot());
            StepUntil(() => M.PointsPlayed >= 2 && M.Rally.Phase == RallyPhase.InPlay && M.LastBounce.HasValue);
            Time.timeScale = 1;
            _game.Flow.Pilot = null;
            yield return _keyboard.Tap(Key.Escape);
            yield return _keyboard.Tap(Key.T);
            Assert.That(_game.Flow.Screen, Is.EqualTo(AppScreen.Title));
            yield return null;
            var now = (Stage.Near.transform.position, Stage.Near.sprite, Stage.Near.flipX, Stage.Far.transform.position, Stage.Far.sprite, Stage.Ball.transform.position, Stage.Ball.enabled, Stage.BounceMark.enabled);
            Assert.That(now, Is.EqualTo(initial), "起動直後と同じ表示");
        }

        [UnityTest]
        public IEnumerator PauseKey_StopsTime()
        {
            yield return StartGame();
            yield return _keyboard.Tap(Key.Escape);
            Assert.That(_game.Flow.Screen, Is.EqualTo(AppScreen.Paused));
            var tick = M.Tick;
            yield return WaitSeconds(.2f);
            Assert.That(M.Tick, Is.EqualTo(tick));
            Assert.That(Text("headline"), Is.EqualTo("一時停止"));
            Assert.That(IsShown("restart") && IsShown("title"), Is.True);
            yield return _keyboard.Tap(Key.P);
            Assert.That(_game.Flow.Screen, Is.EqualTo(AppScreen.Playing));
            Assert.That(IsShown("panel"), Is.False);
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
            Assert.That(M.Tick, Is.LessThan(30));
            yield return ClickButton("pause");
            yield return ClickButton("title");
            Assert.That(_game.Flow.Screen, Is.EqualTo(AppScreen.Title));
        }

        [UnityTest]
        public IEnumerator QuitButton_RequestsQuitOnce()
        {
            var requests = 0;
            _game.QuitHandler = () => requests++;
            yield return ClickButton("quit");
            Assert.That(requests, Is.EqualTo(1));
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
        public IEnumerator FocusLoss_PausesAndDropsHeldKeys()
        {
            yield return StartGame();
            StageIncoming();
            _keyboard.Hold(Key.LeftArrow);
            var x = M.Near.Position.X;
            yield return WaitUntil(() => M.Near.Position.X < x);
            _game.SendMessage("OnApplicationFocus", false);
            Assert.That(_game.Flow.Screen, Is.EqualTo(AppScreen.Paused));
            _keyboard.Hold();
            yield return null;
            _game.SendMessage("OnApplicationFocus", true);
            yield return null;
            yield return null;
            var paused = M.Near.Position;
            yield return _keyboard.Tap(Key.Enter);
            Assert.That(_game.Flow.Screen, Is.EqualTo(AppScreen.Playing));
            yield return WaitSeconds(.1f);
            if (M.Phase == MatchPhase.Point) { Assert.That(M.Near.Position, Is.EqualTo(paused), "押していた左は持ち越さない"); }
        }

        [UnityTest]
        public IEnumerator SettingsAsset_IsValid_AndCpuVersusAutopilotFinishes()
        {
            yield return null;
            Assert.DoesNotThrow(() => _game.Settings.Validate());
            var match = new TennisMatch(_game.Settings.Rules, seed: 1);
            var pilot = new Autopilot();
            for (var i = 0; i < TickLimit && !match.IsFinished; i++) { match.Step(pilot.CommandFor(match)); }
            Assert.That(match.IsFinished, Is.True, "設定アセットの値で試合が決着する（CPU の強さを変えたら確かめる）");
        }

        private void SetMouse(Vector2 position, MouseButton? pressed = null)
        {
            var state = new MouseState { position = position };
            if (pressed.HasValue) { state = state.WithButton(pressed.Value); }
            InputSystem.QueueStateEvent(_mouse, state);
        }

        // 実際の座標でボタンに当たることを確かめ、マウスで押して離す。バッチ実行ではUIイベントが配送されないので、
        // マウスのクリックが届かなかったときだけボタンへ直接送る（GUI のエディタでは届くので、二重に押さない）。
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
            Assert.That(panel.Pick(RuntimePanelUtils.ScreenToPanel(panel, new Vector2(point.x, Screen.height - point.y))), Is.SameAs(button));

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
                if (!delivered) { SendButtonClick(name); }
                yield return null;
                yield return null;
            }
            finally
            {
                button.clicked -= OnClicked;
            }
        }

        // ボタンへポインタの押下と離しを送る（ClickEvent では Button.clicked が呼ばれない）。
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
