using System;
using Game.Core;

namespace Game.App
{
    public static class Autopilot
    {
        public static FishingCommand Next(FishingState game)
        {
            if (game == null) { throw new ArgumentNullException(nameof(game)); }
            return new FishingCommand(game.Phase == FishingPhase.Biting, game.Tension < 55);
        }
        public static void Step(FishingFlow flow)
        {
            if (flow == null) { throw new ArgumentNullException(nameof(flow)); }
            if (flow.Game == null) { return; }
            var command = Next(flow.Game);
            flow.SetInput(false, false); flow.SetInput(command.Hook, command.Reel); flow.StepOneTick();
        }
    }
}
