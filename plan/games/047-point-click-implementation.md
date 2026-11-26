# 047 ポイント＆クリックADV 実装計画

## Context

- 仕様書 [plan/games/047-point-click.md](047-point-click.md) の中心課題は「**動詞・対象・所持品のルール表現**」、重点は「**イベント依存グラフ**」。
- In scope：3 部屋・5 つの謎、持ち物の組み合わせと NPC 会話、謎を全部解いて脱出、タイトル→操作説明→結果→再プレイの一巡。Out of scope：フルボイス・複数章（増やさない。セーブ・ヒント機能・自由移動の経路探索も作らない）。
- 題材固有テスト：**所持しないアイテムを使用できない／解決済みギミックを重複実行しない／依存関係から詰みが発生しない**。完了判定：**全 5 謎を解ける／アイテム状態が移動後も保持**。
- 046 のマージ後に main から `feature/game047` を切って作業する。
- ユーザーと決めたこと：
  - **4 動詞を明示**（見る・取る・使う・話す）。動詞を選ばずにクリックしたら「歩く／出口なら移動」。持ち物を選ぶと「使う ＜持ち物＞ → ＜対象＞」になる。画面下に作りかけの命令文（「使う 包丁 → 漁網」）を出す。
  - **横に歩く主人公**：床の上を左右だけ歩く（Tick で整数の移動、経路探索なし）。対象の「立ち位置」まで歩いてから行為を解決する。
  - **ルールは行指向テキスト**（046 と同じく構文解析 → 名前の解決と検証 → コンパイル。実行器はコンパイル結果だけを読む）。
  - 舞台：**嵐の港町の灯台守小屋**（船着き場・小屋・灯台の 3 部屋、NPC は船頭）。オリジナルの作品名はセッション 1 で決める。
- 乱数は使わない（ルールは決定的）。再現性は「同じルール表と同じ命令列 → 同じ状態ハッシュ」で保ち、README に明記。敗北はない（行き詰まりを作らない設計なので、結果は脱出のみ。結果画面に行為の回数と経過時間）。

---

## セッション分割（区切りを progress.csv の記録/メモに残す）

| セッション | 内容 | 終わりの状態 |
|---|---|---|
| 0 | `dev new 047`（2D）、int-geometry（`Vec2i`・`Rect`）と tile-motion（`StateHash`）を参照、雛形と slnx を最小コミット、この計画を `plan/games/047-point-click-implementation.md` に保存、progress.csv を「着手」、AGENTS.md の利用作品の行 | `dev lint/check/test`・`dev test 047 -Unity` 通過 |
| 1 | ルール表と DSL の文法を README に固定。Core の**構文解析・検証・コンパイル・世界の状態（World）・行為の解決・歩行・依存グラフ・状態空間の探索**＋テスト（小さなテスト用ルール表で） | 題材固有テスト 3 件が .NET と Mono で通る。検証の負例が揃う |
| 2 | **本番のルール表（3 部屋・5 謎・NPC 会話）と解答手順**、App（Flow・命令の組み立て・会話・一時停止）・操縦役＋テスト | 解答手順で脱出、最終ハッシュが .NET と Mono で一致、本番データで「詰みなし」の探索が通る |
| 3 | Playable：生成素材（背景 3・主人公・船頭・物・持ち物の絵）・Room Prefab（ホットスポットとデータの照合）・HUD（動詞・持ち物・命令文・台詞・会話の選択肢・タイトル／操作説明／一時停止／結果）・入力・音・設定アセット・**ビルド前の検証フック**・シーン生成・撮影台本 | Windows ビルドと撮影 |
| 4 | Verification：PlayMode 実シーン・撮影の再現・README/evidence | 状態「検証待ち」 |

**クリックの感触（動詞を選んだままにするか 1 回で外すか、歩いている途中の取り消し、右クリックの役割）はセッション 3 の最初の試遊で確かめ、数値より先に決める**（024・038・041 の教訓）。

---

## ルール表の DSL（セッション 1 で README に固定する文法の案）

1 行 1 文、`#` 以降はコメント。宣言を先、ルールを後に書く。

| 文 | 例 | 意味 |
|---|---|---|
| 部屋 | `@room dock 船着き場 width 1920 start 300` | 部屋 ID・名前・床の幅（整数の単位）・入ったときの位置 |
| 物 | `@object dock.net 漁網 rect 820 140 1040 300 stand 930 [hidden]` | ホットスポット（int-geometry の `Rect`）・立ち位置・初期で隠すか。描画順は書いた順（後ろが手前） |
| 出口 | `@exit dock.path 小道 rect … stand 1850 -> cottage at 200 [locked]` | 行き先の部屋と入る位置。`locked` は `unlock` で開く |
| 人物 | `@npc dock.captain 船頭 rect … stand …` | 話せる相手（話す対象） |
| 持ち物 | `@item knife 包丁` | 持ち物の ID と名前 |
| フラグ | `@flag lamp_oiled` | bool のみ（数値は作らない） |
| 謎 | `@puzzle open_door 灯台の扉を開ける` | 5 つの謎。`solve` で解決、二度と解決しない |
| 目標 | `@goal escape` | 脱出の条件となる謎（最後の謎） |
| ルール | `use knife on dock.net when !solved free_key : solve free_key ; give rusty_key ; 「網を切ると、錆びた鍵が落ちた。」` | 動詞・対象・（持ち物）・条件・効果の列・文 |
| 組み合わせ | `use tea_leaves on kettle : take tea_leaves ; take kettle ; give tea ; 「お茶を淹れた。」` | 持ち物どうし。**A on B と B on A を同じ組に正規化** |
| 会話 | `topic dock.captain weather 「この嵐はいつ止む？」 when !asked_weather : set asked_weather ; 「明日の朝までは無理だな」` | 話すと条件に合う話題を選択肢で出す |

- 動詞：`look`・`take`・`use`・`talk`（＋内部の `walk`）。条件：`has <item>`・`<flag>`・`solved <puzzle>`・`in <room>` とその否定 `!`、`and` だけ（`or` は作らない＝ルールを分けて書く）。
- 効果：`give`・`take`（持ち物を失う）・`set`/`clear`・`solve`・`show`/`hide`（物）・`unlock`（出口）・`goto <room> at x`・文。
- **ルールの選び方**：同じ（動詞・対象・持ち物）で条件が成り立つ最初のルール。成り立つルールがなければ動詞ごとの既定の文（「それは取れない。」）。**解決済みの謎を `solve` するルールは自動で不適用**（コンパイラが暗黙の条件 `!solved P` を足す）。

## 検証（`RuleCompiler`、`RuleError`＝行番号・理由。046 の `ScriptError` の形）

- **構文**：未知の文・動詞・効果、括弧の不一致、`use` で持ち物も対象もない、`look` に持ち物、`rect` の向き（min ≤ max）、立ち位置が部屋の幅の外
- **参照**：未定義の部屋・物・出口・持ち物・フラグ・謎、重複 ID、別の部屋の物を指す（条件 `in` で守っていない）、5 つの謎と目標の数を設定（`AdventureRules`）と照合
- **見る**：全ての物・出口・人物に `look` の既定のルールがある（何もない返事を作らない）
- **状態空間の探索（`StateSpace`）**：位置を除いた抽象状態（部屋・持ち物・フラグ・謎・物の表示・出口の鍵）を、開始から**全ての行為（今いる部屋の対象 × 4 動詞 × 持ち物＋移動＋会話の話題）**で幅優先にたどる。状態数に上限（設定、超えたら例外で拒否）。ここから次を検証：
  - **詰みなし**：到達できる全ての状態から目標の謎の解決に到達できる（逆向きの到達性）
  - **全ルールが使われる**：効果を持つ全てのルールが、ある到達状態で選ばれる（死んだルールを拒否）
  - **曖昧さなし**：どの到達状態でも、同じ（動詞・対象・持ち物）で条件が成り立つルールは高々 1 つ（書いた順への依存を許さない）
  - **持ち物の整合**：持っていない持ち物を `take` する効果が選ばれない、同じ持ち物を 2 個持つ状態に到達しない
  - 5 つの謎がどれも解決できる
- **依存グラフ（`DependencyGraph`）**：ルールから「謎 P を解くのに要る持ち物・フラグ・謎」と「それを生む謎・拾い物」を導いて謎の DAG を作る。循環を拒否し、Mermaid の文字列を出す関数を置く（README に貼った図とテストで照合）
- 行数・宣言の数の上限を展開前に検証（044）、文の字数の上限（台詞枠の設定値）

## ルール表（セッション 1・2 で README に固定する要点）

| 項目 | 採用ルール |
|---|---|
| 世界の状態 | `World`：今の部屋・主人公の x・向き・持ち物（取った順の列、重複なし、上限 8）・フラグ・解いた謎・物の表示・出口の鍵・保留中の行為・会話中の相手。**部屋ごとの状態は World が全部屋ぶん持ち、移動では今の部屋だけを変える**（移動後も保持を構造で保証）。`Clone`・`Hash`（`StateHash`）・読み取り専用の窓口 `WorldView`（043） |
| 命令 | `Act(verb, target, item?)`・`WalkTo(x)`・`Talk(topic)`・`EndTalk`。**持っていない持ち物の `use` は Core で `Rejected` にし、状態・ハッシュを変えない**（UI が作れなくても Core で拒否）。会話中の行為・終了後の操作も拒否（044 の方式） |
| 歩く | `Act` は「立ち位置まで歩いてから解決」を保留にする。Tick ごとに整数の速さで進み、着いた Tick に解決。歩いている途中の新しいクリックは保留を置き換える（前の行為は捨てる）。出口は着いた Tick に移動 |
| 解決 | 当たったルールの効果を書いた順に適用し、出来事（`Said`・`ItemGained`・`ItemLost`・`PuzzleSolved`・`ObjectShown/Hidden`・`ExitUnlocked`・`RoomChanged`・`Escaped`）を記録。音と表示は出来事から作る（039） |
| 会話 | 話すと条件の成り立つ話題を選択肢で出す（「さようなら」は常にある）。話題の選択も 1 つの行為として状態空間の探索に入る |
| 脱出 | 目標の謎を解いた Tick に終了。結果画面：行為の回数・経過時間・解いた謎 5/5 |
| 操作 | 左クリック：動詞→対象、持ち物→対象。動詞なしのクリックは歩く／出口へ。右クリック：選択を外す（試遊で決める）。1〜4 で動詞、Esc で一時停止（つづける・タイトルへ）。フォーカス復帰直後のクリックは捨てる（010）。押した画面の照合は意図を当てはめる直前（044） |

---

## セッション 1：ルールと世界（Core）

- `Rules/`：`RuleParser`（行 → 文、行番号付き）、`RuleCompiler`（名前の解決・型・検証 → `CompiledRules`：部屋・物の配置・ルールの表（動詞・対象・持ち物の組で引ける）・指紋）、`RuleError`
- `World/`：`World`・`WorldView`・`Command`・`ActResult`/`Rejected`・`AdventureEvent`・`Walker`（整数の歩行）、`HitTest`（部屋の座標 → 一番手前の見えている物／出口／人物。`Rect.Contains` は両端を含むので、重なりは描画順で決める）
- `Analysis/`：`StateSpace`（抽象状態の幅優先探索、検証とテストで共用）・`DependencyGraph`
- 046 の `ScriptParser`／`ScriptCompiler`／`ScriptError`／`PathExplorer`、044 の `DeckDataValidator`・`Fingerprint` を写して直す

### テスト（セッション 1）

- **所持しないアイテムを使用できない**：未取得・使用済み（`take` で失った）・組み合わせの片方だけ持つ、の各場合で `use` が `Rejected`、状態とハッシュが不変。取った後は同じ命令が通る
- **解決済みギミックを重複実行しない**：同じ行為を 2 回 → 2 回目は効果なし（持ち物が増えない・出来事が出ない・既定の文）、別の持ち物・別の順序（組み合わせの逆向き）でも二重に解けない
- **詰みが発生しない**：テスト用ルール表で探索が通る。わざと詰む表（唯一の瓶を別の対象に使える・鍵を消費する扉が 2 つ）を検証が行番号付きで拒否する
- 検証の負例を 1 ケースずつ（未定義の参照・重複 ID・`look` のない物・死んだルール・曖昧なルール・循環する依存・状態数の上限超え・立ち位置が部屋の外）
- **移動後も保持**：部屋 A で拾う・物を隠す・出口を開ける → B へ → A へ戻って全て保持、`Clone` が共有しない
- 歩行：保留の置き換え・着いた Tick の解決・部屋の端・速さの境界。ヒットテストの重なり・隠した物は当たらない
- Mermaid の出力が固定の依存グラフと一致

## セッション 2：本番データ・App

- `Assets/Game/Data/Rules.txt`（本番）と `Walkthrough.txt`（解答手順。テスト・撮影で再生、ゲーム本体は使わない、012・033）。5 つの謎の案（セッション 1・2 で確定）：
  1. 小屋の包丁を船着き場の漁網に使う → 錆びた鍵
  2. 錆びた鍵で灯台の扉を開ける（出口の鍵）
  3. 空き瓶を油樽に使う → 油の瓶
  4. 茶葉＋やかん（組み合わせ）→ お茶を船頭に渡す（会話で必要と知る）→ マッチ
  5. 油の瓶とマッチを灯台のランプに使う → 点灯 → 迎えの船で脱出（目標）
  依存：1→2、2・3・4→5（一本道にしない）
- App：`AdventureFlow`（Title / HowTo / Playing / Talking / Paused / Result）、`CommandBuilder`（動詞・持ち物・対象の組み立てと命令文の文字列）、`Intent`（1 本の列）、`Autopilot`（解答手順の再生。撮影・テスト用）
- テスト：解答手順で脱出し全 5 謎、最終ハッシュを .NET と Mono で固定値照合。本番データで探索が通る（状態数を README に記録）。状態遷移（タイトル→プレイ→結果→タイトル→はじめから で初期化、結果後の入力を無視、重複した決定で 2 回解決しない）。命令の組み立て（動詞の切り替え・持ち物の選び直し・会話中のクリックの拒否）

## セッション 3：Playable（Unity）

- `AdventureGame`（Flow の駆動）、`AdventureInput`（マウスの押下を時刻つきで。UI と世界のクリックはパネル座標で `worldBound` と照合、046）、`RoomStage`（背景・物・主人公・船頭の SpriteRenderer、`WorldView` を写すだけ。Tick の状態をそのまま描く）、`AdventureHud`（PanelRenderer：動詞 4・持ち物の枠・命令文・台詞・会話の選択肢・ホバーの名前・タイトル／操作説明／一時停止／結果。作った直後に一度描画）
- 素材：`AdventureArtGenerator`（背景 3・主人公の歩き 2〜4 コマ・船頭・物・持ち物のアイコン）。**Room Prefab の物の位置と大きさをルール表の `rect` と生成と同じ関数で照合**し、1 つずらした表で不一致になるテスト（023・027）
- 日本語フォントは 045・046 の `Font.CreateDynamicFontFromOSFont`
- **ビルド前の検証**：`RulesBuildCheck`（`IPreprocessBuildWithReport`、`BuildFailedException`、046 の `ScenarioBuildCheck` を写す）。壊したルール表で `dev build` が止まることを一度確かめる
- `AdventureSettings`（ScriptableObject：歩く速さ・台詞の表示時間・音量）、`AdventureAudio`（拾う・使う・解決・移動・脱出の控えめな音）、`AdventureSceneBuilder`
- 撮影台本：タイトル、操作説明、各部屋、動詞と命令文、拾った持ち物、組み合わせ、会話の選択肢、各謎の解決、灯台の点灯、結果

## セッション 4：Verification

- PlayMode 実シーン：クリックで動詞→対象→歩いて解決（E2E、013）、持っていない持ち物は選べない、部屋を往復して物と持ち物が残る、会話、解答手順で結果画面まで、タイトルへ戻ると起動直後と一致（021）、フォーカス復帰のクリックを捨てる、歩いている途中の置き換え
- 性能は計測対象ではない。状態空間の探索の時間と状態数だけ記録
- `dev build`、`dev capture` 2 回の SHA-256 一致、evidence/README
- 試遊で見てほしい点：謎の難しさと手がかりの文、クリックの感触、歩く速さ、1 周の長さ、音量

## 主なファイル

- 新規：`games/047-point-click/`（dev new）、`plan/games/047-point-click-implementation.md`
- 更新：`plan/progress.csv`、`unity-100games.slnx`、`AGENTS.md`（int-geometry・tile-motion の利用作品の行、完了時の知見）、作品 README
- 参照（写して直す）：`games/046-visual-novel/Assets/Game/Core/Script/ScriptParser.cs`・`ScriptCompiler.cs`・`ScriptError.cs`・`Runtime/PathExplorer.cs`、`games/046-visual-novel/Assets/Game/Unity/NovelHud.cs`・`NovelGame.cs`・`Editor/` の `ScenarioBuildCheck`、`games/044-deckbuilder/Assets/Game/Core/DeckDataValidator.cs`・`Fingerprint.cs`、`games/043-traditional-roguelike/Assets/Game/Core/PlayerView.cs`、`packages/com.games100.int-geometry/Runtime/Rect.cs`・`Vec2i.cs`、`packages/com.games100.tile-motion/Runtime/StateHash.cs`
- 共通化の候補：条件の評価と状態空間の探索は 048（Interactable の状態機械）・049（進行イベントの条件評価）でも使う見込み。3 題目（049）で再利用価値が明確になってから `packages/` へ抽出し、047 では作品内に置く

## 検証

- 各セッションの終わりに `dev lint` → `dev check` → `dev test 047`、Unity 層を触ったら `dev test 047 -Unity` と `-Mode PlayMode`
- セッション 3・4 で `dev build 047`（壊したルール表でビルドが止まることも一度確かめて戻す）・`dev capture 047`。状態は「検証待ち」まで。完了は人間の試遊と承認の後
