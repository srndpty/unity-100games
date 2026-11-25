# 041 検証記録

2026-11-26 実施。自動検証を一通り通し、状態を「検証待ち」として記録した。

## 自動検証

| 項目 | 結果 |
|---|---|
| dev lint | 成功 |
| dev check | 成功（警告 0） |
| dev test | 成功。Core 17/17、App 5/5 |
| Unity EditMode | 84/84（IntGeometry 52、TileMotion 10、Core 17、App 5。Mono） |
| Unity PlayMode | 24/24（実シーン E2E 8、共通の証跡公開 16） |
| Windows ビルド | 成功、StandaloneWindows64 |
| ビルド起動 | dev capture で起動・全 15 場面撮影・正常終了 |
| 証跡 | JPEG 品質 85、15 枚 |
| 撮影再現 | 同一ビルド `eee1dac3e0904249ba91c3afbf2ba60b` の連続 2 回で 15 枚すべての SHA-256 が完全一致。画像世代 `fe8d9dd1e5c658e2`。撮影 ID `20261125T194145Z-a22b43e3` と `20261125T194243Z-255edcaa`（[最新の記録](current.json)） |
| 固定ハッシュ | 操縦役のシード 1 完走（2,436 Tick・218 歩・14 戦、データ指紋 `0xAF9AE773`）の最終状態ハッシュ `0x5CA021E4` が .NET・Mono・実機撮影の 3 環境で一致 |
| 性能 | フィールド定常進行 180 Tick：Core の Step p95 0.000 ms、描画の更新 p95 0.017 ms。描画フレームの GC 割り当て増分は 24 フレーム中 0 フレーム（4096 バイトを割り当てた確認フレームは 5368 バイトとして検出）。Unity エディタのバッチ実行、Mono、`ProfilerRecorder` の「GC Allocated In Frame」 |

撮影の台本（`EmberlockCaptureScenario`）は隔離された一時セーブ領域を使い、シード 1 と Tick 進行だけで全 15 場面を決定的に撮影する。

## 画面

| 01. タイトル | 02. あそびかた | 03. 灯守りの町（フィールド） |
|---|---|---|
| ![タイトル](captures/fe8d9dd1e5c658e2/01_title.jpg) | ![あそびかた](captures/fe8d9dd1e5c658e2/02_howto.jpg) | ![灯守りの町](captures/fe8d9dd1e5c658e2/03_town_field.jpg) |

| 04. よろず屋（購入・即時装備差分） | 05. 灯の教会（セーブ完了） | 06. 坑道 B1F（宝箱取得） |
|---|---|---|
| ![よろず屋](captures/fe8d9dd1e5c658e2/04_shop_equip.jpg) | ![教会セーブ](captures/fe8d9dd1e5c658e2/05_church_save.jpg) | ![宝箱取得](captures/fe8d9dd1e5c658e2/06_dungeon_chest.jpg) |

| 07. 通常戦闘（コマンド選択） | 08. 呪文ダメージ演出 | 09. レベルアップ表示 |
|---|---|---|
| ![戦闘コマンド](captures/fe8d9dd1e5c658e2/07_battle_command.jpg) | ![呪文ダメージ](captures/fe8d9dd1e5c658e2/08_battle_spell_hit.jpg) | ![レベルアップ](captures/fe8d9dd1e5c658e2/09_level_up.jpg) |

| 10. MP 不足グレーアウトと拒否表示 | 11. 全滅後の教会復帰（所持金半減） | 12. 焔鎖の魔将ヴァルグ戦 |
|---|---|---|
| ![MP不足](captures/fe8d9dd1e5c658e2/10_battle_mp_shortage.jpg) | ![全滅教会復帰](captures/fe8d9dd1e5c658e2/11_wipeout_church.jpg) | ![ボス戦](captures/fe8d9dd1e5c658e2/12_boss_valug.jpg) |

| 13. エンディング | 14. 冒険の記録（リザルト） | 15. 破損セーブ検知タイトル |
|---|---|---|
| ![エンディング](captures/fe8d9dd1e5c658e2/13_ending.jpg) | ![リザルト](captures/fe8d9dd1e5c658e2/14_result.jpg) | ![破損セーブ検知](captures/fe8d9dd1e5c658e2/15_title_corrupt_save.jpg) |
