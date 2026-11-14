# Unity ゲーム制作100本ノック

100本の小さなゲームを Unity で1本ずつ作り、ジャンルごとの実装課題を学ぶ。仕様は [plan/](plan/README.md)、進捗は [plan/progress.csv](plan/progress.csv)。

## 必要なもの

- Unity 6.6（Unity Hub と Unity CLI）
- .NET SDK 9
- PowerShell 7
- pre-commit

## セットアップ

```
pre-commit install
dev help
```

## 使い方

```
dev new 002          # 仕様書 plan/games/002-*.md に対応するプロジェクトを作成
dev gui 002          # Unity エディタで開く
dev test             # 全作品の Core テスト (Unity 不要)
dev test 002 -Unity  # Unity 上でテスト
dev lint             # 整形・静的解析の検証
dev check            # 型チェック
dev build 002        # Windows ビルド
dev run 002          # ビルドを起動
```

構成と開発ルールは [AGENTS.md](AGENTS.md) を参照。

## 作品一覧

| No. | 題材 | 状態 |
|---:|---|---|
| 001 | [Pong](games/001-pong/README.md) | 完了 |
