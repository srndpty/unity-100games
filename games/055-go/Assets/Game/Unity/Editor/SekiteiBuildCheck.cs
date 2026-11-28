#nullable enable
using System;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Game.Unity.Editor
{
    /// <summary>ビルドの直前に設定アセットを対局と同じ規則（Core・App の Validate）で検証し、範囲の外ならビルドを止める。</summary>
    public sealed class SekiteiBuildCheck : IPreprocessBuildWithReport
    {
        public const string SettingsPath = "Assets/Game/Unity/SekiteiSettings.asset";

        public int callbackOrder => 0;

        public void OnPreprocessBuild(BuildReport report)
        {
            var error = Check();
            if (error != null) { throw new BuildFailedException(error); }
            Debug.Log("[SekiteiBuildCheck] 設定の検証を通りました。");
        }

        /// <summary>設定を検証し、誤りがあれば報告の文を返す（なければ null）。</summary>
        public static string? Check(SekiteiSettings? settings = null)
        {
            settings ??= AssetDatabase.LoadAssetAtPath<SekiteiSettings>(SettingsPath);
            if (settings == null) { return $"設定がありません: {SettingsPath}"; }
            try
            {
                settings.Validate();
                return null;
            }
            catch (InvalidOperationException e)
            {
                return $"設定の検証に失敗しました（{SettingsPath}）\n{e.Message}";
            }
        }
    }
}
