#nullable enable
using System;
using System.Collections;
using System.IO;
using Game.App;
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
    /// 最初のシードは固定にする（実行ごとのシードに依存させない）。
    /// </summary>
    public sealed class SummerSceneTests
    {
        private const string SceneName = "MeteorSummer";
        private const int Seed = 1;

        private TestKeyboard _keyboard = null!;
        private TestMouse _mouse = null!;
        private string _dir = "";
        private FileSaveStore _store = null!;
        private SummerGame _game = null!;
        private int _quits;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _dir = Path.Combine(Path.GetTempPath(), "meteor_playmode_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
            _store = new FileSaveStore(Path.Combine(_dir, "save.txt"));
            SummerGame.SetOverridesForTests(_store, Seed);
            _quits = 0;
            SummerGame.QuitHandler = () => _quits++;
            _keyboard = new TestKeyboard();
            _mouse = new TestMouse();
            yield return LoadScene();
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            Time.timeScale = 1f;
            SummerGame.SetOverridesForTests(null, null);
            SummerGame.QuitHandler = Application.Quit;
            _mouse.Dispose();
            _keyboard.Dispose();
            if (Directory.Exists(_dir)) { Directory.Delete(_dir, true); }
            yield return null;
        }

        private IEnumerator LoadScene()
        {
            yield return SceneManager.LoadSceneAsync(SceneName);
            yield return null;
            _game = Object.FindAnyObjectByType<SummerGame>();
            Assert.That(_game, Is.Not.Null);
            for (int i = 0; i < 60 && _game.Hud == null; i++) { yield return null; }
            Assert.That(_game.Hud, Is.Not.Null, "HUD が読み込まれる");
            Time.timeScale = 0f;
            _game.Render();
            yield return null;
        }

        private SummerFlow Flow => _game.Flow;

        private VisualElement Hud => _game.Hud!.Root;

        private bool Visible(string name) => Hud.Q(name).resolvedStyle.display == DisplayStyle.Flex;

        /// <summary>キーを 1 回押して離し、1 Tick 進める。</summary>
        private IEnumerator Press(Key key)
        {
            yield return _keyboard.Tap(key);
            Flow.StepTicks(1);
            _game.Render();
            yield return null;
        }

        /// <summary>メニューのカーソルを index まで ↓ で動かして Enter（キー入力だけで）。</summary>
        private IEnumerator Choose(int index)
        {
            var count = Flow.MenuItems.Count;
            var steps = ((index - Flow.MenuCursor) % count + count) % count;
            for (int i = 0; i < steps; i++) { yield return Press(Key.DownArrow); }
            Assert.That(Flow.MenuCursor, Is.EqualTo(index));
            yield return Press(Key.Enter);
        }

        /// <summary>操縦役の判断をキー入力に変えて 1 手進める（会話は全文を出してから送る）。</summary>
        private IEnumerator PilotStep(Autopilot pilot)
        {
            if (Flow.View == PlayView.Morning)
            {
                var before = Flow.Commands;
                yield return Choose(pilot.ChooseAction(Flow.Summer!));
                Assert.That(Flow.Commands, Is.EqualTo(before + 1), "行動が実行された");
            }
            else if (Flow.IsChoice)
            {
                var before = Flow.Commands;
                yield return Choose(pilot.ChooseOption(Flow.Summer!));
                Assert.That(Flow.Commands, Is.EqualTo(before + 1), "選択肢が実行された");
            }
            else
            {
                if (Flow.InText && !Flow.IsRevealed) { yield return Press(Key.Space); }
                yield return Press(Key.Enter);
            }
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

        private IEnumerator Click(Vector2 screen)
        {
            _mouse.Press(screen);
            yield return null;
            _mouse.Release(screen);
            yield return null;
            Flow.StepTicks(1);
            _game.Render();
            yield return null;
        }

        // ---- Prefab と素材 ----

        [UnityTest]
        public IEnumerator StageMatchesBookAssets_AndBrokenStageIsDetected()
        {
            yield return null;
            Assert.That(_game.Stage.Mismatch(_game.Book), Is.Null);
            _game.Settings.Validate();
            var copy = Object.Instantiate(_game.Stage.gameObject).GetComponent<SummerStage>();
            try
            {
                copy.Portrait.transform.localPosition += Vector3.right;
                Assert.That(copy.Mismatch(_game.Book), Does.Contain("位置"));
                copy.Portrait.transform.localPosition = SummerStage.PortraitPosition;
                copy.Portrait.sortingOrder = copy.Background.sortingOrder - 1;
                Assert.That(copy.Mismatch(_game.Book), Does.Contain("背景より奥"));
            }
            finally
            {
                Object.Destroy(copy.gameObject);
            }
        }

        // ---- キー操作 ----

        [UnityTest]
        public IEnumerator Keys_StartSummer_ChooseAction_AndShowReport()
        {
            Assert.That(Visible("title"), Is.True);
            Assert.That(Visible("date"), Is.False);
            yield return Press(Key.Enter);
            Assert.That(Flow.Screen, Is.EqualTo(FlowScreen.Playing));
            Assert.That(Visible("date"), Is.True);
            Assert.That(Visible("stats"), Is.True);
            Assert.That(Visible("actions"), Is.True);
            Assert.That(Hud.Q<Label>("day").text, Is.EqualTo("1 日目 / 30"));
            Assert.That(Hud.Q<Label>("action-row-0-label").text, Is.EqualTo("勉強"));
            Assert.That(Hud.Q<Label>("action-row-0-cursor").text, Is.EqualTo("▶"));
            Assert.That(_game.Stage.Background.sprite.name, Is.EqualTo("BG_room"));
            Assert.That(_store.Exists, Is.True, "朝の自動セーブ");

            yield return Choose(_game.Book.ActionIndex("exercise"));
            Assert.That(Flow.View, Is.EqualTo(PlayView.Report));
            Assert.That(Visible("report"), Is.True);
            Assert.That(Visible("actions"), Is.False);
            Assert.That(Hud.Q<Label>("report-title").text.StartsWith("運動 — ", StringComparison.Ordinal), Is.True);
            Assert.That(_game.Stage.Background.sprite.name, Is.EqualTo("BG_town"));
        }

        [UnityTest]
        public IEnumerator TextReveal_Speaker_Portrait_AndChoiceByMouse()
        {
            yield return Press(Key.Enter);
            yield return Choose(_game.Book.ActionIndex("club"));
            yield return Press(Key.Enter);
            Assert.That(Flow.View, Is.EqualTo(PlayView.Scene));
            Assert.That(Flow.SceneIndex, Is.EqualTo(_game.Book.EventIndex("e_sumi_first")));
            Assert.That(Visible("window"), Is.True);
            Assert.That(Visible("name-plate"), Is.True);
            Assert.That(Hud.Q<Label>("name").text, Is.EqualTo("星野澄"));
            var text = Hud.Q<Label>("text");
            Assert.That(text.text.Length, Is.LessThan(Flow.FullText.Length), "文字送りの途中");
            Assert.That(_game.Stage.Portrait.enabled, Is.True);
            Assert.That(_game.Stage.Portrait.sprite.name, Is.EqualTo("Portrait_sumi_normal"));
            Assert.That(_game.Stage.Background.sprite.name, Is.EqualTo("BG_club"));
            yield return Press(Key.Space);
            Assert.That(text.text, Is.EqualTo(Flow.FullText), "決定で全文");
            yield return Press(Key.Enter);
            yield return Press(Key.Space);
            Assert.That(_game.Stage.Portrait.sprite.name, Is.EqualTo("Portrait_sumi_smile"), "表情が行に合わせて変わる");
            yield return Press(Key.Enter);
            Assert.That(Flow.IsChoice, Is.True);
            Assert.That(Visible("choices"), Is.True);
            Assert.That(Visible("window"), Is.False);

            yield return Click(ScreenPointIn(Hud.Q("option-1")));
            Assert.That(Flow.IsChoice, Is.False, "選択肢をクリックで選んだ");
            Assert.That(Flow.Summer!.Flag(_game.Book.FlagIndex("met_sumi")), Is.True);
            var chosen = -1;
            foreach (var e in Flow.Summer.Log)
            {
                if (e.Kind == Game.Core.Play.LogKind.Choice) { chosen = e.Value; }
            }

            Assert.That(chosen, Is.EqualTo(1), "2 つ目を選んだ");
        }

        [UnityTest]
        public IEnumerator MouseClickOnActionRow_AndOnDisabledRow()
        {
            yield return Click(ScreenPointIn(Hud.Q("title-row-0")));
            Assert.That(Flow.Screen, Is.EqualTo(FlowScreen.Playing), "はじめからをクリック");
            yield return Click(ScreenPointIn(Hud.Q("action-row-1")));
            Assert.That(Flow.Commands, Is.EqualTo(1));
            Assert.That(Flow.ReportTitle.StartsWith("運動", StringComparison.Ordinal), Is.True);

            // 日曜まで進めて、閉まっている図書室の行をクリックしても進まない。
            var pilot = new Autopilot(_game.Book, PilotPolicy.Sumi, 3);
            for (int i = 0; i < 400 && !(Flow.Day == 7 && Flow.View == PlayView.Morning); i++) { yield return PilotStep(pilot); }
            Assert.That(Flow.Day, Is.EqualTo(7));
            var commands = Flow.Commands;
            yield return Click(ScreenPointIn(Hud.Q("action-row-" + _game.Book.ActionIndex("library"))));
            Assert.That(Flow.Commands, Is.EqualTo(commands));
            Assert.That(Visible("notice"), Is.True);
            Assert.That(Hud.Q<Label>("notice").text, Is.EqualTo("日曜日は選べない"));
            Assert.That(Hud.Q<Label>("action-row-" + _game.Book.ActionIndex("library") + "-detail").text, Is.EqualTo("日曜日は選べない"));
        }

        [UnityTest]
        public IEnumerator Detail_TogglesWithTab()
        {
            yield return Press(Key.Enter);
            Assert.That(Visible("detail"), Is.False);
            yield return Press(Key.Tab);
            Assert.That(Visible("detail"), Is.True);
            Assert.That(Hud.Q<Label>("detail-body").text.Contains("学力　10 / 100", StringComparison.Ordinal), Is.True);
            var hints = Hud.Q<Label>("detail-hints").text;
            Assert.That(hints.StartsWith("結末の目安", StringComparison.Ordinal), Is.True, hints);
            foreach (var h in _game.Book.Hints) { Assert.That(hints.Contains(h, StringComparison.Ordinal), Is.True, h); }
            yield return Press(Key.Tab);
            Assert.That(Visible("detail"), Is.False);
        }

        [UnityTest]
        public IEnumerator Detail_IsModal_ClicksAndKeysDoNotReachBehind()
        {
            yield return Press(Key.Enter);
            yield return Press(Key.Tab);
            Assert.That(Visible("detail"), Is.True);
            var hash = Flow.SummerHash;
            yield return Click(ScreenPointIn(Hud.Q("action-row-0")));
            yield return Press(Key.Enter);
            yield return Press(Key.DownArrow);
            Assert.That(Flow.Commands, Is.EqualTo(0));
            Assert.That(Flow.SummerHash, Is.EqualTo(hash));
            Assert.That(Flow.Day, Is.EqualTo(1));
            yield return Press(Key.Escape);
            Assert.That(Visible("detail"), Is.False);
            Assert.That(Flow.Screen, Is.EqualTo(FlowScreen.Playing), "Esc は詳細を閉じるだけ");
            yield return Press(Key.Enter);
            Assert.That(Flow.Commands, Is.EqualTo(1), "閉じた後は選べる");
        }

        // ---- 通し ----

        [UnityTest]
        public IEnumerator KeysOnly_ThirtyDaysToTheEndingAndBackToTitle()
        {
            yield return Press(Key.Enter);
            var pilot = new Autopilot(_game.Book, PilotPolicy.Shiori, 5);
            for (int i = 0; i < 1500 && Flow.Screen != FlowScreen.Result; i++) { yield return PilotStep(pilot); }
            Assert.That(Flow.Screen, Is.EqualTo(FlowScreen.Result));
            Assert.That(Visible("result"), Is.True);
            Assert.That(_game.Book.Endings[Flow.EndingIndex].Id, Is.EqualTo("shiori"));
            Assert.That(Hud.Q<Label>("result-title").text, Is.EqualTo("― 栞のはさまった夏 ―"));
            Assert.That(_store.Exists, Is.False, "ED に着いたらセーブを消す");
            yield return Press(Key.Enter);
            Assert.That(Flow.Screen, Is.EqualTo(FlowScreen.Title));
            Assert.That(Flow.TitleItems[SummerFlow.TitleContinue].Enabled, Is.False);
        }

        // ---- セーブ ----

        [UnityTest]
        public IEnumerator AutoSave_SurvivesASceneReload_AndContinues()
        {
            yield return Press(Key.Enter);
            var pilot = new Autopilot(_game.Book, PilotPolicy.Sumi, 9);
            for (int i = 0; i < 400 && !(Flow.Day == 4 && Flow.View == PlayView.Morning); i++) { yield return PilotStep(pilot); }
            Assert.That(Flow.Day, Is.EqualTo(4));
            var hash = Flow.SummerHash;

            // 起動し直す（シーンを読み直す）。
            yield return LoadScene();
            Assert.That(Flow.Screen, Is.EqualTo(FlowScreen.Title));
            Assert.That(Flow.TitleItems[SummerFlow.TitleContinue].Enabled, Is.True);
            Assert.That(Hud.Q<Label>("title-detail").text, Is.EqualTo("つづきから：4 日目の朝から"));
            yield return Choose(SummerFlow.TitleContinue);
            Assert.That(Flow.Screen, Is.EqualTo(FlowScreen.Playing));
            Assert.That(Flow.Day, Is.EqualTo(4));
            Assert.That(Flow.SummerHash, Is.EqualTo(hash));
        }

        [UnityTest]
        public IEnumerator BrokenSave_IsExplainedOnTheTitle()
        {
            Assert.That(_store.TryWrite("kind=summer\nversion=1\nbroken", out _), Is.True);
            yield return LoadScene();
            Assert.That(Flow.TitleItems[SummerFlow.TitleContinue].Enabled, Is.False);
            Assert.That(Visible("title-detail"), Is.True);
            Assert.That(Hud.Q<Label>("title-detail").text.StartsWith("セーブを読めませんでした", StringComparison.Ordinal), Is.True);
        }

        // ---- タイトルへ戻った表示・一時停止・終了 ----

        [UnityTest]
        public IEnumerator BackToTitle_LooksLikeAFreshStart()
        {
            yield return null;
            var bg = _game.Stage.Background.sprite.name;
            Assert.That(bg, Is.EqualTo("BG_roof"));
            Assert.That(_game.Stage.Portrait.enabled, Is.False);

            yield return Press(Key.Enter);
            yield return Choose(_game.Book.ActionIndex("club"));
            yield return Press(Key.Enter);
            Assert.That(_game.Stage.Portrait.enabled, Is.True);
            yield return Press(Key.Escape);
            Assert.That(Visible("pause"), Is.True);
            yield return Choose(SummerFlow.PauseTitle);
            Assert.That(Flow.Screen, Is.EqualTo(FlowScreen.Title));
            Assert.That(_game.Stage.Background.sprite.name, Is.EqualTo(bg));
            Assert.That(_game.Stage.Portrait.enabled, Is.False);
            foreach (var name in new[] { "date", "stats", "actions", "report", "window", "choices", "detail", "pause", "result", "notice" })
            {
                Assert.That(Visible(name), Is.False, name);
            }

            Assert.That(Visible("title"), Is.True);
        }

        [UnityTest]
        public IEnumerator FocusLoss_Pauses_AndKeysAreIgnoredUntilResumed()
        {
            yield return Press(Key.Enter);
            _game.SendMessage("OnApplicationFocus", false);
            _game.Render();
            yield return null;
            Assert.That(Flow.Screen, Is.EqualTo(FlowScreen.Paused));
            Assert.That(Visible("pause"), Is.True);
            yield return Press(Key.Enter);
            Assert.That(Flow.Screen, Is.EqualTo(FlowScreen.Paused), "フォーカスのない間のキーは当てない");
            _game.SendMessage("OnApplicationFocus", true);
            yield return null;
            Assert.That(Flow.Screen, Is.EqualTo(FlowScreen.Paused), "戻っても一時停止のまま");
            yield return Press(Key.Escape);
            Assert.That(Flow.Screen, Is.EqualTo(FlowScreen.Playing));
            Assert.That(Flow.Commands, Is.EqualTo(0));
        }

        [UnityTest]
        public IEnumerator QuitFromTitle_CallsTheQuitHandlerOnce()
        {
            yield return Choose(SummerFlow.TitleQuit);
            yield return null;
            yield return null;
            Assert.That(_quits, Is.EqualTo(1));
        }
    }
}
