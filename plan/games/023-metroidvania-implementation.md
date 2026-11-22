# 023 探索型アクション 実装計画

## Context

`plan/progress.csv` の 23 番（探索型アクション、難易度3・Phase 2）が未着手。仕様書 [plan/games/023-metroidvania.md](023-metroidvania.md) の中心課題は「**能力フラグによるゲート判定**」と「**部屋を跨ぐ状態保持**」。In scope は「連結5部屋・新能力2種で通路解禁・マップ・回復・ボス」と、タイトル→操作説明→勝敗→再プレイの一巡。

ユーザーと決めたこと：
- 能力は **二段ジャンプ ＋ 破壊ショット**（高い段の扉と、ひびの壁の扉）
- 021・022 にある整数の衝突処理は 023 で3題目の利用になるので、**`packages/` にローカル UPM パッケージとして抽出**して使う（021・022 は置き換えない）
- 体力0は **最後に触れたセーブ像の状態から再開**（ファイル保存はしない。起動中だけ）

作品名はオリジナル（仮）：**MOSSLIGHT DEPTHS**。参考作品の名称・素材・演出は使わない。

---

## セッション分割（progress.csv の記録/メモに区切りを残す）

| セッション | 内容 | 終わりの状態 |
|---|---|---|
| 0（済） | `packages/` へ抽出・dev.ps1/props/slnx 対応・`dev new 023`・最小コミット | `dev lint/check/test` 通過 |
| 1（済） | Core ＋ App とテスト（ルール表の固定を含む） | Core/App テスト通過、README のルール表 |
| 2（済） | Playable（Stage Prefab・HUD・地図・音・シーン生成） | 人間が通しで遊べる |
| 3 | Verification（PlayMode・ビルド・`dev capture`・README/evidence） | 状態「検証待ち」 |

---

## セッション0：共通パッケージ `com.games100.tile-motion`

### 置き場所と中身
`packages/com.games100.tile-motion/`
- `package.json`（name `com.games100.tile-motion`、version `0.1.0`、unity `6000.6`）
- `Runtime/TileMotion.asmdef`（`noEngineReferences: true`、名前空間 `TileMotion`）＋ `csc.rsp`（雛形の Core と同じ Nullable 設定）
- `Runtime/` に 022 から移植（021 と 022 の差分は 022 側を採用）：
  - `Box`（`Span` を含む）・`Units` ← [022 Box.cs](../../games/022-precision-platformer/Assets/Game/Core/Box.cs)
  - `ISolidGrid`（新規：`AnySolidInColumn(int, Box)` / `AnySolidInRow(int, Box)`）
  - `BodyMotion.MoveX/MoveY`・`GroundProbe` ← [022 BodyMotion.cs](../../games/022-precision-platformer/Assets/Game/Core/BodyMotion.cs)。`TileMap` 引数を `ISolidGrid` に置き換え（`WallProbe` は 022 だけで使うので抽出しない）
  - `Countdown` ← [022 Countdown.cs](../../games/022-precision-platformer/Assets/Game/Core/Countdown.cs)
  - `StateHash` ← [022 StateHash.cs](../../games/022-precision-platformer/Assets/Game/Core/StateHash.cs)
- `Tests/TileMotion.Tests.asmdef` ＋ 021/022 の `TerrainTests`・`GraceTests` のうち抽出部分のテストを移植
- `dotnet~/TileMotion/TileMotion.csproj`・`dotnet~/TileMotion.Tests/TileMotion.Tests.csproj`（末尾が `~` のフォルダは Unity が読まないので、bin/obj の DLL が取り込まれない）

### 繋ぎ込み
- **build/*.props**：`Compile Include` のパスを `SharedSourceDir` プロパティ（既定は今の `Assets/Game/$(GameLayer)`、テストは `Assets/Tests/$(GameLayer)`）で上書きできるようにし、パッケージの csproj から再利用する。
- **Unity**：023 の `Packages/manifest.json` に `"com.games100.tile-motion": "file:../../../packages/com.games100.tile-motion"` と `"testables": ["com.games100.tile-motion"]`（`dev test 023 -Unity` で Mono 上でもパッケージのテストを流す）。`Game.Core.asmdef` の references に `TileMotion`。
- **dotnet**：`G023.Core.csproj` から `TileMotion.csproj` を ProjectReference。`unity-100games.slnx` に `/packages/` フォルダとして 2 csproj を登録。
- **dev.ps1**：
  - `Test-MetaFiles`・空白整形の対象に `packages/*/`（末尾が `~` のフォルダを除く）を加える（ローカルパッケージも `.meta` が必要）
  - `Test-SolutionProjects` の期待値に `packages/*/dotnet~/**/*.csproj` を加える
  - `$SharedPathPattern` に `^packages/` を加える（パッケージ変更時は全作品を検査）
  - `dev test` がパッケージのテストも実行することを確認（slnx 経由でなければ対象に追加）
- **.meta の生成**：`dev test 023 -Unity` を一度流して、パッケージ配下の `.meta` を生成・コミット対象にする。
- **文書**：AGENTS.md の「構成」表に `packages/` 行を追加。`plan/TODO.md` に「021・022 を `tile-motion` に置き換える」を記録。

### 手順
1. `git switch -c feature/game023`（main から）
2. `dev new 023` → 雛形と slnx 登録だけを最小コミット（AGENTS.md の規約どおり）
3. パッケージ抽出・props・dev.ps1・slnx → `dev lint` / `dev check` / `dev test`

---

## セッション1：ルール表（README に固定する）と Core / App

### ルール表（要点）

| 項目 | 採用ルール |
|---|---|
| 単位 | 1 タイル = 256 単位の整数、60 Tick/秒。時間の設定は Tick 数。**乱数は使わない**（敵・ボスは Tick で決まる動き） |
| 部屋 | 5 部屋、各 40×22 タイルの 1 画面（カメラ固定）。部屋の端の「扉」区間から出ると隣の部屋の対応する扉に入る |
| 地図（部屋の配置） | 下の図。扉は双方向 |
| タイル | 空 `.`・固体 `#`・ひびの壁 `%`（固体。破壊ショットで壊れる）・開始 `S`・セーブ像 `$`・能力 `A`/`B`・回復の実 `+`・敵 `w`・ボス `M` |
| 自機 | 横移動・ジャンプ（可変ジャンプ・接地猶予・入力バッファは `Countdown`）・ショット。体力 5（設定） |
| 二段ジャンプ（能力A） | 空中で 1 回だけ追加のジャンプ。着地で回復。未取得なら空中のジャンプ押下は何もしない（バッファには残る） |
| ショット | 最初から使える。向いている方向へ直進、画面内 2 発まで。敵とボスに 1 ダメージ。固体・ひびの壁で消える |
| 破壊ショット（能力B） | 取得後のショットは、当たったひびの壁を壊す（その 1 タイル）。**未取得ならひびの壁は壊れない** |
| 敵 | 歩く敵（往復、段差で折り返す）。体力 2。接触で 1 ダメージ・ノックバック・60 Tick の無敵 |
| 回復 | 回復の実：+2（上限まで）、取ると消えて二度と出ない。セーブ像：触れると全回復し、その時点の世界の状態を記録 |
| ボス | 部屋 5。体力 20。突進 → 停止 → 3 方向弾の周期（Tick で決まる）。撃破で勝利 |
| 世界の状態（部屋をまたいで保持） | 取得済み能力・壊したひびの壁・取った回復の実・訪れた部屋・ボス撃破・最後のセーブ |
| 部屋ごとにリセット | 敵（再入室で出現位置に戻る）・ショット・ボス戦の途中経過（ボス部屋を出たら体力満タンで戻る） |
| 敗北 | 体力0 → ゲームオーバー画面 → Enter で**最後にセーブ像に触れた時点の世界の状態**とその像の前から再開（体力満タン）。セーブ後に取ったものは失う。死亡回数を数える |
| 勝利 | ボス撃破 → 結果画面（遊んだ時間・死亡回数・回復の実の取得数）→ Enter で最初から |
| 終了後 | 勝利・ゲームオーバー中の Tick と入力は状態を変えない |
| 保存 | ファイルには保存しない（起動中だけ）。破損データの扱いは対象外 |

部屋の配置案（地図画面にこの格子を描く）：

```
 [4 塔: 破壊ショット・セーブ像] ─(ひびの壁)─ [5 ボス]
        │(高い段：二段ジャンプ)
 [1 拠点: 開始・セーブ像] ── [2 回廊: 敵・ひびの壁の奥に回復の実] ── [3 洞穴: 二段ジャンプ]
```
- 3 の二段ジャンプは落ちた先にあり、**取らないと 2 へ戻れない**（「取得後に戻れる」をテストする場面）
- 2 のひびの壁は破壊ショット取得後に戻ると開けられる（戻り探索）
- 1→4 は二段ジャンプ、4→5 は破壊ショットのゲート

### Core（`Assets/Game/Core/`、`TileMotion` を参照）
- `TileMap`（`ISolidGrid` を実装。ひびの壁は「壊されていなければ固体」）
- `Ability`（`[Flags]`：`DoubleJump`・`BreakShot`）と `AbilitySet`
- `RoomMap` / `Rooms`（文字の地図から 5 部屋、扉・アイテム・敵・像の位置）、`WorldLayout`（部屋の格子と扉の対応）
- `WorldState`：上の「保持」項目。`Clone()`・`Hash()`。セーブ像はこのコピーを持つ
- `Player`（022 の `Player` から移動・ジャンプを流用し、ダッシュ・壁蹴りを除いて二段ジャンプ・ショット・体力・無敵・ノックバックを追加）
- `Shot`・`Walker`・`Boss`（接触は 021 の `EnemyContact` の考え方で相対軌跡、ショットの当たりは経路で判定）
- `RoomRun`：1 部屋の進行（自機・敵・ショット・アイテム）。扉の通過を事象として返す
- `Expedition`：全体（現在の部屋・`WorldState`・部屋の移動・セーブ／再開・勝敗・経過 Tick）
- `ExplorerConfig`：設定と検証（有限・範囲・**ゲートが成り立つこと**：二段ジャンプなしの最高到達点 < 高い段 ≤ 二段ジャンプありの到達点を、021 の `Traversal` の方式で実際に自機を動かして確かめる）

### App（`Assets/Game/App/`）
- `ExploreFlow`：Title / Playing / Paused / GameOver / Victory。フレーム単位の Tick 上限、遊んだ時間は上限と分けて数える（017・022 の知見）
- `Intent` / `HeldControls`：022 から移植（左右・ジャンプ・ショット）。再開・部屋移動ではキーを一度離すまで受け付けない（020 の知見）
- `RouteScript` / `Routes`：全編を完走する入力の列。区間の探索は 022 の `RouteFinder` を移植した `[Explicit]` テストで作る。ボス戦は `Expedition.Clone()` の先読みで操作する `BossPilot`（021 の `Autopilot` 方式）

### テスト（題材固有を優先）
- **未取得能力ではゲートを通れない**：
  - 1 の高い段：二段ジャンプなしで到達できる位置を有限の探索（入力の組み合わせ × 量子化した位置）で列挙し、4 への扉に届かないこと。ありなら届くこと
  - ひびの壁：未取得のショットが当たっても壊れず通れない。取得後は壊れて通れる。壊れた状態は `WorldState` に残る
- **取得後に戻れる**：3 で二段ジャンプを取ると 2 へ戻れる（ルートの再生）、取る前は戻れない（探索）
- **部屋再入室で重要状態が保持される**：能力・壊した壁・取った回復の実・ボス撃破が、出入り（往復・複数回）で残る。敵は出現位置に戻る（保持しないものも明示的にテスト）
- **セーブ／再開**：セーブ後に取った物は敗北で失われる、セーブ前の物は残る、再開位置と体力
- 共通：状態遷移（開始→進行→終了→再戦、終了後の入力を無視）、負テスト（未定義の操作・範囲外の設定・NaN・勝利後の Tick・同じ回復の実の二重取得・同じ Tick の扉と被弾）、完走（`Routes` で全編を完走し最終ハッシュを .NET と Mono で照合）

---

## セッション2：Playable（Unity 層）

- `MossDepthsGame`（入力・カメラ・HUD・音）、`MossDepthsStage`（1 部屋の Tilemap・自機・敵・アイテム・像）、`MossDepthsSettings`（ScriptableObject：速度・体力・ボス・音量）
- Editor：`MossDepthsArtGenerator`（ドット絵生成、1 タイル 32px・Point）、`MossDepthsSceneBuilder`（5 部屋の Stage Prefab を Core の地図から生成し、既存 Prefab は上書きしない。022 の構成を踏襲）
- HUD（PanelRenderer）：体力・取得能力・**小さな地図**（訪れた部屋・今の部屋・ボス部屋）。一時停止中は大きな地図。操作説明をタイトルに表示
- 壊れたひびの壁・取った実は Stage の再入室時に `WorldState` から反映。タイトルへ戻ったら Core から作り直す（021 の知見）
- 音は控えめ、`AudioPolicy.ShouldMute` で消音

## セッション3：Verification

- PlayMode：キー操作（移動・二段ジャンプ・ショット）、部屋の切り替えと Stage の表示、ひびの壁が壊れて見えなくなる、再入室で表示が保持、ゲームオーバー→セーブ像から再開、勝利の結果画面、タイトルへ戻った表示が起動直後と一致、一時停止・フォーカス喪失
- `dev build 023`、`dev capture 023`（`ICaptureScenario`：タイトル・各部屋・能力取得・ひびの壁破壊・地図・ボス・結果）、同一ビルドで再撮影の一致
- README（環境・操作・ルール表・設計上の選択・パッケージに委譲したもの・未実装範囲）、`evidence/README.md`、`progress.csv` を「検証待ち」に

---

## 主なファイル

- 新規：`packages/com.games100.tile-motion/**`、`games/023-metroidvania/**`
- 変更：[dev.ps1](../../dev.ps1)、[build/Game.Library.props](../../build/Game.Library.props)、[build/Game.Tests.props](../../build/Game.Tests.props)、[unity-100games.slnx](../../unity-100games.slnx)、[AGENTS.md](../../AGENTS.md)、[plan/TODO.md](../TODO.md)、[plan/progress.csv](../progress.csv)
- 流用元：[022 Player.cs](../../games/022-precision-platformer/Assets/Game/Core/Player.cs)、[022 PrecisionFlow.cs](../../games/022-precision-platformer/Assets/Game/App/PrecisionFlow.cs)、[022 RouteScript.cs](../../games/022-precision-platformer/Assets/Game/App/RouteScript.cs)、[022 RouteFinder.cs](../../games/022-precision-platformer/Assets/Tests/App/RouteFinder.cs)、[021 EnemyContact.cs](../../games/021-platformer-2d/Assets/Game/Core/EnemyContact.cs)、[021 Traversal.cs](../../games/021-platformer-2d/Assets/Game/Core/Traversal.cs)、[021 Autopilot.cs](../../games/021-platformer-2d/Assets/Game/App/Autopilot.cs)、022 の SceneBuilder / ArtGenerator / CaptureScenario

## 検証

- 各セッションの終わりに `dev lint` → `dev check` → `dev test`
- Unity 層に触れたら `dev test 023 -Unity` と `dev test 023 -Unity -Mode PlayMode`（パッケージのテストも Mono で流れる）
- `dev build 023` → `dev capture 023` → 証跡を確認 → 「検証待ち」で人間に渡す（`dev run 023`）

---

## セッション1の結果と計画との差（2026-11-22）

- ルール表は [README](../../games/023-metroidvania/README.md) の「採用ルール」を正とする。
- 試合の開始位置を最初のセーブとして扱う（像に触れる前に倒れても開始位置から再開）。訪れた部屋もセーブの記録に含めて戻す。
- ボスの部屋の入口の段は消した。突進が段で止まり、真横の弾も段で消えて安全地帯になったため。
- 到達範囲の探索は、状態をそのまま比べると 40 万手でも尽きなかった。位置と速さをまとめた近似にし、届く高さの差が大きい場面でだけ「届かない」の根拠に使う。
- テストで場面を作るため `Expedition.StartAt`（世界の状態と位置を指定して始める）を足した。撮影台本でも使える。
- 同じ Tick の「致死の被弾」と「扉」の順序は規則（被弾が先）をコードで保証しているが、その場面を作るテストはない（README の未実装範囲に記載）。

## セッション2の結果（2026-11-22）

- Unity 層（`MossDepthsGame`・`MossDepthsStage`・生成スクリプト・撮影台本）を実装し、Windows ビルドと `dev capture`（13 枚）が通った。
- 撮影で、塔の入力の列が玉を取る前に像に触れてセーブしていたことに気づいた（ルールどおりだが、再開で破壊ショットを失う）。RouteFinder の小目標を「破壊ショットを持って像に触れる」にして列を作り直し、通しは 2565 Tick・ハッシュ `0xD0072BE8`（.NET と Mono で一致）になった。
- 次（セッション3）：PlayMode テスト（キー操作・部屋の切り替え・ひびの壁の表示・再入室・ゲームオーバーと再開・勝利・タイトルへ戻った表示・一時停止とフォーカス）、evidence/README、同一ビルドの再撮影の一致、状態を「検証待ち」へ。
