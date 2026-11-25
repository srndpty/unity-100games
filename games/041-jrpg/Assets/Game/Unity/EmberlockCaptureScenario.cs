#nullable enable
using System;
using System.IO;
using Game.App;
using Game.Core;
using Game.Unity.Evidence;
using UnityEngine;

namespace Game.Unity
{
    /// <summary>
    /// <c>dev capture 041</c> 用の決定論的証跡撮影台本。
    /// 乱数シード <see cref="EvidenceCapture.Seed"/> と Tick 進行だけで全 14 場面を撮影する。
    /// </summary>
    public sealed class EmberlockCaptureScenario : MonoBehaviour, ICaptureScenario
    {
        public async Awaitable RunAsync(CaptureContext capture)
        {
            if (capture == null)
            {
                throw new ArgumentNullException(nameof(capture));
            }

            var game = FindAnyObjectByType<EmberlockGame>();
            if (game == null)
            {
                throw new InvalidOperationException("シーン上に EmberlockGame が見つかりません。");
            }

            // UI Toolkit の UXML 読み込み完了を待つ（PanelRenderer の知見）
            await capture.WaitUntilAsync(() => game.HudRoot != null, 10f);

            var captureDir = Path.Combine(Application.temporaryCachePath, "emberlock_capture_save");
            if (Directory.Exists(captureDir))
            {
                Directory.Delete(captureDir, recursive: true);
            }

            var isolatedStore = new FileSaveStore(Path.Combine(captureDir, "capture_save.txt"));
            game.ReinitializeWithSaveStore(isolatedStore, EvidenceCapture.Seed);

            capture.Record("dataFingerprint", game.Data.Fingerprint.ToString("X8", System.Globalization.CultureInfo.InvariantCulture));
            capture.Record("stepTicks", game.Settings.StepTicks.ToString(System.Globalization.CultureInfo.InvariantCulture));
            capture.Record("eventDurationTicks", game.Settings.EventDurationTicks.ToString(System.Globalization.CultureInfo.InvariantCulture));
            capture.Record("encounterMultiplier", game.Settings.EncounterRateMultiplier.ToString("R", System.Globalization.CultureInfo.InvariantCulture));

            // 01. タイトル画面
            game.Render();
            await capture.ShootAsync("01_title", "Title screen (seed=1, clean save store)");

            // 02. あそびかた画面
            game.Flow.TapButton(InputButton.Down);
            game.Flow.TapButton(InputButton.Confirm);
            game.Render();
            await capture.ShootAsync("02_howto", "How-to-play screen");

            // タイトルへ戻って新規開始
            game.Flow.TapButton(InputButton.Confirm);
            game.Flow.StartNewGameWithSeed(EvidenceCapture.Seed);
            game.Flow.StepTicks(4);
            game.Render();

            // 03. 灯守りの町フィールド探索
            await capture.ShootAsync("03_town_field", $"Town field ({game.Flow.World!.Explorer.MapId}, tick={game.Flow.World.PlayTicks})");

            // 04. よろず屋（購入・装備差分）
            game.Flow.World.AddGold(120);
            game.Flow.World.Explorer.WarpTo("town", 3, 10, Direction.Down);
            game.Flow.TapButton(InputButton.Confirm); // 店を開く
            game.Flow.TapButton(InputButton.Confirm); // かう
            for (var i = 0; i < 3; i++)
            {
                game.Flow.TapButton(InputButton.Down); // 鉄の長剣へカーソル移動
            }

            game.Flow.TapButton(InputButton.Confirm); // 装備対象選択へ
            game.Render();
            await capture.ShootAsync("04_shop_equip", $"Town shop buy & equip preview (tick={game.Flow.World.PlayTicks})");
            game.Flow.TapButton(InputButton.Confirm); // アレンが購入＆装備
            game.Flow.TapButton(InputButton.Cancel);
            game.Flow.TapButton(InputButton.Cancel);

            // 05. 灯の教会でのセーブ
            game.Flow.World.Explorer.WarpTo("town", 14, 5, Direction.Up);
            game.Flow.TapButton(InputButton.Confirm); // 教会を開く
            game.Flow.TapButton(InputButton.Confirm); // いのる（セーブ）
            game.Render();
            await capture.ShootAsync("05_church_save", $"Church save completed (tick={game.Flow.World.PlayTicks})");
            game.Flow.TapButton(InputButton.Cancel);

            // 06. 坑道 B1F で宝箱を開ける
            game.Flow.World.Explorer.WarpTo("dungeon_f1", 14, 3, Direction.Up);
            game.Flow.TapButton(InputButton.Confirm); // 氷紋の杖の宝箱を開ける
            game.Render();
            await capture.ShootAsync("06_dungeon_chest", $"Dungeon B1F treasure chest opened (tick={game.Flow.World.PlayTicks})");
            game.Flow.TapButton(InputButton.Confirm);

            // 07. 通常戦闘（コマンド選択画面）
            for (var i = 0; i < 2; i++)
            {
                game.Flow.World.Party[i].AddExp(i, game.Data.ExpTable.ExpForLevel(2) - 2);
            }

            var mage = game.Flow.World.Party[2];
            mage.AddExp(2, game.Data.ExpTable.ExpForLevel(2));
            mage.FullRestore();

            game.Flow.World.Explorer.WarpTo("dungeon_f1", 5, 5, Direction.Right);
            var walkGuard = 0;
            while (game.Flow.Screen == QuestScreen.Field && walkGuard < 2000)
            {
                walkGuard++;
                if (!game.Flow.World.Explorer.IsMoving)
                {
                    var dirBtn = (game.Flow.World.Explorer.X == 5) ? InputButton.Right : InputButton.Left;
                    game.Flow.TapButton(dirBtn);
                }

                game.Flow.StepTicks(1);
            }

            game.Render();
            await capture.ShootAsync("07_battle_command", $"Dungeon battle command input (tick={game.Flow.World.PlayTicks})");

            // 08. 呪文ダメージ演出（アレン・リナは防御し、セオがフロストを放つ）
            for (var a = 0; a < 2; a++)
            {
                game.Flow.TapButton(InputButton.Down);
                game.Flow.TapButton(InputButton.Down);
                game.Flow.TapButton(InputButton.Down);
                game.Flow.TapButton(InputButton.Confirm); // ぼうぎょ
            }

            game.Flow.TapButton(InputButton.Down);
            game.Flow.TapButton(InputButton.Confirm); // セオ: じゅもん
            game.Flow.TapButton(InputButton.Down);    // フロスト
            game.Flow.TapButton(InputButton.Confirm);
            game.Flow.TapButton(InputButton.Confirm); // 対象決定 -> ターン解決開始

            var animGuard = 0;
            while (game.Flow.Screen == QuestScreen.Battle &&
                   game.Flow.ActiveBattle != null &&
                   game.Flow.ActiveBattle.CurrentEvent?.Kind != BattleEventKind.DamageDealt &&
                   animGuard < 200)
            {
                animGuard++;
                game.Flow.StepTicks(1);
            }

            game.Render();
            await capture.ShootAsync("08_battle_spell_hit", $"Battle spell hit & damage popup (tick={game.Flow.World.PlayTicks})");

            // 09. 戦闘勝利・レベルアップ表示まで進める
            var lvUpCaptured = false;
            var battleGuard = 0;
            while (game.Flow.Screen == QuestScreen.Battle && battleGuard < 3000)
            {
                battleGuard++;
                var bp = game.Flow.ActiveBattle!;
                if (!lvUpCaptured && bp.CurrentEvent?.Kind == BattleEventKind.LevelUp)
                {
                    game.Render();
                    await capture.ShootAsync("09_level_up", $"Party level-up announcement (tick={game.Flow.World.PlayTicks})");
                    lvUpCaptured = true;
                }

                if (bp.Step != BattleInputStep.Animating)
                {
                    game.Flow.TapButton(InputButton.Confirm);
                }

                game.Flow.StepTicks(1);
            }

            // 10. 戦闘開始前に MP 3 にしておき、上部バーの表示とも整合した MP 不足グレーアウト拒否表示を撮影
            mage.SetVitals(mage.MaxHp, 3);
            game.Flow.World.Explorer.WarpTo("dungeon_f2", 5, 5, Direction.Right);
            var mpWalkGuard = 0;
            while (game.Flow.Screen == QuestScreen.Field && mpWalkGuard < 2000)
            {
                mpWalkGuard++;
                if (!game.Flow.World.Explorer.IsMoving)
                {
                    var dirBtn = (game.Flow.World.Explorer.X == 5) ? InputButton.Right : InputButton.Left;
                    game.Flow.TapButton(dirBtn);
                }

                game.Flow.StepTicks(1);
            }

            for (var a = 0; a < 2; a++)
            {
                game.Flow.TapButton(InputButton.Down);
                game.Flow.TapButton(InputButton.Down);
                game.Flow.TapButton(InputButton.Down);
                game.Flow.TapButton(InputButton.Confirm); // ぼうぎょ
            }

            game.Flow.TapButton(InputButton.Down);
            game.Flow.TapButton(InputButton.Confirm); // セオ: じゅもん一覧へ
            game.Flow.TapButton(InputButton.Confirm); // MP 不足のまま押下 -> 「MPが足りない！」表示
            game.Render();
            await capture.ShootAsync("10_battle_mp_shortage", $"Battle spell greyed out & MP shortage message (tick={game.Flow.World.PlayTicks})");

            // 11. 全滅 -> 教会復帰シーン
            game.Flow.World.Explorer.WarpTo("dungeon_f3", 8, 8, Direction.Right);
            for (var i = 0; i < game.Flow.World.Party.Count; i++)
            {
                game.Flow.World.Party[i].SetVitals(1, 0);
            }

            var wipeGuard = 0;
            while (game.Flow.Screen == QuestScreen.Field && wipeGuard < 2000)
            {
                wipeGuard++;
                if (!game.Flow.World.Explorer.IsMoving)
                {
                    var dirBtn = (game.Flow.World.Explorer.X == 8) ? InputButton.Right : InputButton.Left;
                    game.Flow.TapButton(dirBtn);
                }

                game.Flow.StepTicks(1);
            }

            while (game.Flow.Screen == QuestScreen.Battle && wipeGuard < 5000)
            {
                wipeGuard++;
                var bp = game.Flow.ActiveBattle!;
                if (bp.Step == BattleInputStep.RootCommand)
                {
                    if (bp.RootCursor != 3)
                    {
                        game.Flow.TapButton(InputButton.Down);
                    }
                    else
                    {
                        game.Flow.TapButton(InputButton.Confirm);
                    }
                }
                else
                {
                    game.Flow.TapButton(InputButton.Confirm);
                }

                game.Flow.StepTicks(1);
            }

            game.Render();
            await capture.ShootAsync("11_wipeout_church", $"Wipeout respawn at church (tick={game.Flow.World.PlayTicks})");

            // 12. 自動操縦で育成・ボス戦突入〜撃破〜エンディング〜リザルトまで完走
            game.Flow.StartNewGameWithSeed(EvidenceCapture.Seed);
            var bossShotTaken = false;
            var endingShotTaken = false;
            var clearGuard = 0;
            while (game.Flow.Screen != QuestScreen.Result && clearGuard < 120_000)
            {
                clearGuard++;
                if (!bossShotTaken &&
                    game.Flow.Screen == QuestScreen.Battle &&
                    game.Flow.ActiveBattle != null &&
                    game.Flow.ActiveBattle.Battle.IsBossBattle &&
                    game.Flow.ActiveBattle.Step == BattleInputStep.RootCommand)
                {
                    game.Render();
                    await capture.ShootAsync("12_boss_valug", $"Boss battle against Valug (tick={game.Flow.World!.PlayTicks})");
                    bossShotTaken = true;
                }

                if (!endingShotTaken && game.Flow.Screen == QuestScreen.Ending)
                {
                    game.Render();
                    await capture.ShootAsync("13_ending", $"Ending screen (tick={game.Flow.World!.PlayTicks}, hash=0x{game.Flow.World.Hash():X8})");
                    endingShotTaken = true;
                }

                Autopilot.Step(game.Flow);
            }

            if (game.Flow.Screen != QuestScreen.Result)
            {
                throw new InvalidOperationException("撮影台本の自動操縦がリザルト画面に到達しませんでした。");
            }

            // 14. リザルト画面
            game.Render();
            await capture.ShootAsync("14_result", $"Result screen (tick={game.Flow.World!.PlayTicks}, hash=0x{game.Flow.World.Hash():X8})");

            // 15. 破損セーブ検知タイトル画面
            isolatedStore.Write("version=1\ncorrupted_capture_test_without_checksum");
            game.Flow.RefreshTitleState();
            game.Render();
            await capture.ShootAsync("15_title_corrupt_save", "Title screen with corrupt save warning & Continue hidden");

            isolatedStore.Delete();
        }
    }
}
