using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using TileMotion;

namespace Game.Core
{
    /// <summary>
    /// 難度曲線。速さと解禁される難度は、進んだ距離だけで決まり、単調に増えて上限で止まる。
    /// 速さはチャンクの入口の距離で決め、チャンクの中では変えない（到達性をチャンクごとの速さで厳密に検査できる）。
    /// </summary>
    public static class Difficulty
    {
        public static int SpeedLevel(RunConfig config, long distance)
        {
            if (distance <= 0) { return 0; }
            var level = distance / ((long)config.SpeedLevelMeters * Units.Tile);
            return (int)Math.Min(level, config.MaxSpeedLevel);
        }

        /// <summary>入口の距離が <paramref name="distance"/> の区間の速さ（単位/Tick）。</summary>
        public static int Speed(RunConfig config, long distance) => config.BaseSpeed + (config.SpeedStep * SpeedLevel(config, distance));

        /// <summary>入口の距離が <paramref name="distance"/> のチャンクに出せる難度の上限。</summary>
        public static int Tier(RunConfig config, long distance)
        {
            if (distance <= 0) { return 1; }
            var tier = 1 + (distance / ((long)config.TierMeters * Units.Tile));
            return (int)Math.Min(tier, config.MaxTier);
        }

        /// <summary>難度 <paramref name="tier"/> のチャンクが最初に出うる入口の距離（助走の後）。</summary>
        public static long Unlocks(RunConfig config, int tier) =>
            Math.Max((long)config.RunwayMeters * Units.Tile, (long)(tier - 1) * config.TierMeters * Units.Tile);

        /// <summary>速さと難度の両方が上限に達する距離（m）。</summary>
        public static long CapMeters(RunConfig config) =>
            Math.Max((long)config.MaxSpeedLevel * config.SpeedLevelMeters, (long)(config.MaxTier - 1) * config.TierMeters);
    }

    /// <summary>
    /// 検査を通った設定とチャンクの組。作るときに、各チャンクを出うる全ての速さ・全ての入口のレーンで
    /// 通り抜けられるか探索し（<see cref="Planner.Passable"/>）、通れないものがあれば例外にする（fail-closed）。
    /// 検査は重いので、1 つ作って試合ごとに使い回す。
    /// </summary>
    public sealed class RunRules
    {
        private RunRules(RunConfig config, ChunkDef[] chunks, Planner planner)
        {
            Config = config;
            Chunks = Array.AsReadOnly(chunks);
            Planner = planner;
            var shortest = int.MaxValue;
            foreach (var chunk in chunks) { shortest = Math.Min(shortest, chunk.Length(config)); }
            ShortestChunk = shortest;
        }

        public RunConfig Config { get; }

        public ReadOnlyCollection<ChunkDef> Chunks { get; }

        public Planner Planner { get; }

        public int ShortestChunk { get; }

        /// <summary>前方・後方の長さから決まる、生きているチャンクの数の上界。</summary>
        public static long ActiveChunkBound(RunConfig config, int shortestChunk) =>
            ((((long)config.LookAheadMeters + config.BehindMeters) * Units.Tile) / shortestChunk) + 2;

        public static RunRules Build(RunConfig config, IReadOnlyList<ChunkSpec> specs)
        {
            if (config == null) { throw new ArgumentNullException(nameof(config)); }
            if (specs == null) { throw new ArgumentNullException(nameof(specs)); }
            config.Validate();
            var copy = config.Copy();
            if (specs.Count == 0) { throw new ArgumentException("チャンクを 1 つ以上定義してください。", nameof(specs)); }
            var chunks = new ChunkDef[specs.Count];
            for (var i = 0; i < specs.Count; i++) { chunks[i] = ChunkDef.Parse(i, specs[i], copy.MaxTier); }
            for (var tier = 1; tier <= copy.MaxTier; tier++)
            {
                if (Array.FindIndex(chunks, c => c.Tier <= tier) < 0) { throw new ArgumentException($"難度 {tier} までに出せるチャンクがありません。", nameof(specs)); }
            }

            var shortest = int.MaxValue;
            foreach (var chunk in chunks) { shortest = Math.Min(shortest, chunk.Length(copy)); }
            var bound = ActiveChunkBound(copy, shortest);
            if (copy.MaxActiveChunks < bound)
            {
                throw new ArgumentException($"生きているチャンクの上限 {copy.MaxActiveChunks} は、前方・後方の長さと最も短いチャンクから決まる {bound} 以上にしてください。", nameof(config));
            }

            var planner = new Planner(copy, new MotionGraph(copy));
            foreach (var chunk in chunks)
            {
                var problem = FindUnpassable(copy, planner, chunk);
                if (problem != null) { throw new ArgumentException(problem, nameof(specs)); }
            }

            return new RunRules(copy, chunks, planner);
        }

        /// <summary>チャンクを出うる速さ（解禁される距離の速さ〜最も速い段階）の一覧。</summary>
        public static IEnumerable<int> SpeedsFor(RunConfig config, int tier)
        {
            var from = Difficulty.SpeedLevel(config, Difficulty.Unlocks(config, tier));
            for (var level = from; level <= config.MaxSpeedLevel; level++) { yield return config.BaseSpeed + (config.SpeedStep * level); }
        }

        /// <summary>通れない速さと入口のレーンがあれば、その説明を返す。</summary>
        public static string? FindUnpassable(RunConfig config, Planner planner, ChunkDef chunk)
        {
            if (planner == null) { throw new ArgumentNullException(nameof(planner)); }
            if (chunk == null) { throw new ArgumentNullException(nameof(chunk)); }
            foreach (var speed in SpeedsFor(config, chunk.Tier))
            {
                for (var lane = 0; lane < RunConfig.Lanes; lane++)
                {
                    if (!planner.Passable(chunk, speed, lane)) { return $"チャンク {chunk.Name} は速さ {speed}・入口のレーン {lane} から通り抜けられません（到達不能な配置）。"; }
                }
            }

            return null;
        }
    }
}
