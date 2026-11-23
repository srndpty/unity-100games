using System;
using Game.Core;
using UnityEngine;

namespace Game.Unity
{
    public sealed class RhythmSettings : ScriptableObject
    {
        public RhythmRules Rules = new();
        public float LeadSeconds = 1.8f;
        public float Volume = .35f;
        public AudioClip[] Clips;

        public void Validate()
        {
            Rules.Validate();
            if (!float.IsFinite(LeadSeconds) || LeadSeconds is < .5f or > 4 ||
                !float.IsFinite(Volume) || Volume is < 0 or > 1)
            {
                throw new InvalidOperationException("表示先行時間または音量が無効です。");
            }
        }
    }
}
