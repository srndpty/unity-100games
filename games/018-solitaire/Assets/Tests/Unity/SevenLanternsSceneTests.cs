using System.Collections;
using Game.App;
using Game.Core;
using NUnit.Framework;
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
    public sealed class SevenLanternsSceneTests
    {
        private const float WaitLimitSeconds = 5f;

        private TestKeyboard _keyboard;
        private Mouse _mouse;
        private SevenLanternsGame _game;

        [UnitySetUp]
        public IEnumerator Load()
        {
            _keyboard = new TestKeyboard();
            _mouse = InputSystem.AddDevice<Mouse>();
            yield return SceneManager.LoadSceneAsync("SevenLanterns");
            yield return null;
            _game = Object.FindAnyObjectByType<SevenLanternsGame>();
            for (var i = 0; i < 30 && _game.HudRoot == null; i++) { yield return null; }
            Assert.That(_game.HudRoot, Is.Not.Null);
        }

        [UnityTearDown]
        public IEnumerator Unload()
        {
            InputSystem.RemoveDevice(_mouse);
            _keyboard.Dispose();
            yield return null;
        }

        private SolitaireFlow Flow => _game.Flow;

        private SolitaireGame Game => Flow.Game;

        private IReadOnlyTable Table => Game.Table;

        private SevenLanternsTableView View => _game.View;

        private static PileId T(int column) => PileId.Tableau(column - 1);

        private string Text(string name) => _game.HudRoot.Q<TextElement>(name).text;

        private bool IsShown(string name) => _game.HudRoot.Q(name).style.display.value == DisplayStyle.Flex;

        private void SetFocus(bool focus) => _game.SendMessage("OnApplicationFocus", focus);

        private static Table Parse(params string[] lines) => TableText.Parse(string.Join("\n", lines));

        private IEnumerator Begin(Table table = null)
        {
            if (table != null) { Flow.GameFactory = _ => new SolitaireGame(Flow.Config, table); }
            yield return _keyboard.Tap(Key.Enter);
            Assert.That(Flow.Screen, Is.EqualTo(AppScreen.Playing));
            yield return null;
        }

        private void SetMouse(Vector2 position, bool pressed = false)
        {
            var state = new MouseState { position = position };
            if (pressed) { state = state.WithButton(MouseButton.Left); }
            InputSystem.QueueStateEvent(_mouse, state);
        }

        private Vector2 Screen(TablePoint point) => _game.TableToScreen(point);

        /// <summary>札を掴む位置。一番上の札なら中心、下の札なら上の札に隠れていない上端の帯。</summary>
        private TablePoint Grip(PileId pile, int index)
        {
            var center = TableLayout.CardCenter(Table, pile, index);
            return index == Table.CountOf(pile) - 1 ? center : center + new TablePoint(0, (TableLayout.CardHeight / 2) - .08);
        }

        /// <summary>掴んだ札の底を、置き先の札に少しずらして重ねるときのポインタの位置。</summary>
        private TablePoint DropPoint(PileId from, int index, TablePoint grip, PileId to)
        {
            var count = Table.CountOf(to);
            var target = count == 0
                ? TableLayout.SlotCenter(to)
                : TableLayout.CardCenter(Table, to, count - 1) + new TablePoint(0, to.Kind == PileKind.Tableau ? -TableLayout.FaceUpStep : 0);
            return target + (grip - TableLayout.CardCenter(Table, from, index));
        }

        private IEnumerator Click(TablePoint point)
        {
            var position = Screen(point);
            SetMouse(position);
            yield return null;
            SetMouse(position, true);
            yield return null;
            SetMouse(position);
            yield return null;
        }

        private IEnumerator PressAndMove(TablePoint from, TablePoint to)
        {
            SetMouse(Screen(from));
            yield return null;
            SetMouse(Screen(from), true);
            yield return null;
            SetMouse(Vector2.Lerp(Screen(from), Screen(to), .5f), true);
            yield return null;
            SetMouse(Screen(to), true);
            yield return null;
        }

        private IEnumerator Drag(TablePoint from, TablePoint to)
        {
            yield return PressAndMove(from, to);
            SetMouse(Screen(to));
            yield return null;
            yield return null;
        }

        /// <summary>札が置き場所へ寄り切るのを実時間の上限で待つ（バッチ実行はフレームが極端に短い）。</summary>
        private IEnumerator WaitForCardsToSettle()
        {
            var limit = Time.realtimeSinceStartup + WaitLimitSeconds;
            while (!ViewMatchesTable() && Time.realtimeSinceStartup < limit) { yield return null; }
            AssertViewMatchesTable();
        }

        private bool ViewMatchesTable()
        {
            foreach (var pile in PileId.All)
            {
                for (var index = 0; index < Table.CountOf(pile); index++)
                {
                    var card = Table.CardAt(pile, index);
                    var center = TableLayout.CardCenter(Table, pile, index);
                    if (Vector2.Distance(View.CardPosition(card), new Vector2((float)center.X, (float)center.Y)) > .001f) { return false; }
                    if (View.IsFaceUp(card) != index >= Table.FaceDownCountOf(pile)) { return false; }
                }
            }

            return true;
        }

        private void AssertViewMatchesTable() => Assert.That(ViewMatchesTable(), Is.True, "描画が盤と一致しない\n" + TableText.Format(Table));

        // バッチ実行では UI イベントが配送されないので、実際の座標でボタンに当たることを確かめてから送る。
        private IEnumerator ClickButton(string name)
        {
            yield return null;
            yield return null;
            SendButtonClick(name);
            yield return null;
            yield return null;
        }

        private void SendButtonClick(string name)
        {
            var button = _game.HudRoot.Q<Button>(name);
            var panelPoint = button.worldBound.center;
            var panel = _game.HudRoot.panel;
            Assert.That(panel.Pick(panelPoint), Is.SameAs(button), name);

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

        [UnityTest]
        public IEnumerator Title_ShowsNameRulesAndAnExampleDeal()
        {
            Assert.That(Flow.Screen, Is.EqualTo(AppScreen.Title));
            Assert.That(Text("headline"), Is.EqualTo("SEVEN LANTERNS"));
            Assert.That(Text("message"), Does.Contain("組札に積み上げて"));
            Assert.That(IsShown("stats"), Is.False);
            var example = Deal.Create(0, Flow.Config.DrawCount);
            var top = example.CardAt(T(7), 6);
            var center = TableLayout.CardCenter(example, T(7), 6);
            Assert.That(Vector2.Distance(View.CardPosition(top), new Vector2((float)center.X, (float)center.Y)), Is.LessThan(.001f));
            Assert.That(View.IsFaceUp(top), Is.True);
            yield return null;
        }

        [UnityTest]
        public IEnumerator Enter_DealsAndTheCardsSlideIntoPlace()
        {
            yield return Begin();
            Assert.That(IsShown("stats"), Is.True);
            Assert.That(Text("moves"), Is.EqualTo("手数  0"));
            Assert.That(Text("foundation"), Is.EqualTo("組札  0 / 52"));
            yield return WaitForCardsToSettle();
        }

        [UnityTest]
        public IEnumerator Mouse_DragMovesARunAndFlipsTheCardBelow()
        {
            yield return Begin(Parse("stock: *", "t1: QS | 9C 8D", "t2: TD"));
            yield return WaitForCardsToSettle();
            var grip = Grip(T(1), 1);
            yield return Drag(grip, DropPoint(T(1), 1, grip, T(2)));
            Assert.That(Game.Moves, Is.EqualTo(1));
            Assert.That(TableText.Format(Table), Does.Contain("t1: | QS\n").And.Contain("t2: | TD 9C 8D\n"));
            Assert.That(Text("moves"), Is.EqualTo("手数  1"));
            yield return WaitForCardsToSettle();
            Assert.That(View.IsFaceUp(Card.Parse("QS")), Is.True, "下の伏せ札が開く");
        }

        [UnityTest]
        public IEnumerator Mouse_HeldCardsFollowThePointerAndTheTableWaits()
        {
            yield return Begin(Parse("stock: *", "t1: QS | 9C 8D", "t2: TD"));
            yield return WaitForCardsToSettle();
            var before = TableText.Format(Table);
            var grip = Grip(T(1), 1);
            var hover = new TablePoint(-3, -2);
            yield return PressAndMove(grip, hover);
            yield return null;

            Assert.That(Flow.Input.Dragging, Is.EqualTo(new PileRun(T(1), 2)));
            Assert.That(TableText.Format(Table), Is.EqualTo(before), "離すまで盤は変わらない");
            var baseCenter = hover - (grip - TableLayout.CardCenter(Table, T(1), 1));
            Assert.That(Vector2.Distance(View.CardPosition(Card.Parse("9C")), new Vector2((float)baseCenter.X, (float)baseCenter.Y)), Is.LessThan(.01f));
            Assert.That(View.CardPosition(Card.Parse("8D")).y, Is.EqualTo((float)(baseCenter.Y - TableLayout.FaceUpStep)).Within(.01f));
            Assert.That(View.SortingOrderOf(Card.Parse("8D")), Is.GreaterThan(View.SortingOrderOf(Card.Parse("9C"))));
            Assert.That(View.SortingOrderOf(Card.Parse("9C")), Is.GreaterThan(View.SortingOrderOf(Card.Parse("TD"))), "掴んだ札は手前に描く");

            SetMouse(Screen(hover));
            yield return null;
            yield return null;
            Assert.That(TableText.Format(Table), Is.EqualTo(before), "何もない所で離すと元に戻る");
            yield return WaitForCardsToSettle();
        }

        [UnityTest]
        public IEnumerator Mouse_IllegalDropIsRejectedAndTheCardSlidesBack()
        {
            yield return Begin(Parse("stock: *", "t1: 9C", "t2: 9H"));
            yield return WaitForCardsToSettle();
            var before = TableText.Format(Table);
            var grip = Grip(T(1), 0);
            yield return Drag(grip, DropPoint(T(1), 0, grip, T(2)));
            Assert.That(Flow.Notice, Is.EqualTo(Notice.Rejected));
            Assert.That(Text("status"), Is.EqualTo(SevenLanternsGame.RejectMessage(MoveError.RankMismatch)));
            Assert.That(TableText.Format(Table), Is.EqualTo(before));
            yield return WaitForCardsToSettle();
        }

        [UnityTest]
        public IEnumerator Mouse_ClickSelectsWithAHighlightAndClickElsewhereMoves()
        {
            yield return Begin(Parse("stock: *", "t1: QS | 9C 8D", "t2: TD"));
            yield return WaitForCardsToSettle();
            yield return Click(Grip(T(1), 1));
            Assert.That(Flow.Input.Selected, Is.EqualTo(new PileRun(T(1), 2)));
            Assert.That(View.IsHighlighted(Card.Parse("9C")), Is.True);
            Assert.That(View.IsHighlighted(Card.Parse("8D")), Is.True);
            Assert.That(View.IsHighlighted(Card.Parse("TD")), Is.False);
            Assert.That(Text("status"), Is.EqualTo("置きたい場所をクリックしてください。"));

            yield return Click(TableLayout.CardCenter(Table, T(2), 0));
            Assert.That(Game.Moves, Is.EqualTo(1));
            Assert.That(View.IsHighlighted(Card.Parse("9C")), Is.False);
            yield return WaitForCardsToSettle();
        }

        [UnityTest]
        public IEnumerator Mouse_StockClickDrawsAndRecycles()
        {
            yield return Begin(Parse("stock: 2C 3C", "t1: *"));
            yield return Click(TableLayout.SlotCenter(PileId.Stock));
            yield return Click(TableLayout.SlotCenter(PileId.Stock));
            Assert.That(Table.CountOf(PileId.Waste), Is.EqualTo(2));
            yield return WaitForCardsToSettle();
            Assert.That(View.IsFaceUp(Card.Parse("2C")), Is.True);

            yield return Click(TableLayout.SlotCenter(PileId.Stock));
            Assert.That(Table.CountOf(PileId.Stock), Is.EqualTo(2), "空の山札をクリックすると戻す");
            yield return WaitForCardsToSettle();
            Assert.That(View.IsFaceUp(Card.Parse("2C")), Is.False);
        }

        [UnityTest]
        public IEnumerator Mouse_DoubleClickSendsTheLastCardHomeAndWins()
        {
            yield return Begin(DemoTables.OneMoveFromWin());
            var king = TableLayout.CardCenter(Table, T(3), 0);
            yield return Click(king);
            yield return Click(king);
            Assert.That(Game.Phase, Is.EqualTo(GamePhase.Won));
            Assert.That(Flow.Screen, Is.EqualTo(AppScreen.Result));
            yield return null;
            Assert.That(Text("headline"), Is.EqualTo("完成！"));
            Assert.That(Text("message"), Does.Contain("組札  52 / 52"));
            Assert.That(IsShown("title"), Is.True);
        }

        [UnityTest]
        public IEnumerator Mouse_MoveThatLeavesNoProgressShowsStuck()
        {
            yield return Begin(DemoTables.OneMoveFromStuck(1));
            var waste = PileId.Waste;
            var grip = Grip(waste, 2);
            yield return Drag(grip, DropPoint(waste, 2, grip, T(7)));
            Assert.That(Game.Phase, Is.EqualTo(GamePhase.Stuck));
            yield return null;
            Assert.That(Text("headline"), Is.EqualTo("手詰まり"));

            yield return _keyboard.Tap(Key.Enter);
            Assert.That(Flow.Screen, Is.EqualTo(AppScreen.Playing), "結果画面から次の配札");
        }

        [UnityTest]
        public IEnumerator UndoKeyAndButton_PutTheMoveBack()
        {
            yield return Begin(Parse("stock: *", "t1: QS | 9C", "t2: TD"));
            Assert.That(_game.HudRoot.Q<Button>("undo").enabledSelf, Is.False, "戻す手がなければ押せない");
            var start = TableText.Format(Table);
            var grip = Grip(T(1), 1);
            yield return Drag(grip, DropPoint(T(1), 1, grip, T(2)));
            Assert.That(Game.Moves, Is.EqualTo(1));
            yield return null;
            Assert.That(_game.HudRoot.Q<Button>("undo").enabledSelf, Is.True);

            yield return _keyboard.Tap(Key.Z);
            Assert.That(TableText.Format(Table), Is.EqualTo(start));
            Assert.That(Text("status"), Is.EqualTo("1 手戻しました。"));
            yield return WaitForCardsToSettle();
            Assert.That(View.IsFaceUp(Card.Parse("QS")), Is.False, "開いた札は伏せ直る");

            yield return Drag(grip, DropPoint(T(1), 1, grip, T(2)));
            Assert.That(Game.Moves, Is.EqualTo(1));
            yield return ClickButton("undo");
            Assert.That(TableText.Format(Table), Is.EqualTo(start), "ボタンでも戻る");
        }

        [UnityTest]
        public IEnumerator Pause_StopsTimeBlocksTheTableAndResigns()
        {
            yield return Begin();
            yield return _keyboard.Tap(Key.Escape);
            Assert.That(Flow.Screen, Is.EqualTo(AppScreen.Paused));
            var ticks = Flow.ElapsedTicks;
            yield return Click(TableLayout.SlotCenter(PileId.Stock));
            yield return _keyboard.Tap(Key.Z);
            Assert.That(Game.Moves, Is.EqualTo(0), "一時停止中は札を動かせない");
            Assert.That(Flow.ElapsedTicks, Is.EqualTo(ticks));
            Assert.That(IsShown("restart"), Is.True);
            Assert.That(IsShown("resign"), Is.True);

            yield return ClickButton("resign");
            Assert.That(Flow.Screen, Is.EqualTo(AppScreen.Result));
            Assert.That(Game.Phase, Is.EqualTo(GamePhase.Resigned));
            yield return null;
            Assert.That(Text("headline"), Is.EqualTo("おしまい"));
        }

        [UnityTest]
        public IEnumerator Pause_RestartKeyDealsANewSeed()
        {
            yield return Begin();
            var seed = Flow.MatchSeed;
            yield return _keyboard.Tap(Key.Escape);
            yield return _keyboard.Tap(Key.R);
            Assert.That(Flow.Screen, Is.EqualTo(AppScreen.Playing));
            Assert.That(Flow.MatchSeed, Is.EqualTo(seed + 1));
            yield return WaitForCardsToSettle();
        }

        [UnityTest]
        public IEnumerator LosingFocusDropsTheHeldCardsAndTheReturnClickIsIgnored()
        {
            yield return Begin(Parse("stock: *", "t1: QS | 9C", "t2: TD"));
            yield return WaitForCardsToSettle();
            var before = TableText.Format(Table);
            var grip = Grip(T(1), 1);
            var drop = DropPoint(T(1), 1, grip, T(2));
            yield return PressAndMove(grip, drop);
            Assert.That(Flow.Input.Dragging, Is.Not.Null);

            SetFocus(false);
            Assert.That(Flow.Screen, Is.EqualTo(AppScreen.Paused));
            Assert.That(Flow.Input.Dragging, Is.Null);
            SetMouse(Screen(drop));
            yield return null;
            yield return null;
            Assert.That(TableText.Format(Table), Is.EqualTo(before), "フォーカスを失った後に離しても置かない");

            // 表示したばかりのボタンが配置されるまで待つ。
            yield return null;
            yield return null;

            // ウィンドウを選び直すクリックが再開ボタンに当たっても再開しない。
            SetFocus(true);
            SendButtonClick("submit");
            yield return null;
            yield return null;
            Assert.That(Flow.Screen, Is.EqualTo(AppScreen.Paused));

            // 同じ手順で、復帰から時間がたったクリックなら再開する。
            yield return ClickButton("submit");
            Assert.That(Flow.Screen, Is.EqualTo(AppScreen.Playing));
            yield return WaitForCardsToSettle();
        }

        [UnityTest]
        public IEnumerator SubmitButton_StartsTheGame()
        {
            yield return ClickButton("submit");
            Assert.That(Flow.Screen, Is.EqualTo(AppScreen.Playing));
        }

        [UnityTest]
        public IEnumerator QuitButton_QuitsOnlyFromTheTitle()
        {
            var quits = 0;
            _game.QuitHandler = () => quits++;
            yield return ClickButton("quit");
            Assert.That(quits, Is.EqualTo(1));

            yield return Begin();
            yield return null;
            Assert.That(IsShown("quit"), Is.False);
        }

        [Test]
        public void ScreenAndTableCoordinatesRoundTrip()
        {
            var point = new TablePoint(-4.2, 1.3);
            var back = _game.ScreenToTable(_game.TableToScreen(point));
            Assert.That(back.X, Is.EqualTo(point.X).Within(.01));
            Assert.That(back.Y, Is.EqualTo(point.Y).Within(.01));
        }
    }
}
