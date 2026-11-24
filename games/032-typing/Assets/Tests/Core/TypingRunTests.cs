using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace Game.Core.Tests
{
    public sealed class TypingRunTests
    {
        private static readonly WordBook _book = TestKit.Book("かね", "かぎ", "つき", "しか", "ほし", "きって");

        // --- 誤字で進行しない ---

        [Test]
        public void 誤字は敵を進めず連続正解を0に戻し狙いを保つ()
        {
            var run = TestKit.QuietRun(_book);
            var enemy = run.Place(TestKit.Word(_book, "かね"), 0);
            Assert.That(TestKit.TypeAll(run, "ka"), Is.EqualTo(TypeOutcome.Correct));
            Assert.That(run.Combo, Is.EqualTo(2));

            Assert.That(run.Type('x'), Is.EqualTo(TypeOutcome.Miss));
            Assert.That(enemy.Matcher.Typed, Is.EqualTo("ka"));
            Assert.That(run.Target, Is.SameAs(enemy));
            Assert.That(run.Combo, Is.Zero);
            Assert.That(run.MaxCombo, Is.EqualTo(2));
            Assert.That(run.Misses, Is.EqualTo(1));
            Assert.That(run.CorrectKeys, Is.EqualTo(2));

            Assert.That(TestKit.TypeAll(run, "ne"), Is.EqualTo(TypeOutcome.Defeated));
            Assert.That(run.Defeated, Is.EqualTo(1));
        }

        [Test]
        public void 誰も受け付けない最初の1打は誤字で狙わない()
        {
            var run = TestKit.QuietRun(_book);
            var enemy = run.Place(TestKit.Word(_book, "かね"), 0);
            Assert.That(run.Type('t'), Is.EqualTo(TypeOutcome.Miss));
            Assert.That(run.Target, Is.Null);
            Assert.That(enemy.Matcher.Typed, Is.Empty);
            Assert.That(run.Type('-'), Is.EqualTo(TypeOutcome.Miss));
        }

        [Test]
        public void 敵がいないときの打鍵は誤字()
        {
            var run = TestKit.QuietRun(_book);
            Assert.That(run.Type('a'), Is.EqualTo(TypeOutcome.Miss));
            Assert.That(run.Misses, Is.EqualTo(1));
        }

        [TestCase('A')]
        [TestCase('1')]
        [TestCase(' ')]
        [TestCase('\b')]
        [TestCase('あ')]
        public void 受け付けない文字は例外(char key)
        {
            var run = TestKit.QuietRun(_book);
            Assert.That(() => run.Type(key), Throws.TypeOf<ArgumentOutOfRangeException>());
            Assert.That(run.Misses, Is.Zero);
        }

        // --- 複数敵のターゲットが混線しない ---

        [Test]
        public void 最初の1打を受け付ける敵のうち最も手前を狙う()
        {
            var run = TestKit.QuietRun(_book);
            var far = run.Place(TestKit.Word(_book, "かね"), 0, age: 10);
            var near = run.Place(TestKit.Word(_book, "かぎ"), 1, age: 20);
            run.Type('k');
            Assert.That(run.Target, Is.SameAs(near));
            Assert.That(near.Matcher.Typed, Is.EqualTo("k"));
            Assert.That(far.Matcher.Typed, Is.Empty);
        }

        [Test]
        public void 同じ手前なら出現順の早い敵を狙う()
        {
            var run = TestKit.QuietRun(_book);
            var first = run.Place(TestKit.Word(_book, "かね"), 0, age: 10);
            run.Place(TestKit.Word(_book, "かぎ"), 1, age: 10);
            run.Type('c');
            Assert.That(run.Target, Is.SameAs(first));
        }

        [Test]
        public void 受け付けない敵はより手前でも狙わない()
        {
            var run = TestKit.QuietRun(_book);
            run.Place(TestKit.Word(_book, "かね"), 0, age: 100);
            var moon = run.Place(TestKit.Word(_book, "つき"), 1, age: 1);
            run.Type('t');
            Assert.That(run.Target, Is.SameAs(moon));
        }

        [Test]
        public void 狙い中の打鍵は他の敵の単語として受け付けない()
        {
            var run = TestKit.QuietRun(_book);
            var bell = run.Place(TestKit.Word(_book, "かね"), 0, age: 20);
            var key = run.Place(TestKit.Word(_book, "かぎ"), 1, age: 10);
            var moon = run.Place(TestKit.Word(_book, "つき"), 2, age: 5);
            TestKit.TypeAll(run, "ka");
            Assert.That(run.Target, Is.SameAs(bell));

            // 「かぎ」の g・「つき」の t は狙い（かね）にとって誤字。
            Assert.That(run.Type('g'), Is.EqualTo(TypeOutcome.Miss));
            Assert.That(run.Type('t'), Is.EqualTo(TypeOutcome.Miss));
            Assert.That(key.Matcher.Typed, Is.Empty);
            Assert.That(moon.Matcher.Typed, Is.Empty);
            Assert.That(bell.Matcher.Typed, Is.EqualTo("ka"));
            Assert.That(run.Target, Is.SameAs(bell));
        }

        [Test]
        public void 倒した直後の1打は改めて狙いを選ぶ()
        {
            var run = TestKit.QuietRun(_book);
            run.Place(TestKit.Word(_book, "かね"), 0, age: 20);
            var key = run.Place(TestKit.Word(_book, "かぎ"), 1, age: 10);
            Assert.That(TestKit.TypeAll(run, "kane"), Is.EqualTo(TypeOutcome.Defeated));
            Assert.That(run.Target, Is.Null);
            Assert.That(run.TakeEvents() & (RunEvents.Defeated | RunEvents.Released), Is.EqualTo(RunEvents.Defeated | RunEvents.Released));

            Assert.That(run.Type('k'), Is.EqualTo(TypeOutcome.Correct));
            Assert.That(run.Target, Is.SameAs(key));
            Assert.That(run.Enemies, Is.EqualTo(new[] { key }));
        }

        [Test]
        public void 狙っていた敵が手前の線に着くと狙いが外れ次の1打は他の敵へ()
        {
            var run = TestKit.QuietRun(_book, approach: 100);
            var bell = run.Place(TestKit.Word(_book, "かね"), 0, age: 99);
            var key = run.Place(TestKit.Word(_book, "かぎ"), 1, age: 10);
            TestKit.TypeAll(run, "ka");
            run.TakeEvents();
            run.Tick();
            Assert.That(run.Enemies, Has.No.Member(bell));
            Assert.That(run.Target, Is.Null);
            Assert.That(run.Hp, Is.EqualTo(2));
            Assert.That(run.TakeEvents() & (RunEvents.Hit | RunEvents.Released), Is.EqualTo(RunEvents.Hit | RunEvents.Released));

            Assert.That(run.Type('k'), Is.EqualTo(TypeOutcome.Correct));
            Assert.That(run.Target, Is.SameAs(key));
            Assert.That(key.Matcher.Typed, Is.EqualTo("k"));
        }

        // --- バックスペースの扱い ---

        [Test]
        public void Backspaceは狙いを外し進み具合を最初に戻す_誤字と連続正解は変えない()
        {
            var run = TestKit.QuietRun(_book);
            var bell = run.Place(TestKit.Word(_book, "かね"), 0, age: 20);
            TestKit.TypeAll(run, "kax");
            TestKit.TypeAll(run, "n");
            var misses = run.Misses;
            var combo = run.Combo;
            var correct = run.CorrectKeys;
            run.TakeEvents();

            Assert.That(run.Backspace(), Is.True);
            Assert.That(run.Target, Is.Null);
            Assert.That(bell.Matcher.Typed, Is.Empty);
            Assert.That(bell.Matcher.Guide, Is.EqualTo("kane"));
            Assert.That(run.Misses, Is.EqualTo(misses));
            Assert.That(run.Combo, Is.EqualTo(combo));
            Assert.That(run.CorrectKeys, Is.EqualTo(correct));
            Assert.That(run.TakeEvents(), Is.EqualTo(RunEvents.Released));
        }

        [Test]
        public void 狙いがないときのBackspaceは何も変えない()
        {
            var run = TestKit.QuietRun(_book);
            run.Place(TestKit.Word(_book, "かね"), 0);
            var before = run.Hash();
            Assert.That(run.Backspace(), Is.False);
            Assert.That(run.Hash(), Is.EqualTo(before));
            Assert.That(run.TakeEvents(), Is.EqualTo(RunEvents.None));
        }

        [Test]
        public void Backspaceの後は別の敵を狙える_連打しても2回目以降は何もしない()
        {
            var run = TestKit.QuietRun(_book);
            var near = run.Place(TestKit.Word(_book, "かね"), 0, age: 20);
            var other = run.Place(TestKit.Word(_book, "しか"), 1, age: 10);
            TestKit.TypeAll(run, "ka");
            Assert.That(run.Backspace(), Is.True);
            Assert.That(run.Backspace(), Is.False);
            Assert.That(run.Backspace(), Is.False);

            Assert.That(TestKit.TypeAll(run, "shika"), Is.EqualTo(TypeOutcome.Defeated));
            Assert.That(run.Enemies, Is.EqualTo(new[] { near }));
            Assert.That(near.Matcher.Typed, Is.Empty);
            Assert.That(other.Matcher.IsComplete, Is.True);
        }

        [Test]
        public void Backspaceの後に同じ敵を別の綴りで打ち直せる()
        {
            var run = TestKit.QuietRun(_book);
            var deer = run.Place(TestKit.Word(_book, "しか"), 0);
            TestKit.TypeAll(run, "sh");
            run.Backspace();
            Assert.That(TestKit.TypeAll(run, "cika"), Is.EqualTo(TypeOutcome.Defeated));
            Assert.That(deer.Matcher.Typed, Is.EqualTo("cika"));
        }

        // --- 勝敗の一貫処理 ---

        [Test]
        public void 規定数ちょうどの撃破でクリアし以後の打鍵とTickは無視する()
        {
            var run = TestKit.QuietRun(_book, goal: 2);
            run.Place(TestKit.Word(_book, "かね"), 0);
            run.Place(TestKit.Word(_book, "つき"), 1);
            run.Place(TestKit.Word(_book, "ほし"), 2);
            Assert.That(TestKit.TypeAll(run, "kane"), Is.EqualTo(TypeOutcome.Defeated));
            Assert.That(run.Status, Is.EqualTo(RunStatus.Playing));
            Assert.That(TestKit.TypeAll(run, "tuki"), Is.EqualTo(TypeOutcome.Defeated));
            Assert.That(run.Status, Is.EqualTo(RunStatus.Cleared));
            Assert.That(run.TakeEvents() & RunEvents.Cleared, Is.EqualTo(RunEvents.Cleared));

            var hash = run.Hash();
            Assert.That(run.Type('h'), Is.EqualTo(TypeOutcome.Ignored));
            Assert.That(run.Backspace(), Is.False);
            for (var i = 0; i < 1000; i++) { run.Tick(); }
            Assert.That(run.Hash(), Is.EqualTo(hash));
            Assert.That(run.TakeEvents(), Is.EqualTo(RunEvents.None));
        }

        [Test]
        public void 体力が0になると失敗し以後の打鍵とTickは無視する()
        {
            var run = TestKit.QuietRun(_book, hp: 2, approach: 100);
            run.Place(TestKit.Word(_book, "かね"), 0, age: 99);
            run.Place(TestKit.Word(_book, "つき"), 1, age: 99);
            run.Place(TestKit.Word(_book, "ほし"), 2, age: 50);
            run.Tick();
            Assert.That(run.Status, Is.EqualTo(RunStatus.Failed));
            Assert.That(run.Hp, Is.Zero);
            Assert.That(run.TakeEvents() & RunEvents.Failed, Is.EqualTo(RunEvents.Failed));

            var hash = run.Hash();
            Assert.That(run.Type('h'), Is.EqualTo(TypeOutcome.Ignored));
            run.Tick();
            Assert.That(run.Hash(), Is.EqualTo(hash));
        }

        [Test]
        public void 同じTickなら打鍵の撃破が先で_規定数に届けば被弾しない()
        {
            // 体力 1・規定数 1。手前の線に着く直前の敵がいる Tick の前に、別の敵を打ち切る。
            var run = TestKit.QuietRun(_book, hp: 1, goal: 1, approach: 100);
            run.Place(TestKit.Word(_book, "かね"), 0, age: 99);
            run.Place(TestKit.Word(_book, "つき"), 1, age: 10);
            Assert.That(TestKit.TypeAll(run, "tuki"), Is.EqualTo(TypeOutcome.Defeated));
            run.Tick();
            Assert.That(run.Status, Is.EqualTo(RunStatus.Cleared));
            Assert.That(run.Hp, Is.EqualTo(1));
        }

        [Test]
        public void 同じTickなら打鍵が先で_打ち終えていなければ被弾する()
        {
            var run = TestKit.QuietRun(_book, hp: 1, goal: 1, approach: 100);
            run.Place(TestKit.Word(_book, "かね"), 0, age: 99);
            TestKit.TypeAll(run, "kan");
            run.Tick();
            Assert.That(run.Status, Is.EqualTo(RunStatus.Failed));
            Assert.That(run.Defeated, Is.Zero);
        }

        [Test]
        public void 敵は出現からApproachTicksで手前の線に着く()
        {
            var run = TestKit.QuietRun(_book, hp: 3, approach: 100);
            var enemy = run.Place(TestKit.Word(_book, "かね"), 0);
            Assert.That(enemy.Distance, Is.EqualTo(Enemy.Far));
            for (var i = 0; i < 99; i++) { run.Tick(); }
            Assert.That(enemy.Distance, Is.EqualTo(Enemy.Far / 100));
            Assert.That(run.Hp, Is.EqualTo(3));
            run.Tick();
            Assert.That(run.Hp, Is.EqualTo(2));
            Assert.That(run.Enemies, Is.Empty);
        }

        [Test]
        public void 演出のために最後の撃破_被弾_誤字のTickを残しハッシュには入れない()
        {
            var run = TestKit.QuietRun(_book, approach: 100);
            Assert.That(run.LastDefeated, Is.Null);
            Assert.That(run.LastDefeatedTick, Is.EqualTo(-1));
            Assert.That(run.LastHitTick, Is.EqualTo(-1));
            Assert.That(run.LastMissTick, Is.EqualTo(-1));
            var bell = run.Place(TestKit.Word(_book, "かね"), 3, age: 40);
            run.Place(TestKit.Word(_book, "つき"), 1, age: 98);
            run.Tick();
            TestKit.TypeAll(run, "kane");
            Assert.That(run.LastDefeated, Is.SameAs(bell));
            Assert.That(run.LastDefeated!.Lane, Is.EqualTo(3));
            Assert.That(run.LastDefeatedTick, Is.EqualTo(1));

            var twin = run.Clone();
            run.Type('q');
            Assert.That(run.LastMissTick, Is.EqualTo(1));
            run.Tick();
            Assert.That(run.LastHitTick, Is.EqualTo(2));
            twin.Type('x');
            twin.Tick();
            Assert.That(twin.Hash(), Is.EqualTo(run.Hash()), "誤字の文字が違っても以後は同じ");
            Assert.That(twin.LastDefeated, Is.Not.SameAs(run.LastDefeated), "複製は描画用の敵も独立");
        }

        // --- 得点 ---

        [Test]
        public void 得点は連続正解の倍率と撃破のかな数で決まる()
        {
            Assert.That(TypingRun.Multiplier(0), Is.EqualTo(1));
            Assert.That(TypingRun.Multiplier(19), Is.EqualTo(1));
            Assert.That(TypingRun.Multiplier(20), Is.EqualTo(2));
            Assert.That(TypingRun.Multiplier(80), Is.EqualTo(5));
            Assert.That(TypingRun.Multiplier(10000), Is.EqualTo(5));

            var run = TestKit.QuietRun(_book);
            run.Place(TestKit.Word(_book, "かね"), 0);
            TestKit.TypeAll(run, "kane");
            Assert.That(run.Score, Is.EqualTo((4 * 10) + (2 * 50)));
            Assert.That(run.AccuracyPermil, Is.EqualTo(1000));
            run.Type('q');
            Assert.That(run.AccuracyPermil, Is.EqualTo(800));
        }

        [Test]
        public void 打鍵の速さは遊んだ時間から数える()
        {
            Assert.That(TypingRun.KeysPerMinute(300, 3600), Is.EqualTo(300));
            Assert.That(TypingRun.KeysPerMinute(5, 0), Is.Zero);
            Assert.That(TypingRun.KeysPerMinute(int.MaxValue, 1), Is.EqualTo(int.MaxValue));
        }

        // --- 出題 ---

        [Test]
        public void 各難度の山は尽きるまで同じ語を出さず_全難度で50語を出題する()
        {
            var book = TestKit.Production();
            var config = new VigilConfig();
            var seen = new HashSet<int>();
            foreach (var difficulty in new[] { Difficulty.Easy, Difficulty.Normal, Difficulty.Hard })
            {
                var level = config.Level(difficulty);
                var words = book.Entries.Where(e => e.Tier >= level.MinTier && e.Tier <= level.MaxTier).Select(e => e.Index).ToList();
                foreach (var seed in new[] { 1, 2, 3 })
                {
                    var spawned = SpawnedWords(book, difficulty, seed, words.Count);
                    Assert.That(spawned, Is.EquivalentTo(words), $"{difficulty} シード {seed}");
                    seen.UnionWith(spawned);
                }
            }

            Assert.That(seen, Is.EquivalentTo(Enumerable.Range(0, 50)));
        }

        [Test]
        public void 出現する語は画面の敵と最初の1打が重ならない語を優先する()
        {
            var book = TestKit.Book("かね", "かぎ", "つき");
            for (var seed = 1; seed <= 20; seed++)
            {
                var config = new VigilConfig();
                config.Easy.MaxEnemies = 2;
                config.Easy.FirstSpawnTicks = 1;
                var run = new TypingRun(config, book, Difficulty.Easy, seed);
                run.Place(TestKit.Word(book, "かね"), 0);
                run.Tick();
                Assert.That(string.Join(",", run.Enemies.Select(e => e.Word.Reading.Kana)), Is.EqualTo("かね,つき"), $"シード {seed}");
            }
        }

        [Test]
        public void 出現は同時数と規定数を超えず空いた列に出る()
        {
            var book = TestKit.Production();
            var run = new TypingRun(new VigilConfig(), book, Difficulty.Hard, 7);
            var level = new VigilConfig().Hard;
            for (var tick = 0; tick < 3000 && run.Status == RunStatus.Playing; tick++)
            {
                run.Tick();
                Assert.That(run.Enemies.Count, Is.LessThanOrEqualTo(level.MaxEnemies));
                Assert.That(run.Enemies.Select(e => e.Lane).Distinct().Count(), Is.EqualTo(run.Enemies.Count));
                Assert.That(run.Enemies.Select(e => e.Word).Distinct().Count(), Is.EqualTo(run.Enemies.Count));
                Assert.That(run.Defeated + run.Enemies.Count, Is.LessThanOrEqualTo(level.Goal));
            }
        }

        // --- 再現性 ---

        [Test]
        public void 同じシードと同じ打鍵なら同じハッシュ_シードが違えば出題が変わる()
        {
            var book = TestKit.Production();
            Assert.That(Play(book, 5).Hash(), Is.EqualTo(Play(book, 5).Hash()));
            var orders = Enumerable.Range(1, 10).Select(seed => string.Join(",", SpawnedWords(book, Difficulty.Normal, seed, 5))).Distinct().Count();
            Assert.That(orders, Is.GreaterThan(5));
        }

        [Test]
        public void 複製は独立して進む()
        {
            var run = TestKit.QuietRun(_book);
            run.Place(TestKit.Word(_book, "かね"), 0);
            run.Place(TestKit.Word(_book, "つき"), 1);
            TestKit.TypeAll(run, "ka");
            var clone = run.Clone();
            Assert.That(clone.Hash(), Is.EqualTo(run.Hash()));
            Assert.That(clone.Target!.Id, Is.EqualTo(run.Target!.Id));
            Assert.That(clone.Target, Is.Not.SameAs(run.Target));

            TestKit.TypeAll(clone, "ne");
            clone.Tick();
            Assert.That(run.Enemies.Count, Is.EqualTo(2));
            Assert.That(run.Target!.Matcher.Typed, Is.EqualTo("ka"));
            Assert.That(clone.Hash(), Is.Not.EqualTo(run.Hash()));
        }

        [Test]
        public void 今は同じ位置でも以後が分かれる状態はハッシュが違う()
        {
            // 狙いと打った綴りが違うだけで、敵の位置と統計は同じ。
            var a = TestKit.QuietRun(_book);
            var b = TestKit.QuietRun(_book);
            foreach (var run in new[] { a, b }) { run.Place(TestKit.Word(_book, "しか"), 0); }
            a.Type('s');
            b.Type('c');
            Assert.That(a.CorrectKeys, Is.EqualTo(b.CorrectKeys));
            Assert.That(a.Hash(), Is.Not.EqualTo(b.Hash()));

            // 打った分を戻した（Backspace）か、狙ったままか。
            var c = TestKit.QuietRun(_book);
            var d = TestKit.QuietRun(_book);
            foreach (var run in new[] { c, d }) { run.Place(TestKit.Word(_book, "しか"), 0); run.Type('s'); }
            c.Backspace();
            Assert.That(c.Hash(), Is.Not.EqualTo(d.Hash()));
        }

        // --- 設定 ---

        [Test]
        public void 既定の設定は本番の単語帳で検証を通る() => Assert.That(() => new VigilConfig().Validate(TestKit.Production()), Throws.Nothing);

        [TestCase("Hp", 0)]
        [TestCase("Hp", 10)]
        [TestCase("Lanes", 0)]
        [TestCase("MaxEnemies", 0)]
        [TestCase("MaxEnemies", 6)]
        [TestCase("ApproachTicks", 59)]
        [TestCase("Goal", 0)]
        [TestCase("MinTier", 0)]
        [TestCase("MaxTier", 4)]
        [TestCase("SpawnMinTicks", 0)]
        [TestCase("SpawnMaxTicks", 1)]
        [TestCase("FirstSpawnTicks", -1)]
        public void 範囲外の設定は拒否する(string field, int value)
        {
            var config = new VigilConfig();
            switch (field)
            {
                case "Hp": config.Hp = value; break;
                case "Lanes": config.Lanes = value; break;
                case "MaxEnemies": config.Normal.MaxEnemies = value; break;
                case "ApproachTicks": config.Hard.ApproachTicks = value; break;
                case "Goal": config.Easy.Goal = value; break;
                case "MinTier": config.Easy.MinTier = value; break;
                case "MaxTier": config.Easy.MaxTier = value; break;
                case "SpawnMinTicks": config.Easy.SpawnMinTicks = value; break;
                case "SpawnMaxTicks": config.Easy.SpawnMaxTicks = value; break;
                case "FirstSpawnTicks": config.Easy.FirstSpawnTicks = value; break;
                default: throw new ArgumentOutOfRangeException(nameof(field));
            }

            Assert.That(() => config.Validate(), Throws.TypeOf<ArgumentOutOfRangeException>());
            Assert.That(() => new TypingRun(config, _book, Difficulty.Easy, 1), Throws.TypeOf<ArgumentOutOfRangeException>());
        }

        [Test]
        public void 難度の段の語が同時出現数より少なければ拒否する()
        {
            var config = new VigilConfig();
            config.Easy.MaxEnemies = 3;
            var book = TestKit.Book("かね", "つき");
            Assert.That(() => config.Validate(book), Throws.TypeOf<ArgumentOutOfRangeException>().With.Message.Contains("Easy"));
        }

        [Test]
        public void 未定義の難度は拒否する()
        {
            Assert.That(() => new VigilConfig().Level((Difficulty)9), Throws.TypeOf<ArgumentOutOfRangeException>());
        }

        [Test]
        public void 試合は設定の複製を持つ()
        {
            var config = TestKit.Quiet(approach: 100);
            var run = new TypingRun(config, _book, Difficulty.Easy, 1);
            config.Easy.ApproachTicks = 5000;
            config.Hp = 9;
            Assert.That(run.ApproachTicks, Is.EqualTo(100));
            Assert.That(run.MaxHp, Is.EqualTo(3));
        }

        [Test]
        public void 置く敵の列と経過は範囲内に限る()
        {
            var run = TestKit.QuietRun(_book, approach: 100);
            var word = TestKit.Word(_book, "かね");
            Assert.That(() => run.Place(word, -1), Throws.TypeOf<ArgumentOutOfRangeException>());
            Assert.That(() => run.Place(word, 5), Throws.TypeOf<ArgumentOutOfRangeException>());
            Assert.That(() => run.Place(word, 0, age: 100), Throws.TypeOf<ArgumentOutOfRangeException>());
        }

        /// <summary>敵が出るたびに既定の綴りで打ち切って倒し、最初の <paramref name="count"/> 体の語の番号を返す。</summary>
        private static List<int> SpawnedWords(WordBook book, Difficulty difficulty, int seed, int count)
        {
            var config = new VigilConfig();
            config.Level(difficulty).Goal = 999;
            config.Hp = 9;
            var run = new TypingRun(config, book, difficulty, seed);
            var words = new List<int>();
            var known = new HashSet<int>();
            for (var tick = 0; tick < 100000 && words.Count < count; tick++)
            {
                run.Tick();
                foreach (var enemy in run.Enemies.ToList())
                {
                    if (known.Add(enemy.Id)) { words.Add(enemy.Word.Index); }
                }

                foreach (var enemy in run.Enemies.ToList()) { TestKit.TypeAll(run, enemy.Matcher.Guide); }
            }

            return words.Take(count).ToList();
        }

        private static TypingRun Play(WordBook book, int seed)
        {
            var run = new TypingRun(new VigilConfig(), book, Difficulty.Normal, seed);
            for (var tick = 0; tick < 2000; tick++)
            {
                run.Tick();
                if (tick % 30 == 0 && run.Enemies.Count > 0) { run.Type(run.Enemies[0].Matcher.Guide[0]); }
            }

            return run;
        }
    }
}
