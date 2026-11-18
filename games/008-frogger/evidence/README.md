# 008 検証記録

2026-11-18実施。2026-11-18に人間の完了承認を受領。

## 自動検証

| 項目 | 結果 |
|---|---|
| dev lint | 成功（全作品） |
| dev check | 成功（全作品、警告0・エラー0） |
| dev test | 成功（全作品）。008 Core 19/19、App 9/9 |
| Unity EditMode | 28/28（Core/App、Mono） |
| Unity PlayMode | 33/33（実シーン17、共通証跡公開16） |
| Windowsビルド | 成功、StandaloneWindows64 |
| ビルド起動 | dev captureで起動・全場面撮影・正常終了 |
| 証跡 | 1280×720、JPEG品質85、8枚。全画像を目視確認 |
| 撮影再現 | 同一ビルドを2回撮影し、画像世代98e53d9a33792f95が一致 |

Build GUID：`92591d1e6cfe41a3bbeccb07e9ed5680`。公開ポインタは[current.json](current.json)、実行記録は[manifest.json](runs/20261118T035548Z-b8b6bce4/manifest.json)。SHA-256は保存したJPEGに対して照合した。乱数は使用しない（共通撮影基盤のSeed欄は1）。

Unityのビルドログにライセンスクライアントの署名検証警告が出たが、エディタは継続し、ビルドとPlayerの撮影起動は成功した。ゲーム側のコンパイルエラーはない。

## 最大設定の負荷

Unity 6000.6.9f1、Windows、Intel Core i9-13900KF、バッチPlayModeの640×480。全レーンの速度絶対値12、幅3.5、カエル半幅0.4。100回ウォームアップ後、Core更新＋View描画反映を1000回測定。

- CPU時間：p50 0.0137ms、p95 0.0147ms。
- 移動物体24個、SpriteRenderer 26個。更新中に描画物を追加しない。
- Unityエディタ内のCPU処理時間であり、GPU時間・PlayerのFPS・他PCでの性能を保証する値ではない。

## 画面証跡

### タイトル

![タイトル](captures/98e53d9a33792f95/01-title.jpg)

### 道路の横断

![道路](captures/98e53d9a33792f95/02-road.jpg)

### 丸太に乗る

![足場](captures/98e53d9a33792f95/03-log.jpg)

### クリア

![クリア](captures/98e53d9a33792f95/04-clear.jpg)

### 車との接触

![車接触](captures/98e53d9a33792f95/05-car-hit.jpg)

### 川への落下

![落水](captures/98e53d9a33792f95/06-water.jpg)

### 足場による場外移動

![場外](captures/98e53d9a33792f95/07-carried-out.jpg)

### 再挑戦

![再挑戦](captures/98e53d9a33792f95/08-replay.jpg)

## 人間の確認

2026-11-18に完了承認を受領。画像容量の調整後もlint・check・全作品Core/Appテスト・Unity全61件・Windowsビルド・証跡8枚・同一ビルド再撮影を確認済み。

## 制作画像の容量調整

BoardはJPEG品質95（835142バイト）、CarはRGBA画素が完全一致する可逆PNG再圧縮（2034079バイト）で、2000KB制限に対応。GUIDを維持してUnity参照を再確認した。
