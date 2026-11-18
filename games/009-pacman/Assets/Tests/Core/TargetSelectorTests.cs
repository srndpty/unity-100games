using NUnit.Framework;

namespace Game.Core.Tests
{
    public sealed class TargetSelectorTests
    {
        private static readonly GridPoint _corner = new(0, 16);

        private static TargetContext Context(GridPoint self) =>
            new(new GridPoint(10, 4), Direction.Right, self, new GridPoint(6, 4), _corner);

        [Test]
        public void Direct_TargetsPlayerCell()
        {
            Assert.That(new DirectSelector().ChooseTarget(Context(new GridPoint(1, 1))), Is.EqualTo(new GridPoint(10, 4)));
        }

        [Test]
        public void Ambush_TargetsCellsAheadOfPlayer()
        {
            Assert.That(new AmbushSelector(4).ChooseTarget(Context(new GridPoint(1, 1))), Is.EqualTo(new GridPoint(14, 4)));
        }

        [Test]
        public void Pincer_MirrorsLeaderAroundPivot()
        {
            Assert.That(new PincerSelector(2).ChooseTarget(Context(new GridPoint(1, 1))), Is.EqualTo(new GridPoint(18, 4)));
        }

        [Test]
        public void Shy_ChasesAtExactlyRadius()
        {
            Assert.That(new ShySelector(8).ChooseTarget(Context(new GridPoint(2, 4))), Is.EqualTo(new GridPoint(10, 4)));
        }

        [Test]
        public void Shy_RetreatsJustInsideRadius()
        {
            Assert.That(new ShySelector(8).ChooseTarget(Context(new GridPoint(3, 4))), Is.EqualTo(_corner));
        }

        [Test]
        public void AllFour_PickDifferentTargetsInSameSituation()
        {
            var context = Context(new GridPoint(9, 4));
            var targets = new[]
            {
                new DirectSelector().ChooseTarget(context),
                new AmbushSelector(4).ChooseTarget(context),
                new PincerSelector(2).ChooseTarget(context),
                new ShySelector(8).ChooseTarget(context),
            };
            Assert.That(targets, Is.Unique);
        }
    }
}
