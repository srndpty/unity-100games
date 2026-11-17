using System;

namespace Game.Core
{
    public enum FishKind { Crucian, Bass, Carp }
    [Serializable]
    public sealed class FishProfile
    {
        public FishKind Kind;
        public float Distance = 10;
        public float Stamina = 60;
        public float Pull = .8f;
        public float RunMin = 1;
        public float RunMax = 2;
        public float RestMin = 2;
        public float RestMax = 3;
        public FishProfile Copy() => (FishProfile)MemberwiseClone();
        public void Validate()
        {
            FishingConfig.Range(Distance, 2, 28); FishingConfig.Range(Stamina, 10, 200);
            FishingConfig.Range(Pull, .1f, 1.6f);
            FishingConfig.Period(RunMin, RunMax); FishingConfig.Period(RestMin, RestMax);
        }
    }
    [Serializable]
    public sealed class FishingConfig
    {
        public const int TickRate = 60;
        public float CastSeconds = .5f;
        public float WaitMin = 2;
        public float WaitMax = 4;
        public float ApproachSeconds = .5f;
        public float BiteSeconds = 1;
        public float LandingSeconds = .8f;
        public float TurnMin = .5f;
        public float TurnMax = 1;
        public float BreakSeconds = .75f;
        public float SlackSeconds = 1.5f;
        public float InitialTension = 40;
        public float ReelTension = 38;
        public float PullTension = 18;
        public float ReleaseTension = 32;
        public float ReelSpeed = 2.4f;
        public float FishSpeed = 1.2f;
        public float Fatigue = 5;
        public float ReelFatigue = 5;
        public FishProfile[] Fish =
        {
            new() { Kind = FishKind.Crucian },
            new() { Kind = FishKind.Bass, Distance = 16, Stamina = 90, Pull = 1.1f, RunMin = 2, RunMax = 3, RestMin = 1, RestMax = 2 },
            new() { Kind = FishKind.Carp, Distance = 22, Stamina = 120, Pull = 1.4f, RunMin = 3, RunMax = 4, RestMin = 1, RestMax = 2 }
        };
        public FishingConfig Copy()
        {
            var copy = (FishingConfig)MemberwiseClone();
            copy.Fish = Array.ConvertAll(Fish, fish => fish.Copy()); return copy;
        }
        public void Validate()
        {
            Range(CastSeconds, .1f, 2); Period(WaitMin, WaitMax); Range(ApproachSeconds, .1f, WaitMin);
            Range(BiteSeconds, .25f, 3); Range(LandingSeconds, .1f, 3); Period(TurnMin, TurnMax);
            Range(BreakSeconds, .25f, 3); Range(SlackSeconds, .5f, 4); Range(InitialTension, 20, 70);
            Range(ReelTension, 30, 50); Range(PullTension, 10, 20); Range(ReleaseTension, 25, 40);
            Range(ReelSpeed, 2, 4); Range(FishSpeed, .5f, 1.5f); Range(Fatigue, 2, 10); Range(ReelFatigue, 0, 10);
            if (Fish == null || Fish.Length != 3) { throw new ArgumentException("魚は3種必要です。"); }
            var seen = 0;
            foreach (var fish in Fish)
            {
                if (fish == null || (int)fish.Kind < 0 || (int)fish.Kind > 2 || (seen & (1 << (int)fish.Kind)) != 0)
                { throw new ArgumentException("魚種が不正または重複しています。"); }
                seen |= 1 << (int)fish.Kind; fish.Validate();
                if (ReleaseTension <= (float)((double)PullTension * fish.Pull) || ReelTension <= ReleaseTension)
                { throw new ArgumentException("緩めると張力が下がり、巻くと上がる設定が必要です。"); }
            }
        }
        public static int Ticks(float seconds)
        {
            Range(seconds, 0, 60);
            var ticks = (double)seconds * TickRate;
            var nearest = Math.Round(ticks);
            return seconds == (float)(nearest / TickRate) ? (int)nearest : (int)Math.Ceiling(ticks);
        }
        public static void Range(float value, float min, float max)
        {
            if (!float.IsFinite(value) || value < min || value > max) { throw new ArgumentException("設定値が範囲外です。"); }
        }
        public static void Period(float min, float max) { Range(min, .1f, 10); Range(max, min, 10); }
    }
}
