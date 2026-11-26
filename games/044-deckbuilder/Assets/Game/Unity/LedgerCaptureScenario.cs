using System;
using System.Linq;
using Game.App;
using Game.Core;
using Game.Unity.Evidence;
using UnityEngine;

namespace Game.Unity
{
    /// <summary>
    /// 証跡の台本。実時間とキー入力を使わず、意図と操縦役と Tick で場面を作って撮る。シードは撮影用の固定値（<see cref="EvidenceCapture.Seed"/>）。
    /// </summary>
    public sealed class LedgerCaptureScenario : MonoBehaviour, ICaptureScenario
    {
        /// <summary>1 つの場面を待つ上限（操作か Tick の数）。</summary>
        private const int SceneLimit = 200_000;

        private LedgerGame _game;
        private IPilot _pilot;

        private DeckFlow F => _game.Flow;

        public async Awaitable RunAsync(CaptureContext capture)
        {
            _game = GetComponent<LedgerGame>();
            await capture.WaitUntilAsync(() => _game.HudRoot != null, 10);
            capture.Record("settings", JsonUtility.ToJson(_game.Settings));
            _pilot = new Autopilot();

            await Shoot(capture, "01-title", "タイトル");
            Apply(IntentKind.HowTo);
            await Shoot(capture, "02-howto", "遊び方（操作・効果の順序・意図の印・状態異常・ルート）");
            Apply(IntentKind.Cancel);
            Apply(IntentKind.Confirm);
            await Shoot(capture, "03-route", "ルートの地図と戦 1 の 2 択（通常／手強い）");

            Apply(IntentKind.Option, 1);
            Ticks(() => !F.Playback.Busy);
            await Shoot(capture, "04-turn", "戦 1 の最初のターン。敵の頭上に次の行動（意図）、下に手札とエネルギー");

            var card = F.Run.Battle.Hand.First(c => c.Def.NeedsTarget && F.Run.Battle.Affordable(c));
            Apply(IntentKind.SelectCard, card.Id);
            Apply(IntentKind.CycleTarget, 1);
            await Shoot(capture, "05-target", $"「{card.Def.Name}」を選び、対象を選んでいるところ（矢印と足元の印）");

            Apply(IntentKind.Confirm);
            Ticks(() => F.Playback.Current is { Kind: EventKind.Damaged } && F.Playback.Age >= 4);
            await Shoot(capture, "06-hit", "解決の演出の途中。Core は解決し終えているが、卓は出来事を順に当てはめている（当たった数字）");

            Ticks(() => !F.Playback.Busy);
            Apply(IntentKind.EndTurn);
            Ticks(() => F.Playback.Current is { Kind: EventKind.EnemyActed } && F.Playback.Age >= 8);
            await Shoot(capture, "07-enemy", "敵の番。公開していた行動を実行する");

            Pilot(() => F.Screen == AppScreen.Reward);
            await Shoot(capture, "08-reward", "報酬：カード 3 択（手強い方を選んだので希少度が上がる）と、デッキから 1 枚の除去");
            Pilot(() => F.Run.RewardTaken);
            Apply(IntentKind.PickRemoval, F.Run.Deck.First(c => c.Def.Id == "strike").Id);
            await Shoot(capture, "09-removal", "カードを取った後、除去の候補（打ち込み）を選んだところ");

            Pilot(() => F.Screen == AppScreen.Rest);
            await Shoot(capture, "10-rest", "休憩：回復か最大 HP の増加");
            Pilot(() => F.Screen == AppScreen.Route);
            await Shoot(capture, "11-route2", "戦 2 の 2 択");

            Pilot(() => F.Screen == AppScreen.Route && F.Run.Node == Run.BossNode);
            await Shoot(capture, "12-boss-route", "ボスの節");
            Pilot(() => F.Screen == AppScreen.Battle && !F.Playback.Busy);
            await Shoot(capture, "13-boss", "ボス戦の始め");
            Pilot(() => F.Screen != AppScreen.Battle || (F.Playback.Current is { Kind: EventKind.Summoned } && F.Playback.Age >= 12));
            if (F.Screen == AppScreen.Battle) { await Shoot(capture, "14-summon", "ボスが HP 半分で一度だけ仲間を呼ぶ"); }
            Pilot(() => F.Screen != AppScreen.Battle || (F.Playback.Current is { Kind: EventKind.Victory or EventKind.Defeat } && F.Playback.Age >= 10));
            if (F.Screen == AppScreen.Battle) { await Shoot(capture, "15-ending", "ボス戦の決着"); }
            Pilot(() => F.Screen == AppScreen.Result);
            await Shoot(capture, "16-result", "結果の画面");

            // 新しい挑戦（シード 2）で、ターン終了だけを続けて倒れた結果を撮る。
            Apply(IntentKind.Confirm);
            Apply(IntentKind.Confirm);
            Apply(IntentKind.Option, 1);
            _pilot = new EndTurnPilot();
            Pilot(() => F.Screen == AppScreen.Result);
            await Shoot(capture, "17-defeat", "ターン終了だけを続けて倒れた結果。倒れたら最初から");

            Apply(IntentKind.Confirm);
            Apply(IntentKind.Confirm);
            Apply(IntentKind.Cancel);
            await Shoot(capture, "18-pause", "一時停止");
        }

        private void Apply(IntentKind kind, int value = 0)
        {
            F.Apply(new Intent(kind, value));
            _game.ConsumeEvents();
        }

        /// <summary>Tick だけを進めて条件を待つ（操作はしない）。</summary>
        private void Ticks(Func<bool> done)
        {
            for (int i = 0; i < SceneLimit; i++)
            {
                if (done()) { return; }
                F.Step();
                _game.ConsumeEvents();
            }

            throw new InvalidOperationException("撮影場面を作れませんでした（Tick）。");
        }

        /// <summary>操縦役と Tick で条件を待つ。演出の途中は Tick を進め、入力を受け付けるときは操縦役の意図を 1 つ。</summary>
        private void Pilot(Func<bool> done)
        {
            for (int i = 0; i < SceneLimit; i++)
            {
                if (done()) { return; }
                var intent = _pilot.Decide(F);
                if (intent != null) { F.Apply(intent.Value); }
                else { F.Step(); }
                _game.ConsumeEvents();
            }

            throw new InvalidOperationException("撮影場面を作れませんでした（操縦役）。");
        }

        private async Awaitable Shoot(CaptureContext capture, string name, string scene)
        {
            // 作り直した HUD の要素は次のフレームで配置されるので、1 フレーム置いてから描き直して撮る（矢印は配置済みの位置から引く）。
            _game.Render();
            await Awaitable.NextFrameAsync();
            _game.Render();
            var run = F.Run;
            var detail = run == null
                ? scene
                : $"{scene}。シード {F.Seed}、節 {run.Node}、HP {run.Hp}/{run.MaxHp}、デッキ {run.Deck.Count} 枚、Tick {F.Tick}";
            await capture.ShootAsync(name, detail);
        }

        /// <summary>ターン終了だけを続ける操縦役（倒れる場面を作る）。</summary>
        private sealed class EndTurnPilot : IPilot
        {
            public Intent? Decide(DeckFlow flow) => flow.Screen == AppScreen.Battle && flow.AcceptsBattleInput ? new Intent(IntentKind.EndTurn) : null;
        }
    }
}
