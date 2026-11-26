#nullable enable
using System;
using Game.Core;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Game.Unity.Editor
{
    /// <summary>
    /// ビルドの直前に本番シナリオを遊ぶときと同じ規則でコンパイルし、誤り（存在しないラベルなど）があれば
    /// 行番号と理由を付けてビルドを止める。
    /// </summary>
    public sealed class ScenarioBuildCheck : IPreprocessBuildWithReport
    {
        public const string ScenarioPath = "Assets/Game/Data/Scenario.txt";
        public const string SettingsPath = "Assets/Game/Unity/NovelSettings.asset";

        /// <summary>テスト用：シナリオの本文を差し替える（null なら Assets から読む）。</summary>
        public static string? SourceOverride { get; set; }

        public int callbackOrder => 0;

        public void OnPreprocessBuild(BuildReport report)
        {
            var error = Check();
            if (error != null) { throw new BuildFailedException(error); }
            Debug.Log("[ScenarioBuildCheck] シナリオの検証を通りました。");
        }

        /// <summary>シナリオを検証し、誤りがあれば報告の文を返す（なければ null）。</summary>
        public static string? Check()
        {
            var settings = AssetDatabase.LoadAssetAtPath<NovelSettings>(SettingsPath);
            if (settings == null) { return $"設定がありません: {SettingsPath}"; }
            string source;
            if (SourceOverride != null)
            {
                source = SourceOverride;
            }
            else
            {
                var asset = AssetDatabase.LoadAssetAtPath<TextAsset>(ScenarioPath);
                if (asset == null) { return $"シナリオがありません: {ScenarioPath}"; }
                source = asset.text;
            }

            var result = Scenario.Compile(source, settings.MaxTextLength);
            return result.Ok ? null : $"シナリオの検証に失敗しました（{ScenarioPath}）\n{result.Report()}";
        }
    }
}
