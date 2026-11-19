# 014. ぷよぷよ — SPORE BLOOM

仕様書：[plan/games/014-puyo-puyo.md](../../plan/games/014-puyo-puyo.md)

同じ色の胞子を4つつなげて開花させ、連鎖で相手の苗床へ枯れ胞子を送る対戦パズル。参考作品からはメカニクスだけを採用し、名称、絵、配色、音は独自に作っている。

## 環境

- Unity：6000.6.9f1（Universal 2D、C# 9.0）
- 対象プラットフォーム：Windows (StandaloneWindows64)
- 入力方式：Input System のキーボード。HUD のボタンはマウスでも押せる。HUD は PanelRenderer / UI Toolkit。日本語は Windows の Yu Gothic で表示する

## 起動手順

```
dev gui 014      # エディタで開く
dev build 014    # Windows ビルド
dev run 014      # ビルドを起動
dev capture 014  # 証跡を evidence/ に撮る
```

シーンは `Assets/Scenes/SporeBloom.unity`。

## 操作

| 入力 | 内容 |
|---|---|
| ← → / A D | 左右へ移動。長押しでリピート |
| ↓ / S | 速く落とす。接地中はすぐ確定する |
| ↑ / X | 右回転 |
| Z | 左回転 |
| Esc / P | 一時停止、再開 |
| Enter / ボタン | 開始、再開、もう一度 |
| R / ボタン | やり直す（一時停止中と結果画面） |
| T / ボタン | タイトルへ（一時停止中と結果画面） |

## 採用ルール

| 項目 | 採用ルール |
|---|---|
| 盤面 | 幅6×可視12＋非表示1段（計13段）。範囲外は壁扱い |
| 胞子 | 4色＋枯れ胞子（おじゃま） |
| 供給 | 色は一様乱数。両者が同じペア列を順に使う。次の2組を表示する |
| 出現 | 軸を3列目の可視最上段、子をその上の非表示段に置く |
| 回転 | 左右90度。①その場 ②軸を反対へ1マス押し出す ③縦向きで左右とも塞がれば軸と子を入れ替える ④それでも無理なら回らない |
| 落下 | 整数マス単位、60Hz の固定 Tick。30秒ごとに重力の段階が上がる（1段に 40〜6 Tick） |
| ソフトドロップ | 2 Tick で1段。接地中は即確定。次のペアへは持ち越さず、押し直しが必要。ハードドロップなし |
| 接地猶予 | 30 Tick。移動・回転の成功で計り直す。1ペア8回まで。より低い段へ進むと回数も戻る |
| 消去 | 上下左右に同色4個以上。非表示段は連結にも消去にも数えない |
| 枯れ胞子 | 消えた胞子に上下左右で隣接すると消える。連結せず、得点にも数えない |
| 得点 | 消した数×10×max(1, 連鎖倍率＋連結ボーナス＋色数ボーナス) |
| 倍率の内訳 | 連鎖倍率は 0, 4, 8, 16, 32, 64, 96, 128（以降は最終値）。連結ボーナスは各グループの (個数−4) の合計。色数ボーナスは 3×(色数−1) |
| 攻撃 | (得点＋端数)÷70 の商を送り、余りは持ち越す。先に自分の予告と相殺する |
| 着弾 | 自分の連鎖が終わった後に最大30個。6個ごとに全列へ1個ずつ、端数はシード付き乱数で別々の列へ。満杯の列にあふれた分は消える |
| 敗北 | 解決後に出現位置（3列目の可視最上段）が埋まっている。同じ Tick に両者なら引き分け |
| CPU | 全配置を1手だけ読んで評価する。10 Tick ごとに1操作し、30% の確率で評価を無視して適当に置く |
| 再戦 | 試合ごとに新しいシードを使う |

既定値での攻撃量の目安は、1連鎖 0個、2連鎖 2個、3連鎖 7個、4連鎖 16個、5連鎖 34個（各連鎖4個消し）。

## 設計上の選択

| 層 | 責務 |
|---|---|
| Core | `Board`（確定した胞子）、`ActivePair` / `PairRotation`（操作ペアと回転）、`GroupFinder` / `BoardGravity` / `ChainResolver`（解決の純粋関数）、`ScoreRules` / `GarbageMeter` / `GarbageDropper`（得点と攻撃）、`PlayerField`（片側のフェーズ進行）、`VersusMatch`（対戦）、`SporeConfig`（設定） |
| App | `VersusFlow`（画面遷移、操作の窓口、実時間から Tick への変換）、`PairPlanner`（配置の評価）、`CpuController`（CPU の操縦）、`ShiftRepeater`（左右の長押し）、`DemoMatches`（撮影とテストの場面） |
| Unity | `SporeBloomGame`（入力・HUD・音）、`SporeBloomView` / `SporeFieldView`（描画）、`SporeBloomSettings`（設定アセット）、シーンと素材の生成、撮影台本 |

解決の流れは次のフェーズで進む。

```
Falling → (確定) → Settling → 判定 ─あり→ Popping → Settling …（連鎖+1）
                                └なし→ GarbageFalling → 窒息判定 → Spawning → Falling / Defeated
```

- **解決のロジックと演出を分ける。** 連結探索・重力・消去は時間を持たない純粋関数で、`PlayerField` は待ち時間を数えて節目で呼ぶだけにした。描画は `PlayerField` の状態を読むだけで、Core へ書き戻さない。
- **演出時間で結果が変わらない。** 演出0 Tick と既定値で解決した結果の一致、段階的な解決と一括の解決の一致をテストしている。
- **攻撃は両者の Tick の後に届ける。** 同じ Tick の攻撃が処理の順序に左右されない。
- **CPU はプレイヤーと同じ操作だけを使う。** 盤面を直接書き換えず、`GameCommand` を一定の間隔で発行する。
- **Core は整数だけで動かす。** 同じシードと入力列の最終盤面を固定のハッシュ値で照合し、Mono と .NET の一致を確かめている。
- **物理エンジンは使わない。** 落下と衝突はすべて Core のマス目で判定する。

設定アセットは `Assets/Game/Unity/SporeBloomSettings.asset`。落下速度、接地猶予、演出時間、得点、攻撃量、CPU の強さ、長押しの間隔、音量を調整できる。

シーンの再生成：

```
unity run games/014-puyo-puyo -- -executeMethod Game.Unity.Editor.SporeBloomSceneBuilder.Build
```

素材は `SporeBloomArtGenerator` が図形から描き出した PNG。再生成は既存のファイル、そのインポート設定、Prefab を上書きしない。

## テスト

| 種類 | 場所 | 件数 | 実行方法 |
|---|---|---:|---|
| Core | `Assets/Tests/Core/` | 86 | `dev test` |
| App | `Assets/Tests/App/` | 47 | `dev test` |
| EditMode（Core/App を Mono で実行） | 同上 | 133 | `dev test 014 -Unity` |
| PlayMode（実シーン21、共通証跡16） | `Assets/Tests/Unity/` | 37 | `dev test 014 -Unity -Mode PlayMode` |

結果と画面証跡は [evidence/README.md](evidence/README.md) に記録している。

## 未実装範囲

仕様書の Out of scope どおり、競技ルールの全再現、高度な連鎖 AI、大量のアセット制作、追加ステージ、課金・実績・外部ランキング。加えて、ネットワーク対戦、難易度の選択、BGM、スコアの保存は採用していない。

証跡画像はJPEG（`.jpg`）、品質85を標準とし、保存したJPEGのSHA-256をマニフェストに記録する。
