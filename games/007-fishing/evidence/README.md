# 007 検証・動作証跡

2026-11-18実施。自動検証済み、人間のプレイ確認待ち。

## 検証結果

| 項目 | 結果 |
|---|---|
| `dev lint` | 成功。全作品の.metaと空白、証跡リンクを検査 |
| `dev check` | 全作品成功、警告0・エラー0 |
| `dev test` | 全作品成功。007はCore 26/26、App 10/10 |
| `dev test 007 -Unity` | EditMode 36/36成功 |
| `dev test 007 -Unity -Mode PlayMode` | 17/17成功（実シーン9、証跡公開8） |
| `dev build 007` | Windows / StandaloneWindows64成功 |
| Windows起動 | `dev capture 007`で実ビルドを起動。タイトル→合わせ→ファイト→3種捕獲・総合結果→新しいプレイ→糸切れを確認 |
| `dev capture 007` | 8枚公開、終了コード0。同一ビルドを2回撮影して全画像SHA-256一致 |

Coreは張力の閾値・継続・解除、緩めたときの低下、捕獲と逃走の競合、アタリの受付期限、魚AIの遷移、設定の不正値、秒からTickへの隣接float境界、終了後の更新拒否とシード再現を確認した。

Appは3種×固定シード1〜20を各60秒以内に捕獲。巻き続け・緩め続けの失敗はCoreで確認した。3種揃える総合結果、同種の重複捕獲、失敗後の再投、新シード、釣果リセット、長押し持ち越し、停止とフォーカス喪失の両順序、小さい時間分割・巨大delta・端数リセットを確認した。

PlayModeは実シーンへTestKeyboardで入力し、UIクリック、合わせ・停止・再投、固定シードでの3種捕獲、危険警告・スタミナ0の表示、16:10〜21:9のカメラ範囲、再読込時のSprite・Texture解放と描画物数の不変性を確認した。撮影画像で日本語・配置・ゲージ・捕獲／失敗表示も目視した。

## 公開画像

公開中の組は[current.json](current.json)が指す。実行記録は[runs/20261117T163452Z-ec187784/manifest.json](runs/20261117T163452Z-ec187784/manifest.json)。シード1、Unity 6000.6.9f1、1280×720。日時はmanifestではUTC、検証日は日本時間。

| 場面 | 画像 |
|---|---|
| タイトル・操作説明 | [01-title.png](captures/ef50ed2e2b26b457/01-title.png) |
| ポイント選択 | [02-points.png](captures/ef50ed2e2b26b457/02-points.png) |
| アタリ | [03-bite.png](captures/ef50ed2e2b26b457/03-bite.png) |
| ファイト・張力・スタミナ | [04-fight.png](captures/ef50ed2e2b26b457/04-fight.png) |
| コブナ捕獲 | [05-catch-0.png](captures/ef50ed2e2b26b457/05-catch-0.png) |
| スズキ捕獲 | [05-catch-1.png](captures/ef50ed2e2b26b457/05-catch-1.png) |
| コイ捕獲・3種達成 | [05-catch-2.png](captures/ef50ed2e2b26b457/05-catch-2.png) |
| 糸切れの結果 | [08-broken-line.png](captures/ef50ed2e2b26b457/08-broken-line.png) |

## 確認待ち・既知事項

- `dev run 007`で人間のプレイ確認待ち。アタリの分かりやすさ、巻く／緩める判断、3種の差、難易度、音量、再投の操作感を確認する。
- 日本語は対象OSのYu Gothicを使う。Windows以外は配布対象外。
- ビルド時にUnityのライセンスクライアント署名検証のログが出るが、ビルドは成功し、起動・撮影も終了コード0。
- 撮影終了ログにUnityのComputeBuffer／D3D12解放に関するメッセージが出る。ゲーム側例外はなく、再読込時の自作Sprite・Textureの解放テストは成功している。
