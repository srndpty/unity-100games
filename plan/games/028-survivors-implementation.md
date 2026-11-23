# 028 自動攻撃サバイバー 実装計画

## Context

- 028 自動攻撃サバイバー（難易度 3・Phase 2）に着手する。仕様書 [plan/games/028-survivors.md](028-survivors.md) の中心課題は「**敵・武器をデータ定義**」、重点は「**近傍探索と更新頻度を制御**」。
- In scope：武器 5 種、敵密度の増加、レベルアップ選択、ボスまたは時間生存で勝ち、タイトル→操作説明→結果→再プレイ。Out of scope：大規模メタ進行・多数キャラ・素材量産。
- 題材固有テスト：経験値の重複獲得なし／強化後の武器値反映／上限敵数で更新が安定。完了判定：通しプレイが完走、**敵数別フレーム時間を記録**。
- ユーザーと決めたこと：
  - 敵・武器の定義は **ScriptableObject**（Unity 層のアセット → Core の素の定義クラスへ写す。検証は Core）
  - **5 分＋最後にボス**（仕様書の「10 分」からの変更として README に記録。長さは設定の Tick 数）。ボスを倒せば勝利、体力 0 で敗北
  - **無限の平面＋追従カメラ**。遠く離れた敵は自機の前方へ置き直す
  - レベルアップの 3 択は **1/2/3 キー・矢印＋Enter・クリックのどれでも**。選択中は試合の Tick が止まる
- 作品名はオリジナル（仮）：**THORN TIDE**。参考作品の名称・キャラ・武器名・演出は使わない。
- 作業は `main` から `feature/game028` を切って行う。

---

## セッション分割（区切りを progress.csv の記録/メモに残す）

| セッション | 内容 | 終わりの状態 |
|---|---|---|
| 0（済） | 共通パッケージの抽出（下記）、`dev new 028`・雛形と slnx 登録の最小コミット、この計画を `plan/games/028-survivors-implementation.md` に置く | `dev lint/check/test` 通過 |
| 1 | ルール表を README に固定。Core（定義と検証・近傍探索・敵・武器・経験値・試合）＋ App とテスト | 題材固有テストが .NET と Mono で通る、操縦役で完走 |
| 2 | Playable（定義アセット・Stage Prefab・一括描画・HUD・3 択 UI・音・シーン生成・撮影台本） | 人間が通しで遊べる |
| 3 | Verification（PlayMode・敵数別の負荷計測・ビルド・`dev capture`・README/evidence） | 状態「検証待ち」 |

### セッション0：共通パッケージ `com.games100.int-geometry`

- `Vec2i`・`IntMath`・`Rect`・`Touch`・`Sweep.First`（`Geometry.cs`）、`Trig`（整数の角度表）、`SeededRandom` が 025〜027 に写されている（3 題以上）。AGENTS.md の方針どおり `packages/com.games100.int-geometry` に抽出し、**028 から使う**（tile-motion と同じく既存作品は書き換えない）。
- 抽出元：`games/027-bullet-hell/Assets/Game/Core/Geometry.cs`・`Trig.cs`・`SeededRandom.cs` とそのテスト。`noEngineReferences`、`dotnet~/` の csproj、`testables`。AGENTS.md の「構成」にパッケージの説明を 1 行足す。
- tile-motion の `Units`・`Countdown`・`StateHash` も参照する。

---

## ルール表（セッション1で README に固定する要点）

| 項目 | 採用ルール |
|---|---|
| 単位 | 位置・速さは整数（1 セル = 256 単位）。60 Tick/秒、時間の設定は Tick 数。5 分 = 18000 Tick。浮動小数点を使わず Mono と .NET で一致させる |
| フィールド | 無限の平面（座標は int、5 分の最大移動でも桁に余裕があることを設定の検証で保証）。視野 `View.Of(自機, 視野の大きさ)` は自機中心で、カメラと同じ関数 |
| 自機 | WASD/矢印で 8 方向移動（斜めは `×181/256`、最低 1）。向き（最後に動いた方向）を持つ。体力制、敵に触れると敵ごとの接触ダメージ、その後 `InvincibleTicks` 無敵（被弾した Tick を含めて数える `Countdown`） |
| 敵の定義 | `EnemyDef`：体力・速さ・半径・接触ダメージ・落とす経験値・**行動の種類**（`Chase`＝自機へ向かう／`Swarm`＝出現時の自機方向へ直進して通り過ぎる／`Boss`）。種類の振る舞いはコード、数値はデータ |
| 出現 | `SpawnTable`：経過時間の区間ごとに「目標の生存数」「出現間隔」「種類の重み」。生存数が目標を下回る分だけ、視野の外周リング（視野の外・`SpawnRing` 以内）に乱数で出す。**生存数は常に `MaxEnemies` 以下**（満杯のときは出さず、予算は上限 1 回分で頭打ち） |
| 置き直し | 自機から `DespawnDistance` 以上離れた敵（ボス以外）は、体力を保ったまま自機の進行方向側の出現リングへ置き直す（総数を増やさない） |
| 更新頻度 | 敵の向きの計算（自機への正規化）は `SteerIntervalTicks` ごと、**敵の番号で位相をずらして**毎 Tick 一部だけ更新。移動は毎 Tick 直前の向きで行う。敵どうしの押し合いは近傍グリッドで、1 体あたり見る近傍の数に上限 `SeparationNeighbors` |
| 近傍探索 | `SpatialGrid`：自機中心の一様グリッド（セル幅は設定）。毎 Tick 計数ソートで固定長配列に詰め直す（Step で割り当て 0）。問い合わせは「最も近い敵」「円内の敵」「近い順に k 体」 |
| 武器の定義 | `WeaponDef`：種類（下記 5 種）とレベル 1〜`MaxLevel` の値の表（ダメージ・間隔・数・範囲・速さ・貫通・持続）。**レベル表の値をそのまま使う**（倍率計算をしない）ので「強化後の値」が表と照合できる |
| 武器 5 種 | **Bolt**（最も近い敵へ弾。数・貫通がレベルで増える）／**Orbit**（自機の周りを回る刃。`Trig` の角度表）／**Pulse**（自機中心の円の範囲へ一定間隔でダメージ）／**Lash**（向きの方向へ矩形の斬撃）／**Arc**（最も近い敵から近い順に k 体へ連鎖。各段の距離に上限）。名前は仮 |
| 当たり判定 | 弾×敵・自機×敵は Tick 中の相対軌跡の掃引（`Sweep.First`）。範囲攻撃（Pulse・Lash・Orbit）は発動 Tick の位置で判定。同じ攻撃が同じ敵に当たるのは 1 回（Orbit は敵ごとの再ヒット待ち Tick） |
| 経験値 | 撃破で敵の位置に宝石（固有 ID・値）。自機の `PickupRadius` に入ると吸い寄せ、触れた Tick に**1 回だけ**加算して即座に消す。宝石の数が `MaxGems` を超えたら、最も古い宝石の値を最も近い宝石へ足して消す（**合計値を保存**） |
| レベルアップ | 経験値の必要量の表。到達したら試合を止めて 3 択を出す（専用の乱数系列）。候補＝未所持の武器の取得 or 所持武器の次レベル。最大まで上がったら「体力回復」を候補に入れる。1 回の取得で複数レベル上がったら 3 択を順に出す（保留数で持つ） |
| ボス | 18000 Tick で出現（通常の出現は続ける）。倒したら勝利。体力 0 で敗北 |
| 乱数 | 1 試合 = 1 シードの xorshift。出現・3 択・武器の散らばりで系列を分ける（015） |
| 終了後 | 勝利・敗北の後の Tick と入力は状態を変えない。3 択中の移動入力・Step は拒否 |

---

## セッション1：Core / App

### Core（`Assets/Game/Core/`、int-geometry と tile-motion を参照）

- `EnemyDef`・`WeaponDef`（`WeaponLevel[]`）・`SpawnTable`・`TideConfig`：公開フィールド＋検証（有限・範囲・レベル表の長さと単調性・`MaxEnemies`・弾数の見積もり（間隔と寿命と数）が `MaxProjectiles` 以下・視野と出現リングの大小・区間表が 5 分を覆う・座標の桁）。既定値の定義をコードに持ち、テストもそれを使う
- `SpatialGrid`：計数ソートのバケット、問い合わせ 3 種。参照実装（全探索）と乱数配置で照合
- `EnemySet`：SoA（位置・向き・体力・種類・再ヒット待ち）、固定容量、削除は末尾と入れ替え（順序は決定的）。`Steer`（位相ずらし）・`Move`・`Separate`・`Relocate`
- `Weapons`：5 種の発動（所持レベルの値を表から引く）、`ProjectileSet`（固定容量 SoA）
- `GemSet`：ID 付きの宝石、吸い寄せ・取得・統合
- `LevelUp`：必要量の表・保留数・3 択の生成・`Choose(i)`
- `TideRun`：試合。1 Tick の順序＝時間 → 自機の移動 → グリッド再構築 → 敵の向き（位相分の一部）→ 敵の移動と押し合い → 武器の発動 → 弾の移動と弾×敵の掃引 → 撃破と宝石 → 自機×敵の掃引 → 宝石の取得 → 置き直し → 出現 → ボス・勝敗。`Step(Command)`・`Choose(int)`・`Hash()`（全 SoA・`Countdown`・乱数・保留数まで混ぜる、022）・`Clone()`

### App（`Assets/Game/App/`）

- `TideFlow`：Title / Playing / LevelUp / Paused / Result。実時間 → 固定 Tick、フレーム単位の Tick 上限（026 `SiegeFlow` を土台）。LevelUp 中は Tick を進めず選択の意図だけ受け付ける。フォーカス喪失で保留入力も消す。遊んだ時間は `unscaledDeltaTime`（017）
- `Intent`・`HeldControls`（026 から流用）＋ 選択の意図（1/2/3・上下・決定・クリックの番号）を 1 本の列で処理（012）
- `Autopilot`：近傍グリッドで周囲の敵の密度が低い方へ動き、宝石へ寄る。3 択は「未所持の武器 → 低いレベル」の固定の好み。試合ごとに判断状態を捨てる（023）

### テスト（題材固有を優先）

- **経験値の重複獲得なし**：同じ Tick に自機が複数の宝石に触れる・同じ宝石に 2 回目の接触・統合直後の宝石・吸い寄せ中の撃破で、「取得した経験値の合計 = 消えた宝石の値の合計」「宝石 ID は 1 度しか加算されない」。1 回で複数レベル上がる量を取ると 3 択がその回数出る。3 択中に Step しても経験値が動かない
- **強化後の武器値反映**：各武器を Lv1 → Lv2 → … と `Choose` で上げ、次の発動のダメージ・数・範囲・間隔が `WeaponDef` の表の値と一致（敵の体力の減り方・弾の数・発動 Tick で確認）。最大レベルの武器は候補に出ない
- **上限敵数で更新が安定**：`MaxEnemies` に張り付けたまま長く進め、毎 Tick「生存数 ≤ 上限」「重なりの解消で位置が発散しない（自機からの距離が上限内）」「Step の割り当て 0（.NET）」。位相ずらしで各敵が `SteerIntervalTicks` ごとにちょうど 1 回向きを更新されること。グリッドの問い合わせが全探索と一致
- 出現：区間ごとの目標数、視野の外にだけ出る、満杯から空きが出ても予算が頭打ち。置き直しで総数・体力が変わらない
- 掃引：速い弾がすり抜ける位置関係でも当たる、貫通数の境界
- 共通：状態遷移（開始→進行→LevelUp→進行→終了→再戦・終了後の入力無視）、負テスト（NaN・範囲外・表の長さ不一致・未定義の選択番号・3 択外での `Choose`・終了後の Step・重複した選択の押下）、再現性（同シード同入力で同ハッシュ）、**完走**（Autopilot で 20 シードが上限 Tick 以内に勝利または敗北。固定シードの最終ハッシュを .NET と Mono で照合）

---

## セッション2：Playable（Unity 層）

- 定義アセット：`EnemyDefinition`・`WeaponDefinition`（ScriptableObject、レベル表を Inspector で編集）と `ThornTideSettings`（それらの一覧・出現表・自機・音量）。Unity 層が Core の `TideConfig` へ写して検証する。生成スクリプトは既存アセットを上書きしない（コードの既定値を変えたら `.asset` も直す、024）
- `ThornTideGame`（入力・Flow・HUD・音）、`ThornTideStage`（Stage Prefab：床のタイル・自機・ボス・武器の見た目の見本）
- 大量の敵・宝石・弾は 027 の `BulletLayer` と同じく **1 つの Mesh に四角形を詰めて描く**（容量ぶんの四角形を用意し使わない分は大きさ 0。`SetVertices` は配列ごと渡す、027）。毎フレームの割り当て 0、`ReadOnlyCollection` を foreach しない
- カメラ：`View.Of` の中心へ追従。床は自機の位置でタイルの UV をずらす
- HUD（PanelRenderer）：残り時間・レベルと経験値バー・体力・撃破数・所持武器とレベル。3 択のカード（武器名・レベル・次の値の説明。1/2/3・矢印＋Enter・クリック）。フォーカス復帰直後のクリックは捨てる（010）。タイトルに操作説明、結果画面（勝敗・生存時間・レベル・撃破数）
- 音：撃破・取得・レベルアップは間引いて控えめ、`AudioPolicy.ShouldMute` で消音
- Editor：`ThornTideArtGenerator`（生成ドット絵）、`ThornTideSceneBuilder`（Stage Prefab・定義アセット・シーン）
- 撮影台本：タイトル・開始直後・各武器の発動・3 択・敵が密な場面・ボス・勝利・敗北（試合を分けてよい、026）

## セッション3：Verification

- PlayMode：WASD の移動、3 択をキー・クリック両方で選ぶ（ポインタイベント、013）、選択中に時間が止まる、HUD のボタン、結果 → 再戦、タイトルへ戻った表示が起動直後と一致（021）、一時停止・フォーカス喪失、設定アセットの検証
- **敵数別のフレーム時間**：敵 100 / 200 / 400 / `MaxEnemies` で Core の Step と描画の更新の p50/p95、割り当て（`ProfilerRecorder`、027）、生成物数を記録。対象機・測定条件も書く（003/008）
- `dev build 028`、`dev capture 028`、同一ビルドの再撮影の一致
- README（環境・操作・ルール表・データ定義の流れ・物理エンジンを使わないこと・仕様書からの変更（5 分）・未実装範囲）、`evidence/README.md`、`progress.csv` を「検証待ち」へ

---

## 主なファイル

- 新規：`packages/com.games100.int-geometry/**`、`games/028-survivors/**`、`plan/games/028-survivors-implementation.md`
- 流用元：
  - `games/027-bullet-hell/Assets/Game/Core/Geometry.cs`・`Trig.cs`・`SeededRandom.cs`（パッケージへ）、`BulletPool.cs`（SoA・末尾入れ替え）、`Assets/Game/Unity/BulletLayer`（一括描画）
  - `packages/com.games100.tile-motion`（`Units`・`Countdown`・`StateHash`）
  - `games/026-twin-stick/Assets/Game/App/SiegeFlow.cs`・`HeldControls.cs`・`Intent.cs`・`Autopilot.cs`、`Core/SiegeRun.cs`（出現の予算・視野）、追従カメラ
  - 026/027 の SceneBuilder・ArtGenerator・CaptureScenario

## 検証

- 各セッションの終わりに `dev lint` → `dev check` → `dev test`
- Unity 層を触ったら `dev test 028 -Unity` と `dev test 028 -Unity -Mode PlayMode`
- `dev build 028` → `dev capture 028` → 証跡を確認 → 「検証待ち」で人間に渡す（`dev run 028`）
