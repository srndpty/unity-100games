using System;
using System.Collections.Generic;
using Game.Core.Mechanisms;
using Game.Core.Play;
using IntGeometry;
using NUnit.Framework;

namespace Game.Core.Tests
{
    public sealed class MechanismTests
    {
        [Test]
        public void Table_AcceptsOnlyInstallWhenLocked_AndTurnWhenActive_AndNothingWhenSolved()
        {
            Assert.That(MechanismTable.Lookup(Phase.Locked, MechanismOp.Install).Kind, Is.EqualTo(TransitionKind.Install));
            Assert.That(MechanismTable.Lookup(Phase.Locked, MechanismOp.Turn).Refusal, Is.EqualTo(Refusal.Locked));
            Assert.That(MechanismTable.Lookup(Phase.Locked, MechanismOp.Probe).Refusal, Is.EqualTo(Refusal.Locked));
            Assert.That(MechanismTable.Lookup(Phase.Active, MechanismOp.Install).Refusal, Is.EqualTo(Refusal.AlreadyInstalled));
            Assert.That(MechanismTable.Lookup(Phase.Active, MechanismOp.Turn).Kind, Is.EqualTo(TransitionKind.Turn));
            Assert.That(MechanismTable.Lookup(Phase.Active, MechanismOp.Probe).Refusal, Is.EqualTo(Refusal.NotAllowed));
            foreach (var op in new[] { MechanismOp.Install, MechanismOp.Turn, MechanismOp.Probe })
            {
                Assert.That(MechanismTable.Lookup(Phase.Solved, op).Refusal, Is.EqualTo(Refusal.AlreadySolved), "解けた仕掛けは何も受け付けない");
            }
        }

        [Test]
        public void CyclicLocks_WrapAround_AndSolutionReachesAnswerFromAnyValues()
        {
            var r = new SeededRandom(5);
            foreach (var kind in new[] { MechanismKind.Dial, MechanismKind.Rings, MechanismKind.Clock })
            {
                for (int seed = 0; seed < 50; seed++)
                {
                    var m = (CyclicLock)MechanismFactory.Create(kind, new RandomSeries(seed));
                    var values = new int[m.ValueCount];
                    m.Reset(values);
                    Assert.That(m.IsSolved(values), Is.False, "始めの値は答えでない");
                    for (int i = 0; i < values.Length; i++) { values[i] = r.Next(m.Positions); }
                    foreach (var slot in m.SolutionFrom(values))
                    {
                        m.Turn(values, slot, out var mistake);
                        Assert.That(mistake, Is.False);
                    }

                    Assert.That(m.IsSolved(values), Is.True, $"{kind} seed {seed}");
                }
            }

            var dial = new DialLock(new RandomSeries(1));
            var v = new[] { 9, 0, 0 };
            dial.Turn(v, 0, out _);
            Assert.That(v[0], Is.Zero, "9 の次は 0");
        }

        [Test]
        public void Sequence_WrongPressRestarts_AndFullAnswerOpensFromAnyProgress()
        {
            var m = new SequenceLock(new RandomSeries(3));
            var a = m.Answer;
            Assert.That(new HashSet<int>(a).Count, Is.EqualTo(SequenceLock.GemCount), "答えは 4 つの宝石の並べ替え");
            var values = new int[1];
            m.Turn(values, a[0], out var mistake);
            m.Turn(values, a[1], out _);
            Assert.That(mistake, Is.False);
            Assert.That(values[0], Is.EqualTo(2));
            m.Turn(values, a[3], out mistake);
            Assert.That(mistake, Is.True);
            Assert.That(values[0], Is.Zero, "間違えたら最初から");
            m.Turn(values, a[0], out _);
            m.Turn(values, a[0], out mistake);
            Assert.That(mistake, Is.True);
            Assert.That(values[0], Is.EqualTo(1), "間違えた宝石が答えの最初なら 1 つ目として数える");

            for (int progress = 0; progress < SequenceLock.GemCount; progress++)
            {
                values[0] = progress;
                foreach (var g in m.SolutionFrom(values)) { m.Turn(values, g, out _); }
                Assert.That(m.IsSolved(values), Is.True, $"押した数 {progress} からでも答えの順に全部押せば開く");
            }
        }

        [Test]
        public void Answers_DependOnSeed_AndAreDeterministic()
        {
            var seen = new HashSet<string>();
            for (int seed = 1; seed <= 40; seed++)
            {
                var room = new Room(Lab.Study, seed);
                var again = new Room(Lab.Study, seed);
                var text = "";
                for (int m = 0; m < room.Mechanisms.Count; m++)
                {
                    Assert.That(room.Mechanisms[m].Answer, Is.EqualTo(again.Mechanisms[m].Answer), "同じシードなら同じ答え");
                    text += room.Mechanisms[m].AnswerText + "/";
                }

                seen.Add(text);
            }

            Assert.That(seen.Count, Is.EqualTo(40), "シードが違えば答えも違う");
        }

        [Test]
        public void AnswerText_DescribesTheAnswer()
        {
            var clock = new ClockHands(new RandomSeries(2));
            Assert.That(ClockHands.TimeText(0, 0), Is.EqualTo("12 時 00 分"));
            Assert.That(ClockHands.TimeText(3, 8), Is.EqualTo("3 時 40 分"));
            Assert.That(clock.AnswerText, Is.EqualTo(ClockHands.TimeText(clock.Answer[0], clock.Answer[1])));
            var rings = new RingLock(new RandomSeries(2));
            Assert.That(rings.AnswerText.StartsWith("上から " + RingLock.Constellations[rings.Answer[0]], StringComparison.Ordinal), Is.True, rings.AnswerText);
        }
    }
}
