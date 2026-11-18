# 007 検証・動作証跡

2026-11-18実施。自動検証済み、人間のプレイ確認待ち。

## 検証結果

| 項目 | 結果 |
|---|---|
| `dev lint` | 成功。全作品の.metaと空白、証跡リンクを検査 |
| `dev check` | 全作品成功、警告0・エラー0 |
| `dev test` | 全作品成功。007はCore 26/26、App 10/10 |
| `dev test 007 -Unity` | EditMode 36/36成功 |
| PlayMode（Unity CLI・GUIエディター） | 30/30成功（実シーン14、証跡公開・移行16）。開いているエディターで `unity command test run --mode playmode --async-tests true --filter Game.Unity.Tests --filter-type Assembly` を実行 |
| Windowsビルド（Unity CLI・GUIエディター） | Windows / StandaloneWindows64成功、警告0・エラー0。`unity command build run --target StandaloneWindows64 --output-path Builds/Windows/007-fishing.exe --confirm true` |
| Windows起動 | `dev capture 007`で実ビルドを起動。タイトル→合わせ→ファイト→3種捕獲・総合結果→新しいプレイ→糸切れを確認 |
| `dev capture 007` | 11枚公開、終了コード0。同一ビルドを2回撮影して全画像SHA-256一致 |

Coreは張力の閾値・継続・解除、緩めたときの低下、捕獲と逃走の競合、アタリの受付期限、魚AIの遷移、設定の不正値、秒からTickへの隣接float境界、終了後の更新拒否とシード再現を確認した。

Appは3種×固定シード1〜20を各60秒以内に捕獲。巻き続け・緩め続けの失敗はCoreで確認した。3種揃える総合結果、同種の重複捕獲、失敗後の再投、新シード、釣果リセット、長押し持ち越し、停止とフォーカス喪失の両順序、小さい時間分割・巨大delta・端数リセットを確認した。

PlayModeは実シーンへTestKeyboardで入力し、UIクリック、合わせ・停止・再投、固定シードでの3種捕獲、危険警告・スタミナ0の表示、16:10〜21:9のカメラ範囲、再読込時のインポート済みSprite参照と描画物数の不変性を確認した。撮影画像で日本語・配置・ゲージ・捕獲／失敗表示も目視した。追加で、クリック即キャスト、初回の合わせから長押し巻き取り、遊泳と水面の餌への食いつき、保存済みPrefab内の描画物、一本の糸と固定ウキの一定のウキ下、踏ん張り姿勢への切り替え、人物・竿の共通Rigの動きと停止時の不変性を検証した。

## 公開画像

公開中の組は[current.json](current.json)が指す。実行記録は[runs/20261118T001322Z-c2ef882d/manifest.json](runs/20261118T001322Z-c2ef882d/manifest.json)。シード1、Unity 6000.6.9f1、1200×720。日時はmanifestではUTC、検証日は日本時間。

| 場面 | 画像 |
|---|---|
| タイトル・操作説明 | [01-title.jpg](captures/36e4947f990972a1/01-title.jpg) |
| ポイント選択 | [02-points.jpg](captures/36e4947f990972a1/02-points.jpg) |
| 待機中の遊泳 | [02a-roaming.jpg](captures/36e4947f990972a1/02a-roaming.jpg) |
| 水面の餌へ接近 | [02b-approach.jpg](captures/36e4947f990972a1/02b-approach.jpg) |
| アタリ | [03-bite.jpg](captures/36e4947f990972a1/03-bite.jpg) |
| ヒット直後の踏ん張り姿勢・直線の糸 | [03a-hooked.jpg](captures/36e4947f990972a1/03a-hooked.jpg) |
| ファイト・張力・スタミナ | [04-fight.jpg](captures/36e4947f990972a1/04-fight.jpg) |
| コブナ捕獲 | [05-catch-0.jpg](captures/36e4947f990972a1/05-catch-0.jpg) |
| スズキ捕獲 | [05-catch-1.jpg](captures/36e4947f990972a1/05-catch-1.jpg) |
| コイ捕獲・3種達成 | [05-catch-2.jpg](captures/36e4947f990972a1/05-catch-2.jpg) |
| 糸切れの結果 | [08-broken-line.jpg](captures/36e4947f990972a1/08-broken-line.jpg) |

## 確認待ち・既知事項

- `dev run 007`で人間のプレイ確認待ち。アタリの分かりやすさ、巻く／緩める判断、3種の差、難易度、音量、再投の操作感を確認する。
- 日本語は対象OSのYu Gothicを使う。Windows以外は配布対象外。
- バッチ検証時にUnityのILPPワーカー待ちが発生したため、最終PlayModeとビルドは起動済みGUIエディターにUnity CLIで接続して実施した。最終ビルドは警告0・エラー0、撮影も終了コード0。
- 撮影終了ログにUnityのComputeBuffer／D3D12解放に関するメッセージが出る。ゲーム側例外はなく、再読込時の自作Sprite・Textureの解放テストは成功している。

固定ウキの追加確認：ウキ下は待機中と同じ描画上0.2を維持する。ファイト中の2時点で口元からの距離と糸上の配置を検証し、ウキが水中へ引き込まれることを撮影画像でも確認した。

## 2026-11-18 画像形式の標準化

JPEG品質85・元の解像度を標準とする。JPEG標準を反映したWindowsビルドで再撮影し、同じビルドを2回撮影して全画像のSHA-256一致を確認した。当初のJPEG移行では画像合計が16.32MBから1.74MBになった。

JPEG保存の追加テストでは、拡張子・JPEG署名・復号後の解像度と色・圧縮後のSHA-256・公開後の再検証を確認した。

共通撮影形式変更後のUnity再検証：EditMode 36/36、PlayMode 23/23成功（JPEG保存・復号・公開の追加テストを含む）。

## レビュー対応：証跡移行の途中失敗

READMEの更新内容を公開前に準備し、stagingの画像・manifestへのリンクを検証する。新画像と記録を配置し、READMEを原子的に置換してからcurrent.jsonを原子的に置換する。両方を検証後、参照されない旧世代を削除する。READMEとポインタは別操作のため途中では異なる世代を指し得るが、どちらの参照先も保持する。失敗時はstagingと一時ファイルをfinallyで片付け、再実行で参照を揃える。

追加7ケースで、JPEG生成途中、画像公開、README置換、ポインタ置換の失敗、全公開操作直前の失敗注入、再実行、失敗後の通常撮影でもREADME参照が保護されることを確認した。いずれもVerifyCurrentとREADME全リンクの存在を検証する。既存001〜007の公開済みJPEGも移行コマンドで再検証し、再圧縮されないことを確認した。

復旧用のAssets/_RecoveryはGit管理から除外し、ローカルのバックアップは保持した。キャプチャはJPEG品質85を継続し、単色主体ではPNGより増える場合があることを共通ルールに明記した。制作素材の形式は別に選ぶ。

レビュー対応後の最終Windowsビルドは警告0・エラー0。2回の撮影で画像世代36e4947f990972a1が一致し、11枚すべて同じSHA-256で公開された。
