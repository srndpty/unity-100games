using System;
using System.Collections;
using System.Collections.Generic;
using Game.App;
using Game.Core;
using IntGeometry;
using NUnit.Framework;
using TileMotion;
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
    /// <summary>実シーンを読み込み、仮想のキーボードとマウスで操作する。</summary>
    public sealed class AshenSceneTests
    {
        private const int TickLimit = 60 * 60 * 8;

        private TestKeyboard _keyboard;
        private Mouse _mouse;
        private AshenGame _game;

        [UnitySetUp]
        public IEnumerator Load()
        {
            _keyboard = new TestKeyboard();
            _mouse = InputSystem.AddDevice<Mouse>();
            _mouse.MakeCurrent();
            yield return SceneManager.LoadSceneAsync("AshenReliquary");
            yield return null;
            _game = Object.FindAnyObjectByType<AshenGame>();
            for (int i = 0; i < 30 && _game.HudRoot == null; i++) { yield return null; }
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

        private DelveRun R => _game.Flow.Run;

        private DelveConfig Rules => _game.Rules;

        private AshenStage Stage => _game.Stage;

        private VisualElement Root => _game.HudRoot;

        private string Text(string name) => Root.Q<Label>(name).text;

        private bool IsShown(string name)
        {
            var display = Root.Q(name).style.display;
            return display.keyword == StyleKeyword.Undefined && display.value == DisplayStyle.Flex;
        }

        private IEnumerator StartGame()
        {
            yield return _keyboard.Tap(Key.Enter);
            Assert.That(_game.Flow.Screen, Is.EqualTo(AppScreen.HowTo));
            yield return _keyboard.Tap(Key.Enter);
            Assert.That(_game.Flow.Screen, Is.EqualTo(AppScreen.Playing));
            yield return WaitUntil(() => R.Tick > 0);
        }

        /// <summary>入口の部屋の中に自機を置き、他の敵を遠ざける（倒れたことにする）。</summary>
        private Foe Arena(FoeKind kind, int dx, int life)
        {
            R.Hero.Position = FloorMap.TileCenter(3, R.Map.EntranceRow - 3);
            Foe chosen = null;
            foreach (var f in R.Foes)
            {
                if (chosen == null && f.Kind == kind)
                {
                    chosen = f;
                    continue;
                }

                f.Dead = true;
                f.Dropped = true;
            }

            chosen.Position = R.Hero.Position + new Vec2i(dx, 0);
            chosen.Life = life;
            return chosen;
        }

        private static IEnumerator WaitUntil(Func<bool> condition, float seconds = 5f)
        {
            float limit = Time.realtimeSinceStartup + seconds;
            while (!condition() && Time.realtimeSinceStartup < limit) { yield return null; }
            Assert.That(condition(), Is.True, "時間内に条件を満たさなかった");
        }

        /// <summary>時計を止め（timeScale 0）、Tick を直接進める。</summary>
        private void StepUntil(Func<bool> done)
        {
            Time.timeScale = 0;
            for (int i = 0; i < TickLimit && !done(); i++)
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
            for (int i = 0; i < ticks; i++)
            {
                _game.Flow.StepTick();
                _game.ConsumeEvents();
            }

            _game.Render();
        }

        [UnityTest]
        public IEnumerator Scene_HasOneStageMatchingTheMap_AndShowsTheTitle()
        {
            yield return null;
            Assert.That(Object.FindObjectsByType<AshenStage>(FindObjectsInactive.Include), Has.Length.EqualTo(1));
            Assert.That(Stage.Mismatch(_game.Floors), Is.Null);
            Assert.That(Text("headline"), Is.EqualTo("ASHEN RELIQUARY"));
            Assert.That(IsShown("stats"), Is.False, "タイトルでは HUD の数値を出さない");
            Assert.That(Stage.FloorViews[0].gameObject.activeSelf, Is.True);
            Assert.That(Stage.FloorViews[1].gameObject.activeSelf || Stage.FloorViews[2].gameObject.activeSelf, Is.False);
            Assert.That(Stage.Hero.transform.localPosition, Is.EqualTo(AshenStage.Local(_game.Floors[1].Entrance)));
            Assert.That(Stage.VisibleFoes, Is.EqualTo(_game.Floors[1].Spawns.Count), "配置の敵は試合の前から見える");
        }

        [UnityTest]
        public IEnumerator HowToScreen_ThenPlay_ShowsHudForFloorOne()
        {
            yield return _keyboard.Tap(Key.Enter);
            Assert.That(Text("headline"), Is.EqualTo("あそびかた"));
            Assert.That(Text("message").Contains("薙ぎ払い", StringComparison.Ordinal), Is.True);
            yield return _keyboard.Tap(Key.Escape);
            Assert.That(_game.Flow.Screen, Is.EqualTo(AppScreen.Title));
            yield return StartGame();
            yield return null;
            Assert.That(IsShown("panel"), Is.False);
            Assert.That(Text("life"), Is.EqualTo($"HP {R.Hero.Life} / {R.Stats.Life}"));
            Assert.That(Text("floor").Contains(R.Map.Name, StringComparison.Ordinal), Is.True);
            Assert.That(Text("potions"), Is.EqualTo($"回復薬 ×{Rules.StartPotions}"));
        }

        [UnityTest]
        public IEnumerator Wasd_MovesTheHero_AndReleaseStops()
        {
            yield return StartGame();
            Arena(FoeKind.Grunt, 20 * Units.Tile, 100);
            var start = R.Hero.Position;
            _keyboard.Hold(Key.D, Key.S);
            yield return WaitUntil(() => R.Hero.Position.X > start.X + Units.Tile);
            Assert.That(R.Hero.Position.Y, Is.LessThan(start.Y), "D と S で右下へ");
            _keyboard.Hold();
            yield return null;
            yield return null;
            var stopped = R.Hero.Position;
            long tick = R.Tick;
            yield return WaitUntil(() => R.Tick > tick + 10);
            Assert.That(R.Hero.Position, Is.EqualTo(stopped), "離すと止まる");
            Assert.That(Stage.Hero.transform.localPosition, Is.EqualTo(AshenStage.Local(R.Hero.Position)));
        }

        [UnityTest]
        public IEnumerator LeftClick_SwingsTowardThePointer_AndHitsTheFoe()
        {
            yield return StartGame();
            var foe = Arena(FoeKind.Grunt, 300, 100000);
            yield return null;
            var screen = _game.CoreToScreen(foe.Position);
            SetMouse(screen);
            yield return null;
            SetMouse(screen, MouseButton.Left);
            yield return WaitUntil(() => foe.Life < 100000);
            Assert.That(R.Hero.SwingDirection.X, Is.GreaterThan(0), "マウスの向き（右）へ振る");
            Time.timeScale = 0;
            _game.Render();
            var renderer = Stage.FloorViews[0].Foes[IndexOf(foe)];
            Assert.That(renderer.enabled, Is.True);
            Assert.That(renderer.color, Is.EqualTo(AshenStage.FoeTint(foe, R.Tick)), "描いた色は Core の状態から決めた色");
            Assert.That(AshenStage.FoeTint(foe, foe.LastHitTick), Is.Not.EqualTo(Color.white), "受けた Tick は赤く光る");
            Time.timeScale = 1;
            SetMouse(screen);
            yield return null;
            yield return null;
            long last = R.Hero.SwingStartTick;
            long tick = R.Tick;
            yield return WaitUntil(() => R.Tick > tick + (2 * R.AttackInterval));
            Assert.That(R.Hero.SwingStartTick, Is.EqualTo(last), "離すと振るのをやめる");
            Assert.That(Stage.Crosshair.enabled, Is.True, "プレイ中は照準を描く");
        }

        [UnityTest]
        public IEnumerator RightClick_SpinsOnce_AndTheRingIsDrawn()
        {
            yield return StartGame();
            Arena(FoeKind.Grunt, 20 * Units.Tile, 100);
            var screen = _game.CoreToScreen(R.Hero.Position + new Vec2i(Units.Tile, 0));
            SetMouse(screen, MouseButton.Right);
            yield return WaitUntil(() => R.Hero.SpinStartTick > 0);
            Time.timeScale = 0;
            _game.Render();
            Assert.That(Stage.Spin.enabled, Is.True);
            float diameter = (float)Rules.SkillRadius * 2 / Units.Tile;
            Assert.That(Stage.Spin.transform.localScale.x, Is.EqualTo(diameter).Within(1e-4f), "輪の大きさは当たりの範囲");
            Step(Rules.SkillWindup + 10);
            Assert.That(Stage.Spin.enabled, Is.False);
            Assert.That(Text("skill").Contains("あと", StringComparison.Ordinal), Is.True, "待ちの間は残り秒数");
        }

        [UnityTest]
        public IEnumerator PickWithE_OpenInventory_SelectAndEquip_UpdatesTheSheet()
        {
            yield return StartGame();
            Arena(FoeKind.Grunt, 20 * Units.Tile, 100);
            var loot = R.Loot;
            var helm = new Item(loot.Base("hornhelm"), Rarity.Magic, new[] { new RolledAffix(loot.Affix("vital"), 0, new List<int> { 12 }) });
            R.AddGroundForTest(R.Hero.Position + new Vec2i(64, 0), helm);
            yield return _keyboard.Tap(Key.E);
            yield return WaitUntil(() => R.Inventory.Count == 1);

            yield return _keyboard.Tap(Key.I);
            Assert.That(_game.Flow.Screen, Is.EqualTo(AppScreen.Inventory));
            long tick = R.Tick;
            yield return null;
            Assert.That(IsShown("inventory"), Is.True);
            SendButtonClick(Root.Q("bag").Q<Button>(null, "cell"));
            yield return null;
            yield return null;
            var lines = ItemText.Describe(helm);
            Assert.That(Text("detail-name"), Is.EqualTo(lines[0]), "説明は Core の文をそのまま出す");
            Assert.That(Text("detail"), Is.EqualTo(string.Join("\n", lines.GetRange(1, lines.Count - 1))));
            Assert.That(Text("compare"), Is.EqualTo(AshenHud.CompareText(R.Compare(helm))));
            int lifeBefore = R.Stats.Life;

            SendButtonClick(Root.Q<Button>("act-equip"));
            yield return null;
            yield return null;
            Assert.That(R.Equipment[Slot.Helm], Is.SameAs(helm));
            Assert.That(R.Stats.Life, Is.EqualTo(lifeBefore + 15 + 12));
            Assert.That(Text("sheet"), Is.EqualTo(string.Join("\n", ItemText.Stats(R.Stats))));
            Assert.That(R.Tick, Is.EqualTo(tick), "持ち物の画面の間は進まない");

            yield return _keyboard.Tap(Key.I);
            Assert.That(_game.Flow.Screen, Is.EqualTo(AppScreen.Playing));
            yield return null;
            Assert.That(Text("life"), Is.EqualTo($"HP {R.Hero.Life} / {R.Stats.Life}"), "HUD の最大 HP も変わる");
        }

        [UnityTest]
        public IEnumerator GroundTag_ShowsTheNameInRarityColor_AndClickPicksUp()
        {
            yield return StartGame();
            Arena(FoeKind.Grunt, 20 * Units.Tile, 100);
            var loot = R.Loot;
            var ring = new Item(loot.Base("silverring"), Rarity.Rare, new[]
            {
                new RolledAffix(loot.Affix("sharp"), 0, new List<int> { 1, 2 }),
                new RolledAffix(loot.Affix("vital"), 0, new List<int> { 9 }),
                new RolledAffix(loot.Affix("keen"), 0, new List<int> { 3 }),
            });
            var ground = R.AddGroundForTest(R.Hero.Position + new Vec2i(2 * Units.Tile, 0), ring);
            yield return null;
            yield return null;
            Assert.That(_game.Hud.VisibleTags, Is.EqualTo(1));
            Button tag = null;
            Root.Q("tags").Query<Button>().ForEach(b => { if (b.style.display == DisplayStyle.Flex) { tag = b; } });
            Assert.That(tag.text, Is.EqualTo(ring.Name));
            Assert.That(tag.style.color.value, Is.EqualTo(AshenStage.RarityColor(Rarity.Rare)));
            SendButtonClick(tag);
            yield return null;
            yield return null;
            Assert.That(R.Inventory, Has.Member(ring));
            Assert.That(R.Hero.SwingStartTick, Is.EqualTo(-1), "名前札のクリックでは攻撃しない");
            Assert.That(R.Ground, Has.No.Member(ground));
            yield return null;
            Assert.That(_game.Hud.VisibleTags, Is.Zero);
        }

        [UnityTest]
        public IEnumerator Death_ShowsTheFallenHero_ThenRespawnsAtTheEntrance()
        {
            yield return StartGame();
            var foe = Arena(FoeKind.Grunt, 20 * Units.Tile, 100);
            Time.timeScale = 0;
            R.HurtHero(100000, Element.Physical);
            Step(1);
            Assert.That(Text("life"), Is.EqualTo("倒れた…"));
            Assert.That(Stage.Crosshair.enabled, Is.False);
            Assert.That(Stage.Hero.color, Is.Not.EqualTo(Color.white));
            StepUntil(() => !R.Hero.Dead);
            Assert.That(R.Hero.Position, Is.EqualTo(R.Map.Entrance));
            Assert.That(Stage.Hero.transform.localPosition, Is.EqualTo(AshenStage.Local(R.Map.Entrance)));
            Assert.That(foe.Position, Is.EqualTo(foe.Spawn.Center), "生きている敵は元の位置");
            Assert.That(_game.Hud.ToastText(R), Is.EqualTo("入口で目を覚ました"));
        }

        [UnityTest]
        public IEnumerator Boss_ShowsTheBossBar_AndProjectilesAreDrawn()
        {
            yield return StartGame();
            Time.timeScale = 0;
            while (R.FloorNumber < 3)
            {
                R.Hero.Position = FloorMap.TileCenter(R.Map.StairsColumn, R.Map.StairsRow);
                Step(1);
            }

            Assert.That(Stage.FloorViews[2].gameObject.activeSelf, Is.True);
            Foe boss = null;
            foreach (var f in R.Foes) { if (f.Kind == FoeKind.Boss) { boss = f; } }
            R.Hero.Position = boss.Position + new Vec2i(-5 * Units.Tile, 0);
            R.Hero.Invuln = 600;
            StepUntil(() => R.Projectiles.Count >= Rules.BossFanCount);
            Assert.That(IsShown("boss"), Is.True);
            Assert.That(Stage.VisibleProjectiles, Is.EqualTo(R.Projectiles.Count));
            boss.Life = boss.MaxLife / 2;
            Step(1);
            Assert.That(Text("boss-name").Contains("激昂", StringComparison.Ordinal), Is.True);
        }

        [UnityTest]
        public IEnumerator Autopilot_ToResult_ThenBackToTitle_RestoresTheStartupView()
        {
            yield return null;
            var heroStart = Stage.Hero.transform.localPosition;
            var foeStart = new List<Vector3>();
            foreach (var f in Stage.FloorViews[0].Foes) { foeStart.Add(f.transform.localPosition); }
            yield return StartGame();
            _game.Flow.Pilot = new Autopilot();
            StepUntil(() => _game.Flow.Screen == AppScreen.Result);
            yield return null;
            Assert.That(Text("headline"), Is.EqualTo("番人を倒した！"));
            Assert.That(Text("message").Contains($"倒した敵  {R.Record.Kills}", StringComparison.Ordinal), Is.True);

            _game.Flow.Pilot = null;
            Time.timeScale = 1;
            yield return _keyboard.Tap(Key.Enter);
            Assert.That(_game.Flow.Screen, Is.EqualTo(AppScreen.Title));
            yield return null;
            Assert.That(Stage.FloorViews[0].gameObject.activeSelf, Is.True);
            Assert.That(Stage.FloorViews[2].gameObject.activeSelf, Is.False);
            Assert.That(Stage.Hero.transform.localPosition, Is.EqualTo(heroStart));
            Assert.That(Stage.Hero.color, Is.EqualTo(Color.white));
            for (int i = 0; i < foeStart.Count; i++)
            {
                Assert.That(Stage.FloorViews[0].Foes[i].enabled, Is.True, "倒した敵もタイトルでは元に戻る");
                Assert.That(Stage.FloorViews[0].Foes[i].transform.localPosition, Is.EqualTo(foeStart[i]));
            }

            Assert.That(Stage.VisibleGround + Stage.VisibleProjectiles + Stage.VisibleLanes, Is.Zero);
            Assert.That(Stage.Swing.enabled || Stage.Spin.enabled || Stage.Crosshair.enabled, Is.False);
            Assert.That(_game.Hud.VisibleTags, Is.Zero);
            Assert.That(IsShown("boss"), Is.False);
        }

        [UnityTest]
        public IEnumerator FocusLoss_PausesAndDropsHeldKeys()
        {
            yield return StartGame();
            Arena(FoeKind.Grunt, 20 * Units.Tile, 100);
            _keyboard.Hold(Key.D);
            yield return WaitUntil(() => R.Hero.Position.X > FloorMap.TileCenter(3, 0).X);
            _game.SendMessage("OnApplicationFocus", false);
            Assert.That(_game.Flow.Screen, Is.EqualTo(AppScreen.Paused));
            _keyboard.Hold();
            yield return null;
            _game.SendMessage("OnApplicationFocus", true);
            yield return _keyboard.Tap(Key.Enter);
            var position = R.Hero.Position;
            long tick = R.Tick;
            yield return WaitUntil(() => R.Tick > tick + 20);
            Assert.That(R.Hero.Position, Is.EqualTo(position), "押していたキーを持ち越さない");
        }

        [UnityTest]
        public IEnumerator ClickThatRestoresFocus_DoesNotQuit_ButNextClickDoes()
        {
            int requests = 0;
            _game.QuitHandler = () => requests++;
            yield return null;
            _game.SendMessage("OnApplicationFocus", false);
            yield return null;
            _game.SendMessage("OnApplicationFocus", true);
            SendButtonClick(Root.Q<Button>("quit"));
            yield return null;
            yield return null;
            Assert.That(requests, Is.Zero, "フォーカスを戻すクリックでは終了しない");
            yield return null;
            SendButtonClick(Root.Q<Button>("quit"));
            yield return null;
            yield return null;
            Assert.That(requests, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator SettingsAsset_MatchesTheCodeDefaults_AndTheAutopilotHash()
        {
            // 生成スクリプトはアセットを上書きしないので、コードの既定値を変えたらアセットも直す（024）。
            yield return null;
            Assert.That(JsonUtility.ToJson(Rules), Is.EqualTo(JsonUtility.ToJson(new DelveConfig())), "設定アセットの値がコードの既定値と同じ");
            var run = new DelveRun(Rules, _game.Settings.ParseLoot(), _game.Floors, 1);
            var pilot = new Autopilot();
            while (!run.IsOver && run.Tick < TickLimit) { run.Step(pilot.CommandFor(run)); }
            Assert.That(run.Hash(), Is.EqualTo(0xA6B0053Cu), "シード 1 は App テストの固定値と同じ");
        }

        /// <summary>
        /// 3 階で全ての敵が自機を追い、番人の弾が飛び、地面の品が上限いっぱいの場面で、Core の 1 Tick と描画の更新（CPU）を別々に計測する。
        /// </summary>
        [UnityTest]
        public IEnumerator Performance_WithAllFoesAggroAndTheGroundFull()
        {
            yield return null;
            var config = Rules.Copy();
            config.InvulnTicks = 600;
            var run = new DelveRun(config, _game.Settings.ParseLoot(), _game.Floors, 1);
            while (run.FloorNumber < 3)
            {
                run.Hero.Position = FloorMap.TileCenter(run.Map.StairsColumn, run.Map.StairsRow);
                run.Step(HeroCommand.Idle(run.Hero.Position));
            }

            var gen = new LootGenerator(run.Loot);
            var rng = new SeededRandom(5);
            while (run.Ground.Count < config.GroundCap)
            {
                var p = FloorMap.TileCenter(10 + (run.Ground.Count % 8), 13 + (run.Ground.Count / 8 % 2));
                run.AddGroundForTest(p, gen.Roll(rng, 3, Rarity.Rare));
            }

            foreach (var f in run.Foes) { f.Aggro = true; }
            run.Hero.Position = FloorMap.TileCenter(20, 12);
            const int Samples = 600;
            var step = new double[Samples];
            var render = new double[Samples];
            var watch = new System.Diagnostics.Stopwatch();
            var command = new HeroCommand(0, 0, run.Hero.Position + new Vec2i(Units.Tile, 0), true);
            int maxProjectiles = 0;
            for (int i = 0; i < Samples; i++)
            {
                run.Hero.Invuln = 600;
                run.Hero.Life = run.Stats.Life;
                watch.Restart();
                run.Step(command);
                step[i] = watch.Elapsed.TotalMilliseconds;
                maxProjectiles = Math.Max(maxProjectiles, run.Projectiles.Count);
                watch.Restart();
                Stage.Render(run, config, command.Aim, true);
                render[i] = watch.Elapsed.TotalMilliseconds;
            }

            Assert.That(run.Ground.Count, Is.EqualTo(config.GroundCap), "地面は上限いっぱい");
            Assert.That(Stage.VisibleGround, Is.EqualTo(config.GroundCap));
            Assert.That(maxProjectiles, Is.GreaterThanOrEqualTo(config.BossFanCount), "番人の弾が飛んでいる");
            Array.Sort(step);
            Array.Sort(render);
            var report = $"3 階・敵 {run.Map.Spawns.Count} 体が追跡・地面の品 {config.GroundCap}・弾 最大 {maxProjectiles}・{Samples} Tick：" +
                $"Core の Step p50 {step[Samples / 2]:F3} ms / p95 {step[Samples * 95 / 100]:F3} ms / 最大 {step[Samples - 1]:F3} ms、" +
                $"描画の更新 p50 {render[Samples / 2]:F3} ms / p95 {render[Samples * 95 / 100]:F3} ms / 最大 {render[Samples - 1]:F3} ms、描画物 {Stage.PooledRenderers} 個";
            Debug.Log("[Performance] " + report);
            TestContext.WriteLine(report);
            Assert.That(step[Samples * 95 / 100], Is.LessThan(4), "Core の Step は 1 Tick（16.7 ms）に十分収まる");
            Assert.That(render[Samples * 95 / 100], Is.LessThan(4));
        }

        [UnityTest]
        public IEnumerator Settings_RejectInvalidValues()
        {
            yield return null;
            AssertRejected(s => s.Volume = float.NaN, typeof(ArgumentOutOfRangeException));
            AssertRejected(s => s.ViewHeightTiles = 100, typeof(ArgumentOutOfRangeException));
            AssertRejected(s => s.Rules.HeroSpeed = 0, typeof(ArgumentException));
            AssertRejected(s => s.Loot = null, typeof(InvalidOperationException));
            AssertRejected(s => s.Rules = null, typeof(ArgumentNullException));
        }

        [Test]
        public void FormatTicks_ShowsMinutesAndSeconds()
        {
            Assert.That(AshenHud.FormatTicks(0), Is.EqualTo("0:00"));
            Assert.That(AshenHud.FormatTicks(59), Is.EqualTo("0:00"));
            Assert.That(AshenHud.FormatTicks(3600 * 3 + 600), Is.EqualTo("3:10"));
        }

        private void AssertRejected(Action<AshenSettings> change, Type exception)
        {
            var settings = Object.Instantiate(_game.Settings);
            Assert.DoesNotThrow(settings.Validate);
            change(settings);
            Assert.Throws(exception, settings.Validate);
            Object.DestroyImmediate(settings);
        }

        private int IndexOf(Foe foe)
        {
            for (int i = 0; i < R.Foes.Count; i++)
            {
                if (ReferenceEquals(R.Foes[i], foe)) { return i; }
            }

            return -1;
        }

        private void SetMouse(Vector2 position, MouseButton? pressed = null)
        {
            var state = new MouseState { position = position };
            if (pressed.HasValue) { state = state.WithButton(pressed.Value); }
            InputSystem.QueueStateEvent(_mouse, state);
        }

        // ボタンへポインタの押下と離しを送る（ClickEvent では Button.clicked が呼ばれない。013）。
        private static void SendButtonClick(Button button)
        {
            Assert.That(button, Is.Not.Null);
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
