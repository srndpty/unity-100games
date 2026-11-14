# {{TITLE}}

仕様書：[plan/games/{{GAME_ID}}.md](../../plan/games/{{GAME_ID}}.md)

## 環境

- Unity：{{UNITY_VERSION}}
- 対象プラットフォーム：Windows (StandaloneWindows64)
- 入力方式：（着手時に記入）

## 起動手順

```
dev gui {{NNN}}      # エディタで開く
dev build {{NNN}}    # Windows ビルド
dev run {{NNN}}      # ビルドを起動
dev capture {{NNN}}  # 証跡のスクリーンショットを evidence/ に撮る（ICaptureScenario の実装が必要）
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
