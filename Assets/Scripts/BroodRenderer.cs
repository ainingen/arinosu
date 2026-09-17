using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 子どもをマスごとの塊として描く（行動モデル.md 13-8）。
/// 子ども1個ずつに GameObject は持たせず、使い回すスプライトで描く。
/// </summary>
[DisallowMultipleComponent]
public class BroodRenderer : MonoBehaviour
{
    [SerializeField] private SoilGrid grid;
    [SerializeField] private BroodField broodField;
    [SerializeField] private BroodSettings settings;

    [Header("素材（art/brood/README.md）")]
    [Tooltip("色は素材に焼き込み済み。SpriteRenderer.color は白のまま使う")]
    [SerializeField] private Sprite eggSprite;
    [SerializeField] private Sprite larvaSprite;
    [SerializeField] private Sprite cocoonSprite;

    [Header("描き方")]
    [Tooltip("土より前・アリより後ろ。繭→幼虫→卵 の順に前へ出す")]
    [SerializeField] private int sortingOrder = -3;
    [Tooltip("マスの中心からずらす幅（cm）。実寸だと繭は1マスより大きいので広めに散らす")]
    [SerializeField] private float scatter = 0.25f;
    [Tooltip("幼虫がいちばん小さいとき（size = 0）の倍率")]
    [SerializeField, Range(0.05f, 1f)] private float larvaMinScale = 0.3f;
    [Tooltip("作り直す間隔（秒）。子どもの数が変わらなければ描き直さない")]
    [SerializeField] private float refreshInterval = 0.2f;

    private readonly List<SpriteRenderer> pool = new List<SpriteRenderer>();
    private int usedCount;
    private int lastVersion = -1;
    private float timer;

    private void Awake()
    {
        if (grid == null) grid = FindFirstObjectByType<SoilGrid>();
        if (broodField == null) broodField = FindFirstObjectByType<BroodField>();
    }

    private void LateUpdate()
    {
        if (broodField == null || grid == null || settings == null) return;

        timer -= Time.unscaledDeltaTime;
        bool changed = broodField.Version != lastVersion;
        if (!changed && timer > 0f) return;

        timer = refreshInterval;
        lastVersion = broodField.Version;
        Redraw();
    }

    /// <summary>今ある子どもを描き直す。</summary>
    private void Redraw()
    {
        usedCount = 0;

        var all = broodField.All;
        // マスごとに何個目かを数えながら、少しずつずらして描く
        var drawnInCell = new Dictionary<int, int>();

        for (int i = 0; i < all.Count; i++)
        {
            BroodItem item = all[i];
            int key = item.cellY * grid.Width + item.cellX;

            int drawn;
            drawnInCell.TryGetValue(key, out drawn);
            if (drawn >= settings.maxDrawnPerCell) continue;
            drawnInCell[key] = drawn + 1;

            SpriteRenderer renderer = Take();
            ApplyLook(renderer, item);
        }

        // 余ったぶんは隠す
        for (int i = usedCount; i < pool.Count; i++) pool[i].enabled = false;
    }

    /// <summary>
    /// 1個ぶんの見た目を決める。
    /// 素材は実寸で作ってあるので（PPU で調整済み）、倍率は等倍が基本。
    /// 幼虫だけは育ち具合 size で大きくなる。
    /// </summary>
    private void ApplyLook(SpriteRenderer renderer, BroodItem item)
    {
        Sprite sprite;
        float scale = 1f;
        int order;

        switch (item.stage)
        {
            case BroodStage.Egg:
                sprite = eggSprite;
                order = sortingOrder + 2;
                break;
            case BroodStage.Larva:
                sprite = larvaSprite;
                scale = Mathf.Lerp(larvaMinScale, 1f, Mathf.Clamp01(item.size));
                order = sortingOrder + 1;
                break;
            default:
                sprite = cocoonSprite;
                order = sortingOrder;
                break;
        }

        renderer.enabled = true;
        renderer.sprite = sprite;
        renderer.color = Color.white;     // 色は素材に焼き込み済み
        renderer.sortingOrder = order;    // 小さいものが上に見えるように

        // ずらし方と向きは子ども1個ごとに決まっている（毎フレーム変えない）
        Vector2 center = grid.CellToWorld(item.cellX, item.cellY) + item.drawUnitOffset * scatter;
        renderer.transform.position = new Vector3(center.x, center.y, 0f);
        renderer.transform.localRotation = Quaternion.Euler(0f, 0f, item.drawAngle);
        renderer.transform.localScale = new Vector3(scale, scale, 1f);
    }

    /// <summary>使い回しのスプライトを1枚借りる。</summary>
    private SpriteRenderer Take()
    {
        if (usedCount < pool.Count) return pool[usedCount++];

        var go = new GameObject("Brood");
        go.transform.SetParent(transform, false);
        var renderer = go.AddComponent<SpriteRenderer>();
        renderer.sortingOrder = sortingOrder;
        pool.Add(renderer);
        usedCount++;
        return renderer;
    }
}
