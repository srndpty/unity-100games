using System;
using IntGeometry;
using TileMotion;

namespace Game.Core.Tests
{
    /// <summary>テスト用の設定と操作の補助。既定の定義の並び（敵：mite・gnat・husk・warden、武器：bolt・orbit・pulse・lash・arc）を使う。</summary>
    internal static class TestKit
    {
        public const int T = Units.Tile;

        public const int Mite = 0;
        public const int Gnat = 1;
        public const int Husk = 2;
        public const int Warden = 3;

        public const int Bolt = 0;
        public const int Orbit = 1;
        public const int Pulse = 2;
        public const int Lash = 3;
        public const int Arc = 4;

        /// <summary>敵が出ない設定（区間の目標の生存数を 0 にする）。</summary>
        public static TideConfig Quiet(Action<TideConfig>? edit = null)
        {
            var config = new TideConfig();
            foreach (var stage in config.Stages) { stage.TargetAlive = 0; }
            edit?.Invoke(config);
            return config;
        }

        /// <summary>試合を作る。<paramref name="unarmed"/> なら最初の武器も外す（狙った武器だけを確かめる）。</summary>
        public static TideRun Run(TideConfig? config = null, int seed = 1, bool unarmed = true)
        {
            var run = new TideRun(config ?? Quiet(), seed);
            if (unarmed)
            {
                for (var w = 0; w < run.Config.Weapons.Length; w++) { run.SetWeaponLevel(w, 0); }
            }

            return run;
        }

        /// <summary><paramref name="ticks"/> Tick の間、動かない。</summary>
        public static RunEvents Wait(TideRun run, int ticks)
        {
            var events = RunEvents.None;
            for (var i = 0; i < ticks; i++)
            {
                run.Step(PlayerCommand.Idle);
                events |= run.TakeEvents();
            }

            return events;
        }

        public static Vec2i At(int x, int y) => new(x, y);

        /// <summary>敵の番号 <paramref name="id"/> の今の並びの位置（いなければ -1）。</summary>
        public static int IndexOf(TideRun run, int id)
        {
            for (var i = 0; i < run.Enemies.Count; i++)
            {
                if (run.Enemies.Id(i) == id) { return i; }
            }

            return -1;
        }

        /// <summary>番号 <paramref name="id"/> の敵の体力（いなければ 0）。</summary>
        public static int HealthOf(TideRun run, int id)
        {
            var i = IndexOf(run, id);
            return i < 0 ? 0 : run.Enemies.Health(i);
        }

        /// <summary>止まって動かない、体力の多い敵の定義にする（当たりの数を体力の減りで数える）。</summary>
        public static void Sturdy(TideConfig config, int def, int health = 10000)
        {
            config.Enemies[def].Health = health;
            config.Enemies[def].Speed = 1;
            config.Enemies[def].ContactDamage = 0;
        }
    }
}
