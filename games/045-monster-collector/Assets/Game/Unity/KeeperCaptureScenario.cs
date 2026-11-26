#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Game.App;
using Game.Core;
using Game.Unity.Evidence;
using UnityEngine;

namespace Game.Unity
{
    /// <summary>
    /// <c>dev capture 045</c> の撮影台本。シード <see cref="EvidenceCapture.Seed"/> と Tick だけで進め、操縦役で通しを遊んで
    /// 各場面に最初に着いた Tick を撮る。同じビルドなら毎回同じ画像になる。
    /// </summary>
    public sealed class KeeperCaptureScenario : MonoBehaviour, ICaptureScenario
    {
        public const int MaxTicks = 400_000;

        public async Awaitable RunAsync(CaptureContext capture)
        {
            if (capture == null) { throw new ArgumentNullException(nameof(capture)); }
            var game = FindAnyObjectByType<KeeperGame>();
            if (game == null) { throw new InvalidOperationException("KeeperGame がありません。"); }
            await capture.WaitUntilAsync(() => game.Hud != null, 10f);

            var dir = Path.Combine(Application.temporaryCachePath, "thornwild_capture");
            if (Directory.Exists(dir)) { Directory.Delete(dir, true); }
            var save = new FileSaveStore(Path.Combine(dir, "save.txt"));
            var records = new FileSaveStore(Path.Combine(dir, "records.txt"));
            game.Reinitialize(save, records, EvidenceCapture.Seed);
            var flow = game.Flow;
            capture.Record("textTicks", game.Settings.TextTicks.ToString(CultureInfo.InvariantCulture));
            capture.Record("quietTicks", game.Settings.QuietTicks.ToString(CultureInfo.InvariantCulture));
            capture.Record("fingerprint", Fingerprint.Of(game.Data).ToString("X8", CultureInfo.InvariantCulture));

            async Awaitable Shoot(string name, string note)
            {
                game.Render();
                var w = flow.World;
                await capture.ShootAsync(name, w == null ? note : $"{note} (seed={w.Seed}, playTick={w.PlayTicks}, battles={w.BattleCount})");
            }

            await Shoot("01_title", "タイトル");
            flow.Tap(InputButton.Up);
            flow.Tap(InputButton.Confirm);
            flow.StepTicks(1);
            await Shoot("02_howto", "あそびかた");
            flow.Tap(InputButton.Confirm);
            flow.StepTicks(1);
            flow.Tap(InputButton.Confirm);
            flow.StepTicks(1);
            await Shoot("03_starter", "最初の 1 匹を選ぶ");

            var pilot = new Autopilot { SaveAtCenter = true };
            var taken = new HashSet<string>();
            var menusShown = false;
            var storageShown = false;
            for (int tick = 0; tick < MaxTicks && flow.Screen != FlowScreen.Result; tick++)
            {
                var shot = Scene(flow, taken);
                if (shot != null)
                {
                    taken.Add(shot.Value.Name);
                    await Shoot(shot.Value.Name, shot.Value.Note);
                }

                var w = flow.World;
                if (!menusShown && w != null && flow.Screen == FlowScreen.Field && !flow.InDialogue && !w.Explorer.IsMoving && w.Roster.Party.Count >= 3)
                {
                    menusShown = true;
                    flow.Tap(InputButton.Cancel);
                    flow.Tap(InputButton.Confirm);
                    flow.StepTicks(1);
                    await Shoot("10_party", "てもち");
                    flow.Tap(InputButton.Cancel);
                    flow.Tap(InputButton.Down);
                    flow.Tap(InputButton.Confirm);
                    flow.StepTicks(1);
                    await Shoot("11_dex", "ずかん");
                    flow.Tap(InputButton.Cancel);
                    flow.Tap(InputButton.Cancel);
                    flow.StepTicks(1);
                    continue;
                }

                if (!storageShown && w != null && flow.Screen == FlowScreen.CenterMenu && !flow.InDialogue && w.Roster.Party.Count >= 3)
                {
                    storageShown = true;
                    flow.Tap(InputButton.Down);
                    flow.Tap(InputButton.Confirm);
                    flow.StepTicks(1);
                    await Shoot("13_storage", "回復所のボックス");
                    flow.Tap(InputButton.Cancel);
                    flow.StepTicks(1);
                    continue;
                }

                pilot.Step(flow);
                flow.StepTicks(1);
            }

            if (flow.Screen != FlowScreen.Result) { throw new InvalidOperationException("操縦役が結果画面に着きませんでした。"); }
            await Shoot("19_result", $"結果（hash=0x{flow.World!.Hash():X8}）");
            foreach (var name in Expected)
            {
                if (!taken.Contains(name)) { throw new InvalidOperationException($"場面 {name} を撮れませんでした。"); }
            }

            flow.Tap(InputButton.Confirm);
            flow.StepTicks(1);
            await Shoot("20_title_records", "クリアの記録を出したタイトル");
            if (!save.TryWrite("THORNWILD-SAVE\nversion=1\nbroken", out var failure)) { throw new InvalidOperationException(failure); }
            flow.RefreshTitle();
            await Shoot("21_title_broken_save", "壊れたセーブの案内（つづきからを出さない）");
            save.Delete();
            records.Delete();
        }

        private static readonly string[] Expected =
        {
            "04_town", "05_wild_appears", "06_command", "07_moves", "08_super_effective", "09_status", "12_capture", "14_level_up",
            "15_learn_move", "16_center_save", "17_trainer_intro", "18_trainer_battle",
        };

        /// <summary>撮る場面に着いたか（まだ撮っていないもの）。</summary>
        private static (string Name, string Note)? Scene(KeeperFlow flow, HashSet<string> taken)
        {
            (string, string)? Once(string name, string note, bool condition) => condition && !taken.Contains(name) ? (name, note) : null;
            var w = flow.World;
            var p = flow.Presenter;
            var beat = p?.Current;
            var ev = beat?.Source;
            // 操縦役は文を送るので、出来事を当ててから 4 Tick 目（送れるようになる前）に撮る。
            var started = p != null && beat != null && p.ElapsedInBeat == 4;
            return Once("04_town", "灯ノ村", flow.Screen == FlowScreen.Field && !flow.InDialogue && w != null)
                ?? Once("05_wild_appears", "野生のモンスターが現れた", started && ev!.Kind == BattleEventKind.Started && p!.Kind == BattleKind.Wild)
                ?? Once("06_command", "戦闘のコマンド", flow.BattleMenu == BattleMenu.Root && flow.Screen == FlowScreen.Battle)
                ?? Once("07_moves", "技を選ぶ（属性と PP）", flow.BattleMenu == BattleMenu.Moves && flow.Screen == FlowScreen.Battle)
                ?? Once("08_super_effective", "効果は ばつぐん", started && ev!.Kind == BattleEventKind.Damaged && ev.Effectiveness > 100)
                ?? Once("09_status", "状態異常になった", started && ev!.Kind == BattleEventKind.StatusInflicted)
                ?? Once("12_capture", "捕獲玉で捕まえた", started && ev!.Kind == BattleEventKind.Captured)
                ?? Once("14_level_up", "レベルアップ", started && ev!.Kind == BattleEventKind.LeveledUp)
                ?? Once("15_learn_move", "4 つ埋まっていて技を入れ替える", flow.Screen == FlowScreen.LearnMove && !flow.InDialogue)
                ?? Once("16_center_save", "回復所で記録した", flow.InDialogue && flow.Dialogue.Contains("記録した", StringComparison.Ordinal))
                ?? Once("17_trainer_intro", "トレーナーが道を塞ぐ", flow.Screen == FlowScreen.Field && flow.InDialogue && w != null && w.Mode == WorldMode.Battle)
                ?? Once("18_trainer_battle", "トレーナー戦", started && ev!.Kind == BattleEventKind.SentOut && !ev.Player && p!.Kind == BattleKind.Trainer);
        }
    }
}
