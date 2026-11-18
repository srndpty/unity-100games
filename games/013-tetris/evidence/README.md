# 013 検証記録

2026-11-19実施。人間の動作確認は未実施（検証待ち）。

## 自動検証

| 項目 | 結果 |
|---|---|
| dev lint | 成功（全作品） |
| dev check | 成功（全作品、警告0・エラー0） |
| dev test | 成功（全作品）。013 Core 80/80、App 48/48 |
| Unity EditMode | 128/128（Core/App、Mono） |
| Unity PlayMode | 35/35（実シーン19、共通証跡公開16） |
| Windowsビルド | 成功、StandaloneWindows64 |
| ビルド起動 | dev captureで起動・全場面撮影・正常終了 |
| 証跡 | JPEG品質85、9枚 |
| 撮影再現 | 同一ビルドを2回撮影し、画像世代e8f32410a1a454e5が一致 |

Build GUID：`97c217a7300142589b06c3a25491b792`。シード：1。公開ポインタは[current.json](current.json)、実行記録は[manifest.json](runs/20261118T200328Z-f946af33/manifest.json)。

同じシードと入力列の最終盤面は、固定のハッシュ値と照合するテストが dotnet（.NET）と Unity EditMode（Mono）の両方で通っている。

終了ボタンは、押すと終了処理を1回呼ぶことをPlayModeテストで確認している。エディタでは `Application.Quit` が働かないため、ビルドで実際に終了する動作は自動テストの対象外。

Unityのビルドログにライセンスクライアントの検証メッセージが出たが、ビルドとPlayerの撮影起動は成功した。

## 画面証跡

### タイトル

![タイトル](captures/e8f32410a1a454e5/01-title.jpg)

### プレイ開始

![プレイ開始](captures/e8f32410a1a454e5/02-start.jpg)

### プレイ中

薄く表示しているのがハードドロップの着地点。

![プレイ中](captures/e8f32410a1a454e5/03-playing.jpg)

### ホールド

![ホールド](captures/e8f32410a1a454e5/04-hold.jpg)

### ライン消去の演出

![ライン消去の演出](captures/e8f32410a1a454e5/05-clearing.jpg)

### ライン消去後

消した段の上のブロックが下へ詰まった場面。

![ライン消去後](captures/e8f32410a1a454e5/06-cleared.jpg)

### 一時停止

![一時停止](captures/e8f32410a1a454e5/07-paused.jpg)

### 結果

![結果](captures/e8f32410a1a454e5/08-result.jpg)

### 再プレイ

![再プレイ](captures/e8f32410a1a454e5/09-replay.jpg)

## 人間の確認

未実施。`dev run 013` で空盤面から敗北まで遊び、速度・操作感・音量を確認する。
