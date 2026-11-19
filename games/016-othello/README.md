# 016. オセロ — DUSK & DAWN

仕様書：[plan/games/016-othello.md](../../plan/games/016-othello.md)

8×8 の盤で、相手の石を自分の石で挟んで返す二人零和ゲーム。黒は宵（藍に三日月）、白は暁（淡い金に日輪）の石として描く。プレイヤーは CPU と対戦し、CPU の強さ（探索深さ）と自分の色をタイトルで選ぶ。参考作品からはメカニクスだけを採用し、名称、絵、配色、音は独自に作っている。

## 環境

- Unity：6000.6.9f1（Universal 2D、C# 9.0）
- 対象プラットフォーム：Windows (StandaloneWindows64)
- 入力方式：Input System のマウスとキーボード。HUD のボタンはマウスでも押せる。HUD は PanelRenderer / UI Toolkit。日本語は Windows の Yu Gothic で表示する

## 起動手順

```
dev gui 016      # エディタで開く
dev build 016    # Windows ビルド
dev run 016      # ビルドを起動
dev capture 016  # 証跡を evidence/ に撮る
```

## 操作

シーンは `Assets/Scenes/DuskDawn.unity`。

| 入力 | タイトル・結果画面 | プレイ中 |
|---|---|---|
| 左クリック | ボタンを押す | 点の出ているマスに打つ。打てないマスは「そこには打てません」と出て何も起きない |
| 矢印 / WASD | 左右で CPU の強さ、上下で石の色 | カーソルを動かす（キーを使うまでカーソルは出さない） |
| Enter / Space | 開始、もう一度 | カーソルの位置に打つ（一時停止中は再開） |
| Esc / P | — | 一時停止、再開 |
| R / ボタン | — | やり直す（一時停止中） |
| T / ボタン | タイトルへ（結果画面） | タイトルへ（一時停止中） |

結果画面で選び直した強さと色は次の試合のもので、終わった試合の表示（勝敗・あなたの色）は変えない。

## 採用ルール

| 項目 | 採用ルール |
|---|---|
| 盤面 | 8×8。初期配置は中央4マス（d4・e5 が白、e4・d5 が黒）。黒が先手 |
| 着手 | 空きマスで、8方向のどれかに相手の石を1つ以上挟み、自分の石で閉じる手だけ合法。挟んだ全方向を同時に返す |
| 非合法手 | 挟めない・盤外・埋まったマス・手番でない側・演出中・終局後の手は理由付きで拒否し、局面は変わらない |
| パス | 手番の側に合法手がなく、相手にはあるとき自動でパスする（パスの操作はない）。パスは知らせる間（既定 60 Tick）を置いて相手へ渡す |
| 終局 | 両者とも合法手がない。盤が埋まった場合と、空きが残ったまま両者とも打てない場合の両方 |
| 勝敗 | 石数の多い方が勝ち。同数は引き分け。空きマスはどちらにも加えない |
| CPU | Minimax（negamax 形式）＋ αβ 枝刈り。強さは 弱い=深さ1 / 普通=3 / 強い=5。パスは深さを使わない |
| 評価 | 終局は石差 × 10,000。途中は位置の重み（隅 +120、X打ち -40、C打ち -20 など）の差＋合法手の数の差 × 8 |
| 同点の手 | 最善の評価の手が複数あれば、試合のシードの乱数で選ぶ。手は重みの大きい順の固定順序で調べる |
| 時間 | 60Hz の固定 Tick。返す演出 18、パスの表示 60、CPU が手を決めてから打つまで 30 Tick |
| 演出中の入力 | 返す演出・パスの表示・CPU の手番に来たクリックとキーの着手は、入力の受付（App）で捨てる。カーソルの移動だけは受け付ける。返す演出中は手番が移っていても「石を返しています…」と出し、手番の案内と合法手の印は返し終わってから出す |
| 再戦 | 試合ごとに新しいシード（起動時の値から1ずつ進める） |

棋譜の表記は列 a〜h（左から）、行 1〜8（上から）。

## 設計上の選択

| 層 | 責務 |
|---|---|
| Core | `Position`（不変の局面。黒と白の ulong 2枚と手番。合法手の生成と着手の適用）、`OthelloGame`（パス・終局・勝敗の判定と演出の待ち時間）、`Evaluator`（評価）、`Searcher`（negamax + αβ）、`OthelloConfig`（設定）、`BoardText`（文字列の局面）、`Bits`（popcount など） |
| App | `OthelloFlow`（画面遷移、強さと色の選択、操作の窓口、実時間から Tick への変換）、`CpuPlayer`（探索して考える間の後にプレイヤーと同じ入口で打つ）、`BoardCursor`（キーのカーソルと盤座標→マス）、`DemoGames`（撮影とテストの局面：パス直前・終盤） |
| Unity | `DuskDawnGame`（入力・HUD・音）、`DuskDawnBoardView`（盤・石・合法手の印・直前の手・カーソル・返す演出の描画）、`DuskDawnSettings`（設定アセット）、シーンと素材の生成、撮影台本 |

```
AwaitingMove ─着手→ Placing ─FlipTicks→ ┬ 手番の側が打てる → AwaitingMove
                                         ├ 打てず相手は打てる → Passing ─PassTicks→ AwaitingMove（相手）
                                         └ 両者打てない → Over
```

- **合法手の生成はビット演算、検証は素朴な走査。** `Position.LegalMoves` は8方向へのシフトとマスクで求める。テスト側に64マス×8方向を1マスずつ歩く参照実装を置き、ランダム対局300局の全局面で、両方の手番について合法手と全マスの返る石を照合している。
- **パス・終局はゲームの進行が決め、局面は知らない。** `Position` は「打てる手の集合」と「打った後の局面」だけを返し、パスするか終局かは `OthelloGame.ResolveTurn` の1か所で決める。探索も同じ規則（打てなければ相手の手番、両者打てなければ終局）で読む。
- **CPU はプレイヤーと同じ入口しか使わない。** `CpuPlayer` は `OthelloGame.TryPlay` で打ち、拒否されたら例外にする。
- **探索はメインスレッドで同期に行う。** 深さ5でも下の計測のとおり1手 20ms 程度に収まるので、別スレッドや分割実行はしない。ノード数の上限（20万）をテストで検査している。
- **Core は整数だけで動かす。** 固定シードの CPU 同士の対局の最終局面を固定値で照合し、Mono と .NET の一致を確かめている。
- **次の試合の選択と、遊んでいる試合の設定を分ける。** タイトルと結果画面で選ぶ `Level` / `PlayerSide` は次の試合のもので、試合の開始時に `MatchLevel` / `MatchPlayerSide` へ写す。結果画面で色を選び直しても、終わった試合の勝敗が入れ替わらない。
- **返す演出は描画だけで行う。** Core は着手の時点で局面を確定し、`LastMove` と `LastFlips` を残す。描画はフェーズの進み具合から、返る石を前半は元の色で細く、後半は新しい色で太く描く。
- **物理エンジンは使わない。**

設定アセットは `Assets/Game/Unity/DuskDawnSettings.asset`。演出の Tick 数、CPU の考える間、強さごとの探索深さ（1〜5）、音量を調整できる。

シーンの再生成：

```
unity run games/016-othello -- -executeMethod Game.Unity.Editor.DuskDawnSceneBuilder.Build
```

素材は `DuskDawnArtGenerator` が図形から描き出した PNG。再生成は既存のファイル、そのインポート設定、Stage Prefab を上書きしない。

### 探索の計測

ランダム対局20局の3局面おきの400局面で、深さ5の着手決定を1回ずつ計測（`SearchPerformanceTests`）。対象機は開発用 Windows PC。

| 環境 | ノード数 p50 / p95 / 最大 | 時間 p50 / p95 / 最大 |
|---|---|---|
| .NET 9（dotnet test、Debug） | 2,093 / 8,073 / 17,924 | 1.09 / 4.32 / 9.60 ms |
| Mono（Unity EditMode） | 同上 | 2.66 / 10.85 / 22.90 ms |

ノード数は環境に依存せず一致する。時間はエディタ上の値で、ビルドした Player の値ではない。

## テスト

| 種類 | 場所 | 件数 | 実行方法 |
|---|---|---:|---|
| Core | `Assets/Tests/Core/` | 79 | `dev test` |
| App | `Assets/Tests/App/` | 38 | `dev test` |
| EditMode（Core/App を Mono で実行） | 同上 | 117 | `dev test 016 -Unity` |
| PlayMode（実シーン19、共通証跡16） | `Assets/Tests/Unity/` | 35 | `dev test 016 -Unity -Mode PlayMode` |

仕様書の題材固有テストとの対応：

| 仕様のテスト | 主なテスト |
|---|---|
| 挟めない手を拒否 | `OthelloGameTests.IllegalSquaresAreRejectedWithoutChangingTheGame` / `OutOfBoardIsRejected` / `MoveByTheSideNotToMoveIsRejected`、`PositionTests.OnlyClosedLinesAreFlipped` / `LinesDoNotWrapAroundTheBoardEdge`、App `IllegalClickIsRejectedAndChangesNothing`、PlayMode `Mouse_IllegalClickIsRejectedButALegalClickPlays` |
| 合法手ゼロでパス | `OthelloGameTests.SideWithoutLegalMovesPassesAutomatically` / `PassIsShownForPassTicksAndRejectsMoves` / `PassAfterAMoveHandsTheTurnBackToTheMover`、App `PassIsReportedAndTheGameContinues`、PlayMode `Pass_IsAnnouncedAndTheTurnReturns` |
| 両者打てないと終局 | `OthelloGameTests.GameEndsWhenNeitherSideCanMoveWithEmptySquaresLeft` / `GameEndsWhenTheBoardIsFull` / `MovesAfterTheEndAreRejectedAndTicksStop`、PlayMode `Result_ShowsTheWinAndEnterReplays` / `Result_ShowsTheLossWhenTheCpuHasMoreDiscs` / `Result_ShowsTheDrawWhenTheCountsAreEqual` |
| 合法手を全探索で照合 | `BruteForceTests.LegalMovesAndFlipsMatchTheNaiveScanInEveryVisitedPosition` |
| CPU対戦が完走 | App `CpuMatchesFinishForEveryLevelAndSide`（3段階×2色×3シード）、PlayMode `Mouse_ClickPlaysAndTheCpuAnswers` |
| Minimax と評価 | `SearcherTests.AlphaBetaGivesTheSameValueAsPlainMinimax`（深さ1〜4、枝刈りなしと一致）/ `BestMovesAreExactlyTheMovesWithTheBestPlainValue`、App `StrongerCpuBeatsTheWeakestMoreOften` |

結果と画面証跡は [evidence/README.md](evidence/README.md) に記録している。

## 未実装範囲

仕様書の Out of scope どおり、ネット対戦、大会用時計、アセットの大量制作・実績など。加えて、待った、棋譜の保存と表示、盤の座標の表示、BGM は採用していない。

証跡画像はJPEG（`.jpg`）、品質85を標準とし、保存したJPEGのSHA-256をマニフェストに記録する。
