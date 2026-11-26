using System;
using System.Linq;
using System.Reflection;
using Game.Core.Runtime;
using NUnit.Framework;

namespace Game.Core.Tests
{
    /// <summary>実行器：送り・選択・表示状態・拒否・保存と復元。</summary>
    public sealed class RunnerTests
    {
        [Test]
        public void StartsAtFirstTextWithStageApplied()
        {
            var r = new ScriptRunner(Lab.Script());
            Assert.That(r.Phase, Is.EqualTo(RunnerPhase.Text));
            Assert.That(r.Current.Text, Is.EqualTo("こんばんは"));
            Assert.That(r.Script.Characters[r.Current.Speaker].Name, Is.EqualTo("澪"));
            Assert.That(r.Background, Is.EqualTo("hall"));
            Assert.That(r.Slot(StageSlot.Left), Is.EqualTo(new Portrait(0, "calm")));
            Assert.That(r.Slot(StageSlot.Right).IsEmpty, Is.True);
        }

        [Test]
        public void AdvanceReachesNarrationThenChoice()
        {
            var r = new ScriptRunner(Lab.Script());
            Assert.That(r.Advance().Accepted, Is.True);
            Assert.That(r.Current.Text, Is.EqualTo("扉が閉まる音がした。"));
            Assert.That(r.Current.Speaker, Is.EqualTo(-1));
            Assert.That(r.Advance().Accepted, Is.True);
            Assert.That(r.Phase, Is.EqualTo(RunnerPhase.Choice));
            Assert.That(r.Current.Options.Select(o => o.Text), Is.EqualTo(Lab.Of("鍵を調べる", "澪を信じる", "何もしない")));
        }

        [TestCase(0, "good")]
        [TestCase(1, "normal")]
        [TestCase(2, "bad")]
        public void ChoiceLeadsToEnding(int option, string ending)
        {
            var r = Lab.Play(Lab.Script(), option);
            Assert.That(r.Phase, Is.EqualTo(RunnerPhase.Ended));
            Assert.That(r.Script.Endings[r.EndingIndex].Id, Is.EqualTo(ending));
        }

        [Test]
        public void ChoiceAssignmentsApplyBeforeJump()
        {
            var r = AtChoice();
            Assert.That(r.Value("clue"), Is.EqualTo(0));
            r.Choose(0);
            Assert.That(r.Value("clue"), Is.EqualTo(1));
            Assert.That(r.Current.Text, Is.EqualTo("鍵は冷たかった。"));
            Assert.That(r.Slot(StageSlot.Right), Is.EqualTo(new Portrait(1, "calm")));
        }

        [Test]
        public void ConditionBoundary()
        {
            // clue >= 2：ちょうど 2 で成り立ち、1 足りない 1 では成り立たない。
            var src = Lab.Replace(Lab.LineOf("$if clue >= 1 -> good"), "$if clue >= 2 -> good");
            src = Lab.Replace(Lab.LineOf("- 鍵を調べる -> look_key { clue += 1 }", src), "- 鍵を調べる -> look_key { clue += 2 }", src);
            src = Lab.Replace(Lab.LineOf("- 何もしない -> verdict", src), "- 何もしない -> verdict { clue += 1 }", src);
            var script = Lab.Script(src);
            var exact = Lab.Play(script, 0);
            Assert.That(exact.Value("clue"), Is.EqualTo(2));
            Assert.That(script.Endings[exact.EndingIndex].Id, Is.EqualTo("good"));
            var shortBy1 = Lab.Play(script, 2);
            Assert.That(shortBy1.Value("clue"), Is.EqualTo(1));
            Assert.That(script.Endings[shortBy1.EndingIndex].Id, Is.EqualTo("bad"));
        }

        [Test]
        public void ShowingSameCharacterMovesSlotAndHideClears()
        {
            var src = Lab.Replace(Lab.LineOf("@show mio smile left"), "@show mio smile right\n@hide ren\n@show ren calm left\n@clear\n@show mio calm center");
            var r = AtChoice(Lab.Script(src));
            r.Choose(1);
            Assert.That(r.Slot(StageSlot.Left).IsEmpty, Is.True);
            Assert.That(r.Slot(StageSlot.Right).IsEmpty, Is.True);
            Assert.That(r.Slot(StageSlot.Center), Is.EqualTo(new Portrait(0, "calm")));

            var moved = Lab.Script(Lab.Replace(Lab.LineOf("@show mio smile left"), "@show mio smile right"));
            var m = AtChoice(moved);
            m.Choose(1);
            Assert.That(m.Slot(StageSlot.Left).IsEmpty, Is.True, "同じ人物は 1 枠だけ");
            Assert.That(m.Slot(StageSlot.Right), Is.EqualTo(new Portrait(0, "smile")));
        }

        [Test]
        public void WrongOperationsAreRejectedWithoutChange()
        {
            var r = new ScriptRunner(Lab.Script());
            var h = r.Hash();
            Assert.That(r.Choose(0).Accepted, Is.False, "文の待ちで選択");
            Assert.That(r.Hash(), Is.EqualTo(h));

            r = AtChoice();
            h = r.Hash();
            Assert.That(r.Advance().Accepted, Is.False, "選択肢の待ちで送り");
            Assert.That(r.Choose(3).Accepted, Is.False, "範囲外");
            Assert.That(r.Choose(-1).Accepted, Is.False, "負の番号");
            Assert.That(r.Hash(), Is.EqualTo(h));
            Assert.That(r.Value("clue"), Is.EqualTo(0));
        }

        [Test]
        public void EndedRejectsEverything()
        {
            var r = Lab.Play(Lab.Script(), 0);
            var h = r.Hash();
            Assert.That(r.Advance().Accepted, Is.False);
            Assert.That(r.Choose(0).Accepted, Is.False);
            Assert.That(r.Advance().Reason, Is.EqualTo("終わっています"));
            Assert.That(r.Hash(), Is.EqualTo(h));
        }

        [Test]
        public void ValuesSaturateAtLimit()
        {
            var src = Lab.Replace(Lab.LineOf("*verdict"), "*verdict\n$set n += 9999\n$set n += 9999\n$set n -= 9999\n$set n -= 9999\n$set n -= 9999\n$set n -= 9999", Lab.Replace(1, "$var n = 0"));
            var r = Lab.Play(Lab.Script(src), 0);
            Assert.That(r.Value("n"), Is.EqualTo(-9999));
        }

        [Test]
        public void CloneIsIndependent()
        {
            var a = AtChoice();
            var b = a.Clone();
            b.Choose(0);
            Assert.That(a.Phase, Is.EqualTo(RunnerPhase.Choice));
            Assert.That(a.Value("clue"), Is.EqualTo(0));
            Assert.That(b.Value("clue"), Is.EqualTo(1));
        }

        // ---- 題材固有：ロードで選択前状態へ戻る（Core の部分） ----

        [Test]
        public void RestoringChoiceSnapshotReturnsToBeforeTheChoice()
        {
            var r = AtChoice();
            var saved = r.Snapshot();
            var before = r.Hash();
            r.Choose(0);
            Assert.That(r.Value("clue"), Is.EqualTo(1));

            Assert.That(ScriptRunner.TryRestore(r.Script, saved, out var back, out var error), Is.True, error);
            Assert.That(back!.Phase, Is.EqualTo(RunnerPhase.Choice));
            Assert.That(back.Hash(), Is.EqualTo(before));
            Assert.That(back.Value("clue"), Is.EqualTo(0), "選択の代入は保存時点では未適用");
            Assert.That(back.Slot(StageSlot.Left), Is.EqualTo(new Portrait(0, "calm")));
            back.Choose(1);
            while (back.Phase == RunnerPhase.Text) { back.Advance(); }
            Assert.That(back.Script.Endings[back.EndingIndex].Id, Is.EqualTo("normal"), "別の選択肢で別の分岐へ");
        }

        [Test]
        public void RestoredRunnerFollowsSameFuture()
        {
            var a = AtChoice();
            Assert.That(ScriptRunner.TryRestore(a.Script, a.Snapshot(), out var b, out _), Is.True);
            a.Choose(0);
            b!.Choose(0);
            while (a.Phase == RunnerPhase.Text) { a.Advance(); b.Advance(); }
            Assert.That(b.Hash(), Is.EqualTo(a.Hash()));
        }

        [Test]
        public void SnapshotIsNotSharedWithRunner()
        {
            var r = AtChoice();
            var s = r.Snapshot();
            r.Choose(0);
            Assert.That(s.Variables[0], Is.EqualTo(0));
        }

        [Test]
        public void InvalidSnapshotsAreRejected()
        {
            var script = Lab.Script();
            var ok = AtChoice(script).Snapshot();
            var empty = Portrait.Empty;
            Reject(script, new RunnerSnapshot(-1, ok.Variables, ok.Background, ok.Slots), "スクリプトの外");
            Reject(script, new RunnerSnapshot(script.Instructions.Count, ok.Variables, ok.Background, ok.Slots), "スクリプトの外");
            Reject(script, new RunnerSnapshot(0, ok.Variables, ok.Background, ok.Slots), "文でも選択肢でもありません");
            var ending = script.Instructions.ToList().FindIndex(i => i.Kind == InstructionKind.Ending);
            Reject(script, new RunnerSnapshot(ending, ok.Variables, ok.Background, ok.Slots), "文でも選択肢でもありません");
            Reject(script, new RunnerSnapshot(ok.Pc, Lab.Ints(0), ok.Background, ok.Slots), "変数の数");
            Reject(script, new RunnerSnapshot(ok.Pc, Lab.Ints(0, 2), ok.Background, ok.Slots), "trusted の値 2");
            Reject(script, new RunnerSnapshot(ok.Pc, Lab.Ints(10000, 0), ok.Background, ok.Slots), "clue の値 10000");
            Reject(script, new RunnerSnapshot(ok.Pc, ok.Variables, "cellar", ok.Slots), "背景 cellar");
            Reject(script, new RunnerSnapshot(ok.Pc, ok.Variables, ok.Background, new[] { empty, empty }), "枠の数");
            Reject(script, new RunnerSnapshot(ok.Pc, ok.Variables, ok.Background, new[] { new Portrait(0, "calm"), new Portrait(0, "calm"), empty }), "2 枠");
            Reject(script, new RunnerSnapshot(ok.Pc, ok.Variables, ok.Background, new[] { new Portrait(5, "calm"), empty, empty }), "人物 5");
            Reject(script, new RunnerSnapshot(ok.Pc, ok.Variables, ok.Background, new[] { new Portrait(1, "smile"), empty, empty }), "ren smile");
        }

        // ---- DSL と実行器の分離 ----

        [Test]
        public void RuntimeDoesNotDependOnParser()
        {
            var runtime = typeof(ScriptRunner).Assembly.GetTypes().Where(t => t.Namespace == "Game.Core.Runtime").ToList();
            Assert.That(runtime, Has.Member(typeof(ScriptRunner)));
            const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
            foreach (var t in runtime)
            {
                foreach (var f in t.GetFields(all)) { AssertRuntime(t, f.FieldType); }
                foreach (var p in t.GetProperties(all)) { AssertRuntime(t, p.PropertyType); }
                foreach (var m in t.GetMethods(all).Cast<MethodBase>().Concat(t.GetConstructors(all)))
                {
                    if (m is MethodInfo mi) { AssertRuntime(t, mi.ReturnType); }
                    foreach (var p in m.GetParameters()) { AssertRuntime(t, p.ParameterType); }
                }
            }
        }

        private static void AssertRuntime(Type owner, Type used)
        {
            var t = used.IsByRef || used.IsArray ? used.GetElementType()! : used;
            Assert.That(t.Namespace, Is.Not.EqualTo("Game.Core.Script"), $"{owner.Name} が {t.Name} を使っている");
            if (t.IsGenericType)
            {
                foreach (var a in t.GetGenericArguments()) { AssertRuntime(owner, a); }
            }
        }

        private static void Reject(CompiledScript script, RunnerSnapshot s, string fragment)
        {
            Assert.That(ScriptRunner.TryRestore(script, s, out var r, out var error), Is.False, fragment);
            Assert.That(r, Is.Null);
            Assert.That(error, Does.Contain(fragment));
        }

        private static ScriptRunner AtChoice(CompiledScript? script = null)
        {
            var r = new ScriptRunner(script ?? Lab.Script());
            while (r.Phase == RunnerPhase.Text) { r.Advance(); }
            Assert.That(r.Phase, Is.EqualTo(RunnerPhase.Choice));
            return r;
        }
    }
}
