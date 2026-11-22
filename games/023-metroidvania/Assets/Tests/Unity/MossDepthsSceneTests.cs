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
using PlayerInput = Game.Core.PlayerInput;
using Object = UnityEngine.Object;

namespace Game.Unity.Tests
{
    public sealed class MossDepthsSceneTests
    {
        private const int TickLimit = 60 * 60 * 5;

        private TestKeyboard _keyboard;
        private Mouse _mouse;
        private MossDepthsGame _game;

        [UnitySetUp]
        public IEnumerator Load()
        {
            _keyboard = new TestKeyboard();
            _mouse = InputSystem.AddDevice<Mouse>();
            yield return SceneManager.LoadSceneAsync("MossDepths");
            yield return null;
            _game = Object.FindAnyObjectByType<MossDepthsGame>();
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

        private Expedition E => _game.Flow.Expedition;

        /// <summary>塔のひびの壁の表示が、世界の状態（壊れているか）と全マスで一致するか。壊れて描かれていないマスの数を返す。</summary>
        private int AssertTowerCracksMatch(WorldState state)
        {
            var stage = _game.StageAt(Rooms.Tower);
            var map = Rooms.Create().Rooms[Rooms.Tower].Map;
            var hidden = 0;
            for (var row = 0; row < map.Height; row++)
            {
                for (var column = 0; column < map.Width; column++)
                {
                    if (map[column, row] != TileKind.Cracked) { continue; }
                    var broken = state != null && state.IsBroken(Rooms.Tower, column, row, map);
                    Assert.That(stage.Cracked.HasTile(new Vector3Int(column, row, 0)), Is.EqualTo(!broken), $"ひびの壁 ({column}, {row})");
                    if (broken) { hidden++; }
                }
            }

            return hidden;
        }

        private string Text(string name) => _game.HudRoot.Q<Label>(name).text;

        private bool IsShown(string name) => _game.HudRoot.Q<Button>(name).style.display.value == DisplayStyle.Flex;

        private IEnumerator StartGame()
        {
            yield return _keyboard.Tap(Key.Enter);
            Assert.That(_game.Flow.Screen, Is.EqualTo(AppScreen.Playing));

            // 開始の Tick より前に押したジャンプ・ショットは「押したままのキー」として扱われるので、1 Tick 進むのを待つ。
            yield return WaitUntil(() => E.ElapsedTicks > 0);
        }

        /// <summary>時計を止め（timeScale 0）、操縦役を付けてから Enter で始める（入力の列を開始の Tick から合わせる）。</summary>
        private IEnumerator StartPiloted(IPilot pilot)
        {
            Time.timeScale = 0;
            _game.Flow.Pilot = pilot;
            yield return _keyboard.Tap(Key.Enter);
            Assert.That(_game.Flow.Screen, Is.EqualTo(AppScreen.Playing));
            Assert.That(E.ElapsedTicks, Is.Zero);
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

        [UnityTest]
        public IEnumerator Stages_MatchCoreRoomsAndOnlyTheHubIsShownAtTitle()
        {
            yield return null;
            var world = Rooms.Create();
            Assert.That(Object.FindObjectsByType<MossDepthsStage>(FindObjectsInactive.Include), Has.Length.EqualTo(world.Rooms.Count), "Stage のインスタンスは部屋ごとに 1 つ");
            for (var i = 0; i < world.Rooms.Count; i++)
            {
                var stage = _game.StageAt(i);
                Assert.That(stage.RoomIndex, Is.EqualTo(i));
                Assert.That(stage.Mismatch(world.Rooms[i]), Is.Null, world.Rooms[i].Name);
                Assert.That(stage.gameObject.activeSelf, Is.EqualTo(i == world.StartRoom));
            }

            Assert.That(Text("headline"), Is.EqualTo("MOSSLIGHT DEPTHS"));
            Assert.That(IsShown("submit") && IsShown("quit"), Is.True);
            Assert.That(_game.HudRoot.Q("stats").style.display.value, Is.EqualTo(DisplayStyle.None));
        }

        [UnityTest]
        public IEnumerator StageMismatch_DetectsMovedOrChangedObjects()
        {
            // レビュー P2-1：照合が物の数しか見ず、玉を 1 マス動かした地図でも Prefab をそのまま使っていた。
            yield return null;
            Assert.That(_game.StageAt(Rooms.Cave).Mismatch(Variant(Rooms.Cave, rows => rows)), Is.Null, "元の地図とは一致する");
            Assert.That(_game.StageAt(Rooms.Cave).Mismatch(Variant(Rooms.Cave, rows => Move(rows, 'J', -1))), Does.Contain("アイテム").And.Contain("位置"), "玉を 1 マス動かす");
            Assert.That(_game.StageAt(Rooms.Cave).Mismatch(Variant(Rooms.Cave, rows => Swap(rows, 'J', 'K'))), Does.Contain("絵"), "玉の種類を変える");
            Assert.That(_game.StageAt(Rooms.Corridor).Mismatch(Variant(Rooms.Corridor, rows => Move(rows, 'w', -1))), Does.Contain("歩く敵"), "敵を 1 マス動かす");
            Assert.That(_game.StageAt(Rooms.Hub).Mismatch(Variant(Rooms.Hub, rows => Move(rows, '$', 1))), Does.Contain("セーブ像"), "像を 1 マス動かす");
            Assert.That(_game.StageAt(Rooms.Lair).Mismatch(Variant(Rooms.Lair, rows => Move(rows, 'M', -1))), Does.Contain("ボス"), "ボスを 1 マス動かす");
        }

        /// <summary>本番の部屋の文字の地図を変えて解析し直す。</summary>
        private static Room Variant(int index, Func<string[], string[]> change)
        {
            var (name, x, y, rows) = Rooms.Source(index);
            return Room.Parse(name, x, y, change(rows));
        }

        /// <summary>最初に見つかった記号を横に <paramref name="dx"/> マス動かす（動かした先は空きマスであること）。</summary>
        private static string[] Move(string[] rows, char symbol, int dx)
        {
            for (var line = 0; line < rows.Length; line++)
            {
                var column = rows[line].IndexOf(symbol);
                if (column < 0) { continue; }
                var chars = rows[line].ToCharArray();
                Assert.That(chars[column + dx], Is.EqualTo('.'), "動かした先は空きマス");
                chars[column] = '.';
                chars[column + dx] = symbol;
                rows[line] = new string(chars);
                return rows;
            }

            throw new InvalidOperationException($"'{symbol}' がありません。");
        }

        private static string[] Swap(string[] rows, char from, char to)
        {
            for (var line = 0; line < rows.Length; line++)
            {
                if (rows[line].IndexOf(from) >= 0)
                {
                    rows[line] = rows[line].Replace(from, to);
                    return rows;
                }
            }

            throw new InvalidOperationException($"'{from}' がありません。");
        }

        [UnityTest]
        public IEnumerator Camera_ShowsTheWholeRoomBelowTheHudBand()
        {
            yield return null;
            var camera = _game.ViewCamera;
            var stage = _game.StageAt(Rooms.Hub);
            var bottomLeft = camera.WorldToViewportPoint(stage.transform.TransformPoint(Vector3.zero));
            var topRight = camera.WorldToViewportPoint(stage.transform.TransformPoint(new Vector3(Room.Columns, Room.Rows + MossDepthsGame.HudBandTiles, 0)));
            Assert.That(bottomLeft.x, Is.GreaterThanOrEqualTo(-1e-4f));
            Assert.That(bottomLeft.y, Is.GreaterThanOrEqualTo(-1e-4f));
            Assert.That(topRight.x, Is.LessThanOrEqualTo(1 + 1e-4f));
            Assert.That(topRight.y, Is.LessThanOrEqualTo(1 + 1e-4f));
        }

        [UnityTest]
        public IEnumerator HoldingRightKey_MovesPlayerAndReleaseStops()
        {
            yield return StartGame();
            var x = E.Player.Body.X;
            _keyboard.Hold(Key.RightArrow);
            yield return WaitUntil(() => E.Player.Body.X > x + Units.Tile);
            _keyboard.Hold();
            yield return WaitUntil(() => E.Player.VelocityX == 0 && E.Player.Grounded);
            var stopped = E.Player.Body.X;
            yield return WaitSeconds(.2f);
            Assert.That(E.Player.Body.X, Is.EqualTo(stopped), "離した後は止まったまま");
            var stage = _game.CurrentStage;
            Assert.That(stage.Player.transform.position, Is.EqualTo(stage.World(E.Player.Body.CenterX, E.Player.Body.Bottom)), "自機の描画は体の下端の中央");
        }

        [UnityTest]
        public IEnumerator JumpKey_LeavesTheGround_HoldingDoesNotJumpAgain()
        {
            yield return StartGame();
            var y = E.Player.Body.Y;
            _keyboard.Hold(Key.Space);
            yield return WaitUntil(() => E.Player.Body.Y > y + Units.Tile);
            yield return WaitUntil(() => E.Player.Grounded);
            yield return WaitSeconds(.3f);
            Assert.That(E.Player.Grounded, Is.True, "押したまま着地しても跳び直さない");
            _keyboard.Hold();
        }

        [UnityTest]
        public IEnumerator DoubleJumpKey_WorksOnlyAfterTheAbility()
        {
            yield return StartGame();

            // 能力がなければ、空中で押し直しても跳ばない。
            _keyboard.Hold(Key.Space);
            yield return WaitUntil(() => !E.Player.Grounded && E.Player.VelocityY <= 0);
            _keyboard.Hold();
            yield return WaitUntil(() => !E.Player.JumpHeld);
            _keyboard.Hold(Key.Space);
            yield return WaitUntil(() => E.Player.Grounded);
            Assert.That(E.Player.AirJumpUsed, Is.False);
            _keyboard.Hold();
            yield return WaitSeconds(.1f);

            E.State.Grant(Ability.DoubleJump);
            _keyboard.Hold(Key.Space);
            yield return WaitUntil(() => !E.Player.Grounded && E.Player.VelocityY <= 0);
            _keyboard.Hold();
            yield return WaitUntil(() => !E.Player.JumpHeld);
            _keyboard.Hold(Key.Z);
            yield return WaitUntil(() => E.Player.AirJumpUsed);
            Assert.That(E.Player.VelocityY, Is.GreaterThan(0), "空中でもう一度跳んだ");
            _keyboard.Hold();
        }

        [UnityTest]
        public IEnumerator ShootKey_FiresOncePerPress_AndTheShotIsDrawn()
        {
            yield return StartGame();
            _keyboard.Hold(Key.X);
            yield return WaitUntil(() => E.Run.Shots.Count == 1);
            yield return null;
            Assert.That(_game.CurrentStage.VisibleShots, Is.EqualTo(E.Run.Shots.Count), "弾が描かれる");
            var shot = _game.CurrentStage.Shots[0];
            Assert.That(shot.transform.position.x, Is.GreaterThan(_game.CurrentStage.Player.transform.position.x - .5f));
            yield return WaitUntil(() => E.Run.Shots.Count == 0);
            yield return WaitSeconds(.3f);
            Assert.That(E.Run.Shots, Is.Empty, "押したままでは撃ち直さない");
            _keyboard.Hold();
        }

        [UnityTest]
        public IEnumerator Pilot_ChangesRooms_StagesMapAndAbilityToast()
        {
            yield return StartPiloted(new RoutePilot(Routes.All()));
            StepUntil(() => E.RoomIndex == Rooms.Corridor);
            Assert.That(_game.CurrentStage.RoomIndex, Is.EqualTo(Rooms.Corridor));
            Assert.That(_game.StageAt(Rooms.Hub).gameObject.activeSelf, Is.False);
            Assert.That(Text("room"), Is.EqualTo("回廊"));
            Assert.That(_game.HudRoot.Q("minimap").Query(className: "map-room").ToList(), Has.Count.EqualTo(2), "訪れた 2 部屋だけを描く");
            Assert.That(_game.HudRoot.Q("minimap").Query(className: "map-current").ToList(), Has.Count.EqualTo(1));

            StepUntil(() => E.State.Has(Ability.DoubleJump));
            Assert.That(_game.Toast, Does.Contain("二段ジャンプ"));
            Assert.That(_game.HudRoot.Q<Label>("toast").style.display.value, Is.EqualTo(DisplayStyle.Flex));
            Assert.That(_game.HudRoot.Q<Label>("ability-jump").ClassListContains("owned"), Is.True);
            Assert.That(_game.HudRoot.Q<Label>("ability-break").ClassListContains("owned"), Is.False);
            var orb = TestKit.PickupIndex(E.Run.Room, PickupKind.DoubleJump);
            Assert.That(_game.CurrentStage.Pickups[orb].enabled, Is.False, "取った玉は消える");
            StepUntil(() => _game.Toast.Length == 0);
            Assert.That(E.ElapsedTicks, Is.GreaterThanOrEqualTo(_game.Settings.ToastTicks), "案内は Tick で消える");
        }

        [UnityTest]
        public IEnumerator CrackedWall_DisappearsWhenBroken_StaysAfterReentry_ReturnsAfterContinue()
        {
            yield return StartPiloted(new RoutePilot(Routes.All(), new TestKit.HoldPilot(new PlayerInput(1, false, false))));
            var tower = _game.StageAt(Rooms.Tower);
            StepUntil(() => E.RoomIndex == Rooms.Tower);
            Assert.That(AssertTowerCracksMatch(E.State), Is.Zero);
            StepUntil(() => E.State.BrokenCount > 0);
            Assert.That(AssertTowerCracksMatch(E.State), Is.GreaterThan(0), "壊れたひびの壁は消える");
            var broken = E.State.BrokenCount;

            // ボスの間へ入り、左へ戻る。壊れた壁はそのまま。
            StepUntil(() => E.RoomIndex == Rooms.Lair);
            _game.Flow.Pilot = new TestKit.HoldPilot(new PlayerInput(-1, false, false));
            StepUntil(() => E.RoomIndex == Rooms.Tower);
            Assert.That(AssertTowerCracksMatch(E.State), Is.EqualTo(broken), "入り直しても壊れたまま");
            Assert.That(E.State.IsVisited(Rooms.Lair), Is.True);

            // ボスの間でボスへ歩き続けて倒れる。塔の像（壁を壊す前のセーブ）から再開すると壁は戻り、破壊ショットは残る。
            _game.Flow.Pilot = new TestKit.HoldPilot(new PlayerInput(1, false, false));
            StepUntil(() => _game.Flow.Screen == AppScreen.GameOver);
            Assert.That(Text("headline"), Is.EqualTo("力尽きた…"));
            Assert.That(Text("message"), Does.Contain("塔"));
            Assert.That(IsShown("submit") && IsShown("restart") && IsShown("title"), Is.True);
            Time.timeScale = 1;
            _game.Flow.Pilot = null;
            yield return _keyboard.Tap(Key.Enter);
            Assert.That(_game.Flow.Screen, Is.EqualTo(AppScreen.Playing));
            yield return null;
            Assert.That(E.RoomIndex, Is.EqualTo(Rooms.Tower));
            Assert.That(AssertTowerCracksMatch(E.State), Is.Zero, "セーブの後に壊した壁は戻って描かれる");
            Assert.That(E.State.Has(Ability.BreakShot), Is.True);
            var orb = TestKit.PickupIndex(E.Run.Room, PickupKind.BreakShot);
            Assert.That(tower.Pickups[orb].enabled, Is.False, "セーブの前に取った玉は消えたまま");
            Assert.That(Text("deaths"), Is.EqualTo("力尽きた  1"));
        }

        [UnityTest]
        public IEnumerator Pilot_BeatsTheBoss_AndShowsTheResult()
        {
            yield return StartPiloted(Routes.Pilot());
            StepUntil(() => E.RoomIndex == Rooms.Lair && E.Run.Bullets.Count > 0);
            Assert.That(_game.CurrentStage.VisibleBullets, Is.EqualTo(E.Run.Bullets.Count), "ボスの弾が描かれる");
            Assert.That(_game.CurrentStage.Boss.enabled, Is.True);
            StepUntil(() => _game.Flow.Screen == AppScreen.Victory);
            Assert.That(Text("headline"), Is.EqualTo("塔の主を倒した！"));
            Assert.That(Text("message"), Does.Contain(MossDepthsGame.FormatTicks(_game.Flow.ClearTicks!.Value)).And.Contain("力尽きた回数  0"));
            Assert.That(_game.CurrentStage.Boss.enabled, Is.False, "倒したボスは消える");
            Assert.That(IsShown("submit") && IsShown("title"), Is.True);
        }

        [UnityTest]
        public IEnumerator BackToTitle_RestoresTheStartupView()
        {
            yield return null;
            var hub = _game.StageAt(Rooms.Hub);
            var tower = _game.StageAt(Rooms.Tower);
            var initial = (hub.Player.transform.position, hub.Player.sprite, hub.Player.flipX, hub.Player.enabled);

            yield return StartPiloted(new RoutePilot(Routes.All()));
            StepUntil(() => E.State.BrokenCount > 0);
            Time.timeScale = 1;
            _game.Flow.Pilot = null;
            yield return _keyboard.Tap(Key.Escape);
            yield return _keyboard.Tap(Key.T);
            Assert.That(_game.Flow.Screen, Is.EqualTo(AppScreen.Title));
            yield return null;
            Assert.That(hub.gameObject.activeSelf, Is.True);
            Assert.That(tower.gameObject.activeSelf, Is.False);
            Assert.That((hub.Player.transform.position, hub.Player.sprite, hub.Player.flipX, hub.Player.enabled), Is.EqualTo(initial), "起動直後と同じ表示");

            // 新しい試合で塔に入ると、壊した壁は描かれ直している。
            yield return StartPiloted(new RoutePilot(Routes.All()));
            Assert.That(E.State.BrokenCount, Is.Zero);
            StepUntil(() => E.RoomIndex == Rooms.Tower);
            Assert.That(AssertTowerCracksMatch(E.State), Is.Zero);
        }

        [UnityTest]
        public IEnumerator PauseKey_StopsTimeAndShowsTheBigMap()
        {
            yield return StartGame();
            yield return _keyboard.Tap(Key.Escape);
            Assert.That(_game.Flow.Screen, Is.EqualTo(AppScreen.Paused));
            var ticks = E.ElapsedTicks;
            yield return WaitSeconds(.2f);
            Assert.That(E.ElapsedTicks, Is.EqualTo(ticks));
            Assert.That(Text("headline"), Is.EqualTo("地図"));
            var bigmap = _game.HudRoot.Q("bigmap");
            Assert.That(bigmap.style.display.value, Is.EqualTo(DisplayStyle.Flex));
            Assert.That(bigmap.Query(className: "map-room").ToList(), Has.Count.EqualTo(1), "まだ拠点だけ");
            Assert.That(IsShown("restart") && IsShown("title"), Is.True);
            yield return _keyboard.Tap(Key.M);
            Assert.That(_game.Flow.Screen, Is.EqualTo(AppScreen.Playing));
            Assert.That(_game.HudRoot.Q("panel").style.display.value, Is.EqualTo(DisplayStyle.None));
        }

        [UnityTest]
        public IEnumerator HudShowsHealthAndHurtBlinks()
        {
            yield return StartGame();
            Time.timeScale = 0;
            E.Player.Hurt(2, 1);
            _game.Flow.StepTick();
            _game.ConsumeEvents();
            _game.Render();
            yield return null;
            Assert.That(Text("health"), Is.EqualTo("♥♥♥♡♡"));
            Assert.That(_game.CurrentStage.Player.sprite, Is.SameAs(_game.CurrentStage.HurtSprite), "押し戻しの間は被弾の姿");
            var shown = 0;
            var hidden = 0;
            for (var i = 0; i < 40; i++)
            {
                _game.Flow.StepTick();
                _game.Render();
                if (_game.CurrentStage.Player.enabled) { shown++; } else { hidden++; }
            }

            Assert.That(shown > 0 && hidden > 0, Is.True, "無敵の間は点滅する");
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
            Assert.That(E.ElapsedTicks, Is.LessThan(30));
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
            _keyboard.Hold(Key.RightArrow);
            yield return WaitUntil(() => E.Player.VelocityX > 0);
            _game.SendMessage("OnApplicationFocus", false);
            Assert.That(_game.Flow.Screen, Is.EqualTo(AppScreen.Paused));
            _keyboard.Hold();
            yield return null;
            _game.SendMessage("OnApplicationFocus", true);
            yield return null;
            yield return null;
            yield return _keyboard.Tap(Key.Enter);
            Assert.That(_game.Flow.Screen, Is.EqualTo(AppScreen.Playing));
            yield return WaitUntil(() => E.Player.VelocityX == 0);
        }

        [UnityTest]
        public IEnumerator SettingsAsset_RoutesStillBeatTheGame()
        {
            // 設定アセットを調整したら、撮影とテストの入力の列が合わなくなっていないかをここで知らせる。
            yield return null;
            var expedition = new Expedition(_game.Settings.Rules, Rooms.Create());
            var pilot = Routes.Pilot();
            for (var i = 0; i < TickLimit && expedition.Status == ExpeditionStatus.Playing; i++) { expedition.Tick(pilot.InputFor(expedition)); }
            Assert.That(expedition.IsWon && expedition.Deaths == 0, Is.True, "設定アセットの値では Routes が通らない。App テストの RouteFinder で作り直す");
        }

        [Test]
        public void Settings_RejectInvalidValues()
        {
            AssertRejected(s => s.Volume = float.NaN);
            AssertRejected(s => s.Volume = 1.5f);
            AssertRejected(s => s.ToastTicks = 0);
            AssertRejected(s => s.Rules.Gravity = 0);
            AssertRejected(s => s.Rules.JumpSpeed = 120, typeof(ArgumentException));
            AssertRejected(s => s.Rules = null, typeof(ArgumentNullException));
        }

        [Test]
        public void FormatTicks_ShowsMinutesSecondsAndHundredths()
        {
            Assert.That(MossDepthsGame.FormatTicks(0), Is.EqualTo("0:00.00"));
            Assert.That(MossDepthsGame.FormatTicks(59), Is.EqualTo("0:00.98"));
            Assert.That(MossDepthsGame.FormatTicks(60 * 75 + 30), Is.EqualTo("1:15.50"));
        }

        private static void AssertRejected(Action<MossDepthsSettings> change, Type exception = null)
        {
            var settings = ScriptableObject.CreateInstance<MossDepthsSettings>();
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

    internal static class TestKit
    {
        public static int PickupIndex(Room room, PickupKind kind)
        {
            for (var i = 0; i < room.Pickups.Count; i++)
            {
                if (room.Pickups[i].Kind == kind) { return i; }
            }

            throw new InvalidOperationException($"{room.Name} に {kind} はありません。");
        }

        /// <summary>同じ入力を押し続ける操縦役。</summary>
        public sealed class HoldPilot : IPilot
        {
            private readonly PlayerInput _input;

            public HoldPilot(PlayerInput input) => _input = input;

            public PlayerInput InputFor(Expedition expedition) => _input;
        }
    }
}
