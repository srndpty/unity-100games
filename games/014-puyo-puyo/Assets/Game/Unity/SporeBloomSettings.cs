using System;
using Game.Core;
using UnityEngine;

namespace Game.Unity
{
    [CreateAssetMenu(menuName = "Game/Spore Bloom Settings")]
    public sealed class SporeBloomSettings : ScriptableObject
    {
        private const int MaxRepeatTicks = 60;
        private const int MaxCpuIntervalTicks = 120;

        [Tooltip("落下速度・接地猶予・得点・攻撃量などのルール。時間は 60Hz の Tick 数。")]
        public SporeConfig Rules = new();

        [Tooltip("CPU が1回操作するまでの Tick 数。大きいほど弱い。")]
        [Range(1, MaxCpuIntervalTicks)] public int CpuActionIntervalTicks = 10;

        [Tooltip("CPU が評価を無視して適当に置く確率。大きいほど弱い。")]
        [Range(0, 100)] public int CpuMistakePercent = 30;

        [Range(1, MaxRepeatTicks)] public int RepeatDelayTicks = 10;
        [Range(1, MaxRepeatTicks)] public int RepeatIntervalTicks = 2;
        [Range(0, 1)] public float Volume = .12f;

        public void Validate()
        {
            Rules.Validate();
            RequireRange(CpuActionIntervalTicks, 1, MaxCpuIntervalTicks, nameof(CpuActionIntervalTicks));
            RequireRange(CpuMistakePercent, 0, 100, nameof(CpuMistakePercent));
            RequireRange(RepeatDelayTicks, 1, MaxRepeatTicks, nameof(RepeatDelayTicks));
            RequireRange(RepeatIntervalTicks, 1, MaxRepeatTicks, nameof(RepeatIntervalTicks));
            if (!float.IsFinite(Volume) || Volume < 0 || Volume > 1) { throw new ArgumentOutOfRangeException(nameof(Volume), Volume, "0〜1 の範囲で指定してください。"); }
        }

        private static void RequireRange(int value, int min, int max, string name)
        {
            if (value < min || value > max) { throw new ArgumentOutOfRangeException(name, value, $"{min}〜{max} の範囲で指定してください。"); }
        }
    }
}
