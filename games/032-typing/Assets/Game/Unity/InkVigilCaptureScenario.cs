using System;
using System.Linq;
using Game.App;
using Game.Core;
using Game.Unity.Evidence;
using UnityEngine;

namespace Game.Unity
{
    /// <summary>
    /// 証跡の台本。実時間と入力を使わず、意図と Tick を順に渡して場面を作って撮る。シードは撮影用の固定値（<see cref="EvidenceCapture.Seed"/>）。
    /// 意図の時刻はすべて 0 にする（時計で Tick を進めず、台本の StepTick だけで進める）。
    /// 1 試合目（ふつう）は打鍵を 1 つずつ渡して、狙い・別の綴り・誤字・Backspace・撃破・被弾を撮る。
    /// 2 試合目（ふつう）は速い操縦役でクリア、3 試合目（むずかしい）は遅い操縦役で失敗する。
    /// </summary>
    public sealed class InkVigilCaptureScenario : MonoBehaviour, ICaptureScenario
    {
        private const int TickLimit = 60 * 60 * 5;
        private const double ClockTime = 0;

        private InkVigilGame _game;

        public async Awaitable RunAsync(CaptureContext capture)
        {
            _game = GetComponent<InkVigilGame>();
            await capture.WaitUntilAsync(() => _game.HudRoot != null, 10);
            capture.Record("settings", JsonUtility.ToJson(_game.Settings.Rules));

            _game.Render();
            await capture.ShootAsync("01-title", "タイトル（難度の選択と操作の説明）");

            Apply(Intent.Of(IntentKind.Submit, ClockTime));
            StepUntil(() => Run.Enemies.Count >= 3, "敵が 3 体");
            Step(40);
            await Shoot(capture, "02-approach", "ふつう：奥から 3 体が迫る。札に表示文・読み・ローマ字の案内");

            // いちばん手前の敵を 3 打ぶん打つ。
            var first = Typist.Nearest(Run)!;
            TypeGuide(3);
            await Shoot(capture, "03-locked", $"最初の 1 打で手前の敵（{first.Word.Display}）を狙い、打った部分の色が変わる");

            // 狙い中に誤字を打つ：進まず、下の表示が赤くなる。
            Apply(Intent.Type(WrongKey(), ClockTime));
            await Shoot(capture, "04-miss", "誤字：狙いの言葉は進まず、連続が 0 に戻り、下の表示が赤くなる");

            // Backspace で狙いを外す。
            Apply(Intent.Of(IntentKind.Backspace, ClockTime));
            Step(1);
            await Shoot(capture, "05-backspace", "Backspace：狙いを外し、その敵の打った分を最初に戻す");

            Step(_game.Settings.EffectTicks);

            // 「し」を含む語を別の綴り（si）で打つ。場面づくりのため、いちばん手前に置く（撮影のシードは固定なので毎回同じ）。
            var word = _game.Flow.Book.Entries.First(e => e.Reading.Kana == "しずかなとしょしつ");
            var lane = Enumerable.Range(0, Run.Lanes).First(l => Run.Enemies.All(e => e.Lane != l));
            var enemy = Run.Place(word, lane, Math.Min(Run.Enemies.Max(e => e.Age) + 1, Run.ApproachTicks / 2));
            TypeKeys("sizuka");
            await Shoot(capture, "06-alternative", $"別の綴り：{word.Display}（しずかなとしょしつ）の「し」を si で打っても受け付け、案内は続きを示す");

            TypeKeys(enemy.Matcher.Guide);
            Step(4);
            await Shoot(capture, "07-defeated", $"{word.Display} を打ち切って撃破（煙の演出）");

            StepUntil(() => Happened(FlowEvents.Hit), "被弾");
            Step(3);
            await Shoot(capture, "08-hit", "打たずにいた敵が手前の線に着いて被弾（体力が減り、画面が赤く揺れる）");

            Apply(Intent.Of(IntentKind.Cancel, ClockTime));
            await Shoot(capture, "09-paused", "一時停止");
            Apply(Intent.Type('t', ClockTime));

            _game.Flow.Pilot = new Typist(8, mistakePermil: 40);
            Apply(Intent.Of(IntentKind.Submit, ClockTime));
            StepUntil(() => _game.Flow.Screen == AppScreen.Result, "クリア");
            await Shoot(capture, "10-cleared", "速い操縦役（7.5 打/秒、誤字 4%）でふつうをクリアした結果");

            Apply(Intent.Of(IntentKind.Cancel, ClockTime));
            Apply(Intent.Of(IntentKind.Down, ClockTime));
            _game.Flow.Pilot = new Typist(30);
            Apply(Intent.Of(IntentKind.Submit, ClockTime));
            StepUntil(() => _game.Flow.Screen == AppScreen.Result, "失敗");
            await Shoot(capture, "11-failed", "遅い操縦役（2 打/秒）でむずかしいに失敗した結果");
        }

        private TypingRun Run => _game.Flow.Run ?? throw new InvalidOperationException("試合がありません。");

        private bool Happened(FlowEvents events) => (_game.LastEvents & events) != 0;

        /// <summary>狙い（なければ手前の敵）が受け付けない文字。</summary>
        private char WrongKey()
        {
            var target = Run.Target ?? Typist.Nearest(Run)!;
            return RomajiTable.Keys.First(key => !target.Matcher.Accepts(key));
        }

        /// <summary>手前の敵の案内を <paramref name="count"/> 打ぶん打つ。</summary>
        private void TypeGuide(int count)
        {
            for (var i = 0; i < count; i++)
            {
                var target = Run.Target ?? Typist.Nearest(Run)!;
                Apply(Intent.Type(target.Matcher.Guide[0], ClockTime));
            }
        }

        private void TypeKeys(string keys)
        {
            foreach (var key in keys) { Apply(Intent.Type(key, ClockTime)); }
        }

        private void Apply(Intent intent)
        {
            _game.Flow.Apply(intent);
            _game.ConsumeEvents();
        }

        private void Step(int ticks)
        {
            for (var i = 0; i < ticks && _game.Flow.Screen == AppScreen.Playing; i++)
            {
                _game.Flow.StepTick();
                _game.ConsumeEvents();
            }
        }

        /// <summary><paramref name="done"/> が成り立つ Tick まで進める。</summary>
        private void StepUntil(Func<bool> done, string scene)
        {
            for (var i = 0; ; i++)
            {
                if (_game.Flow.Run != null && done()) { return; }
                if (i > TickLimit || (_game.Flow.Screen != AppScreen.Playing && !done()))
                {
                    throw new InvalidOperationException("撮影場面を作れませんでした: " + scene);
                }

                _game.Flow.StepTick();
                _game.ConsumeEvents();
            }
        }

        private async Awaitable Shoot(CaptureContext capture, string name, string scene)
        {
            _game.Render();
            var run = _game.Flow.Run;
            var detail = run == null
                ? scene
                : $"{scene}。経過 {run.Ticks} Tick、撃破 {run.Defeated}/{run.Goal}、体力 {run.Hp}、得点 {run.Score}、誤字 {run.Misses}、狙い {(run.Target == null ? "なし" : $"{run.Target.Word.Display}（{run.Target.Matcher.Typed}）")}";
            await capture.ShootAsync(name, detail);
        }
    }
}
