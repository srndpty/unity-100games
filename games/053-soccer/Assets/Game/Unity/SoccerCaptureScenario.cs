#nullable enable
using System;
using System.Globalization;
using Game.App;
using Game.Core;
using Game.Unity.Evidence;
using UnityEngine;

namespace Game.Unity
{
    /// <summary>
    /// <c>dev capture 053</c> の撮影台本。シードは <see cref="EvidenceCapture.Seed"/> に固定し、青も CPU（自動操縦）にして Tick だけを進めるので、毎回同じ画像になる。
    /// タイトル・方針の選択・操作説明・キックオフ・役割の表示（攻撃と守備）・ループパス・ゴール・スローイン・一時停止・試合終了・結果を撮る。
    /// </summary>
    public sealed class SoccerCaptureScenario : MonoBehaviour, ICaptureScenario
    {
        public const int MaxTicks = 20000;

        public async Awaitable RunAsync(CaptureContext capture)
        {
            if (capture == null) { throw new ArgumentNullException(nameof(capture)); }
            var game = FindAnyObjectByType<SoccerGame>();
            if (game == null) { throw new InvalidOperationException("SoccerGame がありません。"); }
            await capture.WaitUntilAsync(() => game.Hud != null, 10f);
            game.Reinitialize(EvidenceCapture.Seed);
            var flow = game.Flow;
            flow.Autopilot = true;
            capture.Record("autopilot", "青・赤とも CPU（青は前から奪う・つなぐ）");
            capture.Record("matchMinutes", game.Settings.MatchMinutes.ToString(CultureInfo.InvariantCulture));

            async Awaitable Shoot(string name, string note)
            {
                game.Render();
                var m = flow.Match;
                var state = m == null ? "" : $" (tick={m.Clock}, score={m.Score(Team.Blue)}-{m.Score(Team.Red)}, hash=0x{m.Hash():X8})";
                await capture.ShootAsync(name, note + state);
            }

            void Tap(InputButton b)
            {
                flow.Tap(b);
                flow.StepTicks(1);
            }

            void Until(Func<Match, bool> done, string what)
            {
                for (int i = 0; i < MaxTicks; i++)
                {
                    var m = flow.Match!;
                    if (done(m)) { return; }
                    if (flow.Screen != FlowScreen.Playing) { throw new InvalidOperationException($"{what} の前に試合が終わりました"); }
                    flow.StepTicks(1);
                }

                throw new InvalidOperationException($"{what} に着きません");
            }

            await Shoot("01_title", "タイトル");
            Tap(InputButton.Down);
            Tap(InputButton.Right);
            await Shoot("02_tactics", "相手の方針を選ぶ（前から奪う・蹴り込む）");
            Tap(InputButton.Down);
            Tap(InputButton.Confirm);
            await Shoot("03_howto", "操作説明");
            Tap(InputButton.Confirm);
            Tap(InputButton.Confirm);
            if (flow.Screen != FlowScreen.Playing) { throw new InvalidOperationException("試合が始まりません"); }
            await Shoot("04_kickoff", "キックオフ");

            game.Stage.ShowRoles = true;
            Until(m => m.Phase == MatchPhase.Play && m.Owner >= 0 && Match.TeamOf(m.Owner) == Team.Blue && !m.Players[m.Owner].IsKeeper && m.Clock > 400
                && flow.BlueAi!.RoleOf(Match.FieldPlayer(Team.Blue, 0) + Match.FieldPlayer(Team.Blue, 1) - m.Owner) == Role.Support
                && m.PhaseTicks > 60, "青の保持と支援");
            await Shoot("05_attack_roles", "青の攻撃：保持者以外の味方が支援の点へ（役割の表示）");
            Until(m => m.Phase == MatchPhase.Play && m.Owner >= 0 && Match.TeamOf(m.Owner) == Team.Red && !m.Players[m.Owner].IsKeeper
                && flow.BlueAi!.RoleOf(1) is Role.Press or Role.Cover && flow.BlueAi.RoleOf(2) is Role.Press or Role.Cover, "赤の保持と寄せ・カバー");
            await Shoot("06_defense_roles", "青の守備：寄せとカバー（役割の表示）");
            game.Stage.ShowRoles = false;
            Until(m => m.Phase == MatchPhase.Play && m.Ball.Height > 1500 && AwayFromPlayers(m, 2500), "浮き球");
            await Shoot("07_lob", "浮き球（影と高さ）");
            Until(m => m.Phase == MatchPhase.Goal, "ゴール");
            await Shoot("08_goal", "ゴール");
            Until(m => m.Phase == MatchPhase.SetPiece && m.Restart == RestartKind.ThrowIn, "スローイン");
            await Shoot("09_throw_in", "スローイン（相手は 3 m 離れる）");
            Tap(InputButton.Cancel);
            await Shoot("10_pause", "一時停止");
            Tap(InputButton.Cancel);
            Until(m => m.Phase == MatchPhase.Ended, "試合終了");
            await Shoot("11_full_time", "試合終了");
            for (int i = 0; i < MaxTicks && flow.Screen != FlowScreen.Result; i++) { flow.StepTicks(1); }
            await Shoot("12_result", "結果");
        }

        private static bool AwayFromPlayers(Match m, int distance)
        {
            foreach (var p in m.Players)
            {
                if (IntGeometry.Vec2i.DistanceSquared(p.Position, m.Ball.Position) < (long)distance * distance) { return false; }
            }

            return true;
        }
    }
}
