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
        public IEnumerator PressingAnUnaffordableCardDoesNothingWhilePressingAnAffordableOneSelectsIt()
        {
            yield return EnterBattle();
            var cheap = B.Hand.First(c => c.Def.NeedsTarget && B.Affordable(c));

            // 押すと選べることを先に確かめる（押下が届いていないのに「何も起きない」が通るのを防ぐ、013）。
            yield return Press(Hud.CardElement(cheap.Id));
            Assert.That(Flow.Targeting.CardId, Is.EqualTo(cheap.Id));
            yield return _keyboard.Tap(Key.Escape);
            Assert.That(Flow.Targeting.HasCard, Is.False);
            Assert.That(Flow.Paused, Is.False);

            while (B.Energy > 0)
            {
                var spend = B.Hand.First(c => B.Affordable(c));
                Flow.Apply(new Intent(IntentKind.SelectCard, spend.Id));
                if (Flow.Targeting.HasCard) { Flow.Apply(new Intent(IntentKind.Confirm)); }
                Flow.FinishPlayback();
                if (B.IsOver) { Assert.Inconclusive("エネルギーを使い切る前に勝った"); }
            }

            yield return null;
            yield return null;
            var expensive = B.Hand.FirstOrDefault(c => c.Def.Cost > 0);
            Assume.That(expensive, Is.Not.Null);
            var hash = Flow.Run.Hash();
            yield return Press(Hud.CardElement(expensive.Id));
            Assert.That(Flow.Targeting.HasCard, Is.False);
            Assert.That(Flow.Run.Hash(), Is.EqualTo(hash));
            Assert.That(Flow.Notice, Is.EqualTo("エネルギーが足りない"));
            Assert.That(Hud.CardElement(expensive.Id).ClassListContains("card-unaffordable"), Is.True);
        }

        [UnityTest]
        public IEnumerator ClicksAndKeysDuringPlaybackAreDroppedAndDoNotCarryOver()
        {
            yield return EnterBattle();
            Time.timeScale = 0;
            Flow.Apply(new Intent(IntentKind.EndTurn));
            for (int i = 0; i < 2000 && Flow.Playback.Table.Hand.Count < 2; i++) { Flow.Step(); }
            Assume.That(Flow.Playback.Busy, Is.True, "次の番の手札を配っている途中");
            yield return null;
            yield return null;
            var hash = Flow.Run.Hash();
            var shown = Flow.Playback.Table.Hand[0];
            yield return Press(Hud.CardElement(shown.Id));
            Assert.That(Flow.Targeting.HasCard, Is.False);
            yield return Press(Hud.Root.Q("end-turn"));
            yield return _keyboard.Tap(Key.Digit1);
            yield return _keyboard.Tap(Key.Enter);
            Assert.That(Flow.Run.Hash(), Is.EqualTo(hash), "演出中の操作は状態を変えない");
            Assert.That(Flow.Targeting.HasCard, Is.False);

            // 演出が終わった後も、捨てた操作は効かない（持ち越さない）。
            Flow.FinishPlayback();
            yield return null;
            Assert.That(Flow.Run.Hash(), Is.EqualTo(hash));
            Assert.That(Flow.Targeting.HasCard, Is.False);
        }

        [UnityTest]
        public IEnumerator PressingStartOnTheTitleDoesNotAlsoChooseARoute()
        {
            yield return null;
            yield return Press(Hud.PanelElement("start"));
            Assert.That(Flow.Screen, Is.EqualTo(AppScreen.Route));
            yield return null;
            yield return null;
            Assert.That(Flow.Screen, Is.EqualTo(AppScreen.Route), "同じクリックの離しで選択肢を押さない（033）");
            Assert.That(Flow.Run.ChosenAt(Run.BattleOneNode), Is.EqualTo(-1));
            yield return Press(Hud.PanelElement("option-1"));
            Assert.That(Flow.Screen, Is.EqualTo(AppScreen.Battle));
            Assert.That(B.Encounter, Is.SameAs(Flow.Run.Route[0][1].Encounter));
        }

        [UnityTest]
        public IEnumerator RewardRemovalAndRestWorkByClicks()
        {
            yield return EnterBattle();
            for (int i = 0; i < 100 && Flow.Screen == AppScreen.Battle; i++)
            {
                foreach (var e in B.Enemies.Where(e => e.Alive)) { e.Hp = 1; }
                var attack = B.Hand.FirstOrDefault(c => c.Def.Type == CardType.Attack && B.Affordable(c));
                if (attack == null) { Flow.Apply(new Intent(IntentKind.EndTurn)); }
                else
                {
                    Flow.Apply(new Intent(IntentKind.SelectCard, attack.Id));
                    Flow.Apply(new Intent(IntentKind.ClickEnemy, B.Enemies.First(e => e.Alive).Id));
                }

                Flow.FinishPlayback();
            }

            Assert.That(Flow.Screen, Is.EqualTo(AppScreen.Reward));
            yield return null;
            yield return null;
            var picked = Flow.Run.RewardOptions[2];
            yield return Press(Hud.PanelElement("reward-2"));
            Assert.That(Flow.Run.Deck.Last().Def, Is.SameAs(picked));
            yield return null;
            var strike = Flow.Run.Deck.First(c => c.Def.Id == "strike");
            yield return Press(Hud.PanelElement($"chip-{strike.Id}"));
            Assert.That(Flow.RemovalCandidate, Is.EqualTo(strike.Id));
            yield return null;
            yield return Press(Hud.PanelElement("remove"));
            Assert.That(Flow.Run.Deck, Has.No.Member(strike));
            yield return null;
            yield return Press(Hud.PanelElement("leave"));
            Assert.That(Flow.Screen, Is.EqualTo(AppScreen.Rest));
            yield return null;
            var max = Flow.Run.MaxHp;
            yield return Press(Hud.PanelElement("rest-1"));
            Assert.That(Flow.Run.MaxHp, Is.EqualTo(max + _game.Rules.RestMaxHpGain));
            Assert.That(Flow.Screen, Is.EqualTo(AppScreen.Route));
        }

        [UnityTest]
        public IEnumerator KilledEnemyIsShownUntilItsDeathIsPlayedThenDisappears()
        {
            yield return EnterBattle(1);
            Time.timeScale = 0;
            var enemy = B.Enemies.First(e => e.Alive);
            var slot = Enumerable.Range(0, LedgerStage.SlotCount).First(i => _game.Stage.SeatAt(i)?.Id == enemy.Id);
            Assert.That(_game.Stage.Slot(slot).enabled, Is.True, "倒す前は出ている");
            enemy.Hp = 1;
            var card = B.Hand.First(c => c.Def.NeedsTarget && B.Affordable(c));
            Flow.Apply(new Intent(IntentKind.SelectCard, card.Id));
            Flow.Apply(new Intent(IntentKind.ClickEnemy, enemy.Id));
            for (int i = 0; i < 400 && Flow.Playback.Current?.Kind != EventKind.Died; i++) { Flow.Step(); }
            _game.Render();
            Assert.That(_game.Stage.Slot(slot).enabled, Is.True, "倒れる演出の間は出している");
            for (int i = 0; i < 400 && Flow.Playback.Current?.Kind == EventKind.Died; i++) { Flow.Step(); }
            _game.Render();
            yield return null;
            Assert.That(_game.Stage.Slot(slot).enabled, Is.False, "倒れる演出の後は消える");
            Assert.That(Flow.Screen != AppScreen.Battle || Hud.EnemyHit(slot).resolvedStyle.display == DisplayStyle.None, Is.True, "クリック範囲も消える");
        }

        [UnityTest]
        public IEnumerator EnemiesKeepTheirPositionsWhenTheFirstOneDiesAndSummonsTakeTheFreeSpot()
        {
            yield return null;
            var data = _game.Data;
            var deck = data.StarterDeck.Select((d, i) => new CardRef(i + 1, d)).ToArray();
            var encounter = data.Encounters.First(e => e.Foes.Count == 2);
            var battle = new Battle(data, _game.Rules, deck, 70, 70, encounter, 1, 0);
            var playback = new EventPlayback(battle, data, _game.Settings.Timing);
            playback.Finish();
            var slots = new Seat[LedgerStage.SlotCount];
            LedgerStage.Assign(playback.Table, slots);
            Assert.That(slots[0].Id, Is.EqualTo(1));
            Assert.That(slots[1].Id, Is.EqualTo(2));
            Assert.That(slots[2], Is.Null);

            playback.Table.SeatById(1).Hp = 0;
            LedgerStage.Assign(playback.Table, slots);
            Assert.That(slots[1].Id, Is.EqualTo(2), "後ろの敵は詰めない");

            var boss = new Battle(data, _game.Rules, deck, 70, 70, data.Encounters.First(e => e.Tier == EncounterTier.Boss), 1, 0);
            boss.EnemyById(1).Intent = boss.EnemyById(1).Def.Moves[boss.EnemyById(1).Def.HalfMove];
            boss.EndTurn();
            var bossPlayback = new EventPlayback(boss, data, _game.Settings.Timing);
            bossPlayback.Finish();
            LedgerStage.Assign(bossPlayback.Table, slots);
            Assert.That(slots[0].Id, Is.EqualTo(1));
            Assert.That(slots[1].Def.Id, Is.EqualTo("imp"), "呼ばれた敵は空いた位置へ");
        }

        [UnityTest]
        public IEnumerator EscapePausesTheBattleAndResumes()
        {
            yield return EnterBattle();
            yield return _keyboard.Tap(Key.Escape);
            Assert.That(Flow.Paused, Is.True);
            yield return null;
            Assert.That(Hud.Panel.resolvedStyle.display, Is.EqualTo(DisplayStyle.Flex));
            Assert.That(Hud.PanelElement("resume"), Is.Not.Null);
            yield return Press(Hud.PanelElement("resume"));
            Assert.That(Flow.Paused, Is.False);
            yield return null;
            Assert.That(Hud.Panel.resolvedStyle.display, Is.EqualTo(DisplayStyle.None));
        }

        [UnityTest]
        public IEnumerator BackToTitleAfterPlayingLooksLikeTheStartup()
        {
            yield return null;
            var playerAtStart = _game.Stage.Player.transform.localPosition;
            yield return EnterBattle(1);
            var card = B.Hand.First(c => c.Def.NeedsTarget && B.Affordable(c));
            Flow.Apply(new Intent(IntentKind.SelectCard, card.Id));
            yield return _keyboard.Tap(Key.Escape);
            yield return _keyboard.Tap(Key.Escape);
            Assert.That(Flow.Paused, Is.True);
            yield return _keyboard.Tap(Key.T);
            Assert.That(Flow.Screen, Is.EqualTo(AppScreen.Title));
            yield return null;
            Assert.That(_game.Stage.Player.transform.localPosition, Is.EqualTo(playerAtStart));
            Assert.That(_game.Stage.Player.color, Is.EqualTo(Color.white));
            Assert.That(_game.Stage.Marker.enabled, Is.False);
            Assert.That(Enumerable.Range(0, LedgerStage.SlotCount).All(i => !_game.Stage.Slot(i).enabled), Is.True);
            Assert.That(Hud.Root.Q("battle").resolvedStyle.display, Is.EqualTo(DisplayStyle.None));
            Assert.That(Hud.Root.Q("seats").resolvedStyle.display, Is.EqualTo(DisplayStyle.None));
            Assert.That(Hud.Arrow.Visible, Is.False);
            Assert.That(Hud.PanelElement("start"), Is.Not.Null);
            Assert.That(Hud.Root.Q<Label>("route").text, Is.Empty);
        }

        [UnityTest]
        public IEnumerator PlayingACardPlaysASound()
        {
            yield return EnterBattle();
            var before = _game.SoundsPlayed;
            var card = B.Hand.First(c => c.Def.NeedsTarget && B.Affordable(c));
            Flow.Apply(new Intent(IntentKind.SelectCard, card.Id));
            Flow.Apply(new Intent(IntentKind.Confirm));
            yield return WaitUntil(() => _game.SoundsPlayed > before);
            Assert.That(_game.LastSound, Is.EqualTo("Card"));
            yield return WaitUntil(() => _game.LastSound == "Hit" || _game.LastSound == "Died");
        }

        [UnityTest]
        public IEnumerator AutopilotFinishesARunInTheRealSceneAndTheHudFollowsTheTable()
        {
            yield return null;
            Time.timeScale = 0;
            var pilot = new Autopilot();
            var resolve = new System.Collections.Generic.List<double>();
            var draw = new System.Collections.Generic.List<double>();
            var watch = new System.Diagnostics.Stopwatch();
            int maxHand = 0, maxEnemies = 0, checks = 0;
            for (int i = 0; i < 400_000 && Flow.Screen != AppScreen.Result; i++)
            {
                var intent = pilot.Decide(Flow);
                if (intent != null)
                {
                    var battleAction = Flow.Screen == AppScreen.Battle;
                    watch.Restart();
                    Flow.Apply(intent.Value);
                    watch.Stop();
                    if (battleAction && Flow.Playback?.Busy == true) { resolve.Add(watch.Elapsed.TotalMilliseconds); }
                }
                else
                {
                    Flow.Step();
                }

                _game.ConsumeEvents();
                watch.Restart();
                _game.Render();
                watch.Stop();
                draw.Add(watch.Elapsed.TotalMilliseconds);
                if (Flow.Playback != null && Flow.Screen == AppScreen.Battle)
                {
                    maxHand = Math.Max(maxHand, Flow.Playback.Table.Hand.Count);
                    maxEnemies = Math.Max(maxEnemies, Flow.Playback.Table.Seats.Count(s => s.Id > 0 && s.Alive));
                    if (i % 97 == 0)
                    {
                        AssertHudMatchesTheTable();
                        checks++;
                    }
                }

                if (i % 300 == 0) { yield return null; }
            }

            Assert.That(Flow.Screen, Is.EqualTo(AppScreen.Result), "操縦役が決着まで進む");
            Assert.That(Flow.Run.Stage, Is.EqualTo(RunStage.Victory));
            Assert.That(checks, Is.GreaterThan(10));
            yield return null;
            Assert.That(Hud.PanelElement("to-title"), Is.Not.Null);
            resolve.Sort();
            draw.Sort();
            double P(System.Collections.Generic.List<double> list, double q) => list[Math.Min(list.Count - 1, (int)(list.Count * q))];
            Debug.Log($"[Perf] カードの解決 {resolve.Count} 回：p50 {P(resolve, .5):0.000} ms / p95 {P(resolve, .95):0.000} ms / 最大 {resolve[resolve.Count - 1]:0.000} ms、" +
                $"描画の更新 {draw.Count} 回：p50 {P(draw, .5):0.000} ms / p95 {P(draw, .95):0.000} ms / 最大 {draw[draw.Count - 1]:0.000} ms、最多の手札 {maxHand}・敵 {maxEnemies}");
            Assert.That(P(resolve, .95), Is.LessThan(5), "1 操作（解決）の p95");
            Assert.That(P(draw, .95), Is.LessThan(5), "描画の更新の p95");
        }

        [UnityTest]
        public IEnumerator IdleBattleFramesAllocateLittle()
        {
            yield return EnterBattle(1);
            using var recorder = global::Unity.Profiling.ProfilerRecorder.StartNew(global::Unity.Profiling.ProfilerCategory.Memory, "GC Allocated In Frame");
            for (int i = 0; i < 10; i++) { yield return null; }
            var samples = new System.Collections.Generic.List<long>();
            for (int i = 0; i < 60; i++)
            {
                yield return null;
                samples.Add(recorder.LastValue);
            }

            samples.Sort();
            Debug.Log($"[Perf] 戦闘の待ち（入力待ち）60 フレームの GC 割り当て：p50 {samples[30]} B / 最大 {samples[59]} B、0 でないフレーム {samples.Count(v => v > 0)}");
            Assert.That(samples[59], Is.LessThan(64 * 1024), "1 フレームの割り当て");
        }

        /// <summary>HUD の数字と手札が、演出の再生位置の卓と一致する（Core の解決済みの値ではない）。</summary>
        private void AssertHudMatchesTheTable()
        {
            var table = Flow.Playback.Table;
            Assert.That(Hud.EnergyText.text, Is.EqualTo($"{table.Energy}/{_game.Rules.Energy}"));
            foreach (var card in table.Hand) { Assert.That(Hud.CardElement(card.Id), Is.Not.Null, "卓の手札は全て描く"); }
            for (int slot = 0; slot < LedgerStage.SlotCount; slot++)
            {
                var seat = _game.Stage.SeatAt(slot);
                if (seat == null || !seat.Alive || !_game.Stage.Slot(slot).enabled) { continue; }
                Assert.That(Hud.SeatText(slot), Does.Contain($"{seat.Hp}/{seat.MaxHp}"));
            }
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
