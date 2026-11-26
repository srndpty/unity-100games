using System;
using Game.App;
using Game.Core;
using Game.Unity.Evidence;
using UnityEngine;

namespace Game.Unity
{
    /// <summary>
    /// 証跡の台本。実時間とキー入力を使わず、意図と操縦役の 1 操作ずつで場面を作って撮る。シードは撮影用の固定値（<see cref="EvidenceCapture.Seed"/>）。
    /// 場面の判定も <see cref="PlayerView"/>（画面に出ている情報）だけで行う。
    /// </summary>
    public sealed class LanternCaptureScenario : MonoBehaviour, ICaptureScenario
    {
        private const double ClockTime = 0;

        /// <summary>1 つの場面を待つ上限（操作の数）。</summary>
        private const int SceneLimit = 20_000;

        private LanternGame _game;

        private PlayerView V => _game.Flow.View ?? throw new InvalidOperationException("試合がありません。");

        public async Awaitable RunAsync(CaptureContext capture)
        {
            _game = GetComponent<LanternGame>();
            await capture.WaitUntilAsync(() => _game.HudRoot != null, 10);
            capture.Record("settings", JsonUtility.ToJson(_game.Settings));

            _game.Render();
            await Shoot(capture, "01-title", "タイトル（背景は見本の階を記憶の色で描いたもの）");
            Apply(Intent.Of(IntentKind.Submit, ClockTime));
            await Shoot(capture, "02-howto", "遊び方（操作・ターン・視界と記憶・満腹度・勝ち負け）");

            Apply(Intent.Of(IntentKind.Submit, ClockTime));
            await Shoot(capture, "03-start", "1 階の始め。画面は自機の周りだけを映し、見えているのは灯りの届く範囲だけ（他の地形は未知で黒）");

            _game.Flow.Pilot = new Autopilot(_game.Rules);
            StepUntil(() => V.Turns >= 60 && V.Monsters.Count == 0, "探索の途中");
            await Shoot(capture, "04-explored", "探索の途中。見た地形は暗く残り（既知マップ）、明るいのは今の視界だけ");

            Apply(Intent.Down(Control.Map, ClockTime));
            await Shoot(capture, "05-map", "M を押している間だけ重ねる全体の地図（見た床・階段・記憶の品・見えている敵・自機）");
            Apply(Intent.Up(Control.Map, ClockTime));

            StepUntil(() => Has(EventKind.Attack) && V.Monsters.Count > 0, "戦い");
            await Shoot(capture, "06-fight", "敵と戦う。下の欄に見えた出来事だけが文で出る");

            StepUntil(() => V.Inventory.Count >= 4 && V.Monsters.Count == 0, "持ち物");
            Apply(Intent.Of(IntentKind.ToggleInventory, ClockTime));
            Apply(Intent.Move(1, ClockTime));
            await Shoot(capture, "07-inventory", "持ち物の画面（品の絵と名前の 2 列、矢印の上下左右で選ぶ。使う・投げる・置く、選んだ品の説明）");
            Apply(Intent.Of(IntentKind.ToggleInventory, ClockTime));

            StepUntil(() => Has(EventKind.ThrowHit), "投げる");
            await Shoot(capture, "08-throw", "直線上の敵へ品を投げて当てた");

            StepUntil(() => V.Floor == 3, "3 階");
            await Shoot(capture, "09-floor3", "階段を降りて 3 階。新しい階の既知マップは空から始まる");

            StepUntil(() => V.Satiety <= _game.Rules.HungryWarning, "空腹");
            await Shoot(capture, "10-hungry", "満腹度が 20 まで減って「おなかが減ってきた」（10 行動で 1 減る）");

            StepUntil(() => V.Floor == 5, "5 階");
            StepUntil(() => V.Turns % 10 == 0 && V.Monsters.Count == 0, "5 階の探索");
            await Shoot(capture, "11-floor5", "5 階。この階の階段を降りれば生還");

            StepUntil(() => _game.Flow.Screen == AppScreen.Result, "結果");
            await Shoot(capture, "12-result", "結果の画面（勝敗・レベル・行動数・倒した敵）");

            // 新しい試合（シード 2）で、足踏みだけを続けて倒れた結果を撮る。
            Apply(Intent.Of(IntentKind.Submit, ClockTime));
            Apply(Intent.Of(IntentKind.Submit, ClockTime));
            Apply(Intent.Of(IntentKind.Submit, ClockTime));
            _game.Flow.Pilot = new WaitingPilot();
            StepUntil(() => _game.Flow.Screen == AppScreen.Result, "倒れる");
            await Shoot(capture, "13-dead", "足踏みを続けて敵に倒された結果。倒れたら最初から");

            Apply(Intent.Of(IntentKind.Submit, ClockTime));
            Apply(Intent.Of(IntentKind.Submit, ClockTime));
            Apply(Intent.Of(IntentKind.Submit, ClockTime));
            Apply(Intent.Of(IntentKind.Back, ClockTime));
            await Shoot(capture, "14-pause", "一時停止");
        }

        private bool Has(EventKind kind)
        {
            foreach (var e in V.Events)
            {
                if (e.Kind == kind) { return true; }
            }

            return false;
        }

        private void Apply(Intent intent)
        {
            _game.Flow.Apply(intent);
            _game.ConsumeEvents();
        }

        private void StepUntil(Func<bool> done, string scene)
        {
            for (int i = 0; i < SceneLimit; i++)
            {
                if (_game.Flow.View != null && done()) { return; }
                if (_game.Flow.Screen != AppScreen.Playing) { break; }
                _game.Flow.StepPilot();
                _game.ConsumeEvents();
            }

            if (_game.Flow.View != null && done()) { return; }
            throw new InvalidOperationException("撮影場面を作れませんでした: " + scene);
        }

        private async Awaitable Shoot(CaptureContext capture, string name, string scene)
        {
            _game.Render();
            var view = _game.Flow.View;
            var detail = view == null
                ? scene
                : $"{scene}。シード {_game.Flow.Seed}、{view.Floor} 階、行動 {view.Turns}、HP {view.Hp}/{view.MaxHp}、満腹 {view.Satiety}、Lv {view.Level}";
            await capture.ShootAsync(name, detail);
        }

        /// <summary>足踏みだけを続ける操縦役（倒れる場面を作る）。</summary>
        private sealed class WaitingPilot : IPilot
        {
            public Command Decide(PlayerView view) => Command.Wait();

            public void Reset()
            {
            }
        }
    }
}
