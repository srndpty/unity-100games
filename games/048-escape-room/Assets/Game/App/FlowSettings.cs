using System.Collections.Generic;

namespace Game.App
{
    /// <summary>画面の流れの調整値（遊んで決める値。Unity 側は設定アセットから渡す）。</summary>
    public sealed class FlowSettings
    {
        public int TickRate { get; set; } = 60;

        /// <summary>処理落ちしたフレームで進める Tick の上限。</summary>
        public int MaxTicksPerFrame { get; set; } = 8;

        /// <summary>視点を移すカメラの演出の Tick（この間のクリック・向き直り・戻るは捨てる）。</summary>
        public int TransitionTicks { get; set; } = 18;

        /// <summary>短い知らせ（拾った・合わない）を出しておく Tick。</summary>
        public int MessageTicks { get; set; } = 150;

        /// <summary>ヒントの文を出しておく Tick。</summary>
        public int HintTicks { get; set; } = 480;

        /// <summary>脱出してから結果画面までの Tick（扉が開く演出）。</summary>
        public int ResultDelayTicks { get; set; } = 90;

        /// <summary>手に取った物を 1 段回すドラッグの長さ（画面の整数座標、縦の半分 = 1000）。</summary>
        public int DragStepUnits { get; set; } = 180;

        public FlowSettings Copy() => (FlowSettings)MemberwiseClone();

        public IReadOnlyList<string> Validate()
        {
            var e = new List<string>();
            if (TickRate < 10 || TickRate > 240) { e.Add($"TickRate は 10〜240（{TickRate}）"); }
            if (MaxTicksPerFrame < 1 || MaxTicksPerFrame > 60) { e.Add($"MaxTicksPerFrame は 1〜60（{MaxTicksPerFrame}）"); }
            if (TransitionTicks < 0 || TransitionTicks > 240) { e.Add($"TransitionTicks は 0〜240（{TransitionTicks}）"); }
            if (MessageTicks < 1 || MessageTicks > 1200) { e.Add($"MessageTicks は 1〜1200（{MessageTicks}）"); }
            if (HintTicks < 1 || HintTicks > 3600) { e.Add($"HintTicks は 1〜3600（{HintTicks}）"); }
            if (ResultDelayTicks < 0 || ResultDelayTicks > 600) { e.Add($"ResultDelayTicks は 0〜600（{ResultDelayTicks}）"); }
            if (DragStepUnits < 20 || DragStepUnits > 2000) { e.Add($"DragStepUnits は 20〜2000（{DragStepUnits}）"); }
            return e;
        }
    }
}
