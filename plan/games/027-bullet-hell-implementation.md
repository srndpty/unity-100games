# 027 弾幕STG 実装計画

## Context

- 027 弾幕STG（難易度 4・Phase 2）に着手する。仕様書 `plan/games/027-bullet-hell.md` の中心課題は「**弾道DSL/パラメータ生成**」、重点は「**大量弾のプーリングまたはデータ指向処理**」。
- In scope：ボス 2 体・各 3 弾幕、低速移動と当たり判定表示、リトライ・残機、タイトル→操作説明→結果→再プレイ。Out of scope：原作スペル全再現・ネット対戦・素材量産。
- 題材固有テスト：弾道式の軌跡が期待値と一致／無敵中の二重被弾なし／弾数上限で破綻しない。完了判定：6 パターンを再現しクリア可能、弾数・処理時間を計測記録。
- ユーザーと決めたこと：
  - 弾幕は **テキスト DSL ＋ Core のパーサ**で定義する（TextAsset、設定アセットから差し替え可能）
  - **ボスラッシュ**（道中なし、ボス 1 → ボス 2）。残機 0 で結果画面 →「このボスから再挑戦」または「最初から」
  - 操作は **矢印 / WASD 移動・Shift 低速（当たり判定表示）・Z 押しっぱなし連射・X ボム・Esc 一時停止**（キーボードのみ）。ボムは In scope 外の追加として README に記録する
- 作品名はオリジナル（仮）：**LANTERN VEIL**。参考作品の名称・キャラクター・スペル名・演出は使わない。
- 027 は `main` から `feature/game027` を切った（026 はマージ済み。026 の `Geometry.cs` などは写して使う）。

---

## セッション分割（難易度 4 なので 5 つに分ける。区切りを progress.csv の記録/メモに残す）

| セッション | 内容 | 終わりの状態 |
|---|---|---|
| 0（済） | `dev new 027`・雛形と slnx 登録の最小コミット・この計画を `plan/games/027-bullet-hell-implementation.md` に置く | `dev lint/check/test` 通過 |
| 1 | ルール表を README に固定。DSL（字句・構文・検証）、弾の運動と SoA の弾プール、角度表 | 題材固有テスト「軌跡」「弾数上限」が .NET と Mono で通る |
| 2 | 試合（自機・ショット・ボム・ボス・弾幕の進行・被弾・残機・再挑戦）、App（Flow・操縦役）、6 弾幕の DSL ファイル | 操縦役で 6 弾幕を突破、最終ハッシュが両環境で一致 |
| 3 | Playable（Stage Prefab・弾の一括描画・HUD・音・シーン生成・撮影台本） | 人間が通しで遊べる |
| 4 | Verification（PlayMode・負荷計測・ビルド・`dev capture`・README/evidence） | 状態「検証待ち」 |

---

## ルール表（セッション1で README に固定する要点）

| 項目 | 採用ルール |
|---|---|
| 単位 | 位置は 1 px = 256 単位の整数、60 Tick/秒、時間の設定は Tick 数。浮動小数点を使わず Mono と .NET で一致させる |
| 角度 | 1 周 = 4096 の整数。sin/cos は **ソースに書いた 1/4 周期の整数表（Q14）** を引く（`Math.Sin` を実行時に使わない）。表は生成テストで `Math.Sin` との差 ±1 以内・対称性を確認 |
| 画面 | 16:9 の画面に縦長のプレイ領域 384×448 px（左寄せ）＋右に HUD。カメラ固定 |
| 自機 | 通常 4.0 px/Tick・低速 1.75 px/Tick（斜めは `×181/256`、最低 1）。領域内で止まる。当たり判定は半径 2.5 px の円、低速中だけ表示 |
| ショット | Z を押している間 `ShotIntervalTicks` ごとに上へ直進弾（低速中は幅を狭めて集中）。ボスの円への接触は掃引で判定し、体力を減らす |
| 被弾 | 敵弾と自機の判定円を **Tick 中の相対軌跡の掃引**で判定（026 の `Sweep.First`）。被弾で残機 -1・画面の敵弾を全消去・`InvincibleTicks` の無敵（被弾した Tick を含めて数える `Countdown`）。**同じ Tick に複数の弾に触れても 1 回**、無敵中は無視 |
| ボム | X で 1 個消費（初期 3、被弾・再挑戦で 3 に戻す）。敵弾を全消去し、`BombTicks` の間無敵＋ボスへ継続ダメージ。ボム中の再使用は不可 |
| 残機 | 初期 3。0 になった Tick で敗北 |
| 弾幕 | 各弾幕は体力と制限時間を持つ。体力 0 または時間切れで突破（時間切れはボーナスなし）、敵弾を全消去し `IntervalTicks` 後に次の弾幕。被弾・ボムなしで突破したら弾幕ボーナス |
| 進行 | ボス 1（3 弾幕）→ ボス 2（3 弾幕）→ 勝利。敗北後の「このボスから再挑戦」は、そのボスの 1 弾幕目・残機 3・ボム 3・得点はボス開始時の値で、**新しいシード**で始め直す（1 試合 = 1 シード） |
| 弾数上限 | 敵弾プールは固定容量 `MaxBullets`（既定 2048）。満杯のときの発射は **捨てて数える**（`DroppedBullets`）。例外にしない・配列を伸ばさない。領域外（余白 32 px）に出た弾・寿命切れの弾は消える |
| 乱数 | 1 試合 = 1 シードの xorshift。DSL の `rand` は弾幕ごと・発射器ごとに系列を分ける（015 の知見） |
| 終了後 | 勝利・敗北後の Tick と入力は状態を変えない |

## 弾道 DSL（セッション1）

行単位の小さなテキスト言語。1 ファイル = 1 ボス（3 弾幕）。数値リテラルは **文字列から直接固定小数点へ変換**（`float.Parse` を使わない）。式は持たず、値は「数値」「`rand(a, b)`」「`aim`（自機の向き）」に限る。

```
boss "Ember Warden" radius=24
pattern "Cinder Bloom" hp=900 time=40s
  move to 192 120 over 60
  emit ring every=20 count=24 speed=1.8 spin=7
  emit aimed every=45 count=5 spread=30 speed=3.0 accel=0.02 maxspeed=4.5
pattern "..." hp=... time=...
  move wander rect=96,80,288,160 every=120
  emit spiral every=4 arms=3 speed=2.2 spin=11 curve=0.4 shape=rice color=blue
```

- 発射器：`ring`（全周）・`fan`（扇）・`aimed`（自機狙いの扇）・`spiral`（腕 × 回転）。共通引数：`every`・`delay`・`count`/`arms`・`speed`・`accel`・`maxspeed`・`curve`（角速度）・`spin`（発射ごとの回転）・`angle`・`spread`・`offset`（ボスからの位置）・`shape`・`color`・`life`
- ボスの動き：`move to x y over N`・`move wander rect=… every=N`
- パーサの出力は不変の `BossScript`（`PatternSpec` → `MoveSpec` / `EmitterSpec`）。**値の範囲と組み合わせを検証**（count×発射頻度×寿命の見積もりが `MaxBullets` を大きく超える定義も警告ではなくエラーにはしない。上限は実行時の捨てる処理で守る）
- エラーは行・列つきの `ScriptError`（未知の語・必須引数なし・引数の重複・範囲外・型違い・pattern の外の emit・ボスあたり 3 弾幕でない）

## 弾の運動とプール（セッション1、データ指向）

- `BulletPool`：固定容量の SoA（`int[] x, y, angle, speed, accel, maxSpeed, curve, age, life` と `byte[] shape, color`）。`Spawn` は満杯なら false。`Step` は全弾を 1 Tick 進め、消える弾は末尾と入れ替えて詰める（順序は決定的）。`Step` で割り当てをしない
- 1 Tick の運動：`angle += curve` → `speed = clamp(speed + accel, maxSpeed)` → `pos += speed × dir(angle)`（Q14 の表、四捨五入の割り算）
- `Trajectory.At(spec, t)`：弾 1 発の参照実装（1 発ずつ進める素朴な版）と、**直進・等加速は閉じた式**（`o + Σ speed_k × dir`）

## 試合（セッション2）

- `VeilRun`：試合 1 回。1 Tick の順序＝時間（無敵・ボム・連射・弾幕の残り時間）→ 自機の移動 → ショット発射 → ボム → ボスの移動 → 発射器（DSL の評価）→ 敵弾の移動 → ショット×ボス → 敵弾×自機 → 弾幕の突破判定 → 勝敗。`Step(PlayerCommand)`・`Hash()`（tile-motion の `StateHash`、プールの全弾・各 `Countdown`・乱数の状態まで混ぜる。022 の知見）・`Clone()`
- `VeilConfig`：公開フィールド＋検証（有限・範囲・`MaxBullets`・自機の半径と速さ・`InvincibleTicks`・`BombTicks`）。弾幕の内容は `BossScript` 2 つ
- 流用：026 の `Geometry.cs`（`Vec2i`・`IntMath`・`Sweep.First`・`Touch`）、`SeededRandom.cs`。tile-motion の `Countdown`・`StateHash`

## App（セッション2）

- `VeilFlow`：Title / Playing / Paused / Result、実時間 → 固定 Tick、フレーム単位の Tick 上限（026 `SiegeFlow` を土台に）。フォーカス喪失で保留入力も消して停止。結果画面の選択肢（このボスから／最初から／タイトル）。再挑戦・再戦は新しいシード
- `Intent`・`HeldControls`（026 から流用）：移動 4 方向・低速・ショット・ボム（ボムは押した瞬間の 1 回）
- `Autopilot`：`VeilRun.Clone()` で 9 方向 × 低速/通常を数十 Tick 先読みし、被弾しない・弾から遠い候補を選ぶ。常にショット、ボムは使わない。判断の状態は試合・弾幕が変わったら捨てる（023）。先読みは数 Tick ごと

## テスト（題材固有を優先）

- **弾道式の軌跡**：直進・加速（`maxspeed` で頭打ちする Tick の前後）・角速度・`spin` の発射ごとの回転で、プールの各 Tick の位置が `Trajectory.At` と一致。直進・等加速は閉じた式とも一致。角度表の対称性と `Math.Sin` との差。`ring` の 24 発が等間隔、`aimed` の中心が自機の方向
- **無敵中の二重被弾なし**：同じ Tick に 2 発 → 残機 -1 だけ。無敵の最後の Tick と切れた直後の Tick の境界。ボム中の接触。被弾とボムが同じ Tick
- **弾数上限で破綻しない**：容量を超える発射を長時間続けても数 ≤ 容量・`DroppedBullets` が増える・例外なし・空きが出たら再び出る。容量ちょうど・1 超え。`Step` の割り当て 0（.NET の `GC.GetAllocatedBytesForCurrentThread`。Mono は PlayMode の ProfilerRecorder で確かめる）
- **DSL**：6 弾幕のファイルがすべて通る。エラーの種類ごとに行・列が合う負テスト。小数の固定小数点変換の境界（`0.004`、桁の多い小数、負数）
- **6 パターンを再現しクリア可能**：操縦役で固定シードの通しが勝利し、各弾幕を体力 0 で突破。最終ハッシュを .NET と Mono で照合。各弾幕の最大同時弾数を記録（容量の何割か）
- 共通：状態遷移（開始→進行→終了→再挑戦→再戦、終了後の入力無視）、負テスト（NaN・範囲外の設定、終了後の Step、ボムの重複押下、ボム 0 での使用）、再現性（同シード同入力で同ハッシュ）、「このボスから再挑戦」の初期状態（残機・ボム・得点・弾幕番号）

## Playable（セッション3、Unity 層）

- `LanternVeilGame`（入力・Flow・HUD・音）、`LanternVeilStage`（Stage Prefab：プレイ領域の枠と背景・自機・判定点・ボス 2 体・ショット）、`LanternVeilSettings`（ScriptableObject：速さ・無敵・ボム・`MaxBullets`・音量・DSL の TextAsset 2 つ）
- 敵弾の描画：**1 つの動的 Mesh（四角形 × 容量）を MeshRenderer で描く**。毎フレーム SoA から頂点を書き、形・色はアトラスの UV で選ぶ。割り当てなし。GameObject を弾ごとに作らない（003 の知見）
- DSL ファイルは `Assets/Game/Patterns/*.txt`。Core テストは .NET と Unity の両方からファイルを読む補助（テストのディレクトリと作業ディレクトリから上へ `Assets/Game/Patterns` を探す）を置く
- HUD（PanelRenderer）：右側にスコア・残機・ボム・ボス名・弾幕名・残り時間・弾数、プレイ領域の上にボスの体力。タイトルに操作説明、結果画面に勝敗・スコア・到達弾幕・ボタン（このボスから／最初から／タイトル）
- Editor：`LanternVeilArtGenerator`（生成ドット絵：自機・ボス 2 体・弾のアトラス・背景）、`LanternVeilSceneBuilder`（既存 Prefab・設定アセットは上書きしない）
- 音：ショット（間引き）・被弾・ボム・弾幕突破を控えめに、`AudioPolicy.ShouldMute` で消音
- 撮影台本：タイトル・6 弾幕それぞれ・低速の判定表示・被弾と無敵・ボム・弾数上限に張り付いた場面・結果（勝利／敗北）・再挑戦の開始

## Verification（セッション4）

- PlayMode：矢印で動く／Shift で低速と判定表示／Z でボスの体力が減る／X でボムと弾消し／被弾の表示／結果画面のボタン（このボスから・最初から）／タイトルへ戻った表示が起動直後と一致（021）／一時停止・フォーカス喪失／設定アセットの検証と DSL の読み込み（024）
- 負荷計測：各弾幕の最大同時弾数、`MaxBullets` いっぱいの設定で Core の Step と Mesh の更新の p50/p95・GC 割り当てを記録（対象機・条件を README に）
- `dev build 027` → `dev capture 027` → 同一ビルドの再撮影で SHA-256 一致 → README・`evidence/README.md`・`progress.csv` を「検証待ち」

---

## 主なファイル

- 新規：`games/027-bullet-hell/**`、`plan/games/027-bullet-hell-implementation.md`
  - Core：`Geometry.cs`（026 から）・`SeededRandom.cs`・`Trig.cs`（角度表）・`Fixed.cs`（小数リテラル → 固定小数点）・`Dsl/Lexer.cs`・`Dsl/Parser.cs`・`BossScript.cs`・`BulletPool.cs`・`Trajectory.cs`・`Emitter.cs`・`VeilConfig.cs`・`VeilRun.cs`
  - App：`Intent.cs`・`HeldControls.cs`・`VeilFlow.cs`・`Autopilot.cs`
  - Patterns：`boss1.txt`・`boss2.txt`
- 流用元：
  - `games/026-twin-stick/Assets/Game/Core/Geometry.cs`・`SeededRandom.cs`、`Assets/Game/App/SiegeFlow.cs`・`HeldControls.cs`・`Intent.cs`・`Autopilot.cs`
  - `packages/com.games100.tile-motion`（`Countdown`・`StateHash`）
  - 026 の SceneBuilder・ArtGenerator・CaptureScenario・HUD の構成
- `Geometry.cs`（円の掃引）は 026・027 の 2 作品目。もう 1 作品で使ったら `packages/` へ抽出する候補として `plan/TODO.md` に書く

## 検証

- 各セッションの終わりに `dev lint` → `dev check` → `dev test`、Mono での Core/App テスト（`dev test 027 -Unity`）
- Unity 層を触ったら `dev test 027 -Unity -Mode PlayMode`
- `dev build 027` → `dev capture 027` → 証跡を確認 → 「検証待ち」で人間に渡す（`dev run 027`）。難しさ（弾速・密度・自機の速さ・判定の大きさ）とボムの数は試遊で調整

---

## セッション1の結果（2026-11-23）

- ルール表と DSL の文法は [README](../../games/027-bullet-hell/README.md) の「採用ルール」「弾道 DSL」を正とする。
- Core：`Field`（領域と単位）・`Trig`（整数の角度表と atan2）・`FixedPoint`（小数の文字列 → 整数の単位）・`BulletPool`（SoA の固定容量の置き場）・`BossScript`（台本の不変の型）・`ScriptParser`（字句・構文・単位の変換・行と列付きの誤り）・`PatternPlayer`（ボスの動きと発射器）。026 から `Geometry`（`Vec2i`・`IntMath`・`Rect`・`Sweep`）と `SeededRandom` を写した（`View` は除いた）。
- Core 119 件が .NET で、tile-motion の 10 件を含む 129 件が Mono で通った。角度表の検査値は両環境で一致。
- 計画との差：
  - sin / cos の表はソースに数値を並べず、起動時に整数だけのテイラー展開（Q30）で作った。`Math.Sin` を使わないので両環境で同じ表になる。
  - 角度は 1 周 = 65536（表は 4096 分割で、角度の下位 4 ビットを捨てて引く）。0.4 度/Tick のような小さい角速度を丸めすぎないため。
  - 弾の内部の位置を 1 px = 2^30 にし、速さ × cos を丸めずに足す。直進・等加速の弾は閉じた式とちょうど一致する。
  - 弾の参照実装（1 発ずつ進める版）は Core の `Trajectory` ではなくテスト側（`TestKit.ReferenceBullet`）に置いた（016 の知見：速い実装をテストの素朴な実装と照合する）。
  - 発射器の `spiral` は `ring` ＋ `spin` と同じなので作らなかった。
  - DSL の引数はすべて `名前=値` の形にそろえた（`move to at=192,360 over=60`）。弾幕の数（3 つ）はパーサではなくセッション2の設定の検証で確かめる。
- 次（セッション2）：試合（`VeilRun`：自機・ショット・ボム・被弾と無敵・残機・弾幕の進行・再挑戦）、`VeilConfig`、App（`VeilFlow`・`Autopilot`）、6 弾幕の台本ファイル（`Assets/Game/Patterns/`）。

## セッション2の結果（2026-11-23）

- Core：`VeilConfig`（設定と台本の組の検証）・`VeilRun`（試合：自機・ショット・ボム・被弾と無敵・残機・弾幕の間と進行・勝敗・再挑戦の始め方・ハッシュ・複製）。App：`Intent`・`HeldControls`（ボムは押すごとに 1 回）・`VeilFlow`（画面遷移・時刻と Tick・「このボスから再挑戦」）・`Autopilot`。台本：`Assets/Game/Patterns/boss1.txt`・`boss2.txt`（6 弾幕）。
- Core 146・App 25 が .NET で、tile-motion を含む 181 件が Mono で通った。シード 1 の自動操縦の通しの最終ハッシュ `0x3364D004` は .NET と Mono で一致。
- 自動操縦の通し（8 シード）：全勝・被弾なし、48 弾幕のうち 47 を体力で突破（1 つは時間切れ）。弾幕ごとの最大同時弾数は約 185・295・106・333・122・265 発（容量 2048 の 16% 以下）、.NET で 1 試合 1〜2 秒。
- テストで見つけて直したこと：
  - 試合の初めの弾幕の間に、ボスの体力と残り時間が 0 のままだった（HUD に 0 と出る）。弾幕の間に入るときに次の弾幕の値を入れる `EnterInterval` にまとめた。
  - 弾幕の間に使ったボムが弾幕の始まりに続いていても、ボーナスの条件（ボムなし）が満たされたままだった。始まりにボムが続いていればボムを使ったことにする。
- 計画との差：
  - 026 の `Sweep.First` は、この作品の座標（1 px = 256、領域 384×448 px）だと遠い組で 2 次式が long を超える。`VeilRun.Touches` で、各軸の隔たりが「移動量と半径の和」を超える組を先に除き（厳密）、それでも座標が大きいときだけ縮めて判定する。
  - 自動操縦は `VeilRun.Clone()` で試合ごと先読みせず、敵弾の置き場だけを作業用の置き場へ写して先読みする（`BulletPool.CopyFrom`、割り当てなし）。新しく撃たれる弾は先読みの長さの間に届かない。
  - 自動操縦は最初「弾との隙間を最大にする」を優先して、ほぼ全弾幕が時間切れになった。「触れず隙間 4 px 以上なら位置（ボスの真下）で選ぶ」に変えた。
  - 6 つ目の弾幕は、ゆるく曲がる輪（角速度 0.25〜0.3 度/Tick）が画面の下を横切り、真下に入れず時間切れが続いた（発射器を 1 つずつ外して確かめた）。角速度を 0.6 度/Tick にして輪をボスの近くで巻かせ、下向きの扇を偶数発にして真下を空けた。
- 次（セッション3）：Unity 層（生成ドット絵・Stage Prefab・敵弾の一括描画の Mesh・HUD・音・`LanternVeilSettings`・シーン生成・撮影台本）。

## セッション3の結果（2026-11-23）

- Unity 層：`LanternVeilGame`（入力・カメラ・HUD・音）、`LanternVeilStage`（Stage Prefab 1 つ）、`BulletLayer`（敵弾を 1 つの Mesh で描く）、`LanternVeilSettings`（設定＋台本の TextAsset 2 つ）、生成スクリプト（ドット絵・弾のアトラス・Stage Prefab・シーン）、撮影台本 14 枚（タイトル・6 弾幕・勝利・低速の当たり判定・被弾・ボム・一時停止・負け・再挑戦）。
- Windows ビルドと `dev capture 027` が通り、14 枚を確かめた。
- 撮影で見つけて直したこと：
  - 敵弾が 1 発も描かれなかった。`Mesh.SetVertices(配列, 開始, 長さ)` が頂点の数をその長さに縮め、三角形の番号が範囲外になっていた。容量ぶんの配列をそのまま渡す。
  - 余白（領域の外 32 px）にいる弾が枠の外に見えた。領域の外を覆う 4 枚の幕を Stage に足した。
- 計画との差：弾数上限に張り付いた場面の撮影はやめた（本番の弾幕は最大 333 発で容量 2048 に届かない）。上限は Core のテスト（容量 64 で本番の台本）で確かめ、セッション4の負荷計測で容量いっぱいの描画を測る。
- 次（セッション4）：PlayMode 実シーン（キー操作・低速と判定表示・ショット・ボム・被弾の表示・結果のボタンと再挑戦・タイトルへ戻った表示・一時停止・フォーカス・設定アセットと台本の読み込み）、負荷計測（容量いっぱいの弾で Step と Mesh の更新の p50/p95・割り当て）、同一ビルドの再撮影、evidence/README、「検証待ち」。

## セッション4の結果（2026-11-23）

- PlayMode 実シーン 17 件（共通の証跡公開 16 件と合わせて 33 件）が通った。弾数の上限いっぱい（2048 発）で Core の Step p95 0.388 ms、弾の Mesh の更新 p95 0.365 ms、割り当て 0。
- 同じビルドで 2 回撮影し、14 枚の SHA-256 が一致した。`evidence/README.md` を書き、状態を「検証待ち」にした。
- 見つけて直したこと：
  - 描画の更新で `ReadOnlyCollection` を `foreach` で回していて、列挙子の箱詰めで割り当てが起きていた。番号で回す。
  - タイトルへ戻ったとき、表示中のショットの数（`VisibleShots`）を 0 に戻していなかった。
- 計画との差：Mono の `GC.GetAllocatedBytesForCurrentThread` は常に 0 を返し、割り当ての検査が何も確かめていなかった（計数が働くかを 4096 バイトの割り当てで確かめて気づいた）。Unity では `ProfilerRecorder` の `GC Allocated In Frame` で、何もしないフレーム・計測のフレーム・4096 バイトのフレームを比べる。完了時に AGENTS.md の知見へ移す候補。
- 試遊で見てほしい点：難しさ（弾幕の密度・弾速・体力・制限時間）、ボムの数、低速の速さ、当たり判定の点の見やすさ、音量。
