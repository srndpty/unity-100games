# 049 閉館後の展示室の動作証跡

Windows x64、Unity 6000.6.9f1。撮影モードで起動し、初期設定の残り3回で敗北と勝利の両方を再生した。1280×720、JPEG品質85。全9枚のSHA-256をマニフェストと照合した。

[撮影記録](runs/20261127T130510Z-1d78102a/manifest.json)

- [タイトル (tick=1)](captures/728656a91faa5bad/01_title.jpg)
- [遊び方 (tick=2)](captures/728656a91faa5bad/00_help.jpg)
- [警備員に尋問 (tick=4)](captures/728656a91faa5bad/02_question.jpg)
- [展示目録を誤提示 (tick=5)](captures/728656a91faa5bad/03_incorrect.jpg)
- [敗北 (tick=7)](captures/728656a91faa5bad/04_lost.jpg)
- [入退室ログで証言を覆す (tick=11)](captures/728656a91faa5bad/05_correct.jpg)
- [封印の検印と複製品の刻印 (tick=18)](captures/728656a91faa5bad/06_seal.jpg)
- [取得した8点の証拠 (tick=22)](captures/728656a91faa5bad/08_records.jpg)
- [真相 (tick=23)](captures/728656a91faa5bad/07_won.jpg)


## 検証結果

| 検証 | 結果 |
|---|---|
| dev lint 049 / dev check 049 | 成功 |
| dev test 049 | Core 10/10、App 3/3 |
| dev test 049 -Unity | EditMode 13/13 |
| dev test 049 -Unity -Mode PlayMode | PlayMode 19/19。仮想マウスの勝敗・再戦と、証言・証拠を切り替えても操作ボタンの位置・大きさが変わらないことを確認 |
| dev build 049 / dev capture 049 | Windowsビルド、起動、撮影成功 |
| 画面確認 | 1280×720（16:9）と1152×720（16:10）。操作説明、封印図、8件を取得した資料一覧、提示ボタンが画面内に収まる |

Windows版の撮影ログにはURPのBloomシェーダーとD3D12の終了時リソースに関する警告が出た。撮影プロセスは正常終了した。実機のマウス操作感、難易度、音量は試遊で確認する。
