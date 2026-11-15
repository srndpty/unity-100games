# 最大設定の負荷計測

2026-11-16、Unity 6000.6.9f1のバッチPlayModeテストで実施。

## 条件・予算

- CPU：13th Gen Intel Core i9-13900KF。Windows x64 Editor / Mono。
- バッチゲームビュー640×480。横長画面での収まりは別のカメラテスト、Windowsビルドは1280×720の証跡で確認する。
- 設定上限の10列×5行・敵50体、盾216セル、自弾8発・敵弾24発。速度・下降量は設定上限、射撃間隔は設定下限。
- 全生成後、各サンプルで弾32発を補充し、Coreを1Tick更新してSpriteRendererへ反映する。300回のCPU時間を測る。
- 予算：CPU p95が16.67ms未満、SpriteRenderer数356以下。画面出力・GPU処理・起動時間は含めない。

## 実測（レビュー修正後）

| 指標 | 結果 |
|---|---|
| CPU p50 | 1.256ms |
| CPU p95 | 1.668ms |
| SpriteRenderer | 356個 |

`InvadersSceneTests.MaximumSettings_CpuAndRendererBudget`で回帰検証する。生成はウォームアップで行い、計測中は使い回す。GPU時間や他のPCのFPSを保証する値ではない。
