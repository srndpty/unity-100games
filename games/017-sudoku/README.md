# 017. 数独 — NINE LATTICE

仕様書：[plan/games/017-sudoku.md](../../plan/games/017-sudoku.md)

9×9 の盤の空いたマスに、どの行・列・3×3 のブロックにも 1〜9 が1つずつ入るように数字を埋めるパズル。問題は試合ごとにシードから生成し、解が1通りであることを生成の後に検査している。難しさは3段階から選ぶ。盤は藍の地に生成りの和紙のマス、数字はフォントを使わず線と円弧で描いた。参考作品からはメカニクスだけを採用し、名称、絵、配色、音は独自に作っている。

## 環境

- Unity：6000.6.9f1（Universal 2D、C# 9.0）
- 対象プラットフォーム：Windows (StandaloneWindows64)
- 入力方式：Input System のマウスとキーボード。HUD の数字パッドと各ボタンはマウスでも押せる。HUD は PanelRenderer / UI Toolkit。日本語は Windows の Yu Gothic で表示する

## 起動手順

```
dev gui 017      # エディタで開く
dev build 017    # Windows ビルド
dev run 017      # ビルドを起動
dev capture 017  # 証跡を evidence/ に撮る
```

## 操作

シーンは `Assets/Scenes/NineLattice.unity`。

| 入力 | タイトル・結果画面 | プレイ中 |
|---|---|---|
| 左クリック | ボタン（難しさを含む）を押す | マスを選ぶ（数字は置かない）。数字パッドのボタンを押す |
| 矢印 / WASD | 左右で難しさを選ぶ | 選ぶマスを動かす（盤の端で止まる） |
| 1〜9（テンキー可） | — | 選んだマスに数字を置く。候補モードでは候補を付け外しする |
| Space / 「候補」 | — | 数字と候補（メモ）の入力を切り替える |
| Delete / BackSpace / 0 / 「消す」 | — | 選んだマスの数字を消す。数字がなければ候補を消す |
| H / 「ヒント」 | — | ヒントを出す |
| Enter | 開始、次の問題 | 再開（一時停止中） |
| Esc / P | — | 一時停止、再開 |
| R / ボタン | — | 別の問題（一時停止中） |
| T / ボタン | タイトルへ（結果画面） | タイトルへ（一時停止中） |

## 採用ルール

| 項目 | 採用ルール |
|---|---|
| 盤面 | 9×9、3×3 ブロック。数字は 1〜9 |
| 問題 | 試合のシードから生成する。解は必ず1通り。与え（初めから入っている数字）は変更も消去もできない |
| 難しさ | タイトルと結果画面で選ぶ（既定はふつう）。やさしい・ふつうは、制約伝播（Naked / Hidden Single）だけで最後まで解ける問題に限る（仮置きが要らない）。むずかしいは解が1通りであることだけを保証し、仮置きが要る問題も出る |
| 与えの数 | 難しさごとの目標数（既定 やさしい 42・ふつう 36・むずかしい 26）。点対称の2マスを組で抜くので、結果は目標か目標＋1。条件を保ったまま抜けなくなれば、それより多いまま出す |
| 数字の入力 | 同じ行・列・ブロックに同じ数字があれば拒否し、重複した相手のマスを赤く示す（盤は変わらない）。正解と違っても重複しなければ置ける。自分の数字は上書きできる |
| 候補（メモ） | 空きマスに 1〜9 の候補を付け外しする。数字を置くとそのマスの候補は消え、同じ行・列・ブロックの同じ数字の候補も消える |
| 消去 | プレイヤーの数字を消す。数字がなければ候補をすべて消す |
| ヒント | ① 正解と違う数字があれば、マス番号の最も小さいものを指す（消すのはプレイヤー）。② なければ制約伝播で決まるマスを1つ埋め、理由（「このマスに入るのは 7 だけです」「この行で 5 が入るのはここだけです」）を表示する。③ 伝播で決まらなければ、空きマスのうち番号の最も小さいものに正解を入れる。どれも1回と数える |
| 完成 | 全マスが埋まり、解と一致したとき。誤りを含んだまま全マスが埋まったら「どこかが違います」と出して続ける |
| 結果 | 難しさ・経過時間・ヒントの回数。次の難しさを選び、次の問題（新しいシード）かタイトルへ。選び直しても終わった試合の表示は変わらない |
| 時間 | 60Hz の固定 Tick で経過時間を数える。一時停止中・フォーカスを失った間は進まない。ルールは時間に依存しない |
| 再戦 | 試合ごとに新しいシード（起動時の値から1ずつ進める） |

マス番号は行 × 9 + 列（左上が 0、右下が 80）。仕様書にない Undo は入れていない。

## 設計上の選択

| 層 | 責務 |
|---|---|
| Core | `Board`（盤の形、行・列・ブロックとピアの表、数字の集合のビット演算）、`Grid`（不変の 81 マス）、`Propagator`（制約伝播）、`Solver`（バックトラック）、`PuzzleGenerator`（生成と一意性の検査）、`SudokuGame`（試合の盤・候補・ヒント・完成判定）、`SudokuConfig`（設定） |
| App | `SudokuFlow`（画面遷移、操作の窓口、実時間から Tick への変換と経過時間）、`CellCursor`（選んでいるマスと盤座標→マス）、`Autopilot`（撮影とテストの解き手）、`DemoPuzzles`（誤りを含んだまま全マスを埋められる盤） |
| Unity | `NineLatticeGame`（入力・HUD・音）、`NineLatticeBoardView`（マスの塗り・数字・候補・選択の枠の描画）、`NineLatticeSettings`（設定アセット）、シーンと素材の生成、撮影台本 |

```
Title ─Enter→ Playing ─完成→ Result ─Enter→ Playing（次のシード）
                │  ↑                └─T→ Title
           Esc  ↓  │ Enter
               Paused ─R→ Playing（次のシード） / ─T→ Title
```

- **制約伝播とバックトラックを分ける。** `Propagator` は Naked Single（候補が1つのマス）と Hidden Single（単位の中で数字の入る場所が1つ）だけを、決まった順で適用する。`Solver` は推論を持たず、各節点で `Propagator` を呼んでから候補の最も少ないマスで分岐するだけ。伝播だけで解ける問題では分岐0回であることをテストで確かめている。
- **ヒントは制約伝播をそのまま使う。** 誤りのない盤の解は問題の解と同じ1つだけなので、伝播で決まる数字は必ず正解になる。理由（どの推論か、どの単位か）は `Deduction` が持ち、HUD がその文を作る。
- **生成後に解の個数を数える。** むずかしいでは、完成盤から点対称の組を抜くたびに `Solver.Count(盤, 2)` が 1 かを調べ、2 以上になる組は戻す。解を2つ見つけた時点で探索を打ち切る。
- **難しさは「伝播だけで解けるか」と与えの数で決める。** やさしい・ふつうでは、抜くたびに `Propagator.Run` で最後まで解けるかを調べ、解けなくなる組は戻す。伝播の推論は正しい手だけを置くので、伝播で解けた盤の解は必ず1通り。最初の版は与え30・一意性だけの1段階だったが、試遊で難しいとの声があり、伝播だけで解ける条件と、与えの多い段階を加えた。むずかしい（与え26）でも、伝播だけでは解けない問題は100問中31問。
- **乱数は場面ごとに系列を分ける。** 完成盤を作る系列と、抜く順を決める系列を試合のシードから別々に作る。難しさは抜く条件と目標数だけを変えるので、同じシードなら難しさが違っても完成盤は同じ。生成は整数だけで行うので、シード1の難しさごとの問題と解を固定のハッシュ値で照合するテストが dotnet（.NET）と Unity EditMode（Mono）の両方で通る。
- **正解と違う数字は Core が拒否しない。** 拒否するのは重複だけで、誤りは完成判定とヒントでだけ扱う。全マスが埋まっても解と一致しなければ完成にしない。
- **生成はメインスレッドで同期に行う。** 下の計測のとおり最も抜く設定でも1問 15ms 未満なので、別スレッドにはしない。
- **次の試合の選択と、遊んでいる試合の設定を分ける。** タイトルと結果画面で選ぶ `Difficulty` は次の試合のもので、開始時に `MatchDifficulty` へ写す。
- **物理エンジンは使わない。**

設定アセットは `Assets/Game/Unity/NineLatticeSettings.asset`。難しさごとの与えの目標数（22〜60）、ヒントで指したマスを光らせる時間、音量を調整できる。

シーンの再生成：

```
unity run games/017-sudoku -- -executeMethod Game.Unity.Editor.NineLatticeSceneBuilder.Build
```

素材は `NineLatticeArtGenerator` が図形から描き出した PNG。再生成は既存のファイル、そのインポート設定、Stage Prefab を上書きしない。

### 生成の計測

与えの目標数を下限の 22 にして、シード 0〜99 の 100 問を1問ずつ生成した時間（`PuzzleGeneratorTests.GenerationIsFastEnoughToRunOnTheMainThread`）。対象機は開発用 Windows PC。やさしいは条件がふつうと同じで目標が多いだけなので、ふつうの値が上限になる。

| 環境 | ふつう（伝播で解けるか）p50 / p95 / 最大 | むずかしい（解の数）p50 / p95 / 最大 |
|---|---|---|
| .NET 9（dotnet test、Debug） | 3.32 / 4.39 / 5.51 ms | 5.13 / 7.21 / 8.08 ms |
| Mono（Unity EditMode） | 5.98 / 8.02 / 9.93 ms | 9.05 / 12.48 / 13.87 ms |

既定の目標で生成した 100 問の与えの数：

| 難しさ | 目標 | 最小 / 平均 / 最大 | 伝播だけで解ける |
|---|---:|---|---|
| やさしい | 42 | 42 / 42.0 / 43 | 100 問すべて（生成の条件） |
| ふつう | 36 | 36 / 36.1 / 37 | 100 問すべて（生成の条件） |
| むずかしい | 26 | 26 / 27.5 / 30 | 69 問 |

時間はエディタ上の値で、ビルドした Player の値ではない。

## テスト

| 種類 | 場所 | 件数 | 実行方法 |
|---|---|---:|---|
| Core | `Assets/Tests/Core/` | 84 | `dev test` |
| App | `Assets/Tests/App/` | 28 | `dev test` |
| EditMode（Core/App を Mono で実行） | 同上 | 112 | `dev test 017 -Unity` |
| PlayMode（実シーン18、共通証跡16） | `Assets/Tests/Unity/` | 34 | `dev test 017 -Unity -Mode PlayMode` |

仕様書の題材固有テストとの対応：

| 仕様のテスト | 主なテスト |
|---|---|
| 行列ブロックで同数字重複を拒否 | `SudokuGameTests.DuplicateOfAGivenIsRejectedWithoutChangingTheGame`（行・列・ブロック）/ `DuplicateOfAPlayerDigitIsRejectedInRowColumnAndBox`、`GridTests.ConflictIsFoundInTheRowColumnOrBox`、App `DuplicateIsRejectedWithTheConflictingCell`、PlayMode `Conflict_IsRejectedAndShowsWhere` |
| 生成問題の解が一意 | `PuzzleGeneratorTests.GeneratedPuzzlesHaveExactlyOneSolutionWhichIsTheStoredSolution`（3段階 × 100 シード）/ `TargetGivensIsRespected`、`SolverTests.CountingStopsAtTheLimit` / `DeadlyRectangleHasExactlyTwoSolutions` |
| 矛盾盤面は解なしと判定 | `SolverTests.ContradictoryGridsHaveNoSolution`（重複・候補の尽きたマス・誤った数字）、`PropagatorTests.DuplicateDigitIsAContradiction` / `CellWithoutCandidatesIsAContradiction` / `DigitWithoutAPlaceInAUnitIsAContradiction` / `ContradictionFoundOnlyAfterPropagatingIsReported` |
| 異なるシードで可解な問題 | `PuzzleGeneratorTests.SameSeedGivesTheSamePuzzleAndDifferentSeedsDiffer` / `SeedOneGivesAFixedPuzzleOnEveryRuntime`、`SudokuGameTests.RepeatedHintsSolveGeneratedPuzzles`、App `AutopilotSolvesGeneratedPuzzlesThroughTheFlow` |
| 誤入力を勝利と判定しない | `SudokuGameTests.FullBoardWithAWrongDigitIsNotSolved`、App `FullBoardWithMistakesIsAnnouncedButNotSolved`、PlayMode `FullBoardWithAMistake_IsAnnouncedAndPlayContinues` |
| 難しさ | `PuzzleGeneratorTests.EasyAndNormalPuzzlesAreSolvedByPropagationAlone` / `SomeHardPuzzlesNeedMoreThanPropagation` / `DifficultyChangesOnlyTheRemovalSoTheSolutionIsShared`、App `DifficultyIsChosenOnTheTitleAndUsedForTheMatch` / `DifficultyChosenOnTheResultAppliesToTheNextMatchOnly`、PlayMode `Title_KeysAndButtonsChooseTheDifficulty` |
| 伝播とバックトラックの分離 | `SolverTests.EasyPuzzleHasOneSolutionFoundWithoutBranching` / `HardPuzzleNeedsBranchingAndStillHasOneSolution`、`PropagatorTests.EveryStepHoldsForItsReasonOnTheGridBeforeIt`（各手の理由を素朴な候補計算で確かめる） |

結果と画面証跡は [evidence/README.md](evidence/README.md) に記録している。

## 未実装範囲

仕様書の Out of scope どおり、複数の変則数独、OCR 入力、アセットの大量制作・実績など。加えて、Undo、Naked / Hidden Single より高度な手筋（ペア・X-Wing など）による難しさの格付け、問題の保存、BGM は採用していない。

証跡画像はJPEG（`.jpg`）、品質85を標準とし、保存したJPEGのSHA-256をマニフェストに記録する。
