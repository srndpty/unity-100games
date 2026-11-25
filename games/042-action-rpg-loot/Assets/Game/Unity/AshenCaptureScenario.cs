using System;
using Game.App;
using Game.Core;
using Game.Unity.Evidence;
using IntGeometry;
using UnityEngine;

namespace Game.Unity
{
    /// <summary>
    /// 証跡の台本。実時間と入力を使わず、意図と Tick を順に渡して場面を作って撮る。シードは撮影用の固定値（<see cref="EvidenceCapture.Seed"/>）。
    /// 意図の時刻はすべて 0 にする（時計で Tick を進めず、台本の StepTick だけで進める）。
    /// </summary>
    public sealed class AshenCaptureScenario : MonoBehaviour, ICaptureScenario
    {
        private const double ClockTime = 0;

        /// <summary>1 つの場面を待つ上限（Tick）。</summary>
        private const int SceneLimit = 60 * 60 * 6;

        private AshenGame _game;

        private DelveRun R => _game.Flow.Run ?? throw new InvalidOperationException("試合がありません。");

        public async Awaitable RunAsync(CaptureContext capture)
        {
            _game = GetComponent<AshenGame>();
            await capture.WaitUntilAsync(() => _game.HudRoot != null, 10);
            capture.Record("settings", JsonUtility.ToJson(_game.Settings));

            _game.Render();
            await Shoot(capture, "01-title", "タイトル");
            Apply(Intent.Of(IntentKind.Submit, ClockTime));
            await Shoot(capture, "02-howto", "操作説明（操作・希少度の色・倒れたときの規則）");

            Apply(Intent.Of(IntentKind.Submit, ClockTime));
            _game.Flow.Pilot = new Autopilot();
            StepUntil(() => Swinging() && NearFoe(5 * 256), "最初の戦い");
            await Shoot(capture, "03-fight", "自動操縦が灰鬼へ基本攻撃を振る（白い弧が当たりの円）");

            StepUntil(() => GroundWith(Rarity.Magic) || GroundWith(Rarity.Rare), "魔法以上のドロップ");
            await Shoot(capture, "04-drop", "倒した敵が装備を落とした。地面の品の上に名前札（色は希少度）");

            StepUntil(() => R.Inventory.Count >= 1 && !_game.Flow.Run.Hero.Dead, "持ち物に品");
            Apply(Intent.Of(IntentKind.ToggleInventory, ClockTime));
            _game.Hud.Select(new AshenHud.Selection(false, 0));
            await Shoot(capture, "05-inventory", "持ち物の画面。選んだ品の効果（基礎と Affix の行）と、装備したときの能力値の変化");

            _game.Hud.Select(new AshenHud.Selection(true, (int)Slot.Weapon));
            await Shoot(capture, "06-equipped", "装備中の武器を選んだところ。左の欄は今の能力値（装備一式から計算し直した値）");
            Apply(Intent.Of(IntentKind.ToggleInventory, ClockTime));

            StepUntil(() => R.FloorNumber == 2, "2 階");
            Step(20);
            await Shoot(capture, "07-floor2", "階段を降りて地下 2 階（沈んだ書庫）");

            StepUntil(() => ChargerWindup(), "角獣の構え");
            await Shoot(capture, "08-charger", "角獣が突進の構え（黄色）。橙の帯が突進の通り道");

            StepUntil(() => R.FloorNumber == 3, "3 階");
            Step(20);
            await Shoot(capture, "09-floor3", "地下 3 階（番人の聖所）");

            StepUntil(() => BossFight(), "番人");
            await Shoot(capture, "10-boss", "灰の番人との戦い。HP が半分を切ると激昂し、火の扇と突進が増える");

            StepUntil(() => R.Status == RunStatus.Won, "番人の撃破");
            _game.Flow.Pilot = new IdlePilot();
            Step(10);
            await Shoot(capture, "11-boss-drop", "番人を倒した。希少（金）の装備を 2 つ落とす");
            _game.Flow.Pilot = new Autopilot();
            StepUntil(() => _game.Flow.Screen == AppScreen.Result, "結果");
            await Shoot(capture, "12-result", "結果（かかった時間・撃破・倒れた回数・拾った装備・最も希少な品）");

            // 新しい試合（シード 2）で、攻撃せずに敵へ向かい続けて倒れる場面を作る。
            Apply(Intent.Of(IntentKind.Submit, ClockTime));
            Apply(Intent.Of(IntentKind.Submit, ClockTime));
            Apply(Intent.Of(IntentKind.Submit, ClockTime));
            _game.Flow.Pilot = new RecklessPilot();
            StepUntil(() => R.Hero.Dead, "倒れる");
            Step(20);
            await Shoot(capture, "13-down", "攻撃せずに敵へ向かい続けて HP 0。倒れて入口での復活を待つ");
            StepUntil(() => !R.Hero.Dead, "復活");
            Step(2);
            await Shoot(capture, "14-respawn", "その階の入口で HP 全快で復活。生きている敵は元の位置へ戻った");

            Apply(Intent.Of(IntentKind.Back, ClockTime));
            await Shoot(capture, "15-pause", "一時停止");
        }

        private bool Swinging()
        {
            var h = R.Hero;
            return h.SwingStartTick >= 0 && R.Tick >= h.SwingStartTick + 3 && R.Tick <= h.SwingHitTick + 2;
        }

        private bool NearFoe(int range)
        {
            foreach (var f in R.Foes)
            {
                if (!f.Dead && Vec2i.DistanceSquared(f.Position, R.Hero.Position) < (long)range * range) { return true; }
            }

            return false;
        }

        private bool GroundWith(Rarity rarity)
        {
            foreach (var g in R.Ground)
            {
                if (!g.IsPotion && g.Item.Rarity == rarity && Vec2i.DistanceSquared(g.Position, R.Hero.Position) < 5L * 256 * 5 * 256) { return true; }
            }

            return false;
        }

        private bool ChargerWindup()
        {
            foreach (var f in R.Foes)
            {
                if (!f.Dead && f.Kind == FoeKind.Charger && f.State == FoeState.ChargeWindup && f.Timer < 20
                    && Vec2i.DistanceSquared(f.Position, R.Hero.Position) < 7L * 256 * 7 * 256)
                {
                    return true;
                }
            }

            return false;
        }

        private bool BossFight()
        {
            foreach (var f in R.Foes)
            {
                if (f.Kind == FoeKind.Boss && !f.Dead && f.Enraged && R.Projectiles.Count >= 3) { return true; }
            }

            return false;
        }

        private void Apply(Intent intent)
        {
            _game.Apply(intent);
            _game.ConsumeEvents();
        }

        private void Step(int ticks)
        {
            for (int i = 0; i < ticks && _game.Flow.Screen == AppScreen.Playing; i++)
            {
                _game.Flow.StepTick();
                _game.ConsumeEvents();
            }
        }

        private void StepUntil(Func<bool> done, string scene)
        {
            for (int i = 0; i < SceneLimit; i++)
            {
                if (_game.Flow.Run != null && done()) { return; }
                if (_game.Flow.Screen != AppScreen.Playing) { break; }
                _game.Flow.StepTick();
                _game.ConsumeEvents();
            }

            if (_game.Flow.Run != null && done()) { return; }
            throw new InvalidOperationException("撮影場面を作れませんでした: " + scene);
        }

        private async Awaitable Shoot(CaptureContext capture, string name, string scene)
        {
            _game.Render();
            var run = _game.Flow.Run;
            var detail = run == null
                ? scene
                : $"{scene}。{run.FloorNumber} 階、Tick {run.Tick}、HP {run.Hero.Life}/{run.Stats.Life}、撃破 {run.Record.Kills}、持ち物 {run.Inventory.Count}";
            await capture.ShootAsync(name, detail);
        }

        /// <summary>何もしない操縦役（落とした品を拾わずに見せる）。</summary>
        private sealed class IdlePilot : IPilot
        {
            public HeroCommand CommandFor(DelveRun run) => HeroCommand.Idle(run.Hero.Position + new Vec2i(256, 0));
        }

        /// <summary>自動操縦の経路で敵へ向かうが、攻撃も回復薬も使わない操縦役（倒れる場面を作る）。</summary>
        private sealed class RecklessPilot : IPilot
        {
            private readonly Autopilot _legs = new();

            public HeroCommand CommandFor(DelveRun run)
            {
                var command = _legs.CommandFor(run);
                return new HeroCommand(command.MoveX, command.MoveY, command.Aim, false);
            }
        }
    }
}
