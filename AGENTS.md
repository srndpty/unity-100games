# AGENTS.md

Unity ゲーム制作100本ノックのリポジトリ。1題材＝1 Unity プロジェクトを `games/NNN-name/` に置くモノレポ。

## 基本方針

- 人間が読む想定の文書は日本語で書く。
- DRY や責務の分離などを意識する。
- ただし、個人向けプロジェクトかつ開発中のため、フォールバックや過剰な互換性残しは不要（禁物）。
- コミット・リリース・完成の判断は人間が行う。テスト通過だけで完成扱いしない。
- 仕様書の Out of scope を勝手に増やさない。コンテンツ量より、ルールの正しさと検証可能性を優先する。

## 構成

| パス | 内容 |
|---|---|
| `plan/` | 100題材の仕様書。着手前に `plan/games/NNN-*.md`、[共通ルール](plan/docs/COMMON_RULES.md)、[ワークフロー](plan/docs/WORKFLOW.md) を読む |
| `plan/progress.csv` | 進捗台帳。1本完了ごとに更新する |
| `games/NNN-name/` | 各作品の Unity プロジェクト |
| `templates/game/` | `dev new` が新規作品に展開する雛形 |
| `build/*.props` | Core を dotnet で検証するための共通 MSBuild 設定 |
| `dev.ps1` | 開発用統一コマンド |

## 各作品の構造

| アセンブリ | 場所 | 責務 |
|---|---|---|
| `Game.Core` | `Assets/Game/Core/` | ゲーム状態、ルール、乱数、コマンド。**Unity API に依存しない**（`noEngineReferences`） |
| `Game.App` | `Assets/Game/App/` | 画面遷移とユースケース。入力を意図に変換して Core に渡す |
| `Game.Unity` | `Assets/Game/Unity/` | 描画、音、入力、物理、Prefab/Scene。Core の結果を反映する |
| `Game.Core.Tests` | `Assets/Tests/Core/` | Core の EditMode テスト（NUnit） |

- `Game.Core` とそのテストは `dotnet/` 配下の csproj からも同じソースを参照しており、Unity なしでビルド・テスト・静的解析できる。CI はこの経路のみを検証する。
- そのため Core とそのテストでは、Unity と .NET の両方で動く書き方をする（NUnit 3.5 にある API、C# 9.0 まで）。
- Core は Nullable 有効・警告はエラー扱い。
- 物理エンジンに密接な題材は完全分離を強制せず、何を物理エンジンに委譲したかを作品の README に書く。
- 共通コードは、3題以上で再利用価値が明確になってから `packages/` にローカル UPM パッケージとして抽出する。

## 技術スタック

- Unity 6.6（各作品の正確な版は `ProjectSettings/ProjectVersion.txt` と作品の README）。操作は Unity CLI（`unity` コマンド）経由。
- C# 9.0。URP、Input System、UI Toolkit、Build Profiles、`Awaitable` など新しい機能を優先して使う。旧来の方式（Built-in RP、旧 Input Manager、IMGUI、コルーチン）は理由がなければ使わない。
- 配布対象は Windows（StandaloneWindows64）。

## dev コマンド

`dev <command> [作品番号]`。番号を省略すると、カレントディレクトリが `games/NNN-*` 配下ならその作品が対象。

| コマンド | 内容 |
|---|---|
| `dev new 002` | 仕様書に対応する Unity プロジェクトを作成（`-Template 3d` で 3D URP） |
| `dev gui 001` | Unity エディタで開く |
| `dev build 001` / `dev run 001` | Windows ビルド／ビルドを起動 |
| `dev test` | 全作品の Core テスト（dotnet、高速） |
| `dev test 001 -Unity [-Mode PlayMode]` | Unity 上でテスト（エディタを閉じた状態で実行） |
| `dev lint` / `dev format` | 整形・アナライザー・`.meta` の検証／整形の適用 |
| `dev check` | Core の型チェック |
| `dev clean [001]` | 生成物の削除 |

変更後は `dev lint`、`dev check`、`dev test` を通す。Unity 層を触ったら `dev test NNN -Unity` も通す。

## 注意点

- `Assets/` にファイルを追加・移動・削除したら `.meta` も一緒にコミットする。新規ファイルの `.meta` は Unity を一度起動すれば生成される（`dev test NNN -Unity` でよい）。
- `Library/` などの生成物はコミットしない。
- pre-commit で lint と型チェック、pre-push で Core テストが走る。`--no-verify` で回避しない。
