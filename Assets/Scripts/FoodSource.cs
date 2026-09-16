using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 地表に置かれた餌1個（行動モデル.md 5章）。段階3は糖分1種類。
/// アリが1匹食べるたびに残量が1減り、0になると消える。
/// 見た目は仮の丸（本体＋輪郭）。素材は段階3の終わりに差し替える。
/// </summary>
[DisallowMultipleComponent]
public class FoodSource : MonoBehaviour
{
    [Header("パーツ")]
    [SerializeField] private SpriteRenderer spriteRenderer;
    [Tooltip("本体の後ろに一回り大きく描く輪郭")]
    [SerializeField] private SpriteRenderer outlineRenderer;
    [Tooltip("残量の数字（デバッグ表示。F1 と同じ切替で出る）")]
    [SerializeField] private TMPro.TMP_Text amountLabel;

    [Header("色")]
    [SerializeField] private Color bodyColor = new Color(0.961f, 0.722f, 0.180f, 1f);
    [SerializeField] private Color outlineColor = new Color(0.20f, 0.12f, 0.02f, 1f);
    [Tooltip("輪郭の太さ（本体に対する倍率）")]
    [SerializeField] private float outlineScale = 1.22f;

    [Header("大きさ")]
    [Tooltip("この残量で最大の大きさになる")]
    [SerializeField] private int fullAmount = 8;
    [Tooltip("AntSettings が渡されなかったときに使う最小直径（cm）")]
    [SerializeField] private float fallbackMinDiameter = 0.8f;
    [Tooltip("AntSettings が渡されなかったときに使う最大直径（cm）")]
    [SerializeField] private float fallbackMaxDiameter = 1.6f;
    [Tooltip("数字を餌の上に置く高さの余白（cm）")]
    [SerializeField] private float labelMargin = 0.18f;

    private AntSettings settings;
    private int amount;
    private bool labelVisible;

    /// <summary>今ある餌の一覧（アリが近くの餌を探すのに使う）。</summary>
    private static readonly List<FoodSource> all = new List<FoodSource>();
    public static IReadOnlyList<FoodSource> All => all;

    /// <summary>残量（アリ何匹分か）。</summary>
    public int Amount => amount;
    /// <summary>置かれている場所。</summary>
    public Vector2 Position => transform.position;

    private void Awake()
    {
        if (spriteRenderer == null) spriteRenderer = GetComponentInChildren<SpriteRenderer>();
    }

    private void OnEnable()
    {
        if (!all.Contains(this)) all.Add(this);
    }

    private void OnDisable()
    {
        all.Remove(this);
    }

    private void Update()
    {
        // 残量の数字は道しるべの重ね表示（F1）と一緒に出す
        bool shouldShow = FieldDebugOverlay.ShowFoodAmounts;
        if (shouldShow == labelVisible) return;
        labelVisible = shouldShow;
        if (amountLabel != null) amountLabel.gameObject.SetActive(shouldShow);
    }

    /// <summary>残量を決めて置く。</summary>
    public void Setup(int initialAmount, AntSettings antSettings = null)
    {
        amount = Mathf.Max(0, initialAmount);
        if (antSettings != null) settings = antSettings;
        ApplyLook();
    }

    /// <summary>アリが1匹分食べる。食べられたら true。</summary>
    public bool TakeOne()
    {
        if (amount <= 0) return false;
        amount--;
        if (amount <= 0)
        {
            Destroy(gameObject);
            return true;
        }
        ApplyLook();
        return true;
    }

    /// <summary>残量に応じて大きさと数字を作り直す。</summary>
    private void ApplyLook()
    {
        float minDiameter = settings != null ? settings.foodMinDiameter : fallbackMinDiameter;
        float maxDiameter = settings != null ? settings.foodMaxDiameter : fallbackMaxDiameter;
        if (maxDiameter < minDiameter) maxDiameter = minDiameter;

        // 残量に比例させるが、小さくなりすぎないよう最小を守る
        float t = fullAmount > 1 ? Mathf.Clamp01((amount - 1f) / (fullAmount - 1f)) : 1f;
        float diameter = Mathf.Max(minDiameter, Mathf.Lerp(minDiameter, maxDiameter, t));

        ApplyRenderer(spriteRenderer, bodyColor, diameter);
        ApplyRenderer(outlineRenderer, outlineColor, diameter * outlineScale);

        if (amountLabel != null)
        {
            amountLabel.text = amount.ToString();
            amountLabel.transform.localPosition = new Vector3(0f, diameter * 0.5f + labelMargin, 0f);
        }
    }

    /// <summary>スプライト1枚の見かけの大きさを diameter（cm）にそろえる。</summary>
    private void ApplyRenderer(SpriteRenderer renderer, Color color, float diameter)
    {
        if (renderer == null) return;
        renderer.color = color;

        Sprite sprite = renderer.sprite;
        float spriteSize = sprite != null ? Mathf.Max(sprite.bounds.size.x, sprite.bounds.size.y) : 1f;
        if (spriteSize <= 0f) spriteSize = 1f;
        float scale = diameter / spriteSize;
        renderer.transform.localScale = new Vector3(scale, scale, 1f);
    }
}
