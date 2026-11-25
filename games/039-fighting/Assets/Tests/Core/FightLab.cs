using System.IO;
using System.Runtime.CompilerServices;

namespace Game.Core.Tests
{
    internal static class FightLab
    {
        public static string MovesText([CallerFilePath] string source = "") => File.ReadAllText(MovesPath(source));

        public static string ReadmeText([CallerFilePath] string source = "")
        {
            var dir = Path.GetDirectoryName(source) ?? ".";
            return File.ReadAllText(Path.GetFullPath(Path.Combine(dir, "..", "..", "..", "README.md")));
        }

        public static MoveTable Table([CallerFilePath] string source = "") => MoveTable.Parse(MovesText(source));

        public static Duel NewDuel(int left = 0, int right = 1, DuelConfig? config = null, [CallerFilePath] string source = "")
        {
            var duel = new Duel(Table(source), config ?? new DuelConfig(), left, right)
            {
                RecordLog = true,
            };
            return duel;
        }

        public static void Place(Duel duel, int gapPx)
        {
            int mid = (duel.Config.WallMin + duel.Config.WallMax) / 2;
            int gap = gapPx * 256;
            duel.At(0).X = mid - (gap / 2);
            duel.At(1).X = mid + (gap / 2);
        }

        public static Pad Guard(Fighter fighter, bool crouch)
        {
            int dx = fighter.Facing > 0 ? -1 : 1;
            int dy = crouch ? -1 : 0;
            return new Pad(5 + dx + (3 * dy), false, false, false);
        }

        public static void Motion(Duel duel, int actor, AttackButton button)
        {
            var facing = duel.At(actor).Facing;
            int diag = facing > 0 ? 3 : 1;
            int forward = facing > 0 ? 6 : 4;
            bool light = button == AttackButton.Light || button == AttackButton.None;
            bool heavy = button == AttackButton.Heavy;
            bool sweep = button == AttackButton.Sweep;
            Step(duel, actor, new Pad(2, false, false, false));
            Step(duel, actor, new Pad(diag, false, false, false));
            Step(duel, actor, new Pad(forward, light, heavy, sweep));
        }

        public static void Step(Duel duel, int actor, Pad pad)
        {
            if (actor == 0) { duel.Step(pad, Pad.Neutral); }
            else { duel.Step(Pad.Neutral, pad); }
        }

        public static void Tap(Duel duel, int actor, string move)
        {
            Pad pad;
            switch (move)
            {
                case "light":
                    pad = new Pad(5, true, false, false);
                    break;
                case "heavy":
                    pad = new Pad(5, false, true, false);
                    break;
                case "sweep":
                    pad = new Pad(2, false, false, true);
                    break;
                case "throw":
                    pad = new Pad(5, true, true, false);
                    break;
                default:
                    Motion(duel, actor, AttackButton.Light);
                    return;
            }

            Step(duel, actor, pad);
        }

        private static string MovesPath(string source)
        {
            var dir = Path.GetDirectoryName(source) ?? ".";
            return Path.GetFullPath(Path.Combine(dir, "..", "..", "..", "Assets", "Game", "Data", "Moves.txt"));
        }
    }
}
