using Game.Core;

namespace Game.App
{
    /// <summary>
    /// 1 試合分の試合状態と CPU を、1 つのシードから組み立てる。
    /// 記録したシードとプレイヤー入力列があれば、何試合目であっても単独で同じ試合を再現できる。
    /// </summary>
    public sealed class SeededMatch
    {
        private readonly CpuController _cpu;

        public SeededMatch(PongConfig config, CpuConfig cpuConfig, int seed)
        {
            Seed = seed;
            State = new MatchState(config, seed);
            // CPU の狙いのぶれは、サーブと別系列の乱数にする。
            _cpu = new CpuController(cpuConfig, Side.Right, ~seed);
        }

        public int Seed { get; }

        public MatchState State { get; }

        /// <summary>プレイヤー（左）の入力で 1 Tick 進める。CPU（右）の入力はこの場で決める。</summary>
        public void Tick(float playerInput)
        {
            State.Tick(playerInput, _cpu.Decide(State));
        }
    }
}
