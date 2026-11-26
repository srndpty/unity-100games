#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Game.App;
using Game.Core;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

namespace Game.Unity.Tests
{
    /// <summary>
    /// 実シーンの PlayMode テスト。<c>Time.timeScale = 0</c> で実時間を止め、Tick はテストから進める（015）。
    /// 入力は仮想キーボード（<see cref="TestKeyboard"/>）から送り、確かめたい処理をテスト側から呼ばない（013）。
    /// </summary>
    public sealed class KeeperSceneTests
    {
        private const string SceneName = "Thornwild";

        private TestKeyboard _keyboard = null!;
        private string _dir = string.Empty;
        private FileSaveStore _save = null!;
        private FileSaveStore _records = null!;
        private KeeperGame _game = null!;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _dir = Path.Combine(Path.GetTempPath(), "thornwild_playmode_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
            _save = new FileSaveStore(Path.Combine(_dir, "save.txt"));
            _records = new FileSaveStore(Path.Combine(_dir, "records.txt"));
            KeeperGame.SetStoresForTests(_save, _records);
            _keyboard = new TestKeyboard();
            yield return LoadScene();
        }

        private IEnumerator LoadScene()
        {
            yield return SceneManager.LoadSceneAsync(SceneName);
            yield return null;
            _game = Object.FindAnyObjectByType<KeeperGame>();
            Assert.That(_game, Is.Not.Null);
            for (int i = 0; i < 60 && _game.Hud == null; i++) { yield return null; }
            Assert.That(_game.Hud, Is.Not.Null, "HUD が読み込まれる");
            Time.timeScale = 0f;
            _game.Render();
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            Time.timeScale = 1f;
            KeeperGame.SetStoresForTests(null, null);
            _keyboard.Dispose();
            if (Directory.Exists(_dir)) { Directory.Delete(_dir, true); }
            yield return null;
        }

        private KeeperFlow Flow => _game.Flow;

        private VisualElement Hud => _game.Hud!.Root;

        /// <summary>キーを 1 回押して離し、1 Tick 進める。</summary>
        private IEnumerator Press(Key key)
        {
            yield return _keyboard.Tap(key);
            Flow.StepTicks(1);
            _game.Render();
        }

        private static string DataPath(string file) => Path.Combine(Application.dataPath, "Game", "Data", file);

        private static MonsterData ParseWithWorld(string world) => MonsterData.Parse(
            File.ReadAllText(DataPath("Types.txt")),
            File.ReadAllText(DataPath("Statuses.txt")),
            File.ReadAllText(DataPath("Moves.txt")),
            File.ReadAllText(DataPath("Species.txt")),
            world);

        private IEnumerator StartToField()
        {
            yield return Press(Key.Enter);
            Assert.That(Flow.Screen, Is.EqualTo(FlowScreen.StarterPick));
            yield return Press(Key.Enter);
            for (int i = 0; i < 5 && Flow.InDialogue; i++) { yield return Press(Key.Enter); }
            Assert.That(Flow.Screen, Is.EqualTo(FlowScreen.Field));
            Assert.That(Flow.InDialogue, Is.False);
        }

        [UnityTest]
        public IEnumerator RealDataAndPrefabsMatch_AndAMovedTrainerIsDetected()
        {
            yield return null;
            _game.Settings.Validate();
            Assert.That(MonsterDataValidator.Validate(_game.Data), Is.Empty);
            Assert.That(_game.MapStages.Count, Is.EqualTo(_game.Data.Maps.Count));
            foreach (var stage in _game.MapStages) { Assert.That(stage.Mismatch(_game.Data), Is.Null, stage.MapId); }
            Assert.That(_game.BattleStage.Mismatch(_game.Data.Species.Count), Is.Null);

            // トレーナーを 1 マス動かした地図・種類を変えた地図では Prefab と一致しない（023）。
            var world = File.ReadAllText(DataPath("World.txt"));
            var moved = ParseWithWorld(world.Replace("trainer saki 見習いのサキ route 12 7 left", "trainer saki 見習いのサキ route 12 8 left", StringComparison.Ordinal));
            var retiled = ParseWithWorld(world.Replace("row #...........#...~~~~......#....~~~~~....#**#", "row #...........#...~~~~......#....~~~~.....#**#", StringComparison.Ordinal));
            var route = FindStage("route");
            Assert.That(route.Mismatch(moved), Is.Not.Null);
            Assert.That(route.Mismatch(retiled), Is.Not.Null);
            Assert.That(route.Mismatch(_game.Data), Is.Null);
        }

        private KeeperMapStage FindStage(string id)
        {
            foreach (var s in _game.MapStages)
            {
                if (s.MapId == id) { return s; }
            }

            throw new InvalidOperationException(id);
        }

        [UnityTest]
        public IEnumerator TitleCursorKeepsLabelsInPlace_AndHowToOpens()
        {
            yield return null;
            yield return null;
            var row0 = Hud.Q<Label>("title-row-0-label");
            var row1 = Hud.Q<Label>("title-row-1-label");
            Assert.That(row0.text, Is.EqualTo("はじめから"));
            Assert.That(row1.text, Is.EqualTo("あそびかた"));
            var x0 = row0.worldBound.xMin;
            var x1 = row1.worldBound.xMin;
            yield return Press(Key.DownArrow);
            yield return null;
            Assert.That(Flow.Cursor, Is.EqualTo(1));
            Assert.That(row0.worldBound.xMin, Is.EqualTo(x0).Within(0.5f), "選択の印で項目名が動かない（032）");
            Assert.That(row1.worldBound.xMin, Is.EqualTo(x1).Within(0.5f));
            yield return Press(Key.Enter);
            Assert.That(Flow.Screen, Is.EqualTo(FlowScreen.HowTo));
            Assert.That(Hud.Q("howto").resolvedStyle.display, Is.EqualTo(DisplayStyle.Flex));
            yield return Press(Key.X);
            Assert.That(Flow.Screen, Is.EqualTo(FlowScreen.Title));
        }

        [UnityTest]
        public IEnumerator HoldingAnArrowWalks_ReleasingStops_AndXOpensTheMenu()
        {
            yield return StartToField();
            var w = Flow.World!;
            var x = w.Explorer.X;
            var hero = FindStage("town").Hero!;
            var heroX = hero.transform.position.x;
            _keyboard.Hold(Key.LeftArrow);
            yield return null;
            Flow.StepTicks(w.Data.Rules.StepTicks * 3);
            _game.Render();
            Assert.That(w.Explorer.X, Is.LessThan(x - 1), "押している間は歩き続ける");
            Assert.That(hero.transform.position.x, Is.LessThan(heroX), "主人公の絵も動く");
            _keyboard.Hold();
            yield return null;
            Flow.StepTicks(w.Data.Rules.StepTicks * 2);
            var stopped = w.Explorer.X;
            Flow.StepTicks(w.Data.Rules.StepTicks * 3);
            Assert.That(w.Explorer.X, Is.EqualTo(stopped), "離すと止まる");
            yield return Press(Key.X);
            Assert.That(Flow.Screen, Is.EqualTo(FlowScreen.FieldMenu));
            Assert.That(Hud.Q("menu").resolvedStyle.display, Is.EqualTo(DisplayStyle.Flex));
        }

        /// <summary>草むらを歩いて野生と出会い、キーで技を選ぶと相手の HP が減る（E2E）。</summary>
        [UnityTest]
        public IEnumerator WalkingInGrassMeetsAWildMonster_AndKeysChooseAMove()
        {
            yield return StartToField();
            var w = Flow.World!;
            w.WarpForTest("route", 3, 2, Direction.Right);
            for (int i = 0; i < 600 && Flow.Screen == FlowScreen.Field; i++)
            {
                if (!w.Explorer.IsMoving) { yield return _keyboard.Tap(w.Explorer.X % 2 == 0 ? Key.RightArrow : Key.LeftArrow); }
                Flow.StepTicks(1);
            }

            Assert.That(Flow.Screen, Is.EqualTo(FlowScreen.Battle), "草むらで出会う");
            for (int i = 0; i < 2000 && Flow.BattleMenu == BattleMenu.Playing; i++) { Flow.StepTicks(1); }
            _game.Render();
            Assert.That(_game.BattleStage.Foe!.enabled, Is.True, "相手の絵が出ている");
            Assert.That(Hud.Q("battle-menu").resolvedStyle.display, Is.EqualTo(DisplayStyle.Flex));
            var foeHp = w.Battle!.FoeMonster.Hp;
            var log = w.Battle.Log.Count;
            yield return Press(Key.Enter);
            Assert.That(Flow.BattleMenu, Is.EqualTo(BattleMenu.Moves));
            var attack = -1;
            for (int i = 0; i < w.Battle.PlayerMonster.Moves.Count; i++)
            {
                if (w.Battle.PlayerMonster.Moves[i].Move.Power > 0) { attack = i; }
            }

            for (int i = 0; i < attack; i++) { yield return Press(Key.DownArrow); }
            yield return Press(Key.Enter);
            Assert.That(w.Battle?.Log.Count ?? int.MaxValue, Is.GreaterThan(log), "キーで選んだ技が解決された");
            Assert.That(w.Battle == null || w.Battle.FoeMonster.Hp < foeHp || Lab.HasMiss(w.Battle), Is.True, "外れなければ相手の HP が減る");
        }

        /// <summary>トレーナー戦では捕獲玉を押しても何も起きず、野生では押すと玉を投げる（013：起きる場合と対で）。</summary>
        [UnityTest]
        public IEnumerator BallIsRejectedAgainstTrainers_ButThrownAtWildOnes()
        {
            yield return StartToField();
            var w = Flow.World!;
            w.WarpForTest("route", 11, 7, Direction.Right);
            yield return Press(Key.Z);
            Assert.That(Flow.InDialogue, Is.True, "トレーナーが話しかけてくる");
            yield return Press(Key.Z);
            for (int i = 0; i < 2000 && Flow.BattleMenu == BattleMenu.Playing; i++) { Flow.StepTicks(1); }
            yield return Press(Key.DownArrow);
            yield return Press(Key.DownArrow);
            yield return Press(Key.Enter);
            Assert.That(Flow.BattleMenu, Is.EqualTo(BattleMenu.Items));
            yield return Press(Key.DownArrow);
            var balls = w.Battle!.Balls;
            var hash = w.Battle.Hash();
            var rejects = Flow.Cues.Reject;
            yield return Press(Key.Enter);
            Assert.That(w.Battle.Balls, Is.EqualTo(balls));
            Assert.That(w.Battle.Hash(), Is.EqualTo(hash));
            Assert.That(Flow.Cues.Reject, Is.EqualTo(rejects + 1));
            Assert.That(Hud.Q<Label>("battle-notice").text.Contains("つかまえられない", StringComparison.Ordinal), Is.True);

            // 野生なら同じ操作で玉を投げる。
            var other = Flow.World!;
            other.AbortBattleForTest();
            other.StartWildForTest(Monster.Create(other.Data.SpeciesOf("brinecrab"), 3, other.Data.Rules));
            Flow.EnterBattleForTest();
            for (int i = 0; i < 2000 && Flow.BattleMenu == BattleMenu.Playing; i++) { Flow.StepTicks(1); }
            yield return Press(Key.DownArrow);
            yield return Press(Key.DownArrow);
            yield return Press(Key.Enter);
            yield return Press(Key.DownArrow);
            var wildBalls = other.Battle!.Balls;
            yield return Press(Key.Enter);
            Assert.That(other.Battle.Balls, Is.EqualTo(wildBalls - 1));
        }

        /// <summary>回復所で記録 → シーンを読み直す → つづきからで同じ世界（実ファイル）。</summary>
        [UnityTest]
        public IEnumerator SaveAtTheCenterAndContinueAfterReload()
        {
            yield return StartToField();
            var w = Flow.World!;
            w.WarpForTest("town", 10, 4, Direction.Up);
            yield return Press(Key.Z);
            while (Flow.InDialogue) { yield return Press(Key.Z); }
            Assert.That(Flow.Screen, Is.EqualTo(FlowScreen.CenterMenu));
            yield return Press(Key.Z);
            Assert.That(File.Exists(Path.Combine(_dir, "save.txt")), Is.True, "記録のファイルができる");
            var hash = w.Hash();
            yield return LoadScene();
            Assert.That(Flow.HasSave, Is.True);
            Assert.That(Hud.Q<Label>("title-row-1-label").text, Is.EqualTo("つづきから"));
            Assert.That(Flow.Cursor, Is.EqualTo(1));
            yield return Press(Key.Enter);
            Assert.That(Flow.Screen, Is.EqualTo(FlowScreen.Field));
            Assert.That(Flow.World!.Hash(), Is.EqualTo(hash));
        }

        [UnityTest]
        public IEnumerator BrokenSaveShowsANotice_AndNoContinue()
        {
            Assert.That(_save.TryWrite("THORNWILD-SAVE\nversion=1\n", out _), Is.True);
            yield return LoadScene();
            yield return null;
            Assert.That(Flow.HasSave, Is.False);
            Assert.That(Hud.Q<Label>("title-notice").text.Contains("読み込めません", StringComparison.Ordinal), Is.True);
            Assert.That(Hud.Q<Label>("title-row-1-label").text, Is.EqualTo("あそびかた"));
        }

        [UnityTest]
        public IEnumerator FocusLossPauses_AndShowsThePausePanel()
        {
            yield return StartToField();
            _keyboard.Hold(Key.RightArrow);
            yield return null;
            Flow.OnFocusChanged(false);
            _keyboard.Hold();
            yield return null;
            _game.Render();
            Assert.That(Flow.IsPaused, Is.True);
            Assert.That(Hud.Q("pause").resolvedStyle.display, Is.EqualTo(DisplayStyle.Flex));
            var x = Flow.World!.Explorer.X;
            Flow.OnFocusChanged(true);
            Flow.StepTicks(30);
            Assert.That(Flow.World.Explorer.X, Is.EqualTo(x), "復帰しても手で再開するまで止まる");
            yield return Press(Key.P);
            Assert.That(Flow.IsPaused, Is.False);
        }

        /// <summary>操縦役で通しを遊んで結果からタイトルへ戻ると、起動直後のタイトルと同じ姿（021）。</summary>
        [UnityTest]
        public IEnumerator AutopilotClearsAndTheTitleLooksLikeStartup()
        {
            yield return null;
            var town = FindStage("town");
            var heroStart = town.Hero!.transform.position;
            var pilot = new Autopilot();
            var guard = 0;
            while (Flow.Screen != FlowScreen.Result && guard++ < 400_000)
            {
                pilot.Step(Flow);
                Flow.StepTicks(1);
                if (guard % 5000 == 0) { yield return null; }
            }

            Assert.That(Flow.Screen, Is.EqualTo(FlowScreen.Result), "操縦役がクリアする");
            _game.Render();
            Assert.That(Hud.Q("result").resolvedStyle.display, Is.EqualTo(DisplayStyle.Flex));
            yield return Press(Key.Enter);
            yield return null;
            Assert.That(Flow.Screen, Is.EqualTo(FlowScreen.Title));
            Assert.That(town.gameObject.activeSelf, Is.True);
            Assert.That(FindStage("route").gameObject.activeSelf, Is.False);
            Assert.That(town.Hero.transform.position, Is.EqualTo(heroStart));
            foreach (var t in FindStage("route").Trainers) { Assert.That(t.enabled, Is.True, "トレーナーも初期の姿に戻る"); }
            Assert.That(Hud.Q<Label>("title-records").text.Contains("クリア 1 回", StringComparison.Ordinal), Is.True);
        }

        /// <summary>F9 で眺める自動プレイが始まり、人のキーは混ざらず、もう一度 F9 で止まる。</summary>
        [UnityTest]
        public IEnumerator F9StartsAndStopsTheWatchableAutoplay()
        {
            yield return null;
            var label = Hud.Q<Label>("autoplay");
            Assert.That(label.text.Contains("F9", StringComparison.Ordinal), Is.True, "タイトルに切り替えの案内");
            yield return _keyboard.Tap(Key.F9);
            Assert.That(_game.IsAutoplaying, Is.True);
            _game.Render();
            Assert.That(label.text.Contains("自動プレイ中", StringComparison.Ordinal), Is.True);

            // 人のキーは捨てる（タイトルで下を押してもカーソルは動かない）。
            _keyboard.Hold(Key.DownArrow);
            yield return null;
            _keyboard.Hold();
            yield return null;

            // 実時間 30 秒ぶん（4.5 倍）進めると、タイトル → 相棒選び → フィールドを歩くところまで進む。
            for (int i = 0; i < 30 * 60; i++) { _game.StepAutoplay(1.0 / 60.0); }
            Assert.That(Flow.World, Is.Not.Null);
            Assert.That(Flow.World!.Roster.Party.Count, Is.GreaterThanOrEqualTo(1));
            Assert.That(Flow.World.Explorer.TotalSteps, Is.GreaterThan(0), "自動で歩いている");
            Assert.That(Flow.World.PlayTicks, Is.InRange(30 * 60 * 4, 30 * 60 * 5), "人の 4〜5 倍の速さ");

            yield return _keyboard.Tap(Key.F9);
            Assert.That(_game.IsAutoplaying, Is.False);
            var ticks = Flow.World.PlayTicks;
            _game.StepAutoplay(1.0);
            Assert.That(Flow.World.PlayTicks, Is.EqualTo(ticks), "止めたら自動では進まない");
        }

        /// <summary>
        /// 自動プレイ中はフォーカスを失っても止まらない。P で止めると進まず、P か Enter で再開できる
        /// （試遊で「一時停止から P も Enter も効かない」とされた）。止まっていた状態から F9 で始めても進む。
        /// </summary>
        [UnityTest]
        public IEnumerator AutoplayIgnoresFocusLoss_AndPauseResumesWithPOrEnter()
        {
            yield return null;
            Flow.StartNewGame(5);
            Flow.World!.ChooseStarter(0);
            Flow.StepTicks(1);
            Flow.OnFocusChanged(false);
            Assert.That(Flow.IsPaused, Is.True, "手で遊んでいる間はフォーカス喪失で止まる");
            yield return _keyboard.Tap(Key.F9);
            Assert.That(Flow.IsPaused, Is.False, "自動プレイを始めたら再開する");

            _game.SendMessage("OnApplicationFocus", false);
            Assert.That(Flow.IsPaused, Is.False, "自動プレイ中はフォーカスを失っても止めない");
            var t0 = Flow.World.PlayTicks;
            for (int i = 0; i < 60; i++) { _game.StepAutoplay(1.0 / 60.0); }
            Assert.That(Flow.World.PlayTicks, Is.GreaterThan(t0));

            foreach (var resume in new[] { Key.P, Key.Enter })
            {
                yield return _keyboard.Tap(Key.P);
                _game.StepAutoplay(1.0 / 60.0);
                Assert.That(Flow.IsPaused, Is.True, "P で止まる");
                var t1 = Flow.World.PlayTicks;
                for (int i = 0; i < 60; i++) { _game.StepAutoplay(1.0 / 60.0); }
                Assert.That(Flow.World.PlayTicks, Is.EqualTo(t1), "止めている間は進まない");
                yield return _keyboard.Tap(resume);
                _game.StepAutoplay(1.0 / 60.0);
                Assert.That(Flow.IsPaused, Is.False, $"{resume} で再開する");
                for (int i = 0; i < 60; i++) { _game.StepAutoplay(1.0 / 60.0); }
                Assert.That(Flow.World.PlayTicks, Is.GreaterThan(t1));
            }

            yield return _keyboard.Tap(Key.F9);
        }

        /// <summary>
        /// 自動プレイはクリアの結果画面で止まって人の操作に戻り（2 周目に入らない）、記録ファイルを変えない（レビュー P2-1）。
        /// 経験値のメーターも戦闘中に出ている。
        /// </summary>
        [UnityTest]
        public IEnumerator AutoplayStopsAtTheResult_AndLeavesTheRecordsAlone()
        {
            Assert.That(_records.TryWrite(Records.Empty.WithClear(123456, 2).Encode(), out _), Is.True);
            yield return LoadScene();
            var path = Path.Combine(_dir, "records.txt");
            var before = File.ReadAllText(path);
            yield return _keyboard.Tap(Key.F9);
            var sawExp = false;
            for (int i = 0; i < 40_000 && _game.IsAutoplaying; i++)
            {
                _game.StepAutoplay(1.0);
                if (!sawExp && Flow.Screen == FlowScreen.Battle && Flow.Presenter != null && Flow.Presenter.Player.Visible)
                {
                    _game.Render();
                    sawExp = Hud.Q("player-exp-row").resolvedStyle.display == DisplayStyle.Flex;
                }

                if (i % 500 == 0) { yield return null; }
            }

            Assert.That(_game.IsAutoplaying, Is.False, "クリアで自動プレイが止まる");
            Assert.That(Flow.Screen, Is.EqualTo(FlowScreen.Result), "結果画面で止まる（2 周目に入らない）");
            Flow.StepTicks(600);
            Assert.That(Flow.Screen, Is.EqualTo(FlowScreen.Result), "人が決定するまで結果のまま");
            Assert.That(File.ReadAllText(path), Is.EqualTo(before), "記録ファイルは変わらない");
            Assert.That(sawExp, Is.True, "戦闘で経験値のメーターが出ていた");
            _game.Render();
            Assert.That(Hud.Q<Label>("result-body").text.Contains("記録しない", StringComparison.Ordinal), Is.True);
        }

        /// <summary>性能の記録：描画の更新と戦闘の 1 ターンの解決（この PC の CPU 時間）。</summary>
        [UnityTest]
        public IEnumerator RenderAndBattleResolveStayCheap()
        {
            yield return StartToField();
            var w = Flow.World!;
            for (int i = 0; i < 5; i++) { w.Roster.Receive(Monster.Create(w.Data.SpeciesOf("brinecrab"), 10, w.Data.Rules)); }
            var render = new List<double>();
            var sw = new Stopwatch();
            for (int i = 0; i < 300; i++)
            {
                Flow.StepTicks(1);
                sw.Restart();
                _game.Render();
                sw.Stop();
                render.Add(sw.Elapsed.TotalMilliseconds);
            }

            var resolve = new List<double>();
            for (int n = 0; n < 200; n++)
            {
                var b = Battle.Wild(w.Data, w.Roster.Party, Monster.Create(w.Data.SpeciesOf("wickmoth"), 12, w.Data.Rules), 5, 3, true, IntGeometry.RandomSeries.Derive(n + 1, 1, 2));
                sw.Restart();
                b.Submit(BattleCommand.Fight(0));
                sw.Stop();
                resolve.Add(sw.Elapsed.TotalMilliseconds);
            }

            var renderP95 = P95(render);
            var resolveP95 = P95(resolve);
            Debug.Log($"[Perf] 描画の更新 p95 {renderP95:F3} ms、戦闘 1 ターンの解決 p95 {resolveP95:F3} ms");
            Assert.That(renderP95, Is.LessThan(8.0), "描画の更新");
            Assert.That(resolveP95, Is.LessThan(2.0), "1 ターンの解決");
        }

        private static double P95(List<double> values)
        {
            values.Sort();
            return values[(int)Math.Floor(values.Count * 0.95)];
        }

        private static class Lab
        {
            public static bool HasMiss(Battle battle)
            {
                foreach (var e in battle.Log)
                {
                    if (e.Kind == BattleEventKind.Missed) { return true; }
                }

                return false;
            }
        }
    }
}
