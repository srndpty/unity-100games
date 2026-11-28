using System.Collections.Generic;

namespace Game.Core
{
    /// <summary>
    /// 対局の規則の調整値（全て整数）。Unity の設定アセットから写す。
    /// 範囲は <see cref="Validate"/> で検証し、対局は検証を通った複製だけを使う。
    /// </summary>
    public sealed class GoConfig
    {
        /// <summary>白に足すコミ（目）。整数なので引き分けがある。</summary>
        public int Komi = 7;

        /// <summary>この手数（パスを含む）に達したら終局する（循環しない対局を必ず終わらせる）。</summary>
        public int MoveLimit = 300;

        public GoConfig Copy() => (GoConfig)MemberwiseClone();

        /// <summary>範囲の外の値を全て挙げる（空なら有効）。</summary>
        public IReadOnlyList<string> Validate()
        {
            var e = new List<string>();
            void Range(string name, int value, int min, int max)
            {
                if (value < min || value > max) { e.Add($"{name} は {min}〜{max}（{value}）"); }
            }

            Range(nameof(Komi), Komi, -Points.Count, Points.Count);
            Range(nameof(MoveLimit), MoveLimit, 2, 1000);
            return e;
        }
    }
}
