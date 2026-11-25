using NUnit.Framework;

namespace Game.Core.Tests
{
    public sealed class MoveTableTests
    {
        [Test]
        public void RealTable_LoadsTwoCharacters()
        {
            var table = FightLab.Table();
            Assert.That(table.Characters, Has.Count.EqualTo(2));
            Assert.That(table[0].Id, Is.EqualTo("cinder"));
            Assert.That(table[1].Id, Is.EqualTo("kiln"));
            Assert.That(table[0].Find("special").IsProjectile, Is.True);
            Assert.That(table[1].Find("special").Advance, Is.GreaterThan(0));
        }

        [Test]
        public void RealTable_ReadmeMatchesGeneratedSheet()
        {
            var table = FightLab.Table();
            Assert.That(FightLab.ReadmeText(), Does.Contain(table.ToMarkdown()));
        }

        [Test]
        public void Rejects_OneCharacter_ZeroStartup_AndShortFrames()
        {
            Assert.That(() => MoveTable.Parse("character only ひとり\n"), Throws.InstanceOf<System.FormatException>());
            var text = FightLab.MovesText();
            Assert.That(() => MoveTable.Parse(text.Replace("startup 4", "startup 0")), Throws.InstanceOf<System.FormatException>());
            Assert.That(() => MoveTable.Parse(text.Replace("repeat 4\n", "repeat 3\n")), Throws.InstanceOf<System.FormatException>());
            Assert.That(() => new DuelConfig { BufferTicks = 0 }.Validate(), Throws.InstanceOf<System.ArgumentOutOfRangeException>());
        }
    }
}
