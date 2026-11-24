using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Game.Core;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Game.Unity.Tests
{
    /// <summary>
    /// 5 面の解答を、実際の Physics2D で探す（作業用。通常のテストでは走らない）。
    /// 1 発ずつ、角度 × 引きの長さ（× 爆発の弾は起爆の Tick）を総当たりし、勝つか、壊した標的・得点が最も多い撃ち方を選んで次の弾へ進む。
    /// 結果は Logs/solutions-found.txt に書く（引きは丸めずに書く。崩れ方が敏感な面では小数 3 桁に丸めただけで結果が変わった）。見つけた手順は Assets/Game/Data/Solutions.txt に写し、通常のテストで再生する。
    /// 実行：<c>unity test games/034-projectile-puzzle --mode PlayMode --filter SolutionSearch</c>
    /// </summary>
    [Explicit]
    public sealed class SolutionSearch
    {
        private static readonly float[] _lengths = { 1.2f, 1.6f, 2.0f, 2.4f };
        private static readonly int[] _fuses = { -1, 20, 40, 60 };

        [UnityTest]
        [Timeout(3600000)]
        public IEnumerator FindSolutions()
        {
            yield return SceneManager.LoadSceneAsync("PebbleSiege");
            yield return null;
            var game = Object.FindAnyObjectByType<PebbleSiegeGame>();
            game.enabled = false;
            var config = game.Settings.Rules;
            var book = game.Settings.Validate();
            var world = new PebbleSiegeWorld(game.Stage, config);
            var output = new StringBuilder();
            for (var i = 0; i < book.Stages.Count; i++)
            {
                var stage = book.Stages[i];
                var plan = new List<ShotStep>();
                SiegeRun best = null;
                for (var k = 0; k < stage.Shots.Count && best?.Phase != RunPhase.Won; k++)
                {
                    var fuses = stage.Shots[k] == ShotKind.Bomb ? _fuses : new[] { -1 };
                    ShotStep? choice = null;
                    var value = long.MinValue;
                    for (var angle = 0f; angle <= 75f; angle += 2.5f)
                    {
                        foreach (var length in _lengths)
                        {
                            foreach (var fuse in fuses)
                            {
                                var radians = angle * Mathf.Deg2Rad;
                                var step = new ShotStep(new V2(-Mathf.Cos(radians) * length, -Mathf.Sin(radians) * length), fuse);
                                plan.Add(step);
                                var run = Replay.Play(world, config, stage, i, plan);
                                plan.RemoveAt(plan.Count - 1);
                                var score = Value(run);
                                if (score > value)
                                {
                                    value = score;
                                    choice = step;
                                    best = run;
                                }
                            }
                        }

                        yield return null;
                    }

                    plan.Add(choice!.Value);
                }

                output.Append(FormattableString.Invariant($"stage  # {i + 1} {stage.Name}: {best?.Phase} 標的残り {best?.TargetsLeft} 得点 {best?.Score} Tick {best?.Ticks}\n"));
                foreach (var step in plan)
                {
                    output.Append(FormattableString.Invariant($"shot {step.Pull.X:R} {step.Pull.Y:R}{(step.DetonateAfter >= 0 ? " " + step.DetonateAfter : string.Empty)}\n"));
                }

                output.Append("end\n");
                Debug.Log(output.ToString());
            }

            world.Dispose();
            File.WriteAllText(Path.Combine(Application.dataPath, "..", "Logs", "solutions-found.txt"), output.ToString(), Encoding.UTF8);
        }

        private static long Value(SiegeRun run)
        {
            if (run.Phase == RunPhase.Won) { return long.MaxValue / 2 + run.Score; }
            return ((long)(run.Stage.TargetCount - run.TargetsLeft) * 1000000) + run.Score;
        }
    }
}
