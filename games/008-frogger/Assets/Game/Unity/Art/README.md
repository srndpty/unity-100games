# 008 制作素材

組み込みimage_genを使用。Frog.png・Car.png・Log.pngは透過PNG、Board.jpgは盤面のイラスト（容量制限に合わせJPEG品質95へ変換）。Car.pngは画素とアルファを保持した可逆PNG再圧縮を適用。生成後にこのディレクトリへコピーし、透過を保持してUnityへインポートした。

FroggerStage.prefabが見た目の配置の正。再生成は既存Prefabを上書きしない。Mask.pngはエディタ生成の白い矩形で、盤面外の動く物体を隠すために使う。生成画像の余白を含めて表示サイズを設定する。カエルの判定は胴体の足元、四肢の装飾は判定外。

以下は使用した全プロンプト。

## frog

```text
Use case: stylized-concept. Asset type: transparent Unity 2D game sprite. Primary request: one charming green frog viewed strictly from directly overhead, facing upward, four splayed legs, compact readable silhouette, hand painted storybook game illustration with clean edges and subtle texture. Centered, fills 85 percent of square canvas. Genuine transparent background. No text, no floor, no shadow outside silhouette, no other objects.
```

## car

```text
Use case: stylized-concept. Asset type: transparent Unity game sprite. One coral red compact car seen strictly from directly overhead, front pointing right. Hand painted storybook illustration, clean rectangular readable silhouette, soft detailed brush texture, dark tires, blue glass, friendly vintage compact proportions. Isolated centered with generous transparent margins, horizontal car aspect ratio about 2:1, no ground shadows, no text, no logos, no background, no other objects. Genuine transparent background.
```

## log

```text
Use case: stylized-concept. Asset type: transparent Unity game sprite. One floating wooden log seen directly from above, horizontal left to right. Long straight rectangular brown bark body with rounded cut ends, width to height ratio 4:1. Hand painted storybook game art, warm golden wood, textured bark and subtle moss, clean readable silhouette. Centered with transparent margins, no water, no external shadow, no text, no extra objects. Genuine transparent background.
```

## board

```text
Use case: stylized-concept. Asset type: Unity 2D game board background, strictly top-down orthographic painted illustration. A rectangular 11-column by 9-row crossing board surrounded by a narrow dark green botanical frame, width to height 11:9. Nine equally tall horizontal bands from BOTTOM to TOP: grassy starting bank one row; gray asphalt road three rows with subtle dashed cream dividers between lanes; grassy median bank one row; teal river three rows with subtle horizontal ripple lanes; grassy destination bank one row. All lanes perfectly horizontal, equal row heights. Exact board extent clearly visible with straight sides. Warm hand painted gouache style matching a storybook frog crossing game, textured grass, tiny flowers along banks, soft readable asphalt, luminous teal water. NO frog, NO cars, NO logs, NO text, NO lettering, NO perspective, NO HUD. Background fills image. Keep decorative plants outside playable road and water.
```
