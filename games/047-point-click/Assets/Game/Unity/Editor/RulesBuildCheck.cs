#nullable enable
using System.Linq;
using Game.Core;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Game.Unity.Editor
{
    /// <summary>
    /// ビルドの直前に本番のルール表を遊ぶときと同じ規則でコンパイルし、誤り（存在しない名前・詰み・使われないルールなど）が
    /// あれば行番号と理由を付けてビルドを止める（046）。
    /// </summary>
    public sealed class RulesBuildCheck : IPreprocessBuildWithReport
    {
        public const string RulesPath = "Assets/Game/Data/Rules.txt";

        /// <summary>テスト用：ルール表の本文を差し替える（null なら Assets から読む）。</summary>
        public static string? SourceOverride { get; set; }

        public int callbackOrder => 0;

        public void OnPreprocessBuild(BuildReport report)
        {
            var error = Check();
            if (error != null) { throw new BuildFailedException(error); }
            Debug.Log("[RulesBuildCheck] ルール表の検証を通りました。");
        }

        /// <summary>ルール表を検証し、誤りがあれば報告の文を返す（なければ null）。</summary>
        public static string? Check()
        {
            string source;
            if (SourceOverride != null)
            {
                source = SourceOverride;
            }
            else
            {
                var asset = AssetDatabase.LoadAssetAtPath<TextAsset>(RulesPath);
                if (asset == null) { return $"ルール表がありません: {RulesPath}"; }
                source = asset.text;
            }

            var result = Adventure.Compile(source);
            return result.Ok ? null : $"ルール表の検証に失敗しました（{RulesPath}）\n{string.Join("\n", result.Errors.Select(e => e.ToString()))}";
        }
    }
}
