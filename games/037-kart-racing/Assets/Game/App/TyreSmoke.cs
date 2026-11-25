using System;
using Game.Core;

namespace Game.App
{
    /// <summary>その Tick に車の後ろから出す煙の種類。</summary>
    [Flags]
    public enum Smoke
    {
        None = 0,

        /// <summary>止まった状態からの加速（ホイールスピン）。</summary>
        Launch = 1,

        /// <summary>前進中のブレーキ。</summary>
        Brake = 1 << 1,

        /// <summary>ドリフト。</summary>
        Drift = 1 << 2,

        /// <summary>芝の上を走る土ぼこり。</summary>
        Dust = 1 << 3,
    }

    /// <summary>車の状態（その Tick の操作・速さ・ドリフト・路面）から煙を決める。表示だけに使い、ルールには関わらない。</summary>
    public static class TyreSmoke
    {
        /// <summary>加速を押していて、この速さ（mm/Tick）未満の間はスタートのホイールスピンとする。</summary>
        public const int LaunchBelow = 160;

        /// <summary>ブレーキを押していて、この速さより速く前進している間は煙を出す。</summary>
        public const int BrakeAbove = 90;

        /// <summary>芝の上で、この速さより速く動いている間は土ぼこりを出す。</summary>
        public const int DustAbove = 60;

        public static Smoke Of(Kart kart)
        {
            if (kart.Finished) { return Smoke.None; }
            var smoke = Smoke.None;
            var input = kart.Input;
            if (kart.Offroad)
            {
                if (Math.Abs(kart.Speed) > DustAbove) { smoke |= Smoke.Dust; }
            }
            else if (input.Accelerate && !input.Brake && kart.Speed > 0 && kart.Speed < LaunchBelow)
            {
                smoke |= Smoke.Launch;
            }

            if (input.Brake && kart.Speed > BrakeAbove) { smoke |= Smoke.Brake; }
            if (kart.DriftTicks > 0) { smoke |= Smoke.Drift; }
            return smoke;
        }
    }
}
