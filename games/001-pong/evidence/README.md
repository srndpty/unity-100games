# 動作証跡：001. Pong

記録日：2026-11-15 / Unity 6000.6.9f1 / Windows 11

## スクリーンショット

`dev capture 001` で撮影した。Windows ビルドを 1280×720 のウィンドウで起動し、撮影台本 [PongCaptureScenario](../Assets/Game/Unity/PongCaptureScenario.cs) が画面を進めて撮る。

- 固定シード（1）で試合を Tick 単位で進め、補間せずにその Tick の状態を描く。同じビルドなら毎回同じ画像になる（2 回撮って SHA-256 が一致することを確認済み）。
- プレイヤーのパドルは動かさないので CPU が勝つ。
- 生成物は [capture/](capture/) にまとめて置く。撮影ごとに検証してからフォルダごと入れ替えるので、新旧の画像が混ざらない。シード、設定値、ビルド GUID、撮影 Tick、各画像の SHA-256 は [capture/manifest.json](capture/manifest.json) に記録している。

| 画面 | 内容 |
|---|---|
| ![タイトル](capture/title.png) | タイトル。操作説明と勝利条件 |
| ![試合中](capture/playing.png) | 試合開始から 100 Tick。サーブされたボールが左へ飛んでいる |
| ![結果](capture/result.png) | 決着（1268 Tick、1-5）後の結果。勝者・スコア・再戦の案内 |

4:3（960×720）のウィンドウでも撮影し、コートとパドルが画面内に収まることを目で確認した（画像は保存していない）。

結果オーバーレイの表示と Enter での再戦は、PlayMode テスト `MatchEnd_ShowsWinnerOnResultOverlay_ThenEnterRematches` でも確認している。

## 通しプレイ

| 日付 | 対象 | 結果 |
|---|---|---|
| 2026-11-15 | 初回実装 | 決着まで遊べた。CPU が強すぎる・音量が大きすぎるという指摘を受け、CPU の速度と狙いの精度、効果音の音量を下げた |
| 2026-11-15 | 1 回目のコードレビュー対応後（画面比率、動くパドルの衝突判定、再戦のシード） | 決着まで遊べた |

2 回目のコードレビュー対応（証跡の公開、接触回数の保証、撮影 Tick の描画、コート端でのパドル位置）は、自動テストと撮影で確認した。遊んだときの挙動に関わる変更は、コート端に着く Tick のパドル位置の求め方だけ。

## テスト結果

| 種類 | 実行方法 | 結果 |
|---|---|---|
| Core / App | `dev test`（dotnet） | 39 / 39、14 / 14 成功 |
| Core + App（EditMode） | `dev test 001 -Unity` | 53 / 53 成功 |
| シーン・入力・カメラ・証跡（PlayMode） | `dev test 001 -Unity -Mode PlayMode` | 16 / 16 成功 |
| lint / 型チェック | `dev lint`、`dev check` | 成功 |

証跡の公開が失敗したときの動作も、実際のビルドで確かめた。公開中の画像を別のプロセスで開いたまま `dev capture 001` を実行すると、5 秒で終了コード 1 になり、公開中の一式は変わらず、一時フォルダも残らなかった。

## ビルド結果

- `dev build 001` 成功（約 20 秒、出力 約 100 MB）
- ビルドログに `[Licensing::Module] LicensingClient has failed validation; ignoring` が出るが、ビルドと起動には影響しない
- Player.log に、URP の未使用ポストプロセス用シェーダ（Bloom、被写界深度など）がビルドから除外された旨の警告が起動時に出る。ポストプロセスは使っていないため影響なし
