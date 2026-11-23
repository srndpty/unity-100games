using System;
using System.Collections;
using Game.App;
using Game.Core;
using IntGeometry;
using NUnit.Framework;
using TileMotion;
using Unity.Profiling;
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
    public sealed class ThornTideSceneTests
    {
        private const int TickLimit = 60 * 60 * 10;

        private TestKeyboard _keyboard;
        private Mouse _mouse;
        private ThornTideGame _game;

        [UnitySetUp]
        public IEnumerator Load()
        {
            _keyboard = new TestKeyboard();
            _mouse = InputSystem.AddDevice<Mouse>();
            _mouse.MakeCurrent();
            yield return SceneManager.LoadSceneAsync("ThornTide");
            yield return null;
            _game = Object.FindAnyObjectByType<ThornTideGame>();
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

        private TideRun R => _game.Flow.Run;

        private TideConfig Rules => _game.Rules;

        private ThornTideStage Stage => _game.Stage;

        private string Text(string name) => _game.HudRoot.Q<Label>(name).text;

        private bool IsShown(string name) => _game.HudRoot.Q(name).style.display.value != DisplayStyle.None;

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

        /// <summary>自機の上に次のレベルの分の宝石を置き、時計で 3 択が出るまで待つ。</summary>
        private IEnumerator RaiseLevel()
        {
            R.PlaceGem(R.Player, (int)(R.ExperienceToNext - R.Experience));
            yield return WaitUntil(() => _game.Flow.Screen == AppScreen.LevelUp);
            yield return null;
        }

        /// <summary>3 択が出てから受け付けの猶予（<see cref="TideFlow.ChoiceGuardSeconds"/>）が過ぎるまで待つ。</summary>
        private IEnumerator PassChoiceGuard()
        {
            var until = _game.Flow.ChoiceShownAt + TideFlow.ChoiceGuardSeconds + .05;
            yield return WaitUntil(() => ThornTideGame.Now >= until);
        }

        /// <summary>起動直後と比べる表示：自機・ボス・群れ・刃・効果・カメラ。</summary>
        private object StartupView() => (
            Stage.Player.transform.position, Stage.Player.enabled, Stage.Player.color, Stage.Player.flipX,
            Stage.Boss.enabled, Stage.Swarm.Visible, Stage.VisibleBlades, Stage.VisibleLashes, Stage.Pulse.enabled, Stage.ArcLine.enabled,
            Stage.Floor.transform.position, _game.ViewCamera.transform.position, EnabledRenderers());

        /// <summary>Stage の下で表示されている描画物の数（数え方の食い違いではなく、実際に見えているもの）。</summary>
        private int EnabledRenderers()
        {
            var count = 0;
            foreach (var renderer in Stage.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer.enabled && renderer.gameObject.activeInHierarchy && renderer is not MeshRenderer) { count++; }
            }

            return count;
        }

        [UnityTest]
        public IEnumerator Scene_HasOneStage_AndShowsTheTitleWithTheViewFramed()
        {
            yield return null;
            Assert.That(Object.FindObjectsByType<ThornTideStage>(FindObjectsInactive.Include), Has.Length.EqualTo(1));
            Assert.That(Stage.Mismatch(), Is.Null);
            Assert.That(Text("headline"), Is.EqualTo("THORN TIDE"));
            StringAssert.Contains("W A S D", Text("message"));
            Assert.That(IsShown("submit") && IsShown("quit"), Is.True);
            Assert.That(IsShown("stats") || IsShown("levelup"), Is.False);
            Assert.That(Stage.Swarm.Capacity, Is.EqualTo(Rules.MaxEnemies + Rules.MaxGems + Rules.MaxProjectiles));
            var camera = _game.ViewCamera;
            Assert.That(camera.orthographicSize * 2, Is.GreaterThanOrEqualTo(Rules.ViewHeightCells - .001f));
            Assert.That(camera.orthographicSize * 2 * camera.aspect, Is.GreaterThanOrEqualTo(Rules.ViewWidthCells - .001f), "視野の幅が収まる");
        }

        [UnityTest]
        public IEnumerator Wasd_MovesThePlayer_TheCameraFollows_AndReleaseStops()
        {
            yield return StartGame();
            var start = R.Player;
            _keyboard.Hold(Key.D, Key.W);
            yield return WaitUntil(() => R.Player.X > start.X + (2 * Units.Tile));
            Assert.That(R.Player.Y, Is.GreaterThan(start.Y), "斜めに動く");
            _keyboard.Hold();
            yield return null;
            yield return null;
            var stopped = R.Player;
            yield return WaitSeconds(.2f);
            Assert.That(R.Player, Is.EqualTo(stopped));
            var local = (Vector2)ThornTideStage.Local(stopped);
            Assert.That((Vector2)Stage.Player.transform.position, Is.EqualTo(local).Using(Vector2EqualityComparer.Instance));
            Assert.That((Vector2)_game.ViewCamera.transform.position, Is.EqualTo(local).Using(Vector2EqualityComparer.Instance), "カメラは自機（視野の中央）を映す");
            Assert.That(Stage.Player.flipX, Is.False, "右へ動いた後は右向き");
        }

        /// <summary>題材の操作：数字キーで選ぶ。3 択の間は時間が止まり、選ぶと武器が表の値のレベルになる。</summary>
        [UnityTest]
        public IEnumerator LevelUp_StopsTime_AndANumberKeyChooses()
        {
            yield return StartGame();
            yield return RaiseLevel();
            Assert.That(IsShown("levelup"), Is.True);
            Assert.That(IsShown("panel"), Is.False);
            var elapsed = R.Elapsed;
            var choice = R.ChoiceAt(1);
            StringAssert.StartsWith("2.", _game.HudRoot.Q<Button>("card1").text);
            if (choice.Kind != ChoiceKind.Heal) { StringAssert.Contains(_game.Settings.Weapons[choice.Weapon].DisplayName, _game.HudRoot.Q<Button>("card1").text); }

            // 出たばかりの 3 択は選ばない（押しっぱなし・連打の誤選択を防ぐ）。
            yield return _keyboard.Tap(Key.Digit2);
            Assert.That(_game.Flow.Screen, Is.EqualTo(AppScreen.LevelUp));
            yield return PassChoiceGuard();
            Assert.That(R.Elapsed, Is.EqualTo(elapsed), "3 択の間は進まない");

            yield return _keyboard.Tap(Key.Digit2);
            Assert.That(_game.Flow.Screen, Is.EqualTo(AppScreen.Playing));
            if (choice.Kind != ChoiceKind.Heal) { Assert.That(R.WeaponLevelOf(choice.Weapon), Is.EqualTo(choice.Level)); }
            yield return null;
            Assert.That(IsShown("levelup"), Is.False);
            yield return WaitUntil(() => R.Elapsed > elapsed, 2f);
        }

        [UnityTest]
        public IEnumerator LevelUp_ArrowKeysMoveTheHighlight_AndEnterChooses()
        {
            yield return StartGame();
            yield return RaiseLevel();
            yield return PassChoiceGuard();
            Assert.That(_game.HudRoot.Q<Button>("card0").ClassListContains("selected"), Is.True);
            yield return _keyboard.Tap(Key.RightArrow);
            yield return null;
            Assert.That(_game.Flow.Highlight, Is.EqualTo(1));
            Assert.That(_game.HudRoot.Q<Button>("card1").ClassListContains("selected"), Is.True);
            Assert.That(_game.HudRoot.Q<Button>("card0").ClassListContains("selected"), Is.False);
            var choice = R.ChoiceAt(1);
            yield return _keyboard.Tap(Key.Enter);
            Assert.That(_game.Flow.Screen, Is.EqualTo(AppScreen.Playing));
            if (choice.Kind != ChoiceKind.Heal) { Assert.That(R.WeaponLevelOf(choice.Weapon), Is.EqualTo(choice.Level)); }
        }

        [UnityTest]
        public IEnumerator LevelUp_ClickingACardChoosesIt()
        {
            yield return StartGame();
            yield return RaiseLevel();
            yield return PassChoiceGuard();
            var choice = R.ChoiceAt(2);
            yield return ClickButton("card2");
            Assert.That(_game.Flow.Screen, Is.EqualTo(AppScreen.Playing));
            if (choice.Kind != ChoiceKind.Heal) { Assert.That(R.WeaponLevelOf(choice.Weapon), Is.EqualTo(choice.Level)); }
        }

        /// <summary>描画が Core の状態と一致する：群れの四角形の数・刃の数と位置・効果・被弾の点滅。</summary>
        [UnityTest]
        public IEnumerator Rendering_MatchesTheRunState()
        {
            yield return StartGame();
            _game.Flow.Pilot = new Autopilot();
            StepUntil(() => R.Elapsed > 90 * Units.TickRate && R.WeaponLevelOf(1) > 0 && R.Projectiles.Count > 0 && R.Gems.Count > 0);
            var boss = R.BossIndex();
            var expected = R.Gems.Count + R.Enemies.Count - (boss >= 0 ? 1 : 0) + R.Projectiles.Count;
            Assert.That(Stage.Swarm.Visible, Is.EqualTo(expected), "宝石・敵・弾を 1 つの Mesh に描く");
            Assert.That(Stage.VisibleBlades, Is.EqualTo(R.WeaponCountOf(1)));
            Assert.That(Stage.PooledBlades, Is.LessThanOrEqualTo(TideConfig.MaxCount));
            Assert.That(Stage.ArcLine.enabled, Is.EqualTo(R.Effects.ArcTick >= 0 && R.Elapsed - R.Effects.ArcTick < ThornTideStage.EffectTicks));

            // 被弾した直後は無敵で、自機を半透明に点滅させる。
            _game.Flow.Pilot = null;
            R.Place(0, R.Player + new Vec2i(Units.Tile / 4, 0));
            StepUntil(() => R.IsInvincible);
            var colors = new bool[2];
            for (var i = 0; i < 2 * ThornTideStage.BlinkTicks && R.IsInvincible; i++)
            {
                _game.Flow.StepTick();
                _game.Render();
                colors[Stage.Player.color.a < 1 ? 0 : 1] = true;
            }

            Assert.That(colors[0], Is.True, "無敵の間は半透明になる Tick がある");
            Assert.That(Text("time"), Does.StartWith("ボスまで"));
        }

        /// <summary>試合の後にタイトルへ戻った表示は、起動直後と一致する（021 の知見）。結果の表示は勝敗に合う。</summary>
        [UnityTest]
        public IEnumerator AfterARun_BackToTitle_LooksLikeStartup()
        {
            yield return null;
            var initial = StartupView();
            yield return StartGame();
            _game.Flow.Pilot = new Autopilot();
            StepUntil(() => _game.Flow.Screen == AppScreen.Result);
            var run = R;
            yield return null;
            var headline = run.Status == RunStatus.Won ? "茨の番人を倒した！" : run.End == RunEnd.TimeUp ? "時間切れ…" : "力尽きた…";
            Assert.That(Text("headline"), Is.EqualTo(headline));
            StringAssert.Contains($"撃破  {run.Kills}", Text("message"));
            Assert.That(IsShown("restart") && IsShown("title"), Is.True);

            _game.Flow.Pilot = null;
            Time.timeScale = 1;
            yield return _keyboard.Tap(Key.T);
            Assert.That(_game.Flow.Screen, Is.EqualTo(AppScreen.Title));
            yield return null;
            Assert.That(StartupView(), Is.EqualTo(initial), "起動直後と同じ表示");
            Assert.That(Text("headline"), Is.EqualTo("THORN TIDE"));
        }

        [UnityTest]
        public IEnumerator Pause_StopsTheRun_AndResumes()
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
            yield return null;
            Assert.That(IsShown("panel"), Is.False);
        }

        [UnityTest]
        public IEnumerator FocusLoss_PausesAndDropsHeldKeys()
        {
            yield return StartGame();
            var x = R.Player.X;
            _keyboard.Hold(Key.RightArrow);
            yield return WaitUntil(() => R.Player.X > x);
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
            yield return WaitSeconds(.3f);
            Assert.That(R.Player, Is.EqualTo(resumed), "フォーカスを失う前のキーは持ち越さない");
        }

        [UnityTest]
        public IEnumerator HudButtons_StartPauseRestartAndBackToTitle()
        {
            yield return ClickButton("submit");
            Assert.That(_game.Flow.Screen, Is.EqualTo(AppScreen.Playing));
            var first = R;
            yield return ClickButton("pause");
            Assert.That(_game.Flow.Screen, Is.EqualTo(AppScreen.Paused));
            yield return ClickButton("restart");
            Assert.That(_game.Flow.Screen, Is.EqualTo(AppScreen.Playing));
            Assert.That(R, Is.Not.SameAs(first));
            Assert.That(R.Seed, Is.EqualTo(first.Seed + 1), "再戦は新しいシード");
            yield return ClickButton("pause");
            yield return ClickButton("title");
            Assert.That(_game.Flow.Screen, Is.EqualTo(AppScreen.Title));
        }

        [UnityTest]
        public IEnumerator ClickThatRestoresFocus_DoesNotChooseACard()
        {
            yield return StartGame();
            yield return RaiseLevel();
            yield return PassChoiceGuard();
            _game.SendMessage("OnApplicationFocus", false);
            yield return null;
            _game.SendMessage("OnApplicationFocus", true);
            SendButtonClick("card0");
            yield return null;
            yield return null;
            Assert.That(_game.Flow.Screen, Is.EqualTo(AppScreen.LevelUp), "フォーカスを戻すクリックでは選ばない");
            yield return ClickButton("card0");
            Assert.That(_game.Flow.Screen, Is.EqualTo(AppScreen.Playing));
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
        public IEnumerator SettingsAssets_MatchTheCodeDefaults_AndTheAutopilotHash()
        {
            // 生成スクリプトはアセットを上書きしないので、コードの既定値を変えたらアセットも直す（024 の知見）。
            yield return null;
            Assert.That(JsonUtility.ToJson(Rules), Is.EqualTo(JsonUtility.ToJson(new TideConfig())), "定義アセットから組み立てた設定がコードの既定値と同じ");
            Assert.That(_game.Settings.Rules.Enemies, Is.Empty, "敵の数値は定義アセットだけが持つ");
            Assert.That(_game.Settings.Rules.Weapons, Is.Empty, "武器の数値は定義アセットだけが持つ");
            foreach (var enemy in _game.Settings.Enemies) { Assert.That(enemy.DisplayName, Is.Not.Empty); }
            foreach (var weapon in _game.Settings.Weapons) { Assert.That(weapon.DisplayName, Is.Not.Empty); }

            var run = new TideRun(Rules, 1);
            var pilot = new Autopilot();
            while (!run.IsOver)
            {
                while (run.IsChoosing) { run.Choose(pilot.ChoiceFor(run)); }
                run.Step(pilot.CommandFor(run));
            }

            Assert.That(run.Hash(), Is.EqualTo(0xFA88782Au), "シード 1 は App テストの固定値と同じ");
        }

        [Test]
        public void Settings_RejectInvalidValuesAndMissingDefinitions()
        {
            AssertRejected(s => s.Volume = float.NaN, typeof(ArgumentOutOfRangeException));
            AssertRejected(s => s.Rules.PlayerSpeed = 0, typeof(ArgumentOutOfRangeException));
            AssertRejected(s => s.Rules = null, typeof(ArgumentNullException));
            AssertRejected(s => s.Enemies = new[] { s.Enemies[0], null, s.Enemies[2], s.Enemies[3] }, typeof(InvalidOperationException));
            AssertRejected(s => s.Weapons = Array.Empty<WeaponDefinition>(), typeof(ArgumentOutOfRangeException));
            AssertRejected(s => s.Enemies = new[] { s.Enemies[0], s.Enemies[1], s.Enemies[2] }, typeof(ArgumentOutOfRangeException));
            AssertRejected(s =>
            {
                var copy = Object.Instantiate(s.Enemies[0]);
                copy.ArtCell = QuadLayer.Cells;
                s.Enemies = new[] { copy, s.Enemies[1], s.Enemies[2], s.Enemies[3] };
            }, typeof(ArgumentOutOfRangeException));
        }

        [Test]
        public void Describe_ListsOnlyTheChangedValues()
        {
            var bolt = new TideConfig().Weapons[0];
            var text = ThornTideGame.Describe(WeaponKind.Bolt, bolt.At(1), bolt.At(2));
            StringAssert.Contains("ダメージ  4 → 5", text);
            StringAssert.Contains("貫通  1 → 2", text);
            StringAssert.DoesNotContain("弾の数", text);
            Assert.That(ThornTideGame.Describe(WeaponKind.Pulse, bolt.At(1), bolt.At(1)), Is.EqualTo("（変化なし）"));
            Assert.That(ThornTideGame.FormatTicks(7378), Is.EqualTo("2:02"));
        }

        /// <summary>
        /// 完了判定「敵数別のフレーム時間を記録」：生存数 100 / 200 / 上限（400）に保ったまま、Core の 1 Tick と描画の更新（CPU）を別々に計測する。
        /// 武器は全部最大で、倒した分は毎 Tick 補充されるので、各サンプルが目標の数の近くの群れを処理していることも確かめる（008 の知見）。
        /// 上限の場合は割り当てもないことを、Unity のフレームの割り当ての計数で確かめる（027 の知見）。
        /// </summary>
        [UnityTest]
        public IEnumerator Performance_ByEnemyCount()
        {
            yield return null;
            var report = new System.Text.StringBuilder();
            foreach (var target in new[] { 100, 200, Rules.MaxEnemies })
            {
                var config = Rules;
                config.PlayerHealth = TideConfig.MaxHealth;
                foreach (var stage in config.Stages)
                {
                    stage.TargetAlive = target;
                    stage.IntervalTicks = 1;
                }

                var run = new TideRun(config, 7);
                for (var w = 0; w < config.Weapons.Length; w++) { run.SetWeaponLevel(w, config.Weapons[w].MaxLevel); }
                var random = new SeededRandom(target);
                while (run.NormalCount < target)
                {
                    var angle = random.Next(Trig.FullTurn);
                    var distance = random.Range(4 * Units.Tile, 18 * Units.Tile);
                    run.Place(random.Range(0, 2), new Vec2i(Trig.Cos(angle) * distance / Trig.One, Trig.Sin(angle) * distance / Trig.One));
                }

                // 刃の描画物が増えきるまで進めてから測る。
                var command = new PlayerCommand(1, 0);
                for (var i = 0; i < 120; i++) { Advance(run, command, config); }

                const int Samples = 600;
                var step = new double[Samples];
                var render = new double[Samples];
                var watch = new System.Diagnostics.Stopwatch();
                long alive = 0;
                var least = int.MaxValue;
                _game.enabled = false;
                using var recorder = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame");
                yield return null;
                yield return null;
                yield return null;
                var baseline = recorder.LastValue;
                for (var i = 0; i < Samples; i++)
                {
                    while (run.IsChoosing) { run.Choose(0); }
                    command = new PlayerCommand(i / 120 % 2 == 0 ? 1 : -1, i / 60 % 2 == 0 ? 1 : 0);
                    watch.Restart();
                    run.Step(command);
                    step[i] = watch.Elapsed.TotalMilliseconds;
                    watch.Restart();
                    Stage.Render(run, config, Stage.transform.TransformPoint(ThornTideStage.Local(run.Player)));
                    render[i] = watch.Elapsed.TotalMilliseconds;
                    alive += run.NormalCount;
                    least = Math.Min(least, run.NormalCount);
                }

                yield return null;
                var measured = recorder.LastValue;
                GC.KeepAlive(new byte[4096]);
                yield return null;
                var probe = recorder.LastValue;
                _game.enabled = true;

                Array.Sort(step);
                Array.Sort(render);
                var line = $"敵 {target} 体（計測中の平均 {alive / Samples}・最小 {least}）・{Samples} Tick：Core の Step p50 {step[Samples / 2]:F3} ms / p95 {step[Samples * 95 / 100]:F3} ms / 最大 {step[Samples - 1]:F3} ms、" +
                    $"描画の更新 p50 {render[Samples / 2]:F3} ms / p95 {render[Samples * 95 / 100]:F3} ms / 最大 {render[Samples - 1]:F3} ms、" +
                    $"四角形 {Stage.Swarm.Visible}・刃 {Stage.PooledBlades}、割り当て {measured - baseline} バイト（4096 バイトの確認 {probe - baseline}）";
                report.AppendLine(line);
                Debug.Log("[Performance] " + line);
                Assert.That(alive / Samples, Is.GreaterThanOrEqualTo(target * 8 / 10), "目標の数の近くの群れを計測する");
                Assert.That(step[Samples * 95 / 100], Is.LessThan(8), "Core の Step は 1 Tick（16.7 ms）に収まる");
                Assert.That(render[Samples * 95 / 100], Is.LessThan(8));
                if (target == Rules.MaxEnemies)
                {
                    Assert.That(recorder.Valid && probe >= baseline + 4096, Is.True, "割り当ての計数が働いている");
                    Assert.That(measured - baseline, Is.LessThanOrEqualTo(0), "上限の群れでも Step と描画の更新で割り当てをしない");
                }
            }

            TestContext.WriteLine(report.ToString());
        }

        private void Advance(TideRun run, PlayerCommand command, TideConfig config)
        {
            while (run.IsChoosing) { run.Choose(0); }
            run.Step(command);
            Stage.Render(run, config, Stage.transform.TransformPoint(ThornTideStage.Local(run.Player)));
        }

        private void AssertRejected(Action<ThornTideSettings> change, Type exception)
        {
            var settings = Object.Instantiate(_game.Settings);
            Assert.DoesNotThrow(() => settings.BuildRules());
            change(settings);
            Assert.Throws(exception, () => settings.BuildRules());
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
            Assert.That(button.style.display.value, Is.Not.EqualTo(DisplayStyle.None), name + " が表示されていない");
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
