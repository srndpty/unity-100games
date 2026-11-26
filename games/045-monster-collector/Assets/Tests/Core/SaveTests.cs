using System;
using System.Collections.Generic;
using System.Globalization;
using NUnit.Framework;

namespace Game.Core.Tests
{
    /// <summary>セーブ：往復で同じ状態・同じ未来、壊れたものは理由付きで拒否する。記録の保存。</summary>
    public sealed class SaveTests
    {
        /// <summary>手持ち 2・ボックス 1・倒したトレーナー 1 で回復所の前に立つ世界。</summary>
        private static World Saved(int seed = 3)
        {
            var w = World.New(Lab.Data, seed);
            w.ChooseStarter(2);
            w.DefeatTrainerForTest(0);
            var caught = Lab.Mon("brinecrab", 7);
            caught.Hp = 4;
            caught.Moves[0].Pp = 3;
            w.StartWildForTest(caught);
            w.Battle!.FoeMonster.Hp = 1;
            w.Battle.FoeMonster.SetStatus(Lab.Data.Status("sleep"), 99);
            w.Battle.Submit(BattleCommand.Ball());
            w.FinishBattle();
            w.Roster.Receive(Lab.Mon("mossling", 4));
            w.Roster.Deposit(2);
            w.Roster.Party[0].GainExp(Lab.Data.Rules, 300);
            w.Roster.Party[0].Hp = 9;
            w.AddPlayTicks(1234);
            w.WarpForTest("town", 10, 4, Direction.Up);
            return w;
        }

        private static string Text(World w)
        {
            Assert.That(SaveCodec.TryEncode(w, out var text, out var error), Is.True, error?.ToString());
            return text;
        }

        private static SaveError Reject(string text)
        {
            Assert.That(SaveCodec.TryDecode(Lab.Data, text, out var world, out var error), Is.False, "拒否されるはず");
            Assert.That(world, Is.Null);
            return error!;
        }

        /// <summary>本文を書き換えて、チェックサムを付け直す（値の検証の負例）。</summary>
        private static string Resign(string text, string from, string to)
        {
            Assert.That(text.Contains(from, StringComparison.Ordinal), Is.True, from);
            var body = text.Substring(0, text.IndexOf("checksum=", StringComparison.Ordinal)).Replace(from, to, StringComparison.Ordinal);
            return body + "checksum=" + SaveCodec.Checksum(body).ToString("x8", CultureInfo.InvariantCulture) + "\n";
        }

        [Test]
        public void RoundTripKeepsTheWorld_AndTheFuture()
        {
            var w = Saved();
            var text = Text(w);
            Assert.That(SaveCodec.TryDecode(Lab.Data, text, out var loaded, out var error), Is.True, error?.ToString());
            Assert.That(loaded!.Hash(), Is.EqualTo(w.Hash()));
            Assert.That(loaded.Roster.Party.Count, Is.EqualTo(2));
            Assert.That(loaded.Roster.Box.Count, Is.EqualTo(1));
            Assert.That(loaded.TrainersDefeated, Is.EqualTo(1));
            Assert.That(loaded.Roster.Party[1].Moves[0].Pp, Is.EqualTo(3));
            Assert.That(Text(loaded), Is.EqualTo(text));

            // 同じ入力を流すと、以後も同じ（遭遇の乱数・戦闘番号まで戻っている）。
            foreach (var x in new[] { w, loaded })
            {
                x.WarpForTest("route", 3, 2, Direction.Right);
                for (int i = 0; i < 3000 && x.Mode == WorldMode.Field; i++) { x.FieldTick(i / 8 % 2 == 0 ? Direction.Right : Direction.Left); }
                while (x.Mode == WorldMode.Battle && !x.Battle!.IsOver) { x.Battle.Submit(BattleCommand.Fight(0)); }
            }

            Assert.That(loaded.Hash(), Is.EqualTo(w.Hash()));
        }

        [Test]
        public void SavingOnlyAtTheCenter()
        {
            var w = Saved();
            w.WarpForTest("town", 10, 5, Direction.Up);
            Assert.That(SaveCodec.TryEncode(w, out _, out var error), Is.False);
            Assert.That(error!.Kind, Is.EqualTo(SaveErrorKind.NotAtCenter));
        }

        [Test]
        public void SavedRecordDoesNotChangeWithLaterPlay()
        {
            var w = Saved();
            var text = Text(w);
            w.Roster.Party[0].Hp = 1;
            w.AddPlayTicks(50);
            Assert.That(SaveCodec.TryDecode(Lab.Data, text, out var loaded, out _), Is.True);
            Assert.That(loaded!.Roster.Party[0].Hp, Is.EqualTo(9));
        }

        [Test]
        public void EveryTruncationIsRejected()
        {
            var text = Text(Saved());
            for (int cut = 0; cut < text.Length - 1; cut++)
            {
                var error = Reject(text.Substring(0, cut));
                Assert.That(new List<SaveErrorKind> { SaveErrorKind.NotASave, SaveErrorKind.Truncated, SaveErrorKind.Corrupted }, Has.Member(error.Kind), $"{cut} 文字で切った");
            }
        }

        [Test]
        public void EverySingleCharacterChangeIsRejected()
        {
            var text = Text(Saved());
            for (int i = 0; i < text.Length; i++)
            {
                if (text[i] == '\n') { continue; }
                var changed = text.Substring(0, i) + (text[i] == '1' ? '2' : '1') + text.Substring(i + 1);
                Reject(changed);
            }
        }

        [TestCase("version=1", "version=2", SaveErrorKind.VersionMismatch)]
        [TestCase("party=2", "party=7", SaveErrorKind.BadValue)]
        [TestCase("party=2", "party=0", SaveErrorKind.BadValue)]
        [TestCase("box=1", "box=31", SaveErrorKind.BadValue)]
        [TestCase("box=1", "box=2", SaveErrorKind.Truncated)]
        [TestCase("mon=brinecrab", "mon=dragon", SaveErrorKind.BadValue)]
        [TestCase("map=town", "map=route", SaveErrorKind.BadValue)]
        [TestCase("balls=4", "balls=100", SaveErrorKind.BadValue)]
        [TestCase("trainers=1", "trainers=7", SaveErrorKind.BadValue)]
        public void BadValuesAreRejectedWithAReason(string from, string to, SaveErrorKind kind)
        {
            var text = Text(Saved());
            Assert.That(Reject(Resign(text, from, to)).Kind, Is.EqualTo(kind));
        }

        [Test]
        public void ZeroRngStateIsRejected()
        {
            var w = Saved();
            var text = Text(w);
            Assert.That(Reject(Resign(text, $"encounter_rng={w.Explorer.EncounterRng.State}\n", "encounter_rng=0\n")).Kind, Is.EqualTo(SaveErrorKind.BadValue));
        }

        [Test]
        public void MonsterValuesAreChecked()
        {
            var text = Text(Saved());
            var line = text.Split('\n')[Array.FindIndex(text.Split('\n'), l => l.StartsWith("mon=brinecrab", StringComparison.Ordinal))];
            var p = line.Substring(4).Split(',');
            var bad = new[]
            {
                $"mon={p[0]},21,{p[2]},{p[3]},{p[4]}",
                $"mon={p[0]},{p[1]},0,{p[3]},{p[4]}",
                $"mon={p[0]},{p[1]},{p[2]},999,{p[4]}",
                $"mon={p[0]},{p[1]},{p[2]},{p[3]},splash:99",
                $"mon={p[0]},{p[1]},{p[2]},{p[3]},splash:1;splash:1",
                $"mon={p[0]},{p[1]},{p[2]},{p[3]},struggle:0",
                $"mon={p[0]},{p[1]},{p[2]},{p[3]}",
            };
            foreach (var b in bad)
            {
                Assert.That(Reject(Resign(text, line, b)).Kind, Is.EqualTo(SaveErrorKind.BadValue), b);
            }
        }

        [Test]
        public void DifferentDataIsRejected()
        {
            var text = Text(Saved());
            var other = Lab.ParseWith("Moves.txt", "move blaze ほむらうず ember 85 85 8 0", "move blaze ほむらうず ember 90 85 8 0");
            Assert.That(SaveCodec.TryDecode(other, text, out _, out var error), Is.False);
            Assert.That(error!.Kind, Is.EqualTo(SaveErrorKind.DataMismatch));
        }

        [Test]
        public void EmptyAndForeignTextIsNotASave()
        {
            Assert.That(Reject(string.Empty).Kind, Is.EqualTo(SaveErrorKind.NotASave));
            Assert.That(Reject("hello\n").Kind, Is.EqualTo(SaveErrorKind.NotASave));
        }

        [Test]
        public void RecordsKeepTheBest_AndRejectBrokenText()
        {
            var r = Records.Empty.WithClear(5000, 4).WithClear(7000, 6).WithClear(4000, 2);
            Assert.That((r.Clears, r.BestTicks, r.BestCaught), Is.EqualTo((3, 4000L, 6)));
            Assert.That(Records.TryDecode(r.Encode(), 6, out var back, out _), Is.True);
            Assert.That((back.Clears, back.BestTicks, back.BestCaught), Is.EqualTo((3, 4000L, 6)));
            var text = r.Encode();
            for (int cut = 0; cut < text.Length - 1; cut++) { Assert.That(Records.TryDecode(text.Substring(0, cut), 6, out _, out _), Is.False); }
            Assert.That(Records.TryDecode(text.Replace("best_caught=6", "best_caught=7", StringComparison.Ordinal), 6, out _, out _), Is.False);
        }
    }
}
