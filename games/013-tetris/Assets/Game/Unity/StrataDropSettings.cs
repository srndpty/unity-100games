using System;
using Game.Core;
using UnityEngine;

namespace Game.Unity
{
    [CreateAssetMenu(menuName = "Game/Strata Drop Settings")]
    public sealed class StrataDropSettings : ScriptableObject
    {
        private const int MaxRepeatTicks = 60;

        [Tooltip("重力・接地猶予・得点などのルール。時間は 60Hz の Tick 数。")]
        public StrataConfig Rules = new();

        [Range(1, MaxRepeatTicks)] public int RepeatDelayTicks = 10;
        [Range(1, MaxRepeatTicks)] public int RepeatIntervalTicks = 2;
        [Range(0, 1)] public float Volume = .12f;

        public void Validate()
        {
            Rules.Validate();
            RequireRange(RepeatDelayTicks, nameof(RepeatDelayTicks));
            RequireRange(RepeatIntervalTicks, nameof(RepeatIntervalTicks));
            if (!float.IsFinite(Volume) || Volume < 0 || Volume > 1) { throw new ArgumentOutOfRangeException(nameof(Volume), Volume, "0〜1 の範囲で指定してください。"); }
        }

        private static void RequireRange(int value, string name)
        {
            if (value < 1 || value > MaxRepeatTicks) { throw new ArgumentOutOfRangeException(name, value, $"1〜{MaxRepeatTicks} の範囲で指定してください。"); }
        }
    }
}
