using System;

namespace Game.Core.Tests
{
    internal static class TestKit
    {
        public static TennisConfig Config() => new();

        /// <summary>CPU に任せず、両方とも何もしない Tick を進める。</summary>
        public static void Idle(TennisMatch match, int ticks)
        {
            for (var i = 0; i < ticks; i++) { match.Step(AthleteCommand.Idle, AthleteCommand.Idle); }
        }

        /// <summary>条件を満たすまで両方とも何もしない Tick を進める。上限を超えたら例外。</summary>
        public static void IdleUntil(TennisMatch match, Func<TennisMatch, bool> condition, int limit = 2000)
        {
            for (var i = 0; i < limit; i++)
            {
                if (condition(match)) { return; }
                match.Step(AthleteCommand.Idle, AthleteCommand.Idle);
            }

            throw new InvalidOperationException("条件を満たしませんでした。");
        }

        public static AthleteCommand Press(ShotKind kind = ShotKind.Stroke, int moveX = 0) =>
            new(moveX, 0, kind != ShotKind.Lob, kind == ShotKind.Lob);

        /// <summary>
        /// 手前の選手のサーブ待ちから、トスを上げて頂点から <paramref name="offset"/> Tick ずらした Tick に打つ。奥は何もしない。
        /// </summary>
        public static void NearServe(TennisMatch match, int offset = 0, int aim = 0)
        {
            if (match.Rally.Phase != RallyPhase.Ready || match.Rally.Server != Side.Near) { throw new InvalidOperationException("手前のサーブ待ちではありません。"); }
            match.Step(Press(), AthleteCommand.Idle);
            match.Step(AthleteCommand.Idle, AthleteCommand.Idle);
            while (match.Rally.TossTick < match.Config.TossApexTick + offset)
            {
                match.Step(AthleteCommand.Idle, AthleteCommand.Idle);
            }

            match.Step(Press(ShotKind.Stroke, aim), AthleteCommand.Idle);
        }

        /// <summary>球だけを飛ばし、最初の出来事を返す。</summary>
        public static BallEvent FlyToFirstEvent(Ball ball, TennisConfig config, int limit = 600)
        {
            for (var i = 0; i < limit; i++)
            {
                var e = ball.Step(config.Physics);
                if (e.Kind != BallEventKind.None) { return e; }
            }

            throw new InvalidOperationException("球が弾みませんでした。");
        }

        /// <summary>手前の選手が打った球として、狙った着地点へ飛ぶ球を作る（ネットの余裕つき）。</summary>
        public static Vec3 VelocityTo(Vec3 from, int targetX, int targetY, TennisConfig config, int clearance = 5000) =>
            ShotSolver.Solve(from, targetX, targetY, config.StrokeSpeed, clearance, config.Gravity);
    }
}
