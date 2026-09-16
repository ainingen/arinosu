# 餌のスプライト（v0.1）

糖液（砂糖水・甘露に相当）が地面に乗った粒を、横から見た形。
アリと違い、色は画像に焼き込み済み。SpriteRenderer.color は白（1,1,1）のまま使う。

## ファイル

| ファイル | 中身 |
|---|---|
| food_drop.png | 512×256。Unity で使う |
| food_drop.svg | 修正用の元データ（Unity には取り込まない） |

## インポート設定

- Texture Type: Sprite (2D and UI) / Sprite Mode: Single
- Pixels Per Unit: **512**（そのままの大きさで横幅＝1 Unity単位）
- Pivot: Custom **(0.5, 0.0625)** ＝粒の底辺の中央。地表面の高さに置くと、地面にちょうど乗る
- Filter Mode: Bilinear / Generate Mip Maps: オン / Compression: None

## 大きさ

- 横幅を「直径」とみなし、残量に応じて AntSettings の foodMinDiameter〜foodMaxDiameter に拡大縮小する
- 縦横比は変えない（transform.localScale を x・y 同じ値に）

## 置き換えるもの

- 今の仮の丸（生成スプライト・琥珀色の着色・輪郭線）は不要になる
- 残量の数字表示（Shift+1 と連動）はそのまま残す
- 描画順は、土より前・アリより後ろ（アリが粒の上に乗って見えるように）
