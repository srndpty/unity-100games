#nullable enable
using System;
using System.Collections;
using System.Diagnostics;
using System.IO;
using Game.App;
using Game.Core;
using Game.Core.Runtime;
using Game.Core.Save;
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
    /// 入力は仮想キーボードと仮想マウスから送り、確かめたい処理をテスト側から呼ばない（013）。
    /// </summary>
    public sealed class NovelSceneTests
    {
        private const string SceneName = "LanternWake";

        private TestKeyboard _keyboard = null!;
        private TestMouse _mouse = null!;
        private string _dir = "";
        private FileSaveStore _quick = null!;
        private FileSaveStore _read = null!;
        private NovelGame _game = null!;
        private int _quits;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _dir = Path.Combine(Path.GetTempPath(), "lantern_playmode_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
            _quick = new FileSaveStore(Path.Combine(_dir, "quick.txt"));
            _read = new FileSaveStore(Path.Combine(_dir, "read.txt"));
            NovelGame.SetStoresForTests(_quick, _read);
            _quits = 0;
            NovelGame.QuitHandler = () => _quits++;
            _keyboard = new TestKeyboard();
            _mouse = new TestMouse();
            yield return LoadScene();
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            Time.timeScale = 1f;
            NovelGame.SetStoresForTests(null, null);
            NovelGame.QuitHandler = Application.Quit;
            _mouse.Dispose();
            _keyboard.Dispose();
            if (Directory.Exists(_dir)) { Directory.Delete(_dir, true); }
            yield return null;
        }

        private IEnumerator LoadScene()
        {
            yield return SceneManager.LoadSceneAsync(SceneName);
            yield return null;
            _game = Object.FindAnyObjectByType<NovelGame>();
            Assert.That(_game, Is.Not.Null);
            for (int i = 0; i < 60 && _game.Hud == null; i++) { yield return null; }
            Assert.That(_game.Hud, Is.Not.Null, "HUD が読み込まれる");
            Time.timeScale = 0f;
            _game.Render();
            yield return null;
        }

        private NovelFlow Flow => _game.Flow;

        private VisualElement Hud => _game.Hud!.Root;

        private bool Visible(string name) => Hud.Q(name).resolvedStyle.display == DisplayStyle.Flex;

        /// <summary>キーを 1 回押して離し、1 Tick 進める。</summary>
        private IEnumerator Press(Key key)
        {
            yield return _keyboard.Tap(key);
            Flow.StepTicks(1);
            _game.Render();
        }

        /// <summary>Enter で文を全文にして送る（キー入力だけで）。</summary>
        private IEnumerator Next()
        {
            if (!Flow.IsRevealed) { yield return Press(Key.Enter); }
            yield return Press(Key.Enter);
        }

        private IEnumerator ToChoice()
        {
            for (int i = 0; i < 400 && !Flow.IsChoice; i++) { yield return Next(); }
            Assert.That(Flow.IsChoice, Is.True);
            yield return null;
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
        }

        // ---- Prefab と素材 ----

        [UnityTest]
        public IEnumerator StageMatchesScenarioAssets_AndBrokenStageIsDetected()
        {
            yield return null;
            Assert.That(_game.Stage.Mismatch(), Is.Null);
            _game.Settings.Validate();
            var copy = Object.Instantiate(_game.Stage.gameObject).GetComponent<NovelStage>();
            try
            {
                copy.Slots[1].transform.localPosition += Vector3.right;
                Assert.That(copy.Mismatch(), Does.Contain("位置"));
                copy.Slots[1].transform.localPosition = NovelStage.SlotPositions[1];
                copy.Slots[2].sortingOrder = copy.Background.sortingOrder - 1;
                Assert.That(copy.Mismatch(), Does.Contain("背景より奥"));
            }
            finally
            {
                Object.Destroy(copy.gameObject);
            }
        }

        // ---- キー操作の通し ----

        [UnityTest]
        public IEnumerator KeysStartStoryRevealTextAndReachFirstChoice()
        {
            Assert.That(Visible("title"), Is.True);
            yield return Press(Key.Enter);
            Assert.That(Flow.Screen, Is.EqualTo(FlowScreen.Reading));
            Assert.That(Visible("window"), Is.True);
            var text = Hud.Q<Label>("text");
            Assert.That(text.text.Length, Is.LessThan(Flow.FullText.Length), "文字送りの途中");
            yield return Press(Key.Space);
            Assert.That(text.text, Is.EqualTo(Flow.FullText), "決定で全文");
            Assert.That(_game.Stage.Background.sprite.name, Is.EqualTo("BG_lighthouse"));
            yield return ToChoice();
            Assert.That(Visible("choices"), Is.True);
            Assert.That(Visible("window"), Is.False);
            Assert.That(Hud.Q<Label>("option-row-1-label").text, Is.EqualTo("澪と宗助の部屋を調べる"));
            yield return Press(Key.DownArrow);
            Assert.That(Hud.Q<Label>("option-row-1-cursor").text, Is.EqualTo("▶"));
            yield return Press(Key.Enter);
            Assert.That(Flow.Runner!.Value("trust"), Is.EqualTo(1));
            Assert.That(Flow.Runner.Value("diary"), Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator SpeakerNameAndPortraitsFollowTheScript()
        {
            yield return Press(Key.Enter);
            for (int i = 0; i < 50 && Flow.SpeakerName.Length == 0; i++) { yield return Next(); }
            Assert.That(Visible("name-plate"), Is.True);
            Assert.That(Hud.Q<Label>("name").text, Is.EqualTo("宗助"));
            var center = _game.Stage.Slots[(int)StageSlot.Center];
            Assert.That(center.enabled, Is.True);
            Assert.That(center.sprite.name, Is.EqualTo("Portrait_sosuke_calm"));
            Assert.That(_game.Stage.Slots[(int)StageSlot.Left].enabled, Is.False);
        }

        // ---- マウス ----

        [UnityTest]
        public IEnumerator ClickOnTitleThenOnOptionChoosesIt()
        {
            yield return Click(ScreenPointIn(Hud.Q("title-row-0")));
            Assert.That(Flow.Screen, Is.EqualTo(FlowScreen.Reading), "はじめからをクリック");
            var before = Flow.ReadCount;
            yield return Click(ScreenPointIn(Hud.Q("window")));
            Assert.That(Flow.IsRevealed, Is.True, "表示途中のクリックで全文");
            yield return Click(ScreenPointIn(Hud.Q("window")));
            Assert.That(Flow.ReadCount, Is.EqualTo(before + 1), "次のクリックで送る");
            yield return ToChoice();
            yield return Click(ScreenPointIn(Hud.Q("option-2")));
            Assert.That(Flow.IsChoice, Is.False);
            Assert.That(Flow.Runner!.Value("clue"), Is.EqualTo(0), "3 つ目（蓮と広間に残る）を選んだ");
            Assert.That(Flow.Runner.Current.TextId, Does.StartWith("c1_stay#"));
        }

        [UnityTest]
        public IEnumerator ClickRightAfterFocusRestoreIsIgnored()
        {
            yield return Press(Key.Enter);
            Flow.StepTicks(300);
            var read = Flow.ReadCount;
            _game.SendMessage("OnApplicationFocus", false);
            _game.SendMessage("OnApplicationFocus", true);
            yield return Click(ScreenPointIn(Hud.Q("window")));
            Assert.That(Flow.ReadCount, Is.EqualTo(read), "ウィンドウを選び直したクリックでは送らない");
            yield return Click(ScreenPointIn(Hud.Q("window")));
            Assert.That(Flow.ReadCount, Is.EqualTo(read + 1));
        }

        // ---- 題材固有：未読を既読スキップしない（実シーン） ----

        [UnityTest]
        public IEnumerator HeldCtrlSkipsReadTextAndStopsAtFirstUnread()
        {
            // 最初の 10 文だけ既読の記録を作って読み直す。
            var log = new ReadLog(_game.Script);
            for (int i = 0; i < 10; i++) { log.MarkRead(_game.Script.TextIds[i]); }
            Assert.That(_read.TryWrite(NovelSaveCodec.EncodeRead(log), out var failure), Is.True, failure);
            yield return LoadScene();
            yield return Press(Key.Enter);
            Assert.That(Flow.IsCurrentRead, Is.True, "最初の文は既読");
            _keyboard.Hold(Key.LeftCtrl);
            yield return null;
            Flow.StepTicks(2);
            _game.Render();
            yield return null;
            Assert.That(Visible("skip"), Is.True, "SKIP を出す");
            Flow.StepTicks(200);
            _game.Render();
            Assert.That(Flow.Runner!.Current.TextId, Is.EqualTo(_game.Script.TextIds[10]), "最初の未読で止まる");
            Assert.That(Flow.IsCurrentRead, Is.False);
            _keyboard.Hold();
            yield return null;
            Flow.StepTicks(1);
            _game.Render();
            yield return null;
            Assert.That(Visible("skip"), Is.False);
        }

        // ---- 題材固有：ロードで選択前の状態に戻る（実ファイル） ----

        [UnityTest]
        public IEnumerator F5AtChoiceThenF9ReturnsBeforeChoice_AndContinueAfterReload()
        {
            yield return Press(Key.Enter);
            yield return ToChoice();
            var saved = Flow.StoryHash;
            yield return Press(Key.F5);
            Assert.That(Visible("notice"), Is.True);
            Assert.That(File.Exists(Path.Combine(_dir, "quick.txt")), Is.True, "ファイルに書く");
            yield return Press(Key.Enter);
            yield return Next();
            Assert.That(Flow.Runner!.Value("clue"), Is.EqualTo(1));
            yield return Press(Key.F9);
            Assert.That(Flow.IsChoice, Is.True);
            Assert.That(Flow.StoryHash, Is.EqualTo(saved));
            Assert.That(Flow.Runner.Value("clue"), Is.EqualTo(0));

            // シーンを読み直して、タイトルの「つづきから」で同じ所から。
            yield return LoadScene();
            Assert.That(Flow.TitleItems[NovelFlow.TitleContinue].Enabled, Is.True);
            yield return Press(Key.DownArrow);
            yield return Press(Key.Enter);
            Assert.That(Flow.Screen, Is.EqualTo(FlowScreen.Reading));
            Assert.That(Flow.StoryHash, Is.EqualTo(saved));
            Assert.That(Visible("choices"), Is.True);
        }

        [UnityTest]
        public IEnumerator BrokenQuickSaveShowsReasonOnTitle()
        {
            Assert.That(_quick.TryWrite("kind=quick\nversion=1\n", out _), Is.True);
            yield return LoadScene();
            Assert.That(Flow.TitleItems[NovelFlow.TitleContinue].Enabled, Is.False);
            Assert.That(Visible("title-detail"), Is.True);
            Assert.That(Hud.Q<Label>("title-detail").text, Does.StartWith("クイックセーブを読めませんでした"));
            yield return Press(Key.DownArrow);
            yield return Press(Key.Enter);
            Assert.That(Flow.Screen, Is.EqualTo(FlowScreen.Title), "選べない");
        }

        // ---- 結末・タイトルへ戻る・終了 ----

        [UnityTest]
        public IEnumerator EachEndingShowsResult_AndTitleAfterwardsMatchesFreshStart()
        {
            yield return null;
            var freshBg = _game.Stage.Background.sprite.name;
            foreach (var ending in new[] { "truth", "normal", "bad" })
            {
                new ChoicePilot(Scenario.RouteTo(_game.Compiled, ending)).RunToEnd(Flow);
                _game.Render();
                yield return null;
                Assert.That(Visible("result"), Is.True);
                Assert.That(Hud.Q<Label>("result-title").text, Is.EqualTo($"― {Flow.EndingName} ―"));
                yield return Press(Key.Enter);
                yield return null;
                Assert.That(Visible("title"), Is.True);

                // タイトルへ戻った表示は起動直後と同じ（021）。
                Assert.That(_game.Stage.Background.sprite.name, Is.EqualTo(freshBg));
                foreach (var s in _game.Stage.Slots) { Assert.That(s.enabled, Is.False); }
                Assert.That(Visible("window"), Is.False);
                Assert.That(Visible("choices"), Is.False);
            }

            Assert.That(Hud.Q<Label>("title-endings").text, Does.Contain("3 / 3"));
            Assert.That(File.Exists(Path.Combine(_dir, "read.txt")), Is.True, "既読と到達を書く");
        }

        [UnityTest]
        public IEnumerator HideWindowAndPauseToTitle()
        {
            yield return Press(Key.Enter);
            yield return Press(Key.H);
            Assert.That(Visible("window"), Is.False);
            yield return Press(Key.Enter);
            Assert.That(Visible("window"), Is.True);
            Assert.That(Flow.ReadCount, Is.EqualTo(0), "枠を戻す決定では送らない");
            yield return Press(Key.Escape);
            Assert.That(Visible("pause"), Is.True);
            yield return Press(Key.DownArrow);
            yield return Press(Key.Enter);
            Assert.That(Flow.Screen, Is.EqualTo(FlowScreen.Title));
            Assert.That(Visible("pause"), Is.False);
        }

        [UnityTest]
        public IEnumerator QuitFromTitleCallsQuitHandlerOnce()
        {
            yield return Press(Key.UpArrow);
            yield return Press(Key.Enter);
            yield return null;
            yield return null;
            Assert.That(_quits, Is.EqualTo(1));
        }

        // ---- 計測：シナリオのコンパイル（Mono） ----

        [UnityTest]
        public IEnumerator ScenarioCompileTime()
        {
            yield return null;
            var text = File.ReadAllText(Path.Combine(Application.dataPath, "Game", "Data", "Scenario.txt"));
            var times = new double[10];
            for (int i = 0; i < times.Length; i++)
            {
                var sw = Stopwatch.StartNew();
                var r = Scenario.Compile(text);
                sw.Stop();
                Assert.That(r.Ok, Is.True);
                times[i] = sw.Elapsed.TotalMilliseconds;
            }

            Array.Sort(times);
            Debug.Log($"[NovelSceneTests] シナリオのコンパイル（網羅探索を含む）：中央値 {times[5]:F2} ms・最大 {times[9]:F2} ms（10 回、Mono）");
            Assert.That(times[5], Is.LessThan(1000));
        }
    }
}
