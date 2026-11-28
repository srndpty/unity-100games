# 全体 TODO

作品をまたぐ課題や、完了済みの作品に後から見つかった未対応の問題を記録する。対応したら項目を消す。

## 未対応

### 021・022：共通パッケージ tile-motion に置き換える

- **対象：** `games/021-platformer-2d`・`games/022-precision-platformer` の `Box`・`Units`・`BodyMotion`・`GroundProbe`（022 は `Countdown`・`StateHash` も）
- **状況：** 023 の着手時に `packages/com.games100.tile-motion` へ抽出した。完了済みの 021・022 は元の複製のまま残している。
- **対応：** 各作品の `TileMap` に `ISolidGrid` を実装させ、重複するファイルを消してパッケージを参照する。完走ハッシュなどの固定値が変わらないことを .NET と Mono の両方で確かめる。

### 026・027：円の掃引（Geometry）を共通パッケージへ抽出する候補

- **対象：** `games/026-twin-stick` と `games/027-bullet-hell` の `Assets/Game/Core/Geometry.cs`（`Vec2i`・`IntMath`・`Rect`・`Touch`・`Sweep`）と、027 の `VeilRun.Touches`（大きな座標を `BigInteger` で厳密に判定する）
- **状況：** 027 は 026 から写して使っている（2 作品目）。026 の `Sweep.First` は座標が大きいと 2 次式が long を超えうる（026 の座標の範囲では超えない）。
- **対応：** 3 作品目で円どうしの掃引を使うときに、`VeilRun.Touches` の厳密な判定を含めて `packages/` へ抽出し、026・027 をパッケージの参照に置き換える。完走ハッシュなどの固定値が変わらないことを .NET と Mono の両方で確かめる。

### 041・045・046・050：セーブの行指向テキスト（SaveText・FileSaveStore）を共通パッケージへ抽出する候補

- **対象：** `games/046-visual-novel` と `games/050-dating-sim` の `Assets/Game/Core/Save/SaveText.cs`・`Assets/Game/Unity/FileSaveStore.cs`・`Assets/Game/App/ISaveStore.cs`（041・045 にも同じ方式の複製がある）
- **状況：** 050 は 046 から写した（チェックサムの種と案内の文だけ違う。050 の `ISaveStore` には `TryDelete` を足した）。049 が別の worktree で並行していたので、050 では作品の中に写した。
- **対応：** Core の `SaveText`（key=value・版・指紋・チェックサム）と App の `ISaveStore`・`MemorySaveStore` を `packages/` へ抽出し、Unity の `FileSaveStore` は作品に残すか別のパッケージにする。各作品の全ての切り位置・1 文字の改変のテストが通ることと、既存のセーブの書式（チェックサムの種）が変わらないことを確かめる。

### 046・047・050：行指向 DSL の構文解析の土台を共通化する候補

- **対象：** `games/046-visual-novel` の `ScriptParser`・`ScriptCompiler`、`games/047-point-click` の `RuleCompiler`、`games/050-dating-sim` の `SummerCompiler`（行の読み・コメント・`@end` の塊・行番号付きの誤り・ID の検査・字句）
- **状況：** 3 作品とも行指向で、誤りを行番号と理由で返す形は同じだが、文法・塊の種類・条件式は作品ごとに違う。
- **対応：** 行の分割・コメント・塊の切り出し・`SummerError` 相当の誤りの型・ID の正規表現だけを `packages/` に抜き出せるかを、文法の違いを含めて検討する。抜き出しても各作品のコンパイラのテスト（誤りの文と行番号）が変わらないことを確かめる。

### 024・053：高さ付きの球と軌道予測を共通化する候補

- **対象：** `games/024-tennis/Assets/Game/Core/Ball.cs`・`BallPredictor.cs` と `games/053-soccer/Assets/Game/Core/Ball.cs`・`BallPredictor.cs`（位置・高さ・速度の整数の球、重力と弾み、予測は本番の Step の複製）
- **状況：** 053 は 024 の方式で作り直した（2 作品目。053 は転がりの摩擦・ポストの掃引・到着時刻の閉じた式・パスの速さの表を足した）。
- **対応：** 3 作品目で高さのある球を使うときに、球の Step（弾み・転がり）と予測・到着時刻を `packages/` へ抽出し、コート固有の当たり（ネット・ポスト）は作品に残す形を検討する。各作品の軌跡の一致のテストと固定ハッシュが変わらないことを .NET と Mono で確かめる。

### 010 SAFE DIG：フォーカス復帰のクリックでゲームが終了する

- **対象：** `games/010-minesweeper/Assets/Game/Unity/SafeDigGame.cs` の終了ボタン
- **症状：** タイトル画面でフォーカスを失った後、終了ボタンの上をクリックしてウィンドウへ戻ると、そのクリックでゲームが終了する。
- **原因：** 終了ボタンだけが `QuitHandler()` を直接呼び、フォーカス復帰直後の入力を捨てる判定（`_focusRestoredFrame`）を通らない。
- **対応：** 終了要求も `Update()` の復帰直後の判定を通す。012 の `HarborShiftGame` では、他の操作と同じ入力の列で処理している。復帰直後のクリックは無視され、次のクリックで終了することを PlayMode テストで確かめる。

### 011 TWIN FORGE：同じ問題がないか確認する

- **対象：** `games/011-2048/Assets/Game/Unity/TwinForgeGame.cs` の HUD ボタン
- **確認すること：** 終了ボタンは `QuitHandler()` を直接呼び、フォーカス復帰直後の入力を捨てる処理もない。実際に再現するかを確かめ、再現すれば 010 と同様に直す。

### 014 SPORE BLOOM：CPU の操作中の落下で置き場所がずれることがある

- **対象：** `games/014-puyo-puyo/Assets/Game/App/CpuController.cs`
- **症状：** CPU は出現時の位置で操作の列を決め、一定の間隔で1つずつ実行する。実行の途中でペアが落ちると、高い列に阻まれて移動が失敗したり、回転の押し出しが変わったりして、計画と違う列に置くことがある。
- **対応：** 実行中の位置から操作の列を計算し直すか、操作を終えるまで落下の影響を受けない計画にする。一定の間隔で実行した着地点が、選んだ置き方と一致することをテストで確かめる。
