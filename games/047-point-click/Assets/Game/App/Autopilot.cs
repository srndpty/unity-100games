using System;
using System.Collections.Generic;
using Game.Core.Analysis;

namespace Game.App
{
    /// <summary>
    /// 操縦役：手の列（解答手順）を順に命じ、台詞は決定で送る。撮影の台本・App のテスト・PlayMode のテストで共有する（002）。
    /// 歩きは Flow の Tick に任せるので、遊ぶときと同じく立ち位置に着いた Tick に解決する。
    /// </summary>
    public sealed class Autopilot
    {
        private readonly IReadOnlyList<Move> _moves;

        public Autopilot(IReadOnlyList<Move> moves)
        {
            _moves = moves ?? throw new ArgumentNullException(nameof(moves));
        }

        /// <summary>次に命じる手の番号。</summary>
        public int Next { get; private set; }

        public bool Done => Next >= _moves.Count;

        /// <summary>
        /// 1 回分の判断。台詞が出ていれば送り、手を受け付ける状態なら次の手を命じる。
        /// 命じた手を世界が受け付けなかったら、手順が規則と合っていないので例外にする。
        /// </summary>
        public void Drive(AdventureFlow flow)
        {
            if (flow == null) { throw new ArgumentNullException(nameof(flow)); }
            if (flow.Screen != FlowScreen.Playing) { return; }
            if (flow.CurrentLine != null)
            {
                flow.Press(IntentKind.Confirm);
                flow.FlushIntents();
                return;
            }

            if (Done || !flow.IsIdle) { return; }
            if (!flow.TryIssue(_moves[Next]))
            {
                throw new InvalidOperationException($"手 {Next + 1}（{_moves[Next].Describe(flow.Book)}）を受け付けませんでした");
            }

            Next++;
        }

        /// <summary>Tick を 1 つずつ進めながら、脱出して結果画面になるまで動かす。上限を超えたら例外。</summary>
        public void RunToResult(AdventureFlow flow, int maxTicks = 100000)
        {
            if (flow == null) { throw new ArgumentNullException(nameof(flow)); }
            for (int i = 0; i < maxTicks; i++)
            {
                if (flow.Screen == FlowScreen.Result) { return; }
                Drive(flow);
                flow.StepTicks(1);
            }

            throw new InvalidOperationException($"{maxTicks} Tick で結果画面に届きません");
        }
    }
}
