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
using UnityEngine.Tilemaps;
using UnityEngine.UIElements;
using MouseButton = UnityEngine.InputSystem.LowLevel.MouseButton;
using Object = UnityEngine.Object;

namespace Game.Unity.Tests
{
    public sealed class HushWardSceneTests
    {
        private const int TickLimit = 60 * 60 * 5;

        private TestKeyboard _keyboard;
        private Mouse _mouse;
        private HushWardGame _game;

        [UnitySetUp]
        public IEnumerator Load()
        {
            _keyboard = new TestKeyboard();
            _mouse = InputSystem.AddDevice<Mouse>();
            yield return SceneManager.LoadSceneAsync("HushWard");
            yield return null;
            _game = Object.FindAnyObjectByType<HushWardGame>();
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

        private Infiltration M => _game.Flow.Match;

        private HushWardStage Stage => _game.Stage;

        private string Text(string name) => _game.HudRoot.Q<Label>(name).text;

        private bool IsShown(string name) => _game.HudRoot.Q<Button>(name).style.display.value == DisplayStyle.Flex;

        private IEnumerator StartGame()
        {
            yield return _keyboard.Tap(Key.Enter);
            Assert.That(_game.Flow.Screen, Is.EqualTo(AppScreen.Playing));
            yield return WaitUntil(() => M.Ticks > 0);
        }

        /// <summary>時計を止め（timeScale 0）、操縦役を付けてから Enter で始める。</summary>
        private IEnumerator StartPiloted(IPilot pilot)
        {
            Time.timeScale = 0;
            _game.Flow.Pilot = pilot;
            yield return _keyboard.Tap(Key.Enter);
            Assert.That(_game.Flow.Screen, Is.EqualTo(AppScreen.Playing));
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

        [UnityTest]
        public IEnumerator Stage_MatchesTheFacility_AndTheTitleExplainsTheControls()
        {
            yield return null;
            Assert.That(Object.FindObjectsByType<HushWardStage>(FindObjectsInactive.Include), Has.Length.EqualTo(1), "Stage のインスタンスは 1 つ");
            Assert.That(Stage.Mismatch(Facility.Ward()), Is.Null);
            Assert.That(Text("headline"), Is.EqualTo("HUSH WARD"));
            Assert.That(Text("message"), Does.Contain("Shift：走る").And.Contain("C：忍び足").And.Contain("F1"), "タイトルに操作説明");
            Assert.That(IsShown("submit") && IsShown("quit"), Is.True);
            Assert.That(_game.HudRoot.Q("stats").style.display.value, Is.EqualTo(DisplayStyle.None));
            foreach (var guard in Stage.Guards) { Assert.That(guard.ConeMesh.vertexCount, Is.EqualTo(_game.Settings.FanRays + 1), "タイトルでも視界の扇を描く"); }
        }

        [UnityTest]
        public IEnumerator StageMismatch_DetectsMovedOrChangedObjects()
        {
            yield return null;
            var rows = new string[Facility.WardRows.Count];
            for (var i = 0; i < rows.Length; i++) { rows[i] = Facility.WardRows[i]; }
            var ward = Facility.Ward();
            Assert.That(Stage.Mismatch(Rebuild(rows, ward.Guards)), Is.Null, "元の施設とは一致する");
            Assert.That(Stage.Mismatch(Rebuild(Replace(rows, 28, 15, '.', 27, 15, 'O'), ward.Guards)), Does.Contain("目的物"), "目的物を 1 マス動かす");
            Assert.That(Stage.Mismatch(Rebuild(Replace(rows, 3, 2, '.', 3, 2, 'X'), ward.Guards)), Does.Contain("木箱"), "木箱を足す");
            Assert.That(Stage.Mismatch(Rebuild(Replace(rows, 1, 1, '.', 2, 1, 'E'), ward.Guards)), Does.Contain("床").Or.Contain("自機"), "入口を動かす");
            var moved = new[] { new GuardPlan("A", Trig.Right, new Waypoint(new Cell(3, 8), 90, Trig.Right)), ward.Guards[1], ward.Guards[2] };
            Assert.That(Stage.Mismatch(Rebuild(rows, moved)), Does.Contain("警備員 A"), "警備員の始めの位置を動かす");
            Assert.That(Stage.Mismatch(Rebuild(rows, new[] { ward.Guards[0], ward.Guards[1] })), Does.Contain("警備員の数"), "警備員を減らす");
        }

        [UnityTest]
        public IEnumerator StageMismatch_ReportsMissingPartsAndWrongTileKinds()
        {
            // 実際の Stage を複製し、描画に要る部品を 1 つずつ壊す。照合は例外ではなく具体的な理由を返す。
            yield return null;
            var ward = Facility.Ward();
            var cases = new (string Label, Action<HushWardStage> Break, string Expected)[]
            {
                ("自機の上向きの絵を空にする", stage => SetField(stage, "_intruderUp", Array.Empty<Sprite>()), "自機の向きの絵"),
                ("自機の横向きの絵を 1 枚欠く", stage => SetField(stage, "_intruderSide", new Sprite[] { null, null }), "自機の向きの絵"),
                ("忍び足の絵を外す", stage => SetField(stage, "_intruderSneak", null), "忍び足の絵"),
                ("足音の Tilemap を外す", stage => SetField(stage, "_noise", null), "足音の Tilemap"),
                ("足音のタイルを外す", stage => SetField(stage, "_noiseTile", null), "足音のタイル"),
                ("運んでいる印を外す", stage => SetField(stage, "_carried", null), "運んでいる印"),
                ("目的物を外す", stage => SetField(stage, "_objective", null), "目的物の Renderer"),
                ("壁のタイルの参照を外す", stage => SetField(stage, "_wallTile", null), "壁のタイル"),
                ("警備員の巡回路を外す", stage => SetField(stage.Guards[0], "_route", null), "警備員 A の描画の部品"),
                ("警備員の記号の絵を外す", stage => SetField(stage.Guards[2], "_exclaim", null), "警備員 C の描画の部品"),
                ("壁の位置に木箱のタイルを置く", stage => Tilemap(stage, "_walls").SetTile(new Vector3Int(0, 0, 0), (TileBase)GetField(stage, "_crateTile")), "壁 (0, 0) のタイル"),
                ("手前の面の壁を奥の壁にする", stage => Tilemap(stage, "_walls").SetTile(new Vector3Int(1, 11, 0), (TileBase)GetField(stage, "_wallTile")), "壁 (1, 11) のタイル"),
                ("木箱の位置に壁のタイルを置く", stage => Tilemap(stage, "_crates").SetTile(new Vector3Int(3, 4, 0), (TileBase)GetField(stage, "_wallTile")), "木箱 (3, 4) のタイル"),
            };
            Assert.That(HushWardStage.IsWallFace(ward.Map, 1, 11), Is.True, "(1, 11) は下が床の壁");
            Assert.That(ward.Map[3, 4], Is.EqualTo(TileKind.Crate));
            foreach (var (label, breakIt, expected) in cases)
            {
                var copy = Object.Instantiate(Stage.gameObject).GetComponent<HushWardStage>();
                try
                {
                    Assert.That(copy.Mismatch(ward), Is.Null, "複製はそのままなら一致する");
                    breakIt(copy);
                    string reason = null;
                    Assert.DoesNotThrow(() => reason = copy.Mismatch(ward), label);
                    Assert.That(reason, Does.Contain(expected), label);
                }
                finally
                {
                    Object.Destroy(copy.gameObject);
                }
            }
        }

        private static object GetField(object target, string name) =>
            target.GetType().GetField(name, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(target);

        private static void SetField(object target, string name, object value) =>
            target.GetType().GetField(name, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.SetValue(target, value);

        private static UnityEngine.Tilemaps.Tilemap Tilemap(HushWardStage stage, string name) => (UnityEngine.Tilemaps.Tilemap)GetField(stage, name);

        private static Facility Rebuild(string[] rows, IReadOnlyList<GuardPlan> guards)
        {
            var plans = new GuardPlan[guards.Count];
            for (var i = 0; i < plans.Length; i++) { plans[i] = guards[i]; }
            return new Facility(FacilityMap.Parse(rows), plans);
        }

        /// <summary>(c0, r0) を <paramref name="was"/> に、(c1, r1) を <paramref name="now"/> にした地図（行 0 が最下段）。</summary>
        private static string[] Replace(string[] rows, int c0, int r0, char was, int c1, int r1, char now)
        {
            var copy = (string[])rows.Clone();
            Set(copy, c0, r0, was);
            Set(copy, c1, r1, now);
            return copy;
        }

        private static void Set(string[] rows, int column, int row, char symbol)
        {
            var line = rows.Length - 1 - row;
            var chars = rows[line].ToCharArray();
            chars[column] = symbol;
            rows[line] = new string(chars);
        }

        [UnityTest]
        public IEnumerator Camera_ShowsTheWholeFacility()
        {
            yield return null;
            var camera = _game.ViewCamera;
            var map = Facility.Ward().Map;
            var bottomLeft = camera.WorldToViewportPoint(Stage.transform.TransformPoint(Vector3.zero));
            var topRight = camera.WorldToViewportPoint(Stage.transform.TransformPoint(new Vector3(map.Width, map.Height, 0)));
            Assert.That(bottomLeft.x, Is.GreaterThanOrEqualTo(-1e-4f));
            Assert.That(bottomLeft.y, Is.GreaterThanOrEqualTo(-1e-4f));
            Assert.That(topRight.x, Is.LessThanOrEqualTo(1 + 1e-4f));
            Assert.That(topRight.y, Is.LessThanOrEqualTo(1 + 1e-4f));
        }

        [UnityTest]
        public IEnumerator Keys_WalkRunAndSneak_AndReleaseStops()
        {
            yield return StartGame();
            var start = M.Intruder.Center;
            _keyboard.Hold(Key.UpArrow);
            yield return WaitUntil(() => M.Intruder.Center.Y > start.Y + (Units.Tile / 2));
            Assert.That(M.Intruder.Gait, Is.EqualTo(Gait.Walk));
            _keyboard.Hold(Key.UpArrow, Key.LeftShift);
            yield return WaitUntil(() => M.Intruder.Gait == Gait.Run);
            _keyboard.Hold(Key.UpArrow, Key.LeftShift, Key.C);
            yield return WaitUntil(() => M.Intruder.Gait == Gait.Sneak);
            Assert.That(Stage.Intruder.color.a, Is.LessThan(.99f), "忍び足は少し透ける");
            _keyboard.Hold();
            yield return null;
            yield return null;
            var stopped = M.Intruder.Center;
            yield return WaitSeconds(.2f);
            Assert.That(M.Intruder.Center, Is.EqualTo(stopped), "離した後は止まったまま");
            Assert.That(Stage.Intruder.transform.localPosition, Is.EqualTo(HushWardStage.Local(M.Intruder.Center)), "自機の描画は体の中心");
        }

        [UnityTest]
        public IEnumerator ConeMesh_IsTheSameFanAsTheVisionRule()
        {
            yield return StartPiloted(new StealthPilot());
            StepUntil(() => M.Ticks >= 120);
            for (var i = 0; i < M.Guards.Count; i++)
            {
                var guard = M.Guards[i];
                var view = Stage.Guards[i];
                var rays = _game.Settings.FanRays;
                // 扇は知覚に使った姿勢（動く前の位置と向き、Tick の始めの視界）で描く。
                var spec = guard.SightVision;
                var eye = guard.SightOrigin;
                var distances = new int[rays];
                Vision.Fan(M.Map, eye, guard.SightFacing, spec, distances);
                var block = new MaterialPropertyBlock();
                view.ConeRenderer.GetPropertyBlock(block);
                Assert.That(block.GetVector("_Flip"), Is.EqualTo(Vector4.one), "Sprites/Default が扇を潰さないよう反転なしを渡す");
                Assert.That(block.GetColor("_RendererColor"), Is.EqualTo(Color.white), "扇の色は頂点の色だけで決まる");
                Assert.That(view.ConeMesh.colors[0].a, Is.GreaterThan(.3f), "扇の中心は見える濃さ");
                var vertices = view.ConeMesh.vertices;
                Assert.That(vertices, Has.Length.EqualTo(rays + 1));
                Assert.That(view.transform.localPosition, Is.EqualTo(HushWardStage.Local(guard.Body.Position)), "体は動いた後の位置");
                Assert.That(view.ConeRenderer.transform.position, Is.EqualTo(Stage.transform.TransformPoint(HushWardStage.Local(eye))), "扇の原点は知覚に使った目の位置");
                for (var r = 0; r < rays; r++)
                {
                    var angle = Vision.FanAngle(guard.SightFacing, spec.HalfAngle, r, rays);
                    var expected = HushWardStage.Local(Vision.PointAt(eye, angle, distances[r]) - eye);
                    Assert.That((vertices[r + 1] - expected).sqrMagnitude, Is.LessThan(1e-8f), $"{guard.Plan.Name} の光線 {r}");
                }
            }
        }

        [UnityTest]
        public IEnumerator MarksGaugeAndLevel_FollowTheGuardStates()
        {
            yield return StartPiloted(new BoldPilot());
            var events = StepUntil(() => AnyMode(GuardMode.Investigate));
            var (view, guard) = First(GuardMode.Investigate);
            Assert.That(view.Mark.enabled && view.Mark.sprite == view.MarkOf(GuardMode.Investigate), Is.True, "調査は ?");
            Assert.That(guard.Brain.Awareness > 0 == view.GaugeFill.enabled, Is.True, "気づき値があるときだけゲージ");
            Assert.That(events & FlowEvents.Suspicious, Is.EqualTo(FlowEvents.Suspicious));

            StepUntil(() => AnyMode(GuardMode.Chase));
            (view, _) = First(GuardMode.Chase);
            Assert.That(view.Mark.sprite, Is.EqualTo(view.MarkOf(GuardMode.Chase)), "追跡は !");
            Assert.That(view.GaugeFill.transform.localScale.x, Is.EqualTo(1f).Within(1e-4f), "発見したらゲージが満ちる");
            yield return null;
            Assert.That(Text("level"), Is.EqualTo("厳戒"));
            Assert.That(Text("discoveries"), Is.EqualTo("発見  1"));
            Assert.That(_game.Toast, Does.Contain("見つかった"));

            // ここから試合の終わりまで毎 Tick 描き、どの警備員の記号も今の状態と合うことを確かめる（捜索が起きるかはシードによる）。
            var seen = new HashSet<GuardMode>();
            for (var t = 0; t < TickLimit && !M.IsOver; t++)
            {
                _game.Flow.StepTick();
                _game.ConsumeEvents();
                _game.Render();
                for (var i = 0; i < M.Guards.Count; i++)
                {
                    var mode = M.Guards[i].Brain.Mode;
                    seen.Add(mode);
                    var mark = Stage.Guards[i].MarkOf(mode);
                    Assert.That(Stage.Guards[i].Mark.enabled, Is.EqualTo(mark != null), $"{t} {mode}");
                    if (mark != null) { Assert.That(Stage.Guards[i].Mark.sprite, Is.EqualTo(mark), $"{t} {mode}"); }
                }
            }

            Assert.That(seen, Has.Member(GuardMode.Patrol).And.Member(GuardMode.Chase));
        }

        private bool AnyMode(GuardMode mode)
        {
            foreach (var guard in M.Guards)
            {
                if (guard.Brain.Mode == mode) { return true; }
            }

            return false;
        }

        private (HushWardGuardView View, Guard Guard) First(GuardMode mode)
        {
            for (var i = 0; i < M.Guards.Count; i++)
            {
                if (M.Guards[i].Brain.Mode == mode) { return (Stage.Guards[i], M.Guards[i]); }
            }

            throw new InvalidOperationException(mode.ToString());
        }

        [UnityTest]
        public IEnumerator DebugKey_ShowsDetailsRoutesAndGoals()
        {
            yield return StartGame();
            var debug = _game.HudRoot.Q("debug");
            Assert.That(debug.style.display.value, Is.EqualTo(DisplayStyle.None));
            yield return _keyboard.Tap(Key.F1);
            yield return null;
            Assert.That(_game.Flow.ShowDebug, Is.True);
            Assert.That(debug.style.display.value, Is.EqualTo(DisplayStyle.Flex));
            Assert.That(Text("debug-text"), Does.Contain("A  巡回").And.Contain("B  巡回").And.Contain("C  巡回"));
            for (var i = 0; i < M.Guards.Count; i++)
            {
                var view = Stage.Guards[i];
                Assert.That(view.Route.enabled, Is.True);
                Assert.That(view.Route.positionCount, Is.EqualTo(M.Guards[i].Plan.Route.Count), "巡回路は巡回点を結ぶ");
                Assert.That(view.Goal.enabled, Is.True);
                Assert.That(view.Goal.transform.position, Is.EqualTo(Stage.transform.TransformPoint(HushWardStage.CellCenter(M.Guards[i].Brain.Goal))), "向かう先の印");
            }

            yield return _keyboard.Tap(Key.F1);
            yield return null;
            Assert.That(debug.style.display.value, Is.EqualTo(DisplayStyle.None));
            foreach (var view in Stage.Guards) { Assert.That(view.Route.enabled || view.Goal.enabled, Is.False); }
        }

        [UnityTest]
        public IEnumerator Footsteps_DrawTheCellsTheSoundReaches()
        {
            yield return StartPiloted(new BoldPilot());
            StepUntil(() => M.LastNoise != null && M.LastNoiseTick == M.Ticks);
            var noise = M.LastNoise.Value;
            var hearing = new Hearing(M.Map);
            hearing.Spread(noise.Source, noise.Radius);
            var expected = 0;
            for (var row = 0; row < M.Map.Height; row++)
            {
                for (var column = 0; column < M.Map.Width; column++)
                {
                    var hears = hearing.Hears(new Cell(column, row));
                    if (hears) { expected++; }
                    Assert.That(Stage.Noise.HasTile(new Vector3Int(column, row, 0)), Is.EqualTo(hears), $"({column}, {row})");
                }
            }

            Assert.That(Stage.NoiseCells, Is.EqualTo(expected));
            Assert.That(expected, Is.GreaterThan(1));
            _game.Flow.Pilot = new HoldStill();
            var shown = M.Ticks;
            StepUntil(() => M.Ticks >= shown + _game.Settings.NoiseShowTicks);
            Assert.That(Stage.NoiseCells, Is.Zero, "時間が経てば消える");
        }

        [UnityTest]
        public IEnumerator Caught_ShowsTheResult_AndEnterRetriesWithANewSeed()
        {
            yield return StartPiloted(new Careless());
            var seed = M.Seed;
            var events = StepUntil(() => _game.Flow.Screen == AppScreen.Caught);
            Assert.That(events & FlowEvents.Caught, Is.EqualTo(FlowEvents.Caught));
            yield return null;
            Assert.That(Text("headline"), Is.EqualTo("捕まった…"));
            Assert.That(Stage.Intruder.color.g, Is.LessThan(.6f), "捕まった自機は赤い");
            Assert.That(IsShown("submit") && IsShown("restart") && IsShown("title"), Is.True);
            _game.Flow.Pilot = null;
            yield return _keyboard.Tap(Key.Enter);
            Assert.That(_game.Flow.Screen, Is.EqualTo(AppScreen.Playing));
            Assert.That(M.Seed, Is.EqualTo(seed + 1));
            Assert.That(M.Ticks, Is.LessThan(30));
        }

        [UnityTest]
        public IEnumerator StealthEscape_ShowsTheUnseenRating()
        {
            yield return StartPiloted(new StealthPilot());
            var events = StepUntil(() => _game.Flow.Screen == AppScreen.Escaped);
            Assert.That(events & (FlowEvents.ObjectiveTaken | FlowEvents.Escaped), Is.EqualTo(FlowEvents.ObjectiveTaken | FlowEvents.Escaped));
            Assert.That(events & FlowEvents.Discovered, Is.EqualTo(FlowEvents.None));
            yield return null;
            Assert.That(Text("headline"), Is.EqualTo("脱出成功！"));
            Assert.That(Text("message"), Does.Contain("未発見"));
            Assert.That(Stage.Objective.enabled, Is.False, "取った目的物は消える");
            Assert.That(Stage.Carried.enabled, Is.True, "運んでいる印");
        }

        [UnityTest]
        public IEnumerator BackToTitle_LooksLikeStartup()
        {
            yield return null;
            var initial = Snapshot();
            yield return StartPiloted(new BoldPilot());
            StepUntil(() => M.Discoveries > 0 || M.IsOver);
            Assert.That(Snapshot(), Is.Not.EqualTo(initial), "遊んで状態が変わった");
            _game.Flow.Pilot = null;
            yield return _keyboard.Tap(Key.Escape);
            yield return _keyboard.Tap(Key.T);
            Assert.That(_game.Flow.Screen, Is.EqualTo(AppScreen.Title));
            yield return null;
            Assert.That(Snapshot(), Is.EqualTo(initial), "タイトルへ戻った表示は起動直後と同じ");
        }

        /// <summary>描画の状態（位置・絵・表示の有無・扇の頂点）を文字列にまとめる。</summary>
        private string Snapshot()
        {
            var parts = new List<string>
            {
                Stage.Intruder.transform.localPosition.ToString("F4"),
                Stage.Intruder.sprite.name,
                Stage.Intruder.color.ToString(),
                Stage.Objective.enabled.ToString(),
                Stage.Carried.enabled.ToString(),
                Stage.NoiseCells.ToString(System.Globalization.CultureInfo.InvariantCulture),
            };
            foreach (var view in Stage.Guards)
            {
                parts.Add(view.transform.localPosition.ToString("F4"));
                parts.Add(view.Body.sprite.name + view.Body.flipX);
                parts.Add(view.Mark.enabled.ToString() + view.GaugeFill.enabled);
                foreach (var vertex in view.ConeMesh.vertices) { parts.Add(vertex.ToString("F4")); }
            }

            return string.Join("|", parts);
        }

        [UnityTest]
        public IEnumerator PauseKey_StopsTime()
        {
            yield return StartGame();
            yield return _keyboard.Tap(Key.Escape);
            Assert.That(_game.Flow.Screen, Is.EqualTo(AppScreen.Paused));
            var ticks = M.Ticks;
            yield return WaitSeconds(.2f);
            Assert.That(M.Ticks, Is.EqualTo(ticks));
            Assert.That(Text("headline"), Is.EqualTo("一時停止"));
            Assert.That(IsShown("restart") && IsShown("title"), Is.True);
            yield return _keyboard.Tap(Key.P);
            Assert.That(_game.Flow.Screen, Is.EqualTo(AppScreen.Playing));
            Assert.That(_game.HudRoot.Q("panel").style.display.value, Is.EqualTo(DisplayStyle.None));
        }

        [UnityTest]
        public IEnumerator HudButtons_StartPauseDebugRestartAndBackToTitle()
        {
            yield return ClickButton("submit");
            Assert.That(_game.Flow.Screen, Is.EqualTo(AppScreen.Playing));
            yield return ClickButton("debug-toggle");
            Assert.That(_game.Flow.ShowDebug, Is.True);
            yield return ClickButton("pause");
            Assert.That(_game.Flow.Screen, Is.EqualTo(AppScreen.Paused));
            yield return ClickButton("restart");
            Assert.That(_game.Flow.Screen, Is.EqualTo(AppScreen.Playing));
            Assert.That(M.Ticks, Is.LessThan(30));
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
            var y = M.Intruder.Center.Y;
            _keyboard.Hold(Key.UpArrow);
            yield return WaitUntil(() => M.Intruder.Center.Y > y);
            _game.SendMessage("OnApplicationFocus", false);
            Assert.That(_game.Flow.Screen, Is.EqualTo(AppScreen.Paused));
            _keyboard.Hold();
            yield return null;
            _game.SendMessage("OnApplicationFocus", true);
            yield return null;
            yield return null;
            yield return _keyboard.Tap(Key.Enter);
            Assert.That(_game.Flow.Screen, Is.EqualTo(AppScreen.Playing));
            var resumed = M.Intruder.Center;
            yield return WaitSeconds(.2f);
            Assert.That(M.Intruder.Center, Is.EqualTo(resumed), "フォーカスを失う前のキーは持ち越さない");
        }

        [UnityTest]
        public IEnumerator SettingsAsset_MatchesTheCodeDefaults_AndTheStealthRouteHolds()
        {
            // 生成スクリプトはアセットを上書きしないので、コードの既定値を変えたらアセットも直す（024 の知見）。
            yield return null;
            Assert.That(JsonUtility.ToJson(_game.Settings.Rules), Is.EqualTo(JsonUtility.ToJson(new WardConfig())), "設定アセットの試合の設定がコードの既定値と同じ");
            var match = new Infiltration(_game.Settings.Rules, Facility.Ward(), 1);
            var pilot = new StealthPilot();
            while (!match.IsOver && match.Ticks < TickLimit) { match.Tick(pilot.InputFor(match)); }
            Assert.That(match.Status, Is.EqualTo(MatchStatus.Escaped), "設定アセットの値で見つからずに脱出できる");
            Assert.That(match.Hash(), Is.EqualTo(0xA0BB2468u), "App テストの固定値と同じ");
        }

        [UnityTest]
        public IEnumerator Performance_TickAndRenderStayCheap()
        {
            // 見つかってから逃げる試合（追跡・捜索・無線・足音の範囲がすべて起きる）で、Core の 1 Tick と描画の更新を分けて測る。
            // 操縦役の先読みは測らない（入力を先に決めてから測る）。
            yield return StartPiloted(new HoldStill());
            var match = new Infiltration(_game.Settings.Rules, Facility.Ward(), 1);
            var pilot = new BoldPilot();
            var ticks = new List<double>();
            var renders = new List<double>();
            var clock = new System.Diagnostics.Stopwatch();
            for (var i = 0; i < 3000 && !match.IsOver; i++)
            {
                var input = pilot.InputFor(match);
                clock.Restart();
                match.Tick(input);
                ticks.Add(clock.Elapsed.TotalMilliseconds);
                clock.Restart();
                Stage.Render(match, _game.Settings.FanRays, true, _game.Settings.NoiseShowTicks);
                renders.Add(clock.Elapsed.TotalMilliseconds);
            }

            Assert.That(match.Discoveries, Is.GreaterThan(0), "追跡の場面を含む");
            var tick95 = Percentile(ticks, .95);
            var render95 = Percentile(renders, .95);
            Debug.Log($"[Performance] {ticks.Count} Tick：Core の Tick p95 {tick95:F3} ms・最大 {Percentile(ticks, 1):F3} ms、描画の更新 p95 {render95:F3} ms・最大 {Percentile(renders, 1):F3} ms");
            Assert.That(tick95, Is.LessThan(2), "Core の 1 Tick は 1 フレームの Tick 上限（5）でも 16 ms に収まる");
            Assert.That(render95, Is.LessThan(4));
        }

        private static double Percentile(List<double> values, double rank)
        {
            var sorted = new List<double>(values);
            sorted.Sort();
            return sorted[Math.Min(sorted.Count - 1, (int)Math.Ceiling(rank * sorted.Count) - 1)];
        }

        [Test]
        public void Settings_RejectInvalidValues()
        {
            AssertRejected(s => s.Volume = float.NaN);
            AssertRejected(s => s.Volume = 1.5f);
            AssertRejected(s => s.ToastTicks = 0);
            AssertRejected(s => s.NoiseShowTicks = 0);
            AssertRejected(s => s.FanRays = 1);
            AssertRejected(s => s.Rules.RunSpeed = 0);
            AssertRejected(s => s.Rules.ChaseSpeed = 99);
            AssertRejected(s => s.Rules = null, typeof(ArgumentNullException));
        }

        [Test]
        public void FormatTicks_ShowsMinutesSecondsAndHundredths()
        {
            Assert.That(HushWardGame.FormatTicks(0), Is.EqualTo("0:00.00"));
            Assert.That(HushWardGame.FormatTicks(59), Is.EqualTo("0:00.98"));
            Assert.That(HushWardGame.FormatTicks((60 * 75) + 30), Is.EqualTo("1:15.50"));
        }

        [Test]
        public void Quarter_RoundsTheFacingToTheNearestSide()
        {
            Assert.That(HushWardGuardView.Quarter(Trig.Right), Is.Zero);
            Assert.That(HushWardGuardView.Quarter(Trig.Up), Is.EqualTo(1));
            Assert.That(HushWardGuardView.Quarter(Trig.Left), Is.EqualTo(2));
            Assert.That(HushWardGuardView.Quarter(Trig.Down), Is.EqualTo(3));
            Assert.That(HushWardGuardView.Quarter(Trig.FullTurn - 100), Is.Zero);
            Assert.That(HushWardGuardView.Quarter((Trig.FullTurn / 8) - 1), Is.Zero);
            Assert.That(HushWardGuardView.Quarter(Trig.FullTurn / 8), Is.EqualTo(1));
        }

        private static void AssertRejected(Action<HushWardSettings> change, Type exception = null)
        {
            var settings = ScriptableObject.CreateInstance<HushWardSettings>();
            Assert.DoesNotThrow(settings.Validate);
            change(settings);
            Assert.Throws(exception ?? typeof(ArgumentOutOfRangeException), settings.Validate);
            Object.DestroyImmediate(settings);
        }

        /// <summary>止まったままの操縦役。</summary>
        private sealed class HoldStill : IPilot
        {
            public IntruderInput InputFor(Infiltration match) => IntruderInput.None;
        }

        /// <summary>警備員を気にせず、歩数が最短の経路を歩いて目的物へ向かう操縦役（捕まる場面を作る）。</summary>
        private sealed class Careless : IPilot
        {
            private Steering _steering;

            public IntruderInput InputFor(Infiltration match)
            {
                _steering ??= new Steering(match.Map);
                return _steering.Toward(match, Steering.GoalOf(match), Gait.Walk);
            }
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
