using System;
using System.IO;
using Game.Core;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UIElements;

namespace Game.Unity.Editor
{
    public static class RhythmSceneBuilder
    {
        private const string AssetRoot = "Assets/Game/Unity/";
        private const string ScenePath = "Assets/Scenes/Rhythm.unity";

        [MenuItem("Game/Rebuild Rhythm Scene")]
        public static void Build()
        {
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) == null)
            {
                var error = AssetDatabase.MoveAsset("Assets/Scenes/SampleScene.unity", ScenePath);
                if (error.Length != 0) { throw new InvalidOperationException(error); }
            }

            var scene = EditorSceneManager.OpenScene(ScenePath);
            foreach (var item in scene.GetRootGameObjects())
            {
                if (item.name == "Rhythm") { UnityEngine.Object.DestroyImmediate(item); }
            }

            var settings = LoadOrCreate<RhythmSettings>(AssetRoot + "RhythmSettings.asset");
            settings.Validate();
            var tracks = Arrangement.All;
            settings.Clips = new AudioClip[tracks.Count];
            for (var i = 0; i < tracks.Count; i++)
            {
                _ = new Scorecard(tracks[i].Song, settings.Rules);
                var file = AssetRoot + "Music/Track" + (i + 1) + ".wav";
                RhythmMusic.Write(file, tracks[i]);
                AssetDatabase.ImportAsset(file, ImportAssetOptions.ForceSynchronousImport);
                var importer = (AudioImporter)AssetImporter.GetAtPath(file);
                var sampleSettings = importer.defaultSampleSettings;
                sampleSettings.compressionFormat = AudioCompressionFormat.PCM;
                sampleSettings.loadType = AudioClipLoadType.DecompressOnLoad;
                sampleSettings.sampleRateSetting = AudioSampleRateSetting.PreserveSampleRate;
                importer.defaultSampleSettings = sampleSettings;
                importer.loadInBackground = false;
                importer.SaveAndReimport();
                settings.Clips[i] = AssetDatabase.LoadAssetAtPath<AudioClip>(file);
            }

            EditorUtility.SetDirty(settings);

            var panel = LoadOrCreate<PanelSettings>(AssetRoot + "UI/RhythmPanel.asset");
            panel.themeStyleSheet = AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>(AssetRoot + "UI/RhythmTheme.tss");
            panel.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            panel.referenceResolution = new Vector2Int(1600, 900);
            panel.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
            panel.match = .5f;
            EditorUtility.SetDirty(panel);

            var stage = new GameObject("Rhythm");
            var renderer = stage.AddComponent<PanelRenderer>();
            renderer.panelSettings = panel;
            renderer.visualTreeAsset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(AssetRoot + "UI/Rhythm.uxml");
            var component = new SerializedObject(stage.AddComponent<RhythmGame>());
            component.FindProperty("_settings").objectReferenceValue = settings;
            component.ApplyModifiedPropertiesWithoutUndo();
            stage.AddComponent<RhythmCapture>();

            EditorSceneManager.SaveScene(scene);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            PlayerSettings.companyName = "100games";
            PlayerSettings.productName = "リズムゲーム";
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.defaultScreenWidth = 1280;
            PlayerSettings.defaultScreenHeight = 720;
            PlayerSettings.resizableWindow = false;
            AssetDatabase.SaveAssets();
        }

        private static T LoadOrCreate<T>(string path) where T : ScriptableObject
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset != null) { return asset; }

            asset = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(asset, path);
            return asset;
        }
    }

    internal static class RhythmMusic
    {
        public static void Write(string path, Arrangement track)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var samples = new double[track.Song.Length];
            for (var step = 0; step < 96; step++)
            {
                var start = track.SampleAt(step);
                var span = track.SampleAt(step + 1) - start;
                if (step % 2 == 0)
                {
                    var pitch = track.Melody[(step / 2 + step / 32 * 2) % track.Melody.Count];
                    Tone(samples, start, span, pitch, .19);
                }

                if (step % 8 == 0)
                {
                    Tone(samples, start, span * 3, track.Melody[step / 8 % 4] - 24, .16);
                }

                if (step % 4 == 0) { Percussion(samples, start, false, .13); }
            }

            foreach (var note in track.Song.Notes)
            {
                Percussion(samples, note.Sample, note.Strike == Strike.Spark, .25);
            }

            using var writer = new BinaryWriter(File.Create(path));
            var bytes = samples.Length * sizeof(short);
            FourCc(writer, "RIFF");
            writer.Write(36 + bytes);
            FourCc(writer, "WAVE");
            FourCc(writer, "fmt ");
            writer.Write(16);
            writer.Write((short)1);
            writer.Write((short)1);
            writer.Write(Song.SampleRate);
            writer.Write(Song.SampleRate * sizeof(short));
            writer.Write((short)sizeof(short));
            writer.Write((short)16);
            FourCc(writer, "data");
            writer.Write(bytes);
            foreach (var sample in samples)
            {
                writer.Write((short)(Math.Clamp(sample, -1, 1) * short.MaxValue));
            }
        }

        private static void Tone(double[] samples, int start, int span, int pitch, double gain)
        {
            var frequency = 440 * Math.Pow(2, (pitch - 69) / 12.0);
            var count = Math.Min(span, samples.Length - start);
            for (var i = 0; i < count; i++)
            {
                var phase = 2 * Math.PI * frequency * i / Song.SampleRate;
                var envelope = Math.Min(1, i / (5.0 * Song.SamplesPerMillisecond)) * (1 - (double)i / count);
                samples[start + i] += gain * envelope * (Math.Sin(phase) + .19 * Math.Sin(phase * 3));
            }
        }

        private static void Percussion(double[] samples, int start, bool bright, double gain)
        {
            var span = Math.Min(100 * Song.SamplesPerMillisecond, samples.Length - start);
            for (var i = 0; i < span; i++)
            {
                var t = (double)i / Song.SampleRate;
                var phase = 2 * Math.PI * t * (bright ? 740 : 118) / (1 + 8 * t);
                var envelope = Math.Min(1, (double)i / Song.SamplesPerMillisecond) * Math.Exp(-55 * t) * (1 - (double)i / span);
                var wave = bright ? Math.Sin(2.27 * phase) * Math.Sin(phase) : Math.Sin(phase);
                samples[start + i] += gain * envelope * wave;
            }
        }

        private static void FourCc(BinaryWriter writer, string value)
        {
            foreach (var symbol in value) { writer.Write((byte)symbol); }
        }
    }
}
