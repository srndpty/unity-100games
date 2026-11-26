# 045 検証記録

2026-11-27 実施。

## 自動検証

| 項目 | 結果 |
|---|---|
| dev lint | 成功 |
| dev check | 成功（警告 0） |
| dev test | 成功。Core 121/121、App 13/13 |
| Unity EditMode | 203/203（パッケージ 69・Core 121・App 13。Mono） |
| Unity PlayMode | 26/26（実シーン 9、共通の証跡公開 17） |
| Windows ビルド | 成功、StandaloneWindows64（ビルドログにライセンスクライアントの署名検証の警告が残るが、結果は成功・終了コード 0） |
| ビルド起動 | dev capture で起動・全場面撮影・正常終了 |
| 証跡 | JPEG 品質 85、21 枚、1280×720 |
| 撮影再現 | 同一ビルド `1b66811cbbf34c359453dd3455a97f24` の 2 回の撮影（撮影 ID `20261126T165518Z-86a07d27` と `20261126T165536Z-eff77831`）で 21 枚の SHA-256 が一致。画像世代 `405afeda2fc3a5bb`（[最新の記録](current.json)）。1 回目の記録は次の公開で消える |
| 固定ハッシュ | シード 1 のトレーナー戦の最終ハッシュ `399851652`、操縦役の通し（シード 1、3 人目に勝って結果画面）の世界のハッシュ `1241441768` が .NET と Mono で一致 |
| 題材固有テスト | 相性の 3×3 の全組と一致補正・相性・乱数の掛ける順、命中は `Next(100) < 命中` の境界（命中 100 でも乱数を 1 回引く、外れたら状態異常も起きない）／手持ち 6 で捕まえるとボックス、ボックスも満杯なら捕獲玉を投げられず玉も減らない、50 シード × 400 手の乱択の出し入れで全局面が上限内／眠り（持続 2・3 の両方）と毒（4 回）が解除予定のターンの開始で治り、控えでも予定どおり治る |
| 通し | 操縦役（キー入力だけを Flow に送る）は 20 シード全てでクリア（戦闘 66〜375 回、全滅 0〜2 回、6 種すべて捕獲）。実シーンでも操縦役で結果画面まで進め、タイトルへ戻った姿が起動直後と一致することを確かめた |
| 性能 | 実シーンで描画の更新（Stage と HUD）300 回の p95 0.038 ms、戦闘の 1 ターンの解決（Core の `Battle.Submit`）200 回の p95 0.009 ms。手持ち 6 匹。Unity エディタのバッチ実行、Mono、Stopwatch（この PC の CPU 時間）。GC の割り当ては計っていない（HUD の文字列を毎フレーム作る） |

撮影の台本はシード 1 の Flow を、操縦役（回復所に寄ったら記録する）でクリアまで遊び、各場面に最初に着いた Tick を撮る。戦闘の出来事は、当ててから 4 Tick 目（文を送れるようになる前）に撮る。てもち・ずかんは仲間が 3 匹になった最初のフィールドで、ボックスは 3 匹以上で最初に回復所のメニューを開いたときに開いて撮る。最後に壊れたセーブを書いてタイトルを読み直す。

## 画面

| タイトル | あそびかた | 最初の 1 匹 |
|---|---|---|
| ![タイトル](captures/405afeda2fc3a5bb/01_title.jpg) | ![あそびかた](captures/405afeda2fc3a5bb/02_howto.jpg) | ![最初の 1 匹](captures/405afeda2fc3a5bb/03_starter.jpg) |

| 灯ノ村 | 野生が現れた | 戦闘のコマンド |
|---|---|---|
| ![灯ノ村](captures/405afeda2fc3a5bb/04_town.jpg) | ![野生が現れた](captures/405afeda2fc3a5bb/05_wild_appears.jpg) | ![戦闘のコマンド](captures/405afeda2fc3a5bb/06_command.jpg) |

| 技を選ぶ | 効果は ばつぐん | 状態異常（毒） |
|---|---|---|
| ![技を選ぶ](captures/405afeda2fc3a5bb/07_moves.jpg) | ![効果は ばつぐん](captures/405afeda2fc3a5bb/08_super_effective.jpg) | ![状態異常（毒）](captures/405afeda2fc3a5bb/09_status.jpg) |

| てもち | ずかん | 捕まえた |
|---|---|---|
| ![てもち](captures/405afeda2fc3a5bb/10_party.jpg) | ![ずかん](captures/405afeda2fc3a5bb/11_dex.jpg) | ![捕まえた](captures/405afeda2fc3a5bb/12_capture.jpg) |

| 回復所のボックス | レベルアップ | 技の入れ替え |
|---|---|---|
| ![回復所のボックス](captures/405afeda2fc3a5bb/13_storage.jpg) | ![レベルアップ](captures/405afeda2fc3a5bb/14_level_up.jpg) | ![技の入れ替え](captures/405afeda2fc3a5bb/15_learn_move.jpg) |

| 回復所で記録 | トレーナーが道を塞ぐ | トレーナー戦 |
|---|---|---|
| ![回復所で記録](captures/405afeda2fc3a5bb/16_center_save.jpg) | ![トレーナーが道を塞ぐ](captures/405afeda2fc3a5bb/17_trainer_intro.jpg) | ![トレーナー戦](captures/405afeda2fc3a5bb/18_trainer_battle.jpg) |

| 結果 | 記録を出したタイトル | 壊れたセーブの案内 |
|---|---|---|
| ![結果](captures/405afeda2fc3a5bb/19_result.jpg) | ![記録を出したタイトル](captures/405afeda2fc3a5bb/20_title_records.jpg) | ![壊れたセーブの案内](captures/405afeda2fc3a5bb/21_title_broken_save.jpg) |
