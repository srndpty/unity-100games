using System.Collections.Generic;

namespace Game.Core.Script
{
    /// <summary>
    /// スクリプトの検証の規則。Core の設定クラスなので公開フィールドで書く（Unity のシリアライズのため）。
    /// 0 の数（ChoiceCount・EndingCount）は「数を問わない」（テスト用の小さなスクリプト向け）。
    /// </summary>
    public sealed class ScriptRules
    {
        /// <summary>1 文の文字数の上限（テキスト枠に収まる数）。</summary>
        public int MaxTextLength = 80;

        /// <summary>選択肢 1 項目の文字数の上限。</summary>
        public int MaxOptionLength = 24;

        /// <summary>話者名・結末名の文字数の上限。</summary>
        public int MaxNameLength = 10;

        public int MinOptions = 2;
        public int MaxOptions = 3;

        /// <summary>選択肢の箇所の数（0 は問わない）。</summary>
        public int ChoiceCount;

        /// <summary>結末の数（0 は問わない）。</summary>
        public int EndingCount;

        /// <summary>スクリプトの行数の上限（読む前に数える）。</summary>
        public int MaxLines = 4000;

        /// <summary>待ちのない命令を 1 回の送りで続けて実行できる上限。</summary>
        public int FlowLimit = 10000;

        /// <summary>int 変数の値の範囲（±）。</summary>
        public int ValueLimit = 9999;

        /// <summary>網羅探索で数える経路の上限。</summary>
        public int MaxPaths = 1024;

        /// <summary>作品の本番シナリオの規則（選択肢 3 箇所・結末 3 つ）。</summary>
        public static ScriptRules Scenario() => new() { ChoiceCount = 3, EndingCount = 3 };

        public IReadOnlyList<string> Validate()
        {
            var e = new List<string>();
            if (MaxTextLength < 1 || MaxTextLength > 200) { e.Add($"MaxTextLength は 1〜200（{MaxTextLength}）"); }
            if (MaxOptionLength < 1 || MaxOptionLength > 60) { e.Add($"MaxOptionLength は 1〜60（{MaxOptionLength}）"); }
            if (MaxNameLength < 1 || MaxNameLength > 20) { e.Add($"MaxNameLength は 1〜20（{MaxNameLength}）"); }
            if (MinOptions < 1 || MaxOptions < MinOptions || MaxOptions > 9) { e.Add($"選択肢の数は 1 ≤ MinOptions ≤ MaxOptions ≤ 9（{MinOptions}〜{MaxOptions}）"); }
            if (ChoiceCount < 0 || EndingCount < 0) { e.Add("ChoiceCount・EndingCount は 0 以上"); }
            if (MaxLines < 1 || MaxLines > 100000) { e.Add($"MaxLines は 1〜100000（{MaxLines}）"); }
            if (FlowLimit < 1 || FlowLimit > 1000000) { e.Add($"FlowLimit は 1〜1000000（{FlowLimit}）"); }
            if (ValueLimit < 1 || ValueLimit > 1000000) { e.Add($"ValueLimit は 1〜1000000（{ValueLimit}）"); }
            if (MaxPaths < 1 || MaxPaths > 100000) { e.Add($"MaxPaths は 1〜100000（{MaxPaths}）"); }
            return e;
        }
    }
}
