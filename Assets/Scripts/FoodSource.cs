using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 地表に置かれた餌1個（行動モデル.md 5章）。段階3は糖分1種類。
/// アリが1匹食べるたびに残量が1減り、0になると消える。
/// 見た目は art/food/README.md の粒の素材。ピボットが粒の底辺にあるので、
/// 地表面の高さに置くと地面にちょうど乗る。
/// </summary>
[DisallowMultipleComponent]
public class FoodSource : MonoBehaviour
{
    [Header("パーツ")]
    [Tooltip("粒の絵。色は画像に焼き込み済みなので、色は白のまま使う")]
    [SerializeField] private SpriteRenderer spriteRenderer;
    [Tooltip("残量の数字（デバッグ表示。Shift+1 と同じ切替で出る）")]
    [SerializeField] private TMPro.TMP_Text amountLabel;

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
    private GameClock clock;
    private int amount;
    private bool labelVisible;
    /// <summary>この餌が現れた日。寿命の判定に使う</summary>
    private double spawnedAtDays;

    /// <summary>今ある餌の一覧（アリが近くの餌を探すのに使う）。</summary>
    private static readonly List<FoodSource> all = new List<FoodSource>();
    public static IReadOnlyList<FoodSource> All => all;

    /// <summary>食べ切られずに乾いて消えた数（デバッグ表示用）。</summary>
    private static int expiredCount;
    public static int ExpiredCount => expiredCount;

    /// <summary>アリが食べた回数の累計（＝持ち帰りの回数）。</summary>
    private static int takenCount;
    public static int TakenCount => takenCount;

    /// <summary>数え上げを 0 に戻す（プレイ開始時に FoodSpawner から呼ぶ）。</summary>
    public static void ResetCounters()
    {
        expiredCount = 0;
        takenCount = 0;
    }

    /// <summary>残量（アリ何匹分か）。</summary>
    public int Amount => amount;
    /// <summary>置かれている場所。</summary>
    public Vector2 Position => transform.position;

    private void Awake()
    {
        if (spriteRenderer == null) spriteRenderer = GetComponentInChildren<SpriteRenderer>();
        if (clock == null) clock = FindFirstObjectByType<GameClock>();
        if (clock != null) spawnedAtDays = clock.ElapsedDays;
    }

    /// <summary>現れてからの日数。</summary>
    public float AgeDays => clock != null ? (float)(clock.ElapsedDays - spawnedAtDays) : 0f;

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
        // 乾いて消える（行動モデル.md 5章）。
        // 食べ切られない餌がいつまでも残ると、上限の枠を占めて新しい餌が出なくなる
        if (settings != null && clock != null && settings.foodLifetimeDays > 0f
            && AgeDays >= settings.foodLifetimeDays)
        {
            expiredCount++;
            Destroy(gameObject);
            return;
        }

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

    /// <summary>地面の上へ置き直す（塚に埋まったときに押し上げるのに使う）。</summary>
    public void PlaceOnGround(Vector2 groundSurface)
    {
        transform.position = new Vector3(groundSurface.x, groundSurface.y, transform.position.z);
    }

    /// <summary>アリが1匹分食べる。食べられたら true。</summary>
    public bool TakeOne()
    {
        if (amount <= 0) return false;
        amount--;
        takenCount++;
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

        float scale = ApplyRenderer(diameter);

        if (amountLabel != null)
        {
            amountLabel.text = amount.ToString();
            amountLabel.transform.localPosition = new Vector3(0f, HeightAboveOrigin(scale) + labelMargin, 0f);
        }
    }

    /// <summary>
    /// 横幅を直径とみなして、粒の見かけの大きさをそろえる。縦横比は変えない。
    /// 色は画像に焼き込み済みなので触らない（白のまま）。
    /// </summary>
    private float ApplyRenderer(float diameter)
    {
        if (spriteRenderer == null) return 1f;

        Sprite sprite = spriteRenderer.sprite;
        float spriteWidth = sprite != null ? sprite.bounds.size.x : 1f;
        if (spriteWidth <= 0f) spriteWidth = 1f;

        float scale = diameter / spriteWidth;
        spriteRenderer.transform.localScale = new Vector3(scale, scale, 1f);
        return scale;
    }

    /// <summary>
    /// 粒の上端が、この GameObject の位置からどれだけ上にあるか。
    /// ピボットが粒の底辺にあるので、数字を出す高さはここから決める。
    /// </summary>
    private float HeightAboveOrigin(float scale)
    {
        Sprite sprite = spriteRenderer != null ? spriteRenderer.sprite : null;
        if (sprite == null) return 0f;

        float heightUnits = sprite.bounds.size.y;
        float pivotFromBottom = sprite.pivot.y / sprite.pixelsPerUnit;
        return (heightUnits - pivotFromBottom) * scale;
    }
}
