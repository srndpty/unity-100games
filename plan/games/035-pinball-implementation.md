# 035 ピンボール 実装計画

## Context

- 035 ピンボール（難易度3・Phase 2）が未着手。仕様書 [plan/games/035-pinball.md](./035-pinball.md) の中心課題は「**高速球の連続衝突検出**」、重点は「**フリッパーのアクチュエータ制御**」。In scope：1 台・フリッパー 2・バンパー 3・プランジャー・マルチボーナス・3 球で終了・タイトル→説明→結果→再プレイ。Out of scope：複雑な電子ルール・実機精度の摩擦。
- 題材固有テスト：バンパー反射と得点が一致／球喪失は 1 度だけ計上／球を複製できない。完了判定：3 球終了まで遊べる・フリッパーに入力が即応。
- ユーザーと決めたこと：
  - **物理は Core の整数物理**（Unity の物理は使わない）。固定小数点の球・線分（カプセル）・円・回転するフリッパーへの掃引判定を自前で書く。.NET と Mono で最終ハッシュを一致させる（013・020）。
  - **マルチボーナス＝マルチボール**：ドロップターゲット 3 枚を全部倒すと 2 個目の球が出る。
  - **2D 真上から**：縦長の台を画面中央、左右に HUD と操作案内（横長固定）。

作品名はオリジナル（仮）：**COMET COIL**。参考作品の名称・素材・演出は使わない。
この計画は着手時に `plan/games/035-pinball-implementation.md` として保存し、各セッションの結果を追記する（025〜034 と同じ）。

---

## セッション分割（progress.csv の記録/メモに区切りを残す）

| セッション | 内容 | 終わりの状態 |
|---|---|---|
| 0 | `git switch -c feature/game035`（main から）→ `dev new 035`（2D）→ 雛形と slnx 登録だけを最小コミット。int-geometry（`Vec2i`・`IntMath`・`Sweep`・`Trig`）と tile-motion（`StateHash`）を参照（manifest の `testables`・asmdef・`dotnet/G035.Core.csproj`、AGENTS.md の両パッケージの行に 035 を追記） | `dev lint/check/test` 通過 |
| 1 | ルール表を README に固定、Core（掃引・フリッパー・台データ・試合）＋ App（Flow・操縦役）とテスト | Core/App テスト通過（.NET と Mono）、固定ハッシュ一致 |
| 2 | Playable：生成ドット絵・Stage Prefab・HUD・音・シーン生成・撮影台本 | Windows ビルドと撮影 |
| 3 | Verification：PlayMode・性能計測・撮影再現・README/evidence | 状態「検証待ち」 |

---

## セッション1：ルール表（README に固定）と Core / App

### ルール表（要点）

| 項目 | 採用ルール |
|---|---|
| 時間 | 60 Tick/秒。Core・App は Tick 単位。入力はイベント時刻を含む Tick に反映（022 の `PrecisionFlow`） |
| 座標 | 整数。1 マス = 256 単位、台は横 40 × 縦 72 マス程度（y 上向き、重力は −y）。球の位置・速度は整数（単位/Tick） |
| 衝突物 | 静的：カプセル（線分＋半径。壁・ガイド・スリングショット）、円（バンパー・ポスト）。動的：フリッパー 2（回転するカプセル） |
| 連続衝突検出 | Tick を `Substeps`（設定で決まる、後述）に分け、各区間で球を直線で動かし、全衝突物への**最初に触れた時刻**を厳密に求める（円は int-geometry の `Sweep.First`、カプセルは Core の `CapsuleSweep`：2 本のずらした直線は 1 次式、両端は 2 次式）。触れた時刻まで進めて反射し、残りの時間で続ける。1 区間の反射は `MaxBouncesPerStep` 回まで、超えたら残り時間を捨てて止める（ルールとして定義・テスト）。ちょうど接するだけでは触れない |
| 反射 | 法線成分を `Restitution`（素材ごと、分数）で返し、接線成分はそのまま（摩擦なし）。速さは `MaxSpeed` で頭打ち |
| フリッパー（アクチュエータ） | 角度・角速度を整数（int-geometry の `Trig` 単位）。押している間は上向きの目標へ `FlipAccel` で加速し `FlipMaxSpeed` で頭打ち、端で止まり保持。離すと `ReturnAccel` で戻る。押した Tick から加速が始まる（遅延 0） |
| フリッパーとの衝突 | 区間ごとにフリッパーを先に回し、球と重なったら法線方向へ押し出し、接点の面の速度（ω × r）を足して反射。続けて球をその姿勢の静的カプセルとして掃引する。**すり抜けない条件**：1 区間の先端の移動 ≤ 球の半径＋フリッパーの半径、1 区間の球の移動 ≤ 球の半径（`Substeps` を設定の最大速度から決め、設定の検証で保証） |
| バンパー 3 | 触れたら反射に加えて外向きに `BumperKick` の速さで弾き、`BumperScore` 点。1 回の接触で 1 回だけ（押し出してから次の掃引をするので同じ接触を二度数えない） |
| ドロップターゲット 3 | 触れたら倒れる（倒れた物は当たりなし）・`TargetScore` 点。3 枚倒すとマルチボール開始（台上が 1 個のときだけ）・ターゲットは起き直す |
| プランジャー | 打ち出しレーンに球があるとき、Space を押している Tick 数で引き（`PlungerMaxTicks` で頭打ち）、離すと引きに比例した速さで上へ打ち出す。レーンの出口は一方通行の扉 |
| マルチボール | 2 個目の球をレーンに置いて自動で打ち出す。台上の球は最大 `MaxBalls`（2）。マルチボール中はターゲットを倒しても球は増えない |
| 球の喪失 | 球の中心がドレイン線より下へ出た Tick に 1 回だけ「失った」とし台から除く。台上に球が残っていれば続行、最後の 1 個を失ったら残り球を 1 減らして次の球をレーンへ。同じ Tick に 2 個失っても 1 減らすだけ |
| 終了 | 3 球を失ったら結果画面（得点・最高記録（起動中のみ））。終了後の入力は状態を変えない。再戦は新しいシード |
| 乱数 | マルチボールの 2 個目の打ち出しの強さのぶれだけ（試合ごとのシード、`SeededRandom`、系列を分ける 015） |

### 台データ（`Assets/Game/Data/Table.txt`、TextAsset）

- 行指向：`wall x0 y0 x1 y1 r`・`post x y r`・`bumper x y r`・`target x y w h`・`flipper L|R pivotX pivotY length r restAngle upAngle`・`plunger …`・`drain y`・`launch …`。
- Core の `TableBook.Parse` が検証：バンパー 3・ターゲット 3・フリッパー 2 ちょうど、全物体が台の内、静的物体がフリッパーの回る範囲・レーンに入らない、半径・長さが正。dotnet のテストも同じファイルを読む（033・034 の `PuzzleBook`・`StageBook` と同じ流れ）。

### Core（`Assets/Game/Core/`）

- `PinballConfig`：重力・`MaxSpeed`・`Restitution`・フリッパーの加速/最大角速度/戻り・`BumperKick`・各得点・プランジャー・`Substeps`・`MaxBouncesPerStep`・`MaxBalls`・球数 3。公開フィールド＋検証（範囲、**すり抜けない条件**、`Sweep.FirstLimit` に収まる、`Ticks` の整数化 004）。検証済みの複製を返す（029・030）
- `CapsuleSweep`：動く円と静止カプセルの最初の接触時刻と法線（long、範囲外は例外）
- `Flipper`：アクチュエータの状態（角度・角速度・押下）と `Step`、ある区間の姿勢・面の速度
- `Ball`：位置・速度・状態（レーン／台上／失った）
- `Table`・`TableBook`：台データと検証
- `PinballRun`：1 試合。`Step(入力)` → その Tick の出来事（バンパー・ターゲット・喪失・マルチボール）を返す。`Score`・`BallsLeft`・`BallsInPlay`・`Hash()`（全状態＋設定と台の指紋、`StateHash`）・`Clone()`
- 割り当て 0 の Step（固定容量の配列、027・028）

### App（`Assets/Game/App/`）

- `PinballFlow`：Title / Playing / Paused / Result。時間は引数で受け取り固定 Tick に変換、フレーム単位の Tick 上限（022）、入力は時刻付きの 1 本の列（012・022）。フォーカス喪失で一時停止し保留入力を消す、復帰は手動再開
- `Autopilot`：球がフリッパーの近くに来たら `Clone` で数通りの打つ時機を先読みして選ぶ（021）、プランジャーは乱数で強さを散らす。撮影・テスト・PlayMode で共有（002）

### テスト（題材固有を優先）

- **バンパー反射と得点が一致**：真っすぐ・斜め・かすりでバンパーに当てたとき、反射後の速度が法線の式と一致し、得点がちょうど `BumperScore` × 接触回数。2 つのバンパーに 1 Tick で続けて当たる・バンパーとターゲットに同じ Tick に当たる・押し出された直後の Tick も二重に数えない。乱数の球で「出来事の数 × 点 = 得点の増分」
- **球喪失は 1 度だけ計上**：ドレイン線をまたいで戻る・同じ Tick に 2 個失う・失った球への以後の Step・最後の球と同時の出来事 → 残り球の減りは 1 回だけ、3 球目で終了は 1 回だけ
- **球を複製できない**：レーンの球を二重に打てない・マルチボールは台上 1 個のときだけ・同じ Tick にターゲットを 2 枚倒しても 2 個目は 1 個・`MaxBalls` を超えない。乱数の長い試合で「台上 + レーン + 失った数 = 出した数」が全 Tick で成り立つ
- **連続衝突検出**：最大速度で薄い壁・バンパー・角へ撃ってすり抜けない（境界値と乱択、区間を細かく刻む素朴な参照実装と接触の有無・順序を照合 016・019）。`CapsuleSweep` を外積の参照実装と照合。最大の角速度のフリッパーが止まった球・向かってくる球をすり抜けない
- **フリッパー**：押した Tick から加速・端で止まる・離すと戻る・押し離しの境界 Tick、30〜240fps と不規則間隔で同じ入力列のハッシュが一致（022）
- 台データ：実データが通る、壊れたデータ 10 通り程度を拒否。設定：すり抜けない条件を破る値・範囲外を拒否
- 共通：状態遷移（タイトル→プレイ→3 球喪失→結果→再戦）、終了後の入力無視、フォーカス喪失・復帰の順序入れ替え、Hash の「今同じでも以後が分かれる」組（022）、操縦役で 20 シードが上限 Tick 内に 3 球を終える、シード 1 の最終ハッシュが .NET と Mono で一致

---

## セッション2：Playable（Unity 層）

- `CometCoilGame`（入力・Tick 駆動・HUD・音）：Input System。左フリッパー＝Z / ← / 左 Shift、右＝/ / → / 右 Shift、プランジャー＝Space（押して引く・離して打つ）、Esc 一時停止。打鍵は時刻付きで Flow へ
- `CometCoilStage`：Core の台データから組んだ Stage Prefab（壁・バンパー・ターゲット・フリッパー・プランジャー・球の見本 2）。`Mismatch` で種類・位置・大きさまで照合（023）、古いインスタンスは前方一致で消す（018）。描画は Tick の状態をそのまま（撮影は補間しない）
- `CometCoilSettings`（ScriptableObject）：`PinballConfig` の値・音量。コードの既定値を変えたら `.asset` も直す（024）
- HUD（PanelRenderer）：得点・残り球・ターゲットの状態・マルチボール表示、操作案内、タイトル、一時停止、結果（得点・最高記録）。作った直後に一度描画、タイトルへ戻ったら作り直す（021）
- 演出：バンパーの光・プランジャーの引き具合・倒れたターゲット。音（フリッパー・バンパー・ターゲット・喪失・マルチボール）は控えめ、`AudioPolicy.ShouldMute` で消音
- Editor：`CometCoilArtGenerator`（生成ドット絵）、`CometCoilSceneBuilder`（Stage Prefab とシーン）
- 撮影台本 `CometCoilCaptureScenario`（Autopilot を Tick 単位で、`EvidenceCapture.Seed`）：タイトル・プランジャーを引く・打ち出し・バンパー・フリッパーで打つ・ターゲット・マルチボール・球の喪失・結果

## セッション3：Verification

- PlayMode（実シーン）：キーを押したフレームでフリッパーの角速度が 0 でない（**入力が即応**、`TestKeyboard`）・離すと戻る／Space の長押しで打ち出し／Stage の描画が Core の位置・角度と一致／3 球を失って結果→再戦／Prefab の照合・設定アセットの検証／タイトルへ戻った表示が起動直後と一致／フォーカス喪失で一時停止
- 性能：マルチボールで球 2 個が最大速度・跳ね続ける場面で Core の Step p95 と描画の更新、割り当て 0（Mono は `ProfilerRecorder`、027）
- `dev build 035` → `dev capture 035` を 2 回、SHA-256 一致（1 回目を README からリンクしてから 2 回目、018・020）
- README：環境・操作・ルール表・**物理を Core で持つ理由とすり抜けない条件**・再現性の範囲・未実装範囲。`evidence/README.md`、`progress.csv` を「検証待ち」へ

---

## 主なファイル

- 新規：`games/035-pinball/**`、`plan/games/035-pinball-implementation.md`
- 変更：`unity-100games.slnx`（dev new）、`plan/progress.csv`、`games/035-pinball/Packages/manifest.json`、`dotnet/G035.Core.csproj`、`AGENTS.md`（パッケージの利用作品、完了時の知見）
- 流用元：`packages/com.games100.int-geometry`（`Vec2i`・`IntMath`・`Sweep.First`・`Trig`・`SeededRandom`）、`packages/com.games100.tile-motion`（`StateHash`）、033 の `Segment`（外積の参照実装）と SnipFlow（一時停止・フォーカス）、034 の `StageBook`（行指向データの検証）、022 の `PrecisionFlow`（入力時刻とフレーム単位の Tick 上限）、021 の `Autopilot`（Clone の先読み）、033/034 の SceneBuilder・ArtGenerator・CaptureScenario・`Mismatch`

## 検証

- 各セッションの終わりに `dev lint` → `dev check` → `dev test`
- Unity 層を触ったら `dev test 035 -Unity` と `dev test 035 -Unity -Mode PlayMode`
- `dev build 035` → `dev capture 035` → 証跡を確認 → 「検証待ち」で人間に渡す（`dev run 035`）。試遊で見てほしい点：重力と球の速さ、フリッパーの速さと打った感触、バンパーの強さ、プランジャーの引き、マルチボールの出やすさ、音量

---

## セッション0の結果（2026-11-24）

- `feature/game035` で `dev new 035`（2D テンプレート）を実行し、int-geometry と tile-motion を `manifest.json`（`testables`）・Core の asmdef・Core の csproj から参照して、雛形と slnx 登録を最小コミットした。Unity の EditMode（パッケージのテストを含む 63 件）、`dev lint/check/test 035` が通った。
- 次（セッション1）：ルール表を README に固定し、Core（`CapsuleSweep`・`Flipper`・`TableBook`・`PinballRun`）と App（`PinballFlow`・`Autopilot`）をテストから作る。

## セッション1の結果（2026-11-25）

- ルール表は [README](../../games/035-pinball/README.md) の「採用ルール」を正とする。
- Core：`Board`・`Capsule`（距離の厳密な判定）・`BallPath`（経路の点の列と保守的前進）・`PinballConfig`・`Table`（台データと検証）・`Flipper`・`PinballRun`。App：`Intent`・`PinballFlow`・`Autopilot`。台データ `Assets/Game/Data/Table.txt`。
- Core 93 件・App 17 件が .NET で、共通パッケージを含む 172 件が Mono で通った。操縦役で 20 シード全部が終わり（最長 27384 Tick）、シード 1・6 の最終ハッシュが .NET と Mono で一致した。
- 計画との差：
  - 接触の時刻を 2 次式で解く（`CapsuleSweep`・`Sweep.First`）のをやめ、経路を 1 単位ずつの点の列とみなし、保守的前進で飛ばす `BallPath` にした（`Sweep.FirstLimit` が台の広さに足りない。整数の比較だけで厳密になる）。
  - 区間の数は設定ではなく定数（8）にした。すり抜けない条件は角速度の上限と球の速さの上限を台の読み込みで検証する。
  - 接触した区間の残りを捨てると斜面で球が止まったので、反射後の速度で残りを進め、位置の端数も保つ。近づいていない相手は経路の判定から除く（フリッパーも止まった姿勢で判定）。
  - 球どうしは当たらない（README の未実装範囲）。
- 次（セッション2）：Unity 層（生成ドット絵・Stage Prefab・HUD・音・シーン生成・撮影台本）。

## セッション2の結果（2026-11-25）

- Unity 層：`CometCoilStage`（静止した部品を台のデータから置いた Stage Prefab。試合中は球・フリッパー・プランジャー・倒れたターゲット・光るバンパーだけを更新）、`CometCoilGame`（キーの押下・解放を時刻つきで 1 本の列に入れる。1 つの操作に束ねた複数のキーは「どれかが押されている」の変化だけを意図にする）、`CometCoilSettings`、`CometCoilArtGenerator`（16 px/マスのドット絵、フリッパー・バンパー・球は台のデータの大きさで描く）、`CometCoilSceneBuilder`、HUD（左に得点・球・最高記録・ターゲット・マルチボール、右に操作）、撮影台本 10 枚。
- Stage Prefab の照合（`Mismatch`）は、壁・扉・ターゲットの位置・向き・大きさ、バンパーの位置・大きさ、フリッパーの軸と絵の大きさ・基点、球の大きさを、置くときと同じ関数（`Layout`・`Segment`・`CircleScale`）の値と比べる。
- 撮影：最初の 1 球は台本がキーの意図で引いて打ち、以後は操縦役。操縦役のシードは設定の `CapturePilotSeed`（12）。.NET で 40 通りを試し、どれもマルチボールになることを確かめたうえで、試合が短い（2577 Tick）値を選んだ。
- Windows ビルドと撮影が通った。Unity の EditMode は 172 件。
- 次（セッション3）：PlayMode 実シーン、性能計測、同一ビルドの撮影再現、`evidence/README.md`、README。
