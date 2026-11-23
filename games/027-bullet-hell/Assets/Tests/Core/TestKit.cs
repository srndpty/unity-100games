using System;

namespace Game.Core.Tests
{
    /// <summary>テストの共通の部品。</summary>
    internal static class TestKit
    {
        /// <summary>px の位置を単位へ。</summary>
        public static Vec2i Px(int x, int y) => new(x * Field.Unit, y * Field.Unit);

        /// <summary>px/Tick の速さを単位へ。</summary>
        public static int Speed(double pxPerTick) => (int)Math.Round(pxPerTick * BulletPool.SpeedOne);

        /// <summary>度を角度の単位へ。</summary>
        public static int Degrees(double degrees) => (int)Math.Round(degrees * Trig.FullTurn / 360.0);

        public static BulletSpawn Bullet(
            Vec2i origin,
            int angle,
            int speed,
            int accel = 0,
            int? maxSpeed = null,
            int curve = 0,
            int life = 0,
            BulletShape shape = BulletShape.Small) =>
            new(origin, angle, speed, accel, maxSpeed ?? Math.Max(speed, 16 * BulletPool.SpeedOne), curve, life, shape, BulletColor.Red);

        /// <summary>撃たない発射器（試合の動きだけを見るテスト用）。</summary>
        public const string Quiet = "emit ring every=3600 delay=36000 speed=1";

        /// <summary>テスト用の短い設定：弾幕の間 1 Tick、残機 9。</summary>
        public static VeilConfig QuickConfig() => new() { IntervalTicks = 1, Lives = 9 };

        /// <summary>同じ弾幕 3 つのボスを 2 体並べた台本の組。</summary>
        public static BossScript[] Bosses(string body, int hp = 100000, int time = 36000, int radius = 24)
        {
            var pattern = $"pattern \"P\" hp={hp} time={time}\n{body}\n";
            var script = ScriptParser.ParseOrThrow($"boss \"B\" radius={radius}\n{pattern}{pattern}{pattern}", "test");
            return new[] { script, script };
        }

        public static VeilRun RunOf(string body, VeilConfig? config = null, int hp = 100000, int time = 36000, int seed = 1) =>
            new(config ?? QuickConfig(), Bosses(body, hp, time), seed);

        /// <summary>条件を満たすまで同じ操作で進め、かかった Tick 数を返す（上限を超えたら失敗）。</summary>
        public static int StepUntil(VeilRun run, Func<VeilRun, bool> done, PlayerCommand command = default, int limit = 20000)
        {
            for (var n = 1; n <= limit; n++)
            {
                run.Step(command);
                if (done(run)) { return n; }
            }

            throw new InvalidOperationException($"{limit} Tick で条件を満たしませんでした。");
        }

        /// <summary>本番の台本（Assets/Game/Patterns）を読む。.NET（テストの出力先）と Unity（プロジェクトの直下）の両方から探す。</summary>
        public static BossScript[] LoadScripts()
        {
            var directory = PatternDirectory();
            return new[]
            {
                ScriptParser.ParseOrThrow(System.IO.File.ReadAllText(System.IO.Path.Combine(directory, "boss1.txt")), "boss1.txt"),
                ScriptParser.ParseOrThrow(System.IO.File.ReadAllText(System.IO.Path.Combine(directory, "boss2.txt")), "boss2.txt"),
            };
        }

        public static string PatternDirectory()
        {
            foreach (var start in new[] { NUnit.Framework.TestContext.CurrentContext.TestDirectory, Environment.CurrentDirectory })
            {
                for (var directory = new System.IO.DirectoryInfo(start); directory != null; directory = directory.Parent)
                {
                    var candidate = System.IO.Path.Combine(directory.FullName, "Assets", "Game", "Patterns");
                    if (System.IO.Directory.Exists(candidate)) { return candidate; }
                }
            }

            throw new System.IO.DirectoryNotFoundException("Assets/Game/Patterns が見つかりません。");
        }

        /// <summary>
        /// 弾 1 発の素朴な参照実装（置き場とは別に書いた、1 発ずつ進める版）。
        /// 置き場の SoA の実装と同じ弾道式を、配列や詰め直しなしで追う。
        /// </summary>
        public sealed class ReferenceBullet
        {
            private long _x;
            private long _y;
            private int _angle;
            private int _speed;
            private readonly int _accel;
            private readonly int _maxSpeed;
            private readonly int _curve;

            public ReferenceBullet(BulletSpawn spawn)
            {
                _x = (long)spawn.Origin.X << BulletPool.PositionShift;
                _y = (long)spawn.Origin.Y << BulletPool.PositionShift;
                _angle = spawn.Angle;
                _speed = spawn.Speed;
                _accel = spawn.Accel;
                _maxSpeed = spawn.MaxSpeed;
                _curve = spawn.Curve;
            }

            public Vec2i Position => new((int)(_x >> BulletPool.PositionShift), (int)(_y >> BulletPool.PositionShift));

            public void Step()
            {
                _angle = (_angle + _curve) % Trig.FullTurn;
                if (_angle < 0) { _angle += Trig.FullTurn; }
                _speed = Math.Min(Math.Max(_speed + _accel, 0), _maxSpeed);
                _x += (long)_speed * Trig.Cos(_angle);
                _y += (long)_speed * Trig.Sin(_angle);
            }
        }
    }
}
