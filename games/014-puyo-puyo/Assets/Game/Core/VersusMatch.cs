namespace Game.Core
{
    public enum MatchOutcome
    {
        InProgress,
        PlayerWin,
        CpuWin,
        Draw,
    }

    /// <summary>2人の盤面を同じ Tick で進め、攻撃の受け渡しと勝敗を決める。</summary>
    public sealed class VersusMatch
    {
        private const int PairStream = 1;
        private const int PlayerGarbageStream = 2;
        private const int CpuGarbageStream = 3;
        private const int CpuBrainStream = 4;

        public VersusMatch(SporeConfig config, int seed)
            : this(config, new PairSequence(DeriveSeed(seed, PairStream)), seed)
        {
        }

        public VersusMatch(SporeConfig config, IPairSource pairs, int seed, Board? playerBoard = null, Board? cpuBoard = null)
        {
            Player = new PlayerField(config, pairs, DeriveSeed(seed, PlayerGarbageStream), playerBoard);
            Cpu = new PlayerField(config, pairs, DeriveSeed(seed, CpuGarbageStream), cpuBoard);
            CpuSeed = DeriveSeed(seed, CpuBrainStream);
            UpdateOutcome();
        }

        public PlayerField Player { get; }
        public PlayerField Cpu { get; }

        /// <summary>CPU の判断に使う乱数のシード。</summary>
        public int CpuSeed { get; }

        public MatchOutcome Outcome { get; private set; }

        public bool IsOver => Outcome != MatchOutcome.InProgress;

        /// <summary>1 Tick 進める。攻撃は両者の Tick が済んでから届けるので、処理の順序で結果が変わらない。</summary>
        public void Tick()
        {
            if (IsOver) { return; }
            Player.Tick();
            Cpu.Tick();
            var toCpu = Player.TakeOutgoing();
            var toPlayer = Cpu.TakeOutgoing();
            Cpu.ReceiveGarbage(toCpu);
            Player.ReceiveGarbage(toPlayer);
            UpdateOutcome();
        }

        private static int DeriveSeed(int seed, int stream)
        {
            var random = new SeededRandom(unchecked(seed + (stream * (int)0x9E3779B9u)));
            return unchecked((int)random.NextUInt());
        }

        private void UpdateOutcome()
        {
            var playerLost = Player.Phase == FieldPhase.Defeated;
            var cpuLost = Cpu.Phase == FieldPhase.Defeated;
            if (playerLost && cpuLost) { Outcome = MatchOutcome.Draw; }
            else if (playerLost) { Outcome = MatchOutcome.CpuWin; }
            else if (cpuLost) { Outcome = MatchOutcome.PlayerWin; }
        }
    }
}
