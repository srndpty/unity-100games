using System;
using System.Globalization;
using Game.App;
using Game.Core;
using Game.Unity.Evidence;
using IntGeometry;
using UnityEngine;

namespace Game.Unity
{
    /// <summary>
    /// 証跡の台本。実時間と入力を使わず、意図と Tick を順に渡して場面を作って撮る。意図の時刻はすべて 0 にする（時計で Tick を進めない）。
    /// 1 番ホールの 1 打目は木に当たる打ち方、2 打目は池か OB に入る打ち方を、実際と同じ関数で探して打つ（場面を確実に作る）。
    /// 以後は操縦役（<see cref="Autopilot"/>）が回る。ラウンドのシードは <see cref="EvidenceCapture.Seed"/>、操縦役のシードは設定の <see cref="BreezewaySettings.CapturePilotSeed"/>。
    /// </summary>
    public sealed class BreezewayCaptureScenario : MonoBehaviour, ICaptureScenario
    {
        private const int TickLimit = 60 * 60 * 20;
        private const double ClockTime = 0;

        private BreezewayGame _game;

        public async Awaitable RunAsync(CaptureContext capture)
        {
            _game = GetComponent<BreezewayGame>();
            await capture.WaitUntilAsync(() => _game.HudRoot != null, 10);
            capture.Record("settings", JsonUtility.ToJson(_game.Settings.Rules));
            capture.Record("pilotSeed", _game.Settings.CapturePilotSeed.ToString(CultureInfo.InvariantCulture));

            _game.Render();
            await capture.ShootAsync("01-title", "タイトル（遊び方の説明と操作）");

            Key(IntentKind.Submit);
            await Shoot(capture, "02-aim", "1 番ホールのティー：向きはカップへ、届くクラブを自動で選ぶ。白い点は予測線（パワー 100%・ずれ 0）、輪は着地の予測");

            // 1 打目：木に当たる打ち方。
            var tree = Find(start => (Simulate(start) & BallEvents.TreeHit) != 0, "木に当たる打ち方");
            Prepare(tree);
            Key(IntentKind.Swing);
            StepUntil(() => Round.Meter!.T == tree.PowerTick, "パワーの Tick");
            Key(IntentKind.Swing);
            Step(12);
            await Shoot(capture, "03-meter", $"メーター：{tree.PowerTick} Tick 目でパワーを決め（赤い線）、カーソルが左端の青いインパクト帯へ戻る。予測線は決めたパワーで描き直す");
            StepUntil(() => Round.Meter!.T == Round.Meter.IdealTick, "インパクトの Tick");
            Key(IntentKind.Swing);
            Step(40);
            await Shoot(capture, "04-flight", "飛んでいる球：影から上へずらして高さを描く。白い点が予測線、橙の点が実際の軌跡（ずれ 0 なので重なる）");
            StepUntil(() => (_game.LastRoundEvents & RoundEvents.TreeHit) != 0, "木に当たる");
            Step(3);
            await Shoot(capture, "05-tree", "木に当たった Tick：手前で跳ね返り、その Tick の残りを新しい速度で計算し直す（予測線も同じ計算）");
            StepUntil(() => Round.Phase == RoundPhase.Aiming, "次の構え");

            // 2 打目：池に入る打ち方。
            var hazard = Find(start => Finish(start) is { Phase: BallPhase.Out, OutSurface: Surface.Water }, "池に入る打ち方");
            Prepare(hazard);
            Swing(hazard.PowerTick, 0);
            StepUntil(() => (_game.LastRoundEvents & RoundEvents.Penalty) != 0, "池・OB");
            Step(20);
            await Shoot(capture, "06-penalty", $"{(Round.LastOutcome == ShotOutcome.Water ? "池" : "OB")}に入った：1 打罰を加えて前の位置から打ち直す（打数 {Round.Strokes}）");

            var pilot = new Autopilot();
            _game.Flow.Pilot = pilot;
            pilot.Reset(_game.Settings.CapturePilotSeed);

            StepUntil(() => Round.Phase == RoundPhase.Aiming && Round.LieSurface == Surface.Green && Round.HoleIndex >= 1, "グリーンで構える");
            await Shoot(capture, "07-green", "グリーンでは寄った表示：矢印は傾き（低いほうへ）。パターの予測線は最初の 40 Tick だけ");

            StepUntil(() => Round.Phase == RoundPhase.Moving && Round.LastSetup?.Club == Clubs.Putter && Round.Ball.Ticks == 50, "パットの途中");
            await Shoot(capture, "08-putt", "パットの途中：橙の点が実際の転がり（傾きで曲がる）");

            StepUntil(() => (_game.LastRoundEvents & RoundEvents.Holed) != 0, "カップイン");
            Step(20);
            await Shoot(capture, "09-holeout", "カップイン：球の速さが上限以下で円に入った。スコアカードを出して次のホールへ");

            StepUntil(() => Round.Phase == RoundPhase.Aiming && Round.HoleIndex == 2 && Round.Strokes == 0, "3 番ホールのティー");
            Key(IntentKind.Cancel);
            await Shoot(capture, "10-paused", "Esc で一時停止（振っている途中なら取り消す）");
            Key(IntentKind.Cancel);

            StepUntil(() => _game.Flow.Screen == AppScreen.Result, "ラウンドの終わり");
            await Shoot(capture, "11-result", "3 ホールの結果：スコアカード・合計とパー差・起動中の最少打数");
        }

        private GolfRound Round => _game.Flow.Round ?? throw new InvalidOperationException("ラウンドがありません。");

        /// <summary>撮影で打つ打ち方（ずれ 0）。</summary>
        private readonly struct Shot
        {
            public Shot(int club, int aim, int powerTick)
            {
                Club = club;
                Aim = aim;
                PowerTick = powerTick;
            }

            public int Club { get; }

            public int Aim { get; }

            public int PowerTick { get; }
        }

        /// <summary>今の構えから、向きがカップに近い順に、条件を満たす打ち方を探す（実際と同じ関数で打ってみる）。</summary>
        private Shot Find(Func<BallState, bool> wanted, string scene)
        {
            var round = Round;
            var config = round.Config;
            var meter = new Meter(config);
            for (var offset = 0; offset <= 80; offset++)
            {
                foreach (var sign in new[] { 1, -1 })
                {
                    for (var club = Clubs.Wood; club <= Clubs.Wedge; club++)
                    {
                        for (var tick = config.MeterHalfTicks; tick >= config.MeterHalfTicks / 2; tick--)
                        {
                            var aim = Trig.Normalize(round.Aim + (sign * offset * config.AimStep));
                            var setup = new ShotSetup(club, aim, 0, 0, meter.GaugeAt(tick), 0, false);
                            if (wanted(Launch.From(config, setup, round.Lie, round.LieSurface))) { return new Shot(club, aim, tick); }
                        }
                    }
                }
            }

            throw new InvalidOperationException("撮影の打ち方が見つかりません：" + scene);
        }

        private BallEvents Simulate(BallState start)
        {
            var round = Round;
            var config = round.Config;
            var ball = start;
            var events = BallEvents.None;
            while (ball.IsMoving) { events |= BallSim.Step(ref ball, round.Hole, round.Wind, config); }
            return events;
        }

        private BallState Finish(BallState start) => Predictor.Finish(start, Round.Hole, Round.Wind, Round.Config);

        /// <summary>クラブと向きを決める（操縦役と同じ操作）。</summary>
        private void Prepare(Shot shot)
        {
            Round.SelectClub(shot.Club);
            Round.SetAim(shot.Aim);
            _game.Render();
        }

        /// <summary>スイングのキーでメーターを進め、パワーの Tick と理想 + ずれの Tick で押す。</summary>
        private void Swing(int powerTick, int error)
        {
            Key(IntentKind.Swing);
            StepUntil(() => Round.Meter!.T == powerTick, "パワーの Tick");
            Key(IntentKind.Swing);
            StepUntil(() => Round.Meter!.T == Round.Meter.IdealTick + error, "インパクトの Tick");
            Key(IntentKind.Swing);
        }

        private void Key(IntentKind kind)
        {
            _game.Apply(new Intent(kind, ClockTime));
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

        /// <summary><paramref name="done"/> が成り立つ Tick まで進める（1 Tick ごとに出来事を取り出して調べる）。</summary>
        private void StepUntil(Func<bool> done, string scene)
        {
            if (done()) { return; }
            for (var i = 0; ; i++)
            {
                if (i > TickLimit || _game.Flow.Screen != AppScreen.Playing)
                {
                    if (done()) { return; }
                    throw new InvalidOperationException("撮影場面を作れませんでした: " + scene);
                }

                _game.Flow.StepTick();
                _game.ConsumeEvents();
                if (done()) { return; }
            }
        }

        private async Awaitable Shoot(CaptureContext capture, string name, string scene)
        {
            _game.Render();
            var round = _game.Flow.Round;
            var detail = round == null
                ? scene
                : $"{scene}。{round.Hole.Number} 番ホール、打数 {round.Strokes}、合計 {round.Total}、経過 {round.Ticks} Tick、ハッシュ 0x{round.Hash():X8}";
            await capture.ShootAsync(name, detail);
        }
    }
}
