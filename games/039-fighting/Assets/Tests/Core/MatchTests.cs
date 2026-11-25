using NUnit.Framework;

namespace Game.Core.Tests
{
    public sealed class MatchTests
    {
        [Test]
        public void Time_HigherRatioWins_EqualIsDraw_AndKoBeatsTheClock()
        {
            var draw = RunToRoundEnd(NewMatch(), delegate { });
            Assert.That(draw.Wins[0], Is.EqualTo(1));
            Assert.That(draw.Wins[1], Is.EqualTo(1));

            var match = NewMatch();
            SkipIntro(match);
            match.Duel.At(0).Health = 1000;
            RunToRoundEnd(match, delegate { });
            Assert.That(match.Wins[1], Is.EqualTo(1));
            Assert.That(match.Wins[0], Is.EqualTo(0));

            var ko = new Match(FightLab.Table(), new DuelConfig { IntroTicks = 2, RoundEndTicks = 2, RoundTicks = 30, MaxRounds = 3 }, 0, 0);
            SkipIntro(ko);
            ko.Duel.At(1).Health = 1;
            ko.Duel.At(0).Health = 1;
            FightLab.Place(ko.Duel, 30);
            ko.Step(new Pad(5, true, false, false), new Pad(5, true, false, false));
            for (int i = 0; i < 30 && ko.Phase == MatchPhase.Fight; i++) { ko.Step(Pad.Neutral, Pad.Neutral); }
            Assert.That(ko.Phase, Is.EqualTo(MatchPhase.RoundEnd));
            Assert.That(ko.Wins[0], Is.EqualTo(1));
            Assert.That(ko.Wins[1], Is.EqualTo(1));
        }

        [Test]
        public void TwoWins_EndTheMatch_DrawsForceAnExtra_ThenCutOff()
        {
            var match = NewMatch();
            for (int round = 0; round < 2; round++)
            {
                SkipIntro(match);
                match.Duel.At(1).Health = 1;
                FightLab.Place(match.Duel, 30);
                match.Step(new Pad(5, true, false, false), Pad.Neutral);
                while (match.Phase == MatchPhase.Fight) { match.Step(Pad.Neutral, Pad.Neutral); }
                while (match.Phase == MatchPhase.RoundEnd) { match.Step(Pad.Neutral, Pad.Neutral); }
            }

            Assert.That(match.IsOver, Is.True);
            Assert.That(match.Result, Is.EqualTo(MatchResult.Left));
            int wins = match.Wins[0];
            var health = match.Duel.At(0).Health;
            for (int i = 0; i < 20; i++) { match.Step(new Pad(6, true, true, true), new Pad(4, true, true, true)); }
            Assert.That(match.Wins[0], Is.EqualTo(wins));
            Assert.That(match.Duel.At(0).Health, Is.EqualTo(health));

            var extra = NewMatch();
            extra = RunDraws(extra, 2);
            Assert.That(extra.IsOver, Is.False, "同時に 2 本なら延長");
            extra = RunDraws(extra, 1);
            Assert.That(extra.Result, Is.EqualTo(MatchResult.Draw));
        }

        private static Match NewMatch()
        {
            var config = new DuelConfig { IntroTicks = 2, RoundEndTicks = 2, RoundTicks = 8, MaxRounds = 3 };
            return new Match(FightLab.Table(), config, 0, 1);
        }

        private static void SkipIntro(Match match)
        {
            while (match.Phase == MatchPhase.Intro) { match.Step(Pad.Neutral, Pad.Neutral); }
        }

        private static Match RunToRoundEnd(Match match, System.Action during)
        {
            SkipIntro(match);
            during();
            int guard = 0;
            while (match.Phase == MatchPhase.Fight && guard++ < 30) { match.Step(Pad.Neutral, Pad.Neutral); }
            Assert.That(match.Phase, Is.EqualTo(MatchPhase.RoundEnd));
            return match;
        }

        private static Match RunDraws(Match match, int rounds)
        {
            for (int i = 0; i < rounds; i++)
            {
                while (match.Phase != MatchPhase.Fight) { match.Step(Pad.Neutral, Pad.Neutral); }
                while (match.Phase == MatchPhase.Fight) { match.Step(Pad.Neutral, Pad.Neutral); }
                while (match.Phase == MatchPhase.RoundEnd && !match.IsOver) { match.Step(Pad.Neutral, Pad.Neutral); }
            }

            return match;
        }
    }
}
