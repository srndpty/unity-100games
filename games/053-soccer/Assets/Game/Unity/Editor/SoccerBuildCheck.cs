#nullable enable
using System;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Game.Unity.Editor
{
    /// <summary>ビルドの直前に設定アセットを試合と同じ規則（<see cref="Game.Core.SoccerConfig.Validate"/>）で検証し、範囲の外ならビルドを止める。</summary>
    public sealed class SoccerBuildCheck : IPreprocessBuildWithReport
    {
        public const string SettingsPath = "Assets/Game/Unity/SoccerSettings.asset";

        public int callbackOrder => 0;

        public void OnPreprocessBuild(BuildReport report)
        {
            var error = Check();
            if (error != null) { throw new BuildFailedException(error); }
            Debug.Log("[SoccerBuildCheck] 設定の検証を通りました。");
        }

        /// <summary>設定を検証し、誤りがあれば報告の文を返す（なければ null）。</summary>
        public static string? Check(SoccerSettings? settings = null)
        {
            settings ??= AssetDatabase.LoadAssetAtPath<SoccerSettings>(SettingsPath);
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
