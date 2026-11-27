using System;
using Game.Core.Data;
using Game.Core.Play;
using IntGeometry;

namespace Game.App
{
    /// <summary>操縦役の方針。</summary>
    public enum PilotPolicy
    {
        /// <summary>天文部の先輩（澄）を目指す。</summary>
        Sumi,

        /// <summary>図書室の同級生（栞）を目指す。</summary>
        Shiori,

        /// <summary>成り行き（シード付きの乱択。どちらにも寄らない）。</summary>
        Drift,
    }

    /// <summary>
    /// 操縦役（撮影台本・App テスト・PlayMode テストで共有する、002）。夏休みの状態から行動と選択肢を決める。
    /// 方針ごとの規則：強制の朝はその行動、疲労 70 以上は休む、お祭りの前にお金が足りなければバイト、
    /// 3 日に 1 度は ED の条件の能力値（先輩は感性・同級生は学力）を伸ばし、ほかの日は相手のいる場所へ行く。
    /// 選択肢は効果の点数（目当ての好感度 × 3・フラグを立てる 50・ほかの能力値は増減の向きだけ ±1・ほかの好感度 × −3）が最大のもの。
    /// </summary>
    public sealed class Autopilot
    {
        public const int FatigueLimit = 70;
        public const int FestivalMoney = 300;
        public const int FestivalDay = 20;

        private readonly SummerBook _book;
        private readonly SeededRandom _rng;
        private readonly int _heart;
        private readonly int _other;
        private readonly int _place;
        private readonly int _train;
        private readonly int _trainStat;

        public Autopilot(SummerBook book, PilotPolicy policy, int seed)
        {
            _book = book ?? throw new ArgumentNullException(nameof(book));
            Policy = policy;
            _rng = new SeededRandom(seed);
            if (policy == PilotPolicy.Drift) { return; }
            var target = policy == PilotPolicy.Sumi ? "sumi" : "shiori";
            _heart = book.Characters[book.CharacterIndex(target)].Heart;
            _other = book.Characters[book.CharacterIndex(policy == PilotPolicy.Sumi ? "shiori" : "sumi")].Heart;
            _place = book.ActionIndex(policy == PilotPolicy.Sumi ? "club" : "library");
            _train = book.ActionIndex(policy == PilotPolicy.Sumi ? "create" : "study");
            _trainStat = book.StatIndex(policy == PilotPolicy.Sumi ? "sense" : "study");
        }

        public PilotPolicy Policy { get; }

        /// <summary>今朝の行動。</summary>
        public int ChooseAction(Summer s)
        {
            if (s == null) { throw new ArgumentNullException(nameof(s)); }
            if (s.Forced) { return _book.ForcedAction; }
            var rest = _book.ActionIndex("rest");
            if (s.Stat(_book.StatIndex("fatigue")) >= FatigueLimit) { return rest; }
            if (Policy == PilotPolicy.Drift) { return Pick(s); }

            var work = _book.ActionIndex("work");
            if (s.Day < FestivalDay && s.Stat(_book.StatIndex("money")) < FestivalMoney && s.CanChoose(work)) { return work; }
            if (s.Day % 3 == 0 && s.Stat(_trainStat) < 60) { return _train; }
            return s.CanChoose(_place) ? _place : _train;
        }

        /// <summary>選択肢を選ぶ。</summary>
        public int ChooseOption(Summer s)
        {
            if (s == null) { throw new ArgumentNullException(nameof(s)); }
            var choices = _book.Events[s.Scene].Choices;
            if (Policy == PilotPolicy.Drift) { return _rng.Next(choices.Count); }
            var best = 0;
            var bestScore = int.MinValue;
            for (int i = 0; i < choices.Count; i++)
            {
                var score = 0;
                foreach (var e in choices[i].Effects)
                {
                    score += e.Kind switch
                    {
                        EffectKind.Set => 50,
                        EffectKind.Clear => -50,
                        _ when e.Target == _heart => 3 * e.Min,
                        _ when e.Target == _other => -3 * e.Min,
                        _ => Math.Sign(e.Min),
                    };
                }

                if (score > bestScore)
                {
                    best = i;
                    bestScore = score;
                }
            }

            return best;
        }

        /// <summary>命令の数の上限（暦の上限 60 日 × 1 日の命令 3 つより多い）。</summary>
        public const int MaxCommands = 1000;

        /// <summary>夏休みを最後まで進める（App を通さない）。上限の命令の数で終わらなければ例外。</summary>
        public void Play(Summer s, int maxCommands = MaxCommands)
        {
            if (s == null) { throw new ArgumentNullException(nameof(s)); }
            for (int i = 0; i < maxCommands && s.Phase != Phase.Ended; i++)
            {
                var r = s.Phase switch
                {
                    Phase.Action => s.ChooseAction(ChooseAction(s)),
                    _ => _book.Events[s.Scene].Choices.Count > 0 ? s.Choose(ChooseOption(s)) : s.Continue(),
                };
                if (!r.Accepted) { throw new InvalidOperationException($"操縦役の命令が拒否されました：{r}"); }
            }

            if (s.Phase != Phase.Ended) { throw new InvalidOperationException($"{maxCommands} 命令で ED に着きませんでした（{s.Day} 日目）"); }
        }

        /// <summary>
        /// 画面の流れに次の 1 手の入力を送る（クリックの意図）。夏休みの命令を送ったら、その命令の通し番号を返す（なければ -1）。
        /// 送った後は <see cref="SummerFlow.Commands"/> が増えたことで実行を確かめる（048）。
        /// </summary>
        public int Drive(SummerFlow flow)
        {
            if (flow == null) { throw new ArgumentNullException(nameof(flow)); }
            switch (flow.Screen)
            {
                case FlowScreen.Title:
                    flow.Click(SummerFlow.TitleNew);
                    return -1;
                case FlowScreen.Paused:
                    flow.Click(SummerFlow.PauseResume);
                    return -1;
                case FlowScreen.HowTo:
                case FlowScreen.Result:
                    return -1;
                default:
                    break;
            }

            var s = flow.Summer!;
            switch (flow.View)
            {
                case PlayView.Morning:
                    flow.Click(ChooseAction(s));
                    return flow.Commands + 1;
                case PlayView.Scene when flow.IsChoice:
                    flow.Click(ChooseOption(s));
                    return flow.Commands + 1;
                default:
                    flow.Press(InputButton.Confirm);
                    return -1;
            }
        }

        private int Pick(Summer s)
        {
            var count = 0;
            for (int a = 0; a < _book.Actions.Count; a++)
            {
                if (s.CanChoose(a)) { count++; }
            }

            var k = _rng.Next(count);
            for (int a = 0; a < _book.Actions.Count; a++)
            {
                if (!s.CanChoose(a)) { continue; }
                if (k-- == 0) { return a; }
            }

            throw new InvalidOperationException("選べる行動がありません");
        }
    }
}
