# 029 エンドレスランナー 実装計画

## Context

- 029 エンドレスランナー（難易度 2・Phase 2）に着手する。仕様書 [plan/games/029-endless-runner.md](029-endless-runner.md) の中心課題は「**障害物配置をチャンク単位で生成**」、重点は「**難度曲線に上限**」。
- In scope：3 レーン移動とジャンプ/スライド、無限チャンク生成、距離スコアと衝突、タイトル→操作説明→結果→再プレイ。Out of scope：3D 迷路分岐・課金通貨・素材量産。
- 題材固有テスト：到達不能な障害物配置を拒否／未使用チャンクを回収／同条件シードで出現順一致。完了判定：失敗と再挑戦が成立、**長時間移動でチャンクが累積しない**。
- ユーザーと決めたこと：
  - 見た目は **3D 後方追従**（`dev new 029 -Template 3d`、URP 3D）。リポジトリ初の 3D テンプレート
  - **エンドレス＋結果画面**：衝突で終了、距離スコアと起動中の最高記録を出す。勝利はなし。難度は一定距離で上限に達し、以後は上限のまま続く
- 作品名はオリジナル（仮）：**CINDER LANES**。参考作品の名称・素材・演出（遺跡・追ってくる怪物など）は使わない。
- 作業は `main` から `feature/game029` を切って行う。物理エンジンは使わない（衝突は Core の整数演算）。README にそう書く。

---

## セッション分割（区切りを progress.csv の記録/メモに残す）

| セッション | 内容 | 終わりの状態 |
|---|---|---|
| 0（済） | `dev new 029 -Template 3d`、3D テンプレートの不要物の確認（下記）、雛形と slnx 登録の最小コミット、この計画を `plan/games/029-endless-runner-implementation.md` に置く | `dev lint/check/test`・`dev test 029 -Unity` 通過 |
| 1 | ルール表を README に固定。Core（自機の動き・掃引・チャンクの表と検証・到達探索・生成と回収・難度曲線）＋ App とテスト | 題材固有テストが .NET と Mono で通る、操縦役で長時間走れる |
| 2 | Playable（3D の Stage Prefab・チャンクの表示プール・追従カメラ・HUD・音・シーン生成・撮影台本） | 人間が通しで遊べる |
| 3 | Verification（PlayMode・長時間走行の生成物数・ビルド・`dev capture`・README/evidence） | 状態「検証待ち」 |

難易度 2 なのでセッション 2 と 3 は進み具合でまとめてよい。ただし初の 3D なので、つまずいたら分ける。

### セッション0：3D テンプレートの確認

- `dev new` の `$unneeded`（[dev.ps1](../../dev.ps1) の `Invoke-New`）は 2D テンプレート向け。3D テンプレートで残るチュートリアル・サンプル（`Assets/TutorialInfo`・`Readme.asset`・`SampleScene`・不要なシーンテンプレートなど）を確かめ、`$unneeded` に足す（dev.ps1 は共通ファイルなので雛形コミットに含める）。
- 雛形（Evidence・`AudioPolicy`・`TestKeyboard`・`WindowsQuitWorkaround`）が 3D テンプレートでもそのまま動くかを `dev test 029 -Unity` と PlayMode で確かめる。分かったことは AGENTS.md の知見へ。
- 共通パッケージ：`com.games100.tile-motion`（`Units`・`Countdown`・`StateHash`）と `com.games100.int-geometry`（`SeededRandom`・`IntMath`）を `manifest.json`・`testables`・Core の csproj から参照する。

---

## ルール表（セッション1で README に固定する要点）

| 項目 | 採用ルール |
|---|---|
| 単位 | 整数。1 m = 256 単位（`Units.Tile`）、60 Tick/秒、時間の設定は Tick 数。進んだ距離は `long`（長時間でも桁あふれしない）。浮動小数点を使わず Mono と .NET で一致させる |
| 座標 | x＝横（レーン）、y＝高さ、z＝進行方向。自機は毎 Tick z へ `speed` 進む（自動で前進） |
| レーン | 3 本。←→/A D で隣のレーンへ `LaneChangeTicks` かけて横に移動（移動中は線形）。端より外へは拒否。移動中の次の入力は先行入力として 1 つ保持（`Countdown` で `InputBufferTicks`） |
| ジャンプ | ↑/W/Space。接地中だけ。整数の放物運動（初速・重力）。二段ジャンプなし |
| スライド | ↓/S。`SlideTicks` の間、当たり箱の高さが低くなる。スライド中のジャンプはスライドを解いて跳ぶ。**空中のスライドは急降下**（下向きの速さを `FastFallSpeed` にして、着地した Tick からスライド）。スライド中の再スライドは延長しない |
| 障害物 3 種 | **Wall**（全高・レーンを塞ぐ → レーン移動で避ける）／**Hurdle**（低い → ジャンプで越える）／**Beam**（頭上の梁 → スライドでくぐる）。動かない |
| 衝突 | 自機と障害物は軸平行の箱。**Tick 中の自機の直線の軌跡で掃引**（各軸の重なりの時刻区間の共通部分、整数の分数で厳密。ちょうど接するだけは触れない）。横からレーン移動で壁に当たっても終了。1 回触れたら試合終了（残機なし） |
| チャンク | 文字の表で書く（1 行＝奥行き 1 区画 `RowLength`、3 文字＝3 レーン、`.` 空・`W`・`H`・`B`）。表・難度（1〜`MaxTier`）・重みを持つ。既定の表は Core に文字列で持ち、パーサで読む |
| チャンクの検証（到達性） | 各チャンクは前後に空の余白行を持つ（どのチャンクの後にも続けられる）。**自機の本物の 1 Tick（`RunnerBody.Step`）を、そのチャンクの障害だけの小さな世界で動かす探索**（021 の `Traversal`）で、どの入口レーン（接地）からも出口まで接地して抜けられることを確かめる。そのチャンクが出うる全ての速さ（解禁距離での速さ〜`MaxSpeed`）で検査し、入る位相の違いは障害を手前に 1 Tick 分伸ばして覆う（十分条件）。通れなければ設定の検証で例外（fail-closed） |
| 生成と回収 | 自機の前方 `LookAhead` までチャンクを並べ、後方 `Behind` を過ぎたチャンクは回収する。生きているチャンクは固定容量のリング（`MaxActiveChunks`、検証で「前方＋後方の長さ ÷ 最短チャンク＋余裕」以上を保証）。生成数 − 回収数 ＝ 生存数 |
| 難度曲線 | 速さはチャンクの入口で決まりチャンク内は一定：`min(BaseSpeed + 入口の距離 × Accel, MaxSpeed)`。解禁される難度は `min(1 + 距離 / TierDistance, MaxTier)`。どちらも単調非減少で上限がある。上限に達する距離を README に書く |
| 出現順 | 1 試合 = 1 シード。チャンクの選択は専用の乱数系列で、**入口の距離（＝それまでのチャンクの長さの和）とシードだけ**で決まる。プレイヤーの操作に依らない。同じチャンクの連続は避ける |
| 得点 | 進んだ距離（m、整数）。最高記録は起動中だけ保持（ファイル保存なし） |
| 終了後 | 終了後の Tick と入力は状態を変えない |

---

## セッション1：Core / App

### Core（`Assets/Game/Core/`）

- `RunnerBody`：レーン・横位置・高さ・縦の速さ・スライド残り（`Countdown`）・先行入力。`Step(入力)` が 1 Tick の動き。立ち/スライドの当たり箱
- `Sweep3`：静止した箱と動く箱の掃引（各軸の区間の共通部分、分数の時刻）。参照実装（Tick を細かく割った素朴な判定）と乱数で照合
- `ChunkDef`・`ChunkParser`・`ChunkLibrary`（既定の表）：文字の表 → 障害物の箱の列。不正な文字・行の長さ・余白なしを拒否
- `Reachability`：チャンク × 速さ × 入口レーンの探索（状態＝`RunnerBody` そのものを重複除去、上限付き）。**到達性の検証と操縦役の両方で使う**
- `Difficulty`：速さと解禁難度の曲線（純粋関数）
- `ChunkStream`：シードからのチャンク選択、リングでの生成と回収、入口の距離
- `RunConfig`：公開フィールド＋検証（有限・範囲・曲線の上限・リング容量・全チャンクの到達性）
- `RunnerRun`：試合。1 Tick の順序＝入力の反映 → 自機の Step → 掃引による衝突 → 距離の加算 → チャンクの生成と回収。`Step(Command)`・`Hash()`（自機の全状態・`Countdown`・乱数・リングまで混ぜる、022）・`Clone()`

### App（`Assets/Game/App/`）

- `RunnerFlow`：Title / Playing / Paused / Result。実時間 → 固定 Tick、フレーム単位の Tick 上限（026 `SiegeFlow` を土台）。フォーカス喪失で保留入力も消す。再戦は起動時の値から進めた新しいシード（010）
- `Intent`：レーン左右・ジャンプ・スライドは押した瞬間の出来事として **1 本の列** で処理（012）
- `Autopilot`：前方のチャンクで `Reachability` の経路を求めて再生。試合ごとに判断状態を捨てる（023）

### テスト（題材固有を優先）

- **到達不能な障害物配置を拒否**：3 レーン全部が壁の行／2 レーン先へ移る距離が `MaxSpeed` で足りない壁の並び（低速なら通るが、そのチャンクが出うる速さに `MaxSpeed` が入るので拒否）／Hurdle の着地前に Beam が来る並び／余白のないチャンク。境界：あと 1 区画で通る・通らないの組。既定の表はすべて通る
- **未使用チャンクを回収**：長く進めて毎 Tick「生存数 ≤ `MaxActiveChunks`」「生成数 − 回収数 ＝ 生存数」「生存チャンクはすべて自機の後方 `Behind` 〜前方 `LookAhead` の範囲」。操縦役で 100 万 Tick（約 4.6 時間分）走っても上限内
- **同条件シードで出現順一致**：同じシードで、別々の入力列（何もしない・操縦役・乱数の操作）でも最初の N 個のチャンクの番号が一致。別のシードでは違う（複数シード）
- 難度曲線：どの距離でも速さ ≤ `MaxSpeed`・難度 ≤ `MaxTier`、単調非減少、上限に達した後は一定。上限の距離ちょうどと 1 前
- 自機：レーン端での拒否、移動中の先行入力、空中ジャンプの拒否、急降下、スライドの長さちょうど、スライド中のジャンプ
- 掃引：速い前進で薄い障害物をすり抜ける位置関係でも当たる、レーン移動中に壁の角をかすめる、ちょうど接するだけは当たらない、ジャンプの頂点で Hurdle の上を通る
- 共通：状態遷移（開始→進行→終了→再戦・終了後の入力無視・一時停止）、負テスト（NaN・範囲外・不正なチャンク表・未定義の操作・終了後の Step・同 Tick の重複入力）、再現性（同シード同入力で同ハッシュ）、**完走**（操縦役で 20 シードが上限の難度・速さで長時間死なない＝生成される並びは常に通れる。固定シードの最終ハッシュを .NET と Mono で照合）

---

## セッション2：Playable（Unity 層、3D）

- `CinderLanesGame`（入力・Flow・カメラ・HUD・音）、`CinderLanesStage`（Stage Prefab：道・自機・障害物 3 種の見本・ライト・カメラ）、`CinderLanesSettings`（ScriptableObject：速さ・曲線・ジャンプ・スライド・音量。生成スクリプトは既存アセットを上書きしない → 既定値を変えたら `.asset` も直す、024）
- **描画は自機の位置を原点にする**：Core の `long` の距離から「チャンクの z − 自機の z」を float にして置く（長時間でも float の精度が落ちない）。カメラは自機の後ろ上に固定し、横はレーンの位置に少し追従
- チャンクの表示：障害物と道の区画は **固定数のプール**（容量は Core の `MaxActiveChunks` × 1 チャンクの最大障害数から決める）。Core のリングで回収されたチャンクの表示はプールへ戻す。毎フレームの割り当て 0
- 見た目：プリミティブのメッシュ＋生成テクスチャ（URP Lit）。自機はプリミティブを組んだ Prefab で、走り・ジャンプ・スライドの姿勢を Transform で付ける。遠くの出現はフォグで隠す。撮影中は補間しない
- HUD（PanelRenderer）：距離・速さの段階・最高記録。タイトルに操作説明（←→ レーン／↑ Space ジャンプ／↓ スライド／Esc 一時停止）、結果画面（距離・最高記録・再挑戦・タイトルへ）。作った直後に一度描画する
- 音：レーン移動・ジャンプ・スライド・衝突を控えめに、`AudioPolicy.ShouldMute` で消音
- Editor：`CinderLanesArtGenerator`（テクスチャ・マテリアル）、`CinderLanesSceneBuilder`（Stage Prefab・設定アセット・シーン）
- 撮影台本：タイトル・走り出し・レーン移動・Hurdle を跳ぶ・Beam をくぐる・上限の速さ・衝突・結果（試合を分けてよい、026）

## セッション3：Verification

- PlayMode：キーでレーン移動・ジャンプ・スライドが効く E2E（013：Core を直接呼ばない）、衝突 → 結果 → 再挑戦で新しいシード、タイトルへ戻った表示が起動直後と一致（021）、一時停止・フォーカス喪失、HUD のボタン（ポインタイベント、013）、設定アセットの検証
- **長時間移動でチャンクが累積しない（Unity 側）**：`timeScale` を上げて操縦役で長く走らせ、障害物・道の GameObject の数と描画中の数が一定で上限以内、Core の生存チャンク数も上限以内
- `dev build 029`、`dev capture 029`、同一ビルドの再撮影の一致（018/020 の手順）
- README（環境・操作・ルール表・チャンクの書き方と検証・物理エンジンを使わないこと・未実装範囲）、`evidence/README.md`、`progress.csv` を「検証待ち」へ

---

## 主なファイル

- 新規：`games/029-endless-runner/**`、`plan/games/029-endless-runner-implementation.md`
- 変更：`dev.ps1`（3D テンプレートの不要物）、`unity-100games.slnx`、`plan/progress.csv`、`AGENTS.md`（3D の知見）
- 流用元：
  - `packages/com.games100.tile-motion`（`Units`・`Countdown`・`StateHash`・`Box` の考え方）、`packages/com.games100.int-geometry`（`SeededRandom`）
  - `games/021-platformer-2d` の `Traversal`（自機の物理で通れるかを決める）・`LevelRun.Clone`
  - `games/022-precision-platformer` の `Motion`（1 Tick の箱の掃引）
  - `games/026-twin-stick/Assets/Game/App/SiegeFlow.cs`・`Intent.cs`・`Autopilot.cs`、026/028 の SceneBuilder・ArtGenerator・CaptureScenario

## 検証

- 各セッションの終わりに `dev lint` → `dev check` → `dev test`
- Unity 層を触ったら `dev test 029 -Unity` と `dev test 029 -Unity -Mode PlayMode`
- `dev build 029` → `dev capture 029` → 証跡を確認 → 「検証待ち」で人間に渡す（`dev run 029`）
- 試遊で見てほしい点（予定）：速さの上限と上がり方、レーン移動の時間、ジャンプの高さと滞空、スライドの長さ、障害物の見やすさ

---

## セッション0の結果（2026-11-23）

- `feature/game029` で `dev new 029 -Template 3d` を実行し、雛形と slnx 登録を最小コミットした。
- 3D テンプレートでは 2D と違い `Assets/TutorialInfo`（Readme 用のエディタスクリプトとアイコン）と `Assets/Readme.asset` が残るので、`dev new` の除去リストに足した。`SampleScene`・`PC_/Mobile_RPAsset`・`InputSystem_Actions` は残す（シーンは生成スクリプトで作り直す、RP アセットは Quality 設定が参照する）。2D の `com.unity.2d.*`・Timeline・Visual Scripting は入らず、代わりに Cinemachine が入る。
- 雛形の部品（Evidence・`TestKeyboard` など）は 3D テンプレートでもそのまま動いた（EditMode 1・PlayMode 16）。
- `com.games100.int-geometry` と `com.games100.tile-motion` を `manifest.json`（`testables`）・`Game.Core.asmdef`・Core の csproj から参照した。
- 次（セッション1）：ルール表を README に固定し、Core（`RunnerBody`・`Sweep3`・チャンクの表と検証・`Reachability`・`Difficulty`・`ChunkStream`・`RunnerRun`）と App（`RunnerFlow`・`Intent`・`Autopilot`）をテストから作る。

## セッション1の結果（2026-11-23）

- ルール表は [README](../../games/029-endless-runner/README.md) の「採用ルール」を正とする。
- Core：`RunConfig`・`Runner`（`LaneState`・`VerticalState`・`RunnerBody`・`RunnerShape`）・`Sweep3`・`Chunks`（文字の表のパーサ）・`ChunkLibrary`（既定 14 個）・`Planner`（`MotionGraph`・`Planner`）・`RunRules`（`Difficulty` と検証）・`ChunkTrack`・`RunnerRun`。App：`Intent`・`RunnerFlow`・`Autopilot`。
- Core 45 件・App 16 件が .NET で、int-geometry 52 件と tile-motion 10 件を含む 123 件が Mono で通った。
- 計画との差：
  - 到達性の探索は状態ごとの幅優先ではなく、「横の状態（35）× 縦の状態（70）」の到達集合をビット集合で Tick ごとに進める動的計画法にした。横と縦の操作を独立にした（1 Tick に横と縦を 1 つずつ）ので、像が横の後続と縦の後続の積になる。素朴な全探索（9 通りの操作と `Sweep3.Boxes`）と乱数のチャンクで一致を確かめた。既定の 14 個の検証は .NET で約 0.3 秒。
  - 各状態が直前の Tick の始めの位置（`PrevX`・`PrevY`）を持つので、状態だけでその Tick の軌跡が決まり、障害物との判定を状態ごとに前もって分けて計算できる。
  - 空中のスライドの急降下は入れず、着地後のスライドの先行入力にした（状態の数を増やさない）。
  - 計画の `ChunkStream` は解析規則（CA1711）に合わせて `ChunkTrack` にした。
  - 自機の物理で「出口を止まった状態で越える」を求めるので、障害物の後ろに着地できる長さの空きがないチャンクは拒否される（既定の `corridor` は Hurdle と Beam の間を 6 行にした）。
- 自動操縦（区間ごとに `Planner.Plan` の列を再生）で、20 シードとも難度の上限（1000 m）を 500 m 越えてもぶつからない。30 万 Tick（約 83 分）走ってもチャンクは上限内。シード 1 の 20000 Tick 後のハッシュ `0xC90FAC81` が .NET と Mono で一致。
- 次（セッション2）：Unity 層（3D の Stage Prefab・自機と障害物の Prefab・チャンクの表示プール・自機を原点にした描画・追従カメラ・フォグ・HUD・音・シーン生成・撮影台本）。

## セッション2・3の結果（2026-11-23）

- 難易度 2 なので Playable と Verification を 1 回で行った。
- Unity 層：`CinderLanesStage`（自機を原点にした描画・障害物と道の区画の固定数のプール）、`CinderLanesGame`（入力・カメラ・HUD・音）、`CinderLanesSettings`、`CinderLanesArtGenerator`（座標から作る模様のテクスチャと URP Lit のマテリアル）、`CinderLanesSceneBuilder`（プリミティブで組んだ Stage Prefab・フォグ・シーン）、撮影台本 9 枚。
- 3D テンプレートでも 2D の作品と同じ構成（PanelRenderer の HUD・Evidence・TestKeyboard）がそのまま動いた。ライト・Global Volume はテンプレートのものを残し、空は単色・アンビエントは Flat・線形フォグにした。
- 計画との差：Space を決定とジャンプの両方に割り当てたため、試合を始めた（再開した）時刻ちょうどの押下を App で捨てるようにした（App 17 件目のテスト）。
- PlayMode 実シーン 11 件（キーの E2E は助走の中で確かめる、衝突は必ずぶつかる操縦役で作る、6 万 Tick の走行で Stage の物の数が変わらず描画が Core と一致）と雛形の 16 件が通った。
- 同じビルドを 2 回撮影し、9 枚の SHA-256 が一致した。状態を「検証待ち」にした。
- 試遊で見てほしい点：速さ（40〜80 単位/Tick ＝ 9.4〜18.8 m/秒）と上がり方（200 m ごと、1000 m で上限）、レーン移動の時間（8 Tick）、ジャンプの高さと滞空（33 Tick）、スライドの長さ（36 Tick）、障害物の見やすさ（フォグの距離）、カメラの位置、音量。

## 試遊の指示を受けた調整（2026-11-23）

- 指示：通り過ぎた壁が視界を奪い続けてやりにくい。厚い壁は通り過ぎた後の横移動でぶつかって楽しくない。自機を自然に走らせ、ジャンプ・ローリングの専用モーションがほしい（ユニティちゃんなど既存の無料素材を使ってよい）。
- 壁を Hurdle・Beam と同じ 0.5 m の板にした（Core の `Obstacle.Box`）。既定のチャンクは全部が到達性の検査を通ったまま。テストの配置 2 件（壁の位置）を直した。自動操縦の最終ハッシュ `0xC90FAC81` は変わらなかった。
- 自機が通り過ぎた障害物を半透明にした。板にしてもカメラは自機の後ろにあるので、通り過ぎてから数 m はカメラと自機の間に残るため。
- 自機をユニティちゃん（3D モデルデータ v1.4.0）に替えた。ライセンス（UCL 3.0）は人間が条文を確認し、第 3 条 4 項（再配布はライセンス関連ファイル一式を同梱）に従ってリポジトリに入れる。表記は画面の右下と README。
- 計画と違ったこと：Unity Toon Shader（0.13.4-preview）は Unity 6000.6 の URP で輪郭線しか描かれず、Simple Lit にした。JUMP00・SLIDE00 はクリップ自体が前進するので、根の動きをポーズに焼き込んだ。
- モーションの再生位置は Core の状態から決める（Animator の速さ 0）。同じビルドの 2 回の撮影で 9 枚の SHA-256 が一致した。PlayMode 実シーン 12 件。

## 試遊の指示を受けた調整（2 回目、2026-11-23）

- 指示：ぶつかったときにダメージか転倒のモーションを再生したい。スライドから走りへノータイムで戻るのが不自然なので、徐々に戻したい。
- 被弾は DAMAGED00 から倒れる DAMAGED01 に替え、結果画面の間に表示の時計で進めて倒れたところで止めた。撮影では台本が秒数を渡す。空中でぶつかったら地面へ落とす。結果のパネルは 0.8 秒遅らせた。
- ジャンプ・スライドの後は、直前の姿勢から走りへ 12 Tick かけてブレンドする。Animator の速さ 0 のまま、`CrossFade` に移り具合を渡して作るので、撮影は同じ絵になる。
- 撮影で、通り過ぎた直後の Beam が半透明でもカメラの前を覆うと分かった。Beam と自機のレーンのものは通り過ぎたらすぐ隠し、隣のレーンの壁・Hurdle だけ半透明（1.5 m 後ろで隠す）にした。
- PlayMode 実シーン 13 件。同じビルドの 2 回の撮影で 11 枚の SHA-256 が一致した。

## 試遊の指示を受けた調整（3 回目、2026-11-23）

- 指示：倒れるモーションが起き上がりの途中まで再生されるので、寝そべったところで止めたい。ぶつかったときにカメラを揺らしたい。
- 止める位置を、生成スクリプト（`CinderLanesCharacter.LyingTime`）が頭の高さから求めるようにした（0.7 → 0.354）。
- カメラの揺れは、倒れてからの秒数だけで決まる減衰する正弦波（0.45 秒・0.22 m）にした。撮影は同じ絵になる。
- 撮影を 12 枚にした（ぶつかった直後・倒れる途中・寝そべった結果）。同じビルドの 2 回の撮影で 12 枚の SHA-256 が一致した。
