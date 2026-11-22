using System;
using System.Collections;
using Game.App;
using Game.Core;
using NUnit.Framework;
using TileMotion;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.TestTools.Utils;
using UnityEngine.UIElements;
using MouseButton = UnityEngine.InputSystem.LowLevel.MouseButton;
using Object = UnityEngine.Object;

namespace Game.Unity.Tests
{
    /// <summary>実シーンを読み込み、仮想のキーボードとマウスで操作する。</summary>
    public sealed class HaloSiegeSceneTests
    {
        private const int TickLimit = 60 * 60 * 6;

        private TestKeyboard _keyboard;
        private Mouse _mouse;
        private HaloSiegeGame _game;

        [UnitySetUp]
        public IEnumerator Load()
        {
            _keyboard = new TestKeyboard();
            _mouse = InputSystem.AddDevice<Mouse>();
            _mouse.MakeCurrent();
            yield return SceneManager.LoadSceneAsync("HaloSiege");
            yield return null;
            _game = Object.FindAnyObjectByType<HaloSiegeGame>();
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

        private SiegeRun R => _game.Flow.Run;

        private SiegeConfig Rules => _game.Settings.Rules;

        private HaloSiegeStage Stage => _game.Stage;

        private string Text(string name) => _game.HudRoot.Q<Label>(name).text;

        private bool IsShown(string name) => _game.HudRoot.Q<Button>(name).style.display.value == DisplayStyle.Flex;

        /// <summary>アリーナの点（Core の単位）が今のカメラで映る画面の位置。</summary>
        private Vector2 ScreenOf(Vec2i point)
        {
            var world = Stage.transform.TransformPoint(HaloSiegeStage.Local(point));
            var screen = _game.ViewCamera.WorldToScreenPoint(world);
            return new Vector2(screen.x, screen.y);
        }

        private IEnumerator StartGame()
        {
            yield return _keyboard.Tap(Key.Enter);
            Assert.That(_game.Flow.Screen, Is.EqualTo(AppScreen.Playing));
            yield return WaitUntil(() => R.Elapsed > 0);
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

        /// <summary>時計を止め（timeScale 0）、Tick を直接進める。</summary>
        private FlowEvents StepUntil(Func<bool> done)
        {
            Time.timeScale = 0;
            var events = FlowEvents.None;
            for (var i = 0; i < TickLimit && !done(); i++)
            {
                _game.Flow.StepTick();
                _game.ConsumeEvents();
                events |= _game.LastEvents;
            }

            _game.Render();
            Assert.That(done(), Is.True, "Tick の上限までに条件を満たさなかった");
            return events;
        }

        private void Step(int ticks)
        {
            Time.timeScale = 0;
            for (var i = 0; i < ticks; i++)
            {
                _game.Flow.StepTick();
                _game.ConsumeEvents();
            }

            _game.Render();
        }

        [UnityTest]
        public IEnumerator Scene_HasOneStageMatchingTheArena_AndShowsTheTitle()
        {
            yield return null;
            Assert.That(Object.FindObjectsByType<HaloSiegeStage>(FindObjectsInactive.Include), Has.Length.EqualTo(1));
            Assert.That(Stage.Mismatch(Rules), Is.Null);
            Assert.That(Text("headline"), Is.EqualTo("HALO SIEGE"));
            Assert.That(IsShown("submit") && IsShown("quit"), Is.True);
            Assert.That(Stage.Crosshair.enabled, Is.False);
            var center = new Vector2(Rules.ArenaWidthCells / 2f, Rules.ArenaHeightCells / 2f);
            Assert.That((Vector2)Stage.Ship.transform.position, Is.EqualTo(center));
            var camera = _game.ViewCamera;
            Assert.That((Vector2)camera.transform.position, Is.EqualTo(center), "試合の前はアリーナの中央を映す");
            Assert.That(camera.orthographicSize * 2, Is.GreaterThanOrEqualTo(Rules.ViewHeightCells - .001f));
            Assert.That(camera.orthographicSize * 2 * camera.aspect, Is.GreaterThanOrEqualTo(Rules.ViewWidthCells - .001f), "画面の範囲の幅が収まる");
        }

        [UnityTest]
        public IEnumerator Wasd_MovesTheShip_AndReleaseStops()
        {
            yield return StartGame();
            var start = R.Ship.Position;
            _keyboard.Hold(Key.D, Key.W);
            yield return WaitUntil(() => R.Ship.Position.X > start.X + (2 * Units.Tile));
            Assert.That(R.Ship.Position.Y, Is.GreaterThan(start.Y), "斜めに動く");
            _keyboard.Hold();
            yield return null;
            yield return null;
            var stopped = R.Ship.Position;
            yield return WaitSeconds(.2f);
            Assert.That(R.Ship.Position, Is.EqualTo(stopped));
            Assert.That((Vector2)Stage.Ship.transform.position, Is.EqualTo((Vector2)HaloSiegeStage.Local(stopped)).Using(Vector2EqualityComparer.Instance));
        }

        /// <summary>題材固有（E2E）：右へ動くキーを押しながら、左のマウスの位置へ撃つ。</summary>
        [UnityTest]
        public IEnumerator MouseAim_ShootsTowardThePointer_WhileKeysMoveElsewhere()
        {
            yield return StartGame();
            var start = R.Ship.Position;
            var target = start + new Vec2i(-8 * Units.Tile, 0);
            var screen = ScreenOf(target);
            InputSystem.QueueStateEvent(_mouse, new MouseState { position = screen });
            yield return null;
            _keyboard.Hold(Key.D);
            InputSystem.QueueStateEvent(_mouse, new MouseState { position = screen }.WithButton(MouseButton.Left));
            yield return WaitUntil(() => R.Bullets.Count >= 3);

            Assert.That(R.Ship.Position.X, Is.GreaterThan(start.X), "キーの向き（右）に動く");
            foreach (var bullet in R.Bullets)
            {
                Assert.That(bullet.Velocity.X, Is.LessThan(0), "マウスの向き（左）に飛ぶ");
            }

            Assert.That(Stage.VisibleBullets, Is.EqualTo(R.Bullets.Count));

            _keyboard.Hold();
            InputSystem.QueueStateEvent(_mouse, new MouseState { position = screen });
            yield return null;
            yield return null;

            // 自機が止まってカメラが動かなくなれば、照準カーソルはマウスの位置にちょうど重なる。
            Assert.That(Stage.Crosshair.enabled, Is.True);
            var crosshair = Stage.Crosshair.transform.position;
            var pointer = _game.ViewCamera.ScreenToWorldPoint(new Vector3(screen.x, screen.y, 10));
            Assert.That(Vector2.Distance(crosshair, pointer), Is.LessThan(.01f), "照準カーソルはマウスの位置に描く");
            var count = R.Bullets.Count;
            yield return WaitUntil(() => R.Bullets.Count < count, 3f);
            Assert.That(R.Bullets.Count, Is.LessThan(count), "離すと撃つのをやめる（弾は寿命で消えていく）");
        }

        /// <summary>カメラは画面の範囲（View.Of）の中央に置く。マウスを動かさなくても、カメラが動けば照準点は変わる。</summary>
        [UnityTest]
        public IEnumerator Camera_FollowsTheShip_AndTheAimFollowsTheCamera()
        {
            yield return StartGame();
            var screen = new Vector2(Screen.width * .75f, Screen.height * .5f);
            InputSystem.QueueStateEvent(_mouse, new MouseState { position = screen });
            yield return null;
            yield return null;
            var aim = _game.Flow.Aim;
            var cameraX = _game.ViewCamera.transform.position.x;

            _keyboard.Hold(Key.D);
            yield return WaitUntil(() => R.Ship.Position.X >= Rules.ArenaWidth - Rules.ShipRadius, 10f);
            _keyboard.Hold();
            yield return null;
            yield return null;

            var view = R.View;
            var camera = _game.ViewCamera.transform.position;
            Assert.That(camera.x, Is.GreaterThan(cameraX));
            Assert.That(camera.x, Is.EqualTo((view.MinX + (view.Width / 2f)) / Units.Tile).Within(.001f), "カメラは画面の範囲の中央");
            Assert.That(view.MaxX, Is.EqualTo(Rules.ArenaWidth), "アリーナの端では寄せ止める");
            Assert.That(_game.Flow.Aim.X, Is.GreaterThan(aim.X), "マウスは動かしていないが、照準点はカメラと一緒に動く");
            Assert.That(_game.Flow.Aim, Is.EqualTo(_game.PointerToCore(screen)));
        }

        [UnityTest]
        public IEnumerator ClickingTheHudPauseButton_PausesWithoutShooting()
        {
            yield return StartGame();
            yield return ClickButton("pause");
            Assert.That(_game.Flow.Screen, Is.EqualTo(AppScreen.Paused));
            Assert.That(R.Bullets, Is.Empty, "ボタンの上のクリックでは撃たない");
        }

        [UnityTest]
        public IEnumerator WarningsLanesAndEnemies_AreDrawnFromTheRun()
        {
            yield return StartGame();
            Time.timeScale = 0;
            var ship = R.Ship.Position;
            R.Place(EnemyKind.Chaser, ship + new Vec2i(6 * Units.Tile, 3 * Units.Tile), active: false);
            var lancer = R.Place(EnemyKind.Lancer, ship + new Vec2i(-8 * Units.Tile, 0));
            _game.Render();
            Assert.That(Stage.VisibleWarnings, Is.EqualTo(1));
            Assert.That(Stage.VisibleEnemies, Is.EqualTo(1));
            Assert.That(Stage.VisibleLanes, Is.Zero);

            StepUntil(() => lancer.Mode == LancerMode.Aim);
            Assert.That(Stage.VisibleLanes, Is.EqualTo(1), "狙っている Lancer は突進の筋を出す");
            StepUntil(() => lancer.Mode == LancerMode.Dash);
            Assert.That(Stage.VisibleLanes, Is.Zero);
            Assert.That(Stage.VisibleWarnings + Stage.VisibleEnemies, Is.EqualTo(R.Enemies.Count));
        }

        /// <summary>撃たれた敵は点滅の間だけ色が変わる（Prefab に保存した色で確かめる。レビュー P3-1）。</summary>
        [UnityTest]
        public IEnumerator HitEnemy_IsTintedWhileFlashing()
        {
            yield return StartGame();
            Time.timeScale = 0;
            var lancer = R.Place(EnemyKind.Lancer, R.Ship.Position + new Vec2i(3 * Units.Tile, 0));
            Step(1);
            var body = EnemyRenderer(lancer);
            Assert.That(body.color, Is.EqualTo(Color.white), "撃たれる前は通常の色");

            for (var i = 0; i < 60 && !lancer.IsFlashing; i++)
            {
                R.Step(new ShipCommand(0, 0, true, lancer.Position));
                _game.Render();
            }

            Assert.That(lancer.IsFlashing, Is.True);
            Assert.That(R.Enemies, Has.Member(lancer), "体力 3 の Lancer は 1 発では倒れない");
            Assert.That(EnemyRenderer(lancer).color, Is.Not.EqualTo(Color.white), "撃たれた直後は色が変わる");

            while (lancer.IsFlashing) { R.Step(new ShipCommand(0, 0, false, lancer.Position)); }
            _game.Render();
            Assert.That(EnemyRenderer(lancer).color, Is.EqualTo(Color.white), "点滅が終われば戻る");
        }

        /// <summary>設定で当たりの半径を変えると、描画の大きさも同じだけ変わる（見た目と当たりが食い違わない）。</summary>
        [UnityTest]
        public IEnumerator SpritesAreScaledToTheConfiguredRadius()
        {
            yield return null;
            var config = new SiegeConfig { ShipRadius = 144, BulletRadius = 64 };
            config.Chaser.Radius = 512;
            config.Bouncer.Radius = 52;
            config.Validate();
            var run = new SiegeRun(config, 1);
            var ship = run.Ship.Position;
            var chaser = run.Place(EnemyKind.Chaser, ship + new Vec2i(8 * Units.Tile, 0));
            var bouncer = run.Place(EnemyKind.Bouncer, ship + new Vec2i(-8 * Units.Tile, 0));
            run.Step(new ShipCommand(0, 0, true, ship + new Vec2i(0, Units.Tile)));
            Stage.Render(run, ship, true, config);

            Assert.That(EnemyRenderer(chaser).transform.localScale.x, Is.EqualTo(512f / HaloSiegeStage.ChaserArtRadius).Within(1e-5f));
            Assert.That(EnemyRenderer(bouncer).transform.localScale.x, Is.EqualTo(.5f).Within(1e-5f));
            Assert.That(Stage.Ship.transform.localScale.x, Is.EqualTo(2f).Within(1e-5f));
            Assert.That(Stage.VisibleBullets, Is.EqualTo(1));
            var bulletAt = Stage.transform.TransformPoint(HaloSiegeStage.Local(run.Bullets[0].Position));
            SpriteRenderer bullet = null;
            foreach (var renderer in Stage.GetComponentsInChildren<SpriteRenderer>())
            {
                if (renderer.enabled && renderer != Stage.Ship && renderer != Stage.Crosshair && Vector2.Distance(renderer.transform.position, bulletAt) < .001f) { bullet = renderer; }
            }

            Assert.That(bullet, Is.Not.Null, "弾が描かれていない");
            Assert.That(bullet.transform.localScale.x, Is.EqualTo(64f / HaloSiegeStage.BulletArtRadius).Within(1e-5f));
        }

        /// <summary>敵の位置に描かれている SpriteRenderer。</summary>
        private SpriteRenderer EnemyRenderer(Enemy enemy)
        {
            var at = Stage.transform.TransformPoint(HaloSiegeStage.Local(enemy.Position));
            foreach (var renderer in Stage.GetComponentsInChildren<SpriteRenderer>())
            {
                if (renderer.enabled && renderer.sprite == Stage.EnemySprite(enemy.Kind) && Vector2.Distance(renderer.transform.position, at) < .001f) { return renderer; }
            }

            Assert.Fail("敵が描かれていない");
            return null;
        }

        [UnityTest]
        public IEnumerator Hurt_DimsTheShipWhileInvincible_AndTheHudShowsLives()
        {
            yield return StartGame();
            Time.timeScale = 0;
            R.Place(EnemyKind.Bouncer, R.Ship.Position);
            Step(1);
            yield return null;
            Assert.That(R.Ship.Lives, Is.EqualTo(Rules.Lives - 1));
            Assert.That(Text("lives"), Is.EqualTo("◆◆◇"));
            var dim = 0;
            var bright = 0;
            for (var i = 0; i < 40; i++)
            {
                Step(1);
                if (Stage.Ship.color.a < 1) { dim++; } else { bright++; }
            }

            Assert.That(dim > 0 && bright > 0, Is.True, "無敵の間は点滅する（消えずに半透明）");
            Assert.That(Stage.Ship.enabled, Is.True);
            Step(R.Ship.InvincibleRemaining);
            Assume.That(R.Ship.IsInvincible, Is.False, "無敵が切れた Tick に別の敵に当たった");
            Assert.That(Stage.Ship.color.a, Is.EqualTo(1f), "無敵が切れたら点滅しない");
        }

        [UnityTest]
        public IEnumerator Defeat_ShowsTheResult_ThenBackToTitleRestoresTheStartupView()
        {
            yield return null;
            var initial = (Stage.Ship.transform.position, Stage.Ship.transform.rotation, Stage.Ship.enabled, Stage.Ship.color, _game.ViewCamera.transform.position);
            yield return StartGame();
            _keyboard.Hold(Key.D, Key.S);
            yield return WaitSeconds(.3f);
            _keyboard.Hold();
            Time.timeScale = 0;
            while (!R.IsOver)
            {
                if (!R.Ship.IsInvincible) { R.Place(EnemyKind.Chaser, R.Ship.Position); }
                Step(1);
            }

            Assert.That(_game.Flow.Screen, Is.EqualTo(AppScreen.Result));
            yield return null;
            Assert.That(Text("headline"), Is.EqualTo("撃墜された…"));
            Assert.That(IsShown("restart") && IsShown("title"), Is.True);
            Assert.That(Stage.Crosshair.enabled, Is.False);

            Time.timeScale = 1;
            yield return _keyboard.Tap(Key.T);
            Assert.That(_game.Flow.Screen, Is.EqualTo(AppScreen.Title));
            yield return null;
            var now = (Stage.Ship.transform.position, Stage.Ship.transform.rotation, Stage.Ship.enabled, Stage.Ship.color, _game.ViewCamera.transform.position);
            Assert.That(now, Is.EqualTo(initial), "起動直後と同じ表示");
            Assert.That(Stage.VisibleEnemies + Stage.VisibleWarnings + Stage.VisibleBullets + Stage.VisibleLanes, Is.Zero);
            Assert.That(Text("headline"), Is.EqualTo("HALO SIEGE"));
        }

        [UnityTest]
        public IEnumerator Autopilot_PlaysToTheEnd_AndTheResultMatchesTheOutcome()
        {
            // シーンは実行ごとに別のシードで試合を作るので、勝敗そのものではなく、結果に合った表示かを確かめる。
            yield return null;
            Time.timeScale = 0;
            _game.Flow.Pilot = new Autopilot();
            yield return _keyboard.Tap(Key.Enter);
            StepUntil(() => _game.Flow.Screen == AppScreen.Result);
            yield return null;
            if (R.Status == RunStatus.Won)
            {
                Assert.That(Text("headline"), Is.EqualTo("守り抜いた！"));
                StringAssert.Contains("残機ボーナス", Text("message"));
                Assert.That(Text("time"), Is.EqualTo("残り 0:00"));
            }
            else
            {
                Assert.That(Text("headline"), Is.EqualTo("撃墜された…"));
                Assert.That(Text("lives"), Is.EqualTo("◇◇◇"));
            }

            StringAssert.Contains($"SCORE  {R.Score}", Text("message"));
        }

        [UnityTest]
        public IEnumerator PauseKey_StopsTime()
        {
            yield return StartGame();
            yield return _keyboard.Tap(Key.Escape);
            Assert.That(_game.Flow.Screen, Is.EqualTo(AppScreen.Paused));
            var ticks = R.Elapsed;
            yield return WaitSeconds(.2f);
            Assert.That(R.Elapsed, Is.EqualTo(ticks));
            Assert.That(Text("headline"), Is.EqualTo("一時停止"));
            Assert.That(UnityEngine.Cursor.visible, Is.True, "メニューでは OS のカーソルを出す");
            yield return _keyboard.Tap(Key.P);
            Assert.That(_game.Flow.Screen, Is.EqualTo(AppScreen.Playing));
            Assert.That(_game.HudRoot.Q("panel").style.display.value, Is.EqualTo(DisplayStyle.None));
        }

        [UnityTest]
        public IEnumerator FocusLoss_PausesAndDropsHeldKeysAndButton()
        {
            yield return StartGame();
            var x = R.Ship.Position.X;
            _keyboard.Hold(Key.D);
            InputSystem.QueueStateEvent(_mouse, new MouseState { position = new Vector2(10, 10) }.WithButton(MouseButton.Left));
            yield return WaitUntil(() => R.Ship.Position.X > x && R.Bullets.Count > 0);
            _game.SendMessage("OnApplicationFocus", false);
            Assert.That(_game.Flow.Screen, Is.EqualTo(AppScreen.Paused));
            _keyboard.Hold();
            InputSystem.QueueStateEvent(_mouse, new MouseState { position = new Vector2(10, 10) });
            yield return null;
            _game.SendMessage("OnApplicationFocus", true);
            yield return null;
            yield return null;
            yield return _keyboard.Tap(Key.Enter);
            Assert.That(_game.Flow.Screen, Is.EqualTo(AppScreen.Playing));
            var resumed = R.Ship.Position;
            var lastBullet = 0;
            foreach (var bullet in R.Bullets) { lastBullet = Math.Max(lastBullet, bullet.Id); }
            yield return WaitSeconds(.2f);
            Assert.That(R.Ship.Position, Is.EqualTo(resumed), "フォーカスを失う前のキーは持ち越さない");
            foreach (var bullet in R.Bullets) { Assert.That(bullet.Id, Is.LessThanOrEqualTo(lastBullet), "フォーカスを失う前のボタンも持ち越さない"); }
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
            Assert.That(R.Elapsed, Is.LessThan(30));
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
        public IEnumerator SettingsAsset_MatchesTheCodeDefaults_AndTheCaptureRunHash()
        {
            // 生成スクリプトはアセットを上書きしないので、コードの既定値を変えたらアセットも直す（024 の知見）。
            yield return null;
            Assert.That(JsonUtility.ToJson(Rules), Is.EqualTo(JsonUtility.ToJson(new SiegeConfig())), "設定アセットの試合の設定がコードの既定値と同じ");
            var run = new SiegeRun(Rules, 1);
            var pilot = new Autopilot();
            while (!run.IsOver) { run.Step(pilot.CommandFor(run)); }
            Assert.That(run.Hash(), Is.EqualTo(0xE8B259C2u), "シード 1 は App テストの固定値と同じ");
        }

        /// <summary>
        /// 最大許容設定（敵 64 体・弾 32 発）で、Core の 1 Tick と描画の更新（CPU）を別々に計測する。敵は上限まで置き、
        /// 毎 Tick 撃ち続ける。各サンプルが上限いっぱいの状態を処理していることも確かめる（008 の知見）。
        /// </summary>
        [UnityTest]
        public IEnumerator Performance_AtTheMaximumDensity()
        {
            yield return null;
            var config = new SiegeConfig
            {
                MaxAlive = SiegeConfig.MaxAliveLimit,
                FireIntervalTicks = 1,
                BulletLifeTicks = SiegeConfig.MaxBulletsLimit,
                BulletSpeed = 8,
                InvincibleTicks = SiegeConfig.MaxStatusTicks,
                Lives = SiegeConfig.MaxLives,
                OffscreenLimitTicks = SiegeConfig.MaxStatusTicks,
            };
            config.Chaser.Speed = 1;
            config.Lancer.Speed = 1;
            config.LancerDashSpeed = 1;
            var run = new SiegeRun(config, 1);
            var view = run.View;
            var kinds = new[] { EnemyKind.Chaser, EnemyKind.Bouncer, EnemyKind.Lancer };
            for (var i = 0; run.Enemies.Count < config.MaxAlive; i++)
            {
                var point = new Vec2i(view.MinX + Units.Tile + (i % 16 * 2 * Units.Tile), view.MinY + Units.Tile + (i / 16 * 4 * Units.Tile));
                if (Vec2i.DistanceSquared(point, run.Ship.Position) < (long)config.SafeRadius * config.SafeRadius) { continue; }
                run.Place(kinds[i % 3], point, new Vec2i(1, 1));
            }

            var aim = run.Ship.Position + new Vec2i(0, 4 * Units.Tile);
            var command = new ShipCommand(0, 0, true, aim);
            for (var i = 0; i < config.BulletLifeTicks; i++) { run.Step(command); }

            const int Samples = 600;
            var step = new double[Samples];
            var render = new double[Samples];
            var watch = new System.Diagnostics.Stopwatch();
            for (var i = 0; i < Samples; i++)
            {
                watch.Restart();
                run.Step(command);
                step[i] = watch.Elapsed.TotalMilliseconds;
                Assert.That(run.Enemies.Count, Is.EqualTo(config.MaxAlive), "上限いっぱいの状態を計測する");
                Assert.That(run.Bullets.Count, Is.EqualTo(config.BulletCapacity));
                watch.Restart();
                Stage.Render(run, aim, true, config);
                render[i] = watch.Elapsed.TotalMilliseconds;
            }

            Assert.That(Stage.VisibleEnemies, Is.EqualTo(config.MaxAlive));
            Assert.That(Stage.VisibleBullets, Is.EqualTo(config.BulletCapacity));
            Array.Sort(step);
            Array.Sort(render);
            var report = $"敵 {config.MaxAlive} 体・弾 {config.BulletCapacity} 発・{Samples} Tick：Core の Step p50 {step[Samples / 2]:F3} ms / p95 {step[Samples * 95 / 100]:F3} ms / 最大 {step[Samples - 1]:F3} ms、" +
                $"描画の更新 p50 {render[Samples / 2]:F3} ms / p95 {render[Samples * 95 / 100]:F3} ms / 最大 {render[Samples - 1]:F3} ms、描画物 {Stage.PooledRenderers} 個";
            Debug.Log("[Performance] " + report);
            TestContext.WriteLine(report);
            Assert.That(step[Samples * 95 / 100], Is.LessThan(4), "Core の Step は 1 Tick（16.7 ms）に十分収まる");
            Assert.That(render[Samples * 95 / 100], Is.LessThan(4));
        }

        [Test]
        public void Settings_RejectInvalidValues()
        {
            AssertRejected(s => s.Volume = float.NaN);
            AssertRejected(s => s.Volume = 1.5f);
            AssertRejected(s => s.Rules.ShipSpeed = 0);
            AssertRejected(s => s.Rules.MaxAlive = SiegeConfig.MaxAliveLimit + 1);
            AssertRejected(s => s.Rules = null, typeof(ArgumentNullException));
        }

        [Test]
        public void FormatTicks_ShowsMinutesAndSeconds()
        {
            Assert.That(HaloSiegeGame.FormatTicks(0), Is.EqualTo("0:00"));
            Assert.That(HaloSiegeGame.FormatTicks(59), Is.EqualTo("0:00"));
            Assert.That(HaloSiegeGame.FormatTicks(18000), Is.EqualTo("5:00"));
        }

        private static void AssertRejected(Action<HaloSiegeSettings> change, Type exception = null)
        {
            var settings = ScriptableObject.CreateInstance<HaloSiegeSettings>();
            Assert.DoesNotThrow(settings.Validate);
            change(settings);
            Assert.Throws(exception ?? typeof(ArgumentOutOfRangeException), settings.Validate);
            Object.DestroyImmediate(settings);
        }

        private void SetMouse(Vector2 position, MouseButton? pressed = null)
        {
            var state = new MouseState { position = position };
            if (pressed.HasValue) { state = state.WithButton(pressed.Value); }
            InputSystem.QueueStateEvent(_mouse, state);
        }

        // 実際の座標でボタンに当たることを確かめ、マウスで押して離す。バッチ実行では UI イベントが配送されないので、
        // マウスのクリックが届かなかったときだけボタンへ直接送る（GUI のエディタでは届くので、二重に押さない。022 の知見）。
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
