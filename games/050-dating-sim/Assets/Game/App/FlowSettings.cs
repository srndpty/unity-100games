using System.Collections.Generic;

namespace Game.App
{
    /// <summary>画面の流れの調整値（遊んで決める値。Unity 側は設定アセットから渡す）。</summary>
    public sealed class FlowSettings
    {
        public int TickRate { get; set; } = 60;

        /// <summary>処理落ちしたフレームで進める Tick の上限。</summary>
        public int MaxTicksPerFrame { get; set; } = 8;

        /// <summary>文字送りの 1 文字あたりの Tick（0 なら一度に全文）。</summary>
        public int TicksPerChar { get; set; } = 2;

        /// <summary>案内（セーブしました など）を出しておく Tick。</summary>
        public int NoticeTicks { get; set; } = 150;

        public FlowSettings Copy() => (FlowSettings)MemberwiseClone();

        public IReadOnlyList<string> Validate()
        {
            var e = new List<string>();
            if (TickRate < 10 || TickRate > 240) { e.Add($"TickRate は 10〜240（{TickRate}）"); }
            if (MaxTicksPerFrame < 1 || MaxTicksPerFrame > 60) { e.Add($"MaxTicksPerFrame は 1〜60（{MaxTicksPerFrame}）"); }
            if (TicksPerChar < 0 || TicksPerChar > 30) { e.Add($"TicksPerChar は 0〜30（{TicksPerChar}）"); }
            if (NoticeTicks < 1 || NoticeTicks > 1200) { e.Add($"NoticeTicks は 1〜1200（{NoticeTicks}）"); }
            return e;
        }
    }
}
