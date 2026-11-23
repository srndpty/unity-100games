# 030 ステルスアクション 実装計画

## Context

- 030 ステルスアクション（難易度4・Phase 2）が未着手。仕様書 [plan/games/030-stealth.md](./030-stealth.md) の中心課題は「**知覚センサと意思決定を分離**」、重点は「**巡回→調査→追跡→捜索 FSM**」。In scope：警備員3体と1施設、視界・足音・警戒レベル、目的物回収で脱出、タイトル→操作説明→結果→再プレイ。
- 題材固有テスト：遮蔽物越しに視認しない／音だけで即発見としない／見失った後に警戒が減衰。完了判定：見つからず／見つかって両経路でクリア可能、敵状態をデバッグ表示。
- ユーザーと決めたこと：**2D 見下ろし（タイル地図＋生成ドット絵）**、**追跡中の警備員に触れられたら捕獲＝失敗**（振り切れば捜索→減衰で戻る。目的物を持って出口に着けば追跡中でもクリア）、自機は**歩き／忍び足／走り**のみ（おびき寄せ・気絶は入れない）、**1 画面の施設**。

作品名はオリジナル（仮）：**HUSH WARD**。参考作品の名称・素材・演出は使わない。
この計画は着手時に `plan/games/030-stealth-implementation.md` として保存し、各セッションの結果を追記する（025〜029 と同じ）。

---

## セッション0：準備

- `git switch -c feature/game030`（main から）→ `dev new 030`（2D テンプレート）→ 雛形と slnx 登録だけを最小コミット（指示を待たずに可）。
- `Packages/manifest.json` に tile-motion・int-geometry と `testables`、`dotnet/G030.Core.csproj` から両パッケージを ProjectReference。AGENTS.md の両パッケージの行に 030 を追記。
- `dev lint/check/test` 通過。

---

## セッション分割（progress.csv の記録/メモに区切りを残す）

| セッション | 内容 | 終わりの状態 |
|---|---|---|
| 0（済） | 上記 | `dev lint/check/test` 通過 |
| 1 | ルール表を README に固定、Core（知覚・判断・施設）＋ App とテスト | Core/App テスト通過（.NET と Mono） |
| 2 | Playable（生成ドット絵・Stage Prefab・視界の扇の描画・デバッグ表示・HUD・音・シーン生成・撮影台本） | 人間が通しで遊べる |
| 3 | Verification（PlayMode・ビルド・`dev capture`・再撮影の一致・README/evidence） | 状態「検証待ち」 |

---

## セッション1：ルール表（README に固定）と Core / App

### ルール表（要点）

| 項目 | 採用ルール |
|---|---|
| 単位 | 1 タイル = 256 単位の整数（tile-motion `Units`）、60 Tick/秒、時間・距離の設定は Tick 数と単位。浮動小数点を使わない |
| 施設 | 32×18 タイルの 1 画面（上端は HUD の帯）。文字の地図：壁（移動・視線・音を遮る）、木箱（移動・視線を遮る、音は通す）、床、入口兼出口、目的物（機密ファイル）、警備員の巡回点 |
| 自機 | 8 方向移動（斜め 181/256、最低 1。022 の知見）。**歩き**（既定）／**忍び足**（C 押下中、遅い・足音小）／**走り**（Shift 押下中、速い・足音大）。壁・木箱は `BodyMotion` で止まる |
| 足音 | 移動した Tick ごとに `StepTicks` 間隔で音の出来事（位置・届く距離）。届く距離：忍び足 1.5 タイル・歩き 3・走り 7（既定）。止まっている間は出ない |
| 聴覚 | 音源から**床と木箱を通る経路の BFS 距離**（壁を回り込む）が届く距離以内の警備員だけが聞く。壁越しの直線距離では聞かない |
| 視覚 | 警備員の位置から自機の中心へ、距離 ≤ 視程、向きとの角 ≤ 半視野角（整数の内積・外積で判定、`Trig` の向き）、かつ**視線上のマス（supercover、斜めの角をかすめる場合は両側のマス）に壁・木箱がない**。毎 Tick、移動後の位置で 1 回判定。走りの 1 Tick 移動はタイル未満と検証で保証 |
| 気づき | 警備員ごとの気づき値 0〜1000。見えている Tick は距離に応じて増える（近いほど速い）。**1000 に達したら発見（追跡）**。見えていない Tick は減衰 |
| 判断 FSM | **巡回**（巡回点を順に歩く・各点で見回す）→ 見えたが未発見、または音を聞いた → **調査**（その位置へ歩き、着いたら見回す）→ 気づき 1000 → **追跡**（最後に見た位置へ走る、見えている間は更新）→ 見失って最後に見た位置に着いた → **捜索**（周囲のマスをシード付き乱数で `SearchTicks` 巡る）→ 時間切れ → 巡回へ戻る。**音は調査にしかならず、音だけで追跡に入らない** |
| 警戒レベル | 施設全体：平常／警戒／厳戒。誰かが追跡に入ると厳戒（他の警備員は最後に見た位置の調査へ、視程 +）。誰も追跡していない Tick が続くと `AlertDecayTicks` で警戒、さらに平常へ。警戒中は巡回が速く視程が長い |
| 見失った後の減衰 | 捜索・巡回の間、気づき値は毎 Tick `DecayPerTick` 減る。警戒レベルも上の時間で下がる |
| 捕獲 | **追跡中**の警備員と自機の箱が、その Tick の 1 軸ずつの移動経路の掃引で触れたら捕獲（025 `Sweep.Along` の方式）。追跡していない警備員に触れても捕獲しない（押し戻されるだけ）→ 調査に入る |
| 目的物・出口 | 目的物に触れると取得。取得後に出口のマスに入ると脱出成功。未取得で出口に入っても何も起きない |
| 同じ Tick の順序 | 自機の移動 → 足音 → 目的物 → 知覚（視覚・聴覚）→ 判断 → 警備員の移動 → 捕獲 → 脱出。**捕獲と脱出が同じ Tick なら捕獲**（テストで場面を作る） |
| 結果 | 脱出：時間・発見回数（追跡に入った回数）・評価（発見 0 なら「未発見」）。捕獲：ゲームオーバー → Enter で再挑戦（新しいシード）／Esc でタイトル |
| 乱数 | 1 試合 = 1 シードの `SeededRandom`。警備員ごとに系列を分ける（015 の知見）。再戦シードは起動時の値から試合ごとに進める（010） |
| 終了後 | 脱出・捕獲の後の Tick と入力は状態を変えない |

施設の案（地図の文字は README に固定）：南西に入口兼出口、北東の資料室に目的物。警備員 A＝中央廊下の周回、B＝資料室前で左右を見回す見張り（巡回点 1 つ）、C＝西棟と東棟を往復。木箱の列で忍び足の抜け道、走れば近道だが音が届く配置にする。

### Core（`Assets/Game/Core/`）
使用：tile-motion（`Box`・`Units`・`BodyMotion`・`ISolidGrid`・`Countdown`・`StateHash`）、int-geometry（`Vec2i`・`IntMath`・`Trig`・`SeededRandom`）。

- **知覚（センサ）**：状態を持たない純粋関数。判断を知らない。
  - `FacilityMap`：文字の地図、`ISolidGrid`、`BlocksSight(cell)`・`BlocksSound(cell)`
  - `LineOfSight.Clear(map, from, to)`：supercover の整数走査
  - `Vision.Sees(map, eye, facing, spec, target)`／`Vision.Fan(...)`（描画用の扇の各光線の届く距離。判定と同じ `LineOfSight` を使う）
  - `Hearing.Reach(map, source, radius)`：BFS 距離の場（再利用バッファ）
  - `Percept`：その Tick に警備員が得たもの（見えた位置・距離／聞いた位置）
- **判断**：`GuardBrain`（FSM、気づき値、最後に見た位置、捜索の目標、見回しの位相）。入力は `Percept` と施設の警戒レベルだけで、地図の視線計算をしない
- `GuardBody`：巡回点・調査先へのタイル BFS の経路追従、向きの回転（`Trig` の角度で 1 Tick に一定量）
- `Intruder`：自機の移動・歩き方・足音の間隔
- `AlertLevel`：施設の警戒レベルと減衰
- `Infiltration`：試合全体。`Tick(IntruderInput)`・`Clone()`・`Hash()`・`StartAt(...)`（テスト・撮影用に位置・警備員の状態を指定）、発見回数・経過 Tick・結果
- `WardConfig`：公開フィールド＋検証（有限・範囲、走りの 1 Tick 移動 < 1 タイル、足音の距離 忍び足 < 歩き < 走り、視野角 0〜180°、捕獲の箱が 1 タイルの通路を通れる 等）
- 状態のハッシュに FSM の状態・気づき値・各タイマー・最後に見た位置・乱数の状態・警戒レベルを含める（022 の知見）

### App（`Assets/Game/App/`）
- `WardFlow`：Title / Playing / Paused / Caught / Escaped。時刻付き入力・フレーム単位の Tick 上限・遊んだ時間の分離を 025 の `VaultFlow` から移植
- `Intent`・`HeldControls`：025 から移植（WASD/矢印・Shift・C・Esc・Enter・F1）
- `Autopilot`：2 方式。**隠密**＝目標へのタイル BFS の経路を、`Infiltration.Clone()` で一定 Tick 先読みして気づき値が増えない手（進む・待つ・別経路・忍び足）だけから選ぶ。**強行**＝発見を気にせず走って目的物と出口へ向かい、追跡を振り切る。どちらも試合ごとに判断状態を捨てる（023）。隠密で全シードを通せない場合は、022 の `RouteFinder` と同じく探索で作った入力の列をテストに持って再生する（見つからない限り乱数を使わないので、列はシードに依らない）

### テスト（題材固有を優先）
- **遮蔽物越しに視認しない**：壁・木箱の向こう／斜めの角をかすめる視線（両側のどちらかが壁なら見えない）／視程ちょうど・1 単位外／半視野角ちょうど・外／背後。`LineOfSight` を素朴な参照実装（細かい有理数の点列で通るマスを列挙）と乱数の地図で照合
- **音だけで即発見としない**：走りの足音を何度聞かせても追跡にならず調査になる／壁の向こうで直線距離は近いが経路が遠いと聞こえない／木箱越しは聞こえる／忍び足は 1.5 タイル外で聞こえない
- **見失った後に警戒が減衰**：追跡 → 視線を切る → 最後に見た位置で捜索 → `SearchTicks` 後に巡回、気づき値が単調に減り 0 に戻る／警戒レベルが厳戒 → 警戒 → 平常へ境界 Tick どおりに下がる／捜索中に再び見えれば追跡へ戻る
- **センサと判断の分離**：`GuardBrain` に作った `Percept` を与えるだけで全遷移を確かめる（地図なし）
- **両経路でクリア**：隠密で発見 0 のまま脱出（20 シード）、強行で発見 ≥ 1 から脱出（20 シード）。固定シードの最終ハッシュを .NET と Mono で照合
- 捕獲：追跡中だけ捕獲、追跡でない警備員との接触は捕獲しない、経路の掃引（斜めのかすり）、捕獲と脱出の同 Tick
- 共通：状態遷移（開始→進行→終了→再戦、終了後の入力無視）、負テスト（未定義の操作・NaN/範囲外の設定・終了後の Tick・同 Tick の二重押下）、再現性（同シード同入力で同ハッシュ・警備員ごとの系列の独立・「今は同じでも以後が分かれる」組でハッシュが違う）

---

## セッション2：Playable（Unity 層）

- `HushWardGame`（時刻付き入力・HUD・音）、`HushWardStage`（Tilemap・木箱・目的物・出口・警備員・自機）、`HushWardSettings`（ScriptableObject：速さ・視程・視野角・足音・気づきの速さ・減衰・音量）。コードの既定値を変えたら `.asset` も直す（024）
- **視界の扇**：`Vision.Fan` の光線の距離から Mesh を作り半透明で描く（判定と同じ関数）。色は状態で変える（巡回＝薄い黄、調査＝橙、追跡＝赤）
- **デバッグ表示**（完了判定）：警備員の頭上に状態の記号（? / ! / …）と気づきのゲージを常に表示。F1 で詳細（状態名・気づき値・最後に見た位置・捜索の目標・巡回経路・足音の輪）を切り替え
- 足音の輪（届く距離）を短く描き、音の大きさが分かるようにする
- Editor：`HushWardArtGenerator`（ドット絵、1 タイル 32px・Point）、`HushWardSceneBuilder`（Core の地図から Stage Prefab、既存 Prefab は上書きしない、種類と位置まで照合する `Mismatch`、古いインスタンスは前方一致で消す）
- HUD（PanelRenderer）：警戒レベル・目的物の有無・時間・発見回数、タイトルの操作説明、捕獲・脱出の結果画面。作った直後に一度描画。タイトルへ戻ったら Core から作り直す（021）
- 音（足音・発見の警告音・取得・脱出・捕獲）は控えめ、`AudioPolicy.ShouldMute` で消音
- 撮影台本 `HushWardCaptureScenario`：タイトル・巡回中・木箱の陰（視線が遮られる）・足音で調査に来る・発見（追跡）・振り切って捜索・F1 の詳細表示・目的物の取得・脱出（未発見）・捕獲

## セッション3：Verification

- PlayMode：キー操作（歩き・忍び足・走りの速さ）、扇の Mesh が Core の `Vision.Fan` と一致、デバッグ表示の切り替えと状態の記号、捕獲 → 再挑戦、脱出の結果画面（未発見／発見あり）、タイトルへ戻った表示が起動直後と一致、一時停止・フォーカス喪失、Prefab の照合、設定アセットの検証
- 性能：警備員 3 体の知覚（視線・BFS）と扇の Mesh 更新の p95、割り当て 0（027 の `ProfilerRecorder` の方式）
- `dev build 030`、`dev capture 030`、同一ビルドで 2 回撮影して SHA-256 一致（1 回目を README からリンクしてから 2 回目）
- README（環境・操作・ルール表・センサと判断の境界・パッケージに委譲したもの・未実装範囲）、`evidence/README.md`、`progress.csv` を「検証待ち」へ

---

## 主なファイル

- 新規：`games/030-stealth/**`、`plan/games/030-stealth-implementation.md`
- 変更：`unity-100games.slnx`（dev new）、`plan/progress.csv`、`games/030-stealth/Packages/manifest.json`、`dotnet/G030.Core.csproj`
- 流用元：025 の `VaultFlow`・`HeldControls`・`Intent`・`Sweep.Along`・SceneBuilder / ArtGenerator / CaptureScenario / `Mismatch`、021 `Autopilot`（Clone の先読み）、022 `RouteFinder`、027 の割り当て計測、`packages/com.games100.tile-motion`・`packages/com.games100.int-geometry`

## 検証

- 各セッションの終わりに `dev lint` → `dev check` → `dev test`
- Unity 層を触ったら `dev test 030 -Unity` と `dev test 030 -Unity -Mode PlayMode`（パッケージのテストも Mono で流れる）
- `dev build 030` → `dev capture 030` → 証跡を確認 → 「検証待ち」で人間に渡す（`dev run 030`）

---

## セッション0の結果（2026-11-23）

- `feature/game030` で `dev new 030`（2D テンプレート）を実行し、雛形と slnx 登録を最小コミットした。EditMode 1/1。
- `com.games100.int-geometry` と `com.games100.tile-motion` を `manifest.json`（`testables`）・各 asmdef・Core の csproj から参照した（029 と同じ形）。
- 次（セッション1）：ルール表を README に固定し、Core（`FacilityMap`・`LineOfSight`・`Vision`・`Hearing`・`Percept`・`GuardBrain`・`GuardBody`・`Intruder`・`AlertLevel`・`Infiltration`・`WardConfig`）と App（`WardFlow`・`Intent`・`HeldControls`・`Autopilot`）をテストから作る。

## セッション1の結果（2026-11-24）

- ルール表は [README](../../games/030-stealth/README.md) の「採用ルール」を正とする。
- Core：`FacilityMap`・`Facility`（本番の施設と警備員 3 人の配置）、センサ（`LineOfSight`・`Vision`・`Hearing`・`Percept`）、判断（`GuardBrain`・`Order`・`Waypoint`）、`GuardBody`・`Intruder`・`PathField`・`SearchArea`、025 から移植した経路の掃引（`Motion`・`BoxSweep`）、`AlertState`・`Infiltration`・`WardConfig`。App：`Intent`・`HeldControls`・`WardFlow`・`StealthPilot`・`BoldPilot`・`Steering`・`Exposure`。
- Core 67 件・App 36 件が .NET で、共通パッケージを含む 165 件が Mono で通った。
- 計画との差：
  - 視線は supercover の走査ではなく、「閉じた線分が遮るマスの閉じた正方形に触れるか」を Liang–Barsky の区間を分数で比べて判定する定義にした（角・面に沿う線も遮る）。テストは外積による分離軸の別の式と照合する。
  - `Percept` に体の状態（マスの中心に立っているか・向き）と、前の Tick に触れた位置を入れた。判断は「着いたか」をこれで知り、地図を見ない。捜索の行き先だけ `ISearchArea` に選ばせる。
  - 追跡していない警備員に触れても捕まらず、その警備員が次の Tick に発見する（触れた Tick に捕まえると「追跡中だけ捕獲」と食い違う）。
  - 調査中に見えている間は気づき値が増え続けるので、正面の近くで見つかると、調査で近づきながら気づき値が満ちてそのまま捕まる。「出口の隣で捕獲と脱出が同じ Tick」の場面は、背後から触れて追跡させてから作った。
  - 操縦役は最短経路だと資料室の正面の扉（B の正面）を通って捕まった。巡回を試走して各マスが視界・近くにあった割合（`Exposure`）を数え、それを重みにした経路にした。候補の順序（「歩いて 0 Tick 進む」＝待つが忍び足より先）で止まり続けた不具合も直した。
  - 見つかった後に逃げる操縦役は、見られる Tick を聞かれる Tick の 2 倍悪いとして点を付け、走る・来た道を戻る・歩数が最短の経路の候補も試す。20 シードすべて逃げ切った（初めは北西の部屋で捜索中の A に向かって走り込み、1 シードで捕まった）。
- 隠密：1819 Tick・誰も調査に入らず脱出、ハッシュ `0xA0BB2468`（シードに依らない、.NET と Mono で一致）。強行：20 シードで 944〜1104 Tick・発見 1〜2 回、シード 1 のハッシュ `0x3C5EC9CF`（.NET と Mono で一致）。
- 次（セッション2）：Unity 層（生成ドット絵・Stage Prefab・視界の扇の Mesh・状態の記号と気づきのゲージ・F1 の詳しい表示・足音の輪・HUD・音・シーン生成・撮影台本）。
