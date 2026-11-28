# 054 リーチ麻雀

Unity 6000.6.9f1、2D URP、Windows x64。人間1人とCPU3人で四人打ちの東風戦を行う。

## 起動と操作

- リポジトリルートで `dev build 054`、続いて `dev run 054`。
- 自分の手牌をクリックして打牌する。ツモ牌は右端に離して表示する。打牌できるときは、手牌のまとまりを残す目安として候補を金枠で表示する。リーチ打牌はCPUと同じく、自分の手牌・河・副露・ドラ表示牌から残り枚数を推定して待ちを比較する。
- ロン・チー・ポン・カンなどが可能なときは対局操作欄に表示する。チーは使う順子を選び、鳴かない場合は、操作欄または画面右下の「見送り」を押す。
- リーチは「リーチ」を押してから有効な打牌を選ぶ。ウィンドウのフォーカスを失うと停止し、戻ったら「対局を再開」を押す。
- 局結果で点数の移動を確認して「進む」を押す。東風戦の最終順位から再戦できる。

## 採用ルール

| 項目 | 裁定 |
|---|---|
| 卓 | 34種136枚、赤なし。25,000点持ち、人間が起家 |
| 進行 | 親和了・親聴牌流局で連荘。東4局で親が流れるか、精算後に0点未満の席があれば終了。同点は起家からの席順 |
| 鳴き | ロン、ポン・大明槓、チーの順。チーは上家のみ。同順位は放銃者からの手番順。食い替え禁止 |
| リーチ | 門前聴牌、1,000点以上、山残り4枚以上。宣言打牌へのロンがなければ成立。河の金色は宣言した牌、席の「立直」は成立を示す。鳴かれた宣言牌は河に半透明で残す |
| 振聴 | 自分の河、同巡の見逃し、リーチ後の見逃しでロン不可。同巡振聴は次の自摸で解除。ツモ和了は可能。自身の河は役の有無を問わず待ち全体を確認する。他家の打牌の見逃しは、その時点でロン可能だった場合に振聴となる |
| カン | 暗槓・明槓・加槓を合計4回まで。嶺上牌と槓ドラあり。槍槓は加槓のみ。リーチ後の暗槓は自摸牌を使い、待ちと和了形を維持する場合に限る |
| 精算 | 3人まで複数ロン。各和了者に本場を支払い、供託は放銃者から最も近い和了者へ。流局の聴牌料は計3,000点。終局の残り供託は首位へ |
| 卓ルール | 西入、アガリやめ、途中流局、流し満貫、責任払い、待ちによるダブル役満を採用しない |

採用役はリーチ、ダブルリーチ、一発、門前ツモ、断么九（喰いタンあり）、平和、一盃口、二盃口、役牌、七対子、対々和、三暗刻、三槓子、三色同順、三色同刻、一気通貫、混全帯么九、純全帯么九、混老頭、小三元、混一色、清一色、海底、河底、嶺上開花、槍槓。採用役満は国士無双・四暗刻・大三元で、複合時は加算する。ドラと裏ドラは役を満たした手に加算する。裏ドラはリーチ和了時に限る。

通常形の符は10符単位、支払いは100点単位で切り上げる。七対子25符、平和ツモ20符、喰い平和形30符、連風牌の雀頭4符。切り上げ満貫なし。満貫・跳満・倍満・三倍満・数え役満の基本点は順に2,000、3,000、4,000、6,000、8,000点。判定の参照資料は[EMA公式ルール](https://mahjong-europe.org/portal/images/docs/Riichi-rules-2025-EN.pdf)。卓の裁定は上の表に従う。

## 牌画像

標準のImageGenで生成した図柄と牌の地を、[描画用アトラス](Assets/Game/Unity/MahjongTiles.png)にまとめた。1〜9萬、1〜9筒、1〜9索、字牌7種を照合し、7筒・7索は生成された図柄のパーツを組み直して数を合わせた。手牌・河・副露・結果画面で同じ図柄を使う。対面のCPUは裏向きの牌を横に並べ、左右のCPUは裏向きの牌を左右逆向きに90度回転させ、卓に沿って詰めて並べる。公開情報である手牌の枚数だけを使う。上家は画面左、下家は右に置く。副露では横向きの牌の位置で鳴き元を表し、加槓は横向き牌を重ね、暗槓は両端を裏向きにする。

図柄と裏面の生成プロンプト

> Create ONE precise game sprite atlas, landscape 3:2 canvas, strict 9 equal columns × 4 equal rows, evenly spaced cell centers like graph paper, pure solid white paper, no dividers or tiles or captions. Each cell contains only one centered traditional Japanese riichi mahjong face DESIGN, large clear ink with generous surrounding white gutter, consistent traditional mahjong art, upright. Exact indexing left to right: row 1 nine red/black two-character designs 一萬, 二萬, 三萬, 四萬, 五萬, 六萬, 七萬, 八萬, 九萬. Row 2 nine pinzu designs: exactly 1,2,3,4,5,6,7,8,9 colored circular dot pips respectively in each of the successive nine cells, arranged in traditional clear grids or diagonals, red/blue/green. Row 3 nine souzu designs: a stylized green/red sparrow in cell 1 followed by exactly 2,3,4,5,6,7,8,9 separated green/red bamboo stick pips in the respective successive cells. Row 4 exactly these nine designs 東, 南, 西, 北, an empty blue rectangular frame for white dragon, green 發, red 中, a single blank jade-green mahjong tile seen from rear slightly above with narrow ivory cap, a single blank jade-green mahjong tile seen from the side slightly above with narrow ivory cap. No other designs. Clean modern commercial game art in traditional painted ink styles. The regular cell grid is essential for automatic extraction.


## 設計

Coreは物理牌ID、捨て牌履歴、副露、山、席ごとの状態を保持。局面番号と合法操作を照合し、全席の応答を待って鳴き・ロンを裁定する。和了判定は面子分解と和了牌の割当を列挙し、精算点が最大の組み合わせを採る。Appは画面遷移と入力キュー、公開情報と自身の手牌で動く簡易CPUを持つ。UnityはUI Toolkitで牌と卓を表示し、設定アセットでCPU間隔と音量を調整する。

シーン再生成は `unity run games/054-riichi-mahjong -- -executeMethod Game.Unity.Editor.MahjongSceneBuilder.Build`。

[動作証跡と検証結果](evidence/README.md)
