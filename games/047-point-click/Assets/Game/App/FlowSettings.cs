using System.Collections.Generic;

namespace Game.App
{
    /// <summary>画面の流れの調整値（遊んで決める値。Unity 側は設定アセットから渡す）。</summary>
    public sealed class FlowSettings
    {
        public int TickRate { get; set; } = 60;

        /// <summary>処理落ちしたフレームで進める Tick の上限。</summary>
        public int MaxTicksPerFrame { get; set; } = 8;

        /// <summary>台詞を出しておく Tick（基本）。</summary>
        public int LineBaseTicks { get; set; } = 90;

        /// <summary>台詞を出しておく Tick（1 文字あたりの追加）。</summary>
        public int LineTicksPerChar { get; set; } = 6;

        /// <summary>行為を命じたら動詞と持ち物の選択を外すか（外さなければ同じ動詞で続けて操作できる）。</summary>
        public bool ResetVerbAfterCommand { get; set; } = true;

        public FlowSettings Copy() => (FlowSettings)MemberwiseClone();

        public IReadOnlyList<string> Validate()
        {
            var e = new List<string>();
            if (TickRate < 10 || TickRate > 240) { e.Add($"TickRate は 10〜240（{TickRate}）"); }
            if (MaxTicksPerFrame < 1 || MaxTicksPerFrame > 60) { e.Add($"MaxTicksPerFrame は 1〜60（{MaxTicksPerFrame}）"); }
            if (LineBaseTicks < 1 || LineBaseTicks > 1200) { e.Add($"LineBaseTicks は 1〜1200（{LineBaseTicks}）"); }
            if (LineTicksPerChar < 0 || LineTicksPerChar > 60) { e.Add($"LineTicksPerChar は 0〜60（{LineTicksPerChar}）"); }
            return e;
        }
    }
}
