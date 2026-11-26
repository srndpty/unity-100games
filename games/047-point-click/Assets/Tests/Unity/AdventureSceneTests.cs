#nullable enable
using System.Collections;
using System.Linq;
using Game.App;
using Game.Core;
using Game.Core.Rules;
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
    public sealed class AdventureSceneTests
    {
        private const string SceneName = "Tidewatch";

        private TestKeyboard _keyboard = null!;
        private TestMouse _mouse = null!;
        private AdventureGame _game = null!;
        private int _quits;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _quits = 0;
            AdventureGame.QuitHandler = () => _quits++;
            _keyboard = new TestKeyboard();
            _mouse = new TestMouse();
            yield return LoadScene();
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            Time.timeScale = 1f;
            AdventureGame.QuitHandler = Application.Quit;
            _mouse.Dispose();
            _keyboard.Dispose();
            yield return null;
        }

        private AdventureFlow Flow => _game.Flow;

        private RuleBook Book => _game.Book;

        private VisualElement Hud => _game.Hud!.Root;

        private IEnumerator LoadScene()
        {
            yield return SceneManager.LoadSceneAsync(SceneName);
            yield return null;
            _game = Object.FindAnyObjectByType<AdventureGame>();
            Assert.That(_game, Is.Not.Null);
            for (int i = 0; i < 60 && _game.Hud == null; i++) { yield return null; }
            Assert.That(_game.Hud, Is.Not.Null, "HUD が読み込まれる");
            Time.timeScale = 0f;
            _mouse.Move(new Vector2(1, 1));
            _game.Render();
            yield return null;
        }

        private bool Visible(string name) => Hud.Q(name).resolvedStyle.display == DisplayStyle.Flex;

        /// <summary>キーを 1 回押して離し、1 Tick 進める。</summary>
        private IEnumerator Press(Key key)
        {
            yield return _keyboard.Tap(key);
            Flow.StepTicks(1);
            _game.Render();
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

        /// <summary>ルール表の物の矩形の中心の画面の座標。</summary>
        private Vector2 ScreenPointOf(string hotspot)
        {
            var a = Book.Hotspots[Book.HotspotIndex(hotspot)].Area;
            return _game.ScreenPoint(new Vector2Int((a.MinX + a.MaxX) / 2, (a.MinY + a.MaxY) / 2));
        }

        private IEnumerator Click(Vector2 screen, bool right = false)
        {
            if (right) { _mouse.PressRight(screen); }
            else { _mouse.Press(screen); }
            yield return null;
            _mouse.Release(screen);
            yield return null;
            Flow.StepTicks(1);
            _game.Render();
            yield return null;
        }

        /// <summary>歩き終えるまで Tick を進める。</summary>
        private IEnumerator Settle()
        {
            for (int i = 0; i < 3000 && Flow.IsWalking; i++) { Flow.StepTicks(1); }
            Assert.That(Flow.IsWalking, Is.False);
            _game.Render();
            yield return null;
        }

        /// <summary>Enter で台詞を全て送る。</summary>
        private IEnumerator SkipLines()
        {
            for (int i = 0; i < 20 && Flow.CurrentLine != null; i++) { yield return Press(Key.Enter); }
            Assert.That(Flow.CurrentLine, Is.Null);
        }

        private IEnumerator Start()
        {
            yield return Press(Key.Enter);
            Assert.That(Flow.Screen, Is.EqualTo(FlowScreen.Playing));
        }

        // ---- Prefab とシーン ----

        [UnityTest]
        public IEnumerator StageMatchesRules_AndTitleShowsTheCottage()
        {
            yield return null;
            Assert.That(_game.Stage.Mismatch(Book), Is.Null);
            _game.Settings.Validate();
            Assert.That(Visible("title"), Is.True);
            Assert.That(Visible("bar"), Is.False);
            Assert.That(_game.Stage.RoomRoots[Book.StartRoom].gameObject.activeSelf, Is.True);
            Assert.That(_game.Stage.RoomRoots[Book.RoomIndex("dock")].gameObject.activeSelf, Is.False);
            Assert.That(_game.Stage.Player.enabled, Is.False);
        }

        // ---- キーとマウスの通し ----

        [UnityTest]
        public IEnumerator VerbKeyThenClickOnKnife_WalksAndPicksItUp()
        {
            yield return Start();
            yield return Press(Key.Digit2);
            Assert.That(Flow.SelectedVerb, Is.EqualTo(Verb.Take));
            Assert.That(Hud.Q<Label>("sentence").text, Is.EqualTo("取る"));
            yield return Click(ScreenPointOf("cottage.knife"));
            Assert.That(Flow.IsWalking, Is.True, "包丁の前まで歩く");
            var x = _game.Stage.Player.transform.localPosition.x;
            Flow.StepTicks(3);
            _game.Render();
            Assert.That(_game.Stage.Player.transform.localPosition.x, Is.LessThan(x), "左へ歩いている姿を描く");
            yield return Settle();
            Assert.That(Flow.Inventory, Is.EqualTo(new[] { Book.ItemIndex("knife") }));
            Assert.That(_game.Stage.Hotspots[Book.HotspotIndex("cottage.knife")].enabled, Is.False, "拾った包丁は部屋から消える");
            Assert.That(Visible("line"), Is.True);
            Assert.That(Hud.Q<Label>("line-text").text, Is.EqualTo("包丁を手に取った。"));
            yield return null;
            Assert.That(_game.Hud!.IsSlotIconShown(0), Is.True, "持ち物の枠に絵が出る");
            Assert.That(_game.Hud.IsSlotIconShown(1), Is.False);
        }

        [UnityTest]
        public IEnumerator HoverShowsObjectNameInSentence()
        {
            yield return Start();
            _mouse.Move(ScreenPointOf("cottage.stove"));
            yield return null;
            _game.Render();
            yield return null;
            Assert.That(Hud.Q<Label>("sentence").text, Is.EqualTo("歩く ストーブ"));
            _mouse.Move(ScreenPointIn(_game.Hud!.Verb(0)));
            yield return null;
            _game.Render();
            Assert.That(Hud.Q<Label>("sentence").text, Is.EqualTo("歩く"));
        }

        [UnityTest]
        public IEnumerator VerbButtonAndItemSlot_BuildUseCommand_RightClickClears()
        {
            yield return Start();
            yield return Click(ScreenPointIn(_game.Hud!.Verb(1)));
            Assert.That(Flow.SelectedVerb, Is.EqualTo(Verb.Take), "動詞のボタンをクリック");
            yield return Click(ScreenPointOf("cottage.cup"));
            yield return Settle();
            yield return SkipLines();
            yield return Click(ScreenPointIn(_game.Hud.Slot(0)));
            Assert.That(Flow.SelectedTool, Is.EqualTo(Book.ItemIndex("cup")));
            _mouse.Move(ScreenPointOf("cottage.stove"));
            yield return null;
            _game.Render();
            Assert.That(Hud.Q<Label>("sentence").text, Is.EqualTo("使う カップ → ストーブ"));
            yield return Click(ScreenPointOf("cottage.stove"), right: true);
            Assert.That(Flow.SelectedVerb, Is.EqualTo(Verb.Walk), "右クリックで選択を外す");
            Assert.That(Flow.SelectedTool, Is.EqualTo(-1));
            Assert.That(Flow.IsWalking, Is.False, "右クリックでは歩かない");
        }

        // ---- 題材固有：所持しないアイテムを使用できない（画面から） ----

        [UnityTest]
        public IEnumerator EmptySlotCannotBeUsed()
        {
            yield return Start();
            yield return Click(ScreenPointIn(_game.Hud!.Slot(0)));
            yield return Click(ScreenPointIn(_game.Hud.Slot(5)));
            Assert.That(Flow.SelectedTool, Is.EqualTo(-1), "空の枠は選べない");
            Assert.That(Flow.SelectedVerb, Is.EqualTo(Verb.Walk));
            Assert.That(Flow.IsWalking, Is.False, "帯の上のクリックは部屋に通さない");
            Assert.That(Flow.CurrentLine, Is.Null);
            Assert.That(Flow.ActionCount, Is.EqualTo(0));
        }

        // ---- 完了判定：アイテム状態が移動後も保持（画面から） ----

        [UnityTest]
        public IEnumerator PickedUpItemsStayAfterLeavingAndReturning()
        {
            yield return Start();
            yield return Press(Key.Digit2);
            yield return Click(ScreenPointOf("cottage.knife"));
            yield return Settle();
            yield return SkipLines();
            yield return Click(ScreenPointOf("cottage.door"));
            yield return Settle();
            Assert.That(Flow.Room, Is.EqualTo(Book.RoomIndex("dock")));
            Assert.That(_game.Stage.RoomRoots[Book.RoomIndex("dock")].gameObject.activeSelf, Is.True);
            Assert.That(_game.Stage.RoomRoots[Book.StartRoom].gameObject.activeSelf, Is.False);
            Assert.That(Hud.Q<Label>("room-name").text, Is.EqualTo("船着き場"));
            yield return Click(ScreenPointOf("dock.path"));
            yield return Settle();
            Assert.That(Flow.Room, Is.EqualTo(Book.StartRoom));
            Assert.That(Flow.Inventory, Is.EqualTo(new[] { Book.ItemIndex("knife") }));
            Assert.That(_game.Stage.Hotspots[Book.HotspotIndex("cottage.knife")].enabled, Is.False, "戻っても包丁は机に戻らない");
            Assert.That(_game.Stage.Hotspots[Book.HotspotIndex("cottage.cup")].enabled, Is.True);
        }

        // ---- 会話 ----

        [UnityTest]
        public IEnumerator TalkToCaptainAndChooseTopicByClick()
        {
            yield return Start();
            yield return Click(ScreenPointOf("cottage.door"));
            yield return Settle();
            yield return Press(Key.Digit4);
            yield return Click(ScreenPointOf("dock.captain"));
            yield return Settle();
            Assert.That(Hud.Q<Label>("line-text").text, Is.EqualTo("おう、灯台守の見習いか。ひどい嵐だな。"));
            yield return SkipLines();
            Assert.That(Visible("topics"), Is.True);
            Assert.That(Visible("bar"), Is.True);
            Assert.That(Hud.Q<Label>("topic-row-0-label").text, Is.EqualTo("町へ戻れませんか？"));
            yield return Click(ScreenPointOf("dock.net"));
            Assert.That(Flow.IsWalking, Is.False, "会話中は部屋のクリックで歩かない");
            yield return Click(ScreenPointIn(_game.Hud!.Topic(0)));
            Assert.That(Hud.Q<Label>("line-text").text, Is.EqualTo("町へ戻れませんか？"));
            yield return Click(ScreenPointIn(Hud.Q("line")));
            Assert.That(Hud.Q<Label>("line-name").text, Is.EqualTo("船頭"));
            yield return SkipLines();
            Assert.That(Hud.Q<Label>("topic-row-1-label").text, Is.EqualTo("火を持っていませんか？"), "聞いた後に新しい話題が出る");
            yield return Press(Key.UpArrow);
            yield return Press(Key.Enter);
            Assert.That(Flow.IsTalking, Is.False, "↑ で「さようなら」を選んで終える");
            Assert.That(Visible("topics"), Is.False);
        }

        // ---- 一時停止・フォーカス ----

        [UnityTest]
        public IEnumerator EscPausesWalkingAndResumes()
        {
            yield return Start();
            yield return Click(_game.ScreenPoint(new Vector2Int(1600, 300)));
            Flow.StepTicks(2);
            var x = Flow.PlayerX;
            yield return Press(Key.Escape);
            Assert.That(Visible("pause"), Is.True);
            Flow.StepTicks(30);
            Assert.That(Flow.PlayerX, Is.EqualTo(x));
            yield return Press(Key.Escape);
            Flow.StepTicks(5);
            Assert.That(Flow.PlayerX, Is.GreaterThan(x));
        }

        [UnityTest]
        public IEnumerator ClickRightAfterFocusRestoreIsIgnored()
        {
            yield return Start();
            var x = Flow.PlayerX;
            _game.SendMessage("OnApplicationFocus", false);
            _game.SendMessage("OnApplicationFocus", true);
            yield return Click(_game.ScreenPoint(new Vector2Int(1600, 300)));
            Assert.That(Flow.IsWalking, Is.False, "ウィンドウを選び直したクリックでは歩かない");
            yield return Click(_game.ScreenPoint(new Vector2Int(1600, 300)));
            Assert.That(Flow.IsWalking, Is.True);
            yield return Settle();
            Assert.That(Flow.PlayerX, Is.Not.EqualTo(x));
        }

        // ---- 完了判定：全 5 謎を解ける（実シーンで結果画面まで）・タイトルへ戻ると起動直後と同じ ----

        [UnityTest]
        public IEnumerator WalkthroughReachesResult_AndTitleLooksLikeStartup()
        {
            var startup = Snapshot();
            yield return Start();
            var text = System.IO.File.ReadAllText(System.IO.Path.Combine(Application.dataPath, "Game", "Data", "Walkthrough.txt"));
            var pilot = new Autopilot(Adventure.ParseWalkthrough(Book, text));
            for (int i = 0; i < 40000 && Flow.Screen != FlowScreen.Result; i++)
            {
                pilot.Drive(Flow);
                Flow.StepTicks(1);
                if (i % 50 == 0)
                {
                    _game.Render();
                    yield return null;
                }
            }

            _game.Render();
            yield return null;
            Assert.That(Flow.Screen, Is.EqualTo(FlowScreen.Result));
            Assert.That(Flow.SolvedCount, Is.EqualTo(5));
            Assert.That(Visible("result"), Is.True);
            Assert.That(Hud.Q<Label>("result-stats").text.StartsWith("解いた謎　5 / 5", System.StringComparison.Ordinal), Is.True);
            Assert.That(_game.Stage.LampLit.enabled, Is.True, "灯ったランプを描く");
            Assert.That(_game.Stage.RoomRoots[Book.RoomIndex("lighthouse")].gameObject.activeSelf, Is.True);

            yield return Press(Key.Enter);
            Assert.That(Flow.Screen, Is.EqualTo(FlowScreen.Title));
            Assert.That(Snapshot(), Is.EqualTo(startup), "タイトルへ戻った表示が起動直後と同じ（021）");
        }

        [UnityTest]
        public IEnumerator QuitFromTitleByClick()
        {
            yield return Click(ScreenPointIn(_game.Hud!.MenuRow(FlowScreen.Title, AdventureFlow.TitleQuit)));
            yield return null;
            Assert.That(_quits, Is.EqualTo(1));
        }

        /// <summary>見た目を決める状態の要約（部屋・物の表示・主人公・灯・HUD の画面）。</summary>
        private string Snapshot()
        {
            _game.Render();
            var s = _game.Stage;
            var rooms = string.Concat(s.RoomRoots.Select(r => r.gameObject.activeSelf ? '1' : '0'));
            var hotspots = string.Concat(s.Hotspots.Select(h => h.enabled ? '1' : '0'));
            return $"{rooms}|{hotspots}|{s.Player.enabled}|{s.LampLit.enabled}|{Visible("title")}|{Visible("bar")}|{Visible("line")}|{Visible("result")}";
        }
    }
}
