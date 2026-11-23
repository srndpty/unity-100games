using System;
using System.Collections;
using Game.App;
using Game.Core;
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
    /// <summary>実シーンを読み込み、仮想のキーボード（と HUD のボタンはマウス）で操作する。</summary>
    public sealed class LanternVeilSceneTests
    {
        private const int TickLimit = 60 * 60 * 10;

        private TestKeyboard _keyboard;
        private Mouse _mouse;
        private LanternVeilGame _game;

        [UnitySetUp]
        public IEnumerator Load()
        {
            _keyboard = new TestKeyboard();
            _mouse = InputSystem.AddDevice<Mouse>();
            _mouse.MakeCurrent();
            yield return SceneManager.LoadSceneAsync("LanternVeil");
            yield return null;
            _game = Object.FindAnyObjectByType<LanternVeilGame>();
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

        private VeilRun R => _game.Flow.Run;

        private VeilConfig Rules => _game.Settings.Rules;

        private LanternVeilStage Stage => _game.Stage;

        private string Text(string name) => _game.HudRoot.Q<Label>(name).text;

        private bool IsShown(string name) => _game.HudRoot.Q<Button>(name).style.display.value == DisplayStyle.Flex;

        private IEnumerator StartGame()
        {
            yield return _keyboard.Tap(Key.Enter);
            Assert.That(_game.Flow.Screen, Is.EqualTo(AppScreen.Playing));
            yield return WaitUntil(() => R.Elapsed > 0);
        }

        // バッチ実行はフレームが極端に短いので、フレーム数ではなく実時間の上限で待つ（011 の知見）。
        private static IEnumerator WaitUntil(Func<bool> condition, float seconds = 8f)
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

        /// <summary>起動直後と比べる表示：自機・当たり判定の点・ボス・体力のバー・ボムの輪・弾・ショット。</summary>
        private object StartupView() => (
            Stage.Player.transform.position, Stage.Player.enabled, Stage.Player.color,
            Stage.Hitbox.enabled, Stage.Boss.transform.position, Stage.Boss.sprite, Stage.Boss.enabled, Stage.Boss.color,
            Stage.HpBar.enabled, Stage.BombRing.enabled, Stage.Bullets.Visible, Stage.VisibleShots, _game.ViewCamera.transform.position);

        [UnityTest]
        public IEnumerator Scene_HasOneStage_AndShowsTheTitleWithTheFieldInView()
        {
            yield return null;
            Assert.That(Object.FindObjectsByType<LanternVeilStage>(FindObjectsInactive.Include), Has.Length.EqualTo(1));
            Assert.That(Stage.Mismatch(Rules), Is.Null);
            Assert.That(Text("headline"), Is.EqualTo("LANTERN VEIL"));
            StringAssert.Contains("Shift", Text("message"));
            Assert.That(IsShown("submit") && IsShown("quit"), Is.True);
            Assert.That(IsShown("retry"), Is.False);
            Assert.That(_game.HudRoot.Q("side").style.display.value, Is.EqualTo(DisplayStyle.None));
            Assert.That((Vector2)Stage.Player.transform.localPosition, Is.EqualTo((Vector2)LanternVeilStage.Local(Rules.PlayerStart)));
            Assert.That(Stage.Bullets.Capacity, Is.EqualTo(Rules.MaxBullets));

            // プレイ領域の四隅が画面の内側に映る（領域は左に寄せ、右側は HUD）。
            var camera = _game.ViewCamera;
            var field = LanternVeilStage.FieldSize;
            foreach (var corner in new[] { Vector2.zero, field, new Vector2(field.x, 0), new Vector2(0, field.y) })
            {
                var viewport = camera.WorldToViewportPoint(Stage.transform.TransformPoint(corner));
                Assert.That(viewport.x, Is.InRange(0f, 1f), $"{corner} の横");
                Assert.That(viewport.y, Is.InRange(0f, 1f), $"{corner} の縦");
            }
        }

        [UnityTest]
        public IEnumerator ArrowKeysAndWasd_MoveThePlayer_AndReleaseStops()
        {
            yield return StartGame();
            var start = R.Player;
            _keyboard.Hold(Key.LeftArrow, Key.W);
            yield return WaitUntil(() => R.Player.X < start.X - (20 * Field.Unit) && R.Player.Y > start.Y + (20 * Field.Unit));
            _keyboard.Hold();
            yield return null;
            yield return null;
            var stopped = R.Player;
            yield return WaitSeconds(.2f);
            Assert.That(R.Player, Is.EqualTo(stopped));
        }

        [UnityTest]
        public IEnumerator Shift_SlowsAndShowsTheHitbox()
        {
            yield return StartGame();
            _keyboard.Hold(Key.LeftShift, Key.D);
            yield return WaitUntil(() => R.Slow && R.Player.X > R.PlayerStart.X);
            var before = R.Player.X;
            var ticks = R.Elapsed;
            yield return WaitUntil(() => R.Elapsed >= ticks + 10);
            Assert.That(R.Player.X - before, Is.EqualTo((R.Elapsed - ticks) * Rules.PlayerSlowSpeed), "低速の速さで動く");
            yield return null;
            Assert.That(Stage.Hitbox.enabled, Is.True);
            Assert.That((Vector2)Stage.Hitbox.transform.localPosition, Is.EqualTo((Vector2)LanternVeilStage.Local(R.Player)));

            _keyboard.Hold(Key.D);
            yield return WaitUntil(() => !R.Slow);
            yield return null;
            Assert.That(Stage.Hitbox.enabled, Is.False);
        }

        [UnityTest]
        public IEnumerator ZKey_ShootsAndDamagesTheBoss_AndTheBarShrinks()
        {
            yield return StartGame();
            _keyboard.Hold(Key.Z);
            yield return WaitUntil(() => R.Phase == RunPhase.Pattern && R.BossHp < R.CurrentPattern.Hp - 10);
            _keyboard.Hold();
            yield return null;
            Assert.That(Stage.VisibleShots, Is.GreaterThan(0));
            var ratio = (float)R.BossHp / R.CurrentPattern.Hp;
            Assert.That(Stage.HpBar.transform.localScale.x, Is.EqualTo(LanternVeilStage.FieldSize.x * ratio).Within(1e-4));
        }

        [UnityTest]
        public IEnumerator XKey_Bombs_ClearsBulletsShowsTheRingAndUpdatesTheHud()
        {
            yield return StartGame();
            yield return WaitUntil(() => R.Bullets.Count > 30, 12f);
            yield return _keyboard.Tap(Key.X);
            yield return WaitUntil(() => R.BombRemaining > 0);
            yield return null;
            Assert.That(R.Bullets.Count, Is.EqualTo(0));
            Assert.That(Stage.Bullets.Visible, Is.EqualTo(0));
            Assert.That(Stage.BombRing.enabled, Is.True);
            Assert.That(Text("bombs"), Is.EqualTo("ボム  ★★☆"));
        }

        [UnityTest]
        public IEnumerator Bullets_AreDrawnAtTheirPositions()
        {
            yield return StartGame();
            StepUntil(() => R.Bullets.Count > 50);
            Assert.That(Stage.Bullets.Visible, Is.EqualTo(R.Bullets.Count));
            var vertices = Stage.Bullets.GetComponent<MeshFilter>().sharedMesh.vertices;
            Assert.That(vertices.Length, Is.EqualTo(Rules.MaxBullets * 4));
            for (var i = 0; i < R.Bullets.Count; i += 7)
            {
                var center = (vertices[i * 4] + vertices[(i * 4) + 2]) / 2;
                Assert.That(Vector3.Distance(center, LanternVeilStage.Local(R.Bullets.PositionOf(i))), Is.LessThan(1e-4f), $"#{i}");
            }

            var unused = vertices[R.Bullets.Count * 4];
            Assert.That(unused, Is.EqualTo(Vector3.zero), "使わない四角形は大きさ 0");
        }

        [UnityTest]
        public IEnumerator Hurt_BlinksWhileInvincible_AndTheHudShowsLives()
        {
            yield return StartGame();
            _game.Flow.Pilot = new StillPilot();
            StepUntil(() => R.Hits > 0);
            yield return null;
            Assert.That(Text("lives"), Is.EqualTo("残機  ◆◆◇"));
            var dim = 0;
            var bright = 0;
            for (var i = 0; i < 40; i++)
            {
                Step(1);
                if (Stage.Player.color.a < 1) { dim++; } else { bright++; }
            }

            Assert.That(dim > 0 && bright > 0, Is.True, "無敵の間は点滅する（消えずに半透明）");
            Step(R.InvincibleRemaining);
            Assume.That(R.IsInvincible, Is.False, "無敵が切れた Tick にまた被弾した");
            Assert.That(Stage.Player.color.a, Is.EqualTo(1f));
        }

        [UnityTest]
        public IEnumerator Defeat_RetryKeyStartsTheSameBoss_ThenTitleRestoresTheStartupView()
        {
            yield return null;
            var initial = StartupView();
            yield return StartGame();
            _game.Flow.Pilot = new StillPilot();
            StepUntil(() => _game.Flow.Screen == AppScreen.Result);
            var lost = R;
            yield return null;
            Assert.That(lost.Status, Is.EqualTo(RunStatus.Lost));
            Assert.That(Text("headline"), Is.EqualTo("灯が消えた…"));
            Assert.That(IsShown("retry") && IsShown("title"), Is.True);
            StringAssert.Contains($"ボス {lost.BossIndex + 1} から再挑戦", Text("message"));
            Assert.That(Stage.Player.enabled, Is.False);

            _game.Flow.Pilot = null;
            Time.timeScale = 1;
            yield return _keyboard.Tap(Key.C);
            Assert.That(_game.Flow.Screen, Is.EqualTo(AppScreen.Playing));
            Assert.That(R, Is.Not.SameAs(lost));
            Assert.That(R.BossIndex, Is.EqualTo(lost.BossIndex));
            Assert.That(R.Lives, Is.EqualTo(Rules.Lives));
            Assert.That(R.Score, Is.GreaterThanOrEqualTo(lost.BossStartScore));

            yield return _keyboard.Tap(Key.Escape);
            yield return _keyboard.Tap(Key.T);
            Assert.That(_game.Flow.Screen, Is.EqualTo(AppScreen.Title));
            yield return null;
            Assert.That(StartupView(), Is.EqualTo(initial), "起動直後と同じ表示（021 の知見）");
            Assert.That(Text("headline"), Is.EqualTo("LANTERN VEIL"));
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
                Assert.That(Text("headline"), Is.EqualTo("帳は晴れた！"));
                StringAssert.Contains("残機ボーナス", Text("message"));
                Assert.That(IsShown("retry"), Is.False, "勝った結果では再挑戦を出さない");
                Assert.That(Stage.Boss.enabled, Is.False);
            }
            else
            {
                Assert.That(Text("headline"), Is.EqualTo("灯が消えた…"));
                Assert.That(IsShown("retry"), Is.True);
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
            yield return _keyboard.Tap(Key.P);
            Assert.That(_game.Flow.Screen, Is.EqualTo(AppScreen.Playing));
            Assert.That(_game.HudRoot.Q("panel").style.display.value, Is.EqualTo(DisplayStyle.None));
        }

        [UnityTest]
        public IEnumerator FocusLoss_PausesAndDropsHeldKeys()
        {
            yield return StartGame();
            var x = R.Player.X;
            _keyboard.Hold(Key.RightArrow, Key.Z);
            yield return WaitUntil(() => R.Player.X > x && R.Shots.Count > 0);
            _game.SendMessage("OnApplicationFocus", false);
            Assert.That(_game.Flow.Screen, Is.EqualTo(AppScreen.Paused));
            _keyboard.Hold();
            yield return null;
            _game.SendMessage("OnApplicationFocus", true);
            yield return null;
            yield return null;
            yield return _keyboard.Tap(Key.Enter);
            Assert.That(_game.Flow.Screen, Is.EqualTo(AppScreen.Playing));
            var resumed = R.Player;

            // ショットは約 37 Tick（0.6 秒）で画面の上へ出る。
            yield return WaitSeconds(.8f);
            Assert.That(R.Player, Is.EqualTo(resumed), "フォーカスを失う前のキーは持ち越さない");
            Assert.That(R.Shots.Count, Is.EqualTo(0), "撃ち続けない（前のショットは画面の外へ出た）");
        }

        [UnityTest]
        public IEnumerator HudButtons_StartPauseRestartRetryAndBackToTitle()
        {
            yield return ClickButton("submit");
            Assert.That(_game.Flow.Screen, Is.EqualTo(AppScreen.Playing));
            yield return ClickButton("pause");
            Assert.That(_game.Flow.Screen, Is.EqualTo(AppScreen.Paused));
            yield return ClickButton("restart");
            Assert.That(_game.Flow.Screen, Is.EqualTo(AppScreen.Playing));
            Assert.That(R.Elapsed, Is.LessThan(30));

            _game.Flow.Pilot = new StillPilot();
            StepUntil(() => _game.Flow.Screen == AppScreen.Result);
            _game.Flow.Pilot = null;
            Time.timeScale = 1;
            var lost = R;
            yield return ClickButton("retry");
            Assert.That(_game.Flow.Screen, Is.EqualTo(AppScreen.Playing));
            Assert.That(R, Is.Not.SameAs(lost));
            Assert.That(R.BossIndex, Is.EqualTo(lost.BossIndex));

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
        public IEnumerator SettingsAsset_MatchesTheCodeDefaults_AndTheAutopilotHash()
        {
            // 生成スクリプトはアセットを上書きしないので、コードの既定値を変えたらアセットも直す（024 の知見）。
            yield return null;
            Assert.That(JsonUtility.ToJson(Rules), Is.EqualTo(JsonUtility.ToJson(new VeilConfig())), "設定アセットの試合の設定がコードの既定値と同じ");
            Assert.That(_game.Settings.Boss1.name, Is.EqualTo("boss1"));
            Assert.That(_game.Settings.Boss2.name, Is.EqualTo("boss2"));
            var run = new VeilRun(Rules, _game.Settings.Validate(), 1);
            var pilot = new Autopilot();
            while (!run.IsOver) { run.Step(pilot.CommandFor(run)); }
            Assert.That(run.Hash(), Is.EqualTo(0x3364D004u), "シード 1 は App テストの固定値と同じ（設定アセットの台本が本番のファイル）");
        }

        /// <summary>
        /// 弾数の上限いっぱい（既定の容量 2048 発）で、Core の 1 Tick と描画の更新（CPU）を別々に計測する。ボスの周りに遅い弾を撃ち続けて
        /// 置き場を満杯に保ち、各サンプルが容量いっぱいの状態を処理していることも確かめる（008 の知見）。割り当てもないことを確かめる。
        /// </summary>
        [UnityTest]
        public IEnumerator Performance_AtFullBulletCapacity()
        {
            yield return null;
            var config = new VeilConfig { IntervalTicks = 1 };
            const string Pattern = "pattern \"F\" hp=100000 time=36000\n  emit ring every=1 count=256 speed=0.05 spin=3 shape=rice color=cyan\n";
            var script = ScriptParser.ParseOrThrow("boss \"F\" radius=24\n" + Pattern + Pattern + Pattern, "full");
            var run = new VeilRun(config, new[] { script, script }, 1);
            var command = new PlayerCommand(0, 0, false, true, false);
            while (run.Bullets.Count < config.MaxBullets) { run.Step(command); }

            const int Samples = 600;
            var step = new double[Samples];
            var render = new double[Samples];
            var watch = new System.Diagnostics.Stopwatch();
            // ショットの描画物が増えきる（最初の数十 Tick は見本から増やす）まで進めてから測る。
            for (var i = 0; i < 120; i++)
            {
                run.Step(command);
                Stage.Render(run, config);
            }

            // Mono では GC.GetAllocatedBytesForCurrentThread が常に 0 を返すので、Unity のフレームごとの割り当ての計数で測る。
            // ゲームの Update（HUD の文字列を作る）を止め、何もしないフレーム・計測のフレーム・4096 バイトを割り当てるフレームを比べる。
            _game.enabled = false;
            using var recorder = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame");
            yield return null;
            yield return null;
            yield return null;
            var baseline = recorder.LastValue;
            for (var i = 0; i < Samples; i++)
            {
                watch.Restart();
                run.Step(command);
                step[i] = watch.Elapsed.TotalMilliseconds;
                watch.Restart();
                Stage.Render(run, config);
                render[i] = watch.Elapsed.TotalMilliseconds;
                if (run.Bullets.Count != config.MaxBullets) { Assert.Fail($"容量いっぱいの状態を計測する（{run.Bullets.Count}）"); }
            }

            yield return null;
            var measured = recorder.LastValue;
            GC.KeepAlive(new byte[4096]);
            yield return null;
            var probe = recorder.LastValue;
            _game.enabled = true;
            var counterWorks = recorder.Valid && probe >= baseline + 4096;
            var allocated = measured - baseline;

            Assert.That(Stage.Bullets.Visible, Is.EqualTo(config.MaxBullets));
            Assert.That(run.Bullets.Dropped, Is.GreaterThan(0), "満杯の間の発射は捨てた");
            Assert.That(run.Hits, Is.Zero, "自機は弾に触れていない（被弾の全消去で計測が崩れない）");
            Array.Sort(step);
            Array.Sort(render);
            var report = $"弾 {config.MaxBullets} 発（容量いっぱい）・{Samples} Tick：Core の Step p50 {step[Samples / 2]:F3} ms / p95 {step[Samples * 95 / 100]:F3} ms / 最大 {step[Samples - 1]:F3} ms、" +
                $"描画の更新（Mesh の頂点の書き込み）p50 {render[Samples / 2]:F3} ms / p95 {render[Samples * 95 / 100]:F3} ms / 最大 {render[Samples - 1]:F3} ms、" +
                $"割り当て {allocated} バイト（何もしないフレーム {baseline}・計測のフレーム {measured}・4096 バイトの確認 {probe}）";
            Debug.Log("[Performance] " + report);
            TestContext.WriteLine(report);
            Assert.That(counterWorks, Is.True, "割り当ての計数が働いている");
            Assert.That(allocated, Is.LessThanOrEqualTo(0), "Step と描画の更新で割り当てをしない（何もしないフレームより増えない）");
            Assert.That(step[Samples * 95 / 100], Is.LessThan(4), "Core の Step は 1 Tick（16.7 ms）に十分収まる");
            Assert.That(render[Samples * 95 / 100], Is.LessThan(4));
        }

        [Test]
        public void Settings_RejectInvalidValuesAndBrokenScripts()
        {
            AssertRejected(s => s.Volume = float.NaN, typeof(ArgumentOutOfRangeException));
            AssertRejected(s => s.Rules.PlayerSpeed = 0, typeof(ArgumentOutOfRangeException));
            AssertRejected(s => s.Rules = null, typeof(ArgumentNullException));
            AssertRejected(s => s.Boss2 = null, typeof(InvalidOperationException));
            AssertRejected(s => s.Boss1 = new TextAsset("boss \"B\" radius=8\npattern \"P\" hp=1 time=60\nemit ring every=1"), typeof(FormatException));
            AssertRejected(s => s.Boss1 = s.Boss2 = new TextAsset("boss \"B\" radius=8\npattern \"P\" hp=1 time=60\nemit ring every=1 speed=1"), typeof(ArgumentException));
        }

        [Test]
        public void FormatTicks_ShowsMinutesAndSeconds()
        {
            Assert.That(LanternVeilGame.FormatTicks(0), Is.EqualTo("0:00"));
            Assert.That(LanternVeilGame.FormatTicks(59), Is.EqualTo("0:00"));
            Assert.That(LanternVeilGame.FormatTicks(7378), Is.EqualTo("2:02"));
        }

        private void AssertRejected(Action<LanternVeilSettings> change, Type exception)
        {
            var settings = Object.Instantiate(_game != null ? _game.Settings : Resources.FindObjectsOfTypeAll<LanternVeilSettings>()[0]);
            Assert.DoesNotThrow(() => settings.Validate());
            change(settings);
            Assert.Throws(exception, () => settings.Validate());
            Object.DestroyImmediate(settings);
        }

        /// <summary>動かずに低速で撃ち続ける操縦役（被弾・敗北の場面を作る）。</summary>
        private sealed class StillPilot : IPilot
        {
            public PlayerCommand CommandFor(VeilRun run) => new(0, 0, slow: true, shoot: true, bomb: false);
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
