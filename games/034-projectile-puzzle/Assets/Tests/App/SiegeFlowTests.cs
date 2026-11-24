using System;
using System.Collections.Generic;
using Game.Core;
using NUnit.Framework;

namespace Game.App.Tests
{
    /// <summary>物理のない世界：命令を記録し、決めた報告を返す。</summary>
    internal sealed class FakeWorld : ISiegeWorld
    {
        public List<int> Loads { get; } = new();

        public List<TickCommands> Commands { get; } = new();

        public List<int> RemovedPieces { get; } = new();

        public int Clears { get; private set; }

        /// <summary>撃った Tick の報告に足すこと（例：標的を落とす）。</summary>
        public Action<TickReport>? OnLaunch { get; set; }

        /// <summary>撃ってからこの Tick 数だけ動き続ける。</summary>
        public int MovingTicks { get; set; } = 10;

        private int _moving;

        public void Load(int index, Stage stage)
        {
            Loads.Add(index);
            _moving = 0;
        }

        public void Simulate(TickCommands commands, TickReport report)
        {
            Commands.Add(commands);
            if (commands.Launch != null)
            {
                _moving = MovingTicks;
                OnLaunch?.Invoke(report);
            }

            if (_moving > 0)
            {
                _moving--;
                report.MaxSpeed = 5f;
            }
        }

        public void Remove(IReadOnlyList<int> pieces, IReadOnlyList<int> joints, bool shot)
        {
            RemovedPieces.AddRange(pieces);
            if (shot) { ShotRemovals++; }
        }

        /// <summary>静止して弾を消した回数。</summary>
        public int ShotRemovals { get; private set; }

        public void Clear() => Clears++;

        public int LaunchCount
        {
            get
            {
                var count = 0;
                foreach (var command in Commands)
                {
                    if (command.Launch != null) { count++; }
                }

                return count;
            }
        }
    }

    public sealed class SiegeFlowTests
    {
        private const string Text =
            "stage 一\nhint a\nlauncher 4 2.5\nshots normal bomb\nblock wood 20 1 0.4 2\nblock lantern 20 2.3 0.6 0.6\nend\n" +
            "stage 二\nhint b\nlauncher 4 2.5\nshots normal\nblock lantern 22 0.3 0.6 0.6\nend\n";

        private static readonly SiegeConfig _config = new();
        private static readonly V2 _launcher = new(4f, 2.5f);
        private static readonly V2 _release = new(2.5f, 1.5f);

        private FakeWorld _world = null!;
        private SiegeFlow _flow = null!;
        private double _time;

        [SetUp]
        public void SetUp()
        {
            _world = new FakeWorld();
            _flow = new SiegeFlow(_config, StageBook.Parse(Text), _world);
            _time = 0;
        }

        private void Apply(Intent intent) => _flow.Apply(intent);

        private void Key(IntentKind kind) => Apply(Intent.Of(kind, _time));

        private void Mouse(IntentKind kind, V2 point) => Apply(Intent.Mouse(kind, point, _time));

        /// <summary>時計を <paramref name="ticks"/> Tick 進める（1 フレーム 1 Tick）。</summary>
        private void Ticks(int ticks)
        {
            for (var i = 0; i < ticks; i++)
            {
                _time += 1.0 / SiegeRun.TickRate;
                _flow.Advance(_time + 1e-9);
            }
        }

        private void StartStage()
        {
            Key(IntentKind.Submit);
            Key(IntentKind.Submit);
            Assert.That(_flow.Screen, Is.EqualTo(AppScreen.Playing));
        }

        private void Drag(params V2[] path)
        {
            Mouse(IntentKind.PointerDown, _launcher);
            for (var i = 0; i < path.Length - 1; i++) { Mouse(IntentKind.PointerMove, path[i]); }
            Mouse(IntentKind.PointerUp, path[path.Length - 1]);
        }

        private void KnockDownAllTargets() => _world.OnLaunch = report => report.FellOut.Add(1);

        [Test]
        public void TitleSelectPlayLoadsTheStage()
        {
            Assert.That(_flow.Screen, Is.EqualTo(AppScreen.Title));
            Mouse(IntentKind.PointerDown, V2.Zero);
            Assert.That(_flow.Screen, Is.EqualTo(AppScreen.Select));
            Key(IntentKind.Submit);
            Assert.That(_flow.Screen, Is.EqualTo(AppScreen.Playing));
            Assert.That(string.Join(",", _world.Loads), Is.EqualTo("0"));
            Assert.That(_flow.Run!.Stage.Name, Is.EqualTo("一"));
        }

        [Test]
        public void ReleasingTheDragLaunchesWithTheReleasePullAtTheNextTick()
        {
            StartStage();
            Drag(_release);
            Assert.That(_world.LaunchCount, Is.EqualTo(0), "Tick の境目まで撃たない");
            Ticks(1);
            Assert.That(_world.Commands[0].Launch, Is.EqualTo(Launch.Velocity(_release - _launcher, _config)));
            Assert.That(_flow.Run!.ShotsLeft, Is.EqualTo(1));
            Assert.That((_flow.TakeEvents() & FlowEvents.Launched) != 0, Is.True);
        }

        [Test]
        public void TheDragPathDoesNotChangeTheLaunchVelocity()
        {
            StartStage();
            Drag(new V2(5f, 5f), new V2(1f, 0f), new V2(3.9f, 2.4f), _release);
            Ticks(1);
            var winding = _world.Commands[0].Launch;

            SetUp();
            StartStage();
            Drag(_release);
            Ticks(1);
            Assert.That(_world.Commands[0].Launch, Is.EqualTo(winding));
        }

        [Test]
        public void PressFarFromTheLauncherDoesNotAim()
        {
            StartStage();
            Mouse(IntentKind.PointerDown, new V2(10f, 5f));
            Assert.That(_flow.Aim, Is.Null);
            Mouse(IntentKind.PointerUp, _release);
            Ticks(1);
            Assert.That(_world.LaunchCount, Is.EqualTo(0));
        }

        [Test]
        public void AimFollowsThePointerAndIsClampedForDrawing()
        {
            StartStage();
            Mouse(IntentKind.PointerDown, _launcher);
            Mouse(IntentKind.PointerMove, new V2(-20f, 2.5f));
            Assert.That(_flow.Aim!.Value.Length, Is.EqualTo(_config.MaxPull).Within(1e-5f));
            Assert.That(_flow.AimVelocity!.Value.Length, Is.EqualTo(_config.MaxSpeed).Within(1e-3f));
            Mouse(IntentKind.PointerMove, _launcher);
            Assert.That(_flow.AimVelocity, Is.Null, "短い引きは取り消しになる");
        }

        [Test]
        public void ShortReleaseCancelsWithoutUsingAShot()
        {
            StartStage();
            Drag(_launcher + new V2(-0.1f, 0f));
            Ticks(5);
            Assert.That(_world.LaunchCount, Is.EqualTo(0));
            Assert.That(_flow.Run!.ShotsLeft, Is.EqualTo(2));
            Assert.That((_flow.TakeEvents() & FlowEvents.AimCancelled) != 0, Is.True);
        }

        [Test]
        public void RightClickOrEscCancelsTheAimAndEscThenPauses()
        {
            StartStage();
            Mouse(IntentKind.PointerDown, _launcher);
            Key(IntentKind.CancelAim);
            Mouse(IntentKind.PointerUp, _release);
            Ticks(1);
            Assert.That(_world.LaunchCount, Is.EqualTo(0));

            Mouse(IntentKind.PointerDown, _launcher);
            Key(IntentKind.Cancel);
            Assert.That(_flow.Screen, Is.EqualTo(AppScreen.Playing), "構えていれば Esc は取り消しだけ");
            Assert.That(_flow.Aim, Is.Null);
            Key(IntentKind.Cancel);
            Assert.That(_flow.Screen, Is.EqualTo(AppScreen.Paused));
            Key(IntentKind.Cancel);
            Assert.That(_flow.Screen, Is.EqualTo(AppScreen.Playing));
        }

        [Test]
        public void ClickDuringABombsFlightDetonatesOnceAndTheLaunchReleaseDoesNot()
        {
            StartStage();
            Drag(_release);
            Ticks(_world.MovingTicks + _config.RestTicks + 2);
            _world.MovingTicks = 200;
            Assert.That(_flow.Run!.NextShot, Is.EqualTo(ShotKind.Bomb));
            Drag(_release);
            Ticks(3);
            Assert.That(_flow.Run!.FlyingShot, Is.EqualTo(ShotKind.Bomb));
            Assert.That(_world.Commands.Exists(c => c.Detonate), Is.False, "撃った離しでは起爆しない");
            Mouse(IntentKind.PointerDown, new V2(25f, 5f));
            Mouse(IntentKind.PointerDown, new V2(25f, 5f));
            Ticks(2);
            Assert.That(_world.Commands.FindAll(c => c.Detonate).Count, Is.EqualTo(1));
            Key(IntentKind.Submit);
            Ticks(2);
            Assert.That(_world.Commands.FindAll(c => c.Detonate).Count, Is.EqualTo(1), "1 発で 1 回だけ");
        }

        [Test]
        public void SpaceDetonatesTheBomb()
        {
            StartStage();
            Drag(_release);
            Ticks(_world.MovingTicks + _config.RestTicks + 2);
            _world.MovingTicks = 200;
            Drag(_release);
            Ticks(3);
            Key(IntentKind.Submit);
            Ticks(1);
            Assert.That(_world.Commands.FindAll(c => c.Detonate).Count, Is.EqualTo(1));
        }

        [Test]
        public void WinningAfterSettlingRecordsAndUnlocksTheNextStage()
        {
            StartStage();
            KnockDownAllTargets();
            Drag(_release);
            Ticks(5);
            Assert.That(_flow.Screen, Is.EqualTo(AppScreen.Playing), "動いている間は確定しない");
            Ticks(_world.MovingTicks + _config.RestTicks);
            Assert.That(_flow.Screen, Is.EqualTo(AppScreen.Outcome));
            Assert.That(_flow.Run!.Phase, Is.EqualTo(RunPhase.Won));
            Assert.That(string.Join(",", _world.RemovedPieces), Is.EqualTo("1"));
            Assert.That(_flow.Progress.Unlocked, Is.EqualTo(2));
            Assert.That(_flow.Progress.Best(0), Is.EqualTo(_flow.Run.Score));
            Assert.That(_flow.IsNewBest, Is.True);
            var commands = _world.Commands.Count;
            Ticks(30);
            Assert.That(_world.Commands.Count, Is.EqualTo(commands), "結果の後は物理を進めない");

            Key(IntentKind.Submit);
            Assert.That(_flow.Screen, Is.EqualTo(AppScreen.Playing));
            Assert.That(_flow.Cursor, Is.EqualTo(1));
            Assert.That(string.Join(",", _world.Loads), Is.EqualTo("0,1"));
        }

        [Test]
        public void LosingRetriesTheSameStageWithAFreshWorld()
        {
            StartStage();
            Drag(_release);
            Ticks(_world.MovingTicks + _config.RestTicks + 12);
            Drag(_release);
            Ticks(_world.MovingTicks + _config.RestTicks + 2);
            Assert.That(_flow.Screen, Is.EqualTo(AppScreen.Outcome));
            Assert.That(_flow.Run!.Phase, Is.EqualTo(RunPhase.Lost));
            Assert.That(_flow.Progress.Unlocked, Is.EqualTo(1));
            Mouse(IntentKind.PointerDown, V2.Zero);
            Assert.That(_flow.Screen, Is.EqualTo(AppScreen.Playing));
            Assert.That(_flow.Run!.ShotsLeft, Is.EqualTo(2));
            Assert.That(string.Join(",", _world.Loads), Is.EqualTo("0,0"));
        }

        [Test]
        public void RetryDuringPlayReloadsTheWorld()
        {
            StartStage();
            Drag(_release);
            Ticks(3);
            Key(IntentKind.Retry);
            Assert.That(string.Join(",", _world.Loads), Is.EqualTo("0,0"));
            Assert.That(_flow.Run!.ShotsUsed, Is.EqualTo(0));
            Assert.That((_flow.TakeEvents() & FlowEvents.Retried) != 0, Is.True);
        }

        [Test]
        public void LockedStagesCannotBeChosen()
        {
            Key(IntentKind.Submit);
            Mouse(IntentKind.PointerDown, V2.Zero);
            Apply(Intent.Choose(1, _time));
            Assert.That(_flow.Screen, Is.EqualTo(AppScreen.Select));
            Apply(Intent.Move(1, _time));
            Assert.That(_flow.Cursor, Is.EqualTo(0));
            Mouse(IntentKind.PointerDown, V2.Zero);
            Apply(Intent.Choose(0, _time));
            Assert.That(_flow.Screen, Is.EqualTo(AppScreen.Playing));
        }

        [Test]
        public void ChooseOnlyWorksForAClickPressedOnTheSelectScreen()
        {
            Mouse(IntentKind.PointerDown, V2.Zero);
            Assert.That(_flow.Screen, Is.EqualTo(AppScreen.Select));
            Apply(Intent.Choose(0, _time));
            Assert.That(_flow.Screen, Is.EqualTo(AppScreen.Select), "タイトルで押したクリックの離しは札を押さない");
        }

        [Test]
        public void LosingFocusCancelsTheAimPausesAndDropsTheRefocusClick()
        {
            StartStage();
            Mouse(IntentKind.PointerDown, _launcher);
            _flow.SetFocus(false);
            Assert.That(_flow.Aim, Is.Null);
            Assert.That(_flow.Screen, Is.EqualTo(AppScreen.Paused));
            Mouse(IntentKind.PointerUp, _release);
            _flow.SetFocus(true);
            Key(IntentKind.Cancel);
            Assert.That(_flow.Screen, Is.EqualTo(AppScreen.Playing));
            Mouse(IntentKind.PointerDown, _launcher);
            Assert.That(_flow.Aim, Is.Null, "復帰直後のクリックは捨てる");
            Mouse(IntentKind.PointerUp, _release);
            Ticks(2);
            Assert.That(_world.LaunchCount, Is.EqualTo(0));
            Drag(_release);
            Ticks(1);
            Assert.That(_world.LaunchCount, Is.EqualTo(1));
        }

        [Test]
        public void RefocusBeforeTheClickOrderIsAlsoHandled()
        {
            StartStage();
            _flow.SetFocus(false);
            _flow.SetFocus(true);
            Key(IntentKind.Submit);
            Drag(_release);
            Ticks(1);
            Assert.That(_world.LaunchCount, Is.EqualTo(0), "フォーカス復帰の押下から離すまでは捨てる");
        }

        [Test]
        public void LeavingThePlayClearsTheWorld()
        {
            StartStage();
            Key(IntentKind.Cancel);
            Key(IntentKind.Back);
            Assert.That(_flow.Screen, Is.EqualTo(AppScreen.Select));
            Assert.That(_world.Clears, Is.EqualTo(1));
            Assert.That(_flow.Run, Is.Null);
            Key(IntentKind.Cancel);
            Assert.That(_flow.Screen, Is.EqualTo(AppScreen.Title));
            Assert.That(_world.Clears, Is.EqualTo(1), "面がなければ片付けない");
        }

        [Test]
        public void LongFrameAdvancesAtMostTheFrameBudget()
        {
            StartStage();
            _flow.Advance(10.0);
            Assert.That(_world.Commands.Count, Is.EqualTo(SiegeFlow.MaxTicksPerFrame));
            _flow.Advance(10.0);
            Assert.That(_world.Commands.Count, Is.EqualTo(SiegeFlow.MaxTicksPerFrame));
        }

        [Test]
        public void ClearingAllStagesShowsTheResultThenTheTitle()
        {
            StartStage();
            KnockDownAllTargets();
            Drag(_release);
            Ticks(_world.MovingTicks + _config.RestTicks + 1);
            Key(IntentKind.Submit);
            _world.OnLaunch = report => report.FellOut.Add(0);
            Drag(_release);
            Ticks(_world.MovingTicks + _config.RestTicks + 1);
            Assert.That(_flow.Run!.Phase, Is.EqualTo(RunPhase.Won));
            Key(IntentKind.Submit);
            Assert.That(_flow.Screen, Is.EqualTo(AppScreen.Result));
            Assert.That(_flow.Progress.AllCleared, Is.True);
            Key(IntentKind.Submit);
            Assert.That(_flow.Screen, Is.EqualTo(AppScreen.Title));
        }

        [Test]
        public void PlannedPilotShootsThePlanAndDetonatesOnSchedule()
        {
            var plans = ShotPlan.Parse("stage\nshot -1.5 -1\nshot -2 -0.5 30\nend\nstage\nend\n");
            _flow.Pilot = new PlannedPilot(plans);
            _world.MovingTicks = 100;
            StartStage();
            Mouse(IntentKind.PointerDown, _launcher);
            Assert.That(_flow.Aim, Is.Null, "操縦役がいる間はマウスで構えない");
            for (var i = 0; i < 600; i++) { _flow.StepTick(); }
            var launches = _world.Commands.FindAll(c => c.Launch != null);
            Assert.That(launches.Count, Is.EqualTo(2));
            Assert.That(launches[0].Launch, Is.EqualTo(Launch.Velocity(new V2(-1.5f, -1f), _config)));
            Assert.That(_world.Commands.IndexOf(launches[0]), Is.EqualTo(PlannedPilot.AimDelay), "静止を待ってから撃つ");
            var second = _world.Commands.IndexOf(launches[1]);
            var detonation = _world.Commands.FindIndex(c => c.Detonate);
            Assert.That(detonation - second, Is.EqualTo(30));
        }

        [Test]
        public void ConfigIsACopyThatCannotChangeTheFlow()
        {
            // レビュー P2-2：検証済みの設定を外から書き換えられると、次の面で例外になる・物理と Core の設定が分かれる。
            var exposed = _flow.Config;
            exposed.MaxPull = 0f;
            exposed.MaxSpeed = 1f;
            Assert.That(_flow.Config.MaxPull, Is.EqualTo(_config.MaxPull));
            Assert.That(_flow.Config, Is.Not.SameAs(_flow.Config));
            StartStage();
            Drag(_release);
            Ticks(1);
            Assert.That(_world.Commands[0].Launch, Is.EqualTo(Launch.Velocity(_release - _launcher, _config)));
        }

        [Test]
        public void TheShotIsRemovedWhenTheRunEndsOnSettling()
        {
            // レビュー P3-1：静止で勝敗が決まると次の Tick がないので、その Tick に弾を消す。
            StartStage();
            KnockDownAllTargets();
            Drag(_release);
            Ticks(_world.MovingTicks + _config.RestTicks + 1);
            Assert.That(_flow.Screen, Is.EqualTo(AppScreen.Outcome));
            Assert.That(_world.ShotRemovals, Is.EqualTo(1));
        }

        [Test]
        public void NonFiniteMousePointsAreRejected()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => Intent.Mouse(IntentKind.PointerDown, new V2(float.NaN, 0f), 0));
            Assert.Throws<ArgumentException>(() => Intent.Mouse(IntentKind.Submit, V2.Zero, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => Intent.Of(IntentKind.Submit, double.NaN));
            Assert.That(Intent.Mouse(IntentKind.PointerMove, new V2(1e6f, -1e6f), 0).Point, Is.EqualTo(new V2(Intent.PointLimit, -Intent.PointLimit)));
        }
    }
}
