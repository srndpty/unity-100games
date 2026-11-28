using System.Collections.Generic;

namespace Game.App
{
    /// <summary>画面の流れの調整値（遊んで決める値。Unity 側は設定アセットから渡す）。</summary>
    public sealed class FlowSettings
    {
        /// <summary>固定 Tick の数（1 秒あたり）。</summary>
        public const int TickRate = 60;

        /// <summary>処理落ちしたフレームで進める Tick の上限。</summary>
        public int MaxTicksPerFrame { get; set; } = 8;

        /// <summary>石を置いた・取った演出の Tick。この間は着手の入力を捨てる。</summary>
        public int PlaceAnimTicks { get; set; } = 12;

        /// <summary>パス・拒否の通知を出しておく Tick。</summary>
        public int NoticeTicks { get; set; } = 90;

        /// <summary>CPU が手番を受けてから打つまでの最小の Tick（探索が早く終わっても待つ）。</summary>
        public int CpuThinkTicks { get; set; } = 30;

        /// <summary>
        /// CPU が 1 Tick に必ず回すプレイアウトの数。Unity はこれに加えてフレームの残り時間で回す（<see cref="GoFlow.Think"/>）。
        /// 撮影とテストはこの数だけで進むので、同じ Tick に同じ表示になる。
        /// </summary>
        public int PlayoutsPerTick { get; set; } = 20;

        public FlowSettings Copy() => (FlowSettings)MemberwiseClone();

        public IReadOnlyList<string> Validate()
        {
            var e = new List<string>();
            void Range(string name, int value, int min, int max)
            {
                if (value < min || value > max) { e.Add($"{name} は {min}〜{max}（{value}）"); }
            }

            Range(nameof(MaxTicksPerFrame), MaxTicksPerFrame, 1, 60);
            Range(nameof(PlaceAnimTicks), PlaceAnimTicks, 0, 120);
            Range(nameof(NoticeTicks), NoticeTicks, 1, 600);
            Range(nameof(CpuThinkTicks), CpuThinkTicks, 1, 600);
            Range(nameof(PlayoutsPerTick), PlayoutsPerTick, 1, 100_000);
            return e;
        }
    }
}
