# 女王と子どものスプライト（v0.1）

## ファイル

| ファイル | 中身 | 色 | Assets の置き場所 |
|---|---|---|---|
| queen_body.png | 女王の胴体（頭が上。羽の痕・単眼つき） | 白（着色して使う） | Assets/Sprites/Ant/ |
| brood_egg.png | 卵 | 焼き込み済み | Assets/Sprites/Brood/ |
| brood_larva.png | 幼虫（C字） | 焼き込み済み | Assets/Sprites/Brood/ |
| brood_cocoon.png | 繭（尾端に黒い点＝胎便） | 焼き込み済み | Assets/Sprites/Brood/ |

SVG は修正用の元データ（Unity には取り込まない。art/brood/ に置く）。

## インポート設定（共通）

- Texture Type: Sprite (2D and UI) / Sprite Mode: Single
- Filter Mode: Bilinear / Generate Mip Maps: オン / Compression: None
- Pivot: すべて中央 (0.5, 0.5)

| ファイル | Pixels Per Unit | 実物の大きさ（1 Unity単位 = 1cm） |
|---|---|---|
| queen_body.png | **602** | 体長 1.7cm |
| brood_egg.png | **1600** | 長さ 0.12cm |
| brood_larva.png | **394** | 幅 0.65cm（size = 1 のとき。size で拡大縮小、最小 0.3 倍【仮】） |
| brood_cocoon.png | **349** | 長さ 1.1cm |

## 女王の組み立て

胴体は queen_body.png。脚と触角は**働きアリの部品（ant_leg_*.png / ant_antenna.png）をそのまま使い、localScale を 1.35 にする**。
働きアリと同じく、左側は x を負・角度の符号を反転・flipX。色は働きアリと同じ方法で着色（通常＝黒、選択＝赤）。

| パーツ | 位置 (x, y)（胴体の中心から、Unity単位、右側） | 角度（Z回転） | localScale | 描画順 |
|---|---|---|---|---|
| 触角 | (0.077, 0.680) | +55° | 1.35 | 胴体より下 |
| 前脚 | (0.119, 0.408) | +40° | 1.35 | 胴体より下 |
| 中脚 | (0.111, 0.221) | -5° | 1.35 | 胴体より下 |
| 後脚 | (0.102, 0.102) | -40° | 1.35 | 胴体より下 |

歩行アニメは働きアリと同じ三脚歩行（女王はほとんど歩かないので、振れ幅はそのままでよい）。

## 子どもの描き方

- 色は焼き込み済みなので SpriteRenderer.color は白のまま
- 実物の大きさで描くと、繭（1.1cm）は1マス（0.2cm）より大きい。塊は1マスに収めず、**マスの中心から ±0.25cm【仮】の範囲に少しずつずらして重ねる**。ずらし方は子ども1個ごとに固定（毎フレーム変えない）
- 向きは子ども1個ごとにランダムな角度で固定
- 重なりの順は 繭 → 幼虫 → 卵（小さいものが上）
- 運ばれている子どもは、運んでいるアリの顎の先に、実物の大きさで描く
- 今の「内側が明るく縁が暗い」仮の図形は不要になる（素材に輪郭が含まれている）
