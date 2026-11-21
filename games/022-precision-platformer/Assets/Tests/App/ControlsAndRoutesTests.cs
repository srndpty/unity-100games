using System;
using Game.Core;
using NUnit.Framework;

namespace Game.App.Tests
{
    /// <summary>押しているキーから Tick ごとの入力への変換と、入力の列（ルート）。</summary>
    public sealed class ControlsAndRoutesTests
    {
        /// <summary>
        /// 全ルートで 10 部屋を通したときの状態のハッシュ。整数だけで計算するので .NET と Mono で同じ値になる。
        /// 部屋・ルート・設定の既定値を変えたら作り直す。
        /// </summary>
        private const uint ExpectedClimbHash = 0x4FC97CE8u;

        private static readonly Control[] _allControls = { Control.Left, Control.Right, Control.Up, Control.Down, Control.Jump, Control.Dash };

        [Test]
        public void Held_LastPressedDirectionWinsPerAxis()
        {
            var held = new HeldControls();
            held.Press(Control.Left);
            held.Press(Control.Right);
            held.Press(Control.Down);
            held.Press(Control.Up);
            Assert.That(held.TakeTickInput(), Is.EqualTo(new PlayerInput(1, 1, false, false)));
            held.Release(Control.Right);
            held.Release(Control.Up);
            Assert.That(held.TakeTickInput(), Is.EqualTo(new PlayerInput(-1, -1, false, false)), "離すと、まだ押しているほうに戻る");
            held.Press(Control.Left);
            held.Release(Control.Left);
            held.Release(Control.Down);
            Assert.That(held.TakeTickInput(), Is.EqualTo(PlayerInput.None));
        }

        [Test]
        public void Held_ShortJumpAndDashPressesLastOneTick()
        {
            var held = new HeldControls();
            held.Press(Control.Jump);
            held.Release(Control.Jump);
            held.Press(Control.Dash);
            held.Release(Control.Dash);
            Assert.That(held.TakeTickInput(), Is.EqualTo(new PlayerInput(0, 0, true, true)), "Tick の間に押して離しても 1 Tick は押したことにする");
            Assert.That(held.TakeTickInput(), Is.EqualTo(PlayerInput.None));
        }

        [Test]
        public void Held_ClearForgetsEverything()
        {
            var held = new HeldControls();
            foreach (var control in _allControls) { held.Press(control); }
            held.Clear();
            Assert.That(held.TakeTickInput(), Is.EqualTo(PlayerInput.None));
        }

        [Test]
        public void Held_RejectsUndefinedControl()
        {
            var held = new HeldControls();
            Assert.Throws<ArgumentOutOfRangeException>(() => held.Press((Control)99));
            Assert.Throws<ArgumentOutOfRangeException>(() => held.Release((Control)99));
            Assert.Throws<ArgumentOutOfRangeException>(() => Intent.Down((Control)99, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => Intent.Of((IntentKind)99, 0));
            Assert.Throws<ArgumentException>(() => Intent.Of(IntentKind.ControlDown, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => Intent.Of(IntentKind.Submit, double.NaN));
            Assert.Throws<ArgumentOutOfRangeException>(() => Intent.Up(Control.Jump, double.PositiveInfinity));
        }

        [Test]
        public void Route_ParsesSegmentsAndFormatsBack()
        {
            var route = RouteScript.Parse("R2 RUJX1 -3 LDJ1");
            Assert.That(route.Length, Is.EqualTo(7));
            Assert.That(route.InputAt(0), Is.EqualTo(new PlayerInput(1, 0, false, false)));
            Assert.That(route.InputAt(2), Is.EqualTo(new PlayerInput(1, 1, true, true)));
            Assert.That(route.InputAt(3), Is.EqualTo(PlayerInput.None));
            Assert.That(route.InputAt(6), Is.EqualTo(new PlayerInput(-1, -1, true, false)));
            Assert.That(route.InputAt(7), Is.EqualTo(PlayerInput.None), "列の後は何も押さない");
            Assert.That(route.InputAt(-1), Is.EqualTo(PlayerInput.None));
            Assert.That(RouteScript.Format(route.Inputs), Is.EqualTo("R2 RUJX1 -3 LDJ1"));
        }

        [TestCase("R", TestName = "Route_RejectsMissingCount")]
        [TestCase("12", TestName = "Route_RejectsMissingKeys")]
        [TestCase("R0", TestName = "Route_RejectsZeroTicks")]
        [TestCase("LR3", TestName = "Route_RejectsBothHorizontal")]
        [TestCase("JJ3", TestName = "Route_RejectsDuplicateKey")]
        [TestCase("Q3", TestName = "Route_RejectsUnknownKey")]
        [TestCase("R3601", TestName = "Route_RejectsTooLong")]
        public void Route_RejectsInvalidText(string text)
        {
            Assert.Throws<FormatException>(() => RouteScript.Parse(text));
        }

        [Test]
        public void Routes_ClearEveryRoomWithoutDying()
        {
            var rooms = Rooms.All();
            var routes = Routes.All();
            Assert.That(routes, Has.Count.EqualTo(rooms.Count));
            for (var i = 0; i < rooms.Count; i++)
            {
                var run = new RoomRun(new PrecisionConfig(), rooms[i]);
                for (var tick = 0; tick < routes[i].Length && !run.IsCleared; tick++)
                {
                    run.Tick(routes[i].InputAt(tick));
                    Assert.That(run.Deaths, Is.Zero, $"{i + 1} 部屋目（{rooms[i].Name}）の {tick + 1} Tick 目で死亡");
                }

                Assert.That(run.IsCleared, Is.True, $"{i + 1} 部屋目（{rooms[i].Name}）を抜けられない");
            }
        }

        [Test]
        public void Pilot_ClimbsAllTenRoomsInARow_WithAFixedHash()
        {
            var climb = new Climb(new PrecisionConfig(), Rooms.All());
            var pilot = new RoutePilot(Routes.All());
            for (var i = 0; i < 20000 && !climb.IsWon; i++) { climb.Tick(pilot.Next(climb)); }
            Assert.That(climb.IsWon, Is.True, "10 部屋を連続で完走する");
            Assert.That(climb.Deaths, Is.Zero);
            Assert.That(climb.Hash(), Is.EqualTo(ExpectedClimbHash), $"ハッシュ 0x{climb.Hash():X8}（{climb.ElapsedTicks} Tick）");
        }

        [Test]
        public void Pilot_RejectsNull()
        {
            Assert.Throws<ArgumentNullException>(() => _ = new RoutePilot(null!));
            Assert.Throws<ArgumentException>(() => _ = new RoutePilot(new RouteScript[] { null! }));
            Assert.Throws<ArgumentNullException>(() => new RoutePilot(Routes.All()).Next(null!));
        }
    }
}
