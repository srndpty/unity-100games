using System;
using System.Collections;
using System.Linq;
using System.Reflection;
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
    public sealed class InkVigilSceneTests
    {
        private const int TickLimit = 60 * 60 * 5;

        private TestKeyboard _keyboard;
        private InkVigilGame _game;

        [UnitySetUp]
        public IEnumerator Load()
        {
            _keyboard = new TestKeyboard();
            yield return SceneManager.LoadSceneAsync("InkVigil");
            yield return null;
            _game = Object.FindAnyObjectByType<InkVigilGame>();
            for (var i = 0; i < 30 && _game.HudRoot == null; i++) { yield return null; }
            Assert.That(_game.HudRoot, Is.Not.Null);
        }

        [UnityTearDown]
        public IEnumerator Unload()
        {
            Time.timeScale = 1;
            _keyboard.Dispose();
            yield return null;
        }

        private TypingRun Run => _game.Flow.Run;

        private InkVigilStage Stage => _game.Stage;

        private string Text(string name) => _game.HudRoot.Q<Label>(name).text;

        private bool Shown(string name) => _game.HudRoot.Q(name).style.display.value == DisplayStyle.Flex;

        private static Key KeyOf(char c) => c == '-' ? Key.Minus : Key.A + (c - 'a');

        /// <summary>時計を止めて（timeScale 0）Enter で始める。Tick はテストが進める。</summary>
        private IEnumerator StartPaused()
        {
            Time.timeScale = 0;
            yield return _keyboard.Tap(Key.Enter);
            Assert.That(_game.Flow.Screen, Is.EqualTo(AppScreen.Playing));
        }

        private IEnumerator TypeKeys(string keys)
        {
            foreach (var c in keys) { yield return _keyboard.Tap(KeyOf(c)); }
        }

        // バッチ実行はフレームが極端に短いので、フレーム数ではなく実時間の上限で待つ（011 の知見）。
        private static IEnumerator WaitUntil(Func<bool> condition, float seconds = 5f)
        {
            var limit = Time.realtimeSinceStartup + seconds;
            while (!condition() && Time.realtimeSinceStartup < limit) { yield return null; }
            Assert.That(condition(), Is.True, "時間内に条件を満たさなかった");
        }

        /// <summary>Tick を直接進める。出来事は画面の側（音）でも受け取る。</summary>
        private FlowEvents StepUntil(Func<bool> done)
        {
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

        private VisualElement Row(Difficulty difficulty) => _game.HudRoot.Q(InkVigilGame.DifficultyRow(difficulty));

        private string Marker(Difficulty difficulty) => Row(difficulty).Q<Label>(className: "difficulty-marker").text;

        private Label[] DifficultyNames() =>
            new[] { Difficulty.Easy, Difficulty.Normal, Difficulty.Hard }.Select(d => Row(d).Q<Label>(className: "difficulty-name")).ToArray();

        private WordEntry Word(string kana) => _game.Flow.Book.Entries.First(e => e.Reading.Kana == kana);

        private int FreeLane() => Enumerable.Range(0, Run.Lanes).First(l => Run.Enemies.All(e => e.Lane != l));

        [UnityTest]
        public IEnumerator Stage_IsComplete_AndTheTitleExplainsTheControls()
        {
            yield return null;
            Assert.That(Object.FindObjectsByType<InkVigilStage>(FindObjectsInactive.Include), Has.Length.EqualTo(1), "Stage のインスタンスは 1 つ");
            Assert.That(Stage.Mismatch(), Is.Null);
            Assert.That(Text("headline"), Is.EqualTo("INK VIGIL"));
            Assert.That(Text("controls"), Does.Contain("Backspace").And.Contain("shi / si").And.Contain("Enter"));
            Assert.That(Marker(Difficulty.Normal), Is.EqualTo("▶"));
            Assert.That(Marker(Difficulty.Easy), Is.Empty);
            Assert.That(Shown("stats"), Is.False);
            Assert.That(Shown("typing"), Is.False);
            for (var lane = 0; lane < InkVigilStage.MaxLanes; lane++)
            {
                Assert.That(Stage.EnemyRenderer(lane).enabled, Is.False);
                Assert.That(_game.PlateOf(lane).style.display.value, Is.EqualTo(DisplayStyle.None));
            }
        }

        [UnityTest]
        public IEnumerator StageMismatch_ReportsMissingParts()
        {
            // 実際の Stage を複製し、描画に要る部品を 1 つずつ壊す。照合は例外ではなく具体的な理由を返す。
            yield return null;
            var cases = new (string Label, Action<InkVigilStage> Break, string Expected)[]
            {
                ("回廊の絵", s => Set(s, "_corridor", null), "回廊"),
                ("手前の線を動かす", s => Get<SpriteRenderer>(s, "_nearLine").transform.localPosition += Vector3.up * .1f, "手前の線の高さ"),
                ("見張りを動かす", s => Get<SpriteRenderer>(s, "_keeper").transform.localPosition += Vector3.right, "見張りの位置"),
                ("閃光", s => Set(s, "_flash", null), "閃光"),
                ("煙の絵", s => Get<SpriteRenderer>(s, "_puff").sprite = null, "煙"),
                ("敵の置き場を減らす", s => Set(s, "_enemies", Get<SpriteRenderer[]>(s, "_enemies").Take(4).ToArray()), "敵の置き場が 5 個"),
                ("敵の置き場の欠け", s => Get<SpriteRenderer[]>(s, "_enemies")[2] = null, "敵の置き場 2"),
                ("敵の絵の欠け", s => Get<Sprite[]>(s, "_tierSprites")[5] = null, "敵の絵 5"),
                ("敵の絵を減らす", s => Set(s, "_tierSprites", Get<Sprite[]>(s, "_tierSprites").Take(4).ToArray()), "敵の絵が 6 枚"),
            };

            foreach (var (label, damage, expected) in cases)
            {
                var copy = Object.Instantiate(Stage.gameObject).GetComponent<InkVigilStage>();
                try
                {
                    Assert.That(copy.Mismatch(), Is.Null, $"{label}：壊す前");
                    damage(copy);
                    Assert.That(copy.Mismatch(), Does.Contain(expected), label);
                }
                finally
                {
                    Object.Destroy(copy.gameObject);
                }
            }
        }

        [UnityTest]
        public IEnumerator Keys_TypeTheNearestEnemy_AndDefeatIt()
        {
            yield return _keyboard.Tap(Key.Enter);
            yield return WaitUntil(() => Run.Enemies.Count > 0);
            var enemy = Typist.Nearest(Run);
            var guide = enemy.Matcher.Guide;
            yield return TypeKeys(guide.Substring(0, 1));
            Assert.That(Run.Target, Is.SameAs(enemy));
            Assert.That(Text("typing-display"), Is.EqualTo(enemy.Word.Display), "下（上部）の表示は狙いの表示文");
            Assert.That(Text("typing-romaji"), Does.StartWith($"<color=#FFD66E>{guide[0]}</color>"), "打った部分の色を変える");
            Assert.That(_game.PlateOf(enemy.Lane).ClassListContains("target"), Is.True);

            yield return TypeKeys(enemy.Matcher.Guide);
            Assert.That(Run.Defeated, Is.EqualTo(1));
            Assert.That(Run.Enemies, Has.No.Member(enemy));
            Assert.That(Run.Misses, Is.Zero);
            Assert.That(Text("defeated"), Does.Contain("撃破  1 /"));
        }

        [UnityTest]
        public IEnumerator WrongKey_DoesNotAdvance_AndFlashesTheTypingLine()
        {
            yield return StartPaused();
            var enemy = Run.Place(Word("かね"), 0, 100);
            yield return TypeKeys("ka");
            Assert.That(enemy.Matcher.Typed, Is.EqualTo("ka"));
            yield return TypeKeys("q");
            Assert.That(enemy.Matcher.Typed, Is.EqualTo("ka"), "誤字で進まない");
            Assert.That(Run.Misses, Is.EqualTo(1));
            Assert.That(Run.Combo, Is.Zero);
            Assert.That(_game.HudRoot.Q("typing").ClassListContains("miss"), Is.True);
            StepUntil(() => Run.Ticks - Run.LastMissTick >= _game.Settings.EffectTicks);
            Assert.That(_game.HudRoot.Q("typing").ClassListContains("miss"), Is.False, "演出の Tick を過ぎたら戻る");
        }

        [UnityTest]
        public IEnumerator Backspace_ReleasesTheTarget_AndResetsItsProgress()
        {
            yield return StartPaused();
            var deer = Run.Place(Word("しゃしん"), 1, 100);
            yield return TypeKeys("sh");
            Assert.That(Run.Target, Is.SameAs(deer));
            yield return _keyboard.Tap(Key.Backspace);
            Assert.That(Run.Target, Is.Null);
            Assert.That(deer.Matcher.Typed, Is.Empty);
            Assert.That(Run.Misses, Is.Zero);
            Assert.That(Text("typing-display"), Is.EqualTo("狙いなし"));
            Assert.That(_game.PlateOf(1).ClassListContains("target"), Is.False);

            // 別の綴りで打ち直せる。
            yield return TypeKeys("syasinn");
            Assert.That(Run.Defeated, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator TwoEnemies_WithTheSameFirstKey_DoNotCrossTalk()
        {
            yield return StartPaused();
            var bell = Run.Place(Word("かね"), FreeLane(), 200);
            var key = Run.Place(Word("かぎ"), FreeLane(), 100);
            yield return TypeKeys("kag");
            Assert.That(Run.Target, Is.SameAs(bell), "手前（出現から長い）の敵を狙う");
            Assert.That(bell.Matcher.Typed, Is.EqualTo("ka"));
            Assert.That(key.Matcher.Typed, Is.Empty, "狙っていない敵は進まない");
            Assert.That(Run.Misses, Is.EqualTo(1), "g は狙いの「かね」にとって誤字");
            Assert.That(Run.Target, Is.SameAs(bell));

            _game.Render();
            Assert.That(_game.PlateOf(bell.Lane).ClassListContains("target"), Is.True);
            Assert.That(_game.PlateOf(key.Lane).ClassListContains("target"), Is.False);
        }

        [UnityTest]
        public IEnumerator SameFrameKeys_AreAppliedInOrder()
        {
            yield return StartPaused();
            var moon = Run.Place(Word("つき"), 0, 100);

            // 1 フレームに t → s の押下を続けて届ける（t を押したまま s を足す）。
            _keyboard.Hold(Key.T);
            _keyboard.Hold(Key.T, Key.S);
            yield return null;
            _keyboard.Hold();
            yield return null;
            Assert.That(moon.Matcher.Typed, Is.EqualTo("ts"));
            Assert.That(Run.Misses, Is.Zero);
        }

        [UnityTest]
        public IEnumerator EnemiesAndPlates_FollowThePerspective()
        {
            yield return StartPaused();
            var near = Run.Place(Word("ほし"), 0, 400);
            var far = Run.Place(Word("ゆき"), 4, 10);
            _game.Render();
            yield return null;
            foreach (var enemy in new[] { near, far })
            {
                var renderer = Stage.EnemyRenderer(enemy.Lane);
                var foot = InkVigilPerspective.Foot(enemy.Lane, Run.Lanes, enemy.Distance);
                Assert.That(renderer.enabled, Is.True);
                Assert.That(renderer.transform.localPosition.x, Is.EqualTo(foot.x).Within(1e-4f));
                Assert.That(renderer.transform.localPosition.y, Is.EqualTo(foot.y).Within(.1f), "上下の揺れの範囲");
                Assert.That(renderer.transform.localScale.x, Is.EqualTo(InkVigilPerspective.SpriteScale(enemy.Distance)).Within(1e-5f));
                var plate = _game.PlateOf(enemy.Lane);
                var expected = RuntimePanelUtils.CameraTransformWorldToPanel(_game.HudRoot.panel, Stage.HeadOf(enemy, Run.Lanes), _game.ViewCamera);
                Assert.That(plate.style.left.value.value, Is.EqualTo(expected.x).Within(.01f));
                Assert.That(plate.style.top.value.value, Is.EqualTo(expected.y).Within(.01f));
                Assert.That(plate.Q<Label>(className: "plate-display").text, Is.EqualTo(enemy.Word.Display));
            }

            Assert.That(Stage.EnemyRenderer(near.Lane).sortingOrder, Is.GreaterThan(Stage.EnemyRenderer(far.Lane).sortingOrder), "手前ほど前に描く");
            Assert.That(Stage.EnemyRenderer(near.Lane).transform.localScale.x, Is.GreaterThan(Stage.EnemyRenderer(far.Lane).transform.localScale.x));
        }

        [UnityTest]
        public IEnumerator Hit_ReducesHearts_AndFlashes()
        {
            yield return StartPaused();
            Run.Place(Word("ほし"), 2, Run.ApproachTicks - 1);
            var events = StepUntil(() => Run.Hp < Run.MaxHp);
            Assert.That(events & FlowEvents.Hit, Is.EqualTo(FlowEvents.Hit));
            Assert.That(Text("hp"), Is.EqualTo(InkVigilGame.Hearts(Run.MaxHp - 1, Run.MaxHp)));
            Assert.That(Stage.Flash.enabled, Is.True);
            Assert.That(_game.ViewCamera.transform.position.x, Is.Not.EqualTo(0).Within(1e-4f), "被弾の直後は画面が揺れる");
        }

        [UnityTest]
        public IEnumerator SelectHard_Clear_Retry_AndBackToTitle()
        {
            yield return _keyboard.Tap(Key.DownArrow);
            Assert.That(Marker(Difficulty.Hard), Is.EqualTo("▶"));
            _game.Flow.Pilot = new Typist(8);
            yield return StartPaused();
            Assert.That(Run.Difficulty, Is.EqualTo(Difficulty.Hard));
            var seed = Run.Seed;
            StepUntil(() => _game.Flow.Screen == AppScreen.Result);
            Assert.That(Run.Status, Is.EqualTo(RunStatus.Cleared));
            Assert.That(Text("headline"), Is.EqualTo("夜を守りきった！"));
            Assert.That(Text("message"), Does.Contain("最高記録").And.Contain("打/分"));
            Assert.That(Stage.Puff.enabled, Is.False, "結果の画面では演出を消す");

            yield return _keyboard.Tap(Key.Enter);
            Assert.That(_game.Flow.Screen, Is.EqualTo(AppScreen.Playing));
            Assert.That(Run.Seed, Is.Not.EqualTo(seed), "再挑戦は新しいシード");
            Assert.That(Run.Difficulty, Is.EqualTo(Difficulty.Hard));

            _game.Flow.Pilot = new Typist(60);
            StepUntil(() => _game.Flow.Screen == AppScreen.Result);
            Assert.That(Text("headline"), Is.EqualTo("灯が消えた…"));
            yield return _keyboard.Tap(Key.Escape);
            Assert.That(_game.Flow.Screen, Is.EqualTo(AppScreen.Title));
            Assert.That(Row(Difficulty.Hard).Q<Label>(className: "difficulty-best").text, Does.StartWith("最高 "), "タイトルに難度ごとの最高得点");
            Assert.That(Row(Difficulty.Easy).Q<Label>(className: "difficulty-best").text, Is.Empty);
        }

        [UnityTest]
        public IEnumerator BackToTitle_LooksLikeStartup()
        {
            yield return null;
            var startup = Snapshot();
            yield return StartPaused();
            Run.Place(Word("ほし"), 2, 300);
            StepUntil(() => Run.Enemies.Count >= 3);
            yield return TypeKeys(Typist.Nearest(Run).Matcher.Guide);
            yield return _keyboard.Tap(Key.Escape);
            Assert.That(_game.Flow.Screen, Is.EqualTo(AppScreen.Paused));
            Assert.That(Text("message"), Does.Contain("T：タイトルへ"));
            yield return _keyboard.Tap(Key.T);
            Assert.That(_game.Flow.Screen, Is.EqualTo(AppScreen.Title));
            yield return null;
            Assert.That(Snapshot(), Is.EqualTo(startup));
        }

        [UnityTest]
        public IEnumerator PausedKeys_DoNotType_AndFocusLossPausesAndDropsPendingKeys()
        {
            yield return StartPaused();
            var bell = Run.Place(Word("かね"), 0, 100);
            yield return _keyboard.Tap(Key.Escape);
            yield return TypeKeys("ka");
            Assert.That(bell.Matcher.Typed, Is.Empty, "一時停止中の打鍵は試合に届かない（T 以外の文字は何もしない）");
            yield return _keyboard.Tap(Key.Escape);
            Assert.That(_game.Flow.Screen, Is.EqualTo(AppScreen.Playing));

            // 押下が届いた同じフレームにフォーカスを失う：その打鍵は捨て、一時停止する。
            _keyboard.Hold(Key.K);
            InputSystem.Update();
            _game.SendMessage("OnApplicationFocus", false);
            yield return null;
            _keyboard.Hold();
            Assert.That(_game.Flow.Screen, Is.EqualTo(AppScreen.Paused));
            Assert.That(bell.Matcher.Typed, Is.Empty);
            _game.SendMessage("OnApplicationFocus", true);
            yield return null;
            Assert.That(_game.Flow.Screen, Is.EqualTo(AppScreen.Paused), "戻っても自動では再開しない");
        }

        [UnityTest]
        public IEnumerator TitleSelection_DoesNotMoveTheDifficultyNames()
        {
            // 印（▶）の有無で難度名が動かない：選び直しても、各難度名の画面上の位置と大きさが変わらない。
            yield return null;
            yield return null;
            var before = DifficultyNames().Select(l => l.worldBound).ToArray();
            Assert.That(before.All(r => r.width > 0), Is.True, "配置が済んでいる");
            yield return _keyboard.Tap(Key.DownArrow);
            yield return null;
            Assert.That(Marker(Difficulty.Hard), Is.EqualTo("▶"));
            Assert.That(DifficultyNames().Select(l => l.worldBound).ToArray(), Is.EqualTo(before), "↓");
            yield return _keyboard.Tap(Key.UpArrow);
            yield return _keyboard.Tap(Key.UpArrow);
            yield return null;
            Assert.That(Marker(Difficulty.Easy), Is.EqualTo("▶"));
            Assert.That(DifficultyNames().Select(l => l.worldBound).ToArray(), Is.EqualTo(before), "↑ ↑");
            var names = DifficultyNames().Select(l => l.worldBound.center.x).ToArray();
            Assert.That(names[1], Is.EqualTo(names[0]).Within(.01f), "難度名は縦にそろう");
            Assert.That(names[2], Is.EqualTo(names[0]).Within(.01f));
        }

        [UnityTest]
        public IEnumerator BackspaceThenAnotherSpelling_InTheSameFrame_UpdatesTheGuideAndPlate()
        {
            // f → 描画 → 同じフレームに Backspace と h → 描画。打った文字数は同じ 1 だが、案内は hude に変わる。
            yield return StartPaused();
            var brush = Run.Place(Word("ふで"), 1, 100);
            yield return TypeKeys("f");
            Assert.That(Text("typing-romaji"), Is.EqualTo("<color=#FFD66E>f</color>ude"));
            _keyboard.Hold(Key.Backspace);
            _keyboard.Hold(Key.H);
            yield return null;
            _keyboard.Hold();
            yield return null;
            Assert.That(brush.Matcher.Typed, Is.EqualTo("h"));
            Assert.That(Text("typing-romaji"), Is.EqualTo("<color=#FFD66E>h</color>ude"));
            Assert.That(_game.PlateOf(1).Q<Label>(className: "plate-romaji").text, Is.EqualTo("<color=#FFD66E>h</color>ude"));
        }

        [UnityTest]
        public IEnumerator NextMatch_ReusingEnemyIds_RedrawsThePlates()
        {
            // 敵の番号は試合ごとに 0 から使い直す。前の試合で同じ番号・同じ版だった札の文字を残さない。
            yield return StartPaused();
            Run.Place(Word("ふで"), 0, 100);
            _game.Render();
            yield return _keyboard.Tap(Key.Escape);
            yield return _keyboard.Tap(Key.T);
            yield return StartPaused();
            Run.Place(Word("ゆき"), 0, 100);
            _game.Render();
            Assert.That(Run.Enemies[0].Id, Is.Zero);
            Assert.That(_game.PlateOf(0).Q<Label>(className: "plate-display").text, Is.EqualTo("雪"));
            Assert.That(_game.PlateOf(0).Q<Label>(className: "plate-romaji").text, Is.EqualTo("yuki"));
        }

        [UnityTest]
        public IEnumerator SettingsAsset_MatchesTheCodeDefaults()
        {
            yield return null;
            Assert.That(JsonUtility.ToJson(_game.Settings.Rules), Is.EqualTo(JsonUtility.ToJson(new VigilConfig())), "コードの既定値を変えたらアセットも直す");
            Assert.That(_game.Settings.Validate().Count, Is.EqualTo(WordBook.ProductionCount));
        }

        [Test]
        public void Settings_RejectInvalidValues()
        {
            var settings = ScriptableObject.CreateInstance<InkVigilSettings>();
            try
            {
                Assert.That(() => settings.Validate(), Throws.TypeOf<ArgumentNullException>(), "単語帳なし");
                settings.Words = new TextAsset("月\tつき\t1");
                Assert.That(() => settings.Validate(), Throws.TypeOf<FormatException>(), "50 語でない単語帳");
                settings.Words = Object.FindAnyObjectByType<InkVigilGame>()?.Settings.Words ?? Resources.FindObjectsOfTypeAll<InkVigilSettings>().First(s => s.Words != null).Words;
                Assert.That(() => settings.Validate(), Throws.Nothing);
                settings.Volume = float.NaN;
                Assert.That(() => settings.Validate(), Throws.TypeOf<ArgumentOutOfRangeException>());
                settings.Volume = .1f;
                settings.Rules.Lanes = InkVigilStage.MaxLanes + 1;
                Assert.That(() => settings.Validate(), Throws.TypeOf<ArgumentOutOfRangeException>(), "描画できる列を超える");
                settings.Rules.Lanes = 5;
                settings.EffectTicks = 0;
                Assert.That(() => settings.Validate(), Throws.TypeOf<ArgumentOutOfRangeException>());
            }
            finally
            {
                Object.DestroyImmediate(settings);
            }
        }

        [Test]
        public void Perspective_ShrinksTowardTheHorizon()
        {
            var near = InkVigilPerspective.Foot(0, 5, 0);
            var far = InkVigilPerspective.Foot(0, 5, Enemy.Far);
            Assert.That(near.y, Is.EqualTo(InkVigilPerspective.NearY).Within(1e-5f));
            Assert.That(near.x, Is.EqualTo(InkVigilPerspective.LaneX(0, 5)).Within(1e-5f));
            Assert.That(far.y, Is.GreaterThan(near.y).And.LessThan(InkVigilPerspective.HorizonY));
            Assert.That(Mathf.Abs(far.x), Is.LessThan(Mathf.Abs(near.x)));
            Assert.That(InkVigilPerspective.Foot(2, 5, 3000).x, Is.Zero, "中央の列は真ん中");
            Assert.That(InkVigilPerspective.SpriteScale(0), Is.EqualTo(InkVigilPerspective.NearScale));
        }

        [Test]
        public void RichText_MarksTypedRomajiAndDoneKana()
        {
            var matcher = new RomajiMatcher(Reading.Parse("しずか"));
            Assert.That(InkVigilGame.RomajiText(matcher.View), Is.EqualTo("shizuka"));
            Assert.That(InkVigilGame.KanaText(matcher.View), Is.EqualTo("しずか"));
            matcher.Type('s');
            matcher.Type('i');
            Assert.That(InkVigilGame.RomajiText(matcher.View), Is.EqualTo("<color=#FFD66E>si</color>zuka"));
            Assert.That(InkVigilGame.KanaText(matcher.View), Is.EqualTo("<color=#6E6A86>し</color>ずか"));
        }

        private string Snapshot()
        {
            _game.Render();
            var enemies = string.Join(",", Enumerable.Range(0, InkVigilStage.MaxLanes).Select(l => Stage.EnemyRenderer(l).enabled));
            var plates = string.Join(",", Enumerable.Range(0, InkVigilStage.MaxLanes).Select(l => _game.PlateOf(l).style.display.value));
            return $"{enemies}|{plates}|{Stage.Puff.enabled}|{Stage.Flash.enabled}|{Shown("stats")}|{Shown("typing")}|{Shown("panel")}|{Text("headline")}|{Text("message")}|{Marker(Difficulty.Normal)}|{_game.ViewCamera.transform.position}";
        }

        private static T Get<T>(InkVigilStage stage, string field) =>
            (T)typeof(InkVigilStage).GetField(field, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(stage);

        private static void Set(InkVigilStage stage, string field, object value) =>
            typeof(InkVigilStage).GetField(field, BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(stage, value);
    }
}
