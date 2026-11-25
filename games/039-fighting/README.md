# 039. 2D対戦格闘

仕様書：[plan/games/039-fighting.md](../../plan/games/039-fighting.md)

## 環境

- Unity：6000.6.9f1
- 対象プラットフォーム：Windows (StandaloneWindows64)
- 入力方式：（着手時に記入）

## 起動手順

```
dev gui 039      # エディタで開く
dev build 039    # Windows ビルド
dev run 039      # ビルドを起動
dev capture 039  # 証跡を evidence/ に撮る（画像は captures/<画像世代>/、撮影記録は runs/）（ICaptureScenario の実装が必要）
```

## 操作

（着手時に記入）

## 採用ルール

（着手前に小さなルール表として固定する）

## 設計上の選択

（Core / App / Unity の境界、物理エンジンに委譲した範囲など）

## テスト

（種類・場所・実行方法・件数の表）

## 未実装範囲

（仕様書の Out of scope と、削った機能）

証跡画像はJPEG（`.jpg`）、品質85を標準とし、保存したJPEGのSHA-256をマニフェストに記録する。
