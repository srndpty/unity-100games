using System;
using System.Collections.Generic;

namespace Game.Unity.Evidence
{
    /// <summary>
    /// 証跡の記録（evidence/captures/&lt;世代&gt;/manifest.json）。どのビルド・シード・設定で、どの時点を撮ったかと、各画像の SHA-256 を残す。
    /// JsonUtility で書き出すため公開フィールドにしている。
    /// </summary>
    [Serializable]
    public sealed class CaptureManifest
    {
        public string Product = string.Empty;
        public string Version = string.Empty;
        public string UnityVersion = string.Empty;
        public string BuildGuid = string.Empty;
        public int Seed;
        public string CapturedAtUtc = string.Empty;
        public List<Entry> Info = new();
        public List<Shot> Shots = new();

        [Serializable]
        public sealed class Entry
        {
            public string Key = string.Empty;
            public string Value = string.Empty;
        }

        [Serializable]
        public sealed class Shot
        {
            public string File = string.Empty;
            public string Sha256 = string.Empty;

            /// <summary>撮った時点（Tick 数など）。</summary>
            public string Note = string.Empty;
        }
    }
}
