using System;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using Game.Core.Data;
using Game.Core.Play;
using NUnit.Framework;

namespace Game.Core.Tests
{
    /// <summary>テストの道具。小さなテスト用データ（8 日・攻略対象 1 人・イベント 5・ED 2）。</summary>
    internal static class Lab
    {
        /// <summary>
        /// 8 日（月曜始まり、7 日目は日曜で「会う」は選べない）。会うと最初に e_first（選択肢）。夜は好感度があれば e_a（e_b は同じ優先度で後の定義）。
        /// 行動の後に 50 % で e_c（e_a と排他）。疲労 100 の翌朝は休むしか選べない。
        /// </summary>
        public const string Small = @"# テスト用
@calendar 8 mon
@stat study 学力 0..100 = 10
@stat fatigue 疲労 0..100 = 0 cost
@flag met
@char aoi 葵 #ffaa88 faces normal,smile
@heart aoi 0..100 = 0
@bg room 自室
@bg park 公園
@action study 勉強 : study += 3..5 ; fatigue += 30
@action rest 休む bg room : fatigue -= 50
@action meet 会う week mon..sat bg park : aoi += 10
@outcome great 大成功 x200 weight 20
@outcome good 成功 x100 weight 60
@outcome bad 失敗 x50 weight 20
@weight bad 80 when fatigue >= 80
@forced rest when fatigue >= 100

@event e_first slot after day 1..8 priority 50 chance 100 once when action == meet
aoi「はじめまして」
aoi:smile「よろしくね」
- よろしく : aoi += 5 ; set met
- …… : aoi += 1
@end

@event e_a slot night day 1..8 priority 10 chance 100 when aoi >= 1
葵からメッセージが届いた。
@end

@event e_b slot night day 1..8 priority 10 chance 100 when aoi >= 1
同じ優先度で後に定義した夜のイベント。
@end

@event e_c slot after day 1..8 priority 5 chance 50
@bg park
散歩した。
@end

@exclusive e_c e_a

@ending aoi 葵ED priority 10 when aoi >= 30 and met
葵と夏を過ごした。
@end

@ending alone ひとりの夏 default
ひとりの夏だった。
@end
";

        private static SummerBook? _small;
        private static SummerBook? _real;

        /// <summary>本番の台本（Assets/Game/Data/Summer.txt）。</summary>
        public static SummerBook RealBook => _real ??= Compile(DataText("Summer.txt"));

        public static string DataText(string name, [CallerFilePath] string source = "")
        {
            var dir = Path.GetDirectoryName(source) ?? ".";
            return File.ReadAllText(Path.GetFullPath(Path.Combine(dir, "..", "..", "Game", "Data", name)));
        }

        public static SummerBook SmallBook => _small ??= Compile(Small);

        public static SummerBook Compile(string source)
        {
            var r = SummerCompiler.Compile(source);
            Assert.That(r.Errors, Is.Empty, string.Join("\n", r.Errors));
            return r.Book!;
        }

        /// <summary>コンパイルが失敗し、誤りの文に text を含むものがあることを確かめる。</summary>
        public static void AssertError(string source, string text)
        {
            var r = SummerCompiler.Compile(source);
            Assert.That(r.Ok, Is.False, "誤りがあるはず");
            Assert.That(r.Errors.Any(e => e.ToString().Contains(text, StringComparison.Ordinal)), Is.True, $"「{text}」を含む誤りがない：\n{string.Join("\n", r.Errors)}");
        }

        /// <summary>小さなデータを、1 行だけ置き換えて作る。</summary>
        public static string Replace(string from, string to)
        {
            Assert.That(Small.Contains(from, StringComparison.Ordinal), Is.True, from);
            return Small.Replace(from, to, StringComparison.Ordinal);
        }

        public static void Do(Summer s, Outcome o)
        {
            Assert.That(o.Accepted, Is.True, o.ToString());
            _ = s;
        }

        /// <summary>今の段に合う命令を 1 つ進める（行動は pick、選択肢は 0 番）。</summary>
        public static void Step(Summer s, Func<Summer, int> pick)
        {
            switch (s.Phase)
            {
                case Phase.Action:
                    Do(s, s.ChooseAction(pick(s)));
                    break;
                case Phase.Scene:
                    Do(s, s.Book.Events[s.Scene].Choices.Count > 0 ? s.Choose(0) : s.Continue());
                    break;
                default:
                    throw new InvalidOperationException("終わっています");
            }
        }

        /// <summary>夏休みを最後まで進める。</summary>
        public static void Play(Summer s, Func<Summer, int> pick)
        {
            for (int i = 0; i < 1000 && s.Phase != Phase.Ended; i++) { Step(s, pick); }
            Assert.That(s.Phase, Is.EqualTo(Phase.Ended));
        }

        /// <summary>選べる行動の中から、今日の候補の番号で選ぶ（選べなければ選べる最初の行動）。</summary>
        public static Func<Summer, int> Prefer(params string[] ids) => s =>
        {
            foreach (var id in ids)
            {
                var a = s.Book.ActionIndex(id);
                if (s.CanChoose(a)) { return a; }
            }

            for (int a = 0; a < s.Book.Actions.Count; a++)
            {
                if (s.CanChoose(a)) { return a; }
            }

            throw new InvalidOperationException("選べる行動がありません");
        };
    }
}
