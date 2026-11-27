# 052 峡谷の関門の動作証跡

Unity 6000.6.9f1のWindowsビルドで撮影。固定台本で10Waveを防衛し、初期状態に戻して塔なしの敗北まで進めた。掲載するJPEG 10枚のSHA-256を撮影記録と照合済み。勝利と敗北の画像は内容とSHA-256がそれぞれ異なることを確認した。

[撮影記録](runs/20261127T184519Z-ed29ebd1/manifest.json)

- [タイトル (tick=0)](captures/b0e4eff151a29157/01_title.jpg)
- [操作説明 (tick=0)](captures/b0e4eff151a29157/02_help.jpg)
- [準備と射程 (tick=0)](captures/b0e4eff151a29157/03_preparation.jpg)
- [一時停止と再開 (tick=0)](captures/b0e4eff151a29157/08_pause.jpg)
- [第1Waveの攻撃 (tick=33)](captures/b0e4eff151a29157/04_attack.jpg)
- [矢の着弾と被弾明滅 (tick=37)](captures/b0e4eff151a29157/10_hit.jpg)
- [第1Wave終了後の準備 (tick=246)](captures/b0e4eff151a29157/09_between_waves.jpg)
- [第10Wave (tick=4161)](captures/b0e4eff151a29157/05_final_wave.jpg)
- [10Waveを防衛 (tick=4794)](captures/b0e4eff151a29157/06_won.jpg)
- [ライフを失い敗北 (tick=803)](captures/b0e4eff151a29157/07_lost.jpg)

## 検証結果

| 検証 | 結果 |
|---|---|
| dev lint 052 / dev check 052 | 成功 |
| dev test 052 | Core 22/22、App 6/6 |
| Unity EditMode / PlayMode | 28/28、18/18 |
| 仮想マウス | 建設、強化、売却、Wave開始、一時停止中の入力遮断、再開、敗北画面からの再戦を確認 |
| Windowsビルドと撮影 | 成功。1280×720と1152×720で盤面と操作欄の収まりを確認 |

Windowsビルドのログにライセンスクライアントの検証警告が出た。ビルドと撮影は正常終了した。難易度と進行テンポは試遊で確認する。
