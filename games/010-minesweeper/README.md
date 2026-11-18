# 010. マインスイーパー — SAFE DIG

仕様書：[plan/games/010-minesweeper.md](../../plan/games/010-minesweeper.md)

草原に埋まった爆弾を避けて、安全な土をすべて掘り起こす盤面パズル。参考作品からはメカニクスだけを採用し、名称と見た目は独自に作っている。

## 環境

- Unity：6000.6.9f1（Universal 2D、C# 9.0）
- 対象プラットフォーム：Windows (StandaloneWindows64)
- 入力方式：Input System のマウスとキーボード。HUD は PanelRenderer / UI Toolkit。日本語は Windows の Yu Gothic で表示する

## 起動手順

```
dev gui 010      # エディタで開く
dev build 010    # Windows ビルド
dev run 010      # ビルドを起動
dev capture 010  # 証跡を evidence/ に撮る
```

シーンは `Assets/Scenes/SafeDig.unity`。

## 操作

| 入力 | 内容 |
|---|---|
| 左クリック | セルを掘る |
| 右クリック | 旗を立てる / 外す |
| Enter / ボタン | 開始・再戦 |
| Esc / ボタン | タイトルへ戻る |

## 採用ルール

| 項目 | 採用ルール |
|---|---|
| 盤面 | 16×16、爆弾40個の1種類 |
| 初手安全 | 最初に開くセルとその周囲8セルに爆弾を置かない。爆弾は初手の確定時に配置する |
| 再現性 | 「シード＋初手セル」から同じ盤面を再現する。1試合1シードで、再戦は新シード |
| 開く | 未公開かつ旗なしのセルだけ開ける。爆弾なら敗北 |
| 連鎖公開 | 周囲の爆弾が0のセルから、8方向へキューで展開する。旗付きセルは開かず、そこから先へも展開しない |
| 旗 | 未公開セルで切り替える。初手の前も置ける。個数の上限はなく、残り数は負になりうる |
| 勝利 | 爆弾以外の全セルを公開した時点。旗は条件に含めない。残りの爆弾には自動で旗を表示する |
| 敗北 | 全爆弾を表示し、踏んだ爆弾と誤った旗を区別して表示する |
| 終了後の操作 | すべて拒否し、状態を変えない |

## 設計上の選択

| 層 | 責務 |
|---|---|
| Core | `BoardConfig`（設定と検証）、`MineField`（爆弾配置と隣接数）、`MinesweeperGame`（公開状態・旗・勝敗）、`CellFace`（表示用の面） |
| App | `MinesweeperFlow`（画面遷移と操作の窓口）、`AnswerKeyPilot`（テストと撮影で共有する操縦役） |
| Unity | `SafeDigGame`（入力・HUD・音）、`SafeDigView`（描画）、`SafeDigSettings`（設定アセット）、シーンと素材の生成、撮影台本 |

- **爆弾配置と表示を分ける。** 答えは不変の `MineField` に置き、描画は `MinesweeperGame.Face` が返す `CellFace` だけを読む。進行中の `CellFace` には爆弾の有無が含まれない。
- **連鎖公開はキューで行う。** 再帰を使わず、投入時に公開済みにして各セルを最大1回だけ処理する。
- **結果表示で盤面を隠さない。** 案内は盤面の右の固定パネルに置き、負けた盤面を見返せるようにしている。
- **ウィンドウを選び直すクリックでは掘らない。** フォーカス喪失中の操作と、復帰直後のフレームの操作を捨てる。
- **物理エンジンは使わない。** ターン制で、時間にも依存しない。

設定アセットは `Assets/Game/Unity/SafeDigSettings.asset`。爆弾の数と音量を調整できる。盤面の大きさを変えるときは `Art/SafeDigStage.prefab` を消してシーンを再生成する。

```
unity run games/010-minesweeper -- -executeMethod Game.Unity.Editor.SafeDigSceneBuilder.Build
```

素材は `SafeDigArtGenerator` が図形から描き出した PNG。再生成は既存のファイル、そのインポート設定、Prefab を上書きしない。

## テスト

| 種類 | 場所 | 件数 | 実行方法 |
|---|---|---:|---|
| Core | `Assets/Tests/Core/` | 48 | `dev test` |
| App | `Assets/Tests/App/` | 12 | `dev test` |
| EditMode（Core/App を Mono で実行） | 同上 | 60 | `dev test 010 -Unity` |
| PlayMode（実シーン17、共通証跡16） | `Assets/Tests/Unity/` | 33 | `dev test 010 -Unity -Mode PlayMode` |

結果と画面証跡は [evidence/README.md](evidence/README.md) に記録している。

## 未実装範囲

仕様書の Out of scope どおり、ランキング共有、競技タイマーの厳密性、大量のアセット制作、追加ステージ、課金・実績・外部ランキング。加えて、タイマー表示、両クリック同時開き、「?」マーク、一時停止、複数の難易度は採用していない。

証跡画像はJPEG（`.jpg`）、品質85を標準とし、保存したJPEGのSHA-256をマニフェストに記録する。
