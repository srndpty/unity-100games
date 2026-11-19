using System;
using Game.Core;
using UnityEngine;

namespace Game.Unity
{
    [CreateAssetMenu(menuName = "Game/Nine Lattice Settings")]
    public sealed class NineLatticeSettings : ScriptableObject
    {
        private const int MaxHintGlowTicks = 600;

        [Tooltip("問題の生成。難しさごとの与え（初めから入っている数字）の目標数 22〜60。")]
        public SudokuConfig Rules = new();

        [Tooltip("ヒントで指したマスを光らせる時間（60Hz の Tick 数）。")]
        public int HintGlowTicks = 120;

        [Range(0, 1)] public float Volume = .12f;

        public void Validate()
        {
            Rules.Validate();
            if (HintGlowTicks < 0 || HintGlowTicks > MaxHintGlowTicks) { throw new ArgumentOutOfRangeException(nameof(HintGlowTicks), HintGlowTicks, $"0〜{MaxHintGlowTicks} の範囲で指定してください。"); }
            if (!float.IsFinite(Volume) || Volume < 0 || Volume > 1) { throw new ArgumentOutOfRangeException(nameof(Volume), Volume, "0〜1 の範囲で指定してください。"); }
        }
    }
}
