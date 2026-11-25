using System;
using TileMotion;

namespace Game.Core
{
    /// <summary>
    /// 設定・技表・面のデータの指紋。状態のハッシュに混ぜ、規則の値が違えば同じ位置でも別のハッシュにする
    /// （以後の動きを決めるのは状態だけでなく規則の値もなので）。試合の始めに 1 回だけ計算する。
    /// </summary>
    public static class Fingerprint
    {
        public static uint Of(MoveTable table, BrawlConfig config, StageData stage)
        {
            if (table == null) { throw new ArgumentNullException(nameof(table)); }
            if (config == null) { throw new ArgumentNullException(nameof(config)); }
            if (stage == null) { throw new ArgumentNullException(nameof(stage)); }
            var hash = StateHash.Start();
            MixConfig(ref hash, config);
            MixTable(ref hash, table);
            MixStage(ref hash, stage);
            return hash.Value;
        }

        private static void MixConfig(ref StateHash hash, BrawlConfig c)
        {
            foreach (var value in new[]
            {
                c.TickRate, c.ViewWidth, c.ZMin, c.ZMax, c.DepthNum, c.DepthDen, c.EdgeMargin, c.SpawnMargin, c.BufferTicks,
                c.Capacity, c.MaxActiveEnemies, c.MaxAttackers, c.TokenCooldown, c.TokenTimeout, c.GrabDepth, c.GrabReach,
                c.GrabTicks, c.GrabOffset, c.MaxKnees, c.ReleaseStun, c.Lives, c.RespawnInvuln, c.WakeupInvuln, c.DeadTicks,
                c.LandTicks, c.FallLaunchX, c.FallLaunchY, c.FallGravity, c.ScrollSpeed, c.HealAmount, c.ItemWidth, c.ItemDepth,
            })
            {
                hash.Mix(value);
            }
        }

        private static void MixTable(ref StateHash hash, MoveTable table)
        {
            foreach (var a in table.Actors)
            {
                foreach (var value in new[]
                {
                    (int)a.Kind, a.Health, a.Walk, a.WalkZ, a.JumpX, a.JumpY, a.Gravity, a.Poise, a.DownTicks, a.WakeupTicks, a.Score,
                    a.Reach, a.Keep, a.FarRange, a.CloseRange, a.Rage, a.PushWidth, a.PushDepth,
                })
                {
                    hash.Mix(value);
                }

                MixBox(ref hash, a.Body);
                MixText(ref hash, a.Opener);
                MixText(ref hash, a.Far);
                MixText(ref hash, a.Close);
                foreach (var m in a.Moves)
                {
                    MixText(ref hash, m.Id);
                    MixText(ref hash, m.Next);
                    foreach (var value in new[]
                    {
                        (int)m.Kind, m.Startup, m.Active, m.Recovery, m.Damage, m.HitStun, m.HitStop, m.Knockback, m.Stagger,
                        m.Down ? 1 : 0, m.LaunchX, m.LaunchY, m.Advance, m.Window, m.Invuln, m.Cost, m.HasBox ? 1 : 0,
                    })
                    {
                        hash.Mix(value);
                    }

                    if (m.HasBox) { MixBox(ref hash, m.Box); }
                }
            }
        }

        private static void MixStage(ref StateHash hash, StageData stage)
        {
            hash.Mix(stage.StartX);
            hash.Mix(stage.IsArena);
            foreach (var section in stage.Sections)
            {
                hash.Mix(section.CameraX);
                foreach (var wave in section.Waves)
                {
                    hash.Mix((int)wave.Kind);
                    hash.Mix((int)wave.Side);
                    hash.Mix(wave.Z);
                    hash.Mix(wave.Delay);
                    hash.Mix(wave.Food);
                }
            }
        }

        private static void MixBox(ref StateHash hash, BoxShape box)
        {
            hash.Mix(box.X);
            hash.Mix(box.Y);
            hash.Mix(box.W);
            hash.Mix(box.H);
            hash.Mix(box.Depth);
        }

        private static void MixText(ref StateHash hash, string text)
        {
            hash.Mix(text.Length);
            foreach (var ch in text) { hash.Mix(ch); }
        }
    }
}
