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
using UnityEngine.UIElements;
using MouseButton = UnityEngine.InputSystem.LowLevel.MouseButton;
using Object = UnityEngine.Object;

namespace Game.Unity.Tests
{
    public sealed class EmberVaultSceneTests
    {
        private const int TickLimit = 60 * 60 * 5;

        private TestKeyboard _keyboard;
        private Mouse _mouse;
        private EmberVaultGame _game;

        [UnitySetUp]
        public IEnumerator Load()
        {
            _keyboard = new TestKeyboard();
            _mouse = InputSystem.AddDevice<Mouse>();
            yield return SceneManager.LoadSceneAsync("EmberVault");
            yield return null;
            _game = Object.FindAnyObjectByType<EmberVaultGame>();
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

        private Dungeon D => _game.Flow.Dungeon;

        private string Text(string name) => _game.HudRoot.Q<Label>(name).text;

        private bool IsShown(string name) => _game.HudRoot.Q<Button>(name).style.display.value == DisplayStyle.Flex;

        private IEnumerator StartGame()
        {
            yield return _keyboard.Tap(Key.Enter);
            Assert.That(_game.Flow.Screen, Is.EqualTo(AppScreen.Playing));

            // 開始の Tick より前に押した剣・ロールは「押したままのキー」として扱われるので、1 Tick 進むのを待つ。
            yield return WaitUntil(() => D.ElapsedTicks > 0);
        }

        /// <summary>時計を止め（timeScale 0）、操縦役を付けてから Enter で始める。</summary>
        private IEnumerator StartPiloted(IPilot pilot)
        {
            Time.timeScale = 0;
            _game.Flow.Pilot = pilot;
            yield return _keyboard.Tap(Key.Enter);
            Assert.That(_game.Flow.Screen, Is.EqualTo(AppScreen.Playing));
            Assert.That(D.ElapsedTicks, Is.Zero);
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

        /// <summary>時計を止め（timeScale 0）、Tick を直接進める。出来事は画面の側（音・案内）でも受け取る。</summary>
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

        /// <summary>鍵の扉のタイルが、扉の開閉（ダンジョンの状態）と一致するか。描かれている扉のタイルの数を返す。</summary>
        private int AssertDoorsMatch(int room, DungeonState state)
        {
            var stage = _game.StageAt(room);
            var map = Rooms.Create().Rooms[room].Map;
            var drawn = 0;
            for (var row = 0; row < map.Height; row++)
            {
                for (var column = 0; column < map.Width; column++)
                {
                    var kind = map[column, row];
                    if (kind != TileKind.SmallDoor && kind != TileKind.BossDoor) { continue; }
                    var open = state != null && state.IsOpen(room, map.DoorSideOf(column, row)!.Value);
                    var has = stage.Doors.HasTile(new Vector3Int(column, row, 0));
                    Assert.That(has, Is.EqualTo(!open), $"扉 ({column}, {row})");
                    if (has) { drawn++; }
                }
            }

            return drawn;
        }

        [UnityTest]
        public IEnumerator Stages_MatchCoreRooms_OnlyTheEntranceIsShownAtTitle()
        {
            yield return null;
            var world = Rooms.Create();
            Assert.That(Object.FindObjectsByType<EmberVaultStage>(FindObjectsInactive.Include), Has.Length.EqualTo(world.Rooms.Count), "Stage のインスタンスは部屋ごとに 1 つ");
            for (var i = 0; i < world.Rooms.Count; i++)
            {
                var stage = _game.StageAt(i);
                Assert.That(stage.RoomIndex, Is.EqualTo(i));
                Assert.That(stage.Mismatch(world.Rooms[i]), Is.Null, world.Rooms[i].Name);
                Assert.That(stage.gameObject.activeSelf, Is.EqualTo(i == world.StartRoom));
                Assert.That(stage.transform.position, Is.EqualTo(EmberVaultGame.RoomOrigin(world.Rooms[i])), "部屋の格子どおりに並ぶ");
            }

            Assert.That(Text("headline"), Is.EqualTo("EMBER VAULT"));
            Assert.That(Text("message"), Does.Contain("Z：剣").And.Contain("X：回避ロール"), "タイトルに操作説明");
            Assert.That(IsShown("submit") && IsShown("quit"), Is.True);
            Assert.That(_game.HudRoot.Q("stats").style.display.value, Is.EqualTo(DisplayStyle.None));
        }

        [UnityTest]
        public IEnumerator StageMismatch_DetectsMovedOrChangedObjects()
        {
            yield return null;
            Assert.That(_game.StageAt(Rooms.East).Mismatch(Variant(Rooms.East, rows => rows)), Is.Null, "元の地図とは一致する");
            Assert.That(_game.StageAt(Rooms.East).Mismatch(Variant(Rooms.East, rows => Move(rows, 'k', 1))), Does.Contain("宝箱").And.Contain("位置"), "宝箱を 1 マス動かす");
            Assert.That(_game.StageAt(Rooms.East).Mismatch(Variant(Rooms.East, rows => Swap(rows, 'k', 'K'))), Does.Contain("宝箱").And.Contain("絵"), "宝箱の中身を変える");
            Assert.That(_game.StageAt(Rooms.West).Mismatch(Variant(Rooms.West, rows => Move(rows, 's', 1))), Does.Contain("敵").And.Contain("位置"), "敵を 1 マス動かす");
            Assert.That(_game.StageAt(Rooms.West).Mismatch(Variant(Rooms.West, rows => Swap(rows, 's', 'a'))), Does.Contain("敵").And.Contain("絵"), "敵の種類を変える");
            Assert.That(_game.StageAt(Rooms.West).Mismatch(Variant(Rooms.West, rows => Move(rows, 'R', 1))), Does.Contain("小鍵"), "全滅の小鍵を 1 マス動かす");
            Assert.That(_game.StageAt(Rooms.Lair).Mismatch(Variant(Rooms.Lair, rows => Move(rows, 'M', -1))), Does.Contain("敵"), "ボスを 1 マス動かす");
            Assert.That(_game.StageAt(Rooms.Entrance).Mismatch(Variant(Rooms.Entrance, rows => Swap(rows, 'L', 'B'))), Does.Contain("扉"), "扉の種類を変える");
        }

        /// <summary>本番の部屋の文字の地図を変えて解析し直す。</summary>
        private static Room Variant(int index, Func<string[], string[]> change)
        {
            var (name, x, y, rows) = Rooms.Source(index);
            return Room.Parse(name, x, y, change(rows));
        }

        /// <summary>最初に見つかった記号を横に <paramref name="dx"/> マス動かす（動かした先は床であること）。</summary>
        private static string[] Move(string[] rows, char symbol, int dx)
        {
            for (var line = 0; line < rows.Length; line++)
            {
                var column = rows[line].IndexOf(symbol);
                if (column < 0) { continue; }
                var chars = rows[line].ToCharArray();
                Assert.That(chars[column + dx], Is.EqualTo('.'), "動かした先は床");
                chars[column] = '.';
                chars[column + dx] = symbol;
                rows[line] = new string(chars);
                return rows;
            }

            throw new InvalidOperationException($"'{symbol}' がありません。");
        }

        /// <summary>記号 <paramref name="from"/> をすべて <paramref name="to"/> に変える。</summary>
        private static string[] Swap(string[] rows, char from, char to)
        {
            for (var line = 0; line < rows.Length; line++) { rows[line] = rows[line].Replace(from, to); }
            return rows;
        }

        [UnityTest]
        public IEnumerator Camera_ShowsTheWholeRoomBelowTheHudBand()
        {
            yield return null;
            var camera = _game.ViewCamera;
            var stage = _game.StageAt(Rooms.Entrance);
            var bottomLeft = camera.WorldToViewportPoint(stage.transform.TransformPoint(Vector3.zero));
            var topRight = camera.WorldToViewportPoint(stage.transform.TransformPoint(new Vector3(Room.Columns, Room.Rows + EmberVaultGame.HudBandTiles, 0)));
            Assert.That(bottomLeft.x, Is.GreaterThanOrEqualTo(-1e-4f));
            Assert.That(bottomLeft.y, Is.GreaterThanOrEqualTo(-1e-4f));
            Assert.That(topRight.x, Is.LessThanOrEqualTo(1 + 1e-4f));
            Assert.That(topRight.y, Is.LessThanOrEqualTo(1 + 1e-4f));
        }

        [UnityTest]
        public IEnumerator ArrowKeys_MoveDiagonally_AndReleaseStops()
        {
            yield return StartGame();
            var start = D.Hero.Body;
            _keyboard.Hold(Key.LeftArrow, Key.UpArrow);
            yield return WaitUntil(() => D.Hero.Body.Y > start.Y + Units.Tile);
            Assert.That(D.Hero.Body.X, Is.LessThan(start.X), "左上へ動く");
            Assert.That(start.X - D.Hero.Body.X, Is.EqualTo(D.Hero.Body.Y - start.Y), "斜めは両軸に同じだけ");
            _keyboard.Hold();
            yield return null;
            yield return null;
            var stopped = D.Hero.Body;
            yield return WaitSeconds(.2f);
            Assert.That(D.Hero.Body, Is.EqualTo(stopped), "離した後は止まったまま");
            var stage = _game.CurrentStage;
            Assert.That(stage.Hero.transform.position, Is.EqualTo(stage.World(D.Hero.Body)), "自機の描画は体の箱の中央");
        }

        [UnityTest]
        public IEnumerator AttackKey_SwingsOncePerPress_AndTheSwordIsDrawnOnTheHitbox()
        {
            yield return StartGame();
            _keyboard.Hold(Key.Z);
            yield return WaitUntil(() => D.Hero.IsSwordActive);
            yield return null;
            var stage = _game.CurrentStage;
            if (D.Hero.IsSwinging)
            {
                Assert.That(stage.Sword.enabled, Is.True, "振っている間は剣が見える");
                Assert.That(stage.Sword.transform.position, Is.EqualTo(stage.World(D.Hero.SwordFacing(D.Hero.Facing))), "剣の描画は当たりの箱の中央");
            }

            var serial = D.Hero.SwingSerial;
            yield return WaitUntil(() => !D.Hero.IsSwinging);
            yield return WaitSeconds(.3f);
            Assert.That(D.Hero.SwingSerial, Is.EqualTo(serial), "押したままでは振り直さない");
            Assert.That(stage.Sword.enabled, Is.False);
            _keyboard.Hold();
            yield return WaitSeconds(.05f);
            yield return _keyboard.Tap(Key.Space);
            yield return WaitUntil(() => D.Hero.SwingSerial == serial + 1);
        }

        [UnityTest]
        public IEnumerator RollKey_RollsWithInvulnerability()
        {
            yield return StartGame();
            var y = D.Hero.Body.Y;
            _keyboard.Hold(Key.X);
            yield return WaitUntil(() => D.Hero.IsRolling);
            Assert.That(D.Hero.IsInvulnerable, Is.True, "ロールの始めは無敵");
            yield return WaitUntil(() => !D.Hero.IsRolling);
            Assert.That(D.Hero.Body.Y, Is.GreaterThan(y + Units.Tile), "向き（上）へ転がった");
            _keyboard.Hold();
        }

        [UnityTest]
        public IEnumerator PushingTheLockedDoor_NeedsAKey_ThenTheDoorDisappears()
        {
            yield return StartGame();
            Assert.That(AssertDoorsMatch(Rooms.Entrance, D.State), Is.EqualTo(3), "入口の上の小扉が描かれている");
            _keyboard.Hold(Key.UpArrow);
            yield return WaitUntil(() => _game.Toast.Contains("小鍵が必要"));
            Assert.That(D.RoomIndex, Is.EqualTo(Rooms.Entrance));
            Assert.That(AssertDoorsMatch(Rooms.Entrance, D.State), Is.EqualTo(3), "鍵がなければ開かない");
            _keyboard.Hold();

            // 東の間の宝箱を開けたことにして、もう一度押し当てる。
            D.State.TryOpenChest(Rooms.East, 0);
            yield return null;
            Assert.That(Text("keys"), Is.EqualTo("小鍵 ×1"));
            _keyboard.Hold(Key.UpArrow);
            yield return WaitUntil(() => D.State.IsOpen(Rooms.Entrance, Side.Top));
            yield return null;
            Assert.That(AssertDoorsMatch(Rooms.Entrance, D.State), Is.Zero, "開いた扉のタイルは消える");
            Assert.That(Text("keys"), Is.EqualTo("小鍵 ×0"));
            yield return WaitUntil(() => D.RoomIndex == Rooms.Antechamber);
            _keyboard.Hold();
            Assert.That(AssertDoorsMatch(Rooms.Antechamber, D.State), Is.EqualTo(6), "前室の下の扉は開き、左の小扉と右のボス扉は閉じている");
        }

        [UnityTest]
        public IEnumerator Transition_ScrollsTheCameraBetweenTheTwoRooms()
        {
            yield return StartPiloted(new Autopilot());
            var total = _game.Settings.Rules.TransitionTicks;
            StepUntil(() => D.Status == DungeonStatus.Transition && D.TransitionRemaining == total / 2);
            var from = EmberVaultGame.RoomView(D.World.Rooms[D.PreviousRoom]);
            var to = EmberVaultGame.RoomView(D.World.Rooms[D.RoomIndex]);
            var camera = _game.ViewCamera.transform.position;
            var expected = Vector3.Lerp(from, to, (float)(total - D.TransitionRemaining) / total);
            Assert.That(new Vector2(camera.x, camera.y), Is.EqualTo(new Vector2(expected.x, expected.y)), "カメラは残り Tick に比例した位置");
            var leaving = _game.StageAt(D.PreviousRoom);
            Assert.That(leaving.gameObject.activeSelf && _game.CurrentStage.gameObject.activeSelf, Is.True, "切り替え中は 2 部屋とも描く");
            Assert.That(leaving.Hero.enabled, Is.False, "出ていく部屋に自機は描かない");
            foreach (var enemy in leaving.Enemies) { Assert.That(enemy.enabled, Is.False, "出ていく部屋に敵は描かない"); }

            StepUntil(() => D.Status == DungeonStatus.Playing);
            Assert.That(leaving.gameObject.activeSelf, Is.False);
            Assert.That(new Vector2(_game.ViewCamera.transform.position.x, _game.ViewCamera.transform.position.y), Is.EqualTo(new Vector2(to.x, to.y)));
            Assert.That(Text("room"), Is.EqualTo(D.World.Rooms[D.RoomIndex].Name));
            Assert.That(_game.HudRoot.Q("minimap").Query(className: "map-room").ToList(), Has.Count.EqualTo(2), "訪れた 2 部屋だけを描く");
            Assert.That(_game.HudRoot.Q("minimap").Query(className: "map-current").ToList(), Has.Count.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator Pilot_ChestRewardAndDoors_AreDrawnAndKeptOnReentry()
        {
            yield return StartPiloted(new Autopilot());
            var events = StepUntil(() => D.State.IsChestOpen(Rooms.East, 0));
            Assert.That(_game.StageAt(Rooms.East).Chests[0].sprite, Is.SameAs(_game.StageAt(Rooms.East).ChestOpenSprite), "開けた宝箱の絵");
            Assert.That(_game.Toast, Does.Contain("小鍵"));
            Assert.That(_game.HudRoot.Q<Label>("toast").style.display.value, Is.EqualTo(DisplayStyle.Flex));

            StepUntil(() => D.State.IsRewardShown(Rooms.West));
            Assert.That(_game.StageAt(Rooms.West).Reward.enabled, Is.True, "全滅で小鍵が現れる");
            StepUntil(() => D.State.IsRewardTaken(Rooms.West));
            Assert.That(_game.StageAt(Rooms.West).Reward.enabled, Is.False, "取った小鍵は消える");

            // 前室 → 宝物庫 → 前室と入り直しても、開けた扉は開いたまま描かれる。
            StepUntil(() => D.State.HasBossKey);
            Assert.That(D.RoomIndex, Is.EqualTo(Rooms.Vault));
            Assert.That(_game.StageAt(Rooms.Vault).Chests[0].sprite, Is.SameAs(_game.StageAt(Rooms.Vault).ChestOpenSprite));
            Assert.That(_game.HudRoot.Q<Label>("boss-key").ClassListContains("owned"), Is.True);
            StepUntil(() => D.RoomIndex == Rooms.Antechamber && D.Status == DungeonStatus.Playing);
            Assert.That(AssertDoorsMatch(Rooms.Antechamber, D.State), Is.EqualTo(3), "下と左の小扉は開いたまま、ボス扉だけ閉じている");
            StepUntil(() => _game.Toast.Length == 0);
            Assert.That(events & FlowEvents.ChestOpened, Is.EqualTo(FlowEvents.ChestOpened));
        }

        [UnityTest]
        public IEnumerator GameOver_ContinuesFromTheEntrance_WithKeysKept()
        {
            yield return StartPiloted(new Autopilot());
            StepUntil(() => D.State.IsChestOpen(Rooms.East, 0));
            _game.Flow.Pilot = new ChasePilot();
            StepUntil(() => _game.Flow.Screen == AppScreen.GameOver);
            Assert.That(Text("headline"), Is.EqualTo("倒れてしまった…"));
            Assert.That(Text("message"), Does.Contain("入口"));
            Assert.That(IsShown("submit") && IsShown("restart") && IsShown("title"), Is.True);
            Assert.That(_game.CurrentStage.Hero.enabled, Is.True, "倒れた姿を描く");

            Time.timeScale = 1;
            _game.Flow.Pilot = null;
            yield return _keyboard.Tap(Key.Enter);
            Assert.That(_game.Flow.Screen, Is.EqualTo(AppScreen.Playing));
            yield return null;
            Assert.That(D.RoomIndex, Is.EqualTo(Rooms.Entrance));
            Assert.That(_game.CurrentStage.RoomIndex, Is.EqualTo(Rooms.Entrance));
            Assert.That(Text("keys"), Is.EqualTo("小鍵 ×1"), "手に入れた鍵はそのまま");
            Assert.That(Text("deaths"), Is.EqualTo("倒れた  1"));
            Assert.That(Text("health"), Is.EqualTo(new string('♥', _game.Settings.Rules.MaxHealth)));
            Assert.That(_game.Toast, Does.Contain("入口から再開"));
        }

        [UnityTest]
        public IEnumerator Pilot_BeatsTheBoss_TheTreasureAppears_AndTheResultIsShown()
        {
            yield return StartPiloted(new Autopilot());
            StepUntil(() => D.RoomIndex == Rooms.Lair && D.Status == DungeonStatus.Playing && D.Run.Projectiles.Count > 0);
            var lair = _game.CurrentStage;
            Assert.That(lair.VisibleShots, Is.EqualTo(D.Run.Projectiles.Count), "ボスの弾が描かれる");
            Assert.That(lair.Treasure.enabled, Is.False, "倒す前に秘宝はない");
            StepUntil(() => D.State.BossDefeated);
            Assert.That(lair.Treasure.enabled, Is.True, "倒すと秘宝が現れる");
            Assert.That(lair.Enemies[0].enabled, Is.False, "倒したボスは消える");
            Assert.That(_game.Toast, Does.Contain("秘宝"));
            StepUntil(() => _game.Flow.Screen == AppScreen.Victory);
            Assert.That(Text("headline"), Is.EqualTo("秘宝を手に入れた！"));
            Assert.That(Text("message"), Does.Contain(EmberVaultGame.FormatTicks(_game.Flow.ClearTicks!.Value)).And.Contain("倒れた回数  0"));
            Assert.That(IsShown("submit") && IsShown("title"), Is.True);
        }

        [UnityTest]
        public IEnumerator BackToTitle_RestoresTheStartupView()
        {
            yield return null;
            var entrance = _game.StageAt(Rooms.Entrance);
            var initial = (entrance.Hero.transform.position, entrance.Hero.sprite, entrance.Hero.flipX, entrance.Hero.enabled);

            yield return StartPiloted(new Autopilot());
            StepUntil(() => D.State.IsOpen(Rooms.Entrance, Side.Top) && D.RoomIndex == Rooms.Antechamber && D.Status == DungeonStatus.Playing);
            Time.timeScale = 1;
            _game.Flow.Pilot = null;
            yield return _keyboard.Tap(Key.Escape);
            yield return _keyboard.Tap(Key.T);
            Assert.That(_game.Flow.Screen, Is.EqualTo(AppScreen.Title));
            yield return null;
            Assert.That(entrance.gameObject.activeSelf, Is.True);
            Assert.That(_game.StageAt(Rooms.Antechamber).gameObject.activeSelf, Is.False);
            Assert.That((entrance.Hero.transform.position, entrance.Hero.sprite, entrance.Hero.flipX, entrance.Hero.enabled), Is.EqualTo(initial), "起動直後と同じ表示");
            Assert.That(AssertDoorsMatch(Rooms.Entrance, null), Is.EqualTo(3), "開けた扉は閉じた姿に戻る");
            var camera = _game.ViewCamera.transform.position;
            var view = EmberVaultGame.RoomView(D?.World.Rooms[Rooms.Entrance] ?? Rooms.Create().Rooms[Rooms.Entrance]);
            Assert.That(new Vector2(camera.x, camera.y), Is.EqualTo(new Vector2(view.x, view.y)), "カメラも入口に戻る");
        }

        [UnityTest]
        public IEnumerator PauseKey_StopsTimeAndShowsTheBigMap()
        {
            yield return StartGame();
            yield return _keyboard.Tap(Key.Escape);
            Assert.That(_game.Flow.Screen, Is.EqualTo(AppScreen.Paused));
            var ticks = D.ElapsedTicks;
            yield return WaitSeconds(.2f);
            Assert.That(D.ElapsedTicks, Is.EqualTo(ticks));
            Assert.That(Text("headline"), Is.EqualTo("地図"));
            var bigmap = _game.HudRoot.Q("bigmap");
            Assert.That(bigmap.style.display.value, Is.EqualTo(DisplayStyle.Flex));
            Assert.That(bigmap.Query(className: "map-room").ToList(), Has.Count.EqualTo(1), "まだ入口だけ");
            Assert.That(bigmap.Query(className: "map-link-locked").ToList(), Has.Count.EqualTo(1), "入口の上の小扉は閉じた色");
            Assert.That(IsShown("restart") && IsShown("title"), Is.True);
            yield return _keyboard.Tap(Key.P);
            Assert.That(_game.Flow.Screen, Is.EqualTo(AppScreen.Playing));
            Assert.That(_game.HudRoot.Q("panel").style.display.value, Is.EqualTo(DisplayStyle.None));
        }

        [UnityTest]
        public IEnumerator HudShowsHealth_AndHurtBlinks()
        {
            yield return StartGame();
            Time.timeScale = 0;
            D.Hero.Hurt(2, 0, 0);
            _game.Flow.StepTick();
            _game.ConsumeEvents();
            _game.Render();
            yield return null;
            Assert.That(Text("health"), Is.EqualTo("♥♥♥♥♡♡"));
            var shown = 0;
            var hidden = 0;
            for (var i = 0; i < 40; i++)
            {
                _game.Flow.StepTick();
                _game.Render();
                if (_game.CurrentStage.Hero.enabled) { shown++; } else { hidden++; }
            }

            Assert.That(shown > 0 && hidden > 0, Is.True, "無敵の間は点滅する");
            for (var i = 0; i < _game.Settings.Rules.InvulnTicks; i++) { _game.Flow.StepTick(); }
            _game.Render();
            Assert.That(_game.CurrentStage.Hero.enabled, Is.True, "無敵が切れたら点滅しない");
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
            Assert.That(D.ElapsedTicks, Is.LessThan(30));
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
            var x = D.Hero.Body.X;
            _keyboard.Hold(Key.RightArrow);
            yield return WaitUntil(() => D.Hero.Body.X > x);
            _game.SendMessage("OnApplicationFocus", false);
            Assert.That(_game.Flow.Screen, Is.EqualTo(AppScreen.Paused));
            _keyboard.Hold();
            yield return null;
            _game.SendMessage("OnApplicationFocus", true);
            yield return null;
            yield return null;
            yield return _keyboard.Tap(Key.Enter);
            Assert.That(_game.Flow.Screen, Is.EqualTo(AppScreen.Playing));
            var resumed = D.Hero.Body.X;
            yield return WaitSeconds(.2f);
            Assert.That(D.Hero.Body.X, Is.EqualTo(resumed), "フォーカスを失う前のキーは持ち越さない");
        }

        [UnityTest]
        public IEnumerator SettingsAsset_MatchesTheCodeDefaults_AndTheAutopilotWins()
        {
            // 生成スクリプトはアセットを上書きしないので、コードの既定値を変えたらアセットも直す（024 の知見）。
            yield return null;
            Assert.That(JsonUtility.ToJson(_game.Settings.Rules), Is.EqualTo(JsonUtility.ToJson(new VaultConfig())), "設定アセットの試合の設定がコードの既定値と同じ");
            var dungeon = new Dungeon(_game.Settings.Rules, Rooms.Create(), 1);
            var pilot = new Autopilot();
            for (var i = 0; i < TickLimit && !dungeon.IsWon; i++)
            {
                if (dungeon.Status == DungeonStatus.Down) { dungeon.Continue(); }
                dungeon.Tick(pilot.InputFor(dungeon));
            }

            Assert.That(dungeon.IsWon, Is.True, "設定アセットの値で操縦役が勝てる");
            Assert.That(dungeon.Hash(), Is.EqualTo(0xFCD220F3u), "撮影の試合（シード 1）は App テストの固定値と同じ");
        }

        [Test]
        public void Settings_RejectInvalidValues()
        {
            AssertRejected(s => s.Volume = float.NaN);
            AssertRejected(s => s.Volume = 1.5f);
            AssertRejected(s => s.ToastTicks = 0);
            AssertRejected(s => s.Rules.HeroSpeed = 0);
            AssertRejected(s => s.Rules.RollInvulnTicks = 99);
            AssertRejected(s => s.Rules = null, typeof(ArgumentNullException));
        }

        [Test]
        public void FormatTicks_ShowsMinutesSecondsAndHundredths()
        {
            Assert.That(EmberVaultGame.FormatTicks(0), Is.EqualTo("0:00.00"));
            Assert.That(EmberVaultGame.FormatTicks(59), Is.EqualTo("0:00.98"));
            Assert.That(EmberVaultGame.FormatTicks((60 * 75) + 30), Is.EqualTo("1:15.50"));
        }

        private static void AssertRejected(Action<EmberVaultSettings> change, Type exception = null)
        {
            var settings = ScriptableObject.CreateInstance<EmberVaultSettings>();
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

        /// <summary>いちばん近い敵へ剣を振らずに歩き続ける操縦役（ゲームオーバーの場面を作る）。敵がいなければ止まる。</summary>
        private sealed class ChasePilot : IPilot
        {
            public HeroInput InputFor(Dungeon dungeon)
            {
                Enemy target = null;
                var best = long.MaxValue;
                var hero = dungeon.Hero.Body;
                foreach (var enemy in dungeon.Run.Enemies)
                {
                    if (!enemy.IsAlive) { continue; }
                    var distance = Math.Abs((long)enemy.Body.CenterX - hero.CenterX) + Math.Abs((long)Boxes.CenterY(enemy.Body) - Boxes.CenterY(hero));
                    if (distance < best)
                    {
                        best = distance;
                        target = enemy;
                    }
                }

                return target == null ? HeroInput.None : new HeroInput(Math.Sign(target.Body.CenterX - hero.CenterX), Math.Sign(Boxes.CenterY(target.Body) - Boxes.CenterY(hero)), false, false);
            }
        }
    }
}
