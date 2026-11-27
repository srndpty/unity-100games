#nullable enable
using System.Collections;
using System.Collections.Generic;
using System.Text;
using Game.App;
using Game.Core.Geometry;
using Game.Core.Mechanisms;
using Game.Core.Play;
using Game.Core.Rooms;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace Game.Unity.Tests
{
    /// <summary>
    /// 実シーンの PlayMode テスト。<c>Time.timeScale = 0</c> で実時間を止め、Tick はテストから進める（015）。
    /// 入力は仮想キーボードと仮想マウスから送り、確かめたい処理をテスト側から呼ばない（013）。
    /// </summary>
    public sealed class EscapeSceneTests
    {
        private const string SceneName = "Stargazer";

        private TestKeyboard _keyboard = null!;
        private TestMouse _mouse = null!;
        private EscapeGame _game = null!;
        private int _quits;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _quits = 0;
            EscapeGame.QuitHandler = () => _quits++;
            _keyboard = new TestKeyboard();
            _mouse = new TestMouse();
            yield return SceneManager.LoadSceneAsync(SceneName);
            yield return null;
            _game = Object.FindAnyObjectByType<EscapeGame>();
            Assert.That(_game, Is.Not.Null);
            for (int i = 0; i < 60 && _game.Hud == null; i++) { yield return null; }
            Assert.That(_game.Hud, Is.Not.Null, "HUD が読み込まれる");
            Time.timeScale = 0f;
            _mouse.Move(new Vector2(1, 1));
            _game.Render();
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            Time.timeScale = 1f;
            EscapeGame.QuitHandler = Application.Quit;
            _mouse.Dispose();
            _keyboard.Dispose();
            yield return null;
        }

        private EscapeFlow Flow => _game.Flow;

        private RoomDef Def => _game.Def;

        private VisualElement Hud => _game.Hud!.Root;

        private Camera Cam => _game.ViewCamera;

        private bool Visible(string name) => EscapeHud.Visible(Hud.Q(name));

        private int Part(string id)
        {
            foreach (var p in Def.Parts)
            {
                if (p.Id == id) { return p.Index; }
            }

            Assert.Fail(id);
            return -1;
        }

        private int View(string id)
        {
            foreach (var v in Def.Views)
            {
                if (v.Id == id) { return v.Index; }
            }

            Assert.Fail(id);
            return -1;
        }

        private int Mechanism(MechanismKind kind)
        {
            foreach (var m in Def.Mechanisms)
            {
                if (m.Kind == kind) { return m.Index; }
            }

            Assert.Fail(kind.ToString());
            return -1;
        }

        /// <summary>キーを 1 回押して離し、1 Tick 進める。</summary>
        private IEnumerator Press(Key key)
        {
            yield return _keyboard.Tap(key);
            Flow.StepTicks(1);
            _game.Render();
            yield return null;
        }

        private IEnumerator Click(Vector2 pixel, bool right = false)
        {
            if (right) { _mouse.PressRight(pixel); }
            else { _mouse.Press(pixel); }
            yield return null;
            _mouse.Release(pixel);
            yield return null;
            Flow.StepTicks(1);
            _game.Render();
            yield return null;
        }

        /// <summary>カメラの演出が終わるまで Tick を進める。</summary>
        private IEnumerator Settle()
        {
            for (int i = 0; i < 600 && Flow.InTransition; i++) { Flow.StepTicks(1); }
            Assert.That(Flow.InTransition, Is.False);
            _game.Render();
            yield return null;
        }

        private IEnumerator Start()
        {
            yield return Press(Key.Enter);
            Assert.That(Flow.Screen, Is.EqualTo(FlowScreen.Playing));
        }

        /// <summary>HUD の要素の中に入る画面の座標（左下が原点）を探す。</summary>
        private Vector2 ScreenPointIn(VisualElement element)
        {
            var bound = element.worldBound;
            for (int y = 2; y < Screen.height; y += 4)
            {
                for (int x = 2; x < Screen.width; x += 4)
                {
                    var p = _game.PanelPoint(new Vector2(x, y));
                    if (p != null && bound.Contains(p.Value)) { return new Vector2(x, y); }
                }
            }

            Assert.Fail($"{element.name} の上の画面の座標が見つからない（{bound}）");
            return default;
        }

        /// <summary>クリックすると狙った命令になる画面のピクセル（操縦役と同じ探し方を Core の座標で行い、ピクセルへ写す）。</summary>
        private Vector2 PixelFor(Command command, int selected = -1)
        {
            var point = ClickSearch.PointFor(Flow.Snapshot()!, command, selected, Flow.Region);
            Assert.That(point, Is.Not.Null, command.ToString());
            return _game.PixelPoint(new Vector2Int(point!.Value.X, point.Value.Y));
        }

        /// <summary>命令を、キー・右クリック・持ち物の枠・部屋のクリックだけで行う。</summary>
        private IEnumerator DoByInput(Command c)
        {
            var before = Flow.Executed;
            switch (c.Kind)
            {
                case CommandKind.Turn:
                    yield return Press(c.A > 0 ? Key.RightArrow : Key.LeftArrow);
                    break;
                case CommandKind.Back:
                    yield return Click(new Vector2(Screen.width / 2f, Screen.height * 0.6f), right: true);
                    break;
                case CommandKind.UseItem:
                    {
                        var slot = IndexOf(Flow.Inventory, c.A);
                        yield return Click(ScreenPointIn(_game.Hud!.Slot(slot)));
                        Assert.That(Flow.SelectedItem, Is.EqualTo(c.A));
                        yield return Click(PixelFor(c, c.A));
                        break;
                    }

                default:
                    yield return Click(PixelFor(c));
                    break;
            }

            // 入力が捨てられた・別の命令になった・拒否された、のどれも成功にしない（レビュー P2-1）。
            Autopilot.Verify(Flow, c, before);
            yield return Settle();
        }

        private static int IndexOf(IReadOnlyList<int> list, int value)
        {
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i] == value) { return i; }
            }

            Assert.Fail($"持ち物 {value} がない");
            return -1;
        }

        /// <summary>次の一手（ヒントの答え）を入力だけで行う。</summary>
        private IEnumerator AdvanceByInput()
        {
            var w = Flow.Snapshot()!;
            var step = HintEngine.Next(w);
            foreach (var c in Navigation.PathTo(w, step.View(Def))) { yield return DoByInput(c); }
            foreach (var c in step.Answer(Flow.Snapshot()!)) { yield return DoByInput(c); }
        }

        /// <summary>見た目を決める状態の要約（部品・はめた物・手に取った物・望遠鏡の星・HUD の画面）。</summary>
        private string Snapshot()
        {
            _game.Render();
            var s = _game.Stage;
            var sb = new StringBuilder();
            for (int i = 0; i < s.PartCount; i++) { sb.Append(s.PartAt(i).gameObject.activeSelf ? '1' : '0'); }
            sb.Append('|');
            for (int e = 0; e < Def.Examinables.Count; e++) { sb.Append(s.HeldModel(e).activeSelf ? '1' : '0'); }
            sb.Append('|');
            for (int g = 0; g < SequenceLock.GemCount; g++) { sb.Append(s.Star(g).activeSelf ? '1' : '0').Append(s.Gem(g).sharedMaterial.name); }
            sb.Append('|').Append(Visible("title")).Append(Visible("bar")).Append(Visible("result")).Append(Visible("pause"));
            sb.Append('|').Append(Cam.transform.position.ToString("F4")).Append(Cam.transform.rotation.eulerAngles.ToString("F2"));
            return sb.ToString();
        }

        // ---- Prefab とシーン ----

        [UnityTest]
        public IEnumerator StageMatchesRoom_AndTitleIsShown()
        {
            Assert.That(_game.Stage.Mismatch(Def), Is.Null);
            Assert.That(Visible("title"), Is.True);
            Assert.That(Visible("bar"), Is.False);
            yield return null;
        }

        // ---- 重点：カメラとヒットテスト（Core と Physics.Raycast が同じ部品に当たる） ----

        [UnityTest]
        public IEnumerator CoreHitTest_MatchesPhysicsRaycast_InEveryView_AtStartAndAfterEscape()
        {
            yield return Start();
            var checks = CompareHits();
            new Autopilot().RunToResult(Flow);
            _game.Render();
            yield return null;
            checks += CompareHits();
            Assert.That(checks, Is.GreaterThan(100));
        }

        /// <summary>
        /// 全ての視点でカメラを Core の宣言どおりに置き、今ある全ての部品の手前の面の中心をピクセルへ写して、
        /// Core のヒットテスト（ピクセル → 画面の整数座標 → レイ）と Physics.Raycast（BoxCollider）の当たりを比べる。
        /// </summary>
        private int CompareHits()
        {
            var w = Flow.Snapshot()!;
            Physics.SyncTransforms();
            int n = 0;
            foreach (var view in Def.Views)
            {
                StageMath.Apply(Cam, view.Pose);
                foreach (var part in Def.Parts)
                {
                    if (!w.IsPresent(part.Index)) { continue; }
                    var screen = Cam.WorldToScreenPoint(StageMath.ToWorld(part.Box.FrontCenter(view.Wall)));
                    if (screen.z <= 0 || !Cam.pixelRect.Contains(screen)) { continue; }
                    var core = StageMath.ToCore(screen, Cam.pixelRect);
                    if (!view.Pose.TryRay(core.x, core.y, out var ray)) { continue; }
                    var coreHit = w.HitTest(ray);
                    var physics = Physics.Raycast(Cam.ScreenPointToRay(StageMath.ToPixel(core, Cam.pixelRect)), out var hit, 20f)
                        ? hit.collider.gameObject.name
                        : "(なし)";
                    Assert.That(physics, Is.EqualTo(coreHit < 0 ? "(なし)" : Def.Parts[coreHit].Id), $"視点 {view.Id}・{part.Id} の手前の面の中心 ({core.x}, {core.y})");
                    n++;
                }
            }

            _game.Render();
            return n;
        }

        // ---- 完了判定：5 つの仕掛けを解いて脱出（仮想マウスとキーボードの入力だけで） ----

        [UnityTest]
        public IEnumerator PlayByMouseAndKeyboardOnly_EscapesAndShowsResult()
        {
            yield return Start();
            for (int i = 0; i < 40 && !Flow.Escaped; i++) { yield return AdvanceByInput(); }
            Assert.That(Flow.Escaped, Is.True);
            Assert.That(Flow.SolvedCount, Is.EqualTo(Def.Mechanisms.Count));
            for (int i = 0; i < 600 && Flow.Screen != FlowScreen.Result; i++) { Flow.StepTicks(1); }
            _game.Render();
            yield return null;
            Assert.That(Flow.Screen, Is.EqualTo(FlowScreen.Result));
            Assert.That(Visible("result"), Is.True);
            Assert.That(_game.Hud!.ResultStats.text.Contains("操作", System.StringComparison.Ordinal), Is.True);
            yield return Click(new Vector2(Screen.width / 2f, Screen.height / 2f));
            Assert.That(Flow.Screen, Is.EqualTo(FlowScreen.Title));
        }

        // ---- 観察・回転・ズーム ----

        [UnityTest]
        public IEnumerator DragRotatesTheCylinder_UntilItsBottomShowsTheDigits()
        {
            yield return Start();
            yield return DoByInput(Command.Focus(View("desk")));
            yield return DoByInput(Command.Inspect(Part("desk.cylinder")));
            var cylinder = Def.Parts[Part("desk.cylinder")].Examinable;
            Assert.That(_game.Stage.HeldModel(cylinder).activeSelf, Is.True);
            Assert.That(_game.Stage.Clue(cylinder).activeSelf, Is.False, "手がかりは向いているときだけ");

            // 押したまま上へドラッグ（1 段の 1.4 倍の長さ。2 段には届かない）。
            var from = new Vector2(Screen.width / 2f, Screen.height / 2f);
            var to = from + new Vector2(0f, Screen.height * Flow.DragStepUnits * 1.4f / (2f * CameraPose.HalfHeight));
            _mouse.Press(from);
            yield return null;
            _mouse.Press(to);
            yield return null;
            _mouse.Release(to);
            yield return null;
            Flow.StepTicks(1);
            _game.Render();
            yield return null;
            Assert.That(Flow.ClueFacing, Is.True);
            Assert.That(_game.Stage.Clue(cylinder).activeSelf, Is.True, "底の 3 桁が見える");
            yield return Click(from, right: true);
            Assert.That(Flow.Held, Is.EqualTo(-1));
            Assert.That(_game.Stage.HeldModel(cylinder).activeSelf, Is.False);
        }

        [UnityTest]
        public IEnumerator FocusZoomsCamera_AndRightClickReturnsToWall()
        {
            yield return Start();
            var wall = Cam.transform.position;
            Assert.That(Vector3.Distance(wall, StageMath.ToWorld(Def.Views[View("north")].Pose.Eye)), Is.LessThan(1e-4f));
            yield return Click(PixelFor(Command.Focus(View("desk"))));
            Assert.That(Flow.View, Is.EqualTo(View("desk")));
            Assert.That(Flow.InTransition, Is.True);
            var mid = Cam.transform.position;
            Assert.That(Vector3.Distance(mid, wall), Is.GreaterThan(1e-3f), "演出の途中のカメラ");
            yield return Settle();
            Assert.That(Vector3.Distance(Cam.transform.position, StageMath.ToWorld(Def.Views[View("desk")].Pose.Eye)), Is.LessThan(1e-4f));
            Assert.That(Mathf.Abs(Cam.fieldOfView - (float)Def.Views[View("desk")].Pose.VerticalFovDegrees), Is.LessThan(1e-3f));
            yield return Click(new Vector2(Screen.width / 2f, Screen.height * 0.6f), right: true);
            yield return Settle();
            Assert.That(Flow.View, Is.EqualTo(View("north")));
        }

        [UnityTest]
        public IEnumerator TourOfFourWalls_KeepsTakenItemsAndOpenDrawer()
        {
            yield return Start();
            var dial = Mechanism(MechanismKind.Dial);
            for (int i = 0; i < 10 && Flow.Inventory.Count == 0; i++) { yield return AdvanceByInput(); }
            Assert.That(Flow.PhaseOf(dial), Is.EqualTo(Phase.Solved));
            var before = Snapshot();
            yield return DoByInput(Command.Back);
            for (int i = 0; i < 4; i++) { yield return DoByInput(Command.Turn(1)); }
            Assert.That(Flow.View, Is.EqualTo(View("north")));
            yield return DoByInput(Command.Focus(View("desk")));
            Assert.That(Flow.Inventory.Count, Is.EqualTo(1), "拾った鍵は持ったまま");
            Assert.That(_game.Stage.PartAt(Part("desk.flap")).gameObject.activeSelf, Is.True, "倒れた前板のまま");
            Assert.That(_game.Stage.PartAt(Part("desk.key")).gameObject.activeSelf, Is.False);
            Assert.That(Snapshot(), Is.EqualTo(before), "一巡して戻ると同じ見た目");
        }

        // ---- 完了判定：リセットで初期状態へ（表示も） ----

        [UnityTest]
        public IEnumerator ResetFromPause_LooksLikeNewGame_AndTitleLooksLikeStartup()
        {
            var startup = Snapshot();
            yield return Start();
            var fresh = Snapshot();
            var freshHash = Flow.WorldHash;
            for (int i = 0; i < 6; i++) { yield return AdvanceByInput(); }
            yield return Press(Key.H);
            Assert.That(Visible("hint"), Is.True);
            yield return DoByInput(Command.Back);
            yield return Press(Key.Escape);
            Assert.That(Flow.Screen, Is.EqualTo(FlowScreen.Paused));
            yield return Press(Key.R);
            Assert.That(Flow.Screen, Is.EqualTo(FlowScreen.Playing));
            Assert.That(Flow.WorldHash, Is.EqualTo(freshHash));
            Assert.That(Flow.PlayTicks, Is.EqualTo(1), "経過時間も 0 に戻る（R を押した後にテストの補助が 1 Tick 進める）");
            Assert.That(Snapshot(), Is.EqualTo(fresh), "リセットした後の表示が、遊び始めと同じ");
            Assert.That(Visible("hint"), Is.False);

            yield return Press(Key.Escape);
            yield return Click(ScreenPointIn(_game.Hud!.MenuRow(FlowScreen.Paused, EscapeFlow.PauseTitle)));
            Assert.That(Flow.Screen, Is.EqualTo(FlowScreen.Title));
            Assert.That(Snapshot(), Is.EqualTo(startup), "タイトルへ戻った表示が起動直後と同じ（021）");
        }

        // ---- 入力の受け付け ----

        [UnityTest]
        public IEnumerator ClickRightAfterFocusRestoreIsIgnored()
        {
            yield return Start();
            var p = PixelFor(Command.Focus(View("desk")));
            _game.SendMessage("OnApplicationFocus", false);
            _game.SendMessage("OnApplicationFocus", true);
            yield return Click(p);
            Assert.That(Flow.View, Is.EqualTo(View("north")), "ウィンドウを選び直したクリックでは寄らない");
            yield return Click(p);
            Assert.That(Flow.View, Is.EqualTo(View("desk")));
        }

        [UnityTest]
        public IEnumerator ArrowsAndAdTurn_HintKeyShowsHint()
        {
            yield return Start();
            yield return Press(Key.D);
            yield return Settle();
            Assert.That(Flow.View, Is.EqualTo(View("east")));
            yield return Press(Key.LeftArrow);
            yield return Settle();
            yield return Press(Key.A);
            yield return Settle();
            Assert.That(Flow.View, Is.EqualTo(View("west")));
            yield return Press(Key.H);
            Assert.That(Visible("hint"), Is.True);
            Assert.That(_game.Hud!.HintTextLabel.text, Is.EqualTo(Flow.HintText));
            Assert.That(Flow.HintsUsed, Is.EqualTo(1));
        }

        // ---- レビュー P2-3：画面の比率と、クリックが届く範囲 ----

        [UnityTest]
        public IEnumerator ViewportKeepsSupportedAspect_AndRegionCoversGuaranteed()
        {
            yield return Start();
            var rect = Cam.pixelRect;
            var aspect = rect.width / rect.height;
            Assert.That(aspect, Is.InRange(StageMath.MinAspect - 0.01f, StageMath.MaxAspect + 0.01f), $"画面 {Screen.width}×{Screen.height} の描画の矩形 {rect}");
            Assert.That(Flow.Region.Covers(ScreenRegion.Guaranteed), Is.True, $"届く範囲 {Flow.Region}");
            foreach (var (w, h) in new[] { (640, 480), (1280, 720), (1280, 800), (2560, 1080), (3440, 1080), (1920, 1200) })
            {
                var r = StageMath.Viewport(w, h);
                var pixel = new Rect(r.x * w, r.y * h, r.width * w, r.height * h);
                Assert.That(StageMath.Region(pixel, h).Covers(ScreenRegion.Guaranteed), Is.True, $"{w}×{h}");
            }

            // 描画の矩形の外（帯の上・画面の外）のクリックは部屋に届かない。
            var before = Flow.Executed;
            var outside = rect.yMin > 1f ? new Vector2(rect.center.x, rect.yMin - 1f) : new Vector2(rect.center.x, rect.yMax + 1f);
            yield return Click(outside);
            Assert.That(Flow.Executed, Is.EqualTo(before));
        }

        // ---- レビュー P2-4：ドラッグは、押した画面の版の間だけ ----

        /// <summary>
        /// 手に取った物の上で左ボタンを押したまま、右クリック・Esc で置く（またはフォーカスを失う）。左ボタンを離さないまま
        /// 同じ物を手に取り直して（命令で）、押したままの左ボタンを大きく動かしても、前のドラッグは新しく手に取った物を回さない。
        /// </summary>
        [UnityTest]
        public IEnumerator HeldLeftButtonAcrossPutDown_DoesNotRotateTheNextObject([Values(0, 1, 2)] int interrupt)
        {
            yield return Start();
            yield return DoByInput(Command.Focus(View("desk")));
            yield return DoByInput(Command.Inspect(Part("desk.cylinder")));
            var from = new Vector2(Screen.width / 2f, Screen.height / 2f);
            _mouse.Press(from);
            yield return null;
            switch (interrupt)
            {
                case 0:
                    _mouse.PressBoth(from);
                    yield return null;
                    _mouse.Press(from);
                    yield return null;
                    break;
                case 1:
                    _keyboard.Hold(Key.Escape);
                    yield return null;
                    _keyboard.Hold();
                    yield return null;
                    break;
                default:
                    _game.SendMessage("OnApplicationFocus", false);
                    _game.SendMessage("OnApplicationFocus", true);
                    yield return null;
                    break;
            }

            Flow.StepTicks(1);
            if (interrupt < 2)
            {
                Assert.That(Flow.Held, Is.EqualTo(-1), "右クリック・Esc で置いた");
                Autopilot.Perform(Flow, Command.Inspect(Part("desk.cylinder")));
            }

            Assert.That(Flow.Held, Is.GreaterThanOrEqualTo(0));
            var executed = Flow.Executed;
            _mouse.Press(from + new Vector2(0f, Screen.height * 0.3f));
            yield return null;
            yield return null;
            Flow.StepTicks(1);
            Assert.That(Flow.Orientation, Is.EqualTo(default(Orientation)), "前のドラッグは続かない");
            Assert.That(Flow.Executed, Is.EqualTo(executed));
            _mouse.Release(from);
            yield return null;
        }

        // ---- レビュー P3-1：手がかりの表示がシードの答えと一致する ----

        [UnityTest]
        public IEnumerator CluesShowTheAnswersOfEachSeed([Values(1, 2, 3)] int baseSeed)
        {
            _game.Reinitialize(baseSeed);
            yield return Start();
            var answers = Flow.Room.Mechanisms;
            var dial = answers[Mechanism(MechanismKind.Dial)].Answer;
            var rings = answers[Mechanism(MechanismKind.Rings)].Answer;
            var time = answers[Mechanism(MechanismKind.Clock)].Answer;
            var order = answers[Mechanism(MechanismKind.Sequence)].Answer;
            var stage = _game.Stage;
            _game.Render();
            for (int i = 0; i < DialLock.Digits; i++) { Assert.That(stage.CylinderDigit(i).sharedMaterial.name, Is.EqualTo("Digit" + dial[i]), $"筒の底の {i + 1} 桁目"); }
            for (int i = 0; i < RingLock.RingCount; i++) { Assert.That(stage.DiscSymbol(i).sharedMaterial.name, Is.EqualTo("Symbol" + rings[i]), $"円盤の {i + 1} 番目の星座"); }
            Assert.That(Mathf.DeltaAngle(stage.BookHour.localEulerAngles.z, -((time[0] * 30f) + (time[1] * 2.5f))), Is.EqualTo(0f).Within(0.01f), "天文書の短針");
            Assert.That(Mathf.DeltaAngle(stage.BookMinute.localEulerAngles.z, -(time[1] * 30f)), Is.EqualTo(0f).Within(0.01f), "天文書の長針");

            // 望遠鏡をのぞき、星が光り始める順を記録する。
            foreach (var c in Navigation.PathTo(Flow.Snapshot()!, View("telescope"))) { yield return DoByInput(c); }
            var cycle = (SequenceLock.GemCount * StudyStage.StarStepTicks) + StudyStage.StarRestTicks;
            var seen = new List<int>();
            var was = new bool[SequenceLock.GemCount];
            for (int t = 0; t < cycle * 2 && seen.Count < SequenceLock.GemCount; t++)
            {
                Flow.StepTicks(1);
                _game.Render();
                for (int g = 0; g < SequenceLock.GemCount; g++)
                {
                    var lit = stage.Star(g).activeSelf;
                    if (lit && !was[g] && !seen.Contains(g)) { seen.Add(g); }
                    was[g] = lit;
                }
            }

            // 記録の始まりが周期の途中なら、答えの最初の宝石から比べる。
            var start = seen.IndexOf(order[0]);
            Assert.That(start, Is.GreaterThanOrEqualTo(0));
            var rotated = new List<int>();
            for (int k = 0; k < seen.Count; k++) { rotated.Add(seen[(start + k) % seen.Count]); }
            Assert.That(rotated, Is.EqualTo(order), "星が光る順は金庫の答えの順");
            yield return null;
        }


        [UnityTest]
        public IEnumerator QuitFromTitleByClick()
        {
            yield return Click(ScreenPointIn(_game.Hud!.MenuRow(FlowScreen.Title, EscapeFlow.TitleQuit)));
            yield return null;
            Assert.That(_quits, Is.EqualTo(1));
        }
    }
}
