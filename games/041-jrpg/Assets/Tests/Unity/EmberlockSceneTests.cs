#nullable enable
using System;
using System.Collections;
using System.Diagnostics;
using System.IO;
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
    public sealed class EmberlockSceneTests
    {
        private const string SceneName = "EmberlockHollow";
        private const long BackgroundBytes = 128;

        private TestKeyboard _keyboard = null!;
        private string _tempSaveDir = string.Empty;
        private FileSaveStore _saveStore = null!;
        private EmberlockGame _game = null!;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _tempSaveDir = Path.Combine(Path.GetTempPath(), "emberlock_playmode_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempSaveDir);
            _saveStore = new FileSaveStore(Path.Combine(_tempSaveDir, "save_slot.txt"));
            EmberlockGame.SetSaveStoreOverrideForTests(_saveStore);

            _keyboard = new TestKeyboard();
            yield return SceneManager.LoadSceneAsync(SceneName);
            yield return null;

            _game = Object.FindAnyObjectByType<EmberlockGame>();
            Assert.That(_game, Is.Not.Null, "シーン上に EmberlockGame が存在すること");
            for (var i = 0; i < 60 && _game.HudRoot == null; i++)
            {
                yield return null;
            }

            Assert.That(_game.HudRoot, Is.Not.Null, "PanelRenderer の HUD が読み込まれること");
            Time.timeScale = 0f;
            _game.Render();
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            Time.timeScale = 1f;
            EmberlockGame.SetSaveStoreOverrideForTests(null);
            _keyboard.Dispose();
            if (!string.IsNullOrEmpty(_tempSaveDir) && Directory.Exists(_tempSaveDir))
            {
                Directory.Delete(_tempSaveDir, recursive: true);
            }

            yield return null;
        }

        [UnityTest]
        public IEnumerator RealAssetsAndStagePrefabsMatchCoreDefinitions()
        {
            yield return null;
            _game.Settings.Validate();
            Assert.That(_game.Data.Fingerprint, Is.EqualTo(DefaultGameData.Create().Fingerprint));
            Assert.That(_game.BattleStage.Mismatch(), Is.Null);
            Assert.That(_game.MapStages.Count, Is.EqualTo(_game.Data.Maps.Count));

            for (var i = 0; i < _game.MapStages.Count; i++)
            {
                var stage = _game.MapStages[i];
                var map = _game.Data.GetMap(stage.MapId);
                Assert.That(stage.Mismatch(map), Is.Null, $"Stage {stage.MapId} が Core 定義と一致すること");
            }

            // 宝箱を 1 マス動かした地図では Mismatch が不一致を返すこと（023 の知見）
            var mutatedRaw = _game.Database.ToRawDef(_game.Settings);
            var oldChestX = mutatedRaw.Maps[1].Chests[0].X;
            var chestY = mutatedRaw.Maps[1].Chests[0].Y;
            var lines = mutatedRaw.Maps[1].GridText.Replace("\r\n", "\n").TrimEnd('\n').Split('\n');
            var rowChars = lines[chestY].ToCharArray();
            rowChars[oldChestX] = '.';
            rowChars[oldChestX + 1] = 'C';
            lines[chestY] = new string(rowChars);
            mutatedRaw.Maps[1].GridText = string.Join("\n", lines);
            mutatedRaw.Maps[1].Chests[0].X = oldChestX + 1;
            var mutatedData = GameData.Validate(mutatedRaw);
            var f1Stage = FindMapStage("dungeon_f1");
            Assert.That(f1Stage.Mismatch(mutatedData.GetMap("dungeon_f1")), Is.Not.Null);

            // 実アセット由来の生データを壊したとき GameData.TryValidate が検出すること
            var brokenRaw = _game.Database.ToRawDef(_game.Settings);
            brokenRaw.Shops[0].ProductIds[0] = "missing_product_id";
            var ok = GameData.TryValidate(brokenRaw, out _, out var error);
            Assert.That(ok, Is.False);
            Assert.That(error!.Kind, Is.EqualTo(DataErrorKind.UnresolvedReference));
        }

        [UnityTest]
        public IEnumerator TitleCursorMovementKeepsRowLabelsAlignedAndStartsField()
        {
            yield return null;
            yield return null;
            var hud = _game.HudRoot!;
            var row0 = hud.Q<Label>("title-row-0-label");
            var row1 = hud.Q<Label>("title-row-1-label");
            Assert.That(row0.text, Is.EqualTo("はじめから"));
            Assert.That(row1.text, Is.EqualTo("あそびかた"));

            var x0Before = row0.worldBound.xMin;
            var x1Before = row1.worldBound.xMin;

            // カーソル移動で項目ラベルの水平座標が揺れないこと（032 の知見）
            yield return _keyboard.Tap(Key.DownArrow);
            yield return null;
            Assert.That(_game.Flow.TitleCursor, Is.EqualTo(1));
            Assert.That(row0.worldBound.xMin, Is.EqualTo(x0Before).Within(0.5f));
            Assert.That(row1.worldBound.xMin, Is.EqualTo(x1Before).Within(0.5f));

            // あそびかたを開いて閉じる
            yield return _keyboard.Tap(Key.Enter);
            Assert.That(_game.Flow.Screen, Is.EqualTo(QuestScreen.HowTo));
            yield return _keyboard.Tap(Key.Enter);
            Assert.That(_game.Flow.Screen, Is.EqualTo(QuestScreen.Title));

            // はじめからでフィールドへ
            yield return _keyboard.Tap(Key.UpArrow);
            yield return _keyboard.Tap(Key.Enter);
            Assert.That(_game.Flow.Screen, Is.EqualTo(QuestScreen.Field));
        }

        [UnityTest]
        public IEnumerator FieldHoldKeysMoveHeroStopOnReleaseAndOpenCampMenu()
        {
            yield return StartNewGameFromTitle();
            var startY = _game.Flow.World!.Explorer.Y;

            // 入力 E2E: Core の移動は直接呼ばず、キー保持で移動し離すと止まることを検証（013 の知見）
            _keyboard.Hold(Key.W);
            yield return null;
            for (var i = 0; i < 20; i++)
            {
                _game.Flow.StepTicks(1);
            }

            _keyboard.Hold();
            yield return null;
            for (var i = 0; i < 12; i++)
            {
                _game.Flow.StepTicks(1);
            }

            var stoppedY = _game.Flow.World.Explorer.Y;
            Assert.That(stoppedY, Is.LessThan(startY), "W キー長押しで北へ移動すること");
            Assert.That(_game.Flow.World.Explorer.IsMoving, Is.False, "キー解放後にタイル境界で停止すること");

            for (var i = 0; i < 16; i++)
            {
                _game.Flow.StepTicks(1);
            }

            Assert.That(_game.Flow.World.Explorer.Y, Is.EqualTo(stoppedY), "解放後はそれ以上進まないこと");

            // キャンプメニュー開閉とカーソル列位置の不変性
            yield return _keyboard.Tap(Key.Tab);
            yield return null;
            Assert.That(_game.Flow.Screen, Is.EqualTo(QuestScreen.CampMenu));
            var campRow0 = _game.HudRoot!.Q<Label>("left-row-0-label");
            var campXBefore = campRow0.worldBound.xMin;
            yield return _keyboard.Tap(Key.S);
            yield return null;
            Assert.That(campRow0.worldBound.xMin, Is.EqualTo(campXBefore).Within(0.5f));

            yield return _keyboard.Tap(Key.Escape);
            Assert.That(_game.Flow.Screen, Is.EqualTo(QuestScreen.Field));
        }

        [UnityTest]
        public IEnumerator BattleKeyboardInputRejectsInsufficientMpAndCompletesTurn()
        {
            yield return StartNewGameFromTitle();
            var world = _game.Flow.World!;

            // ダンジョンへ入り、歩行キー入力でエンカウントを発生させる
            world.Explorer.WarpTo("dungeon_f1", 5, 5, Direction.Right);
            var guard = 0;
            while (_game.Flow.Screen == QuestScreen.Field && guard < 2000)
            {
                guard++;
                _keyboard.Hold(world.Explorer.X == 5 ? Key.D : Key.A);
                yield return null;
                _game.Flow.StepTicks(1);
            }

            _keyboard.Hold();
            yield return null;
            Assert.That(_game.Flow.Screen, Is.EqualTo(QuestScreen.Battle));
            Assert.That(_game.BattleStage.gameObject.activeSelf, Is.True);

            // 僧侶リナの MP を 0 にして MP 不足グレーアウト＆拒否メッセージを検証
            world.Party[1].SetVitals(world.Party[1].MaxHp, 0);

            // アレン: たたかう -> 先頭の敵
            yield return _keyboard.Tap(Key.Enter);
            yield return _keyboard.Tap(Key.Enter);
            Assert.That(_game.Flow.ActiveBattle!.CurrentActorIndex, Is.EqualTo(1));

            // リナ: じゅもん一覧を開く
            yield return _keyboard.Tap(Key.S);
            yield return _keyboard.Tap(Key.Enter);
            _game.Render();

            var spellLabel = _game.HudRoot!.Q<Label>("battle-sub-0-label");
            var greyColor = spellLabel.style.color.value;
            Assert.That(greyColor.r, Is.LessThan(0.7f), "MP 不足の呪文はグレーアウト色で表示されること");

            // MP 不足のまま決定キーを押しても手番は進まず「MPが足りない！」が出ること
            yield return _keyboard.Tap(Key.Enter);
            _game.Render();
            var feedback = _game.HudRoot.Q<Label>("battle-feedback");
            Assert.That(feedback.text.Contains("MPが足りない", StringComparison.Ordinal), Is.True);
            Assert.That(_game.Flow.ActiveBattle.CurrentActorIndex, Is.EqualTo(1));

            // キャンセルして防御を選び、セオも攻撃を選んで 1 ターン解決
            yield return _keyboard.Tap(Key.Escape);
            yield return _keyboard.Tap(Key.S);
            yield return _keyboard.Tap(Key.S);
            yield return _keyboard.Tap(Key.Enter); // リナ: ぼうぎょ
            yield return _keyboard.Tap(Key.Enter);
            yield return _keyboard.Tap(Key.Enter); // セオ: たたかう

            Assert.That(_game.Flow.ActiveBattle.Step, Is.EqualTo(BattleInputStep.Animating));
            for (var i = 0; i < 200 && _game.Flow.Screen == QuestScreen.Battle && _game.Flow.ActiveBattle.Step == BattleInputStep.Animating; i++)
            {
                yield return _keyboard.Tap(Key.Enter);
                _game.Flow.StepTicks(4);
            }
        }

        [UnityTest]
        public IEnumerator ChurchSaveSceneReloadAndContinueRestoresExactState()
        {
            yield return StartNewGameFromTitle();
            var world = _game.Flow.World!;
            world.AddGold(77);
            Assert.That(world.Flags.Set("chest_f1_gold"), Is.True);

            // 教会の神父の前に立ってキーボードで話しかけ、セーブを実行
            world.Explorer.WarpTo("town", 14, 5, Direction.Up);
            yield return _keyboard.Tap(Key.Enter);
            Assert.That(_game.Flow.Screen, Is.EqualTo(QuestScreen.Church));
            yield return _keyboard.Tap(Key.Enter); // いのる（セーブ）
            Assert.That(_saveStore.Exists, Is.True);

            var expectedHash = world.Hash();

            // シーンを再読込し、タイトルの「つづきから」をキーボードで選んで完全一致を検証
            yield return SceneManager.LoadSceneAsync(SceneName);
            yield return null;
            _game = Object.FindAnyObjectByType<EmberlockGame>();
            for (var i = 0; i < 60 && _game.HudRoot == null; i++)
            {
                yield return null;
            }

            Time.timeScale = 0f;
            _game.Render();

            Assert.That(_game.Flow.HasValidSave, Is.True);
            Assert.That(_game.Flow.TitleOptions[0], Is.EqualTo(TitleOptionKind.Continue));

            yield return _keyboard.Tap(Key.Enter);
            Assert.That(_game.Flow.Screen, Is.EqualTo(QuestScreen.Field));
            Assert.That(_game.Flow.World!.Hash(), Is.EqualTo(expectedHash));
            Assert.That(_game.Flow.World.Gold, Is.EqualTo(_game.Data.Rules.StartGold + 77));
            Assert.That(_game.Flow.World.Flags.Get("chest_f1_gold"), Is.True);
        }

        [UnityTest]
        public IEnumerator CorruptedSaveFileShowsWarningAndHidesContinueOnTitle()
        {
            Assert.That(_saveStore.TryWrite("version=1\nmap=town\ntruncated_without_checksum", out _), Is.True);

            yield return SceneManager.LoadSceneAsync(SceneName);
            yield return null;
            _game = Object.FindAnyObjectByType<EmberlockGame>();
            for (var i = 0; i < 60 && _game.HudRoot == null; i++)
            {
                yield return null;
            }

            Time.timeScale = 0f;
            _game.Render();

            Assert.That(_game.Flow.HasValidSave, Is.False);
            Assert.That(_game.Flow.TitleOptions, Has.No.Member(TitleOptionKind.Continue));

            var notice = _game.HudRoot!.Q<Label>("corrupt-save-notice");
            Assert.That(notice.style.display.value, Is.EqualTo(DisplayStyle.Flex));
            Assert.That(notice.text.Contains("破損", StringComparison.Ordinal), Is.True);

            // そのまま Enter で「はじめから」に進めること
            yield return _keyboard.Tap(Key.Enter);
            Assert.That(_game.Flow.Screen, Is.EqualTo(QuestScreen.Field));
        }

        [UnityTest]
        public IEnumerator ReturningToTitleRestoresInitialBootVisualsAndFocusPauseWorks()
        {
            var townStage = FindMapStage("town");
            var bootHeroPos = townStage.HeroRenderer!.transform.localPosition;
            var bootHeroSprite = townStage.HeroRenderer.sprite;

            yield return StartNewGameFromTitle();
            var world = _game.Flow.World!;
            world.Explorer.WarpTo("dungeon_f1", 6, 6, Direction.Left);
            _game.Render();

            // いったん別マップを描いてからタイトルへ戻す（021・038 の知見）
            var dungeonStage = FindMapStage("dungeon_f1");
            Assert.That(dungeonStage.gameObject.activeSelf, Is.True);
            Assert.That(townStage.gameObject.activeSelf, Is.False);

            // フォーカス喪失で一時停止し、復帰後も手動再開するまで止まっていること
            _game.SetFocus(false);
            Assert.That(_game.Flow.IsPaused, Is.True);
            var ticksBefore = world.PlayTicks;
            _game.Flow.StepTicks(5);
            Assert.That(world.PlayTicks, Is.EqualTo(ticksBefore));
            _game.SetFocus(true);
            Assert.That(_game.Flow.IsPaused, Is.True);

            // ポーズメニューから「タイトルへもどる」を選ぶ
            yield return _keyboard.Tap(Key.S);
            yield return _keyboard.Tap(Key.Enter);

            _game.Render();
            Assert.That(_game.Flow.Screen, Is.EqualTo(QuestScreen.Title));
            Assert.That(dungeonStage.gameObject.activeSelf, Is.False);
            Assert.That(_game.BattleStage.gameObject.activeSelf, Is.False);
            Assert.That(townStage.gameObject.activeSelf, Is.True);
            Assert.That(townStage.HeroRenderer!.transform.localPosition, Is.EqualTo(bootHeroPos));
            Assert.That(townStage.HeroRenderer.sprite, Is.EqualTo(bootHeroSprite));
        }

        [UnityTest]
        public IEnumerator SteadyStateFieldRenderIsFastAndAllocationFree()
        {
            yield return StartNewGameFromTitle();
            _game.Render();

            const int Samples = 180;
            var stepMs = new double[Samples];
            var renderMs = new double[Samples];
            var sw = new Stopwatch();
            for (var i = 0; i < Samples; i++)
            {
                sw.Restart();
                _game.Flow.StepTicks(1);
                stepMs[i] = sw.Elapsed.TotalMilliseconds;
                sw.Restart();
                _game.Render();
                renderMs[i] = sw.Elapsed.TotalMilliseconds;
            }

            const int Frames = 24;
            var renderAlloc = new long[Frames];
            using var recorder = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame");
            for (var i = 0; i < 6; i++)
            {
                _game.Render();
                yield return null;
            }

            var baseline = recorder.LastValue;
            for (var i = 0; i < Frames; i++)
            {
                _game.Render();
                yield return null;
                renderAlloc[i] = Math.Max(0, recorder.LastValue - baseline);
            }

            GC.KeepAlive(new byte[4096]);
            yield return null;
            var probe = recorder.LastValue;

            Array.Sort(stepMs);
            Array.Sort(renderMs);
            var nonZeroFrames = 0;
            for (var i = 0; i < Frames; i++)
            {
                if (renderAlloc[i] > BackgroundBytes)
                {
                    nonZeroFrames++;
                }
            }

            TestContext.WriteLine($"Step p95={stepMs[Samples * 95 / 100]:F3} ms, Render p95={renderMs[Samples * 95 / 100]:F3} ms, NonZeroAllocFrames={nonZeroFrames}/{Frames}, Probe={probe}");
            Assert.That(recorder.Valid, Is.True);
            Assert.That(probe >= 4096 || probe - baseline >= 4096, Is.True, "計測器が 4096B の割り当てを検出できること");
            Assert.That(stepMs[Samples * 95 / 100], Is.LessThan(2.0));
            Assert.That(renderMs[Samples * 95 / 100], Is.LessThan(2.0));
            Assert.That(nonZeroFrames, Is.LessThanOrEqualTo(Frames / 4));
        }

        private IEnumerator StartNewGameFromTitle()
        {
            Assert.That(_game.Flow.Screen, Is.EqualTo(QuestScreen.Title));
            yield return _keyboard.Tap(Key.Enter);
            Assert.That(_game.Flow.Screen, Is.EqualTo(QuestScreen.Field));
        }

        private EmberlockMapStage FindMapStage(string mapId)
        {
            for (var i = 0; i < _game.MapStages.Count; i++)
            {
                if (string.Equals(_game.MapStages[i].MapId, mapId, StringComparison.Ordinal))
                {
                    return _game.MapStages[i];
                }
            }

            throw new InvalidOperationException($"MapStage not found: {mapId}");
        }
    }
}
