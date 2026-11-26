using System;
using System.Collections;
using Game.App;
using Game.Core;
using IntGeometry;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.Tilemaps;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace Game.Unity.Tests
{
    /// <summary>実シーンを読み込み、仮想のキーボードで操作する。</summary>
    public sealed class LanternSceneTests
    {
        private TestKeyboard _keyboard;
        private LanternGame _game;

        [UnitySetUp]
        public IEnumerator Load()
        {
            _keyboard = new TestKeyboard();
            yield return SceneManager.LoadSceneAsync("MireLantern");
            yield return null;
            _game = Object.FindAnyObjectByType<LanternGame>();
            for (int i = 0; i < 30 && _game.HudRoot == null; i++) { yield return null; }
            Assert.That(_game.HudRoot, Is.Not.Null);
        }

        [UnityTearDown]
        public IEnumerator Unload()
        {
            _keyboard.Dispose();
            yield return null;
        }

        private CrawlRun R => _game.Flow.Run;

        private LanternStage Stage => _game.Stage;

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
        }

        /// <summary>敵と品を除き、自機を 3×3 以上の部屋の中央に置く。</summary>
        private Vec2i QuietRoom()
        {
            R.ClearMonsters();
            for (int y = 0; y < R.Grid.Height; y++)
            {
                for (int x = 0; x < R.Grid.Width; x++) { R.SetItem(new Vec2i(x, y), null); }
            }

            foreach (var room in R.Grid.Rooms)
            {
                if (room.Width >= 5 && room.Height >= 3)
                {
                    R.Teleport(room.Center);
                    return room.Center;
                }
            }

            throw new InvalidOperationException("広い部屋がありません。");
        }

        private static IEnumerator WaitUntil(Func<bool> condition, float seconds = 5f)
        {
            float limit = Time.realtimeSinceStartup + seconds;
            while (!condition() && Time.realtimeSinceStartup < limit) { yield return null; }
            Assert.That(condition(), Is.True, "時間内に条件を満たさなかった");
        }

        private Vector3 PlayerLocal => Stage.Player.transform.localPosition;

        [UnityTest]
        public IEnumerator Scene_HasOneStageMatchingThePreview_AndShowsTheTitle()
        {
            yield return null;
            Assert.That(Object.FindObjectsByType<LanternStage>(FindObjectsInactive.Include), Has.Length.EqualTo(1));
            Assert.That(Stage.Mismatch(_game.Preview.Grid), Is.Null);
            Assert.That(Text("headline"), Is.EqualTo("MIRE LANTERN"));
            Assert.That(IsShown("stats"), Is.False, "タイトルでは数値を出さない");
            Assert.That(IsShown("log"), Is.False);
            Assert.That(Stage.VisibleMonsters, Is.EqualTo(0));
            Assert.That(PlayerLocal, Is.EqualTo(LanternStage.CellCenter(_game.Preview.Start, Stage.Height)));
        }

        [UnityTest]
        public IEnumerator SettingsAssetValidatesAndMatchesTheCodeDefaults()
        {
            yield return null;
            Assert.That(() => _game.Settings.Validate(), Throws.Nothing);
            Assert.That(JsonUtility.ToJson(_game.Settings.Rules), Is.EqualTo(JsonUtility.ToJson(new CrawlConfig())), "コードの既定値を変えたら .asset も直す（024）");
        }

        [UnityTest]
        public IEnumerator NumpadMovesThePlayerAndTheSpriteFollows()
        {
            yield return StartGame();
            var start = QuietRoom();
            yield return _keyboard.Tap(Key.Numpad6);
            Assert.That(R.Player.Position, Is.EqualTo(start + new Vec2i(1, 0)));
            yield return _keyboard.Tap(Key.Numpad1);
            Assert.That(R.Player.Position, Is.EqualTo(start + new Vec2i(0, 1)));
            yield return null;
            Assert.That(PlayerLocal, Is.EqualTo(LanternStage.CellCenter(R.Player.Position, Stage.Height)));
            Assert.That(Text("turns"), Does.StartWith("行動 2"));
        }

        [UnityTest]
        public IEnumerator ShiftArrowsMoveDiagonallyAndCtrlOnlyTurns()
        {
            yield return StartGame();
            var start = QuietRoom();
            _keyboard.Hold(Key.LeftShift);
            yield return null;
            _keyboard.Hold(Key.LeftShift, Key.UpArrow);
            yield return null;
            Assert.That(R.Player.Position, Is.EqualTo(start), "Shift と 1 キーでは動かない");
            _keyboard.Hold(Key.LeftShift, Key.UpArrow, Key.LeftArrow);
            yield return null;
            _keyboard.Hold();
            yield return null;
            Assert.That(R.Player.Position, Is.EqualTo(start + new Vec2i(-1, -1)));

            var actions = R.Player.Actions;
            _keyboard.Hold(Key.LeftCtrl);
            yield return null;
            _keyboard.Hold(Key.LeftCtrl, Key.Numpad3);
            yield return null;
            _keyboard.Hold();
            yield return null;
            Assert.That(R.Player.Facing, Is.EqualTo(Direction.SouthEast));
            Assert.That(R.Player.Actions, Is.EqualTo(actions), "向きを変えるだけ");
            Assert.That(Quaternion.Angle(Stage.Facing.transform.localRotation, LanternStage.FacingRotation(Direction.SouthEast)), Is.LessThan(.01f));
        }

        [UnityTest]
        public IEnumerator HoldingAnArrowWalksRepeatedlyAndReleasingStops()
        {
            yield return StartGame();
            QuietRoom();
            var room = R.Grid.Rooms[R.Grid.RoomAt(R.Player.Position)];
            R.Teleport(new Vec2i(room.X0, room.Center.Y));
            _keyboard.Hold(Key.RightArrow);
            yield return WaitUntil(() => R.Player.Actions >= 3);
            _keyboard.Hold();
            yield return null;
            var after = R.Player.Actions;
            yield return new WaitForSecondsRealtime(.5f);
            Assert.That(R.Player.Actions, Is.EqualTo(after), "離したら止まる");
        }

        [UnityTest]
        public IEnumerator BumpingAWallMovesNobody()
        {
            yield return StartGame();
            QuietRoom();
            var room = R.Grid.Rooms[R.Grid.RoomAt(R.Player.Position)];
            R.Teleport(new Vec2i(room.X0, room.Y0));
            var m = R.AddMonster(MonsterKind.Skitter, new Vec2i(room.X1, room.Y1));
            R.Refresh();
            var before = (R.Hash(), m.Position, R.Scheduler.Now);
            yield return _keyboard.Tap(Key.Numpad7);
            yield return _keyboard.Tap(Key.UpArrow);
            Assert.That((R.Hash(), m.Position, R.Scheduler.Now), Is.EqualTo(before));
            yield return _keyboard.Tap(Key.Space);
            Assert.That(m.Position, Is.Not.EqualTo(before.Position), "足踏みなら敵が動く");
        }

        [UnityTest]
        public IEnumerator MonstersOutsideTheViewAreNotDrawnAndTerrainIsRemembered()
        {
            yield return StartGame();
            var home = QuietRoom();
            Vec2i far = default;
            foreach (var room in R.Grid.Rooms)
            {
                var c = room.Center;
                var seen = new bool[R.Grid.Width * R.Grid.Height];
                Fov.Compute(R.Grid, c, R.Config.ViewRadius, seen);
                if (!seen[R.Grid.Index(home)] && !R.IsVisible(c)) { far = c; }
            }

            var golem = R.AddMonster(MonsterKind.Golem, far);
            golem.SleepTurns = 1000;
            R.Refresh();
            yield return null;
            Assert.That(Stage.VisibleMonsters, Is.EqualTo(0));
            Assert.That(Stage.Terrain.GetTile(LanternStage.Cell(far, Stage.Height)), Is.Null, "見ていない地形は描かない");

            // 近づくと描かれ、離れると消える（地形は記憶の色で残る）。一度出してから消えることを確かめる（038）。
            R.Teleport(far + new Vec2i(1, 0) == far ? far : NeighborOf(far));
            yield return null;
            Assert.That(Stage.VisibleMonsters, Is.EqualTo(1));
            Assert.That(Stage.MonsterRenderers[0].enabled, Is.True);
            Assert.That(Stage.MonsterRenderers[0].sprite, Is.EqualTo(Stage.MonsterSprite(MonsterKind.Golem)));
            R.Teleport(home);
            yield return null;
            Assert.That(Stage.VisibleMonsters, Is.EqualTo(0));
            Assert.That(Stage.MonsterRenderers[0].enabled, Is.False);
            var cell = LanternStage.Cell(far, Stage.Height);
            Assert.That(Stage.Terrain.GetTile(cell), Is.Not.Null);
            Assert.That(Stage.Terrain.GetColor(cell), Is.EqualTo(LanternStage.Remembered));
        }

        private Vec2i NeighborOf(Vec2i p)
        {
            for (int d = 0; d < Directions.Count; d++)
            {
                var q = p + ((Direction)d).Offset();
                if (R.Grid.IsWalkable(q) && R.MonsterAt(q) == null) { return q; }
            }

            throw new InvalidOperationException("隣が空いていません。");
        }

        [UnityTest]
        public IEnumerator InventoryKeysThrowTheSelectedItemTheWayThePlayerFaces()
        {
            yield return StartGame();
            var start = QuietRoom();
            var golem = R.AddMonster(MonsterKind.Golem, start + new Vec2i(2, 0));
            golem.SleepTurns = 100;
            R.Player.Inventory.Add(ItemKind.Bread);
            R.Player.Inventory.Add(ItemKind.Stone);
            R.Refresh();
            _keyboard.Hold(Key.LeftCtrl);
            yield return null;
            _keyboard.Hold(Key.LeftCtrl, Key.RightArrow);
            yield return null;
            _keyboard.Hold();
            yield return null;
            yield return _keyboard.Tap(Key.I);
            Assert.That(_game.Flow.Screen, Is.EqualTo(AppScreen.Inventory));
            Assert.That(IsShown("inventory"), Is.True);
            yield return _keyboard.Tap(Key.DownArrow);
            Assert.That(_game.Flow.Selected, Is.EqualTo(1));
            Assert.That(Text("detail-name"), Is.EqualTo("石"));
            yield return _keyboard.Tap(Key.T);
            Assert.That(_game.Flow.Screen, Is.EqualTo(AppScreen.Playing), "T は持ち物の画面では投げる（タイトルへは戻らない）");
            Assert.That(golem.Hp, Is.EqualTo(golem.MaxHp - R.Config.StoneDamage));
            Assert.That(R.Player.Inventory, Is.EqualTo(new[] { ItemKind.Bread }));
            Assert.That(Root.Q("lines").Children(), Has.Some.Matches<Label>(l => l.text.Contains("石が岩人形に当たった", StringComparison.Ordinal)));
        }

        [UnityTest]
        public IEnumerator BackToTitleLooksLikeStartup()
        {
            yield return null;
            var startupTiles = Snapshot();
            var startupPlayer = PlayerLocal;
            yield return StartGame();
            for (int i = 0; i < 10; i++) { yield return _keyboard.Tap(Key.Space); }
            yield return _keyboard.Tap(Key.Escape);
            Assert.That(_game.Flow.Screen, Is.EqualTo(AppScreen.Paused));
            yield return _keyboard.Tap(Key.T);
            Assert.That(_game.Flow.Screen, Is.EqualTo(AppScreen.Title));
            yield return null;
            Assert.That(Snapshot(), Is.EqualTo(startupTiles));
            Assert.That(PlayerLocal, Is.EqualTo(startupPlayer));
            Assert.That(Stage.VisibleMonsters + Stage.VisibleItems + Stage.VisibleHits, Is.EqualTo(0));
            Assert.That(IsShown("stats"), Is.False);
        }

        private string Snapshot()
        {
            var sb = new System.Text.StringBuilder();
            for (int y = 0; y < Stage.Height; y++)
            {
                for (int x = 0; x < Stage.Width; x++)
                {
                    var cell = new Vector3Int(x, y, 0);
                    var tile = Stage.Terrain.GetTile(cell);
                    sb.Append(tile == null ? "." : tile.name).Append(Stage.Terrain.GetColor(cell).r.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)).Append(',');
                }
            }

            return sb.ToString();
        }

        [UnityTest]
        public IEnumerator DyingShowsTheResultAndEnterReturnsToTitle()
        {
            yield return StartGame();
            R.Player.Hp = 1;
            R.Player.Satiety = 0;
            yield return _keyboard.Tap(Key.Space);
            Assert.That(_game.Flow.Screen, Is.EqualTo(AppScreen.Result));
            yield return null;
            Assert.That(Text("headline"), Is.EqualTo("力尽きた……"));
            Assert.That(Text("message"), Does.Contain("空腹"));
            Assert.That(_game.LastSound, Is.EqualTo("Died"));
            yield return _keyboard.Tap(Key.Enter);
            Assert.That(_game.Flow.Screen, Is.EqualTo(AppScreen.Title));
        }

        [UnityTest]
        public IEnumerator AutopilotFinishesARunInTheRealSceneAndDrawingNeverLeaks()
        {
            yield return StartGame();
            _game.Flow.Pilot = new Autopilot(_game.Rules);
            var step = new System.Collections.Generic.List<double>();
            var draw = new System.Collections.Generic.List<double>();
            var watch = new System.Diagnostics.Stopwatch();
            int maxMonsters = 0;
            int maxItems = 0;
            for (int i = 0; i < 20_000 && _game.Flow.Screen == AppScreen.Playing; i++)
            {
                watch.Restart();
                _game.Flow.StepPilot();
                watch.Stop();
                step.Add(watch.Elapsed.TotalMilliseconds);
                _game.ConsumeEvents();
                watch.Restart();
                _game.Render();
                watch.Stop();
                draw.Add(watch.Elapsed.TotalMilliseconds);
                maxMonsters = Math.Max(maxMonsters, Stage.VisibleMonsters);
                maxItems = Math.Max(maxItems, Stage.VisibleItems);
                if (i % 25 == 0) { AssertDrawingMatchesTheView(); }
                if (i % 200 == 0) { yield return null; }
            }

            Assert.That(_game.Flow.Screen, Is.EqualTo(AppScreen.Result), "操縦役が決着まで進む");
            yield return null;
            AssertDrawingMatchesTheView();
            step.Sort();
            draw.Sort();
            double P(System.Collections.Generic.List<double> list, double q) => list[Math.Min(list.Count - 1, (int)(list.Count * q))];
            Debug.Log($"[Perf] 操作 {step.Count} 回：Submit p50 {P(step, .5):0.000} ms / p95 {P(step, .95):0.000} ms / 最大 {step[step.Count - 1]:0.000} ms、" +
                $"描画 p50 {P(draw, .5):0.000} ms / p95 {P(draw, .95):0.000} ms / 最大 {draw[draw.Count - 1]:0.000} ms、最多の敵 {maxMonsters}・品 {maxItems}、描画物 {Stage.PooledRenderers}");
            Assert.That(P(step, .95), Is.LessThan(5), "1 操作（敵の行動を含む）の p95");
            Assert.That(P(draw, .95), Is.LessThan(5), "描画の更新の p95");
        }

        /// <summary>描いている敵と品が、窓口の視界・記憶と一致する（視界外の敵を描かない）。</summary>
        private void AssertDrawingMatchesTheView()
        {
            var view = _game.Flow.View;
            int enabled = 0;
            foreach (var r in Stage.MonsterRenderers)
            {
                if (!r.enabled) { continue; }
                enabled++;
                var local = r.transform.localPosition;
                var p = new Vec2i(Mathf.FloorToInt(local.x), Stage.Height - 1 - Mathf.FloorToInt(local.y));
                Assert.That(view.IsVisible(p), Is.True, $"見えないマス {p} に敵を描いた");
                Assert.That(view.MonsterAt(p).HasValue, Is.True);
            }

            Assert.That(enabled, Is.EqualTo(view.Monsters.Count));
            foreach (var m in R.Monsters)
            {
                if (!R.IsVisible(m.Position)) { Assert.That(view.MonsterAt(m.Position), Is.Null); }
            }

            for (int y = 0; y < Stage.Height; y++)
            {
                for (int x = 0; x < Stage.Width; x++)
                {
                    var p = new Vec2i(x, y);
                    var tile = Stage.Terrain.GetTile(LanternStage.Cell(p, Stage.Height));
                    Assert.That(tile != null, Is.EqualTo(view.IsKnown(p)), $"既知でないマス {p} を描いた／既知のマスを描かない");
                }
            }
        }
    }
}
