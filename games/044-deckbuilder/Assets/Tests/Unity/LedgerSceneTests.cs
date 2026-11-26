using System;
using System.Collections;
using System.Linq;
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
    /// <summary>実シーンを読み込み、仮想のキーボードと UI の押下イベントで操作する。</summary>
    public sealed class LedgerSceneTests
    {
        private TestKeyboard _keyboard;
        private LedgerGame _game;

        [UnitySetUp]
        public IEnumerator Load()
        {
            _keyboard = new TestKeyboard();
            yield return SceneManager.LoadSceneAsync("CinderLedger");
            yield return null;
            _game = Object.FindAnyObjectByType<LedgerGame>();
            for (int i = 0; i < 30 && _game.HudRoot == null; i++) { yield return null; }
            Assert.That(_game.HudRoot, Is.Not.Null);
        }

        [UnityTearDown]
        public IEnumerator Unload()
        {
            _keyboard.Dispose();
            Time.timeScale = 1;
            yield return null;
        }

        private DeckFlow Flow => _game.Flow;

        private Battle B => Flow.Run.Battle;

        private LedgerHud Hud => _game.Hud;

        private static IEnumerator WaitUntil(Func<bool> condition, float seconds = 10f)
        {
            float limit = Time.realtimeSinceStartup + seconds;
            while (!condition() && Time.realtimeSinceStartup < limit) { yield return null; }
            Assert.That(condition(), Is.True, "時間内に条件を満たさなかった");
        }

        /// <summary>要素の中央を左ボタンで押す（UI Toolkit の押下イベントを送る、013）。</summary>
        private static IEnumerator Press(VisualElement element)
        {
            Assert.That(element, Is.Not.Null);
            Assert.That(float.IsNaN(element.worldBound.x), Is.False, "まだ配置されていない");
            var point = element.worldBound.center;
            Assert.That(element.panel.Pick(point), Is.SameAs(element).Or.Property("parent").SameAs(element), "押す位置に別の要素が重なっている");
            using (var down = PointerDownEvent.GetPooled(new Event { type = EventType.MouseDown, mousePosition = point, button = 0 }))
            {
                down.target = element;
                element.SendEvent(down);
            }

            using (var up = PointerUpEvent.GetPooled(new Event { type = EventType.MouseUp, mousePosition = point, button = 0 }))
            {
                up.target = element;
                element.SendEvent(up);
            }

            yield return null;
            yield return null;
        }

        /// <summary>タイトルから戦 1 の選択肢 option に入り、最初の演出が終わるまで待つ。</summary>
        private IEnumerator EnterBattle(int option = 0)
        {
            yield return _keyboard.Tap(Key.Enter);
            Assert.That(Flow.Screen, Is.EqualTo(AppScreen.Route));
            yield return _keyboard.Tap(option == 0 ? Key.Digit1 : Key.Digit2);
            Assert.That(Flow.Screen, Is.EqualTo(AppScreen.Battle));
            Flow.FinishPlayback();
            yield return WaitUntil(() => Flow.AcceptsBattleInput);
            yield return null;
        }

        [UnityTest]
        public IEnumerator Scene_HasOneStageMatchingTheData_AndShowsTheTitle()
        {
            yield return null;
            Assert.That(Object.FindObjectsByType<LedgerStage>(FindObjectsInactive.Include), Has.Length.EqualTo(1));
            Assert.That(_game.Stage.Mismatch(_game.Data), Is.Null);
            Assert.That(Flow.Screen, Is.EqualTo(AppScreen.Title));
            Assert.That(Hud.Panel.resolvedStyle.display, Is.EqualTo(DisplayStyle.Flex));
            Assert.That(Hud.PanelElement("start"), Is.Not.Null);
            Assert.That(_game.Stage.Player.enabled, Is.True);
            Assert.That(Enumerable.Range(0, LedgerStage.SlotCount).All(i => !_game.Stage.Slot(i).enabled), Is.True, "タイトルでは敵を出さない");
        }

        [UnityTest]
        public IEnumerator SettingsAssetValidatesAndMatchesTheCodeDefaults()
        {
            yield return null;
            Assert.That(() => _game.Settings.Validate(), Throws.Nothing);
            var defaults = ScriptableObject.CreateInstance<LedgerSettings>();
            try
            {
                Assert.That(JsonUtility.ToJson(_game.Settings.Rules), Is.EqualTo(JsonUtility.ToJson(defaults.Rules)), "コードの既定値を変えたら .asset も直す（024）");
                Assert.That(JsonUtility.ToJson(_game.Settings.Timing), Is.EqualTo(JsonUtility.ToJson(defaults.Timing)));
                Assert.That(_game.Settings.Volume, Is.EqualTo(defaults.Volume));
            }
            finally
            {
                Object.Destroy(defaults);
            }
        }

        [UnityTest]
        public IEnumerator AudioIsMutedInBatchRuns()
        {
            yield return null;
            Assert.That(_game.Audio.mute, Is.EqualTo(AudioPolicy.ShouldMute));
            if (Application.isBatchMode) { Assert.That(_game.Audio.mute, Is.True); }
            Assert.That(_game.Audio.volume, Is.LessThanOrEqualTo(.2f));
        }

        [UnityTest]
        public IEnumerator KeyboardSelectsACardAndConfirmsTheTarget()
        {
            yield return EnterBattle();
            var hand = Flow.Playback.Table.Hand;
            var index = hand.ToList().FindIndex(c => c.Def.NeedsTarget && B.Affordable(c));
            Assume.That(index, Is.GreaterThanOrEqualTo(0));
            var card = hand[index];
            var hp = B.Enemies.Sum(e => e.Hp);
            yield return _keyboard.Tap(Key.Digit1 + index);
            Assert.That(Flow.Targeting.CardId, Is.EqualTo(card.Id));
            yield return _keyboard.Tap(Key.Enter);
            Assert.That(B.Events.Any(e => e.Kind == EventKind.CardPlayed && e.Detail == card.Id), Is.True);
            Assert.That(B.Enemies.Sum(e => e.Hp), Is.LessThan(hp));
        }

        [UnityTest]
        public IEnumerator ClickingACardThenAnEnemyPlaysItOnThatEnemyAndShowsTheArrowMeanwhile()
        {
            yield return EnterBattle(1);
            var card = B.Hand.First(c => c.Def.NeedsTarget && B.Affordable(c));
            yield return Press(Hud.CardElement(card.Id));
            Assert.That(Flow.Targeting.CardId, Is.EqualTo(card.Id));
            yield return null;
            Assert.That(Hud.Arrow.Visible, Is.True, "対象を選ぶ間は矢印を出す");
            Assert.That(Hud.Arrow.From, Is.Not.EqualTo(Hud.Arrow.To));
            Assert.That(_game.Stage.Marker.enabled, Is.True);

            var slot = Enumerable.Range(0, LedgerStage.SlotCount).Last(i => _game.Stage.SeatAt(i) != null && _game.Stage.SeatAt(i).Alive);
            var target = _game.Stage.SeatAt(slot).Id;
            yield return Press(Hud.EnemyHit(slot));
            var played = B.Events.Last(e => e.Kind == EventKind.CardPlayed);
            Assert.That(played.Detail, Is.EqualTo(card.Id));
            Assert.That(played.Target, Is.EqualTo(target));
            Assert.That(Hud.Arrow.Visible, Is.False);
        }

        [UnityTest]
        public IEnumerator HudShowsTheTableNotTheResolvedCoreDuringPlayback()
        {
            yield return EnterBattle();
            Time.timeScale = 0;
            var card = B.Hand.First(c => c.Def.NeedsTarget && B.Affordable(c));
            var enemy = B.Enemies.First(e => e.Alive);
            var slot = Enumerable.Range(0, LedgerStage.SlotCount).First(i => _game.Stage.SeatAt(i)?.Id == enemy.Id);
            var before = enemy.Hp;
            Flow.Apply(new Intent(IntentKind.SelectCard, card.Id));
            Flow.Apply(new Intent(IntentKind.ClickEnemy, enemy.Id));
            Flow.Step();
            yield return null;
            Assert.That(enemy.Hp, Is.LessThan(before));
            Assert.That(Hud.SeatText(slot), Does.Contain($"{before}/{enemy.MaxHp}"), "当たる前は前の HP のまま");
            for (int i = 0; i < 200 && Flow.Playback.Current?.Kind != EventKind.Damaged; i++) { Flow.Step(); }
            yield return null;
            Assert.That(Hud.SeatText(slot), Does.Contain($"{enemy.Hp}/{enemy.MaxHp}"));
        }
    }
}
