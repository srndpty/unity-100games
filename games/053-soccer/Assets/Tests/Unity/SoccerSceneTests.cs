#nullable enable
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
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace Game.Unity.Tests
{
    /// <summary>
    /// 実シーンの PlayMode テスト。<c>Time.timeScale = 0</c> で実時間を止め、Tick はテストから進める（015）。
    /// 入力は仮想キーボードと仮想マウスから送り、確かめたい処理をテスト側から呼ばない（013）。最初のシードと試合の長さ（1 分）は固定にする。
    /// </summary>
    public sealed class SoccerSceneTests
    {
        private const string SceneName = "MiniPitch";

        private static readonly string[] _roleNames = { "支援", "抜け出し", "寄せ", "カバー", "回収", "待機", "あなた" };

        private TestKeyboard _keyboard = null!;
        private TestMouse _mouse = null!;
        private SoccerSettings _settings = null!;
        private SoccerGame _game = null!;
        private int _quits;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _settings = ScriptableObject.CreateInstance<SoccerSettings>();
            _settings.MatchMinutes = 1;
            SoccerGame.SetOverridesForTests(3, _settings);
            _quits = 0;
            SoccerGame.QuitHandler = () => _quits++;
            _keyboard = new TestKeyboard();
            _mouse = new TestMouse();
            yield return LoadScene();
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            Time.timeScale = 1f;
            SoccerGame.SetOverridesForTests(null, null);
            SoccerGame.QuitHandler = Application.Quit;
            _mouse.Dispose();
            _keyboard.Dispose();
            Object.Destroy(_settings);
            yield return null;
        }

        private IEnumerator LoadScene()
        {
            yield return SceneManager.LoadSceneAsync(SceneName);
            yield return null;
            _game = Object.FindAnyObjectByType<SoccerGame>();
            Assert.That(_game, Is.Not.Null);
            for (int i = 0; i < 60 && _game.Hud == null; i++) { yield return null; }
            Assert.That(_game.Hud, Is.Not.Null, "HUD が読み込まれる");
            Time.timeScale = 0f;
            _game.Render();
            yield return null;
        }

        private SoccerFlow Flow => _game.Flow;

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

        /// <summary>キーを押したまま（ほかは離して）、Tick を進める。</summary>
        private IEnumerator Hold(int ticks, params Key[] keys)
        {
            _keyboard.Hold(keys);
            yield return null;
            Flow.StepTicks(ticks);
            _game.Render();
            yield return null;
        }

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

        // ---- Prefab ----

        [UnityTest]
        public IEnumerator StageMatchesTheExpectedLayout_AndABrokenStageIsDetected()
        {
            yield return null;
            Assert.That(_game.Stage.Mismatch(), Is.Null);
            _game.Settings.Validate();
            var copy = Object.Instantiate(_game.Stage.gameObject).GetComponent<SoccerStage>();
            try
            {
                copy.Players[2].transform.localScale = Vector3.one;
                Assert.That(copy.Mismatch(), Does.Contain("大きさ"));
                copy.Players[2].transform.localScale = Vector3.one * PitchProjection.PlayerScale;
                copy.Ball.sortingOrder = copy.Players[0].sortingOrder - 1;
                Assert.That(copy.Mismatch(), Does.Contain("球"));
            }
            finally
            {
                Object.Destroy(copy.gameObject);
            }
        }

        // ---- キー操作 ----

        [UnityTest]
        public IEnumerator Enter_StartsTheMatch_TheBoardKickoffAndMarkerAreShown()
        {
            Assert.That(Visible("title"), Is.True);
            Assert.That(Visible("board"), Is.False);
            yield return Press(Key.Enter);
            Assert.That(Flow.Screen, Is.EqualTo(FlowScreen.Playing));
            Assert.That(Visible("board"), Is.True);
            Assert.That(Visible("title"), Is.False);
            Assert.That(Hud.Q<Label>("score").text, Is.EqualTo("青 0 − 0 赤"));
            Assert.That(Hud.Q<Label>("clock").text, Is.EqualTo("1:00"));
            Assert.That(Hud.Q<Label>("restart").text, Is.EqualTo("キックオフ（青）"));
            Assert.That(_game.Stage.Marker.enabled, Is.True);
            var controlled = Flow.Match!.Players[Flow.Controlled].Position;
            Assert.That((Vector2)_game.Stage.Marker.transform.localPosition, Is.EqualTo((Vector2)PitchProjection.Ground(controlled)));
        }

        [UnityTest]
        public IEnumerator KeysOnly_PlayAFullMatch_ToTheResultAndBackToTitle()
        {
            yield return Press(Key.Enter);

            // キックオフは J でパス。以後は右へ走りながら J・K を押す（相手は CPU）。
            yield return Hold(_settings.ToConfig().RestartDelayTicks + 5);
            yield return Press(Key.J);
            Assert.That(Flow.Match!.Phase, Is.EqualTo(MatchPhase.Play), "J でキックオフ");
            var keys = new[] { Key.J, Key.K, Key.Space, Key.L };
            for (int i = 0; i < 400 && Flow.Screen == FlowScreen.Playing; i++)
            {
                yield return Hold(12, Key.D, i % 2 == 0 ? Key.W : Key.S);
                yield return Press(keys[i % keys.Length]);
            }

            Assert.That(Flow.Screen, Is.EqualTo(FlowScreen.Result));
            Assert.That(Visible("result"), Is.True);
            var outcome = Hud.Q<Label>("outcome").text;
            Assert.That(outcome, Is.EqualTo(Flow.Outcome > 0 ? "勝ち！" : Flow.Outcome < 0 ? "負け" : "引き分け"));
            Assert.That(Hud.Q<Label>("final-score").text, Does.StartWith($"青 {Flow.Match!.Score(Team.Blue)} − {Flow.Match.Score(Team.Red)} 赤"));
            Assert.That(Flow.Stats.Of(Team.Blue).Passes, Is.GreaterThan(0), "キーで出したパスが数えられる");
            yield return Press(Key.Enter);
            Assert.That(Flow.Screen, Is.EqualTo(FlowScreen.Title));
        }

        [UnityTest]
        public IEnumerator Switch_WithAHeldKey_TheNewPlayerWaitsForRelease()
        {
            yield return Press(Key.Enter);
            yield return Hold(_settings.ToConfig().RestartDelayTicks + 5);
            yield return Press(Key.J);
            yield return Hold(30);
            var before = Flow.Controlled;

            // 右を押したまま Space。
            _keyboard.Hold(Key.D, Key.Space);
            yield return null;
            Flow.StepTicks(1);
            _keyboard.Hold(Key.D);
            yield return null;
            Flow.StepTicks(10);
            _game.Render();
            yield return null;
            Assert.That(Flow.Controlled, Is.Not.EqualTo(before));
            Assert.That(Flow.WaitingForRelease, Is.True);
            Assert.That(Visible("release"), Is.True, "離すまでの案内");
            var p = Flow.Match!.Players[Flow.Controlled];
            Assert.That(_game.Stage.Marker.transform.localPosition.x, Is.EqualTo(PitchProjection.Ground(p.Position).x).Within(1e-4), "輪が新しい選手へ");

            yield return Hold(2);
            Assert.That(Flow.WaitingForRelease, Is.False);
            Assert.That(Visible("release"), Is.False);
        }

        [UnityTest]
        public IEnumerator F1_ShowsRoleLabels()
        {
            yield return Press(Key.Enter);
            yield return Hold(_settings.ToConfig().RestartDelayTicks + 5);
            yield return Press(Key.J);
            yield return Hold(60);
            yield return Press(Key.F1);
            Assert.That(_game.Stage.ShowRoles, Is.True);
            var any = false;
            for (int i = 0; i < Match.PlayerCount; i++)
            {
                var label = Hud.Q<Label>("role-" + i);
                if (label.resolvedStyle.display != DisplayStyle.Flex) { continue; }
                any = true;
                Assert.That(Array.IndexOf(_roleNames, label.text), Is.GreaterThanOrEqualTo(0), label.text);
            }

            Assert.That(any, Is.True);
            yield return Press(Key.F1);
            Assert.That(_game.Stage.ShowRoles, Is.False);
        }

        // ---- マウス・一時停止・タイトル ----

        [UnityTest]
        public IEnumerator ClickOnTitle_StartsTheMatch()
        {
            yield return Click(ScreenPointIn(Hud.Q("title-row-0")));
            Assert.That(Flow.Screen, Is.EqualTo(FlowScreen.Playing));
        }

        [UnityTest]
        public IEnumerator FocusLoss_Pauses_AndKeysAreIgnoredUntilResumed()
        {
            yield return Press(Key.Enter);
            yield return Hold(10);
            _game.SendMessage("OnApplicationFocus", false);
            _game.Render();
            yield return null;
            Assert.That(Flow.Screen, Is.EqualTo(FlowScreen.Paused));
            Assert.That(Visible("pause"), Is.True);
            var hash = Flow.MatchHash;
            yield return Press(Key.Escape);
            Assert.That(Flow.Screen, Is.EqualTo(FlowScreen.Paused), "フォーカスのない間のキーは当てない");
            _game.SendMessage("OnApplicationFocus", true);
            yield return null;
            Assert.That(Flow.Screen, Is.EqualTo(FlowScreen.Paused), "戻っても一時停止のまま");
            Assert.That(Flow.MatchHash, Is.EqualTo(hash), "一時停止の間は進まない");
            yield return Click(ScreenPointIn(Hud.Q("pause-row-0")));
            Assert.That(Flow.Screen, Is.EqualTo(FlowScreen.Playing));
        }

        [UnityTest]
        public IEnumerator BackToTitle_LooksLikeAFreshStart()
        {
            yield return null;
            var positions = new Vector3[Match.PlayerCount];
            for (int i = 0; i < positions.Length; i++) { positions[i] = _game.Stage.Players[i].transform.localPosition; }
            Assert.That(_game.Stage.Marker.enabled, Is.False);

            yield return Press(Key.Enter);
            yield return Hold(_settings.ToConfig().RestartDelayTicks + 5);
            yield return Press(Key.J);
            yield return Hold(120, Key.D);
            yield return Press(Key.F1);
            yield return Press(Key.Escape);
            yield return Press(Key.DownArrow);
            yield return Press(Key.Enter);
            Assert.That(Flow.Screen, Is.EqualTo(FlowScreen.Title));
            for (int i = 0; i < positions.Length; i++) { Assert.That(_game.Stage.Players[i].transform.localPosition, Is.EqualTo(positions[i]), $"p{i}"); }
            Assert.That(_game.Stage.Marker.enabled, Is.False);
            Assert.That(_game.Stage.Shadow.enabled, Is.False);
            foreach (var t in _game.Stage.Targets) { Assert.That(t.enabled, Is.False); }
            foreach (var name in new[] { "board", "restart", "banner", "hint", "release", "pause", "result", "role-0", "role-1" })
            {
                Assert.That(Visible(name), Is.False, name);
            }

            Assert.That(Visible("title"), Is.True);
            yield return Press(Key.F1);
        }

        [UnityTest]
        public IEnumerator Quit_FromTitle_CallsTheQuitHandlerOnce()
        {
            yield return Press(Key.UpArrow);
            Assert.That(Flow.MenuCursor, Is.EqualTo(SoccerFlow.TitleQuit));
            yield return Press(Key.Enter);
            yield return null;
            yield return null;
            Assert.That(_quits, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator BallAndShadow_FollowTheHeight()
        {
            yield return Press(Key.Enter);
            var m = Flow.Match!;
            m.ForcePlay();
            m.PlaceBall(new Vec2i(0, 0), new Vec2i(50, 0), 1500, 20);
            _game.Render();
            yield return null;
            Assert.That(_game.Stage.Shadow.enabled, Is.True);
            Assert.That(_game.Stage.Ball.transform.localPosition.y, Is.GreaterThan(_game.Stage.Shadow.transform.localPosition.y + 0.5f), "高さの分だけ上に描く");
            Assert.That(_game.Stage.Ball.transform.localScale.x, Is.GreaterThan(PitchProjection.BallBase));
        }
    }
}
