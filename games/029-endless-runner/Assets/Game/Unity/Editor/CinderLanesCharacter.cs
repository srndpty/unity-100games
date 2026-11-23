using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace Game.Unity.Editor
{
    /// <summary>
    /// 自機のモデル（ユニティちゃん 3D モデルデータ v1.4.0、© Unity Technologies Japan/UCL）を Stage Prefab に組み込む。
    /// 元のマテリアルは URP で使えないシェーダー（UTS2）なので、取り込んだテクスチャから URP の Simple Lit（光沢なし）の
    /// マテリアルを作り直す。目の線と頬は半透明のテクスチャなので URP の Unlit（半透明）にする。
    /// Unity Toon Shader（com.unity.toonshader 0.13.4-preview）も試したが、この Unity（6000.6）では輪郭線のパスだけが描かれて真っ黒になったので使わない。
    /// モーションは待機・走り・ジャンプ・スライド・被弾の 5 つを Animator Controller の状態にし、再生位置は Stage が Core の状態から決める。
    /// 既にあるマテリアルとコントローラーは上書きしない。
    /// </summary>
    public static class CinderLanesCharacter
    {
        public const string ModelPath = "Assets/UnityChan/Models/unitychan.fbx";
        private const string TexturePath = "Assets/UnityChan/Models/Texture/";
        private const string AnimationPath = "Assets/UnityChan/Animations/unitychan_";
        private const string Output = CinderLanesArtGenerator.ArtRoot + "UnityChan/";
        private const string ControllerPath = Output + "CinderLanesRunner.controller";

        /// <summary>モデルの元のマテリアル名 → テクスチャと、半透明（Unlit）にするか。</summary>
        private static readonly Dictionary<string, (string Texture, bool Transparent)> _materials = new()
        {
            ["body"] = ("body_01", false),
            ["hair"] = ("hair_01", false),
            ["face"] = ("face_00", false),
            ["skin1"] = ("skin_01", false),
            ["eyebase"] = ("face_00", false),
            ["eye_L1"] = ("eye_iris_L_00", false),
            ["eye_R1"] = ("eye_iris_R_00", false),
            ["eyeline"] = ("eyeline_00", true),
            ["mat_cheek"] = ("cheek_00", true),
        };

        /// <summary>Animator Controller の状態名とクリップ（Stage はこの名前で再生する）。</summary>
        private static readonly (string State, string Clip)[] _states =
        {
            (CinderLanesStage.IdleState, "WAIT00"),
            (CinderLanesStage.RunState, "RUN00_F"),
            (CinderLanesStage.JumpState, "JUMP00"),
            (CinderLanesStage.SlideState, "SLIDE00"),
            (CinderLanesStage.CrashState, "DAMAGED01"),
        };

        /// <summary>モデルを <paramref name="parent"/> の下に置き、マテリアルとコントローラーをつないだ Animator を返す。</summary>
        /// <summary>被弾のクリップの長さ（秒）。</summary>
        public static float CrashLength()
        {
            var clip = _states.First(s => s.State == CinderLanesStage.CrashState).Clip;
            return AssetDatabase.LoadAllAssetsAtPath(AnimationPath + clip + ".fbx").OfType<AnimationClip>().First(c => c.name == clip).length;
        }

        /// <summary>
        /// 被弾（倒れる）のクリップで、寝そべったままの時刻（正規化時間）。頭の高さを 120 等分で調べ、いちばん低い高さ +4 cm 以内に
        /// 続けて収まる区間の中央を返す（その後の起き上がりは見せない）。
        /// </summary>
        public static float LyingTime()
        {
            var clipName = _states.First(s => s.State == CinderLanesStage.CrashState).Clip;
            var clip = AssetDatabase.LoadAllAssetsAtPath(AnimationPath + clipName + ".fbx").OfType<AnimationClip>().First(c => c.name == clipName);
            var model = (GameObject)UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath));
            try
            {
                var head = model.GetComponent<Animator>().GetBoneTransform(HumanBodyBones.Head);
                const int Steps = 120;
                var heights = new float[Steps + 1];
                for (var i = 0; i <= Steps; i++)
                {
                    clip.SampleAnimation(model, clip.length * i / Steps);
                    heights[i] = head.position.y;
                }

                var lowest = Array.IndexOf(heights, heights.Min());
                var first = lowest;
                var last = lowest;
                while (first > 0 && heights[first - 1] <= heights[lowest] + .04f) { first--; }
                while (last < Steps && heights[last + 1] <= heights[lowest] + .04f) { last++; }
                return (first + last) / 2f / Steps;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(model);
            }
        }

        public static Animator Place(Transform parent)
        {
            BakeRootMotion();
            if (!AssetDatabase.IsValidFolder(Output.TrimEnd('/'))) { AssetDatabase.CreateFolder(CinderLanesArtGenerator.ArtRoot.TrimEnd('/'), "UnityChan"); }
            var model = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath));
            PrefabUtility.UnpackPrefabInstance(model, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            model.name = "UnityChan";
            model.transform.SetParent(parent, false);
            foreach (var renderer in model.GetComponentsInChildren<Renderer>(true))
            {
                renderer.sharedMaterials = renderer.sharedMaterials.Select(Replace).ToArray();
            }

            var animator = model.GetComponent<Animator>() ?? throw new InvalidOperationException("モデルに Animator がありません。");
            animator.runtimeAnimatorController = Controller();
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            return animator;
        }

        /// <summary>
        /// クリップの根の動き（前進・高さ・向き）をポーズに焼き込み、足元を基準にする。跳ぶ高さと前進は Core が決めるので、
        /// クリップの前進で体が当たり箱より前へずれたり、クリップの上昇と Core の高さが二重になったりしないようにする。
        /// </summary>
        private static void BakeRootMotion()
        {
            foreach (var (_, clip) in _states)
            {
                var path = AnimationPath + clip + ".fbx";
                var importer = (ModelImporter)AssetImporter.GetAtPath(path);
                var clips = importer.clipAnimations.Length > 0 ? importer.clipAnimations : importer.defaultClipAnimations;
                var changed = false;
                foreach (var item in clips)
                {
                    if (item.lockRootRotation && item.lockRootHeightY && item.lockRootPositionXZ && item.heightFromFeet && !item.keepOriginalPositionY) { continue; }
                    item.lockRootRotation = true;
                    item.keepOriginalOrientation = true;
                    item.lockRootHeightY = true;
                    item.keepOriginalPositionY = false;
                    item.heightFromFeet = true;
                    item.lockRootPositionXZ = true;
                    item.keepOriginalPositionXZ = false;
                    changed = true;
                }

                if (!changed) { continue; }
                importer.clipAnimations = clips;
                importer.SaveAndReimport();
            }
        }

        private static Material Replace(Material original)
        {
            if (original == null || !_materials.TryGetValue(original.name, out var spec)) { throw new InvalidOperationException($"対応のないマテリアルです：{original?.name}"); }
            var path = Output + original.name + ".mat";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null) { return existing; }
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(TexturePath + spec.Texture + ".tga") ?? throw new InvalidOperationException("テクスチャがありません: " + spec.Texture);
            Material material;
            if (spec.Transparent)
            {
                material = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
                material.SetTexture("_BaseMap", texture);
                material.SetFloat("_Surface", 1);
                material.SetFloat("_Blend", 0);
                material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
                material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                material.SetFloat("_ZWrite", 0);
                material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                material.SetOverrideTag("RenderType", "Transparent");
                material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            }
            else
            {
                material = new Material(Shader.Find("Universal Render Pipeline/Simple Lit"));
                material.SetTexture("_BaseMap", texture);
                material.SetColor("_BaseColor", Color.white);
                material.SetColor("_SpecColor", Color.black);
                material.SetFloat("_Smoothness", 0);
                material.DisableKeyword("_SPECULAR_COLOR");
            }

            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        private static AnimatorController Controller()
        {
            var existing = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            if (existing != null) { return existing; }
            var controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
            var machine = controller.layers[0].stateMachine;
            foreach (var (state, clip) in _states)
            {
                var motion = AssetDatabase.LoadAllAssetsAtPath(AnimationPath + clip + ".fbx").OfType<AnimationClip>().FirstOrDefault(c => c.name == clip)
                    ?? throw new InvalidOperationException("クリップがありません: " + clip);
                var added = machine.AddState(state);
                added.motion = motion;
                added.writeDefaultValues = true;
            }

            machine.defaultState = machine.states[0].state;
            AssetDatabase.SaveAssets();
            return controller;
        }
    }
}
