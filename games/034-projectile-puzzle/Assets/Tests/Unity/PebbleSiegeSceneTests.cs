using System;
using System.Collections;
using System.Collections.Generic;
using Game.App;
using Game.Core;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using MouseButton = UnityEngine.InputSystem.LowLevel.MouseButton;
using Object = UnityEngine.Object;

namespace Game.Unity.Tests
{
    public sealed class PebbleSiegeSceneTests
    {
        private const int TickLimit = 60 * 120;

        private TestKeyboard _keyboard;
        private Mouse _mouse;
        private PebbleSiegeGame _game;

        [UnitySetUp]
        public IEnumerator Load()
        {
            _keyboard = new TestKeyboard();
            _mouse = InputSystem.AddDevice<Mouse>();
            _mouse.MakeCurrent();
            yield return SceneManager.LoadSceneAsync("PebbleSiege");
            yield return null;
            _game = Object.FindAnyObjectByType<PebbleSiegeGame>();
            for (var i = 0; i < 30 && _game.HudRoot == null; i++) { yield return null; }
            Assert.That(_game.HudRoot, Is.Not.Null);
        }

        [UnityTearDown]
        public IEnumerator Unload()
        {
            Time.timeScale = 1;
            InputSystem.RemoveDevice(_mouse);
            _keyboard.Dispose();
            yield return null;
        }

        private SiegeRun Run => _game.Flow.Run;

        private PebbleSiegeWorld World => _game.World;

        private SiegeConfig Config => _game.Settings.Rules;

        private string Text(string name) => _game.HudRoot.Q<Label>(name).text;

        private static IReadOnlyList<IReadOnlyList<ShotStep>> Solutions(PebbleSiegeGame game) => ShotPlan.Parse(game.Settings.Solutions.text);

        /// <summary>時計を止めて（timeScale 0）タイトルから面選択を経て 1 面目を始める。Tick はテストが進める。</summary>
        private IEnumerator StartPaused()
        {
            Time.timeScale = 0;
            yield return _keyboard.Tap(Key.Enter);
            yield return _keyboard.Tap(Key.Enter);
            Assert.That(_game.Flow.Screen, Is.EqualTo(AppScreen.Playing));
        }

        private void SetMouse(V2 point, bool left, bool right = false)
        {
            var state = new MouseState { position = _game.FieldToScreen(point) };
            if (left) { state = state.WithButton(MouseButton.Left); }
            if (right) { state = state.WithButton(MouseButton.Right); }
            InputSystem.QueueStateEvent(_mouse, state);
        }

        /// <summary>盤の点をマウスで順にたどる（最初の点で押し、最後の点で離す）。1 点ごとに 1 フレーム。</summary>
        private IEnumerator Drag(params V2[] points)
        {
            SetMouse(points[0], false);
            yield return null;
            for (var i = 0; i < points.Length; i++)
            {
                SetMouse(points[i], true);
                yield return null;
            }

            SetMouse(points[points.Length - 1], false);
            yield return null;
        }

        private IEnumerator Click(V2 point)
        {
            SetMouse(point, false);
            yield return null;
            SetMouse(point, true);
            yield return null;
            SetMouse(point, false);
            yield return null;
        }

        private void Step(int ticks)
        {
            for (var i = 0; i < ticks && _game.Flow.Screen == AppScreen.Playing; i++)
            {
                _game.Flow.StepTick();
                _game.ConsumeEvents();
            }

            _game.Render();
        }

        private void StepUntil(Func<bool> done)
        {
            for (var i = 0; i < TickLimit && !done(); i++)
            {
                _game.Flow.StepTick();
                _game.ConsumeEvents();
            }

            _game.Render();
            Assert.That(done(), Is.True, "Tick の上限までに条件を満たさなかった");
        }

        // ---- 射出ベクトルと弾道プレビュー ----

        [UnityTest]
        public IEnumerator PreviewMatchesTheActualFlightForEveryShotKind()
        {
            _game.enabled = false;
            var stage = StageBook.Parse("stage 空\nhint -\nlauncher 4 2.5\nshots normal heavy bomb\nblock lantern 30 0.3 0.6 0.6\nend\n").Stages[0];
            var world = new PebbleSiegeWorld(_game.Stage, Config);
            try
            {
                foreach (var kind in new[] { ShotKind.Normal, ShotKind.Heavy, ShotKind.Bomb })
                {
                    world.Load(0, stage);
                    var velocity = Launch.Velocity(new V2(-2f, -1.1f), Config)!.Value;
                    var expected = new V2[40];
                    Trajectory.Fill(stage.Launcher, velocity, Config.Gravity, expected);
                    var report = new TickReport();
                    world.Simulate(new TickCommands(velocity, kind, false, false), report);
                    for (var t = 0; t < expected.Length; t++)
                    {
                        if (t > 0)
                        {
                            report.Clear();
                            world.Simulate(default, report);
                        }

                        var actual = world.Shot.Body.position;
                        Assert.That(actual.x, Is.EqualTo(expected[t].X).Within(1e-4f), $"{kind} {t + 1} Tick");
                        Assert.That(actual.y, Is.EqualTo(expected[t].Y).Within(1e-4f), $"{kind} {t + 1} Tick");
                        Assert.That(report.ShotTouched, Is.False);
                    }
                }
            }
            finally
            {
                world.Dispose();
            }

            yield return null;
        }

        [UnityTest]
        public IEnumerator EveryStageStandsStillWithoutShooting()
        {
            _game.enabled = false;
            var book = _game.Settings.Validate();
            var world = new PebbleSiegeWorld(_game.Stage, Config);
            try
            {
                for (var i = 0; i < book.Stages.Count; i++)
                {
                    var run = Replay.Play(world, Config, book.Stages[i], i, Array.Empty<ShotStep>(), limit: 900);
                    Assert.That(run.BrokenCount, Is.EqualTo(0), book.Stages[i].Name);
                    for (var p = 0; p < book.Stages[i].Pieces.Count; p++)
                    {
                        Assert.That(run.Health(p), Is.EqualTo(Config.Spec(book.Stages[i].Pieces[p].Material).Health), $"{book.Stages[i].Name} 部材 {p}");
                        var moved = (Vector2)world.Piece(p).transform.position - new Vector2(book.Stages[i].Pieces[p].Center.X, book.Stages[i].Pieces[p].Center.Y);
                        Assert.That(moved.magnitude, Is.LessThan(.01f), $"{book.Stages[i].Name} 部材 {p}");
                    }

                    yield return null;
                }
            }
            finally
            {
                world.Dispose();
            }
        }

        // ---- 5 面をクリア可能・目標命中で構造が崩れる・再現性 ----

        [UnityTest]
        public IEnumerator SolutionsClearAllFiveStagesThroughTheGame()
        {
            Time.timeScale = 0;
            _game.Flow.Pilot = new PlannedPilot(Solutions(_game));
            yield return _keyboard.Tap(Key.Enter);
            for (var i = 0; i < StageBook.ProductionCount; i++)
            {
                yield return _keyboard.Tap(Key.Enter);
                Assert.That(_game.Flow.Screen, Is.EqualTo(AppScreen.Playing));
                Assert.That(_game.Flow.Cursor, Is.EqualTo(i));
                StepUntil(() => _game.Flow.Screen == AppScreen.Outcome);
                Assert.That(Run.Phase, Is.EqualTo(RunPhase.Won), $"{i + 1} 面「{Run.Stage.Name}」");
                Assert.That(Text("headline"), Does.StartWith("砦を落とした"));
            }

            yield return _keyboard.Tap(Key.Enter);
            Assert.That(_game.Flow.Screen, Is.EqualTo(AppScreen.Result));
            Assert.That(_game.Flow.Progress.AllCleared, Is.True);
            Assert.That(Text("headline"), Is.EqualTo("全 5 面 クリア！"));
        }

        [UnityTest]
        public IEnumerator ReplayingAStageGivesTheSameResultWhateverCameBefore()
        {
            _game.enabled = false;
            var book = _game.Settings.Validate();
            var solutions = Solutions(_game);
            var world = new PebbleSiegeWorld(_game.Stage, Config);
            try
            {
                for (var i = 0; i < book.Stages.Count; i++)
                {
                    var first = Replay.Play(world, Config, book.Stages[i], i, solutions[i]);
                    var firstPositions = Positions(world);
                    // 別の面を挟んでから同じ面をもう一度（局所の物理のシーンを作り直すので履歴に依らない）。
                    Replay.Play(world, Config, book.Stages[(i + 1) % book.Stages.Count], (i + 1) % book.Stages.Count, solutions[(i + 1) % book.Stages.Count]);
                    var second = Replay.Play(world, Config, book.Stages[i], i, solutions[i]);
                    Assert.That(second.Phase, Is.EqualTo(RunPhase.Won), book.Stages[i].Name);
                    Assert.That(second.Hash(), Is.EqualTo(first.Hash()), book.Stages[i].Name);
                    Assert.That(Positions(world), Is.EqualTo(firstPositions), book.Stages[i].Name);
                    yield return null;
                }
            }
            finally
            {
                world.Dispose();
            }
        }

        [UnityTest]
        public IEnumerator HittingTheFortCollapsesTheStructure()
        {
            _game.enabled = false;
            var book = _game.Settings.Validate();
            var solutions = Solutions(_game);
            var world = new PebbleSiegeWorld(_game.Stage, Config);
            try
            {
                // 3 面「氷の高い塔」：重い弾 1 発の命中で、2 段の塔の部材が複数動く・壊れる。
                var tower = book.Stages[2];
                var hit = Replay.Play(world, Config, tower, 2, solutions[2]);
                var moved = 0;
                for (var p = 0; p < tower.Pieces.Count; p++)
                {
                    var piece = world.Piece(p);
                    var start = new Vector2(tower.Pieces[p].Center.X, tower.Pieces[p].Center.Y);
                    if (piece == null || ((Vector2)piece.transform.position - start).magnitude > .3f) { moved++; }
                }

                Assert.That(hit.ShotsUsed, Is.EqualTo(1));
                Assert.That(moved, Is.GreaterThanOrEqualTo(4), "命中で構造が崩れる");
                Assert.That(hit.BrokenCount, Is.GreaterThanOrEqualTo(3));

                // 2 面「石の土台」：接合した砦。崩すと接合が外れ、物理からも消える。
                var stage = book.Stages[1];
                var run = Replay.Play(world, Config, stage, 1, solutions[1]);
                var broken = 0;
                for (var k = 0; k < stage.Joints.Count; k++)
                {
                    if (run.IsJointBroken(k))
                    {
                        broken++;
                        Assert.That(world.Joint(k), Is.Null, "外れた接合は物理からも消える");
                    }
                }

                Assert.That(broken, Is.GreaterThan(0), "接合が外れる");
                Assert.That(run.Score, Is.GreaterThan(0));
            }
            finally
            {
                world.Dispose();
            }

            yield return null;
        }

        private static List<Vector2> Positions(PebbleSiegeWorld world)
        {
            var positions = new List<Vector2>();
            for (var p = 0; p < world.PieceCount; p++)
            {
                var piece = world.Piece(p);
                positions.Add(piece == null ? new Vector2(-999, -999) : (Vector2)piece.transform.position);
            }

            return positions;
        }

        // ---- 入力 ----

        [UnityTest]
        public IEnumerator MouseDragLaunchesWithThePullAndUsesAShot()
        {
            yield return StartPaused();
            var launcher = Run.Stage.Launcher;
            var release = launcher + new V2(-2f, -1f);
            yield return Drag(launcher, launcher + new V2(-1f, .5f), release);
            Step(1);
            Assert.That(Run.ShotsUsed, Is.EqualTo(1));
            Assert.That(World.Shot, Is.Not.Null);
            var expected = Launch.Velocity(release - launcher, Config)!.Value;
            var actual = World.Shot.Body.linearVelocity;
            Assert.That(actual.x, Is.EqualTo(expected.X).Within(.1f), "初速は離した位置の引きで決まる（画面の座標の丸めまで）");
            Assert.That(actual.y, Is.EqualTo(expected.Y + (Config.Gravity / SiegeRun.TickRate)).Within(.1f));
            Assert.That(Text("shots"), Is.EqualTo("弾  通常 ・ 通常"), "HUD の残りの弾が 1 発減る");
        }

        [UnityTest]
        public IEnumerator ShortDragAndRightClickCancelWithoutShooting()
        {
            yield return StartPaused();
            var launcher = Run.Stage.Launcher;
            yield return Drag(launcher, launcher + new V2(-.1f, 0f));
            Step(2);
            Assert.That(Run.ShotsUsed, Is.EqualTo(0), "短い引き");

            SetMouse(launcher, true);
            yield return null;
            SetMouse(launcher + new V2(-2f, -1f), true);
            yield return null;
            Assert.That(_game.Flow.Aim, Is.Not.Null);
            SetMouse(launcher + new V2(-2f, -1f), true, right: true);
            yield return null;
            Assert.That(_game.Flow.Aim, Is.Null, "右クリックで取り消し");
            SetMouse(launcher + new V2(-2f, -1f), false);
            yield return null;
            Step(2);
            Assert.That(Run.ShotsUsed, Is.EqualTo(0));
        }

        [UnityTest]
        public IEnumerator ClickDuringABombsFlightDetonatesOnce()
        {
            Time.timeScale = 0;
            for (var i = 0; i < 3; i++) { _game.Flow.Progress.Record(i, 1, 1); }
            yield return _keyboard.Tap(Key.Enter);
            for (var i = 0; i < 3; i++) { yield return _keyboard.Tap(Key.RightArrow); }
            yield return _keyboard.Tap(Key.Enter);
            Assert.That(Run.Stage.Name, Is.EqualTo("囲いの奥"));
            Assert.That(Run.NextShot, Is.EqualTo(ShotKind.Bomb));

            var launcher = Run.Stage.Launcher;
            yield return Drag(launcher, launcher + new V2(-2.2f, -.6f));
            Step(10);
            Assert.That(Run.FlyingShot, Is.EqualTo(ShotKind.Bomb));
            Assert.That(Run.Detonated, Is.False, "撃った離しでは起爆しない");
            Assert.That(Text("hint"), Does.StartWith("クリック / Space：起爆"));
            yield return Click(new V2(20f, 10f));
            Step(1);
            Assert.That(Run.Detonated, Is.True);
            Assert.That(World.LastBlastTick, Is.GreaterThanOrEqualTo(0));
            Assert.That(World.Shot, Is.Null, "起爆した弾は消える");
            Assert.That(_game.Stage.Blast.enabled, Is.True);
            yield return Click(new V2(20f, 10f));
            Step(1);
            Assert.That(Run.CanDetonate, Is.False);
        }

        [UnityTest]
        public IEnumerator AimShowsTheBandAndTrajectoryDots()
        {
            yield return StartPaused();
            var launcher = Run.Stage.Launcher;
            SetMouse(launcher, true);
            yield return null;
            SetMouse(launcher + new V2(-1.8f, -1f), true);
            yield return null;
            _game.Render();
            var velocity = _game.Flow.AimVelocity!.Value;
            var points = new V2[Config.PreviewTicks];
            Trajectory.Fill(launcher, velocity, Config.Gravity, points);
            var count = Config.PreviewTicks / Config.PreviewStride;
            for (var i = 0; i < PebbleSiegeStage.MaxDots; i++)
            {
                Assert.That(_game.Stage.Dot(i).enabled, Is.EqualTo(i < count), $"点 {i}");
                if (i >= count) { continue; }
                var expected = points[((i + 1) * Config.PreviewStride) - 1];
                Assert.That((Vector2)_game.Stage.Dot(i).transform.position, Is.EqualTo(new Vector2(expected.X, expected.Y)));
            }

            Assert.That(_game.Stage.Loaded.enabled, Is.True);
            var aim = _game.Flow.Aim!.Value;
            Assert.That((Vector2)_game.Stage.Loaded.transform.position, Is.EqualTo(new Vector2(launcher.X + aim.X, launcher.Y + aim.Y)));
            Assert.That(_game.Stage.Band.GetPosition(1), Is.EqualTo(_game.Stage.Loaded.transform.localPosition));

            SetMouse(launcher + new V2(-1.8f, -1f), false);
            yield return null;
            Step(1);
            _game.Render();
            Assert.That(_game.Stage.Dot(0).enabled, Is.False, "撃ったら点を消す");
            Assert.That(_game.Stage.Loaded.enabled, Is.False);
        }

        [UnityTest]
        public IEnumerator LosingFocusCancelsTheAimAndPauses()
        {
            yield return StartPaused();
            var launcher = Run.Stage.Launcher;
            SetMouse(launcher, true);
            yield return null;
            Assert.That(_game.Flow.Aim, Is.Not.Null);
            _game.SendMessage("OnApplicationFocus", false);
            Assert.That(_game.Flow.Aim, Is.Null);
            Assert.That(_game.Flow.Screen, Is.EqualTo(AppScreen.Paused));
            _game.SendMessage("OnApplicationFocus", true);
            SetMouse(launcher + new V2(-2f, -1f), false);
            yield return null;
            Step(2);
            Assert.That(Run.ShotsUsed, Is.EqualTo(0));
        }

        [UnityTest]
        public IEnumerator RetryRebuildsTheStage()
        {
            yield return StartPaused();
            var launcher = Run.Stage.Launcher;
            yield return Drag(launcher, launcher + new V2(-2.364f, -.417f));
            StepUntil(() => Run.BrokenCount > 0 || Run.Phase != RunPhase.Flying);
            yield return _keyboard.Tap(Key.R);
            Assert.That(Run.ShotsUsed, Is.EqualTo(0));
            Assert.That(World.Shot, Is.Null);
            for (var p = 0; p < Run.Stage.Pieces.Count; p++)
            {
                Assert.That(World.Piece(p), Is.Not.Null);
                Assert.That((Vector2)World.Piece(p).transform.position, Is.EqualTo(new Vector2(Run.Stage.Pieces[p].Center.X, Run.Stage.Pieces[p].Center.Y)));
            }
        }

        [UnityTest]
        public IEnumerator ReturningToTheTitleShowsTheSameAsAtStartup()
        {
            Time.timeScale = 0;
            _game.Render();
            var startPositions = Positions(World);
            var startHeadline = Text("headline");
            var startMessage = Text("message");

            yield return StartPausedFromTitle();
            var launcher = Run.Stage.Launcher;
            yield return Drag(launcher, launcher + new V2(-2.364f, -.417f));
            Step(200);
            yield return _keyboard.Tap(Key.Escape);
            yield return _keyboard.Tap(Key.Backspace);
            yield return _keyboard.Tap(Key.Escape);
            Assert.That(_game.Flow.Screen, Is.EqualTo(AppScreen.Title));
            _game.Render();
            Assert.That(Positions(World), Is.EqualTo(startPositions));
            Assert.That(World.Shot, Is.Null);
            Assert.That(Text("headline"), Is.EqualTo(startHeadline));
            Assert.That(Text("message"), Is.EqualTo(startMessage));
            Assert.That(_game.HudRoot.Q("bar").style.display.value, Is.EqualTo(DisplayStyle.None));
            Assert.That(_game.Stage.Loaded.enabled, Is.False);
        }

        private IEnumerator StartPausedFromTitle()
        {
            yield return _keyboard.Tap(Key.Enter);
            yield return _keyboard.Tap(Key.Enter);
            Assert.That(_game.Flow.Screen, Is.EqualTo(AppScreen.Playing));
        }

        [UnityTest]
        public IEnumerator LockedStageTileCannotBeChosen()
        {
            Time.timeScale = 0;
            yield return _keyboard.Tap(Key.Enter);
            Assert.That(_game.Flow.Screen, Is.EqualTo(AppScreen.Select));
            var tile = _game.HudRoot.Q<Button>(PebbleSiegeGame.TileName(1));
            Assert.That(tile.enabledSelf, Is.False);
            Assert.That(tile.Q<Label>("tile-name").text, Is.EqualTo("？"));
            yield return _keyboard.Tap(Key.RightArrow);
            Assert.That(_game.Flow.Cursor, Is.EqualTo(0));
        }

        // ---- 設定と Prefab ----

        [Test]
        public void SettingsAssetAndPhysicsModeAreValid()
        {
            Assert.That(Physics2D.simulationMode, Is.EqualTo(SimulationMode2D.Script));
            Assert.DoesNotThrow(() => _game.Settings.Validate());
            Assert.That(JsonUtility.ToJson(_game.Settings.Rules), Is.EqualTo(JsonUtility.ToJson(new SiegeConfig())), "設定アセットはコードの既定値と同じ（変えたらアセットも直す）");
            Assert.That(ShotPlan.Parse(_game.Settings.Solutions.text).Count, Is.EqualTo(StageBook.ProductionCount));
        }

        [Test]
        public void StagePrefabHasEveryPartAndMismatchDetectsBrokenCopies()
        {
            Assert.That(_game.Stage.Mismatch(), Is.Null);
            var breakers = new Action<PebbleSiegeStage>[]
            {
                stage => stage.Loaded.sprite = null,
                stage => stage.Dot(3).sprite = null,
                stage => stage.Template(Core.Material.Stone).drawMode = SpriteDrawMode.Simple,
                stage => stage.Template(ShotKind.Bomb).transform.SetParent(null),
                stage => stage.Band.transform.localPosition = Vector3.one,
                stage => stage.Band.useWorldSpace = true,
                stage => stage.Puff(0).sprite = null,
            };
            foreach (var breaker in breakers)
            {
                var copy = Object.Instantiate(_game.Stage);
                try
                {
                    breaker(copy);
                    Assert.That(copy.Mismatch(), Is.Not.Null);
                }
                finally
                {
                    Object.DestroyImmediate(copy.gameObject);
                }
            }
        }

        [Test]
        public void CapacityRejectsTooManyPreviewDots()
        {
            var book = _game.Settings.Validate();
            Assert.That(PebbleSiegeStage.Capacity(book, Config), Is.Null);
            var config = Config.Copy();
            config.PreviewTicks = (PebbleSiegeStage.MaxDots + 1) * config.PreviewStride;
            Assert.That(PebbleSiegeStage.Capacity(book, config), Is.Not.Null);
        }
    }
}
