using System;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using Game.Core.Analysis;
using Game.Core.Play;
using Game.Core.Rules;
using NUnit.Framework;

namespace Game.Core.Tests
{
    /// <summary>テストの道具。小さなテスト用ルール表（2 部屋・3 謎・人物 1）。</summary>
    internal static class Lab
    {
        /// <summary>
        /// 桟橋で包丁を拾い漁網を切る → 鍵で小屋の扉を開ける → 漁師に話を聞いて瓶に油をもらい、
        /// 布と棒で松明を作って、小屋のランプに油を注いで灯す（脱出）。
        /// </summary>
        public const string Small = @"# テスト用
@start dock
@room dock 桟橋 width 1000 start 100
@room shed 小屋 width 800 start 700
@object dock.knife 包丁 rect 200 0 260 60 stand 230
@object dock.bottle 瓶 rect 100 0 160 80 stand 130
@object dock.cloth 布 rect 300 0 360 40 stand 330
@object dock.net 漁網 rect 400 100 600 300 stand 500
@npc dock.man 漁師 rect 700 0 800 400 stand 660
@exit dock.door 小屋の扉 rect 940 0 1000 500 stand 970 -> shed at 50 locked
@exit shed.out 外 rect 0 0 40 500 stand 20 -> dock at 960
@object shed.stick 棒 rect 300 0 330 120 stand 315
@object shed.lamp ランプ rect 600 300 700 500 stand 650
@item knife 包丁
@item key 鍵
@item bottle 瓶
@item oil 油の瓶
@item cloth 布
@item stick 棒
@item torch 松明
@flag asked
@flag lamp_oiled
@puzzle cut_net 網を切る
@puzzle open_door 扉を開ける after cut_net
@puzzle light 灯をともす after open_door
@goal light

look dock.knife : 「錆びた包丁だ。」
take dock.knife : give knife ; hide dock.knife ; 「包丁を拾った。」
look dock.bottle : 「空き瓶だ。」
take dock.bottle : give bottle ; hide dock.bottle ; 「瓶を拾った。」
look dock.cloth : 「ぼろ布だ。」
take dock.cloth : give cloth ; hide dock.cloth ; 「布を拾った。」
look dock.net : 「古い漁網に何か絡まっている。」
use knife on dock.net : solve cut_net ; give key ; 「網を切ると鍵が落ちた。」
look dock.man : 「日に焼けた漁師だ。」
talk dock.man : 「こんにちは。」
topic dock.man oil 「油はありますか？」 when !asked : set asked ; 「瓶があれば分けてやる。」
topic dock.man weather 「いい天気ですね。」 : 「嵐が来るぞ。」
use bottle on dock.man when asked : lose bottle ; give oil ; 「瓶に油を入れてもらった。」
look dock.door : 「小屋の扉だ。」
use key on dock.door : unlock dock.door ; solve open_door ; lose key ; 「扉が開いた。」
look shed.out : 「外へ出る戸口。」
look shed.stick : 「手頃な棒だ。」
take shed.stick : give stick ; hide shed.stick ; 「棒を拾った。」
look shed.lamp : 「油の切れたランプ。」
use oil on shed.lamp : lose oil ; set lamp_oiled ; 「ランプに油を注いだ。」
use torch on shed.lamp when lamp_oiled : solve light ; 「灯がともった。」
use torch on shed.lamp when !lamp_oiled : 「油がない。」
use cloth on stick : lose cloth ; lose stick ; give torch ; 「棒に布を巻いて松明にした。」
look knife : 「錆びている。」
look key : 「小さな鍵。」
look bottle : 「空っぽだ。」
look oil : 「油が入っている。」
look cloth : 「ぼろ布。」
look stick : 「棒。」
look torch : 「火のついた松明。」
";

        private static CompileResult? _small;

        public static CompileResult SmallResult => _small ??= RuleCompiler.Compile(Small, new BookLimits());

        public static RuleBook Book => SmallResult.Book ?? throw new InvalidOperationException(string.Join("\n", SmallResult.Errors));

        public static World NewWorld(int walkSpeed = 10) => new(Book, new WorldSettings { WalkSpeed = walkSpeed });

        public static int H(string id)
        {
            var i = Book.HotspotIndex(id);
            Assert.That(i, Is.GreaterThanOrEqualTo(0), id);
            return i;
        }

        public static int I(string id)
        {
            var i = Book.ItemIndex(id);
            Assert.That(i, Is.GreaterThanOrEqualTo(0), id);
            return i;
        }

        public static Target HT(string id) => Target.Hotspot(H(id));

        public static Target IT(string id) => Target.Item(I(id));

        /// <summary>歩き終えるまで Step する（行為の解決の出来事を返す）。</summary>
        public static AdventureEvent[] Arrive(World w)
        {
            for (int i = 0; i < 10000; i++)
            {
                w.Step();
                if (!w.IsWalking) { return w.Events.ToArray(); }
            }

            throw new InvalidOperationException("着きません");
        }

        /// <summary>部屋の物への行為を命じて着くまで進める。</summary>
        public static AdventureEvent[] Do(World w, Verb verb, string target, string? tool = null)
        {
            var o = w.Act(verb, HT(target), tool == null ? -1 : I(tool));
            Assert.That(o.Accepted, Is.True, $"{verb} {target} {tool}: {o.Reason}");
            return Arrive(w);
        }

        /// <summary>開始から小屋の扉を開けて小屋に入るまで。</summary>
        public static World IntoShed()
        {
            var w = NewWorld();
            Do(w, Verb.Take, "dock.knife");
            Do(w, Verb.Use, "dock.net", "knife");
            Do(w, Verb.Use, "dock.door", "key");
            Do(w, Verb.Walk, "dock.door");
            Assert.That(w.Room, Is.EqualTo(Book.RoomIndex("shed")));
            return w;
        }

        public static string[] Said(AdventureEvent[] events) =>
            events.Where(e => e.Kind == EventKind.Said).Select(e => e.Text).ToArray();

        /// <summary>1 行だけ書き換えた本文。</summary>
        public static string Replace(string from, string to)
        {
            Assert.That(Small.Contains(from, StringComparison.Ordinal), Is.True, from);
            return Small.Replace(from, to, StringComparison.Ordinal);
        }

        public static string DataText(string name, [CallerFilePath] string source = "")
        {
            var dir = Path.GetDirectoryName(source) ?? ".";
            return File.ReadAllText(Path.GetFullPath(Path.Combine(dir, "..", "..", "Game", "Data", name)));
        }

        public static string ReadmeText([CallerFilePath] string source = "")
        {
            var dir = Path.GetDirectoryName(source) ?? ".";
            return File.ReadAllText(Path.GetFullPath(Path.Combine(dir, "..", "..", "..", "README.md")));
        }

        /// <summary>手の列を、遊ぶときと同じく歩いて着いた Tick に解決させて進める。</summary>
        public static World PlayWalking(RuleBook book, System.Collections.Generic.IEnumerable<Move> moves, int walkSpeed = 10)
        {
            var w = new World(book, new WorldSettings { WalkSpeed = walkSpeed });
            foreach (var m in moves)
            {
                Outcome o;
                switch (m.Kind)
                {
                    case MoveKind.Ask:
                        o = w.Ask(m.Topic);
                        break;
                    case MoveKind.EndTalk:
                        o = w.EndTalk();
                        break;
                    default:
                        o = w.Act(m.Verb, m.Target, m.Tool);
                        break;
                }

                Assert.That(o.Accepted, Is.True, $"{m.Describe(book)}: {o.Reason}");
                for (int i = 0; i < 10000 && w.IsWalking; i++) { w.Step(); }
            }

            return w;
        }

        /// <summary>探索とは独立に、手の列を深さ優先でたどって到達状態を列挙する。</summary>
        public static World[] Enumerate(RuleBook book)
        {
            var seen = new System.Collections.Generic.Dictionary<string, World>(StringComparer.Ordinal);
            var stack = new System.Collections.Generic.Stack<World>();
            var start = new World(book, new WorldSettings());
            seen[start.AbstractKey()] = start;
            stack.Push(start);
            while (stack.Count > 0)
            {
                var w = stack.Pop();
                foreach (var m in StateSpace.Moves(w))
                {
                    var n = w.Clone();
                    m.ApplyInstantly(n);
                    var k = n.AbstractKey();
                    if (seen.ContainsKey(k)) { continue; }
                    seen[k] = n;
                    stack.Push(n);
                }
            }

            return seen.Values.ToArray();
        }

        /// <summary>
        /// 探索とは独立に、脱出できない到達状態の鍵を求める。全ての状態と次の状態を列挙し、
        /// 「脱出した、または次のどれかが脱出できる」が変わらなくなるまで繰り返す（不動点）。
        /// </summary>
        public static string[] StuckStates(RuleBook book)
        {
            var states = Enumerate(book);
            var keys = states.Select(w => w.AbstractKey()).ToArray();
            var index = new System.Collections.Generic.Dictionary<string, int>(StringComparer.Ordinal);
            for (int i = 0; i < keys.Length; i++) { index[keys[i]] = i; }
            var next = states.Select(w => StateSpace.Moves(w).Select(m =>
            {
                var n = w.Clone();
                m.ApplyInstantly(n);
                return index[n.AbstractKey()];
            }).ToArray()).ToArray();
            var ok = states.Select(w => w.Escaped).ToArray();
            bool changed = true;
            while (changed)
            {
                changed = false;
                for (int i = 0; i < ok.Length; i++)
                {
                    if (ok[i] || !next[i].Any(n => ok[n])) { continue; }
                    ok[i] = true;
                    changed = true;
                }
            }

            return keys.Where((k, i) => !ok[i]).ToArray();
        }

        /// <summary>配列の定数を引数に直接書かない（CA1861）ための道具。</summary>
        public static string[] Of(params string[] items) => items;

        public static int[] Ints(params int[] items) => items;

        public static CompileResult Compile(string source, BookLimits? limits = null) => RuleCompiler.Compile(source, limits ?? new BookLimits());

        /// <summary>誤りの文に部分文字列を含むものがあるか（ICU に頼らず Ordinal で）。</summary>
        public static bool HasError(CompileResult r, string fragment, int line = -1) =>
            r.Errors.Any(e => e.Message.Contains(fragment, StringComparison.Ordinal) && (line < 0 || e.Line == line));

        public static string Dump(CompileResult r) => string.Join("\n", r.Errors);

        /// <summary>本文の中の文字列が最初に現れる行番号（1 から）。</summary>
        public static int LineOf(string source, string fragment)
        {
            var lines = source.Replace("\r\n", "\n").Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                if (lines[i].Contains(fragment, StringComparison.Ordinal)) { return i + 1; }
            }

            throw new ArgumentException(fragment);
        }
    }
}
