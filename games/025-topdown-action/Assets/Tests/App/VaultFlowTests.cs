using System;
using System.Collections.Generic;
using Game.Core;
using NUnit.Framework;
using TileMotion;

namespace Game.App.Tests
{
    public sealed class VaultFlowTests
    {
        private const double Start = 1.0;
        private const double Tick = 1.0 / Units.TickRate;

        /// <summary>開始位置の右に射手がいる 1 部屋（体力 1 なら撃たれて倒れる）。</summary>
        private static readonly string[] _line = { "#########", "#.......#", "#S..a...#", "#.......#", "#########" };

        /// <summary>開始位置の右 2 マスにボスがいる 1 部屋。</summary>
        private static readonly string[] _arena = { "#######", "#.....#", "#S.M..#", "#.....#", "#######" };

        /// <summary>何もいない 1 部屋。</summary>
        private static readonly string[] _empty = { "###########", "#.........#", "#....S....#", "#.........#", "###########" };

        private static World Line() => new(new[] { Room.Parse("line", 0, 0, _line, 9, 5) });

        private static World Arena() => new(new[] { Room.Parse("arena", 0, 0, _arena, 7, 5) });

        private static World Empty() => new(new[] { Room.Parse("empty", 0, 0, _empty, 11, 5) });

        private static VaultFlow Create(Func<World>? world = null, VaultConfig? config = null, int seed = 1) => new(config ?? new VaultConfig(), seed, world ?? Empty);

        private static VaultFlow Playing(Func<World>? world = null, VaultConfig? config = null)
        {
            var flow = Create(world, config);
            flow.Apply(Intent.Of(IntentKind.Submit, Start));
            flow.TakeEvents();
            return flow;
        }

        /// <summary>時刻 <paramref name="from"/> から <paramref name="ticks"/> Tick ぶん、1 Tick ずつ時計を進める（Tick の区間の途中で止める）。</summary>
        private static FlowEvents Walk(VaultFlow flow, double from, int ticks)
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
            Assert.That(flow.Dungeon, Is.Null);
        }

        [Test]
        public void Title_Play_Down_Continue_FromTheEntrance()
        {
            var config = new VaultConfig { MaxHealth = 1 };
            var flow = Create(Line, config);
            flow.Apply(Intent.Of(IntentKind.Submit, Start));
            Assert.That(flow.TakeEvents(), Is.EqualTo(FlowEvents.Started));
            var events = Walk(flow, Start, 600);
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.GameOver));
            Assert.That(events & FlowEvents.Down, Is.EqualTo(FlowEvents.Down));
            var hash = flow.Dungeon!.Hash();
            flow.Advance(Start + 100);
            flow.StepTick();
            flow.Apply(Intent.Down(Control.Attack, Start + 101));
            Assert.That(flow.Dungeon.Hash(), Is.EqualTo(hash), "ゲームオーバーの画面では試合が進まない");

            flow.Apply(Intent.Of(IntentKind.Submit, Start + 102));
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Playing));
            Assert.That(flow.TakeEvents() & FlowEvents.Continued, Is.EqualTo(FlowEvents.Continued));
            Assert.That(flow.Dungeon.Deaths, Is.EqualTo(1), "同じ試合の続き");
            Assert.That(flow.Dungeon.Hero.Body, Is.EqualTo(new Cell(1, 2).Centered(Hero.Size)), "入口の開始位置から");
            Assert.That(flow.Dungeon.Hero.Health, Is.EqualTo(1));
        }

        [Test]
        public void Victory_RecordsTheTime_AndSubmitStartsANewGameWithANewSeed()
        {
            var flow = Playing(Arena, new VaultConfig { BossHealth = 1 });
            var firstSeed = flow.Dungeon!.Seed;
            flow.Apply(Intent.Down(Control.Right, Start + (0.5 * Tick)));
            flow.Apply(Intent.Up(Control.Right, Start + (1.5 * Tick)));
            flow.Apply(Intent.Down(Control.Attack, Start + (2.5 * Tick)));
            flow.Apply(Intent.Up(Control.Attack, Start + (3.5 * Tick)));
            var events = Walk(flow, Start, 20);
            Assert.That(events & FlowEvents.BossDefeated, Is.EqualTo(FlowEvents.BossDefeated));
            flow.Apply(Intent.Down(Control.Right, Start + (21.5 * Tick)));
            events = Walk(flow, Start + (21 * Tick), 120);
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Victory));
            Assert.That(events & FlowEvents.Won, Is.EqualTo(FlowEvents.Won));
            Assert.That(flow.ClearTicks, Is.EqualTo(flow.Dungeon.ElapsedTicks));
            var hash = flow.Dungeon.Hash();
            flow.StepTick();
            Assert.That(flow.Dungeon.Hash(), Is.EqualTo(hash), "勝った後は進まない");
            Assert.That(flow.TakeEvents(), Is.EqualTo(FlowEvents.None), "2 回目の勝利は起きない");

            flow.Apply(Intent.Of(IntentKind.Submit, Start + 10));
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Playing));
            Assert.That(flow.Dungeon.ElapsedTicks, Is.Zero, "新しい試合");
            Assert.That(flow.Dungeon.Seed, Is.EqualTo(firstSeed + 1), "再戦は新しいシード");
            Assert.That(flow.ClearTicks, Is.Null);
        }

        [Test]
        public void Ticks_FollowTheClock_FromTheStartTime()
        {
            var flow = Playing();
            flow.Advance(Start + (0.5 * Tick));
            Assert.That(flow.Dungeon!.ElapsedTicks, Is.Zero, "1 Tick ぶんの時間が経つまで進まない");
            flow.Advance(Start + (3.5 * Tick));
            Assert.That(flow.Dungeon.ElapsedTicks, Is.EqualTo(3));
            flow.Advance(Start + (2 * Tick));
            Assert.That(flow.Dungeon.ElapsedTicks, Is.EqualTo(3), "時刻が戻っても進まない");
        }

        [Test]
        public void Hitch_SharesTheBudget_AndCountsPlayedTime()
        {
            var flow = Playing();
            var x = flow.Dungeon!.Hero.Body.X;
            flow.Apply(Intent.Down(Control.Left, 2.0));
            flow.Apply(Intent.Up(Control.Left, 3.0));
            flow.Advance(Start + 10);
            Assert.That(flow.Dungeon.ElapsedTicks, Is.EqualTo(VaultFlow.MaxTicksPerFrame), "入力と時計で合わせて上限まで");
            Assert.That(flow.PlayedTicks, Is.EqualTo(600), "遊んだ時間は実際に経った 10 秒");
            var after = flow.Dungeon.Hero.Body.X;
            Walk(flow, Start + 10, 30);
            Assert.That(flow.Dungeon.Hero.Body.X, Is.EqualTo(after), "飛ばした区間で離したキーは離したまま");
            Assert.That(after, Is.EqualTo(x), "押していた区間はすべて飛ばした");
        }

        [Test]
        public void Input_AppliesFromTheTickContainingItsTimestamp()
        {
            var flow = Playing();
            var x = flow.Dungeon!.Hero.Body.X;
            flow.Apply(Intent.Down(Control.Right, Start + (2.5 * Tick)));
            Assert.That(flow.Dungeon.ElapsedTicks, Is.EqualTo(2), "押下より前の 2 Tick を先に進めた");
            Assert.That(flow.Dungeon.Hero.Body.X, Is.EqualTo(x));
            flow.Advance(Start + (3.5 * Tick));
            Assert.That(flow.Dungeon.Hero.Body.X, Is.EqualTo(x + new VaultConfig().HeroSpeed), "3 Tick 目から右へ進む");
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
            Assert.That(flow.Dungeon!.ElapsedTicks, Is.EqualTo(10));
            Assert.That(flow.PlayedTicks, Is.EqualTo(10), "一時停止の間は数えない");
            var x = flow.Dungeon.Hero.Body.X;
            flow.Apply(Intent.Down(Control.Left, Start + 50));
            flow.Apply(Intent.Of(IntentKind.Submit, Start + 50));
            Walk(flow, Start + 50, 30);
            Assert.That(flow.Dungeon.ElapsedTicks, Is.EqualTo(40), "停止していた時間を取り戻さない");
            Assert.That(flow.Dungeon.Hero.Body.X, Is.EqualTo(x), "停止の前後に押したキーは押し直すまで効かない");
        }

        [Test]
        public void RestartAndBackToTitle_OnlyFromPauseGameOverOrVictory()
        {
            var flow = Playing();
            Walk(flow, Start, 20);
            flow.Apply(Intent.Of(IntentKind.Restart, Start + (20.5 * Tick)));
            flow.Apply(Intent.Of(IntentKind.BackToTitle, Start + (20.5 * Tick)));
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Playing), "プレイ中は無視する");
            Assert.That(flow.Dungeon!.ElapsedTicks, Is.EqualTo(20));

            flow.Apply(Intent.Of(IntentKind.TogglePause, Start + 1));
            flow.Apply(Intent.Of(IntentKind.Restart, Start + 1));
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Playing));
            Assert.That(flow.Dungeon.ElapsedTicks, Is.Zero, "最初から");

            flow.Apply(Intent.Of(IntentKind.TogglePause, Start + 2));
            flow.Apply(Intent.Of(IntentKind.BackToTitle, Start + 2));
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Title));
            Assert.That(flow.Dungeon, Is.Null);
            flow.Apply(Intent.Of(IntentKind.Quit, Start + 3));
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Title), "終了は Unity 層が行う");
        }

        [Test]
        public void GameOver_RestartBeginsANewGame()
        {
            var flow = Playing(Line, new VaultConfig { MaxHealth = 1 });
            Walk(flow, Start, 600);
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.GameOver));
            flow.Apply(Intent.Of(IntentKind.TogglePause, Start + 20));
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.GameOver), "ゲームオーバーで一時停止はしない");
            flow.Apply(Intent.Of(IntentKind.Restart, Start + 20));
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Playing));
            Assert.That(flow.Dungeon!.Deaths, Is.Zero, "新しい試合");
        }

        [Test]
        public void FocusLoss_PausesAndIgnoresInputUntilRefocusedAndResumed([Values(true, false)] bool keyBeforeFocusLoss)
        {
            var flow = Playing();
            var x = flow.Dungeon!.Hero.Body.X;
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
            Assert.That(flow.Dungeon.ElapsedTicks, Is.EqualTo(30));
            Assert.That(flow.Dungeon.Hero.Body.X, Is.EqualTo(x), "フォーカスを失う前後のキーは持ち越さない");
        }

        [Test]
        public void Pilot_PlaysTheWholeGameThroughTheFlow()
        {
            var flow = new VaultFlow(new VaultConfig(), 1) { Pilot = new Autopilot() };
            flow.Apply(Intent.Of(IntentKind.Submit, Start));
            var events = FlowEvents.None;
            for (var i = 0; i < AutopilotTests.Limit && flow.Screen != AppScreen.Victory; i++)
            {
                if (flow.Screen == AppScreen.GameOver) { flow.Apply(Intent.Of(IntentKind.Submit, Start)); }
                flow.StepTick();
                events |= flow.TakeEvents();
            }

            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Victory));
            Assert.That(flow.ClearTicks, Is.EqualTo(flow.Dungeon!.ElapsedTicks), "StepTick で直接進めた Tick は 1 Tick ぶんの時間として数える");
            var expected = FlowEvents.Swung | FlowEvents.EnemyDefeated | FlowEvents.ChestOpened | FlowEvents.RewardShown | FlowEvents.KeyTaken
                | FlowEvents.DoorOpened | FlowEvents.RoomChanged | FlowEvents.Arrived | FlowEvents.BossHit | FlowEvents.BossDefeated | FlowEvents.Won;
            Assert.That(events & expected, Is.EqualTo(expected));
        }

        [Test]
        public void FlowEvents_MirrorDungeonEvents()
        {
            // Enum.GetValues<T> は Unity の Mono にないので、ビットの位置を順に調べる（未定義の値は名前が数字になる）。
            var mirrored = 0;
            for (var bit = 0; bit < 31; bit++)
            {
                var kind = (DungeonEvents)(1 << bit);
                if (char.IsDigit(kind.ToString()[0])) { continue; }
                mirrored++;
                Assert.That(((FlowEvents)((long)kind << 1)).ToString(), Is.EqualTo(kind.ToString()), $"{kind} に同じ名前の画面の出来事が対応する");
            }

            Assert.That(mirrored, Is.EqualTo(17));
        }

        /// <summary>各 Tick の入力の列を、時刻付きのキーの押下・解放に直す。Tick n の入力は Tick n の区間の途中で起きる。</summary>
        private static List<Intent> ToKeyEvents(List<HeroInput> inputs, double start, double offset)
        {
            var events = new List<Intent>();
            var previous = HeroInput.None;
            for (var n = 1; n <= inputs.Count; n++)
            {
                var input = inputs[n - 1];
                var time = start + ((n - 1 + offset) * Tick);
                Axis(previous.X, input.X, Control.Left, Control.Right);
                Axis(previous.Y, input.Y, Control.Down, Control.Up);
                if (previous.Attack != input.Attack) { events.Add(input.Attack ? Intent.Down(Control.Attack, time) : Intent.Up(Control.Attack, time)); }
                if (previous.Roll != input.Roll) { events.Add(input.Roll ? Intent.Down(Control.Roll, time) : Intent.Up(Control.Roll, time)); }
                previous = input;

                void Axis(int before, int after, Control negative, Control positive)
                {
                    if (before == after) { return; }
                    if (before != 0) { events.Add(Intent.Up(before < 0 ? negative : positive, time)); }
                    if (after != 0) { events.Add(Intent.Down(after < 0 ? negative : positive, time)); }
                }
            }

            return events;
        }

        private static (Dictionary<long, uint> Trace, Dungeon Dungeon) Play(List<Intent> keys, IEnumerable<double> frames)
        {
            var flow = new VaultFlow(new VaultConfig(), 5);
            flow.Apply(Intent.Of(IntentKind.Submit, Start));
            var trace = new Dictionary<long, uint>();
            var next = 0;
            foreach (var frame in frames)
            {
                while (next < keys.Count && keys[next].Time <= frame) { flow.Apply(keys[next++]); }
                flow.Advance(frame);
                trace[flow.Dungeon!.ElapsedTicks] = flow.Dungeon.Hash();
            }

            return (trace, flow.Dungeon!);
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
            // 操縦役で 1500 Tick 遊んだ入力の列（東の間の宝箱 → 西の回廊の戦い）を、Tick の区間の途中で起きたキー操作に直して届ける。
            var dungeon = new Dungeon(new VaultConfig(), Rooms.Create(), 5);
            var pilot = new Autopilot();
            var inputs = new List<HeroInput>();
            for (var i = 0; i < 1500; i++)
            {
                var input = pilot.InputFor(dungeon);
                inputs.Add(input);
                dungeon.Tick(input);
            }

            inputs.Add(HeroInput.None);
            var keys = ToKeyEvents(inputs, Start, 0.37);
            var end = Start + ((inputs.Count + 30) * Tick);

            var reference = Play(keys, Fixed(60, end));
            Assert.That(reference.Dungeon.State.IsChestOpen(Rooms.East, 0), Is.True, "キー操作に直した入力でも宝箱を開けた");
            var final = reference.Dungeon.Hash();
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
                var (trace, result) = Play(keys, pair.Value);
                Assert.That(result.Hash(), Is.EqualTo(final), $"{pair.Key}：最終状態が 60fps と同じ");
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
            Assert.Throws<ArgumentNullException>(() => _ = new VaultFlow(null!, 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => _ = new VaultFlow(new VaultConfig { HeroSpeed = 0 }, 1));
        }

        [Test]
        public void Intent_RejectsUndefinedAndNonFinite()
        {
            Assert.Throws<ArgumentException>(() => Intent.Of(IntentKind.ControlDown, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => Intent.Of((IntentKind)99, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => Intent.Down((Control)99, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => Intent.Of(IntentKind.Submit, double.NaN));
            Assert.Throws<ArgumentOutOfRangeException>(() => Intent.Down(Control.Attack, double.PositiveInfinity));
        }

        [Test]
        public void HeldControls_LatestDirectionWins_AndShortPressesCount()
        {
            var held = new HeldControls();
            held.Press(Control.Left);
            held.Press(Control.Right);
            held.Press(Control.Up);
            Assert.That(held.Horizontal, Is.EqualTo(1), "後から押したほう");
            held.Release(Control.Right);
            Assert.That(held.Horizontal, Is.EqualTo(-1), "離すと残っているほうに戻る");
            held.Press(Control.Down);
            held.Release(Control.Down);
            Assert.That(held.Vertical, Is.EqualTo(1));
            held.Press(Control.Attack);
            held.Release(Control.Attack);
            held.Press(Control.Roll);
            held.Release(Control.Roll);
            Assert.That(held.TakeTickInput(), Is.EqualTo(new HeroInput(-1, 1, true, true)), "Tick の間に押して離しても 1 Tick は押したことにする");
            Assert.That(held.TakeTickInput(), Is.EqualTo(new HeroInput(-1, 1, false, false)));
            held.Clear();
            Assert.That(held.TakeTickInput(), Is.EqualTo(HeroInput.None));
            Assert.Throws<ArgumentOutOfRangeException>(() => held.Press((Control)99));
        }
    }
}
