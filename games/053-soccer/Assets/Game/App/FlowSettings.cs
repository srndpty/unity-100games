using System.Collections.Generic;
using Game.Core;

namespace Game.App
{
    /// <summary>画面の流れの調整値（遊んで決める値。Unity 側は設定アセットから渡す）。</summary>
    public sealed class FlowSettings
    {
        /// <summary>処理落ちしたフレームで進める Tick の上限。</summary>
        public int MaxTicksPerFrame { get; set; } = 8;

        /// <summary>守備で相手が球を取ったら、球に近い方へ自動で切り替える。</summary>
        public bool AutoSwitchOnDefense { get; set; } = true;

        /// <summary>試合終了の笛から結果画面までの Tick。</summary>
        public int EndBannerTicks { get; set; } = 120;

        /// <summary>相手（赤）の CPU の強さ。既定は弱め（<see cref="AiSkill.Weak"/>）。</summary>
        public AiSkill Cpu { get; set; } = AiSkill.Weak;

        public FlowSettings Copy()
        {
            var c = (FlowSettings)MemberwiseClone();
            c.Cpu = Cpu.Copy();
            return c;
        }

        public IReadOnlyList<string> Validate()
        {
            var e = new List<string>();
            if (MaxTicksPerFrame < 1 || MaxTicksPerFrame > 60) { e.Add($"MaxTicksPerFrame は 1〜60（{MaxTicksPerFrame}）"); }
            if (EndBannerTicks < 1 || EndBannerTicks > 600) { e.Add($"EndBannerTicks は 1〜600（{EndBannerTicks}）"); }
            if (Cpu == null) { e.Add("Cpu がない"); }
            else { e.AddRange(Cpu.Validate()); }
            return e;
        }
    }
}
