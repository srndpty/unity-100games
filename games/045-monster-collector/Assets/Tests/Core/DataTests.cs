using System;
using NUnit.Framework;

namespace Game.Core.Tests
{
    /// <summary>データの読み込みと検証。既定のデータが通り、1 か所ずつ壊すと理由付きで拒否する。</summary>
    public sealed class DataTests
    {
        [Test]
        public void ShippedDataIsValid()
        {
            var data = Lab.Load();
            Assert.That(data.Types.Count, Is.EqualTo(3));
            Assert.That(data.Statuses.Count, Is.EqualTo(2));
            Assert.That(data.Species.Count, Is.EqualTo(6));
            Assert.That(data.Trainers.Count, Is.EqualTo(3));
            Assert.That(MonsterDataValidator.Validate(data), Is.Empty);
        }

        [TestCase("Moves.txt", "move blaze ほむらうず ember 85 85 8 0", "move blaze ほむらうず ember 85 0 8 0", "命中は 1〜100")]
        [TestCase("Moves.txt", "move blaze ほむらうず ember 85 85 8 0", "move blaze ほむらうず ember 85 101 8 0", "命中は 1〜100")]
        [TestCase("Moves.txt", "move spark ひのこ ember 40 100 25 0\ndo damage", "move spark ひのこ ember 40 100 25 0\ndo heal 10", "ダメージの効果がありません")]
        [TestCase("Moves.txt", "move harden かたくなる - 0 100 20 0\ndo stat self def 1", "move harden かたくなる - 0 100 20 0\ndo damage", "威力 0 なのにダメージ")]
        [TestCase("Moves.txt", "do inflict poison 30", "do inflict poison 0", "確率は 1〜100%")]
        [TestCase("Moves.txt", "do stat self def 1", "do stat self def 4", "能力の段")]
        [TestCase("Moves.txt", "move quickjab すばやづき - 30 100 20 1", "move quickjab すばやづき - 30 100 0 1", "PP 0 は わるあがき")]
        [TestCase("Moves.txt", "move quickjab すばやづき - 30 100 20 1", "move spark ダブり ember 40 100 25 0\ndo damage\nmove quickjab すばやづき - 30 100 20 1", "技 spark が重複")]
        [TestCase("Types.txt", "chart ember moss 200", "chart ember moss 150", "50・100・200")]
        [TestCase("Statuses.txt", "status sleep 眠り 1 0 2 3 200", "status sleep 眠り 1 0 3 2 200", "持続は")]
        [TestCase("Statuses.txt", "status sleep 眠り 1 0 2 3 200", "status sleep 眠り 0 0 2 3 200", "効果がありません")]
        [TestCase("Species.txt", "species burrowroot ネモグラ moss 56 56 46 40 190 54", "species brinecrab ダブり tide 45 60 62 35 190 54\nlearn 1 splash\nspecies burrowroot ネモグラ moss 56 56 46 40 190 54", "種族 brinecrab が重複")]
        [TestCase("Species.txt", "species wickmoth トモシガ ember 40 48 40 70 190 50", "species wickmoth トモシガ ember 40 48 40 70 0 50", "捕獲率")]
        [TestCase("Species.txt", "learn 18 blaze", "learn 2 blaze", "昇順")]
        [TestCase("Species.txt", "learn 1 quickjab", "learn 1 struggle", "わるあがき を覚えられません")]
        [TestCase("World.txt", "rule party_max 6", "rule party_max 7", "party_max")]
        [TestCase("World.txt", "rule level_cap 20", "rule level_cap 4", "starter_level")]
        [TestCase("World.txt", "rule center town 10 4", "rule center town 10 5", "カウンター")]
        [TestCase("World.txt", "starter mossling", "starter cinderpup", "属性がすべて違う")]
        [TestCase("World.txt", "wild route \" wickmoth 3 5 35", "wild route \" wickmoth 6 5 35", "レベルは")]
        [TestCase("World.txt", "wild route \" wickmoth 3 5 35", "wild route \" wickmoth 3 5 0", "重み")]
        [TestCase("World.txt", "wild route \" wickmoth 3 5 35", "wild route \" wickmoth 3 5 2147483647", "重みの合計")]
        [TestCase("World.txt", "grass route : 15", "grass route : 0", "遭遇率")]
        [TestCase("World.txt", "trainer touma 茨守りのトウマ route 40 7 left", "trainer touma 茨守りのトウマ route 26 7 left", "同じマス")]
        [TestCase("World.txt", "trainer touma 茨守りのトウマ route 40 7 left", "trainer touma 茨守りのトウマ route 40 0 left", "歩けるマス")]
        [TestCase("World.txt", "portal route 0 7 town 18 6 left", "portal route 0 7 town 19 6 left", "出入口の上")]
        [TestCase("World.txt", "tmon touma ripplet 14", "tmon touma ripplet 21", "レベルは 1〜20")]
        public void BrokenDataIsRejectedWithAReason(string file, string from, string to, string reason)
        {
            var data = Lab.ParseWith(file, from, to);
            var errors = MonsterDataValidator.Validate(data);
            Assert.That(errors.Exists(e => e.Contains(reason, StringComparison.Ordinal)), Is.True, $"「{reason}」を含む理由がない：{string.Join(" / ", errors)}");
        }

        [Test]
        public void ASeventhSpeciesIsRejected()
        {
            var data = Lab.ParseWith(
                "Species.txt",
                "species burrowroot ネモグラ moss 56 56 46 40 190 54",
                "species extra ナナツメ moss 50 50 50 50 100 50\nlearn 1 tackle\nspecies burrowroot ネモグラ moss 56 56 46 40 190 54");
            Assert.That(MonsterDataValidator.Validate(data).Exists(e => e.Contains("種族は 6 種", StringComparison.Ordinal)), Is.True);
        }

        [TestCase("Moves.txt", "do inflict poison 100", "do inflict fever 100")]
        [TestCase("Moves.txt", "move spark ひのこ ember", "move spark ひのこ fire")]
        [TestCase("Species.txt", "learn 5 spark", "learn 5 sparkle")]
        [TestCase("Species.txt", "learn 5 spark", "learn five spark")]
        [TestCase("World.txt", "tmon saki wickmoth 7", "tmon saki dragon 7")]
        [TestCase("World.txt", "row #..HHHH......HHHH..#", "row #..HHHH......HHHH.#")]
        [TestCase("World.txt", "rule step_ticks 8", "rule step_speed 8")]
        [TestCase("World.txt", "portal town 19 6 route 1 7 right", "portal town 19 6 route 1 7 north")]
        public void SyntaxErrorsAreFormatExceptions(string file, string from, string to)
        {
            Assert.Throws<FormatException>(() => Lab.ParseWith(file, from, to));
        }

        [Test]
        public void LoadThrowsWithAllReasons()
        {
            var ex = Assert.Throws<InvalidOperationException>(() => MonsterData.Load(
                Lab.DataText("Types.txt"),
                Lab.DataText("Statuses.txt"),
                Lab.DataText("Moves.txt").Replace("move blaze ほむらうず ember 85 85 8 0", "move blaze ほむらうず ember 85 0 8 0", StringComparison.Ordinal),
                Lab.DataText("Species.txt"),
                Lab.DataText("World.txt")));
            Assert.That(ex!.Message.Contains("blaze", System.StringComparison.Ordinal), Is.True, ex!.Message);
        }
    }
}
