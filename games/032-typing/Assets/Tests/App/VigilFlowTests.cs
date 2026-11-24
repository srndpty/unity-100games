using System;
using System.IO;
using System.Text;
using Game.Core;
using NUnit.Framework;

namespace Game.App.Tests
{
    public sealed class VigilFlowTests
    {
        private const double Tick = 1.0 / VigilFlow.TickRate;

        private static readonly WordBook _book = WordBook.Production(WordsText());

        // --- 状態遷移 ---

        [Test]
        public void タイトル_開始_結果_再挑戦_タイトルの順に進む()
        {
            var flow = new VigilFlow(Quick(goal: 1), _book, 10);
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Title));
            Assert.That(flow.Run, Is.Null);

            flow.Apply(Intent.Of(IntentKind.Submit, 0));
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Playing));
            Assert.That(flow.Run!.Seed, Is.EqualTo(10));
            Assert.That(flow.TakeEvents() & FlowEvents.Started, Is.EqualTo(FlowEvents.Started));

            DefeatFirst(flow, 1.0);
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Result));
            Assert.That(flow.Run!.Status, Is.EqualTo(RunStatus.Cleared));

            flow.Apply(Intent.Of(IntentKind.Submit, 5));
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Playing));
            Assert.That(flow.Run!.Seed, Is.EqualTo(11), "再挑戦は新しいシード");
            Assert.That(flow.Run.Difficulty, Is.EqualTo(Difficulty.Normal));

            flow.Apply(Intent.Of(IntentKind.Cancel, 5.1));
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Paused));
            flow.Apply(Intent.Type('t', 5.2));
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Title));
            Assert.That(flow.Run, Is.Null);
            Assert.That(flow.TakeEvents() & FlowEvents.Title, Is.EqualTo(FlowEvents.Title));
        }

        [Test]
        public void 結果からEscでタイトルへ_タイトルの打鍵とBackspaceは何もしない()
        {
            var flow = new VigilFlow(Quick(goal: 1), _book, 1);
            flow.Apply(Intent.Type('k', 0));
            flow.Apply(Intent.Of(IntentKind.Backspace, 0));
            flow.Apply(Intent.Of(IntentKind.Cancel, 0));
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Title));

            flow.Apply(Intent.Of(IntentKind.Submit, 0));
            DefeatFirst(flow, 1.0);
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Result));
            flow.Apply(Intent.Type('k', 3));
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Result));
            flow.Apply(Intent.Of(IntentKind.Cancel, 3));
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Title));
        }

        [Test]
        public void タイトルで難度を選ぶ_端で止まる()
        {
            var flow = new VigilFlow(new VigilConfig(), _book, 1);
            Assert.That(flow.Selected, Is.EqualTo(Difficulty.Normal));
            flow.Apply(Intent.Of(IntentKind.Up, 0));
            flow.Apply(Intent.Of(IntentKind.Up, 0));
            Assert.That(flow.Selected, Is.EqualTo(Difficulty.Easy));
            flow.Apply(Intent.Of(IntentKind.Down, 0));
            flow.Apply(Intent.Of(IntentKind.Down, 0));
            flow.Apply(Intent.Of(IntentKind.Down, 0));
            Assert.That(flow.Selected, Is.EqualTo(Difficulty.Hard));
            flow.Apply(Intent.Of(IntentKind.Submit, 0));
            Assert.That(flow.Run!.Difficulty, Is.EqualTo(Difficulty.Hard));

            // プレイ中の ↑↓ は難度を変えない。
            flow.Apply(Intent.Of(IntentKind.Up, 0));
            Assert.That(flow.Selected, Is.EqualTo(Difficulty.Hard));
        }

        [Test]
        public void 結果の後の打鍵と時間は試合を変えない()
        {
            var flow = new VigilFlow(Quick(goal: 1), _book, 1);
            flow.Apply(Intent.Of(IntentKind.Submit, 0));
            DefeatFirst(flow, 1.0);
            var hash = flow.Run!.Hash();
            var played = flow.PlayedTicks;
            flow.Apply(Intent.Type('a', 2));
            flow.Apply(Intent.Of(IntentKind.Backspace, 2));
            flow.Advance(100);
            Assert.That(flow.Run.Hash(), Is.EqualTo(hash));
            Assert.That(flow.PlayedTicks, Is.EqualTo(played));
        }

        [Test]
        public void 失敗も結果画面になる()
        {
            var config = Quick(goal: 5);
            config.Hp = 1;
            config.Normal.ApproachTicks = 60;
            var flow = new VigilFlow(config, _book, 1);
            flow.Apply(Intent.Of(IntentKind.Submit, 0));
            for (var t = 1; t <= 200 && flow.Screen == AppScreen.Playing; t++) { flow.Advance(t * 5 * Tick); }
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Result));
            Assert.That(flow.Run!.Status, Is.EqualTo(RunStatus.Failed));
            Assert.That(flow.TakeEvents() & (FlowEvents.Hit | FlowEvents.Failed), Is.EqualTo(FlowEvents.Hit | FlowEvents.Failed));
        }

        // --- 打鍵の順序と時刻 ---

        [Test]
        public void 打鍵は届いた順に反映しBackspaceも同じ列で処理する()
        {
            var flow = Started(Quick());
            var first = WaitForEnemy(flow);
            var guide = first.Matcher.Guide;
            flow.Apply(Intent.Type(guide[0], 0));
            flow.Apply(Intent.Of(IntentKind.Backspace, 0));
            flow.Apply(Intent.Type(guide[0], 0));
            Assert.That(first.Matcher.Typed, Is.EqualTo(guide.Substring(0, 1)));
            Assert.That(flow.Run!.Target, Is.SameAs(first));
            Assert.That(flow.Run.Misses, Is.Zero);
        }

        [Test]
        public void 打鍵の時刻までのTickを先に進める()
        {
            var flow = Started(Quick());
            flow.Apply(Intent.Type('q', 3 * Tick + 0.001));
            Assert.That(flow.Run!.Ticks, Is.EqualTo(3));
            Assert.That(flow.Run.Misses, Is.EqualTo(1));
        }

        [Test]
        public void 一フレームのTick上限を超えた区間は飛ばし遊んだ時間には数える()
        {
            var flow = Started(Quick());
            flow.Advance(1.0);
            Assert.That(flow.Run!.Ticks, Is.EqualTo(VigilFlow.MaxTicksPerFrame));
            Assert.That(flow.PlayedTicks, Is.EqualTo(60));

            // 同じフレームの打鍵と時計で上限を分け合う。
            flow.Apply(Intent.Type('q', 2.0));
            flow.Advance(3.0);
            Assert.That(flow.Run.Ticks, Is.EqualTo(2 * VigilFlow.MaxTicksPerFrame));
            Assert.That(flow.PlayedTicks, Is.EqualTo(180));
        }

        [Test]
        public void 一時停止の間は時間が進まず再開した時刻から数え直す()
        {
            var flow = Started(Quick());
            Run(flow, 0, 1.0);
            flow.Apply(Intent.Of(IntentKind.Cancel, 1.0));
            flow.Advance(50);
            Assert.That(flow.PlayedTicks, Is.EqualTo(60));
            flow.Apply(Intent.Type('a', 50));
            Assert.That(flow.Run!.Misses, Is.Zero, "一時停止中の打鍵は試合に届かない");

            flow.Apply(Intent.Of(IntentKind.Submit, 50));
            Run(flow, 50, 51);
            Assert.That(flow.PlayedTicks, Is.EqualTo(120));
        }

        [Test]
        public void フォーカスを失うと一時停止し戻っても自動では再開しない()
        {
            var flow = Started(Quick());
            Run(flow, 0, 1.0);
            flow.SetFocus(false);
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Paused));
            flow.Apply(Intent.Of(IntentKind.Submit, 1.1));
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Paused), "フォーカスがない間の入力は捨てる");

            flow.SetFocus(true);
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Paused));
            flow.Apply(Intent.Of(IntentKind.Cancel, 2));
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Playing));
        }

        [Test]
        public void 打鍵の速さは遊んだ時間から数える()
        {
            var flow = Started(Quick());
            var enemy = WaitForEnemy(flow);
            var start = flow.PlayedTicks;
            Assert.That(start, Is.Positive);
            var guide = enemy.Matcher.Guide;
            foreach (var key in guide.Substring(0, 2)) { flow.Apply(Intent.Type(key, 0)); }
            Assert.That(flow.PlayedTicks, Is.EqualTo(start));
            Assert.That(flow.KeysPerMinute, Is.EqualTo(TypingRun.KeysPerMinute(2, start)));
        }

        // --- 最高得点 ---

        [Test]
        public void 難度ごとの最高得点を更新したときだけ新記録()
        {
            var flow = new VigilFlow(Quick(goal: 1), _book, 1);
            flow.Apply(Intent.Of(IntentKind.Submit, 0));
            DefeatFirst(flow, 1.0);
            Assert.That(flow.IsNewBest, Is.True);
            var best = flow.BestScore(Difficulty.Normal);
            Assert.That(best, Is.EqualTo(flow.Run!.Score));
            Assert.That(flow.BestScore(Difficulty.Easy), Is.Zero);

            // 何も打たずに負けた試合（0 点）は更新しない。
            flow.Apply(Intent.Of(IntentKind.Submit, 10));
            for (var i = 0; i < 100000 && flow.Screen == AppScreen.Playing; i++) { flow.StepTick(); }
            Assert.That(flow.Run!.Status, Is.EqualTo(RunStatus.Failed));
            Assert.That(flow.Run.Score, Is.Zero);
            Assert.That(flow.IsNewBest, Is.False);
            Assert.That(flow.BestScore(Difficulty.Normal), Is.EqualTo(best));
        }

        // --- 操縦役 ---

        [Test]
        public void 速い操縦役は全ての難度で20シード全勝する([Values] Difficulty difficulty)
        {
            for (var seed = 1; seed <= 20; seed++)
            {
                var run = PlayOut(difficulty, seed, new Typist(8));
                Assert.That(run.Status, Is.EqualTo(RunStatus.Cleared), $"{difficulty} シード {seed}");
                Assert.That(run.Hp, Is.EqualTo(run.MaxHp), $"{difficulty} シード {seed}");
            }
        }

        [Test]
        public void 誤字を混ぜても速い操縦役は勝ち_誤字が数えられる()
        {
            var run = PlayOut(Difficulty.Normal, 3, new Typist(8, mistakePermil: 100));
            Assert.That(run.Status, Is.EqualTo(RunStatus.Cleared));
            Assert.That(run.Misses, Is.Positive);
            Assert.That(run.AccuracyPermil, Is.LessThan(1000));
        }

        // 既定の設定で 20 シードを測ると、勝てる打鍵の間隔の上限は Easy 60・Normal 30・Hard 15 Tick 前後（1・2・4 打/秒）。
        [TestCase(Difficulty.Easy, 90)]
        [TestCase(Difficulty.Normal, 40)]
        [TestCase(Difficulty.Hard, 20)]
        public void 難度ごとの遅さを下回る操縦役は負ける(Difficulty difficulty, int intervalTicks)
        {
            for (var seed = 1; seed <= 5; seed++)
            {
                Assert.That(PlayOut(difficulty, seed, new Typist(intervalTicks)).Status, Is.EqualTo(RunStatus.Failed), $"{difficulty} シード {seed}");
            }
        }

        [Test]
        public void 操縦役の試合はシードで決まり最終ハッシュが固定値と一致する()
        {
            var a = PlayOut(Difficulty.Hard, 1, new Typist(8, mistakePermil: 50));
            var b = PlayOut(Difficulty.Hard, 1, new Typist(8, mistakePermil: 50));
            Assert.That(a.Hash(), Is.EqualTo(b.Hash()));
            Assert.That(a.Status, Is.EqualTo(RunStatus.Cleared));
            TestContext.WriteLine($"Ticks {a.Ticks} Score {a.Score} Hash 0x{a.Hash():X8}");
            Assert.That(a.Hash(), Is.EqualTo(ExpectedHash), $"0x{a.Hash():X8}");
        }

        [Test]
        public void 操縦役は試合ごとに判断の状態を捨てる()
        {
            // 途中でやめた試合の後でも、同じシードなら同じ結果になる。
            var typist = new Typist(8, mistakePermil: 100);
            var fresh = PlayOut(Difficulty.Normal, 4, new Typist(8, mistakePermil: 100)).Hash();

            var flow = new VigilFlow(new VigilConfig(), _book, 3) { Pilot = typist };
            flow.Apply(Intent.Of(IntentKind.Submit, 0));
            for (var i = 0; i < 77; i++) { flow.StepTick(); }
            flow.Apply(Intent.Of(IntentKind.Cancel, 0));
            flow.Apply(Intent.Type('t', 0));
            flow.Apply(Intent.Of(IntentKind.Submit, 0));
            Assert.That(flow.Run!.Seed, Is.EqualTo(4));
            for (var i = 0; i < 100000 && flow.Screen == AppScreen.Playing; i++) { flow.StepTick(); }
            Assert.That(flow.Run.Hash(), Is.EqualTo(fresh));
        }

        [Test]
        public void 操縦役がいる間はキーの打鍵を試合に渡さない()
        {
            var flow = Started(Quick());
            flow.Pilot = new Typist(1000);
            WaitForEnemy(flow);
            flow.Apply(Intent.Type('q', 100));
            Assert.That(flow.Run!.Misses, Is.Zero);
        }

        // --- 負テスト ---

        [Test]
        public void 不正な意図と設定は拒否する()
        {
            Assert.That(() => Intent.Type('1', 0), Throws.TypeOf<ArgumentOutOfRangeException>());
            Assert.That(() => Intent.Type('a', double.NaN), Throws.TypeOf<ArgumentOutOfRangeException>());
            Assert.That(() => Intent.Of(IntentKind.Key, 0), Throws.ArgumentException);
            Assert.That(() => Intent.Of((IntentKind)99, 0), Throws.TypeOf<ArgumentOutOfRangeException>());
            Assert.That(Intent.Type('K', 0).Key, Is.EqualTo('k'));
            Assert.That(() => new Typist(0), Throws.TypeOf<ArgumentOutOfRangeException>());
            Assert.That(() => new Typist(1, 1001), Throws.TypeOf<ArgumentOutOfRangeException>());

            var config = new VigilConfig();
            config.Hard.Goal = 0;
            Assert.That(() => new VigilFlow(config, _book, 1), Throws.TypeOf<ArgumentOutOfRangeException>());
        }

        [Test]
        public void 流れは設定の複製を持つ()
        {
            var config = Quick(goal: 1);
            var flow = new VigilFlow(config, _book, 1);
            config.Normal.Goal = 50;
            flow.Apply(Intent.Of(IntentKind.Submit, 0));
            Assert.That(flow.Run!.Goal, Is.EqualTo(1));
        }

        /// <summary>試合の最終ハッシュ（Hard・シード 1・8 Tick ごと・誤字 5%）。.NET と Mono で一致すること。</summary>
        private const uint ExpectedHash = 0xCC41215F;

        private static VigilConfig Quick(int goal = 99)
        {
            var config = new VigilConfig();
            config.Normal.Goal = goal;
            config.Normal.FirstSpawnTicks = 1;
            return config;
        }

        private static VigilFlow Started(VigilConfig config)
        {
            var flow = new VigilFlow(config, _book, 1);
            flow.Apply(Intent.Of(IntentKind.Submit, 0));
            return flow;
        }

        /// <summary>1 Tick ずつのフレームで <paramref name="from"/> から <paramref name="to"/> 秒まで進める。</summary>
        private static void Run(VigilFlow flow, double from, double to)
        {
            var frames = (int)Math.Round((to - from) * VigilFlow.TickRate);
            for (var i = 0; i <= frames; i++) { flow.Advance(i == frames ? to : from + (i * Tick)); }
        }

        private static Enemy WaitForEnemy(VigilFlow flow)
        {
            for (var i = 0; i < 600 && flow.Run!.Enemies.Count == 0; i++) { flow.StepTick(); }
            return flow.Run!.Enemies[0];
        }

        /// <summary>最初の敵が出るまで進め、その案内どおりに打ち切る。</summary>
        private static void DefeatFirst(VigilFlow flow, double time)
        {
            var enemy = WaitForEnemy(flow);
            foreach (var key in enemy.Matcher.Guide) { flow.Apply(Intent.Type(key, time)); }
        }

        private static TypingRun PlayOut(Difficulty difficulty, int seed, IPilot pilot)
        {
            var config = new VigilConfig();
            var flow = new VigilFlow(config, _book, seed) { Pilot = pilot };
            while (flow.Selected != difficulty) { flow.Apply(Intent.Of(flow.Selected < difficulty ? IntentKind.Down : IntentKind.Up, 0)); }
            flow.Apply(Intent.Of(IntentKind.Submit, 0));
            for (var i = 0; i < 200000 && flow.Screen == AppScreen.Playing; i++) { flow.StepTick(); }
            return flow.Run!;
        }

        private static string WordsText()
        {
            foreach (var start in new[] { TestContext.CurrentContext.TestDirectory, Environment.CurrentDirectory })
            {
                for (var directory = new DirectoryInfo(start); directory != null; directory = directory.Parent)
                {
                    var candidate = Path.Combine(directory.FullName, "Assets", "Game", "Data", "Words.txt");
                    if (File.Exists(candidate)) { return File.ReadAllText(candidate, Encoding.UTF8); }
                }
            }

            throw new FileNotFoundException("Assets/Game/Data/Words.txt が見つかりません。");
        }
    }
}
