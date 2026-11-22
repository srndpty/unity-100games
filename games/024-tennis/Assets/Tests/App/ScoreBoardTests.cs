using System;
using Game.Core;
using NUnit.Framework;

namespace Game.App.Tests
{
    public sealed class ScoreBoardTests
    {
        private static readonly string[] _regularNames = { "0", "15", "30", "40" };

        private static readonly Verdict[] _verdicts =
        {
            Verdict.None, Verdict.Fault, Verdict.Net, Verdict.Out, Verdict.DoubleBounce, Verdict.Unreturned, Verdict.DoubleFault,
        };

        [Test]
        public void RegularGameUsesTennisNames()
        {
            var score = new MatchScore(6, 7, Side.Near);
            var names = new string[4];
            for (var i = 0; i < 4; i++)
            {
                names[i] = ScoreBoard.PointText(score, Side.Near);
                if (i < 3) { score.Award(Side.Near); }
            }

            Assert.That(names, Is.EqualTo(_regularNames));
            Assert.That(ScoreBoard.PointText(score, Side.Far), Is.EqualTo("0"));
        }

        [Test]
        public void DeuceAndAdvantage()
        {
            var score = new MatchScore(6, 7, Side.Near);
            for (var i = 0; i < 3; i++)
            {
                score.Award(Side.Near);
                score.Award(Side.Far);
            }

            Assert.That(ScoreBoard.IsDeuce(score), Is.True);
            score.Award(Side.Far);
            Assert.That(ScoreBoard.PointText(score, Side.Far), Is.EqualTo("AD"));
            Assert.That(ScoreBoard.PointText(score, Side.Near), Is.EqualTo("40"));
            Assert.That(ScoreBoard.IsDeuce(score), Is.False);
            score.Award(Side.Near);
            Assert.That(ScoreBoard.IsDeuce(score), Is.True, "デュースに戻る");
        }

        [Test]
        public void TiebreakUsesNumbers()
        {
            var score = new MatchScore(0, 7, Side.Near);
            for (var i = 0; i < 5; i++) { score.Award(Side.Near); }
            Assert.That(ScoreBoard.PointText(score, Side.Near), Is.EqualTo("5"));
            Assert.That(ScoreBoard.IsDeuce(score), Is.False);
        }

        [Test]
        public void EveryVerdictHasAText()
        {
            // Enum.GetValues<T> は Unity の Mono にないので、定義された値を数える。
            var defined = 0;
            for (var i = 0; i < 64; i++) { defined += Enum.IsDefined(typeof(Verdict), i) ? 1 : 0; }
            Assert.That(_verdicts.Length, Is.EqualTo(defined), "判定を足したらここにも足す");
            foreach (var verdict in _verdicts)
            {
                var text = ScoreBoard.VerdictText(verdict);
                Assert.That(text.Length == 0, Is.EqualTo(verdict == Verdict.None), verdict.ToString());
            }

            Assert.Throws<ArgumentOutOfRangeException>(() => ScoreBoard.VerdictText((Verdict)99));
        }
    }
}
