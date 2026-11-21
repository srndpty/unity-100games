using System;
using System.Collections.Generic;
using Game.Core;
using NUnit.Framework;

namespace Game.App.Tests
{
    public sealed class PrecisionFlowTests
    {
        private const double Start = 1.0;
        private const double Tick = 1.0 / Units.TickRate;

        private static readonly string[] _corridor = { "......", "S...E.", "######" };
        private static readonly string[] _pit = { "E.....", "S.....", "#....." };

        private static Room Corridor() => Room.Parse("corridor", _corridor, 6, 3);

        private static Room Pit() => Room.Parse("pit", _pit, 6, 3);

        private static PrecisionFlow Create(params Room[] rooms) => new(new PrecisionConfig(), rooms.Length == 0 ? new[] { Corridor(), Corridor() } : rooms);

        private static PrecisionFlow Playing(params Room[] rooms)
        {
            var flow = Create(rooms);
            flow.Apply(Intent.Of(IntentKind.Submit, Start));
            flow.TakeEvents();
            return flow;
        }

        /// <summary>時刻 <paramref name="from"/> から <paramref name="ticks"/> Tick ぶん、1 Tick ずつ時計を進める（Tick の区間の途中で止める）。</summary>
        private static void Walk(PrecisionFlow flow, double from, int ticks)
        {
            for (var i = 1; i <= ticks; i++) { flow.Advance(from + ((i + 0.5) * Tick)); }
        }

        [Test]
        public void StartsAtTitle_TimeDoesNothing()
        {
            var flow = Create();
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Title));
            flow.Advance(100);
            flow.StepTick();
            Assert.That(flow.Climb, Is.Null);
        }

        [Test]
        public void Title_Play_Win_Result_Replay()
        {
            var flow = Create();
            flow.Apply(Intent.Of(IntentKind.Submit, Start));
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Playing));
            Assert.That(flow.TakeEvents(), Is.EqualTo(FlowEvents.Started));

            flow.Apply(Intent.Down(Control.Right, Start));
            var events = FlowEvents.None;
            for (var i = 1; i < 600 && flow.Screen == AppScreen.Playing; i++)
            {
                flow.Advance(Start + (i * Tick));
                events |= flow.TakeEvents();
            }

            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Result));
            Assert.That(events & (FlowEvents.RoomCleared | FlowEvents.Won), Is.EqualTo(FlowEvents.RoomCleared | FlowEvents.Won));
            Assert.That(flow.BestTicks, Is.EqualTo(flow.Climb!.ElapsedTicks));
            Assert.That(flow.IsNewBest, Is.True);

            var hash = flow.Climb.Hash();
            flow.Advance(Start + 100);
            flow.StepTick();
            flow.Apply(Intent.Down(Control.Jump, Start + 101));
            Assert.That(flow.Climb.Hash(), Is.EqualTo(hash), "結果画面では試合が進まない");
            Assert.That(flow.TakeEvents(), Is.EqualTo(FlowEvents.None), "2 回目の勝利は起きない");

            flow.Apply(Intent.Of(IntentKind.Submit, Start + 102));
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Playing));
            Assert.That(flow.Climb.ElapsedTicks, Is.Zero, "新しい挑戦");
            Assert.That(flow.IsNewBest, Is.False);
        }

        [Test]
        public void SlowerRun_DoesNotReplaceBest()
        {
            var flow = Playing();
            flow.Pilot = new RoutePilot(new[] { RouteScript.Parse("R600"), RouteScript.Parse("R600") });
            while (flow.Screen == AppScreen.Playing) { flow.StepTick(); }
            var best = flow.BestTicks;
            flow.Apply(Intent.Of(IntentKind.Submit, Start));
            flow.Pilot = new RoutePilot(new[] { RouteScript.Parse("-30 R600"), RouteScript.Parse("R600") });
            while (flow.Screen == AppScreen.Playing) { flow.StepTick(); }
            Assert.That(flow.IsNewBest, Is.False);
            Assert.That(flow.BestTicks, Is.EqualTo(best));
        }

        [Test]
        public void Ticks_FollowTheClock_FromTheStartTime()
        {
            var flow = Playing();
            flow.Advance(Start + (0.5 * Tick));
            Assert.That(flow.Climb!.ElapsedTicks, Is.Zero, "1 Tick ぶんの時間が経つまで進まない");
            flow.Advance(Start + (3.5 * Tick));
            Assert.That(flow.Climb.ElapsedTicks, Is.EqualTo(3));
            flow.Advance(Start + (2 * Tick));
            Assert.That(flow.Climb.ElapsedTicks, Is.EqualTo(3), "時刻が戻っても進まない");
        }

        [Test]
        public void Hitch_SkipsTimeBeyondMaxTicksPerFrame()
        {
            var flow = Playing();
            flow.Advance(Start + 10);
            Assert.That(flow.Climb!.ElapsedTicks, Is.EqualTo(PrecisionFlow.MaxTicksPerFrame), "処理落ちの後に一度に進めるのは上限まで");
            flow.Advance(Start + 10 + (1.5 * Tick));
            Assert.That(flow.Climb.ElapsedTicks, Is.EqualTo(PrecisionFlow.MaxTicksPerFrame + 1), "飛ばした後は時計に沿って進む");
        }

        [Test]
        public void Hitch_InputsAndAdvanceInOneFrameShareTheTickBudget()
        {
            // レビュー P2-1 の例：処理停止の後、1 フレームで 2 つの入力と Advance を処理する。
            var flow = Playing();
            flow.Apply(Intent.Down(Control.Right, 2.0));
            flow.Apply(Intent.Up(Control.Right, 3.0));
            flow.Advance(4.0);
            Assert.That(flow.Climb!.ElapsedTicks, Is.EqualTo(PrecisionFlow.MaxTicksPerFrame), "3 回の呼び出しで合わせて上限まで（以前は 15 Tick 進んだ）");

            // 飛ばした区間の押下・解放も順に反映され、右は離した状態になっている。
            Walk(flow, 4.0, 30);
            Assert.That(flow.Climb.Run.Player.VelocityX, Is.Zero, "離したキーで動き続けない");
            Assert.That(flow.Climb.Run.Player.Body.X, Is.EqualTo(flow.Climb.Run.Room.StartBox.X));
        }

        [Test]
        public void Hitch_KeyPressedDuringSkippedTimeIsStillHeld()
        {
            var flow = Playing();
            flow.Apply(Intent.Down(Control.Left, 1.5));
            flow.Apply(Intent.Up(Control.Left, 1.6));
            flow.Apply(Intent.Down(Control.Right, 2.0));
            flow.Advance(4.0);
            Assert.That(flow.Climb!.ElapsedTicks, Is.EqualTo(PrecisionFlow.MaxTicksPerFrame));
            Walk(flow, 4.0, 10);
            Assert.That(flow.Climb.Run.Player.VelocityX, Is.GreaterThan(0), "飛ばした区間で押した右は押したままになっている");
        }

        [Test]
        public void Hitch_ManyInputsInOneFrameNeverExceedTheBudget()
        {
            var flow = Playing();
            var events = 0;
            for (var i = 0; i < 40; i++)
            {
                var time = Start + 1 + (i * 0.5);
                flow.Apply(i % 2 == 0 ? Intent.Down(Control.Right, time) : Intent.Up(Control.Right, time));
                events++;
            }

            Assert.That(flow.FrameBudget, Is.Zero);
            flow.Advance(Start + 30);
            Assert.That(flow.Climb!.ElapsedTicks, Is.EqualTo(PrecisionFlow.MaxTicksPerFrame), $"{events} 個の入力をためても 1 フレームの上限を超えない");
            Assert.That(flow.FrameBudget, Is.EqualTo(PrecisionFlow.MaxTicksPerFrame), "Advance でフレームを終え、上限を数え直す");
        }

        [Test]
        public void EndFrame_ResetsTheBudgetWithoutAdvancing()
        {
            var flow = Playing();
            flow.Apply(Intent.Down(Control.Right, Start + 10));
            Assert.That(flow.FrameBudget, Is.Zero);
            flow.EndFrame();
            Assert.That(flow.FrameBudget, Is.EqualTo(PrecisionFlow.MaxTicksPerFrame));
            Assert.That(flow.Climb!.ElapsedTicks, Is.EqualTo(PrecisionFlow.MaxTicksPerFrame));
        }

        [Test]
        public void Input_AppliesFromTheTickContainingItsTimestamp()
        {
            // 時刻 τ の押下は τ を含む Tick から効く。それより前の Tick は先に進める。
            var flow = Playing();
            flow.Apply(Intent.Down(Control.Right, Start + (2.5 * Tick)));
            Assert.That(flow.Climb!.ElapsedTicks, Is.EqualTo(2), "押下より前の 2 Tick を先に進めた");
            Assert.That(flow.Climb.Run.Player.VelocityX, Is.Zero);
            flow.Advance(Start + (3.5 * Tick));
            Assert.That(flow.Climb.Run.Player.VelocityX, Is.EqualTo(new PrecisionConfig().GroundAccel), "3 Tick 目から右へ進む");
        }

        /// <summary>各 Tick の入力の列を、時刻付きのキーの押下・解放に直す。Tick n の入力は Tick n の区間の途中で起きる。</summary>
        private static List<Intent> ToKeyEvents(List<PlayerInput> inputs, double start, double offset)
        {
            var events = new List<Intent>();
            var previous = PlayerInput.None;
            for (var n = 1; n <= inputs.Count; n++)
            {
                var input = inputs[n - 1];
                var time = start + ((n - 1 + offset) * Tick);
                void Axis(int before, int after, Control negative, Control positive)
                {
                    if (before == after) { return; }
                    if (before != 0) { events.Add(Intent.Up(before < 0 ? negative : positive, time)); }
                    if (after != 0) { events.Add(Intent.Down(after < 0 ? negative : positive, time)); }
                }

                void Button(bool before, bool after, Control control)
                {
                    if (before == after) { return; }
                    events.Add(after ? Intent.Down(control, time) : Intent.Up(control, time));
                }

                Axis(previous.Horizontal, input.Horizontal, Control.Left, Control.Right);
                Axis(previous.Vertical, input.Vertical, Control.Down, Control.Up);
                Button(previous.Jump, input.Jump, Control.Jump);
                Button(previous.Dash, input.Dash, Control.Dash);
                previous = input;
            }

            return events;
        }

        /// <summary>フレームの時刻の列で、キーの押下・解放を届けながら進める。各フレームの後の (経過 Tick, ハッシュ) を記録する。</summary>
        private static (Dictionary<long, uint> Trace, Climb Climb) Play(List<Intent> keys, IEnumerable<double> frames)
        {
            var flow = new PrecisionFlow(new PrecisionConfig(), Rooms.All());
            flow.Apply(Intent.Of(IntentKind.Submit, Start));
            var trace = new Dictionary<long, uint>();
            var next = 0;
            foreach (var frame in frames)
            {
                // そのフレームまでに起きた入力が、フレームの始めにまとめて届く。
                while (next < keys.Count && keys[next].Time <= frame) { flow.Apply(keys[next++]); }
                flow.Advance(frame);
                trace[flow.Climb!.ElapsedTicks] = flow.Climb.Hash();
            }

            return (trace, flow.Climb!);
        }

        private static IEnumerable<double> Fixed(double fps, double end)
        {
            for (var k = 1; Start + (k / fps) <= end; k++) { yield return Start + (k / fps); }
            yield return end;
        }

        private static IEnumerable<double> Irregular(uint seed, double end)
        {
            var time = Start;
            var state = seed;
            while (time < end)
            {
                state ^= state << 13;
                state ^= state >> 17;
                state ^= state << 5;

                // 1/240〜1/15 秒の不規則な間隔（処理落ちで時間を捨てる上限より短い）。
                time += (1.0 / 240) + ((state % 1000) / 1000.0 * ((1.0 / 15) - (1.0 / 240)));
                yield return Math.Min(time, end);
            }
        }

        [Test]
        public void FrameRate_DoesNotChangeTheResult()
        {
            // 1・2 部屋目のルートを、Tick の区間の途中で起きたキー操作に直して届ける。
            var routes = Routes.All();
            var inputs = new List<PlayerInput>();
            inputs.AddRange(routes[0].Inputs);
            inputs.AddRange(routes[1].Inputs);
            var keys = ToKeyEvents(inputs, Start, 0.37);
            var end = Start + ((inputs.Count + 30) * Tick);

            var reference = Play(keys, Fixed(60, end));
            Assert.That(reference.Climb.RoomIndex, Is.EqualTo(2), "キー操作に直したルートで 2 部屋を抜ける");
            Assert.That(reference.Climb.Deaths, Is.Zero);
            var final = reference.Climb.Hash();

            var schedules = new Dictionary<string, IEnumerable<double>>
            {
                ["30fps"] = Fixed(30, end),
                ["144fps"] = Fixed(144, end),
                ["240fps"] = Fixed(240, end),
                ["不規則1"] = Irregular(12345, end),
                ["不規則2"] = Irregular(987654321, end),
            };
            foreach (var pair in schedules)
            {
                var (trace, climb) = Play(keys, pair.Value);
                Assert.That(climb.Hash(), Is.EqualTo(final), $"{pair.Key}：最終状態が 60fps と同じ");
                var compared = 0;
                foreach (var tick in trace)
                {
                    if (reference.Trace.TryGetValue(tick.Key, out var expected))
                    {
                        Assert.That(tick.Value, Is.EqualTo(expected), $"{pair.Key}：{tick.Key} Tick 目の状態");
                        compared++;
                    }
                }

                Assert.That(compared, Is.GreaterThan(50), $"{pair.Key}：途中の状態も比べた");
            }
        }

        [Test]
        public void Pause_StopsTime_ResumeRestartsTheClockWithoutCatchingUp()
        {
            var flow = Playing();
            Walk(flow, Start, 10);
            flow.Apply(Intent.Of(IntentKind.TogglePause, Start + (10.7 * Tick)));
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Paused));
            flow.Advance(Start + 50);
            flow.StepTick();
            Assert.That(flow.Climb!.ElapsedTicks, Is.EqualTo(10));

            flow.Apply(Intent.Of(IntentKind.Submit, Start + 50));
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Playing));
            Walk(flow, Start + 50, 3);
            Assert.That(flow.Climb.ElapsedTicks, Is.EqualTo(13), "停止していた時間を取り戻さない");
            flow.Apply(Intent.Of(IntentKind.TogglePause, Start + 51));
            flow.Apply(Intent.Of(IntentKind.TogglePause, Start + 52));
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Playing), "停止キーでも再開できる");
        }

        [Test]
        public void KeysPressedWhilePausedAreNotCarriedIntoPlay()
        {
            var flow = Playing();
            flow.Apply(Intent.Down(Control.Right, Start));
            flow.Apply(Intent.Of(IntentKind.TogglePause, Start));
            flow.Apply(Intent.Down(Control.Left, Start));
            flow.Apply(Intent.Of(IntentKind.Submit, Start));
            Walk(flow, Start, 30);
            Assert.That(flow.Climb!.Run.Player.VelocityX, Is.Zero, "停止の前後に押したキーは押し直すまで効かない");
        }

        [Test]
        public void RestartAndBackToTitle_OnlyFromPauseOrResult()
        {
            var flow = Playing();
            Walk(flow, Start, 20);
            flow.Apply(Intent.Of(IntentKind.Restart, Start + (20.5 * Tick)));
            flow.Apply(Intent.Of(IntentKind.BackToTitle, Start + (20.5 * Tick)));
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Playing), "プレイ中は無視する");
            Assert.That(flow.Climb!.ElapsedTicks, Is.EqualTo(20));

            flow.Apply(Intent.Of(IntentKind.TogglePause, Start + 1));
            flow.Apply(Intent.Of(IntentKind.Restart, Start + 1));
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Playing));
            Assert.That(flow.Climb.ElapsedTicks, Is.Zero, "1 部屋目から始め直す");

            flow.Apply(Intent.Of(IntentKind.TogglePause, Start + 2));
            flow.Apply(Intent.Of(IntentKind.BackToTitle, Start + 2));
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Title));
            Assert.That(flow.Climb, Is.Null);
            flow.Apply(Intent.Of(IntentKind.Quit, Start + 3));
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Title), "終了は Unity 層が行う");
        }

        [Test]
        public void FocusLoss_PausesAndIgnoresInputUntilRefocusedAndResumed([Values(true, false)] bool keyBeforeFocusLoss)
        {
            var flow = Playing();
            if (keyBeforeFocusLoss) { flow.Apply(Intent.Down(Control.Right, Start)); }
            flow.SetFocus(false);
            if (!keyBeforeFocusLoss) { flow.Apply(Intent.Down(Control.Right, Start)); }
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Paused));
            flow.Apply(Intent.Of(IntentKind.Submit, Start + 1));
            flow.Advance(Start + 2);
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Paused), "フォーカスがない間の操作は受け付けない");

            flow.SetFocus(true);
            flow.Advance(Start + 3);
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Paused), "戻っても自動では再開しない");
            flow.Apply(Intent.Of(IntentKind.Submit, Start + 3));
            Walk(flow, Start + 3, 30);
            Assert.That(flow.Climb!.ElapsedTicks, Is.EqualTo(30));
            Assert.That(flow.Climb.Run.Player.VelocityX, Is.Zero, "フォーカスを失う前後のキーは持ち越さない");
        }

        [Test]
        public void Deaths_AreReportedAndCounted()
        {
            var flow = Playing(Pit());
            flow.Apply(Intent.Down(Control.Right, Start));
            var events = FlowEvents.None;
            for (var i = 1; i < 300; i++)
            {
                flow.Advance(Start + (i * Tick));
                events |= flow.TakeEvents();
            }

            Assert.That(events & (FlowEvents.Died | FlowEvents.Respawned), Is.EqualTo(FlowEvents.Died | FlowEvents.Respawned));
            Assert.That(flow.Climb!.Deaths, Is.GreaterThanOrEqualTo(2), "残機はなく、押し続ければ何度でも落ちる");
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Playing));
        }

        [Test]
        public void Pilot_ClimbsAllRoomsThroughTheFlow()
        {
            var flow = new PrecisionFlow(new PrecisionConfig(), Rooms.All()) { Pilot = new RoutePilot(Routes.All()) };
            flow.Apply(Intent.Of(IntentKind.Submit, Start));
            var events = FlowEvents.None;
            for (var i = 0; i < 20000 && flow.Screen == AppScreen.Playing; i++)
            {
                flow.StepTick();
                events |= flow.TakeEvents();
            }

            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Result));
            Assert.That(flow.Climb!.Deaths, Is.Zero);
            Assert.That(events & (FlowEvents.Jumped | FlowEvents.WallJumped | FlowEvents.Dashed | FlowEvents.Won), Is.EqualTo(FlowEvents.Jumped | FlowEvents.WallJumped | FlowEvents.Dashed | FlowEvents.Won));
        }

        [Test]
        public void Constructor_RejectsInvalidArguments()
        {
            Assert.Throws<ArgumentNullException>(() => _ = new PrecisionFlow(null!, new[] { Corridor() }));
            Assert.Throws<ArgumentNullException>(() => _ = new PrecisionFlow(new PrecisionConfig(), null!));
            Assert.Throws<ArgumentException>(() => _ = new PrecisionFlow(new PrecisionConfig(), Array.Empty<Room>()));
            Assert.Throws<ArgumentOutOfRangeException>(() => _ = new PrecisionFlow(new PrecisionConfig { DeathTicks = 0 }, new[] { Corridor() }));
        }
    }
}
