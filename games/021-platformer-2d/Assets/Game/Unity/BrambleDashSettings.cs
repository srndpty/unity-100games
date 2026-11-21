using System;
using Game.App;
using Game.Core;
using UnityEngine;

namespace Game.Unity
{
    [CreateAssetMenu(menuName = "Game/Bramble Dash Settings")]
    public sealed class BrambleDashSettings : ScriptableObject
    {
        public PlatformerConfig Rules = new();

        /// <summary>面のクリア・ミスの後、次へ進むまでの Tick 数。</summary>
        public int InterludeTicks = 90;

        /// <summary>カメラの遊び（中心から左右にこのタイル数までは追わない）。</summary>
        [Range(0, 6)] public float CameraDeadZoneTiles = 1.5f;

        [Range(0, 1)] public float Volume = .12f;

        public void Validate()
        {
            Rules.Validate();
            if (InterludeTicks < 1 || InterludeTicks > PlatformerFlow.MaxInterludeTicks)
            {
                throw new ArgumentOutOfRangeException(nameof(InterludeTicks), InterludeTicks, $"1〜{PlatformerFlow.MaxInterludeTicks} で指定してください。");
            }

            if (!float.IsFinite(CameraDeadZoneTiles) || CameraDeadZoneTiles < 0 || CameraDeadZoneTiles > 6)
            {
                throw new ArgumentOutOfRangeException(nameof(CameraDeadZoneTiles), CameraDeadZoneTiles, "0〜6 で指定してください。");
            }

            if (!float.IsFinite(Volume) || Volume < 0 || Volume > 1)
            {
                throw new ArgumentOutOfRangeException(nameof(Volume), Volume, "0〜1 で指定してください。");
            }
        }

        public int CameraDeadZone => Mathf.RoundToInt(CameraDeadZoneTiles * Units.Tile);
    }
}
