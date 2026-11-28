#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using Game.App;
using Game.Core;
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
    /// 探索も Tick で回す（<see cref="GoFlow.SearchInTicks"/>）。ふつうの対局と同じフレームの予算だけで回す経路は、考える時間のテストで確かめる。
    /// 入力は仮想キーボードと仮想マウスから送り、確かめたい処理をテスト側から呼ばない（013）。最初のシードと探索の回数（少なめ）は固定にする。
    /// </summary>
    public sealed class SekiteiSceneTests
    {
        private const string SceneName = "Sekitei";
        private const int PilotPlayouts = 60;

        private const string Walls = @"
            . . . . X O . . .
            . . . . X O . . .
            . . . . X O . . .
            . . . . X O . . .
            . . . . X O . . .
            . . . . X O . . .
            . . . . X O . . .
            . . . . X O . . .
            . . . . X O . . .";

        private TestKeyboard _keyboard = null!;
        private TestMouse _mouse = null!;
        private SekiteiSettings _settings = null!;
        private SekiteiGame _game = null!;
        private int _quits;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _settings = ScriptableObject.CreateInstance<SekiteiSettings>();
            _settings.WeakPlayouts = 80;
            _settings.NormalPlayouts = 100;
            _settings.StrongPlayouts = 3000;
            _settings.OwnershipPlayouts = 150;
            _settings.CpuThinkTicks = 2;
            _settings.PlayoutsPerTick = 50;
            _settings.ThinkMillisecondsPerFrame = 1f;
            SekiteiGame.SetOverridesForTests(3, _settings);
            _quits = 0;
            SekiteiGame.QuitHandler = () => _quits++;
            _keyboard = new TestKeyboard();
            _mouse = new TestMouse();
            yield return LoadScene();
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            Time.timeScale = 1f;
            SekiteiGame.SetOverridesForTests(null, null);
            SekiteiGame.QuitHandler = Application.Quit;
            _mouse.Dispose();
            _keyboard.Dispose();
            Object.Destroy(_settings);
            yield return null;
        }

        private IEnumerator LoadScene()
        {
            yield return SceneManager.LoadSceneAsync(SceneName);
            yield return null;
            _game = Object.FindAnyObjectByType<SekiteiGame>();
            Assert.That(_game, Is.Not.Null);
            for (var i = 0; i < 60 && _game.Hud == null; i++) { yield return null; }
            Assert.That(_game.Hud, Is.Not.Null, "HUD が読み込まれる");
            Time.timeScale = 0f;
            Assert.That(_game.Flow.SearchInTicks, Is.False, "ふつうの対局は Tick で探索しない");
            _game.Flow.SearchInTicks = true;
            _game.Render();
            yield return null;
        }

        private GoFlow Flow => _game.Flow;

        private VisualElement Hud => _game.Hud!.Root;

        private bool Visible(string name) => Hud.Q(name).resolvedStyle.display == DisplayStyle.Flex;

        private string Text(string name) => Hud.Q<Label>(name).text;

        /// <summary>キーを 1 回押して離し、1 Tick 進める。</summary>
        private IEnumerator Press(Key key)
        {
            yield return _keyboard.Tap(key);
            Flow.StepTicks(1);
            _game.Render();
            yield return null;
        }

        private IEnumerator Ticks(int ticks)
        {
            Flow.StepTicks(ticks);
            _game.Render();
            yield return null;
        }

        /// <summary>CPU が打つ（またはプレイヤーの手番・対局の外になる）まで Tick を進める。</summary>
        private IEnumerator UntilPlayerTurn()
        {
            for (var i = 0; i < 400 && Flow.Screen == FlowScreen.Playing && !Flow.IsPlayerTurn; i++) { Flow.StepTicks(1); }
            _game.Render();
            yield return null;
        }

        private Vector2 ScreenPointIn(VisualElement element)
        {
            var bound = element.worldBound;
            for (var y = 2; y < Screen.height; y += 4)
            {
                for (var x = 2; x < Screen.width; x += 4)
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

        private IEnumerator ClickPoint(string point) => Click(_game.ScreenPointOf(Points.Parse(point)));

        private static Key KeyOf(InputButton button) => button switch
        {
            InputButton.Up => Key.UpArrow,
            InputButton.Down => Key.DownArrow,
            InputButton.Left => Key.LeftArrow,
            InputButton.Right => Key.RightArrow,
            InputButton.Confirm => Key.Enter,
            InputButton.Pass => Key.P,
            _ => Key.Escape,
        };

        private IEnumerator StartFrom(Func<int, GoGame> factory)
        {
            Flow.GameFactory = factory;
            yield return Press(Key.Enter);
            Assert.That(Flow.Screen, Is.EqualTo(FlowScreen.Playing));
        }

        // ---- Prefab ----

        [UnityTest]
        public IEnumerator StageMatchesTheExpectedLayout_AndABrokenStageIsDetected()
        {
            yield return null;
            Assert.That(_game.Stage.Mismatch(), Is.Null);
            _game.Settings.Validate();
            var copy = Object.Instantiate(_game.Stage.gameObject).GetComponent<SekiteiStage>();
            try
            {
                copy.Stones[40].transform.localPosition += new Vector3(1f, 0f, 0f);
                Assert.That(copy.Mismatch(), Does.Contain("E5 の石の位置"));
                copy.Stones[40].transform.localPosition = BoardProjection.World(40);
                copy.Marks[3].sortingOrder = SekiteiStage.StoneOrder + 1;
                Assert.That(copy.Mismatch(), Does.Contain("D1 の描画順"));
            }
            finally
            {
                Object.Destroy(copy.gameObject);
            }
        }

        [UnityTest]
        public IEnumerator EveryIntersection_IsPickedAtItsScreenPoint()
        {
            yield return null;
            for (var p = 0; p < Points.Count; p++)
            {
                Assert.That(_game.BoardPointAt(_game.ScreenPointOf(p)), Is.EqualTo(p), Points.Name(p));
            }

            // 交点の間の真ん中は拾わない。
            var between = (_game.ScreenPointOf(Points.Parse("E5")) + _game.ScreenPointOf(Points.Parse("F6"))) / 2f;
            Assert.That(_game.BoardPointAt(between), Is.EqualTo(-1));
        }

        // ---- キー操作で 1 局 ----

        [UnityTest]
        public IEnumerator KeysOnly_PlayAGameToTheEnd_ThenScoreAndReturnToTitle()
        {
            Assert.That(Visible("title"), Is.True);
            Assert.That(Visible("side"), Is.False);
            yield return Press(Key.Enter);
            Assert.That(Flow.Screen, Is.EqualTo(FlowScreen.Playing));
            Assert.That(Visible("side"), Is.True);
            Assert.That(_game.Stage.Cursor.enabled, Is.False, "キーを使うまでカーソルは出さない");

            var mcts = _settings.ToMcts();
            for (var turn = 0; turn < 400 && Flow.Screen == FlowScreen.Playing; turn++)
            {
                yield return UntilPlayerTurn();
                if (Flow.Screen != FlowScreen.Playing) { break; }
                var move = Pilot.ChooseMove(Flow.Game!, mcts, PilotPlayouts, Flow.MatchSeed);
                var before = Flow.Game!.MoveCount;
                if (move == Points.Pass)
                {
                    yield return Press(Key.P);
                }
                else
                {
                    foreach (var key in Pilot.KeysTo(Flow.Cursor.Point, move)) { yield return Press(KeyOf(key)); }
                    Assert.That(Flow.Cursor.Point, Is.EqualTo(move));
                    Assert.That(_game.Stage.Cursor.enabled, Is.True);
                    yield return Press(Key.Enter);
                    Assert.That(_game.Stage.Stones[move].enabled, Is.True, $"{Points.Name(move)} に石が描かれる");
                }

                Assert.That(Flow.Game.MoveCount, Is.EqualTo(before + 1), "キーで打った手が 1 手だけ入る");
            }

            Assert.That(Flow.Screen, Is.EqualTo(FlowScreen.Scoring).Or.EqualTo(FlowScreen.Result));
            if (Flow.Screen == FlowScreen.Scoring)
            {
                Assert.That(Visible("scoring"), Is.True);
                for (var i = 0; i < 400 && Flow.IsEstimating; i++) { Flow.StepTicks(1); }
                yield return Press(Key.Escape);
            }

            Assert.That(Flow.Screen, Is.EqualTo(FlowScreen.Result));
            Assert.That(Visible("result"), Is.True);
            Assert.That(Text("outcome"), Is.EqualTo(Flow.Outcome == Outcome.Win ? "勝ち！" : Flow.Outcome == Outcome.Loss ? "負け" : "引き分け"));
            if (Flow.FinalScore is { } score) { Assert.That(Text("detail"), Does.Contain(SekiteiHud.MarginText(score))); }
            yield return Press(Key.DownArrow);
            yield return Press(Key.Enter);
            Assert.That(Flow.Screen, Is.EqualTo(FlowScreen.Title));
        }

        // ---- マウス ----

        [UnityTest]
        public IEnumerator ClickingAnIntersection_PlacesTheStone_ThenTheCpuReplies()
        {
            yield return Click(ScreenPointIn(Hud.Q("title-row-0")));
            Assert.That(Flow.Screen, Is.EqualTo(FlowScreen.Playing));
            yield return ClickPoint("C3");
            var c3 = Points.Parse("C3");
            Assert.That(Flow.Game![c3], Is.EqualTo(Stone.Black));
            Assert.That(_game.Stage.Stones[c3].enabled, Is.True);
            Assert.That(_game.Stage.Last.enabled, Is.True);
            Assert.That((Vector2)_game.Stage.Last.transform.localPosition, Is.EqualTo((Vector2)BoardProjection.World(c3)));
            Assert.That(Text("turn"), Does.Contain("白の番"));

            // CPU の手番のクリックは捨てる。
            yield return ClickPoint("G7");
            Assert.That(Flow.Game[Points.Parse("G7")], Is.EqualTo(Stone.Empty));
            yield return UntilPlayerTurn();
            Assert.That(Flow.Game.MoveCount, Is.EqualTo(2));
            Assert.That(Flow.Game.StoneCount(Stone.White), Is.EqualTo(1));
            Assert.That(Text("turn"), Does.Contain("黒の番（あなた）"));
        }

        [UnityTest]
        public IEnumerator KoRetake_IsRejectedWithTheReason()
        {
            var config = _settings.ToConfig();
            yield return StartFrom(_ =>
            {
                var g = new GoGame(config, BoardText.Parse(@"
                    . . . . . . . . .
                    . . . . . . . . .
                    . . . . . . . . .
                    . . . . . . . . .
                    . . . . . . . . .
                    . . . . . . . . .
                    . O X . . . . . .
                    O X . X . . . . .
                    . O X . . . . . ."), Stone.White);
                Assert.That(g.TryPlay(Stone.White, Points.Parse("C2")), Is.EqualTo(MoveVerdict.Ok));
                return g;
            });
            var b2 = Points.Parse("B2");
            yield return ClickPoint("B2");
            Assert.That(Flow.LastRejection, Is.EqualTo(MoveVerdict.Ko));
            Assert.That(Text("notice"), Does.Contain("コウ"));
            Assert.That(_game.Stage.Stones[b2].enabled, Is.False);
            Assert.That(Flow.IsPlayerTurn, Is.True);
        }

        [UnityTest]
        public IEnumerator PassButton_Passes_AndTwoPassesOpenTheScoring()
        {
            // コミ 10 で白の 1 目勝ちの局面。黒がパスすると白（CPU）もパスして終局する。
            _settings.Komi = 10;
            _game.Reinitialize(3);
            Flow.SearchInTicks = true;
            var config = _settings.ToConfig();
            yield return StartFrom(_ => new GoGame(config, BoardText.Parse(Walls), Stone.Black));
            yield return Click(ScreenPointIn(Hud.Q("pass")));
            Assert.That(Text("notice"), Does.Contain("黒がパス"));
            for (var i = 0; i < 400 && Flow.Screen == FlowScreen.Playing; i++) { Flow.StepTicks(1); }
            _game.Render();
            yield return null;
            Assert.That(Flow.Screen, Is.EqualTo(FlowScreen.Scoring));
            Assert.That(Flow.IsEstimating, Is.True);
            Assert.That(Text("score-text"), Does.Contain("死石を推定中"));
            for (var i = 0; i < 400 && Flow.IsEstimating; i++) { Flow.StepTicks(1); }
            _game.Render();
            yield return null;
            Assert.That(Flow.IsEstimating, Is.False);
            Assert.That(Visible("scoring"), Is.True);
            Assert.That(Visible("play-buttons"), Is.False);
            Assert.That(_game.Stage.Marks[Points.Parse("A1")].enabled, Is.True, "地の印");
            Assert.That(Text("score-text"), Does.Contain("白の 1 目勝ち"));

            // 黒の壁をクリックすると連ごと死石（薄く描く）になり、もう一度で戻る。
            var e5 = Points.Parse("E5");
            yield return ClickPoint("E5");
            Assert.That(Flow.DeadStones.Count, Is.EqualTo(9));
            Assert.That(_game.Stage.Stones[e5].color.a, Is.LessThan(1f));
            yield return ClickPoint("E1");
            Assert.That(Flow.DeadStones.IsEmpty, Is.True);
            Assert.That(_game.Stage.Stones[e5].color.a, Is.EqualTo(1f));

            yield return Click(ScreenPointIn(Hud.Q("confirm")));
            Assert.That(Flow.Screen, Is.EqualTo(FlowScreen.Result));
            Assert.That(Text("outcome"), Is.EqualTo("負け"));
            Assert.That(Text("detail"), Does.Contain("両者パスで終局"));
        }

        // ---- 一時停止・フォーカス・投了・タイトル ----

        [UnityTest]
        public IEnumerator Resign_FromThePauseMenu_IsALoss()
        {
            yield return Press(Key.Enter);
            yield return Press(Key.Escape);
            Assert.That(Visible("pause"), Is.True);
            yield return Press(Key.DownArrow);
            yield return Press(Key.Enter);
            Assert.That(Flow.Screen, Is.EqualTo(FlowScreen.Result));
            Assert.That(Text("outcome"), Is.EqualTo("負け"));
            Assert.That(Text("detail"), Does.Contain("投了"));
        }

        [UnityTest]
        public IEnumerator FocusLoss_Pauses_AndInputIsIgnoredUntilResumed()
        {
            yield return Press(Key.Enter);
            _game.SendMessage("OnApplicationFocus", false);
            _game.Render();
            yield return null;
            Assert.That(Flow.Screen, Is.EqualTo(FlowScreen.Paused));
            Assert.That(Visible("pause"), Is.True);
            yield return Press(Key.Escape);
            Assert.That(Flow.Screen, Is.EqualTo(FlowScreen.Paused), "フォーカスのない間のキーは当てない");
            _game.SendMessage("OnApplicationFocus", true);
            yield return null;
            yield return null;
            Assert.That(Flow.Screen, Is.EqualTo(FlowScreen.Paused), "戻っても一時停止のまま");
            yield return Click(ScreenPointIn(Hud.Q("pause-row-0")));
            Assert.That(Flow.Screen, Is.EqualTo(FlowScreen.Playing));
            Assert.That(Flow.Game!.MoveCount, Is.EqualTo(0));
        }

        [UnityTest]
        public IEnumerator BackToTitle_LooksLikeAFreshStart()
        {
            yield return null;
            AssertBoardIsEmpty();
            yield return Press(Key.Enter);
            yield return ClickPoint("E5");
            yield return UntilPlayerTurn();
            yield return Press(Key.LeftArrow);
            Assert.That(_game.Stage.Cursor.enabled, Is.True);
            yield return Press(Key.Escape);
            yield return Press(Key.DownArrow);
            yield return Press(Key.DownArrow);
            yield return Press(Key.Enter);
            Assert.That(Flow.Screen, Is.EqualTo(FlowScreen.Title));
            AssertBoardIsEmpty();
            foreach (var name in new[] { "side", "pause", "result", "howto" }) { Assert.That(Visible(name), Is.False, name); }
            Assert.That(Visible("title"), Is.True);
        }

        [UnityTest]
        public IEnumerator Quit_FromTitle_CallsTheQuitHandlerOnce()
        {
            yield return Press(Key.UpArrow);
            Assert.That(Flow.MenuCursor, Is.EqualTo(GoFlow.TitleQuit));
            yield return Press(Key.Enter);
            yield return null;
            yield return null;
            Assert.That(_quits, Is.EqualTo(1));
        }

        // ---- CPU の考える時間 ----

        [UnityTest]
        public IEnumerator CpuThinking_UsesTheFrameBudget_WithoutStallingFrames()
        {
            // 実時間は止めたまま（Tick は進まない）。探索はフレームの予算（8 ms）だけで進む。
            _settings.ThinkMillisecondsPerFrame = 8f;
            Flow.SearchInTicks = false;
            yield return Press(Key.DownArrow);
            yield return Press(Key.RightArrow); // 強い（3,000 回）
            yield return Press(Key.UpArrow);
            yield return Press(Key.Enter);
            yield return ClickPoint("E5");
            for (var i = 0; i < new FlowSettings().PlaceAnimTicks; i++) { Flow.StepTicks(1); }
            Assert.That(Flow.IsCpuThinking, Is.True);

            var done = Flow.Cpu!.Search?.Done ?? 0;
            var watch = new Stopwatch();
            var worst = 0.0;
            for (var frame = 0; frame < 30; frame++)
            {
                watch.Restart();
                yield return null;
                worst = Math.Max(worst, watch.Elapsed.TotalMilliseconds);
            }

            var after = Flow.Cpu.Search!.Done;
            Assert.That(after, Is.GreaterThan(done), "Tick を進めなくても、フレームの残り時間で探索が進む");
            Assert.That(after, Is.LessThan(3000), "1 フレームで全部は回さない");
            Assert.That(worst, Is.LessThan(100.0), $"1 フレームの最長 {worst:F1} ms");
            TestContext.WriteLine($"30 フレームで {after - done} 回、1 フレームの最長 {worst:F1} ms");
        }

        [UnityTest]
        public IEnumerator StrongCpu_InRealTime_FinishesItsSearch_WithinTheFrameBudget()
        {
            // ふつうの対局と同じ：実時間で Tick が進み、探索は Tick では回さず、入力と Tick を含めた 1 フレームの予算（8 ms）の残りだけで回す。
            _settings.StrongPlayouts = 8000;
            _settings.ThinkMillisecondsPerFrame = 8f;
            Flow.SearchInTicks = false;
            yield return Press(Key.DownArrow);
            yield return Press(Key.RightArrow); // 強い（8,000 回）
            yield return Press(Key.DownArrow);
            yield return Press(Key.RightArrow); // 白（CPU が黒で先に打つ）
            yield return Press(Key.UpArrow);
            yield return Press(Key.UpArrow);
            yield return Press(Key.Enter);
            Assert.That(Flow.IsCpuThinking, Is.True);
            Assert.That(Flow.MatchLevel, Is.EqualTo(CpuLevel.Strong));

            Time.timeScale = 1f;
            var frames = new List<double>();
            var watch = new Stopwatch();
            var total = Stopwatch.StartNew();
            while (Flow.Game!.MoveCount == 0 && total.Elapsed.TotalSeconds < 60)
            {
                watch.Restart();
                yield return null;
                frames.Add(watch.Elapsed.TotalMilliseconds);
            }

            Time.timeScale = 0f;
            Assert.That(Flow.Game.MoveCount, Is.EqualTo(1), "60 秒以内に打つ");
            frames.Sort();
            var worst = frames[frames.Count - 1];
            var p95 = frames[(int)(frames.Count * 0.95)];
            TestContext.WriteLine($"強い（8,000 回）：{total.Elapsed.TotalSeconds:F2} 秒・{frames.Count} フレーム、1 フレームの p50 {frames[frames.Count / 2]:F1} ms・p95 {p95:F1} ms・最長 {worst:F1} ms");
            Assert.That(p95, Is.LessThan(25.0), "ふつうのフレームは予算の近くに収まる");
            Assert.That(worst, Is.LessThan(100.0), $"1 フレームの最長 {worst:F1} ms");
        }

        private void AssertBoardIsEmpty()
        {
            for (var p = 0; p < Points.Count; p++)
            {
                Assert.That(_game.Stage.Stones[p].enabled, Is.False, Points.Name(p));
                Assert.That(_game.Stage.Marks[p].enabled, Is.False, Points.Name(p));
            }

            Assert.That(_game.Stage.Last.enabled, Is.False);
            Assert.That(_game.Stage.Cursor.enabled, Is.False);
            Assert.That(_game.Stage.Ghost.enabled, Is.False);
        }
    }
}
