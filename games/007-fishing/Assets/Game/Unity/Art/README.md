# 007 アート素材

組み込みimagegenを使用して2026-11-18に生成。画像のアルファは保持する。魚はSprite Editorで3行をスライスし、口元をピボットにする。背景はリポジトリの2MB上限に収めるためJPEG品質95へ形式変換し、人物・魚は元のPNGアルファを保持する。

- `Lakeside.jpg`：湖畔・桟橋・水中の背景。
- `Angler.png`：右向きの釣り人。竿・糸はLineRendererで接続する。
- `AnglerStruggle.png`：同じ人物の踏ん張り姿勢。ファイトと取り込み中に切り替える。元の人物画像を参照して組み込みimagegenで編集し、アルファを保持した。
- `FishAtlas.png`：上からコブナ・スズキ・コイ。
- `Disc.png`：ウキ・餌用の小円。エディタースクリプトで生成する単純形状。

`FishingStage.prefab`は編集用の正。Sceneビュー／Prefabモードで人物・竿の持ち手・竿先・描画物の配置を調整できる。シーン再生成は既存Prefabを読み、上書きしない。魚・ウキ・糸の実行時位置はFishingViewが状態から更新する。

人物の配置は`AnglerAnchor`、踏ん張りの動きはその子`AnglerRig`で分離する。人物・通常／踏ん張り用の持ち手と竿先・竿本体は同じRigに属し、糸の始点も毎Tickその竿先へ追従する。Rigの揺れ幅・周波数・角度はPrefabのFishingViewで調整する。

## 生成プロンプト（原文）

### 踏ん張り姿勢（Angler.pngを参照した編集）

Use case: identity-preserve. Asset type: second animation pose sprite for a 2D side-view fishing game. Image 1 is the edit target and exact identity/style/costume reference. Keep the same adult angler, face, straw hat, cream shirt, rust vest, sage trousers, brown boots, right-facing side view, full body, same canvas dimensions and character scale. Change only the pose and facial expression: he is now fighting a strong fish, knees bent in a convincing braced stance, hips lowered slightly, torso leaning BACK to the LEFT, both hands gripping together in front near chest height, elbows pulled back, determined strained expression, planted boots. Keep the boot soles at the same baseline as the original and make the hands clearly visible so a separately drawn fishing rod can attach. No fishing rod, no line, no fish, no background, no floor, no cast shadow or glow outside the character. Genuine clean transparent alpha background. Preserve the painterly shading and palette exactly. No text, labels, border or watermark.

### 背景

Use case: stylized-concept. Asset type: finished 2D side-view fishing game environment background, landscape 1536x1024. Create a polished hand-painted illustrated lakeside cutaway for a playable game, not a mockup. Strict orthographic side elevation, waterline exactly 38 percent down the canvas. At the far LEFT 18 percent: a small solid grassy earthen bank with an inviting wooden fishing dock extending slightly right; top of dock exactly at the waterline, clear room on it for a separately placed angler. Above water: layered distant tree-lined lakeshore, soft hills, warm pale morning sky. Below water: a broad open teal underwater cross-section, subtle light shafts, gravel lakebed along very bottom, tasteful aquatic plants near edges. Central and right underwater area uncluttered and light enough to clearly read separate moving fish. Elegant cozy indie game art, painterly detail, coherent lighting, soft natural gold, sage and deep turquoise. No fish, no people, no rods, no fishing line, no UI, no labels, no text, no watermark. Edge to edge art. This is an actual game background texture.

### 人物

Use case: stylized-concept. Asset type: transparent 2D side-view game character sprite. One friendly adult angler standing, facing RIGHT, full body entirely visible. Natural fishing stance: boots on same ground plane, knees relaxed, both hands held together in front at waist/chest level as if gripping a fishing rod; do NOT draw a rod, line or fish because the game adds an articulated rod. Straw fishing hat, cream shirt with rolled sleeves, muted rust fishing vest, sage trousers, brown boots. Polished hand-painted cozy indie fishing game art with crisp readable silhouette and restrained warm details, soft upper-left daylight. Side-on orthographic character, no perspective floor. Character centered and fills canvas vertically with 8 percent clear margins. Real transparent alpha background. No ground, scenery, other characters, text, frames or watermark.

### 魚アトラス

Use case: stylized-concept. Asset type: transparent sprite atlas for a 2D side-view fishing game. Square 1024x1024 canvas, exactly three distinct fish in three separate horizontal rows, one fish per row, evenly centered, all facing LEFT and seen perfectly side-on. Top row small golden-silver crucian carp with deep oval body and clearly visible fins and tail. Middle row elongated silver-green Japanese sea bass with dark dorsal fin. Bottom row larger warm copper common carp with thick body, distinct scales and subtle barbels. Each fish fits entirely inside its own third of the canvas with generous transparent gaps and margins; bodies horizontal, mouths at left and tails at right, no overlap, no splash or shadow outside silhouette. Hand-painted polished cozy indie game sprites, clean attractive silhouettes, detailed fins and scales but readable at small game sizes. Soft daylight matching a warm natural lakeside. True transparent alpha everywhere outside the three fish. No text, labels, outlines around cells, grid, borders, scenery, hooks or watermark.
