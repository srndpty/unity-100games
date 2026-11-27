#nullable enable
using Game.Core.Data;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Game.Unity.Editor
{
    /// <summary>
    /// ビルドの直前に本番の台本を遊ぶときと同じ規則でコンパイルし、誤り（未宣言の名前・成り立たない条件など）があれば
    /// 行番号と理由を付けてビルドを止める（046）。設定アセットの値も検証する。
    /// </summary>
    public sealed class SummerBuildCheck : IPreprocessBuildWithReport
    {
        public const string BookPath = "Assets/Game/Data/Summer.txt";
        public const string SettingsPath = "Assets/Game/Unity/SummerSettings.asset";

        /// <summary>テスト用：台本の本文を差し替える（null なら Assets から読む）。</summary>
        public static string? SourceOverride { get; set; }

        public int callbackOrder => 0;

        public void OnPreprocessBuild(BuildReport report)
        {
            var error = Check();
            if (error != null) { throw new BuildFailedException(error); }
            Debug.Log("[SummerBuildCheck] 台本の検証を通りました。");
        }

        /// <summary>台本と設定を検証し、誤りがあれば報告の文を返す（なければ null）。</summary>
        public static string? Check()
        {
            var settings = AssetDatabase.LoadAssetAtPath<SummerSettings>(SettingsPath);
            if (settings == null) { return $"設定がありません: {SettingsPath}"; }
            try
            {
                settings.Validate();
            }
            catch (System.InvalidOperationException e)
            {
                return $"設定の検証に失敗しました（{SettingsPath}）\n{e.Message}";
            }

            string source;
            if (SourceOverride != null)
            {
                source = SourceOverride;
            }
            else
            {
                var asset = AssetDatabase.LoadAssetAtPath<TextAsset>(BookPath);
                if (asset == null) { return $"台本がありません: {BookPath}"; }
                source = asset.text;
            }

            var result = SummerCompiler.Compile(source);
            return result.Ok ? null : $"台本の検証に失敗しました（{BookPath}）\n{string.Join("\n", result.Errors)}";
        }

        /// <summary>本番の台本をコンパイルする（シーンの生成・素材の生成が使う）。</summary>
        public static SummerBook LoadBook()
        {
            var asset = AssetDatabase.LoadAssetAtPath<TextAsset>(BookPath);
            if (asset == null) { throw new System.InvalidOperationException($"台本がありません: {BookPath}"); }
            var result = SummerCompiler.Compile(asset.text);
            if (!result.Ok) { throw new System.InvalidOperationException(string.Join("\n", result.Errors)); }
            return result.Book!;
        }
    }
}
