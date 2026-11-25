using System;
using System.Collections;
using Game.App;
using Game.Core;
using NUnit.Framework;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace Game.Unity.Tests
{
    public sealed class SootSceneTests
    {
        private const long BackgroundBytes = 64;

        private TestKeyboard _keyboard;
        private SootGame _game;

        private Brawl Brawl => _game.Flow.Brawl;

        [UnitySetUp]
        public IEnumerator Load()
        {
            _keyboard = new TestKeyboard();
            yield return SceneManager.LoadSceneAsync("SootAlley");
            yield return null;
            _game = Object.FindAnyObjectByType<SootGame>();
            for (var i = 0; i < 30 && _game.HudRoot == null; i++) { yield return null; }
            Assert.That(_game.HudRoot, Is.Not.Null);
            Time.timeScale = 0f;
        }

        [UnityTearDown]
        public IEnumerator Unload()
        {
            Time.timeScale = 1f;
            _keyboard.Dispose();
            yield return null;
        }

        [UnityTest]
        public IEnumerator KeysDriveComboJumpKickSpecialAndGrab()
        {
            yield return StartMatch();
            Brawl.AiEnabled = false;
            var p = Brawl.Player;

            yield return Press(Key.J);
            Assert.That(p.Move?.Id, Is.EqualTo("jab1"), "J で攻撃");
            yield return Idle(30);

            yield return Press(Key.K);
            Assert.That(p.State, Is.EqualTo(ActorState.Jump), "K でジャンプ");
            yield return Press(Key.J);
            Assert.That(p.Move?.Id, Is.EqualTo("jumpkick"), "空中で J は跳び蹴り");
            yield return Idle(60);
            Assert.That(p.State, Is.EqualTo(ActorState.Idle));

            var health = p.Health;
            yield return Press(Key.L);
            Assert.That(p.Move?.Id, Is.EqualTo("special"), "L で緊急技");
            Assert.That(p.Health, Is.LessThan(health), "体力を払う");
            yield return Idle(40);

            var foe = Brawl.Spawn(ActorKind.Grunt, p.X + (40 * Px.One), p.Z, -1);
            var grabbed = false;
            for (var i = 0; i < 60 && !grabbed; i++)
            {
                _keyboard.Hold(Key.D);
                yield return null;
                _game.Flow.StepTick();
                grabbed = p.State == ActorState.Grab;
            }

            Assert.That(grabbed, Is.True, "敵へ歩いて掴む");
            Assert.That(foe.State, Is.EqualTo(ActorState.Grabbed));
            _keyboard.Hold();
            yield return null;
            for (var k = 0; k < 3; k++)
            {
                yield return Press(Key.J);
                yield return Idle(20);
            }

            Assert.That(foe.State, Is.Not.EqualTo(ActorState.Grabbed), "膝 2 回の後に投げる");
            Assert.That(foe.Health, Is.LessThan(foe.Def.Health));
        }

        [UnityTest]
        public IEnumerator DrawOrderAndPositionsFollowTheCore()
        {
            yield return StartMatch();
            Brawl.AiEnabled = false;
            var p = Brawl.Player;
            var foe = Brawl.Spawn(ActorKind.Brute, p.X + (30 * Px.One), p.Z + (3 * Px.One), -1);
            _game.Render();
            var config = _game.Rules;
            var stage = _game.Stage;
            Assert.That(stage.Body(0).transform.localPosition, Is.EqualTo(SootStage.WorldOf(p.X, p.Y, p.Z, config)));
            Assert.That(stage.ShadowOf(foe.Slot).transform.localPosition, Is.EqualTo(SootStage.WorldOf(foe.X, 0, foe.Z, config)));
            Assert.That(stage.Body(foe.Slot).sortingOrder, Is.LessThan(stage.Body(0).sortingOrder), "奥の敵を先に描く");
            Assert.That(stage.Body(foe.Slot).flipX, Is.True, "左向き");

            var swapped = false;
            for (var i = 0; i < 10 && !swapped; i++)
            {
                _keyboard.Hold(Key.W);
                yield return null;
                _game.Flow.StepTick();
                _game.Render();
                var front = p.Z < foe.Z;
                Assert.That(stage.Body(0).sortingOrder > stage.Body(foe.Slot).sortingOrder, Is.EqualTo(front), $"z {p.Z} / {foe.Z}");
                swapped = !front;
            }

            Assert.That(swapped, Is.True, "奥へ回り込むと順が入れ替わる");
            _keyboard.Hold(Key.K);
            yield return null;
            _game.Flow.StepTick();
            _keyboard.Hold();
            for (var i = 0; i < 8; i++)
            {
                yield return null;
                _game.Flow.StepTick();
            }

            _game.Render();
            Assert.That(p.Y, Is.GreaterThan(0));
            Assert.That(stage.Body(0).sortingOrder, Is.LessThan(stage.Body(foe.Slot).sortingOrder), "跳んでも奥のまま");
            Assert.That(stage.Body(0).transform.localPosition.y, Is.EqualTo(SootStage.WorldOf(p.X, p.Y, p.Z, config).y));
            Assert.That(stage.ShadowOf(0).transform.localPosition.y, Is.EqualTo(SootStage.WorldOf(p.X, 0, p.Z, config).y), "影は地面に残る");
        }

        [UnityTest]
        public IEnumerator OverlayDrawsBoxesAndTheirDepthBand()
        {
            yield return StartMatch();
            Brawl.AiEnabled = false;
            var p = Brawl.Player;
            Brawl.Place(p, Brawl.PlayerMaxX - (40 * Px.One), p.Z, 1);
            Brawl.Spawn(ActorKind.Grunt, p.X + (18 * Px.One), p.Z + (20 * Px.One), -1);
            yield return Press(Key.F1);
            Assert.That(_game.Flow.ShowBoxes, Is.True);
            yield return Press(Key.J);
            for (var i = 0; i < 4; i++) { _game.Flow.StepTick(); }
            _game.Render();
            Assert.That(p.Phase, Is.EqualTo(MovePhase.Active));
            var vertices = _game.Stage.BoxMesh.vertices;
            var colors = _game.Stage.BoxMesh.colors32;
            var red = 0;
            var green = 0;
            for (var i = 0; i < vertices.Length; i++)
            {
                if (colors[i].a == 0) { continue; }
                if (colors[i].r > 200 && colors[i].g < 100) { red++; }
                if (colors[i].g > 200 && colors[i].r < 100) { green++; }
            }

            Assert.That(red, Is.EqualTo(16), "攻撃箱の正面と奥行きの帯");
            var bounds = _game.Stage.BoxMesh.bounds;
            var at = SootStage.WorldOf(p.X, p.Y, p.Z, _game.Rules);
            Assert.That(bounds.min.x <= at.x && at.x <= bounds.max.x, Is.True, $"Mesh の bounds {bounds} が箱を含む（含まないとカリングされる）");
            Assert.That(green, Is.EqualTo(32), "2 人の喰らい箱");
            Assert.That(Brawl.HitCount, Is.Zero, "奥行きがずれて当たらない");
        }

        [UnityTest]
        public IEnumerator ReturningToTitleRestoresTheBootPicture()
        {
            var stage = _game.Stage;
            var boot = new Vector3[SootStage.Pool];
            var sprites = new Sprite[SootStage.Pool];
            var enabled = new bool[SootStage.Pool];
            for (var i = 0; i < SootStage.Pool; i++)
            {
                boot[i] = stage.Body(i).transform.localPosition;
                sprites[i] = stage.Body(i).sprite;
                enabled[i] = stage.Body(i).enabled;
            }

            var camera = _game.View.transform.position;
            Assert.That(stage.Mismatch(_game.Rules, _game.Flow.Stage), Is.Null);
            yield return StartMatch();
            _game.Flow.Pilot = new Autopilot(3);
            for (var i = 0; i < 2500 && Brawl.SectionIndex == 0; i++) { _game.Flow.StepTick(); }
            _game.Render();
            Assert.That(Brawl.SectionIndex, Is.GreaterThan(0), "先の区画まで進んだ");
            Assert.That(_game.View.transform.position.x, Is.GreaterThan(camera.x));

            yield return Press(Key.Escape);
            Assert.That(_game.Flow.Screen, Is.EqualTo(BrawlScreen.Paused));
            yield return Press(Key.Backspace);
            Assert.That(_game.Flow.Screen, Is.EqualTo(BrawlScreen.Title));
            for (var i = 0; i < SootStage.Pool; i++)
            {
                Assert.That(stage.Body(i).transform.localPosition, Is.EqualTo(boot[i]), "Actor " + i);
                Assert.That(stage.Body(i).sprite, Is.EqualTo(sprites[i]));
                Assert.That(stage.Body(i).enabled, Is.EqualTo(enabled[i]));
            }

            Assert.That(_game.View.transform.position, Is.EqualTo(camera));
        }

        [UnityTest]
        public IEnumerator MismatchCatchesAMovedLampOrTitleActor()
        {
            var stage = _game.Stage;
            Assert.That(stage.Mismatch(_game.Rules, _game.Flow.Stage), Is.Null);
            stage.Lamp(1).transform.localPosition += new Vector3(4f, 0f, 0f);
            Assert.That(stage.Mismatch(_game.Rules, _game.Flow.Stage), Does.Contain("街灯 2"));
            stage.Lamp(1).transform.localPosition -= new Vector3(4f, 0f, 0f);
            stage.Body(2).transform.localPosition += new Vector3(0f, 1f, 0f);
            Assert.That(stage.Mismatch(_game.Rules, _game.Flow.Stage), Does.Contain("Brute"));
            yield return null;
        }

        [UnityTest]
        public IEnumerator PilotClearsTheStageAndTheHudFollows()
        {
            yield return StartMatch();
            _game.Flow.Pilot = new Autopilot(5);
            var hud = _game.HudRoot;
            var sawGo = false;
            var sawBoss = false;
            for (var i = 0; i < 20000 && _game.Flow.Screen == BrawlScreen.Playing; i++)
            {
                _game.Flow.StepTick();
                if (i % 10 != 0) { continue; }
                _game.Render();
                sawGo |= hud.Q<Label>("go").style.display.value == DisplayStyle.Flex;
                sawBoss |= hud.Q("boss").style.display.value == DisplayStyle.Flex;
            }

            _game.Render();
            Assert.That(Brawl.Outcome, Is.EqualTo(Outcome.Cleared));
            Assert.That(sawGo, Is.True, "区画を片付けると GO →");
            Assert.That(sawBoss, Is.True, "ボスの体力を出す");
            Assert.That(hud.Q<Label>("banner").text, Is.EqualTo("STAGE CLEAR"));

            yield return _keyboard.Tap(Key.Enter);
            Assert.That(_game.Flow.Screen, Is.EqualTo(BrawlScreen.Playing), "もう一度");
            Assert.That(Brawl.Tick, Is.Zero);
            _game.Flow.StepTick();
            var tick = Brawl.Tick;
            _game.SetFocus(false);
            Assert.That(_game.Flow.Screen, Is.EqualTo(BrawlScreen.Paused), "フォーカスを失うと一時停止");
            _game.Flow.StepTick();
            Assert.That(Brawl.Tick, Is.EqualTo(tick));
            _game.SetFocus(true);
            Assert.That(_game.Flow.Screen, Is.EqualTo(BrawlScreen.Paused), "復帰は手動");
        }

        [UnityTest]
        public IEnumerator CrowdedStepAndDrawStayCheap()
        {
            yield return StartMatch();
            var p = Brawl.Player;
            for (var i = Brawl.ActiveEnemies; i < _game.Rules.Capacity; i++)
            {
                var kind = (ActorKind)(1 + (i % 3));
                Brawl.Spawn(kind, p.X + ((i % 2 == 0 ? 1 : -1) * (40 + (i * 10)) * Px.One), (i * 11 % 90) * Px.One, -1);
            }

            Assert.That(Brawl.ActiveEnemies, Is.EqualTo(_game.Rules.Capacity), "置き場を全て使う");
            _game.Flow.Pilot = new Autopilot(2);
            const int Samples = 240;
            var step = new double[Samples];
            var render = new double[Samples];
            var config = _game.Rules;
            var watch = new System.Diagnostics.Stopwatch();
            for (var i = 0; i < Samples; i++)
            {
                watch.Restart();
                _game.Flow.StepTick();
                step[i] = watch.Elapsed.TotalMilliseconds;
                watch.Restart();
                _game.Stage.Show(Brawl, config, false);
                render[i] = watch.Elapsed.TotalMilliseconds;
            }

            const int Frames = 30;
            var renderFrames = new long[Frames];
            var idleFrames = new long[Frames];
            using var recorder = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame");
            for (var i = 0; i < 8; i++) { yield return null; }
            var baseline = recorder.LastValue;
            for (var i = 0; i < Frames; i++)
            {
                _game.Stage.Show(Brawl, config, true);
                yield return null;
                renderFrames[i] = recorder.LastValue - baseline;
                yield return null;
                idleFrames[i] = recorder.LastValue - baseline;
            }

            GC.KeepAlive(new byte[4096]);
            yield return null;
            var probe = recorder.LastValue;
            Array.Sort(step);
            Array.Sort(render);
            long renderMax = 0;
            var renderNonZero = 0;
            var idleNonZero = 0;
            for (var i = 0; i < Frames; i++)
            {
                var extra = renderFrames[i] < 0 ? 0 : renderFrames[i];
                var idle = idleFrames[i] < 0 ? 0 : idleFrames[i];
                renderMax = Math.Max(renderMax, extra);
                if (extra > BackgroundBytes) { renderNonZero++; }
                if (idle > BackgroundBytes) { idleNonZero++; }
            }

            TestContext.WriteLine(
                $"敵 {_game.Rules.Capacity} 体：Step p95 {step[Samples * 95 / 100]:F3} ms、描画 p95 {render[Samples * 95 / 100]:F3} ms、" +
                $"描画の割り当て 最大 {renderMax}（{renderNonZero}/{Frames}、無処理 {idleNonZero}、基準 {baseline}、確認 {probe}）");
            Assert.That(recorder.Valid, Is.True);
            Assert.That(probe >= 4096 || probe - baseline >= 4096, Is.True, "計測の仕組みが割り当てを検出できる");
            Assert.That(step[Samples * 95 / 100], Is.LessThan(2));
            Assert.That(render[Samples * 95 / 100], Is.LessThan(2));
            Assert.That(renderNonZero, Is.LessThanOrEqualTo(Frames / 5));
            Assert.That(renderMax, Is.LessThanOrEqualTo(BackgroundBytes));
        }

        private IEnumerator StartMatch()
        {
            yield return _keyboard.Tap(Key.Enter);
            Assert.That(_game.Flow.Screen, Is.EqualTo(BrawlScreen.Howto));
            yield return _keyboard.Tap(Key.Enter);
            Assert.That(_game.Flow.Screen, Is.EqualTo(BrawlScreen.Playing));
        }

        /// <summary>キーを押した状態で 1 Tick 進め、離す。</summary>
        private IEnumerator Press(Key key)
        {
            _keyboard.Hold(key);
            yield return null;
            _game.Flow.StepTick();
            _keyboard.Hold();
            yield return null;
        }

        private IEnumerator Idle(int ticks)
        {
            _keyboard.Hold();
            yield return null;
            for (var i = 0; i < ticks; i++) { _game.Flow.StepTick(); }
        }
    }
}
