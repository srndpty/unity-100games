using System;
using Game.App;
using Game.Core;
using Game.Unity.Evidence;
using UnityEngine;

namespace Game.Unity
{
    /// <summary>証跡の台本。実時間とプレイヤー入力では進めず、意図・操縦役・Tick だけで場面を作る。</summary>
    public sealed class SootCaptureScenario : MonoBehaviour, ICaptureScenario
    {
        private const int Limit = 60000;

        private SootGame _game;

        public async Awaitable RunAsync(CaptureContext capture)
        {
            _game = GetComponent<SootGame>();
            await capture.WaitUntilAsync(() => _game.HudRoot != null, 10);
            capture.Record("volume", _game.Settings.Volume.ToString());
            capture.Record("seed", EvidenceCapture.Seed.ToString());
            _game.Render();
            await capture.ShootAsync("01-title", "タイトル");

            Apply(IntentKind.Submit);
            _game.Render();
            await capture.ShootAsync("02-howto", "操作説明");

            Apply(IntentKind.Submit);
            Flow.Pilot = new Autopilot(EvidenceCapture.Seed);
            Flow.Pilot.Reset(Flow.MatchSeed);
            StepUntil(() => Crowd(90) >= 3, "乱戦");
            await capture.ShootAsync("03-melee", "乱戦  Tick " + Brawl.Tick);

            StepUntil(() => Brawl.Player.Move != null && Brawl.Player.Move.Id == "jab3" && Brawl.Player.HitStop > 0, "コンボの 3 段目");
            await capture.ShootAsync("04-combo", "コンボの 3 段目（アッパー）  Tick " + Brawl.Tick);

            StepUntil(() => Brawl.Player.State == ActorState.Grab && Brawl.Player.Move == null, "掴み");
            await capture.ShootAsync("05-grab", "掴み  Tick " + Brawl.Tick);

            StepUntil(() => !Brawl.Locked, "GO");
            StepTicks(10);
            StepUntil(() => SootGame.GoVisible(Brawl), "GO の表示");
            await capture.ShootAsync("06-go", "区画を片付けて GO →  Tick " + Brawl.Tick);

            StepUntil(() => Brawl.ItemsOnGround > 0, "回復アイテム");
            await capture.ShootAsync("07-item", "倒した敵が落とした回復アイテム  Tick " + Brawl.Tick);

            StepUntil(() => (Brawl.Player.State == ActorState.Idle || Brawl.Player.State == ActorState.Walk) && Brawl.Player.HitStop == 0 && Crowd(60) >= 2 && Brawl.Player.Health > 160, "囲まれた場面");
            Flow.Pilot = null;
            Apply(IntentKind.SpecialDown);
            StepTicks(1);
            Apply(IntentKind.SpecialUp);
            StepUntil(() => Brawl.Player.Move != null && Brawl.Player.Move.Kind == MoveKind.Special && Brawl.Player.Phase == MovePhase.Active, "緊急技");
            await capture.ShootAsync("08-special", "緊急技  Tick " + Brawl.Tick);

            Flow.Pilot = new Autopilot(EvidenceCapture.Seed + 1);
            StepUntil(() => Brawl.Locked && Brawl.Player.State == ActorState.Idle && Brawl.Player.HitStop == 0 && HurtableFoe() != null, "殴れる敵のいる場面");
            Flow.Pilot = null;
            Brawl.AiEnabled = false;
            var foe = HurtableFoe();
            var p = Brawl.Player;
            var config = _game.Rules;
            Brawl.Place(foe, p.X + (p.Facing * 18 * Px.One), Math.Min(config.ZMax, p.Z + (18 * Px.One)), -p.Facing);
            Apply(IntentKind.Overlay);
            Apply(IntentKind.AttackDown);
            StepTicks(1);
            Apply(IntentKind.AttackUp);
            StepUntil(() => p.Move != null && p.Move.Id == "jab1" && p.Phase == MovePhase.Active, "奥行きのずれた空振り");
            await capture.ShootAsync("09-depth", "判定箱：横と高さは重なるが奥行きがずれて当たらない  Tick " + Brawl.Tick);

            Apply(IntentKind.Overlay);
            Brawl.AiEnabled = true;
            Flow.Pilot = new Autopilot(EvidenceCapture.Seed + 2);
            StepUntil(() => BossOnScreen(), "ボス");
            StepTicks(90);
            await capture.ShootAsync("10-boss", "ボス戦  Tick " + Brawl.Tick);

            StepUntil(() => Flow.Screen == BrawlScreen.Result, "クリア");
            _game.Render();
            await capture.ShootAsync("11-clear", "面クリア  " + Brawl.Outcome + "  Tick " + Brawl.Tick);

            Flow.Pilot = null;
            Apply(IntentKind.Submit);
            StepUntil(() => Flow.Screen == BrawlScreen.Result, "敗北");
            _game.Render();
            await capture.ShootAsync("12-gameover", "入力なしで敗北  " + Brawl.Outcome + "  Tick " + Brawl.Tick);
        }

        private BrawlFlow Flow => _game.Flow;

        private Brawl Brawl => _game.Flow.Brawl;

        private int Crowd(int px)
        {
            var p = Brawl.Player;
            var count = 0;
            for (var i = 1; i < Brawl.SlotCount; i++)
            {
                var e = Brawl.At(i);
                if (e.Active && !e.Defeated && Math.Abs(e.X - p.X) <= px * Px.One) { count++; }
            }

            return count;
        }

        /// <summary>画面の中で一番近い、立っていて殴れる敵（いなければ null）。</summary>
        private Actor HurtableFoe()
        {
            Actor best = null;
            for (var i = 1; i < Brawl.SlotCount; i++)
            {
                var e = Brawl.At(i);
                if (!e.Active || !Brawl.Hurtable(e) || e.State == ActorState.Attack) { continue; }
                if (e.X < Brawl.CameraX || e.X > Brawl.CameraX + _game.Rules.ViewWidth) { continue; }
                if (best == null || Math.Abs(e.X - Brawl.Player.X) < Math.Abs(best.X - Brawl.Player.X)) { best = e; }
            }

            return best;
        }

        private bool BossOnScreen()
        {
            for (var i = 1; i < Brawl.SlotCount; i++)
            {
                var e = Brawl.At(i);
                if (e.Active && e.Kind == ActorKind.Boss && e.X > Brawl.CameraX && e.X < Brawl.CameraX + _game.Rules.ViewWidth) { return true; }
            }

            return false;
        }

        private void Apply(IntentKind kind) => Flow.Apply(new Intent(kind, 0));

        private void StepTicks(int ticks)
        {
            for (var i = 0; i < ticks; i++) { Flow.StepTick(); }
            _game.Render();
        }

        private void StepUntil(Func<bool> condition, string label)
        {
            for (var i = 0; i < Limit; i++)
            {
                if (condition()) { _game.Render(); return; }
                if (Flow.Screen != BrawlScreen.Playing) { break; }
                Flow.StepTick();
            }

            if (condition()) { _game.Render(); return; }
            throw new InvalidOperationException(label + " の場面になりませんでした。");
        }
    }
}
