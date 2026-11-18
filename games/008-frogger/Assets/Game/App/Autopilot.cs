using System;
using Game.Core;

namespace Game.App
{
    public static class Autopilot
    {
        // 既定面の通し確認用。8Tickごとに上へ進み、動く足場上でも待つ。
        public static MoveDirection Next(FroggerState game)
        {
            if (game == null) { throw new ArgumentNullException(nameof(game)); }
            return game.TickCount % 8 == 0 ? MoveDirection.Up : MoveDirection.None;
        }
    }
}
