using System;
using Game.Core;
using UnityEngine;

namespace Game.Unity
{
    [CreateAssetMenu(menuName = "Game/Breezeway Settings")]
    public sealed class BreezewaySettings : ScriptableObject
    {
        /// <summary>
        /// 物理とルールの設定（重力・抵抗・風・クラブ・地面・メーター・カップ・打数）。
        /// シーン生成スクリプトはこのアセットを上書きしないので、コードの既定値を変えたらアセットも直す（024 の知見）。
        /// </summary>
        public GolfConfig Rules = new();

        /// <summary>コース（<see cref="Course"/> の書式）。</summary>
        public TextAsset Course;

        [Range(0, 1)] public float Volume = .12f;

        /// <summary>球の高さ 1 m を画面の上へ何 m ずらして描くか（影との離れで高さを表す）。</summary>
        [Range(0, 2)] public float HeightScale = .5f;

        /// <summary>知らせ（ナイスイン・OB など）を出しておく秒数。</summary>
        [Range(.2f, 10)] public float BannerSeconds = 2f;

        /// <summary>撮影で操縦役に渡すシード。</summary>
        public int CapturePilotSeed = 3;

        /// <summary>設定とコースを検証し、コースを返す。</summary>
        public Course Validate()
        {
            if (Rules == null) { throw new ArgumentNullException(nameof(Rules)); }
            if (Course == null) { throw new ArgumentNullException(nameof(Course)); }
            var course = Game.Core.Course.Parse(Course.text, Rules);
            Require(Volume, 0, 1, nameof(Volume));
            Require(HeightScale, 0, 2, nameof(HeightScale));
            Require(BannerSeconds, .2f, 10, nameof(BannerSeconds));
            return course;
        }

        private static void Require(float value, float min, float max, string name)
        {
            if (!float.IsFinite(value) || value < min || value > max)
            {
                throw new ArgumentOutOfRangeException(name, value, $"{min}〜{max} で指定してください。");
            }
        }
    }
}
