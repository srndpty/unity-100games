using System;
using System.Text;
using Game.App;
using Game.Core;
using Game.Unity.Evidence;
using UnityEngine;

namespace Game.Unity
{
    /// <summary>
    /// 証跡の台本。実時間と入力を使わず、意図と Tick を順に渡して場面を作って撮る。シードは撮影用の固定値（<see cref="EvidenceCapture.Seed"/>）。
    /// 意図の時刻はすべて 0 にする（時計で Tick を進めず、台本の StepTick だけで進める）。
    /// 1 試合目は隠密の操縦役で見つからずに脱出、2 試合目は強行の操縦役で見つかってから脱出、3 試合目は警備員を気にせず歩いて捕まる。
    /// </summary>
    public sealed class HushWardCaptureScenario : MonoBehaviour, ICaptureScenario
    {
        private const int TickLimit = 60 * 60 * 5;
        private const double ClockTime = 0;

        private readonly StringBuilder _debug = new();
        private HushWardGame _game;

        public async Awaitable RunAsync(CaptureContext capture)
        {
            _game = GetComponent<HushWardGame>();
            await capture.WaitUntilAsync(() => _game.HudRoot != null, 10);
            capture.Record("settings", JsonUtility.ToJson(_game.Settings));

            _game.Render();
            await capture.ShootAsync("01-title", "タイトル（施設の配置を Stage Prefab のまま描く。試合はまだない）");

            Apply(Intent.Of(IntentKind.Submit, ClockTime));
            Step(1);
            await Shoot(capture, "02-start", "試合の開始直後。警備員の視界（扇）と入口");

            _game.Flow.Pilot = new StealthPilot();
            StepUntil(() => InRows(8, 10), "廊下");
            await Shoot(capture, "03-hall", "隠密：A が離れた隙に中央の廊下へ出て、西の部屋の扉へ向かう");

            StepUntil(() => InRows(12, 16) && Column >= 11 && Column <= 19, "北の中央の部屋");
            Step(30);
            await Shoot(capture, "04-north", "隠密：北の中央の部屋の上の列を、C の見回りの外で抜ける");

            StepUntil(() => Happened(FlowEvents.ObjectiveTaken), "目的物");
            Step(2);
            await Shoot(capture, "05-objective", "隠密：資料室の上の列から機密ファイルを取った（見張り B は扉のほうを見ている）");

            Apply(Intent.Of(IntentKind.ToggleDebug, ClockTime));
            Step(20);
            await Shoot(capture, "06-debug", "F1 の詳しい表示：状態・気づき値・向かう先（十字）・巡回路（線）");
            Apply(Intent.Of(IntentKind.ToggleDebug, ClockTime));

            StepUntil(() => _game.Flow.Screen == AppScreen.Escaped, "脱出（未発見）");
            await Shoot(capture, "07-escaped-unseen", "誰にも気づかれずに脱出した結果（評価：未発見）");

            Apply(Intent.Of(IntentKind.Restart, ClockTime));
            _game.Flow.Pilot = new BoldPilot();
            StepUntil(() => Happened(FlowEvents.Suspicious), "調査");
            Step(8);
            await Shoot(capture, "08-suspicious", "強行：警備員に見られ、気づき値が上がって調査に来る（?）");

            StepUntil(() => Happened(FlowEvents.Discovered), "発見");
            Step(4);
            await Shoot(capture, "09-discovered", "強行：気づき値が満ちて発見（!）。施設は厳戒に上がる");

            StepUntil(() => Happened(FlowEvents.LostTrack), "見失う");
            Step(30);
            await Shoot(capture, "10-search", "強行：視界から逃れ、警備員は見失った位置の周りを捜索する（…）");

            StepUntil(() => _game.Flow.Screen == AppScreen.Escaped, "脱出（発見あり）");
            await Shoot(capture, "11-escaped-found", "見つかってから逃げ切って脱出した結果（発見回数）");

            Apply(Intent.Of(IntentKind.Restart, ClockTime));
            _game.Flow.Pilot = new CarelessPilot();
            StepUntil(() => _game.Flow.Screen == AppScreen.Caught, "捕獲");
            await Shoot(capture, "12-caught", "警備員を気にせず歩いて、追跡中の警備員に捕まった");
        }

        private Infiltration M => _game.Flow.Match ?? throw new InvalidOperationException("試合がありません。");

        private int Column => Cell.Of(M.Intruder.Center).Column;

        private bool InRows(int from, int to)
        {
            var row = Cell.Of(M.Intruder.Center).Row;
            return row >= from && row <= to;
        }

        private bool Happened(FlowEvents events) => (_game.LastEvents & events) != 0;

        /// <summary>警備員を気にせず、歩数が最短の経路を歩いて目的物へ向かう操縦役（捕まる場面を作る）。</summary>
        private sealed class CarelessPilot : IPilot
        {
            private Steering _steering;

            public IntruderInput InputFor(Infiltration match)
            {
                _steering ??= new Steering(match.Map);
                return _steering.Toward(match, Steering.GoalOf(match), Gait.Walk);
            }
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
                if (_game.Flow.Match != null && done()) { return; }
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
            var m = _game.Flow.Match;
            var detail = m == null
                ? scene
                : $"{scene}。経過 {m.Ticks} Tick、自機 {m.Intruder.Center}、目的物 {(m.HasObjective ? "あり" : "なし")}、発見 {m.Discoveries}、{HushWardGame.DebugText(m, _debug).Replace("\n", " / ")}";
            await capture.ShootAsync(name, detail);
        }
    }
}
