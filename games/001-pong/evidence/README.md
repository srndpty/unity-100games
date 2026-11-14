# 動作証跡：001. Pong

記録日：2026-11-14 / Unity 6000.6.9f1 / Windows 11

## スクリーンショット

Windows ビルド（1280×720 ウィンドウ）を起動し、キー入力を送って撮影した。

| 画面 | 内容 |
|---|---|
| ![タイトル](title.png) | タイトル。操作説明と勝利条件 |
| ![試合中](playing.png) | Enter で開始し、W でパドルを上げた直後。CPU が 1 点先取 |

結果画面は撮影していない。代わりに PlayMode テスト `MatchEnd_ShowsWinnerOnResultOverlay_ThenEnterRematches` で、決着すると結果オーバーレイに勝者が表示され、Enter で再戦できることを確認した。

## テスト結果

| 種類 | 実行方法 | 結果 |
|---|---|---|
| Core | `dev test`（dotnet） | 30 / 30 成功 |
| Core + App（EditMode） | `dev test 001 -Unity` | 43 / 43 成功 |
| シーン・入力（PlayMode） | `dev test 001 -Unity -Mode PlayMode` | 6 / 6 成功 |
| lint / 型チェック | `dev lint`、`dev check` | 成功 |

## ビルド結果

- `dev build 001` 成功（約 20 秒、出力 約 100 MB）
- ビルドログに `[Licensing::Module] LicensingClient has failed validation; ignoring` が出るが、ビルドと起動には影響しない
- Player.log に、URP の未使用ポストプロセス用シェーダ（Bloom、被写界深度など）がビルドから除外された旨の警告が起動時に出る。ポストプロセスは使っていないため影響なし
