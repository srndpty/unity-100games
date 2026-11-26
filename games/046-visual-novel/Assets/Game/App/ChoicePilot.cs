using System;
using System.Collections.Generic;

namespace Game.App
{
    /// <summary>
    /// 選択の列を与えて最後まで読む操縦役（撮影・テスト用）。1 回の <see cref="Act"/> で入力を 1 つだけ送る。
    /// 実際の操作と同じ入力（決定・クリック）を通す。
    /// </summary>
    public sealed class ChoicePilot
    {
        private readonly IReadOnlyList<int> _choices;
        private int _next;

        public ChoicePilot(IReadOnlyList<int> choices)
        {
            _choices = choices ?? throw new ArgumentNullException(nameof(choices));
        }

        /// <summary>結果画面に着いたら true。</summary>
        public bool Done { get; private set; }

        public void Act(NovelFlow flow)
        {
            if (flow == null) { throw new ArgumentNullException(nameof(flow)); }
            switch (flow.Screen)
            {
                case FlowScreen.Title:
                    _next = 0;
                    flow.Click(NovelFlow.TitleNew);
                    break;
                case FlowScreen.Paused:
                    flow.Click(NovelFlow.PauseResume);
                    break;
                case FlowScreen.Reading:
                    if (flow.IsChoice)
                    {
                        if (_next >= _choices.Count) { throw new InvalidOperationException("選択の列が足りません"); }
                        flow.Click(_choices[_next++]);
                    }
                    else
                    {
                        flow.Tap(InputButton.Confirm);
                    }

                    break;
                case FlowScreen.Result:
                    Done = true;
                    break;
                default:
                    flow.Tap(InputButton.Confirm);
                    break;
            }
        }

        /// <summary>結果画面まで進める（テスト用）。Tick は 1 回の操作ごとに <paramref name="ticksPerAct"/> 進める。</summary>
        public void RunToEnd(NovelFlow flow, int ticksPerAct = 1, int maxActs = 10000)
        {
            for (int i = 0; i < maxActs && !Done; i++)
            {
                Act(flow);
                flow.StepTicks(ticksPerAct);
            }

            if (!Done) { throw new InvalidOperationException("結果画面に着きません"); }
        }
    }
}
