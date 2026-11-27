#nullable enable
using System;
using Game.Core.Analysis;
using Game.Core.Play;
using Game.Core.Rooms;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Game.Unity.Editor
{
    /// <summary>
    /// ビルドの直前に、本番の部屋の定義を作り（作るときの検証）、状態空間を探索して迂回・詰み・持ち物の違反がないかと、
    /// Stage Prefab が定義と一致するかを確かめる。誤りがあれば理由を付けてビルドを止める（046）。
    /// </summary>
    public sealed class RoomBuildCheck : IPreprocessBuildWithReport
    {
        /// <summary>探索する状態の数の上限（書斎は 15）。</summary>
        public const int MaxStates = 10000;

        /// <summary>テスト用：部屋の定義を差し替える（null なら本番の書斎）。</summary>
        public static Func<RoomDef>? RoomOverride { get; set; }

        public int callbackOrder => 0;

        public void OnPreprocessBuild(BuildReport report)
        {
            var error = Check();
            if (error != null) { throw new BuildFailedException(error); }
            Debug.Log("[RoomBuildCheck] 部屋の定義の検証を通りました。");
        }

        /// <summary>部屋の定義と Stage Prefab を検証し、誤りがあれば報告の文を返す（なければ null）。</summary>
        public static string? Check()
        {
            RoomDef def;
            try
            {
                def = RoomOverride != null ? RoomOverride() : StudyRoom.Create();
            }
            catch (RoomDefinitionException e)
            {
                return e.Message;
            }

            var error = CheckRoom(def);
            if (error != null) { return error; }
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(EscapeSceneBuilder.PrefabPath);
            if (prefab == null) { return $"Stage Prefab がありません: {EscapeSceneBuilder.PrefabPath}"; }
            var stage = prefab.GetComponent<StudyStage>();
            if (stage == null) { return $"Stage Prefab に StudyStage がありません: {EscapeSceneBuilder.PrefabPath}"; }
            var mismatch = stage.Mismatch(def);
            return mismatch == null ? null : $"Stage Prefab が部屋の定義と一致しません：{mismatch}";
        }

        /// <summary>状態空間の探索で迂回・詰み・持ち物の違反を調べる（誤りがなければ null）。</summary>
        public static string? CheckRoom(RoomDef def)
        {
            if (def == null) { throw new ArgumentNullException(nameof(def)); }
            var report = StateSpace.Explore(new Room(def, 1), MaxStates);
            return report.Errors.Count == 0 ? null : $"部屋 {def.Name} の探索で誤りが見つかりました\n{string.Join("\n", report.Errors)}";
        }
    }
}
