# 034 投射パズル 実装計画

## Context

- 034 投射パズル（難易度3・Phase 2）が未着手。仕様書 [plan/games/034-projectile-puzzle.md](./034-projectile-puzzle.md) の中心課題は「**射出ベクトルと弾道プレビュー**」、重点は「**破壊対象の Health/Joint 管理**」。In scope：5 面の建築物と弾 3 種・ドラッグで射出・構造物の破壊と勝利・タイトル→説明→結果→再プレイ。Out of scope：多数素材の破壊演出・オンライン対戦。
- 題材固有テスト：ドラッグ距離から初速が一貫／壊れた部材が二重得点しない／停止後のみ結果確定。完了判定：5 面をクリア可能、目標命中で構造が崩れる。
- ユーザーと決めたこと：
  - **物理は Unity Physics2D に委譲**：部材は `Rigidbody2D`＋`BoxCollider2D`、接合は `FixedJoint2D`（`breakForce`）。Core は射出ベクトル・弾道プレビュー・Health・接合の台帳・得点・停止判定・勝敗を持つ（AGENTS.md「物理エンジンに密接な題材は完全分離を強制せず README に書く」）。再現性は「同じビルド・同じ入力列で同じ結果」までとし、.NET と Mono の物理の一致は求めない。
  - **弾 3 種＝通常・重い・爆発**。
  - **標的全滅で勝ち・残弾で星**：各面の標的を全部壊して静止したら勝ち、弾を使い切って静止しても残れば負け。面は順に解放（033 と同じ）、起動中だけ最高点と星を記録。

作品名はオリジナル（仮）：**PEBBLE SIEGE**（石つぶてで砦の灯籠を落とす）。参考作品の名称・素材・演出は使わない。
この計画は着手時に `plan/games/034-projectile-puzzle-implementation.md` として保存し、各セッションの結果を追記する（025〜033 と同じ）。

---

## セッション分割（progress.csv の記録/メモに区切りを残す）

| セッション | 内容 | 終わりの状態 |
|---|---|---|
| 0 | `git switch -c feature/game034`（main から）→ `dev new 034`（2D）→ 雛形と slnx 登録だけを最小コミット。tile-motion（`StateHash`）を参照（manifest の `testables`・asmdef・`dotnet/G034.Core.csproj`、AGENTS.md の tile-motion の行に 034 を追記）。Unity 6.6 の Physics2D の版（Box2D の世代）と `Physics2D.simulationMode = Script` での `Physics2D.Simulate` の挙動・既定の減衰を小さなスクリプトで確かめ、計画に記録 | `dev lint/check/test` 通過 |
| 1 | ルール表を README に固定、Core（射出・弾道・面データ・Health/接合の台帳・得点・停止判定・試合）＋ App とテスト | Core/App テスト通過（.NET と Mono） |
| 2 | Playable：Physics2D の Stage（部材・接合・標的・弾）、5 面のデータと Stage Prefab、Tick 駆動の物理、弾道の点、HUD、音、シーン生成、5 面の解答（射出の列）の探索と再生 | 5 面が解答の再生でクリア、Windows ビルド |
| 3 | Verification：PlayMode・撮影台本・撮影再現・README/evidence | 状態「検証待ち」 |

---

## セッション1：ルール表（README に固定）と Core / App

### ルール表（要点）

| 項目 | 採用ルール |
|---|---|
| 時間 | 60 Tick/秒。物理は Unity 側が 1 Tick に 1 回 `Physics2D.Simulate(1/60)`（自動シミュレーションを切る）。Core・App は Tick 単位で進む |
| 座標 | 1 = 1 m の float（Unity のワールド座標と同じ）。盤は横 32 m × 縦 18 m 程度。地面は y = 0、下の消滅線より落ちた物は壊れたとみなす |
| 射出 | 弾を構えた位置（発射台）の近くを押してドラッグ、離すと射出。引いた向きの逆向きに飛ぶ。初速 = 方向 × `MaxSpeed` × min(引いた長さ, `MaxPull`) / `MaxPull`。**初速は離した位置だけで決まり**、ドラッグの経路・フレームレート・弾の種類（質量）に依らない（速度を直接与える）。引いた長さが `MinPull` 未満で離したら取り消し（弾を消費しない）。右クリック／Esc でも取り消し |
| 弾道プレビュー | 構えている間、Core の `Trajectory.Points(初速, 重力, dt, n)` の点を描く。Physics2D と同じ半陰的オイラー（v += g·dt、x += v·dt、弾は減衰 0）。最初の `PreviewTicks` Tick ぶん（全部は見せない）を数 Tick おきに点で描く |
| 弾 3 種 | 通常（基準）／重い（質量 ×3、大きさ少し大、壊れにくい部材も押し倒す）／爆発（飛行中にクリック（または Space）で起爆、または最初に何かに触れてから `FuseTicks` 後に自動で起爆。半径内の部材に距離で減るダメージと外向きの撃力）。1 発で起爆は 1 回だけ |
| 弾の順 | 面のデータで弾の列（例：通常・通常・爆発）を決め、順に使う。選ばない |
| 部材 | 木・石・氷の 3 素材の箱（回転 0/90 度で置く）。素材ごとに HP・密度・得点。標的（灯籠）は別の種類で HP が小さい |
| ダメージ | 衝突が始まった時（`OnCollisionEnter2D`）の法線撃力の合計 `I` について、`floor((I − 閾値) × 係数)`（閾値以下は 0、整数）。両方の物体に与える（地面・弾は受けない）。押し合いの持続（Stay）は数えない。面の開始から `GraceTicks` は数えない（積み上げの落ち着き） |
| 破壊 | HP が 0 以下になった Tick に 1 回だけ「壊れた」とし、得点を加える。以後のダメージ・衝突・爆発・消滅線は何もしない（二重得点なし）。壊れた部材の接合はすべて外れる |
| 接合 | 面のデータで部材の組と `breakForce` を持つ。外れるのは（a）力が上限を超えて Physics2D が外したとき（`OnJointBreak2D` を Core へ報告）、（b）どちらかの部材が壊れたとき。1 つの接合は 1 回だけ外れる（重複の報告は無視） |
| 消滅線 | 部材・標的が消滅線より下・盤の左右の外へ出たら壊れたとする（得点あり） |
| 停止 | 全ての動く物（部材・標的・飛行中の弾）の速さ < `RestSpeed` かつ角速度 < `RestAngular` が `RestTicks` Tick 続いたら静止。弾を撃ってから `SettleTimeoutTicks` 経ったら静止とみなす（揺れ続けて進まないのを防ぐ）。弾は静止・盤外・`ShotLifeTicks` 超えで消える |
| 勝敗 | **静止した時点でだけ判定**：標的が全部壊れていれば勝ち、そうでなく弾が残っていれば次の弾を構えられる、弾が無ければ負け。静止前に標的が全滅しても勝ちは確定しない（その間の破壊も得点に入る） |
| 得点・星 | 得点 = 壊した部材・標的の得点の和 + 残弾 × `UnusedShotBonus`。星 = 勝ちなら 1 + 残弾数（最大 3）。面ごとの最高点と星は起動中のみ記録 |
| 終了後 | 勝ち・負けの後の射出・起爆・報告は状態を変えない。R でその面をやり直す（Stage を作り直す） |
| 乱数 | 使わない |

### 面データ（`Assets/Game/Data/Stages.txt`、TextAsset）

- 1 面 = 名前・案内文・発射台の位置・弾の列・部材（`block wood x y w h rot`）・標的・接合（`joint a b breakForce`）。
- Core の `StageBook.Parse` が検証：5 面ちょうど、標的 1 つ以上、弾 1〜5 発、部材が盤の内、部材どうし・地面と重ならない（回転 0/90 なので軸に沿った箱）、大きさが正、接合の番号が存在し自己接合・重複なし、発射台の近くに部材がない。dotnet のテストも同じファイルを読む。
- 5 面：1 本の木の塔 → 石の土台と接合 → 氷の高い塔（重い弾が効く）→ 囲いの奥の標的（爆発が要る）→ 3 種を使い分ける砦。

### Core（`Assets/Game/Core/`）

- `SiegeConfig`：`MaxSpeed`・`MaxPull`・`MinPull`・重力・`PreviewTicks`・素材ごとの HP/得点/閾値/係数・弾の質量比・爆発の半径/ダメージ/撃力/`FuseTicks`・停止の閾値・タイムアウト・`GraceTicks`・`UnusedShotBonus`。公開フィールド＋検証（`float.IsFinite`、範囲、`MinPull < MaxPull`、閾値 ≥ 0）
- `Launch`：引いたベクトル → 初速（取り消しは null）。`Trajectory`：プレビューの点（配列を再利用、割り当て 0）
- `Stage`・`StageBook`（データと検証）
- `PieceLedger`：部材ごとの HP・壊れたか、接合ごとの外れたか。`ApplyImpact(a, b, impulse)`・`ApplyBlast(piece, distance)`・`FellOut(piece)`・`JointBroke(joint)` は、その Tick に新しく壊れた部材・外す接合を返す（Unity はそれだけを消す）
- `RestWatch`：毎 Tick の最大の速さ・角速度から静止を数える
- `SiegeRun`：1 面の試合。`Aim/Launch/Detonate` の命令は Tick の境目でだけ反映。`BeginTick()`（Unity へ渡す命令：射出・起爆）→ Unity が物理を進める → `EndTick(報告)`（衝突・接合の外れ・消滅・速さの最大）。状態：構え／飛行／静止待ち／勝ち／負け。`Score`・`Stars`・`Hash()`（HP・壊れた集合・接合・弾の残り・Tick・結果・設定と面の指紋、tile-motion の `StateHash`）・`Clone()`
- `Progress`：解放済みの面と面ごとの最高点・星

### App（`Assets/Game/App/`）

- `SiegeFlow`：Title / Select / Playing（構え・飛行・静止待ちは `SiegeRun` の状態）/ Paused / Cleared / Failed / Result（5 面）。時間は引数で受け取り固定 Tick に変換（フレーム単位の Tick 上限、017・022）。押下・移動・解放・キーは受け取った順の 1 本の列（012）。フォーカス喪失で構えを取り消し一時停止、復帰直後のクリックは捨てる（010）。札・ボタンはその画面で押したクリックにだけ効く（033）
- `Slingshot`：構えの状態（押した位置が発射台の近くか・今の引き・押している印）。起爆は「飛行中に新しく押した」押下だけ（射出の離しや押したままでは起爆しない、027 のボム）
- 画面座標 → ワールド座標は Unity 側で行う

### テスト（題材固有を優先）

- **ドラッグ距離から初速が一貫**：同じ離した位置なら経路（直線・寄り道・1 フレーム・多数フレーム）・弾の種類によらず同じ初速／長さに比例し `MaxPull` で頭打ち／`MinPull` の境界と隣接 float で取り消し／向きは引いた逆／`Trajectory` の点が閉じた式（x0 + v·t + g·Σ）と一致
- **壊れた部材が二重得点しない**：同じ Tick に 2 回の衝突・衝突と爆発・消滅線と衝突・壊れた後の報告・接合の重複報告 → 得点・壊れた数・Hash が 1 回分。壊れた部材の接合が 1 回だけ外れる
- **停止後のみ結果確定**：標的全滅でも動いている間は勝ちにならない／静止で勝ち／最後の弾の後の揺れの間に標的が壊れたら勝ち／弾切れで静止して残れば負け／揺れが続いてもタイムアウトで確定／`RestTicks` の境界（1 Tick 足りない・ちょうど）
- 面データ：実データの 5 面が通る、壊れたデータ 10 通り程度を拒否
- 共通：状態遷移（タイトル→選択→構え→飛行→静止→勝ち→次の面→…→結果→再プレイ、負け→R）、未解放を選べない、終了後の命令を無視、フォーカス喪失・復帰の順序入れ替え、設定の範囲外、Hash の「今同じでも以後が分かれる」組

---

## セッション2：Playable（Unity 層）

- `PebbleSiegeGame`（入力・Tick 駆動・HUD・音）：`Physics2D.simulationMode = Script`。毎 Tick：`SiegeRun.BeginTick` の命令を弾へ（`linearVelocity` を直接設定）→ `Physics2D.Simulate` → 集めた衝突・接合の外れ・消滅・最大の速さを `EndTick` へ → 返された壊れた部材・外す接合を消す
- `PebbleSiegeStage`：面ごとに Core のデータから組んだ Stage Prefab（部材・標的・接合・発射台・地面）。やり直しは Prefab を置き直して物理の状態ごと作り直す。照合 `Mismatch` は部材の種類・位置・大きさ・回転・接合の組と `breakForce` まで比べる（023）、古いインスタンスは前方一致で消す（018）。部材は `StartAsleep`
- `PebbleSiegeSettings`（ScriptableObject）：`SiegeConfig` の値・音量。コードの既定値を変えたら `.asset` も直す（024）
- 弾道の点（固定数のプール）、引き具合の帯、壊れた部材の小さな破片の演出（数を限る、Out of scope の「多数素材の破壊演出」に入らない程度）、HP の減りを色で表す
- HUD（UI Toolkit、PanelRenderer）：面の名前・残りの弾の列・得点・標的の残り、操作案内（ドラッグで引いて離す・クリックで起爆・R でやり直し・Esc で一時停止）。タイトル、面選択（解放と最高点・星）、勝ち・負け、5 面の結果。作った直後に一度描画、タイトルへ戻ったら作り直す（021）
- 音（射出・衝突・破壊・爆発・勝敗）は控えめ、`AudioPolicy.ShouldMute` で消音
- Editor：`PebbleSiegeArtGenerator`（生成ドット絵）、`PebbleSiegeSceneBuilder`（Stage Prefab とシーン）
- 解答 `Assets/Game/Data/Solutions.txt`：面ごとに「離した位置（引きのベクトル）と起爆の Tick」の列。`[Explicit]` の PlayMode 探索（角度 × 引きの長さを刻み、実際の Physics2D で静止まで進める）で見つけ、撮影とテストで再生（033 の手順の再生）。勝てない面はデータを直す

## セッション3：Verification

- PlayMode（実シーン）：
  - 弾道プレビューの点と実際の飛行（何にも触れる前の Tick）の位置が許容誤差（例 1e-3 m）内で一致（3 種とも、質量に依らない）
  - マウスのドラッグで射出され弾が減る、`MinPull` 未満・右クリックで取り消し、飛行中のクリックで 1 回だけ起爆・射出の離しでは起爆しない
  - 5 面それぞれ、何もしないで `GraceTicks` の数倍進めても部材が壊れず静止（積み上げが安定）
  - 5 面を解答の再生でクリア、同じ入力で 2 回の最終 Hash が一致（同じビルドの再現性）
  - 目標命中で構造が崩れる（命中後に複数の部材が動き、接合が外れる）
  - Prefab の照合・設定アセットの検証・タイトルへ戻った表示が起動直後と一致・フォーカス喪失で構え取り消し
- 撮影台本 `PebbleSiegeCaptureScenario`（解答を Tick 単位で再生）：タイトル・面選択・構えと弾道・飛行中・命中直後・崩落の途中・爆発・勝ち・負け・5 面の結果。`dev build 034` → `dev capture 034` を 2 回、SHA-256 一致（1 回目を README からリンクしてから 2 回目、018・020）
- README：環境・操作・ルール表・**Physics2D に委譲したもの（剛体の運動・衝突・接合の力）と Core が持つもの**・再現性の範囲（同じビルド・同じ機械・Script モード）・許容誤差・未実装範囲。`evidence/README.md`、`progress.csv` を「検証待ち」へ

---

## 主なファイル

- 新規：`games/034-projectile-puzzle/**`、`plan/games/034-projectile-puzzle-implementation.md`
- 変更：`unity-100games.slnx`（dev new）、`plan/progress.csv`、`games/034-projectile-puzzle/Packages/manifest.json`、`dotnet/G034.Core.csproj`、`AGENTS.md`（tile-motion の利用作品、完了時の知見）
- 流用元：`packages/com.games100.tile-motion`（`StateHash`）、033 の `SnipFlow`（一時停止・フォーカス・画面ごとのクリックの持ち主）と `PuzzleBook`（行指向データの検証）・`Progress`（解放）、033 の SceneBuilder・ArtGenerator・CaptureScenario・`Mismatch`、022 のフレーム単位の Tick 上限、027 の押した印

## 検証

- 各セッションの終わりに `dev lint` → `dev check` → `dev test`
- Unity 層を触ったら `dev test 034 -Unity` と `dev test 034 -Unity -Mode PlayMode`
- `dev build 034` → `dev capture 034` → 証跡を確認 → 「検証待ち」で人間に渡す（`dev run 034`）。試遊で見てほしい点：引きの強さ（`MaxSpeed`・`MaxPull`）、プレビューの長さ、壊れやすさ（閾値・HP）、爆発の強さ、5 面の難しさ、音量

---

## セッション0の結果（2026-11-24）

- `feature/game034` で `dev new 034`（2D テンプレート）を実行し、tile-motion を `manifest.json`（`testables`）・Core の asmdef・Core の csproj から参照して、雛形と slnx 登録を最小コミットした。Unity の EditMode（パッケージのテストを含む 11 件）が通った。
- Physics2D の確認（一時的なエディタスクリプト、確認後に削除）：
  - Unity 6000.6.9f1 の Physics2D は従来の Box2D（`velocityIterations` 8・`positionIterations` 3、サブステップなし）。低レベルの新 API（`LowLevelPhysics2D`）は見つからなかった。
  - `Rigidbody2D` の既定は `linearDamping` 0・`angularDamping` 0.05・補間なし・離散の衝突判定。
  - `Physics2D.simulationMode = Script` で `Physics2D.Simulate(1/60)` を 120 回呼んだ弾の位置は、半陰的オイラー（v += g·dt、x += v·dt）と誤差 0 で一致した（位置を先に進める陽的オイラーとは最大 0.327 m ずれる）。質量 3 でも速度を直接与えれば同じ軌道。→ `Trajectory` は半陰的オイラーで書き、PlayMode の許容誤差は 1e-4 m 程度で足りる見込み。
- 次（セッション1）：ルール表を README に固定し、Core と App をテストから作る。

## セッション1の結果（2026-11-24）

- ルール表は [README](../../games/034-projectile-puzzle/README.md) の「採用ルール」を正とする。
- Core：`V2`・`SiegeConfig`（素材・弾の値を含む）・`Launch`／`Trajectory`・`Stage`／`StageBook`・`TickReport`／`TickCommands`・`SiegeRun`・`Progress`・`ShotPlan`。App：`Intent`・`SiegeFlow`（`ISiegeWorld`・`ISiegePilot`）・`PlannedPilot`。
- 面データ `Assets/Game/Data/Stages.txt`（5 面、仮の配置。物理での調整はセッション2）。
- Core 72 件・App 21 件が .NET で、tile-motion を含む 103 件が Mono で通った。
- 計画との差：
  - 部材は回転を持たず、幅・高さで向きを表す（`block 素材 x y 幅 高さ`）。
  - 面の始めの衝突を数えない猶予は「最初の弾を撃つまで」に限った（撃った後は猶予の間でも数える）。
  - 物理の世界は App の `ISiegeWorld`（`Load`・`Simulate`・`Remove`・`Clear`）として Flow が 1 Tick ごとに呼ぶ。App のテストは物理のない偽物で行う。
  - `SiegeRun.Clone` は先読みに使わないので作らなかった。やり直しは試合と物理の世界を作り直す。
  - 操縦役（`PlannedPilot`）は構えられるようになってから 40 Tick 静かに待って撃つ（撮影で構えの絵を撮るため）。
- 次（セッション2）：Unity 層（Physics2D の Stage・Tick 駆動・弾道の点・HUD・音・シーン生成）と、5 面の解答の探索。

## セッション2の結果（2026-11-24）

- Unity 層：`PebbleSiegeWorld`（`ISiegeWorld` の Physics2D 実装）と `SiegeBody`、`PebbleSiegeStage`（発射台・構えた弾・ゴム・弾道の点 32・爆発・土ぼこり 12・部材と弾の見本）、`PebbleSiegeGame`（マウスとキーの入力・HUD・音）、`PebbleSiegeSettings`、`PebbleSiegeArtGenerator`・`PebbleSiegeSceneBuilder`、撮影台本 11 枚。
- 5 面の解答 `Assets/Game/Data/Solutions.txt` は、PlayMode の `[Explicit]` テスト `SolutionSearch` が実際の Physics2D で 1 発ずつ角度（0〜75 度、2.5 度刻み）× 引きの長さ（4 通り）× 起爆の Tick（爆発の弾だけ 4 通り）を総当たりして見つけた（5 面で約 3 秒）。面のデータは仮の配置のまま全面が解けた。
- PlayMode 実シーン 16 件と雛形の 16 件が通った。Windows ビルドと撮影が通った。
- 計画との差：
  - 面を読み込むたびに局所の物理を持つ新しいシーン（`LocalPhysicsMode.Physics2D`）を作り、その `PhysicsScene2D` だけを進める。Box2D の世界ごと作り直すので、前の面・やり直しの履歴に依らず同じ結果になる（別の面を挟んで同じ面を再生し、ハッシュと全部材の位置の一致をテスト）。
  - 部材は当たりの大きさを変えると起きるので、組み終えてから眠らせる（面の始めの 1.5 cm の沈み込みがなくなった）。
  - 衝突のダメージは、各部材が自分の `OnCollisionEnter2D` で自分の分だけを報告する（相手は -1）。2 つの部材がぶつかると両方が自分の撃力を受け、Core の「両方が受ける」と同じになる。
  - 弾道プレビューと実際の飛行は 3 種とも 40 Tick の間 1e-4 m 以内で一致した。
- 次（セッション3）：同一ビルドの撮影再現、`evidence/README.md`、README の更新、「検証待ち」。

## セッション3の結果（2026-11-24）

- 同じビルド（Build GUID `54fa3d6322564bd59d41a41bcffe7afb`）を 2 回撮影し、11 枚の SHA-256 が一致した（1 回目の撮影記録を README からリンクしてから 2 回目を撮った）。
- [検証記録](../../games/034-projectile-puzzle/evidence/README.md) と README（Unity 側の設計・許容誤差・テスト）を書き、状態を「検証待ち」にした。
- 試遊で見てほしい点：引きの強さ（`MaxSpeed` 21 m/s・`MaxPull` 2.4 m）とプレビューの長さ（48 Tick）、部材の壊れやすさ（閾値・HP）、爆発の強さ、5 面の難しさ（1 面目は 1 発で灯籠が落ちる・2 面目は 3 発とも使う）、盤の中で砦が小さく見えないか、音量。
- 完了時に AGENTS.md へ移す候補：面ごとに局所の物理のシーンを作り直すと Physics2D の結果が履歴に依らない、当たりの大きさを変えると `StartAsleep` の体も起きる、衝突のダメージは各体が自分の分だけ報告する。
