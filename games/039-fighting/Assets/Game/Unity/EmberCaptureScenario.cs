using System;
using Game.App;
using Game.Core;
using Game.Unity.Evidence;
using UnityEngine;

namespace Game.Unity
{
    /// <summary>証跡の台本。実時間とプレイヤー入力では進めず、意図と Tick だけで場面を作る。</summary>
    public sealed class EmberCaptureScenario : MonoBehaviour, ICaptureScenario
    {
        private const int Limit = 40000;

        private EmberGame _game;

        public async Awaitable RunAsync(CaptureContext capture)
        {
            _game = GetComponent<EmberGame>();
            await capture.WaitUntilAsync(() => _game.HudRoot != null, 10);
            capture.Record("volume", _game.Settings.Volume.ToString());
            capture.Record("seed", EvidenceCapture.Seed.ToString());
            _game.Render();
            await capture.ShootAsync("01-title", "タイトル");

            Apply(IntentKind.Submit);
            _game.Render();
            await capture.ShootAsync("02-select", "キャラ選択（シンダー）");

            Apply(IntentKind.Submit);
            Step(_game.Flow.Config.IntroTicks);
            await capture.ShootAsync("03-round", "ラウンド開始  Tick " + Duel.Tick);

            Motion();
            StepUntil(() => Duel.At(0).Move != null && Duel.At(0).Move.Id == "special", "必殺");
            await capture.ShootAsync("04-special", "必殺（火球）  Tick " + Duel.Tick);

            Release();
            Hold(IntentKind.RightDown);
            StepUntil(() => Gap() < 36 * 256, "接近");
            Release();
            Hold(IntentKind.LightDown);
            StepUntil(() => Duel.At(1).Health < Duel.At(1).MaxHealth || Duel.At(0).Health < Duel.At(0).MaxHealth, "ヒット");
            await capture.ShootAsync("05-hit", "通常技のヒット  Tick " + Duel.Tick);

            Release();
            Hold(IntentKind.LeftDown);
            StepUntil(() => Duel.At(0).State == FighterState.BlockStun || Duel.At(1).State == FighterState.BlockStun, "ガード");
            await capture.ShootAsync("06-guard", "ガード  Tick " + Duel.Tick);

            Release();
            Hold(IntentKind.RightDown);
            StepUntil(() => Gap() <= 32 * 256, "投げ間合い");
            Release();
            Hold(IntentKind.LightDown);
            Hold(IntentKind.HeavyDown);
            StepUntil(() => Duel.At(0).State == FighterState.Thrown || Duel.At(1).State == FighterState.Thrown || (Duel.At(0).Move != null && Duel.At(0).Move.Id == "throw"), "投げ");
            await capture.ShootAsync("07-throw", "投げ  Tick " + Duel.Tick);

            Release();
            Apply(IntentKind.Overlay);
            _game.Render();
            await capture.ShootAsync("08-boxes", "判定箱  Tick " + Duel.Tick);

            StepUntil(() => Duel.At(0).Health == 0 || Duel.At(1).Health == 0, "KO");
            await capture.ShootAsync("09-ko", "KO  Tick " + Duel.Tick);

            StepUntil(() => _game.Flow.Screen == DuelScreen.Result, "結果");
            await capture.ShootAsync("10-result", "結果 " + _game.Flow.Match.Result);
        }

        private Duel Duel => _game.Flow.Match.Duel;

        private int Gap() => Math.Abs(Duel.At(0).X - Duel.At(1).X);

        private void Motion()
        {
            Release();
            Hold(IntentKind.DownDown);
            Step(1);
            Release();
            Hold(IntentKind.DownDown);
            Hold(IntentKind.RightDown);
            Step(1);
            Release();
            Hold(IntentKind.RightDown);
            Hold(IntentKind.LightDown);
            Step(1);
            Release();
        }

        private void Hold(IntentKind kind) => _game.Flow.Apply(new Intent(kind, 0));

        private void Release()
        {
            _game.Flow.Apply(new Intent(IntentKind.LeftUp, 0));
            _game.Flow.Apply(new Intent(IntentKind.RightUp, 0));
            _game.Flow.Apply(new Intent(IntentKind.DownUp, 0));
            _game.Flow.Apply(new Intent(IntentKind.UpUp, 0));
            _game.Flow.Apply(new Intent(IntentKind.LightUp, 0));
            _game.Flow.Apply(new Intent(IntentKind.HeavyUp, 0));
            _game.Flow.Apply(new Intent(IntentKind.SweepUp, 0));
        }

        private void Apply(IntentKind kind) => _game.Flow.Apply(new Intent(kind, 0));

        private void Step(int ticks)
        {
            for (var i = 0; i < ticks; i++) { _game.Flow.StepTick(); }
        }

        private void StepUntil(Func<bool> condition, string label)
        {
            for (var i = 0; i < Limit && _game.Flow.Screen != DuelScreen.Result; i++)
            {
                if (condition()) { return; }
                _game.Flow.StepTick();
            }

            if (condition()) { return; }
            throw new InvalidOperationException(label + " の場面になりませんでした。");
        }
    }
}
