using System.IO;
using UnityEditor;
using UnityEngine;

namespace Game.Unity.Editor
{
    public static class ThistleArtGenerator
    {
        public const string ArtRoot = "Assets/Game/Unity/Art/";

        public static void GenerateIfMissing()
        {
            Directory.CreateDirectory(ArtRoot);
            Write("kart-0.png", new Color(0.93f, 0.78f, 0.18f));
            Write("kart-1.png", new Color(0.85f, 0.25f, 0.22f));
            Write("kart-2.png", new Color(0.25f, 0.45f, 0.86f));
            Write("kart-3.png", new Color(0.30f, 0.72f, 0.38f));
            AssetDatabase.Refresh();
            for (var i = 0; i < 4; i++)
            {
                var path = ArtRoot + "kart-" + i + ".png";
                var importer = (TextureImporter)AssetImporter.GetAtPath(path);
                importer.textureType = TextureImporterType.Sprite;
                importer.spritePixelsPerUnit = 16f;
                importer.filterMode = FilterMode.Point;
                importer.SaveAndReimport();
            }
        }

        private static void Write(string file, Color body)
        {
            var path = ArtRoot + file;
            if (File.Exists(path)) { return; }
            var texture = new Texture2D(16, 16, TextureFormat.RGBA32, false);
            var clear = new Color(0f, 0f, 0f, 0f);
            for (var y = 0; y < 16; y++)
            {
                for (var x = 0; x < 16; x++) { texture.SetPixel(x, y, clear); }
            }

            for (var y = 4; y <= 11; y++)
            {
                for (var x = 2; x <= 12; x++) { texture.SetPixel(x, y, body); }
            }

            for (var y = 6; y <= 9; y++)
            {
                texture.SetPixel(13, y, Color.white);
                texture.SetPixel(14, y, Color.white);
            }

            File.WriteAllBytes(path, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);
        }
    }
}
