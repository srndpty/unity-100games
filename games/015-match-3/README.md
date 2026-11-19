# 015. マッチ3 — PRISM TIDE

仕様書：[plan/games/015-match-3.md](../../plan/games/015-match-3.md)

潮だまりの結晶を入れ替えて、同じ色を縦か横に3つ以上そろえて消すパズル。決められた手数のうちに目標スコアを集める。参考作品からはメカニクスだけを採用し、名称、絵、配色、音は独自に作っている。

## 環境

- Unity：6000.6.9f1（Universal 2D、C# 9.0）
- 対象プラットフォーム：Windows (StandaloneWindows64)
- 入力方式：Input System のマウスとキーボード。HUD のボタンはマウスでも押せる。HUD は PanelRenderer / UI Toolkit。日本語は Windows の Yu Gothic で表示する

## 起動手順

```
dev gui 015      # エディタで開く
dev build 015    # Windows ビルド
dev run 015      # ビルドを起動
dev capture 015  # 証跡を evidence/ に撮る
```

シーンは `Assets/Scenes/PrismTide.unity`。

## 操作

| 入力 | 内容 |
|---|---|
| 左クリック | 結晶を選ぶ。選んだ結晶の隣をクリックすると入れ替える。離れたマスをクリックすると選び直す。選んだ結晶をもう一度クリックすると外す |
| 左ドラッグ | 押した結晶を、マスの中心から 0.6 マス以上動かした方向の隣と入れ替える |
| 矢印 / WASD | カーソルを動かす。結晶を選んでいれば、その方向の隣と入れ替える |
| Space | カーソルの結晶を選ぶ、外す |
| Esc / P | 一時停止、再開 |
| Enter / ボタン | 開始、再開、もう一度 |
| R / ボタン | やり直す（一時停止中と結果画面） |
| T / ボタン | タイトルへ（一時停止中と結果画面） |

## 採用ルール

| 項目 | 採用ルール |
|---|---|
| 盤面 | 6×6。盤面の外との入れ替えはできない |
| 結晶 | 5色（設定で 3〜6 色）。色ごとに形も変える。特殊な結晶はない |
| 初期盤面 | シード付き乱数。左の2つ・下の2つと同じ色を避けて置くので並びはない。合法手がなければ作り直す |
| 入れ替え | 上下左右に隣り合う2マスだけ。入れ替えた結果、縦か横に同じ色が3つ以上並ぶときだけ成立する |
| 不成立 | 入れ替えて戻す演出だけ行い、盤面・手数・得点は変わらない |
| 消去 | 縦横の3個以上の並びをすべて同時に消す。縦と横が交わるマス（十字・L字・T字）は1回だけ数える |
| 落下・補充 | 消えたマスへ上の結晶が落ち、空いた上端は補充専用の乱数系列で埋める。補充は並びを避けない |
| 連鎖 | 落下・補充の後に並びがあれば続けて消す。並びがなくなるまで繰り返す |
| 得点 | 消した数 × 10 × 連鎖の段数（1段目が1）。合計は long、上限で頭打ち |
| 手数 | 成立した入れ替えで1減る（既定 20手）。連鎖しても1手 |
| 勝敗 | 連鎖を最後まで解決してから判定する。目標スコア（既定 2,000点）以上ならクリア、届かずに手数が0なら手数切れ。最後の1手で届けばクリア |
| 手詰まり | 解決後に合法手がなければ、手数を減らさずに盤面を作り直す（初期盤面と同じ生成、別の乱数系列） |
| 解決中の入力 | 入れ替え・消去・落下・作り直しの演出中の入れ替えは捨てる（先行入力しない）。選択は取り消す |
| 時間 | 60Hz の固定 Tick。入れ替え10、戻し20、消去18、落下は1段4、作り直し40 Tick |
| 再戦 | 試合ごとに新しいシードを使う |

乱数は試合のシードから「初期生成」「補充」「作り直し」の3系列に分け、混ぜない。同じシードと同じ入れ替えの列なら、消去・補充を含めて最終盤面と得点が一致する。

目標スコアの既定値は、Core だけで2,000試合を自動で遊んで決めた。20手で合法手を一様に選ぶと中央値 1,590点（10%点 1,110、90%点 2,450）、一番多く消える手を選ぶと中央値 2,040点。少し考えて連鎖を狙えば届く程度として 2,000点にした。

## 設計上の選択

| 層 | 責務 |
|---|---|
| Core | `Board`（不変の盤面）、`MatchFinder`（並びの検出だけ）、`SwapRules`（入れ替えの合法性と合法手の列挙）、`BoardGravity` / `Refiller`（落下と補充、補充だけが乱数を使う）、`CascadeResolver`（1段・全段の解決の組み立て）、`BoardGenerator`（並びなし＋合法手ありの盤面）、`Match3Game`（フェーズ進行）、`Match3Config`（設定）、`BoardText`（文字列の盤面） |
| App | `Match3Flow`（画面遷移、操作の窓口、実時間から Tick への変換）、`SwapInput`（クリック・ドラッグ・キーを入れ替えの意図へ変換）、`Autopilot`（合法手から乱数で1手選ぶ）、`DemoBoards`（撮影とテストの場面） |
| Unity | `PrismTideGame`（入力・HUD・音）、`PrismBoardView`（描画）、`PrismTideSettings`（設定アセット）、シーンと素材の生成、撮影台本 |

解決の流れは次のフェーズで進む。

```
Idle ─入れ替え─┬─成立→ Swapping → Clearing → Falling ─並びあり→ Clearing …（連鎖+1）
               │                                    └並びなし→ 勝敗判定 → Won / Lost / Reshuffling / Idle
               └─不成立→ Reverting → Idle
```

- **検出と補充を分ける。** `MatchFinder` は盤面を読んで消えるマスを返すだけで、補充を知らない。`Refiller` は空きを乱数で埋めるだけで、並びを知らない。両者をつなぐのは `CascadeResolver` だけで、補充の乱数は並びがあるときにしか引かない。
- **入れ替えの合法性は1か所で決める。** `SwapRules.Check` が盤面外・同じマス・非隣接・並びなしを理由付きで返し、合法手の列挙も同じ判定を使う。入力側（`SwapInput`）はルールを知らず、意図だけを作る。
- **解決のロジックと演出を分ける。** 検出・落下・補充は時間を持たない純粋関数で、`Match3Game` は待ち時間を数えて節目で呼ぶだけ。描画は `Match3Game` の状態とフェーズの進み具合を読むだけで、Core へ書き戻さない。演出0 Tick と既定値で遊んだ結果の一致をテストしている。
- **Core は整数だけで動かす。** 同じシードと入れ替えの列の最終盤面と得点を固定値で照合し、Mono と .NET の一致を確かめている。
- **補充の結晶は盤面の上から落とし、SpriteMask で盤面の外を隠す。**
- **物理エンジンは使わない。** 落下はすべて Core のマス目で決める。

設定アセットは `Assets/Game/Unity/PrismTideSettings.asset`。色数、手数、目標スコア、得点、演出時間、音量を調整できる。

シーンの再生成：

```
unity run games/015-match-3 -- -executeMethod Game.Unity.Editor.PrismTideSceneBuilder.Build
```

素材は `PrismTideArtGenerator` が図形から描き出した PNG。再生成は既存のファイル、そのインポート設定、Stage Prefab を上書きしない。

## テスト

| 種類 | 場所 | 件数 | 実行方法 |
|---|---|---:|---|
| Core | `Assets/Tests/Core/` | 87 | `dev test` |
| App | `Assets/Tests/App/` | 40 | `dev test` |
| EditMode（Core/App を Mono で実行） | 同上 | 127 | `dev test 015 -Unity` |
| PlayMode（実シーン13、共通証跡16） | `Assets/Tests/Unity/` | 29 | `dev test 015 -Unity -Mode PlayMode` |

仕様書の題材固有テストとの対応：

| 仕様のテスト | 主なテスト |
|---|---|
| 不成立スワップを元に戻す | `Match3GameTests.NoMatchSwapIsRevertedWithoutSpendingAMove`、`InvalidSwapChangesNothing`、PlayMode `Mouse_NoMatchSwapGoesBackAndKeepsTheMove` |
| 縦横交差は重複除去 | `MatchFinderTests.CrossCountsSharedCellOnce` / `LShapeCountsCornerOnce` / `TShapeCountsJointOnce`、`CascadeTests.CrossIsScoredOncePerCell` |
| 補充後連鎖を最後まで解決 | `CascadeTests.CascadeAfterRefillIsResolvedToTheEnd`、`Match3GameTests.TargetIsJudgedAfterTheWholeCascade` |
| 消去・補充のシード再現 | `Match3GameTests.SameSeedAndMovesReproduceTheFinalState`（固定値）、`InstantAndTimedResolutionAgree`、`DeadStartingBoardIsReshuffledFromItsOwnStream` |

結果と画面証跡は [evidence/README.md](evidence/README.md) に記録している。

## 未実装範囲

仕様書の Out of scope どおり、特殊ピース、課金・ライフ制、大量のアセット制作、追加ステージ、実績・外部ランキング。加えて、ヒント表示、残り手数のボーナス、BGM、スコアの保存は採用していない。

連鎖は理論上いくらでも続きうる（補充が毎回並びを作る場合）。確率は無視できるほど小さいため上限は設けていない。

証跡画像はJPEG（`.jpg`）、品質85を標準とし、保存したJPEGのSHA-256をマニフェストに記録する。
