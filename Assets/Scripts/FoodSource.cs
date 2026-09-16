using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 地表に置かれた餌1個（行動モデル.md 5章）。段階3は糖分1種類。
/// アリが1匹食べるたびに残量が1減り、0になると消える。
/// </summary>
[DisallowMultipleComponent]
public class FoodSource : MonoBehaviour
{
    [SerializeField] private SpriteRenderer spriteRenderer;

    [Header("見た目（仮の丸。素材は段階3の終わりに差し替える）")]
    [Tooltip("残量1のときの大きさ（cm）")]
    [SerializeField] private float sizeAtOne = 0.25f;
    [Tooltip("残量が最大のときの大きさ（cm）")]
    [SerializeField] private float sizeAtFull = 0.75f;
    [Tooltip("この残量で最大の大きさになる")]
    [SerializeField] private int fullAmount = 8;

    private int amount;

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

    /// <summary>残量を決めて置く。</summary>
    public void Setup(int initialAmount)
    {
        amount = Mathf.Max(0, initialAmount);
        ApplySize();
    }

    /// <summary>アリが1匹分food食べる。食べられたら true。</summary>
    public bool TakeOne()
    {
        if (amount <= 0) return false;
        amount--;
        if (amount <= 0)
        {
            Destroy(gameObject);
            return true;
        }
        ApplySize();
        return true;
    }

    /// <summary>残量に応じて滴の大きさを変える。</summary>
    private void ApplySize()
    {
        if (spriteRenderer == null) return;
        float t = fullAmount > 1 ? Mathf.Clamp01((amount - 1f) / (fullAmount - 1f)) : 1f;
        float size = Mathf.Lerp(sizeAtOne, sizeAtFull, t);

        // スプライト1枚の大きさを size（cm）にそろえる
        Sprite sprite = spriteRenderer.sprite;
        float spriteSize = sprite != null ? Mathf.Max(sprite.bounds.size.x, sprite.bounds.size.y) : 1f;
        if (spriteSize <= 0f) spriteSize = 1f;
        float scale = size / spriteSize;
        spriteRenderer.transform.localScale = new Vector3(scale, scale, 1f);
    }
}
