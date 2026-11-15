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
| `build/*.props` | Core / App を dotnet で検証するための共通 MSBuild 設定（`GameLayer` で層を指定） |
| `dev.ps1` | 開発用統一コマンド |

## 各作品の構造

| アセンブリ | 場所 | 責務 |
|---|---|---|
| `Game.Core` | `Assets/Game/Core/` | ゲーム状態、ルール、乱数、コマンド。**Unity API に依存しない**（`noEngineReferences`） |
| `Game.App` | `Assets/Game/App/` | 画面遷移とユースケース。入力を意図に変換して Core に渡す |
| `Game.Unity` | `Assets/Game/Unity/` | 描画、音、入力、物理、Prefab/Scene。Core の結果を反映する |
| `Game.Core.Tests` | `Assets/Tests/Core/` | Core の EditMode テスト（NUnit） |
| `Game.App.Tests` | `Assets/Tests/App/` | App の EditMode テスト（NUnit） |
| `Game.Unity.Tests` | `Assets/Tests/Unity/` | 実シーンを読み込む PlayMode テスト。入力は雛形の `TestKeyboard` で送る |

- `Game.App` も Unity API に依存しない（`noEngineReferences`、Nullable 有効）。時間は引数で受け取り、固定刻みへの変換もここで行う。
- 雛形には共通部品が入っている：証跡撮影（`Game.Unity.Evidence`）、音の方針（`AudioPolicy`）、PlayMode 用の仮想キーボード（`TestKeyboard`）。

- `Game.Core`・`Game.App` とそれぞれのテストは `dotnet/` 配下の csproj からも同じソースを参照しており、Unity なしでビルド・テスト・静的解析できる。CI はこの経路のみを検証する。
- そのため Core・App とそのテストでは、Unity と .NET の両方で動く書き方をする（NUnit 3.5 にある API、C# 9.0 まで）。
- Core・App は Nullable 有効・警告はエラー扱い。
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
| `dev capture 001` | ビルドを撮影モードで起動し、`evidence/capture/` にスクリーンショットを保存して自動終了 |
| `dev test` | 全作品の Core / App テスト（dotnet、高速） |
| `dev test 001 -Unity [-Mode PlayMode]` | Unity 上でテスト（エディタを閉じた状態で実行） |
| `dev lint` / `dev format` | 整形・アナライザー・`.meta` の検証／整形の適用 |
| `dev check` | Core / App の型チェック |
| `dev clean [001]` | 生成物の削除 |

変更後は `dev lint`、`dev check`、`dev test` を通す。Unity 層を触ったら `dev test NNN -Unity` も通す。

## 1本の進め方

[ワークフロー](plan/docs/WORKFLOW.md) に従う。要点：

- 1 作品 1 セッションを基本にする。大きい題材は「Core とテスト」「Playable」「検証」でセッションを分け、途中の状態を `progress.csv` の記録/メモに残す。
- 自動検証（lint・check・test・Unity テスト・ビルド・`dev capture`）まで通したら状態を「検証待ち」にして人間に渡す。人間が `dev run NNN` で遊んで調整点を返し、OK が出てから「完了」にする。
- 難易度・速度・音量など遊んで決める値は、最初から設定アセット（ScriptableObject）に出しておく。
- 完了時に振り返りを書き、他の作品にも効く知見は下の「Unity 実装の知見」へ移す。

## Unity 実装の知見

作品を作る中で踏んだ落とし穴。新しい知見はここに追記する。

- **シーンはエディタスクリプトで生成する。** 手で編集せず `Editor/` の生成スクリプトを正とし、`unity run games/NNN -- -executeMethod <型>.Build` で再生成する（`-quit` は CLI の予約語で渡せない）。
- **シーンを開いてからアセットを読む。** `EditorSceneManager.OpenScene` は未使用アセットをアンロードするので、先に読んだアセットへの参照が切れて fileID 0 で保存される。
- **PlayMode テストで `InputTestFixture` を使わない。** 入力システムを初期化し直すため `InputSystem.actions`（プロジェクト全体の Input Actions）が null になる。`TestKeyboard` で仮想デバイスに状態イベントを送る。
- **バッチ実行ではゲームビューにフォーカスがなく、入力が捨てられる。** `TestKeyboard` は使用中だけ `InputSettings` でフォーカスを無視させている。
- **`unity test` はバッチ実行（`Application.isBatchMode` が true）。** エディタを開いたままだと実行できないので、人間が遊ぶときは `dev run` を使う。
- **音は控えめに。** 既定の音量は小さくし、`AudioPolicy.ShouldMute`（バッチ実行・証跡撮影）のときは消音する。矩形波は振幅 0.05 程度で十分に聞こえる。
- **証跡は `dev capture` で撮る。** 実ウィンドウにキー入力を送る方式は、作業中の画面と入力を奪う。各作品は `ICaptureScenario` を実装したコンポーネントをシーンに 1 つ置く。
  - 撮影中（`EvidenceCapture.IsRunning`）は乱数に `EvidenceCapture.Seed` を使い、実時間・プレイヤー入力では進めず、台本が Tick 単位で進める。同じビルドなら毎回同じ画像になる。
  - 撮影した画面は補間せず、その Tick の状態をそのまま描く。
  - 画像と `manifest.json`（シード、設定、ビルド GUID、撮影 Tick、SHA-256）は撮影ごとの一時フォルダに書き、検証できたら `evidence/capture/` とフォルダ名の付け替えだけで入れ替える（`EvidencePublisher`）。ファイル単位でコピーしないので新旧が混ざらず、古いファイルも残らない。手書きの `evidence/README.md` は `capture/` の外に置く。
  - 失敗しても撮影プロセスは必ず終了コード 1 で終わる。`dev capture` 側も 120 秒で打ち切る。
- **衝突回数などの実行時の上限は、設定の検証で保証する。** 上限を超えたら例外にする（fail-closed）だけでなく、検証を通った設定では超えないことを理屈とテスト（境界値と乱択）で確かめる。
- **動く物体が途中で端に着く Tick は、着いた時点で止まったとして補間する。** 開始位置と（端で止めた）終了位置を単純に補間すると、端の近くで位置がずれる。
- **画面比率に依存させない。** 正投影カメラの大きさは固定せず、画面の縦横比から毎フレーム決めて、プレイ領域全体が収まるようにする（4:3 や 1:1 でもテストする）。
- **再現性は「1 試合 = 1 シード」で保つ。** 再戦も新しいシードで作り直し、乱数の状態を試合をまたいで持ち越さない。シードと入力列だけで何試合目でも単独で再現できる。
- **動く物体との衝突は、接触した時刻の位置で判定する。** Tick 終了時の位置で判定すると、端での当たり外れが変わる。
- **実行時に作った Texture2D・Sprite・AudioClip などは所有者を決めて破棄する。** シーンを読み直すテストで溜まらないよう、`OnDestroy` から `Dispose` する。
- **設定値は有限かを検証する。** Inspector から無限大や NaN を入れられる。`float.IsFinite` で弾く。
- **Core の設定クラスは公開フィールドで書く。** Unity のシリアライズのため。Core に限り CA1051 を無効化してある。
- **乱数は自前の xorshift を使う。** `System.Random` は Mono と .NET で系列が一致する保証がなく、dotnet と Unity でテスト結果が変わりうる。
- **非推奨 API に注意。** Unity 6.6 では次が警告（CS0618）になる。
  - `FindFirstObjectByType` → `FindAnyObjectByType`。`FindObjectsByType(FindObjectsSortMode)` → 引数なしの `FindObjectsByType<T>()`
  - `UIDocument` は新機能が入らない → `PanelRenderer` を使う。`rootVisualElement` はなく、`RegisterUIReloadCallback`（版番号付きの `VersionedUIReloadCallback`）で渡されるルート要素に UI をつなぐ。UXML のライブリロードでも再度呼ばれる
- **IDE だけに出る Nullable の警告（CS8632）。** Unity が IDE 用に生成する csproj は csc.rsp の `-nullable:enable` を反映しない。雛形の `CsprojNullableSync`（`Editor/`）が生成時に `<Nullable>` を足す。反映されないときは Unity の Preferences > External Tools で「Regenerate project files」を押す。
- **Unity 生成の csproj をリポジトリの slnx に入れない。** slnx は dotnet で検証する `dotnet/` の csproj だけを束ねる（生成 csproj は gitignore 対象で CI に存在しない）。
- **エディタを開いたままでも `unity command` で操作できる。** `unity command editor invoke_menu`、`test run`、`build run`、`script eval` など。PlayMode テストを GUI エディタで流すときは `EditorUtility.audioMasterMute` で一時的に消音する。
- **C# 9 の target-typed new を使う。** 型が明らかなら `new()`（IDE0090 を警告にしている）。

## 注意点

- `Assets/` にファイルを追加・移動・削除したら `.meta` も一緒にコミットする。新規ファイルの `.meta` は Unity を一度起動すれば生成される（`dev test NNN -Unity` でよい）。
- `Library/` などの生成物はコミットしない。
- pre-commit で lint と型チェック、pre-push で Core / App テストが走る。`--no-verify` で回避しない。
