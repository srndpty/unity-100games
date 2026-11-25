using System.IO;
using System.Runtime.CompilerServices;

namespace Game.Core.Tests
{
    /// <summary>テストの場面を作る。データは作品の Assets/Game/Data から読む。</summary>
    internal static class Lab
    {
        public const int Px = 256;

        public static readonly Pad Attack = new(0, 0, true, false, false);

        public static readonly Pad Jump = new(0, 0, false, true, false);

        public static readonly Pad Special = new(0, 0, false, false, true);

        public static string DataText(string name, [CallerFilePath] string source = "")
        {
            var dir = Path.GetDirectoryName(source) ?? ".";
            return File.ReadAllText(Path.GetFullPath(Path.Combine(dir, "..", "..", "Game", "Data", name)));
        }

        public static string ReadmeText([CallerFilePath] string source = "")
        {
            var dir = Path.GetDirectoryName(source) ?? ".";
            return File.ReadAllText(Path.GetFullPath(Path.Combine(dir, "..", "..", "..", "README.md")));
        }

        public static MoveTable Table() => MoveTable.Parse(DataText("Moves.txt"));

        public static StageData Stage() => StageData.Parse(DataText("Stage.txt"));

        /// <summary>波の無い闘技場。敵は考えない（止まったまま）。</summary>
        public static Brawl Arena(BrawlConfig? config = null, int seed = 1)
        {
            var brawl = new Brawl(Table(), config ?? new BrawlConfig(), StageData.Arena(), seed)
            {
                AiEnabled = false,
            };
            return brawl;
        }

        /// <summary>プレイヤーの前（向きの側）dx px、奥へ dz 単位に敵を出す。</summary>
        public static Actor Foe(Brawl brawl, ActorKind kind, int dxPx, int dzUnits = 0)
        {
            var p = brawl.Player;
            var foe = brawl.Spawn(kind, p.X + (p.Facing * dxPx * Px), p.Z, -p.Facing);
            brawl.Place(foe, foe.X, p.Z + dzUnits, -p.Facing);
            return foe;
        }

        public static void Run(Brawl brawl, Pad pad, int ticks)
        {
            for (int i = 0; i < ticks; i++) { brawl.Step(pad); }
        }

        public static void Idle(Brawl brawl, int ticks) => Run(brawl, Pad.Neutral, ticks);

        /// <summary>1 Tick だけ押して離す。</summary>
        public static void Tap(Brawl brawl, Pad pad)
        {
            brawl.Step(pad);
            brawl.Step(Pad.Neutral);
        }

        /// <summary>条件が成り立つまで進める（上限を超えたら false）。</summary>
        public static bool Until(Brawl brawl, System.Func<Brawl, bool> done, int limit = 600, Pad? pad = null)
        {
            for (int i = 0; i < limit; i++)
            {
                if (done(brawl)) { return true; }
                brawl.Step(pad ?? Pad.Neutral);
            }

            return done(brawl);
        }
    }
}
