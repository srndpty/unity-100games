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

### 041・043・044：シード付きの系列の派生（GameRng）を共通パッケージへ抽出する候補

- **対象：** `games/041-jrpg`・`games/043-traditional-roguelike`・`games/044-deckbuilder` の `Assets/Game/Core/GameRng.cs`（状態を持つ xorshift32、試合シード・節・用途からの系列の派生、`Weighted`）
- **状況：** 044 で 3 作品目になった。044 は写して使い（用途の enum だけ違う）、抽出はしていない。
- **対応：** 用途を int で受ける形にして `com.games100.int-geometry` の `SeededRandom` の隣へ抽出し、3 作品を参照に置き換える。固定ハッシュが変わらないことを .NET と Mono の両方で確かめる。

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
