using TileMotion;

namespace Game.Core
{
    /// <summary>
    /// 設定の自機が平らな床から跳んで届く足の高さ（単位）を、自機を実際に動かして測る。
    /// 式で書かずに同じ <see cref="Player.Step"/> を通すので、1 Tick の順序まで含めてゲーム本体と食い違わない。
    /// 二段ジャンプは、地上のジャンプの頂点（上向きの速さが 0 以下になった Tick）でキーを離し、次の Tick に押し直す。
    /// </summary>
    public static class JumpReach
    {
        private const int MaxTicks = 600;

        public static int Measure(ExplorerConfig config, bool doubleJump)
        {
            var floor = new OpenFloor();
            var player = new Player(config, new Box(0, 0, Player.Width, Player.Height));
            var abilities = doubleJump ? Ability.DoubleJump : Ability.None;

            // 開始時のキーは押したまま扱いなので、一度離してから跳ぶ。
            player.Step(floor, PlayerInput.None, abilities, false);
            var best = 0;
            var phase = 0;
            for (var tick = 0; tick < MaxTicks; tick++)
            {
                var jump = phase switch
                {
                    0 => true,
                    1 => false,
                    _ => true,
                };
                player.Step(floor, new PlayerInput(0, jump, false), abilities, false);
                if (player.Body.Bottom > best) { best = player.Body.Bottom; }
                if (phase == 0 && player.VelocityY <= 0 && tick > 0)
                {
                    phase = doubleJump ? 1 : 3;
                }
                else if (phase == 1)
                {
                    phase = 2;
                }

                if (tick > 0 && player.Grounded) { break; }
            }

            return best;
        }

        /// <summary>y = 0 より下が全部固体の、果てのない床。</summary>
        private sealed class OpenFloor : ISolidGrid
        {
            public bool IsSolid(int column, int row) => row < 0;
        }
    }
}
