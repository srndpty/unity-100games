using System;
using System.Collections.Generic;
using Game.Core;
using NUnit.Framework;
using TileMotion;

namespace Game.App.Tests
{
    public sealed class ExploreFlowTests
    {
        private const double Start = 1.0;
        private const double Tick = 1.0 / Units.TickRate;

        /// <summary>右へ歩くとセーブ像（列 3）に触れ、列 7 の歩く敵に当たる 1 部屋。</summary>
        private static readonly string[] _line = { "##########", "#........#", "#........#", "#S.$...w.#", "##########" };

        /// <summary>撃てばすぐ倒せるボスの 1 部屋。</summary>
        private static readonly string[] _arena = { "##########", "#........#", "#........#", "#S....M..#", "##########" };

        private static World Line() => new(new[] { Room.Parse("line", 0, 0, _line, 10, 5) });

        private static World Arena() => new(new[] { Room.Parse("arena", 0, 0, _arena, 10, 5) });

        private static ExploreFlow Create(Func<World>? world = null, ExplorerConfig? config = null) => new(config ?? new ExplorerConfig(), world ?? Line);

        private static ExploreFlow Playing(Func<World>? world = null, ExplorerConfig? config = null)
        {
            var flow = Create(world, config);
            flow.Apply(Intent.Of(IntentKind.Submit, Start));
            flow.TakeEvents();
            return flow;
        }

        /// <summary>時刻 <paramref name="from"/> から <paramref name="ticks"/> Tick ぶん、1 Tick ずつ時計を進める（Tick の区間の途中で止める）。</summary>
        private static FlowEvents Walk(ExploreFlow flow, double from, int ticks)
        {
            var events = FlowEvents.None;
            for (var i = 1; i <= ticks; i++)
            {
                flow.Advance(from + ((i + 0.5) * Tick));
                events |= flow.TakeEvents();
            }

            return events;
        }

        [Test]
        public void StartsAtTitle_TimeDoesNothing()
        {
            var flow = Create();
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Title));
            flow.Advance(100);
            flow.StepTick();
            Assert.That(flow.Expedition, Is.Null);
        }

        [Test]
        public void Title_Play_Down_Continue_FromTheStatue()
        {
            var config = new ExplorerConfig { MaxHealth = 1 };
            var flow = Create(config: config);
            flow.Apply(Intent.Of(IntentKind.Submit, Start));
            Assert.That(flow.TakeEvents(), Is.EqualTo(FlowEvents.Started));
            flow.Apply(Intent.Down(Control.Right, Start));
            var events = FlowEvents.None;
            for (var i = 1; i < 600 && flow.Screen == AppScreen.Playing; i++)
            {
                flow.Advance(Start + (i * Tick));
                events |= flow.TakeEvents();
            }

            Assert.That(flow.Screen, Is.EqualTo(AppScreen.GameOver));
            Assert.That(events & (FlowEvents.Saved | FlowEvents.Down), Is.EqualTo(FlowEvents.Saved | FlowEvents.Down));
            var hash = flow.Expedition!.Hash();
            flow.Advance(Start + 100);
            flow.StepTick();
            flow.Apply(Intent.Down(Control.Jump, Start + 101));
            Assert.That(flow.Expedition.Hash(), Is.EqualTo(hash), "ゲームオーバーの画面では試合が進まない");

            flow.Apply(Intent.Of(IntentKind.Submit, Start + 102));
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Playing));
            Assert.That(flow.TakeEvents() & FlowEvents.Continued, Is.EqualTo(FlowEvents.Continued));
            Assert.That(flow.Expedition.Deaths, Is.EqualTo(1), "同じ試合の続き");
            Assert.That(flow.Expedition.Player.Body, Is.EqualTo(Room.PlaceOnTile(new Cell(3, 1), Player.Width, Player.Height)), "像の前から");
        }

        [Test]
        public void Victory_RecordsTheTime_AndSubmitStartsANewGame()
        {
            var config = new ExplorerConfig { BossHealth = 1, BossIdleTicks = 200 };
            var flow = Playing(Arena, config);
            flow.Apply(Intent.Down(Control.Shoot, Start + (1.5 * Tick)));
            var events = Walk(flow, Start, 120);
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Victory));
            Assert.That(events & (FlowEvents.BossHit | FlowEvents.Won), Is.EqualTo(FlowEvents.BossHit | FlowEvents.Won));
            Assert.That(flow.ClearTicks, Is.EqualTo(flow.Expedition!.ElapsedTicks));
            var hash = flow.Expedition.Hash();
            flow.StepTick();
            Assert.That(flow.Expedition.Hash(), Is.EqualTo(hash), "勝った後は進まない");
            Assert.That(flow.TakeEvents(), Is.EqualTo(FlowEvents.None), "2 回目の勝利は起きない");

            flow.Apply(Intent.Of(IntentKind.Submit, Start + 10));
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Playing));
            Assert.That(flow.Expedition.ElapsedTicks, Is.Zero, "新しい試合");
            Assert.That(flow.ClearTicks, Is.Null);
        }

        [Test]
        public void Ticks_FollowTheClock_FromTheStartTime()
        {
            var flow = Playing();
            flow.Advance(Start + (0.5 * Tick));
            Assert.That(flow.Expedition!.ElapsedTicks, Is.Zero, "1 Tick ぶんの時間が経つまで進まない");
            flow.Advance(Start + (3.5 * Tick));
            Assert.That(flow.Expedition.ElapsedTicks, Is.EqualTo(3));
            flow.Advance(Start + (2 * Tick));
            Assert.That(flow.Expedition.ElapsedTicks, Is.EqualTo(3), "時刻が戻っても進まない");
        }

        [Test]
        public void Hitch_SharesTheBudget_AndCountsPlayedTime()
        {
            var flow = Playing();
            flow.Apply(Intent.Down(Control.Left, 2.0));
            flow.Apply(Intent.Up(Control.Left, 3.0));
            flow.Advance(Start + 10);
            Assert.That(flow.Expedition!.ElapsedTicks, Is.EqualTo(ExploreFlow.MaxTicksPerFrame), "入力と時計で合わせて上限まで");
            Assert.That(flow.PlayedTicks, Is.EqualTo(600), "遊んだ時間は実際に経った 10 秒");
            Walk(flow, Start + 10, 30);
            Assert.That(flow.Expedition.Player.VelocityX, Is.Zero, "飛ばした区間で離したキーは離したまま");
        }

        [Test]
        public void Input_AppliesFromTheTickContainingItsTimestamp()
        {
            var flow = Playing();
            flow.Apply(Intent.Down(Control.Right, Start + (2.5 * Tick)));
            Assert.That(flow.Expedition!.ElapsedTicks, Is.EqualTo(2), "押下より前の 2 Tick を先に進めた");
            Assert.That(flow.Expedition.Player.VelocityX, Is.Zero);
            flow.Advance(Start + (3.5 * Tick));
            Assert.That(flow.Expedition.Player.VelocityX, Is.EqualTo(new ExplorerConfig().GroundAccel), "3 Tick 目から右へ進む");
        }

        [Test]
        public void Pause_StopsTime_AndKeysAreNotCarried()
        {
            var flow = Playing();
            Walk(flow, Start, 10);
            flow.Apply(Intent.Down(Control.Right, Start + (10.6 * Tick)));
            flow.Apply(Intent.Of(IntentKind.TogglePause, Start + (10.7 * Tick)));
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Paused));
            flow.Advance(Start + 50);
            flow.StepTick();
            Assert.That(flow.Expedition!.ElapsedTicks, Is.EqualTo(10));
            Assert.That(flow.PlayedTicks, Is.EqualTo(10), "一時停止の間は数えない");
            flow.Apply(Intent.Down(Control.Left, Start + 50));
            flow.Apply(Intent.Of(IntentKind.Submit, Start + 50));
            Walk(flow, Start + 50, 30);
            Assert.That(flow.Expedition.ElapsedTicks, Is.EqualTo(40), "停止していた時間を取り戻さない");
            Assert.That(flow.Expedition.Player.VelocityX, Is.Zero, "停止の前後に押したキーは押し直すまで効かない");
        }

        [Test]
        public void RestartAndBackToTitle_OnlyFromPauseGameOverOrVictory()
        {
            var flow = Playing();
            Walk(flow, Start, 20);
            flow.Apply(Intent.Of(IntentKind.Restart, Start + (20.5 * Tick)));
            flow.Apply(Intent.Of(IntentKind.BackToTitle, Start + (20.5 * Tick)));
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Playing), "プレイ中は無視する");
            Assert.That(flow.Expedition!.ElapsedTicks, Is.EqualTo(20));

            flow.Apply(Intent.Of(IntentKind.TogglePause, Start + 1));
            flow.Apply(Intent.Of(IntentKind.Restart, Start + 1));
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Playing));
            Assert.That(flow.Expedition.ElapsedTicks, Is.Zero, "最初から");

            flow.Apply(Intent.Of(IntentKind.TogglePause, Start + 2));
            flow.Apply(Intent.Of(IntentKind.BackToTitle, Start + 2));
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Title));
            Assert.That(flow.Expedition, Is.Null);
            flow.Apply(Intent.Of(IntentKind.Quit, Start + 3));
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Title), "終了は Unity 層が行う");
        }

        [Test]
        public void GameOver_RestartBeginsANewGame()
        {
            var flow = Playing(config: new ExplorerConfig { MaxHealth = 1 });
            flow.Apply(Intent.Down(Control.Right, Start));
            for (var i = 1; i < 600 && flow.Screen == AppScreen.Playing; i++) { flow.Advance(Start + (i * Tick)); }
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.GameOver));
            flow.Apply(Intent.Of(IntentKind.TogglePause, Start + 20));
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.GameOver), "ゲームオーバーで一時停止はしない");
            flow.Apply(Intent.Of(IntentKind.Restart, Start + 20));
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Playing));
            Assert.That(flow.Expedition!.Deaths, Is.Zero, "新しい試合");
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
            Assert.That(flow.Expedition!.ElapsedTicks, Is.EqualTo(30));
            Assert.That(flow.Expedition.Player.VelocityX, Is.Zero, "フォーカスを失う前後のキーは持ち越さない");
        }

        [Test]
        public void Pilot_PlaysTheWholeGameThroughTheFlow()
        {
            var flow = new ExploreFlow(new ExplorerConfig()) { Pilot = Routes.Pilot() };
            flow.Apply(Intent.Of(IntentKind.Submit, Start));
            var events = FlowEvents.None;
            for (var i = 0; i < 20000 && flow.Screen == AppScreen.Playing; i++)
            {
                flow.StepTick();
                events |= flow.TakeEvents();
            }

            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Victory));
            Assert.That(flow.ClearTicks, Is.EqualTo(flow.Expedition!.ElapsedTicks), "StepTick で直接進めた Tick は 1 Tick ぶんの時間として数える");
            var expected = FlowEvents.DoubleJumped | FlowEvents.WallBroken | FlowEvents.GainedAbility | FlowEvents.Saved | FlowEvents.RoomChanged | FlowEvents.BossHit | FlowEvents.Won;
            Assert.That(events & expected, Is.EqualTo(expected));
        }

        [Test]
        public void FlowEvents_MirrorExpeditionEvents()
        {
            // Enum.GetValues<T> は Unity の Mono にないので、ビットの位置を順に調べる（未定義の値は名前が数字になる）。
            var mirrored = 0;
            for (var bit = 0; bit < 31; bit++)
            {
                var kind = (ExpeditionEvents)(1 << bit);
                if (char.IsDigit(kind.ToString()[0])) { continue; }
                mirrored++;
                Assert.That(((FlowEvents)((long)kind << 1)).ToString(), Is.EqualTo(kind.ToString()), $"{kind} に同じ名前の画面の出来事が対応する");
            }

            Assert.That(mirrored, Is.EqualTo(15));
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
                if (previous.Horizontal != input.Horizontal)
                {
                    if (previous.Horizontal != 0) { events.Add(Intent.Up(previous.Horizontal < 0 ? Control.Left : Control.Right, time)); }
                    if (input.Horizontal != 0) { events.Add(Intent.Down(input.Horizontal < 0 ? Control.Left : Control.Right, time)); }
                }

                if (previous.Jump != input.Jump) { events.Add(input.Jump ? Intent.Down(Control.Jump, time) : Intent.Up(Control.Jump, time)); }
                if (previous.Shoot != input.Shoot) { events.Add(input.Shoot ? Intent.Down(Control.Shoot, time) : Intent.Up(Control.Shoot, time)); }
                previous = input;
            }

            return events;
        }

        private static (Dictionary<long, uint> Trace, Expedition Expedition) Play(List<Intent> keys, IEnumerable<double> frames)
        {
            var flow = new ExploreFlow(new ExplorerConfig());
            flow.Apply(Intent.Of(IntentKind.Submit, Start));
            var trace = new Dictionary<long, uint>();
            var next = 0;
            foreach (var frame in frames)
            {
                while (next < keys.Count && keys[next].Time <= frame) { flow.Apply(keys[next++]); }
                flow.Advance(frame);
                trace[flow.Expedition!.ElapsedTicks] = flow.Expedition.Hash();
            }

            return (trace, flow.Expedition!);
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
                time += (1.0 / 240) + ((state % 1000) / 1000.0 * ((1.0 / 15) - (1.0 / 240)));
                yield return Math.Min(time, end);
            }
        }

        [Test]
        public void FrameRate_DoesNotChangeTheResult()
        {
            // 拠点 → 回廊 → 洞穴のルートを、Tick の区間の途中で起きたキー操作に直して届ける。
            var routes = Routes.All();
            var inputs = new List<PlayerInput>();
            inputs.AddRange(routes[0].Inputs);
            inputs.AddRange(routes[1].Inputs);

            // 列の後は何も押さない（最後のキーを押したままにすると、洞穴に着いた後で回廊へ戻る）。
            inputs.Add(PlayerInput.None);
            var keys = ToKeyEvents(inputs, Start, 0.37);
            var end = Start + ((inputs.Count + 30) * Tick);

            var reference = Play(keys, Fixed(60, end));
            Assert.That(reference.Expedition.RoomIndex, Is.EqualTo(Rooms.Cave), "キー操作に直したルートで洞穴に着く");
            var final = reference.Expedition.Hash();
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
                var (trace, expedition) = Play(keys, pair.Value);
                Assert.That(expedition.Hash(), Is.EqualTo(final), $"{pair.Key}：最終状態が 60fps と同じ");
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
        public void Constructor_RejectsInvalidArguments()
        {
            Assert.Throws<ArgumentNullException>(() => _ = new ExploreFlow(null!));
            Assert.Throws<ArgumentOutOfRangeException>(() => _ = new ExploreFlow(new ExplorerConfig { Gravity = 0 }));
        }

        [Test]
        public void Intent_RejectsUndefinedAndNonFinite()
        {
            Assert.Throws<ArgumentException>(() => Intent.Of(IntentKind.ControlDown, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => Intent.Of((IntentKind)99, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => Intent.Down((Control)99, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => Intent.Of(IntentKind.Submit, double.NaN));
            Assert.Throws<ArgumentOutOfRangeException>(() => Intent.Down(Control.Jump, double.PositiveInfinity));
        }

        [Test]
        public void HeldControls_LatestDirectionWins_AndShortPressesCount()
        {
            var held = new HeldControls();
            held.Press(Control.Left);
            held.Press(Control.Right);
            Assert.That(held.Horizontal, Is.EqualTo(1), "後から押したほう");
            held.Release(Control.Right);
            Assert.That(held.Horizontal, Is.EqualTo(-1), "離すと残っているほうに戻る");
            held.Press(Control.Jump);
            held.Release(Control.Jump);
            held.Press(Control.Shoot);
            held.Release(Control.Shoot);
            Assert.That(held.TakeTickInput(), Is.EqualTo(new PlayerInput(-1, true, true)), "Tick の間に押して離しても 1 Tick は押したことにする");
            Assert.That(held.TakeTickInput(), Is.EqualTo(new PlayerInput(-1, false, false)));
            held.Clear();
            Assert.That(held.TakeTickInput(), Is.EqualTo(PlayerInput.None));
        }
    }
}
