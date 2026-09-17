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

    [Header("素材（仮の図形）")]
    [SerializeField] private Sprite eggSprite;
    [SerializeField] private Sprite larvaSprite;
    [SerializeField] private Sprite pupaSprite;

    [Header("色")]
    [SerializeField] private Color eggColor = new Color(0.97f, 0.96f, 0.90f, 1f);
    [SerializeField] private Color larvaColor = new Color(0.98f, 0.94f, 0.82f, 1f);
    [SerializeField] private Color pupaColor = new Color(0.78f, 0.60f, 0.35f, 1f);

    [Header("描き方")]
    [Tooltip("土より前・アリより後ろ")]
    [SerializeField] private int sortingOrder = -1;
    [Tooltip("1マスの中でずらす幅（cm）")]
    [SerializeField] private float scatter = 0.06f;
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
            ApplyLook(renderer, item, drawn);
        }

        // 余ったぶんは隠す
        for (int i = usedCount; i < pool.Count; i++) pool[i].enabled = false;
    }

    private void ApplyLook(SpriteRenderer renderer, BroodItem item, int indexInCell)
    {
        Sprite sprite;
        Color color;
        float size;

        switch (item.stage)
        {
            case BroodStage.Egg:
                sprite = eggSprite; color = eggColor; size = settings.eggSize;
                break;
            case BroodStage.Larva:
                sprite = larvaSprite; color = larvaColor;
                size = Mathf.Lerp(settings.larvaSizeMin, settings.larvaSizeMax, Mathf.Clamp01(item.size));
                break;
            default:
                sprite = pupaSprite; color = pupaColor; size = settings.pupaSize;
                break;
        }

        renderer.enabled = true;
        renderer.sprite = sprite;
        renderer.color = color;

        // マスの中で少しずつずらして、塊に見せる
        float angle = indexInCell * 90f + 45f;
        Vector2 offset = new Vector2(Mathf.Cos(angle * Mathf.Deg2Rad), Mathf.Sin(angle * Mathf.Deg2Rad))
            * (indexInCell == 0 ? 0f : scatter);

        Vector2 center = grid.CellToWorld(item.cellX, item.cellY) + offset;
        renderer.transform.position = new Vector3(center.x, center.y, 0f);

        float spriteSize = sprite != null ? Mathf.Max(sprite.bounds.size.x, sprite.bounds.size.y) : 1f;
        if (spriteSize <= 0f) spriteSize = 1f;
        float scale = size / spriteSize;
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
