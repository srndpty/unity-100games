using System;
using System.Collections.Generic;

namespace Game.Core
{
    public sealed class Evidence
    {
        public Evidence(string name, string detail) => (Name, Detail) = (name, detail);

        public string Name { get; }
        public string Detail { get; }
    }

    public sealed class Testimony
    {
        public Testimony(string speaker, string text, IEnumerable<int> rewards)
        {
            (Speaker, Text) = (speaker, text);
            Rewards = new List<int>(rewards).AsReadOnly();
        }

        public string Speaker { get; }
        public string Text { get; }
        public IReadOnlyList<int> Rewards { get; }
    }

    public sealed class Match
    {
        public Match(int testimonyId, int evidenceId, int next, IEnumerable<int> unlock, string finding)
        {
            (TestimonyId, EvidenceId, Next, Finding) = (testimonyId, evidenceId, next, finding);
            Unlock = new List<int>(unlock).AsReadOnly();
        }

        public int TestimonyId { get; }
        public int EvidenceId { get; }
        public int Next { get; }
        public IReadOnlyList<int> Unlock { get; }
        public string Finding { get; }
    }

    public sealed class CaseFile
    {
        public CaseFile(IEnumerable<Testimony> testimony, IEnumerable<Evidence> evidence, IEnumerable<Match> matches)
        {
            Testimony = new List<Testimony>(testimony).AsReadOnly();
            Evidence = new List<Evidence>(evidence).AsReadOnly();
            Matches = new List<Match>(matches).AsReadOnly();
            Validate();
        }

        // 位置を ID として固定する。初期証言と初期証拠はそれぞれ ID 0・1。
        public IReadOnlyList<Testimony> Testimony { get; }
        public IReadOnlyList<Evidence> Evidence { get; }
        public IReadOnlyList<Match> Matches { get; }

        private void Validate()
        {
            if (Testimony.Count != 5 || Evidence.Count != 8 || Matches.Count != 3)
            {
                throw new ArgumentException("証言5件・証拠8件・照合3件が必要です。");
            }

            var available = new HashSet<int> { 0, 1 };
            foreach (var statement in Testimony)
            {
                if (statement.Rewards.Count == 0)
                {
                    throw new ArgumentException("尋問報酬がない証言があります。");
                }

                foreach (var reward in statement.Rewards)
                {
                    if (reward < 0 || reward >= Evidence.Count || !available.Add(reward))
                    {
                        throw new ArgumentException("尋問報酬の参照または重複が不正です。");
                    }
                }
            }

            if (available.Count != Evidence.Count)
            {
                throw new ArgumentException("取得できない証拠があります。");
            }

            var unlocked = new HashSet<int> { 0, 1 };
            var visited = new HashSet<int>();
            var solved = new HashSet<int>();
            for (var index = 0; index != -1; index = Matches[index].Next)
            {
                if (index < 0 || index >= Matches.Count || !visited.Add(index))
                {
                    throw new ArgumentException("照合の遷移先が不正か、循環しています。");
                }

                var match = Matches[index];
                available = new HashSet<int> { 0, 1 };
                foreach (var id in unlocked)
                {
                    available.UnionWith(Testimony[id].Rewards);
                }

                if (!unlocked.Contains(match.TestimonyId) || !solved.Add(match.TestimonyId) ||
                    !available.Contains(match.EvidenceId))
                {
                    throw new ArgumentException("照合に必要な証言または証拠を取得できません。");
                }

                foreach (var id in match.Unlock)
                {
                    if (id < 0 || id >= Testimony.Count || !unlocked.Add(id))
                    {
                        throw new ArgumentException("解放先の参照または重複が不正です。");
                    }
                }
            }

            if (visited.Count != Matches.Count || unlocked.Count != Testimony.Count)
            {
                throw new ArgumentException("到達できない照合または証言があります。");
            }
        }

        public static CaseFile Museum { get; } = new(
            new Testimony[]
            {
                new("警備員", "閉館後、展示室に入った人はいません。", new[] { 2, 3 }),
                new("保存担当", "18時の閉館時点では本物でした。縁に収蔵番号の刻印を確認しました。", new[] { 4 }),
                new("展示担当・三上", "展示室には忘れ物を取りに入っただけです。展示ケースの封印は切れておらず、メダルは本物のままでした。", new[] { 5 }),
                new("保守担当", "閉館時、ケースと台座をまたぐ封印に検印を押しました。", new[] { 6 }),
                new("展示担当・三上", "搬出したのは予備の空の台座だけです。", new[] { 7 }),
            },
            new Evidence[]
            {
                new("展示目録", "青銅製の記念メダル。本物の縁には収蔵番号 M-17、複製品の縁には「複」の刻印がある。"),
                new("会場図", "展示室へは職員通路から入れる。搬出口は別棟にある。"),
                new("入退室ログ", "18:12　三上の職員証と本人照合で展示室へ入室。\n18:19　同じ認証で退室。"),
                new("巡回表", "18:05　来館者の退出を確認。\n18:20　廊下に異常なし。"),
                new("閉館時の撮影記録", "18:00　展示ケース内のメダルの縁に M-17 を確認。"),
                new("台座の開閉記録", "18:15　展示台座が開き、約1分後に閉じた。"),
                new("封印の拡大写真", "18:24　封印の検印は中央で切断。ケース内のメダルの縁に「複」の刻印が写っている。"),
                new("搬出時の検査記録", "18:22　三上の搬出箱を検査。予備台座の底に M-17 の刻印が入ったメダルを確認。"),
            },
            new Match[]
            {
                new(0, 2, 1, new[] { 2, 3 }, "閉館後、三上が展示室に入っていた。"),
                new(2, 6, 2, new[] { 4 }, "封印は切断され、展示メダルは複製品に交換された。"),
                new(4, 7, -1, Array.Empty<int>(), "展示室には複製品が残り、本物は三上が搬出した予備台座の底から見つかった。"),
            });
    }
}
