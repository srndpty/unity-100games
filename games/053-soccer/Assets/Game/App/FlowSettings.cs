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
            c.Cpu = Cpu?.Copy()!;
            return c;
        }

        /// <summary>値の検証。<paramref name="config"/> を渡すと、試合の規則との組み合わせ（タックルの慎重さが届く距離より小さいか）も見る。</summary>
        public IReadOnlyList<string> Validate(SoccerConfig? config = null)
        {
            var e = new List<string>();
            if (MaxTicksPerFrame < 1 || MaxTicksPerFrame > 60) { e.Add($"MaxTicksPerFrame は 1〜60（{MaxTicksPerFrame}）"); }
            if (EndBannerTicks < 1 || EndBannerTicks > 600) { e.Add($"EndBannerTicks は 1〜600（{EndBannerTicks}）"); }
            if (Cpu == null) { e.Add("Cpu がない"); }
            else
            {
                e.AddRange(Cpu.Validate());
                if (config != null && Cpu.TackleMargin >= config.TackleRadius)
                {
                    e.Add($"Cpu.TackleMargin（{Cpu.TackleMargin}）は TackleRadius（{config.TackleRadius}）より小さくします（CPU がタックルしなくなる）");
                }
            }
            return e;
        }
    }
}
