using System;
using System.Linq;
using NUnit.Framework;
using static Game.Core.Tests.TestKit;

namespace Game.Core.Tests
{
    /// <summary>弾道 DSL の字句・構文・単位の変換・誤りの行と列。</summary>
    public sealed class ScriptParserTests
    {
        private const string Sample = @"# 見本
boss ""Ember Warden"" radius=24
pattern ""Cinder Bloom"" hp=900 time=40s
  move to at=192,360 over=60
  move wait over=30
  emit ring every=20 count=24 speed=1.8 spin=7 shape=orb color=blue
  emit aimed every=45 delay=10 count=5 spread=30 speed=3 accel=0.02 maxspeed=4.5
pattern ""Ash Spiral"" hp=1200 time=2400   # Tick で書く
  move wander area=96,280,288,400 over=120
  emit fan every=4 count=3 angle=rand(-10,10) spread=20 speed=rand(1.5,2.5) curve=0.4 offset=0,-8 life=300 shape=rice color=purple
";

        [Test]
        public void Sample_ParsesIntoExpectedSpecs()
        {
            var result = ScriptParser.Parse(Sample);
            Assert.That(result.Errors, Is.Empty, string.Join("\n", result.Errors));
            var boss = result.Script!;
            Assert.That(boss.Name, Is.EqualTo("Ember Warden"));
            Assert.That(boss.Radius, Is.EqualTo(24 * Field.Unit));
            Assert.That(boss.Patterns.Count, Is.EqualTo(2));

            var first = boss.Patterns[0];
            Assert.That(first.Name, Is.EqualTo("Cinder Bloom"));
            Assert.That(first.Hp, Is.EqualTo(900));
            Assert.That(first.TimeTicks, Is.EqualTo(2400));
            Assert.That(first.Moves.Select(m => m.Kind), Is.EqualTo(new[] { MoveKind.To, MoveKind.Wait }));
            Assert.That(first.Moves[0].Target, Is.EqualTo(Px(192, 360)));
            Assert.That(first.Moves[0].Ticks, Is.EqualTo(60));
            Assert.That(first.Moves[1].Ticks, Is.EqualTo(30));

            var ring = first.Emitters[0];
            Assert.That(ring.Kind, Is.EqualTo(EmitterKind.Ring));
            Assert.That(ring.Every, Is.EqualTo(20));
            Assert.That(ring.Delay, Is.EqualTo(0));
            Assert.That(ring.Count, Is.EqualTo(24));
            Assert.That(ring.Speed, Is.EqualTo(Ranged.Fixed(117965)));
            Assert.That(ring.Spin, Is.EqualTo(1274));
            Assert.That(ring.Accel, Is.EqualTo(Ranged.Fixed(0)));
            Assert.That(ring.MaxSpeed, Is.EqualTo(16 * BulletPool.SpeedOne), "既定の上限");
            Assert.That(ring.Shape, Is.EqualTo(BulletShape.Orb));
            Assert.That(ring.Color, Is.EqualTo(BulletColor.Blue));
            Assert.That(ring.Life, Is.EqualTo(0));

            var aimed = first.Emitters[1];
            Assert.That(aimed.Kind, Is.EqualTo(EmitterKind.Aimed));
            Assert.That(aimed.Delay, Is.EqualTo(10));
            Assert.That(aimed.Spread, Is.EqualTo(Degrees(30)));
            Assert.That(aimed.Accel, Is.EqualTo(Ranged.Fixed(1311)));
            Assert.That(aimed.MaxSpeed, Is.EqualTo(Speed(4.5)));
            Assert.That(aimed.Shape, Is.EqualTo(BulletShape.Small), "既定の形");
            Assert.That(aimed.Color, Is.EqualTo(BulletColor.Red), "既定の色");

            var second = boss.Patterns[1];
            Assert.That(second.TimeTicks, Is.EqualTo(2400));
            Assert.That(second.Moves[0].Kind, Is.EqualTo(MoveKind.Wander));
            Assert.That(second.Moves[0].Area, Is.EqualTo(new Rect(96 * Field.Unit, 280 * Field.Unit, 288 * Field.Unit, 400 * Field.Unit)));
            var fan = second.Emitters[0];
            Assert.That(fan.Angle, Is.EqualTo(new Ranged(-Degrees(10), Degrees(10))));
            Assert.That(fan.Speed, Is.EqualTo(new Ranged(Speed(1.5), Speed(2.5))));
            Assert.That(fan.Curve, Is.EqualTo(Ranged.Fixed(73))); // 0.4 度 = 72.8
            Assert.That(fan.OffsetX, Is.EqualTo(Ranged.Fixed(0)));
            Assert.That(fan.OffsetY, Is.EqualTo(Ranged.Fixed(-8 * Field.Unit)));
            Assert.That(fan.Life, Is.EqualTo(300));
        }

        [Test]
        public void CrLfAndTabsAndComments_AreAccepted()
        {
            var text = "boss \"A\" radius=8\r\n\r\n# c\r\npattern \"P\" hp=1 time=1s\r\n\temit ring every=1 speed=1 # x\r\n";
            var result = ScriptParser.Parse(text);
            Assert.That(result.Errors, Is.Empty, string.Join("\n", result.Errors));
            Assert.That(result.Script!.Patterns[0].TimeTicks, Is.EqualTo(60));
        }

        [TestCase("time=0.5s", 30)]
        [TestCase("time=600s", 36000)]
        [TestCase("time=1", 1)]
        public void Seconds_ConvertToWholeTicks(string time, int ticks)
        {
            var result = ScriptParser.Parse($"boss \"A\" radius=8\npattern \"P\" hp=1 {time}\nemit ring every=1 speed=1");
            Assert.That(result.Errors, Is.Empty, string.Join("\n", result.Errors));
            Assert.That(result.Script!.Patterns[0].TimeTicks, Is.EqualTo(ticks));
        }

        /// <summary>誤りの種類ごとに、報告する行と列。</summary>
        [TestCase("pattern \"P\" hp=1 time=60\nemit ring every=1 speed=1", 1, 1, "boss")]
        [TestCase(Head + "emit ring every=1 speed=1 speed=2", 3, 27, "2 回")]
        [TestCase(Head + "emit ring every=1 speed=1 sped=2", 3, 27, "sped")]
        [TestCase(Head + "emit ring every=1", 3, 1, "speed がありません")]
        [TestCase(Head + "emit ring every=1 speed=17", 3, 25, "0〜16")]
        [TestCase(Head + "emit ring every=1 speed=x", 3, 25, "数か rand")]
        [TestCase(Head + "emit ring every=1.5 speed=1", 3, 17, "整数")]
        [TestCase(Head + "emit ring every=0 speed=1", 3, 1, "1 Tick 以上")]
        [TestCase(Head + "emit ring every=1 speed=rand(2,1)", 3, 25, "下限を上限以下")]
        [TestCase(Head + "emit ring every=1 speed=rand(1", 3, 25, "rand(下限, 上限)")]
        [TestCase(Head + "emit ring every=1 speed=5 maxspeed=4", 3, 19, "maxspeed 以下")]
        [TestCase(Head + "emit ring every=1 speed=1 spread=10", 3, 27, "spread は書けません")]
        [TestCase(Head + "emit spiral every=1 speed=1", 3, 6, "ring / fan / aimed")]
        [TestCase(Head + "emit ring every=1 speed=1 shape=star", 3, 27, "small / rice / orb / large")]
        [TestCase(Head + "emit ring every=1 speed=1 offset=1", 3, 27, "x,y")]
        [TestCase(Head + "emit ring every=1 speed=1 count=257", 3, 33, "1〜256")]
        [TestCase(Head + "move to at=385,10 over=10\nemit ring every=1 speed=1", 3, 12, "0〜384")]
        [TestCase(Head + "move wander area=100,100,50,200 over=10\nemit ring every=1 speed=1", 3, 13, "x1 <= x2")]
        [TestCase(Head + "move to over=10\nemit ring every=1 speed=1", 3, 1, "at がありません")]
        [TestCase(Head + "emit ring every=1 speed=1 % ", 3, 27, "使えない文字")]
        [TestCase(Head + "emit ring every=1 speed=1 count", 3, 27, "名前=値")]
        [TestCase("boss \"A radius=8", 1, 6, "閉じていません")]
        [TestCase("boss A radius=8\npattern \"P\" hp=1 time=60\nemit ring every=1 speed=1", 1, 6, "\"名前\"")]
        [TestCase("boss \"A\" radius=8\nemit ring every=1 speed=1", 2, 1, "pattern の中")]
        [TestCase("boss \"A\" radius=8\nboss \"B\" radius=8", 2, 1, "1 つだけ")]
        [TestCase(Head + "fire ring every=1", 3, 1, "未知のキーワード")]
        [TestCase("boss \"A\" radius=8\npattern \"P\" hp=1 time=60\npattern \"Q\" hp=1 time=60\nemit ring every=1 speed=1", 2, 1, "emit がありません")]
        [TestCase("boss \"A\" radius=8\n", 2, 1, "pattern が 1 つも")]
        [TestCase("boss \"A\" radius=8\npattern \"P\" hp=1 time=0.01s\nemit ring every=1 speed=1", 2, 23, "Tick の整数")]
        [TestCase("boss \"A\" radius=8\npattern \"P\" hp=0 time=60\nemit ring every=1 speed=1", 2, 16, "1〜100000")]
        public void Errors_ReportLineAndColumn(string text, int line, int column, string fragment)
        {
            var result = ScriptParser.Parse(text);
            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.Script, Is.Null);
            var error = result.Errors[0];
            Assert.That((error.Line, error.Column), Is.EqualTo((line, column)), error.ToString());
            Assert.That(error.Message, Does.Contain(fragment));
        }

        [Test]
        public void Errors_OnSeveralLines_AreAllReported()
        {
            var text = Head + "emit ring every=1 speed=99\nemit fan every=0 speed=1\nemit aimed every=1 speed=1\n";
            var result = ScriptParser.Parse(text);
            Assert.That(result.Errors.Select(e => e.Line), Is.EqualTo(_errorLines));
        }

        [Test]
        public void BrokenPatternLine_DoesNotCascadeIntoItsStatements()
        {
            var text = "boss \"A\" radius=8\npattern \"P\" time=60\nemit ring every=1 speed=1\nmove wait over=1\n";
            var result = ScriptParser.Parse(text);
            Assert.That(result.Errors.Count, Is.EqualTo(1), string.Join("\n", result.Errors));
            Assert.That(result.Errors[0].Message, Does.Contain("hp がありません"));
        }

        [Test]
        public void ParseOrThrow_ListsErrorsWithSourceName()
        {
            var error = Assert.Throws<FormatException>(() => ScriptParser.ParseOrThrow(Head + "emit ring every=1", "boss1.txt"));
            Assert.That(error!.Message, Does.Contain("boss1.txt:3:1:"));
            Assert.That(ScriptParser.ParseOrThrow(Sample, "sample").Patterns.Count, Is.EqualTo(2));
        }

        [Test]
        public void NullText_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => ScriptParser.Parse(null!));
        }

        private static readonly int[] _errorLines = { 3, 4 };

        private const string Head = "boss \"A\" radius=8\npattern \"P\" hp=1 time=60\n";
    }
}
