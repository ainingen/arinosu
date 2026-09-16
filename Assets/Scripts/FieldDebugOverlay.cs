using UnityEngine;

/// <summary>
/// 匂いの層を目で見るための重ね表示（行動モデル.md 8章）。
/// Shift＋1＝道しるべ、Shift＋2＝巣の匂い、Shift＋3＝選択中のアリの感知点。
/// 表示していないときは何も計算しない。
/// </summary>
[DisallowMultipleComponent]
public class FieldDebugOverlay : MonoBehaviour
{
    /// <summary>今どの層を重ねているか。</summary>
    public enum OverlayMode
    {
        None,
        Trail,
        Nest,
    }

    [SerializeField] private SoilGrid grid;
    [SerializeField] private PheromoneField pheromones;
    [SerializeField] private NestField nestField;
    [SerializeField] private AntSettings settings;
    [SerializeField] private AntSelector selector;

    [Header("色")]
    [SerializeField] private Color trailColor = new Color(0.3f, 1f, 0.4f, 1f);
    [SerializeField] private Color nestColor = new Color(0.4f, 0.7f, 1f, 1f);
    [Tooltip("重ねる濃さの上限")]
    [SerializeField, Range(0f, 1f)] private float overlayAlpha = 0.8f;
    [Tooltip("土より手前、アリより奥に描く")]
    [SerializeField] private int sortingOrder = -5;

    [Header("感知点（F3）")]
    [SerializeField] private Sprite dotSprite;
    [SerializeField] private Color dotColor = new Color(1f, 0.9f, 0.2f, 1f);
    [Tooltip("点の大きさ（cm）")]
    [SerializeField] private float dotSize = 0.12f;

    private OverlayMode mode = OverlayMode.None;
    private bool showSensors;

    private SpriteRenderer overlayRenderer;
    private Texture2D texture;
    private Color32[] pixels;
    private float refreshTimer;

    private SpriteRenderer sensorLeft;
    private SpriteRenderer sensorRight;

    /// <summary>今の重ね表示。</summary>
    public OverlayMode Mode => mode;

    /// <summary>餌の残量の数字を出すか。道しるべの重ね表示（F1）と一緒に切り替わる。</summary>
    public static bool ShowFoodAmounts { get; private set; }

    private void OnDisable()
    {
        ShowFoodAmounts = false;
    }

    private void Awake()
    {
        if (grid == null) grid = FindFirstObjectByType<SoilGrid>();
        if (pheromones == null) pheromones = FindFirstObjectByType<PheromoneField>();
        if (nestField == null) nestField = FindFirstObjectByType<NestField>();
        if (selector == null) selector = FindFirstObjectByType<AntSelector>();
    }

    private void Start()
    {
        CreateOverlay();
        CreateSensorDots();
        ApplyVisibility();
    }

    /// <summary>1マス1ピクセルのテクスチャを world に重ねる。</summary>
    private void CreateOverlay()
    {
        if (grid == null) return;

        var go = new GameObject("FieldOverlay");
        go.transform.SetParent(transform, false);
        go.transform.position = grid.WorldMin;

        overlayRenderer = go.AddComponent<SpriteRenderer>();
        overlayRenderer.sortingOrder = sortingOrder;

        texture = new Texture2D(grid.Width, grid.Height, TextureFormat.RGBA32, false);
        texture.name = "FieldOverlayTexture";
        texture.filterMode = FilterMode.Point;
        texture.wrapMode = TextureWrapMode.Clamp;

        pixels = new Color32[grid.Width * grid.Height];

        float pixelsPerUnit = 1f / grid.CellSize;
        var sprite = Sprite.Create(
            texture,
            new Rect(0f, 0f, grid.Width, grid.Height),
            Vector2.zero,
            pixelsPerUnit,
            0,
            SpriteMeshType.FullRect);
        sprite.name = "FieldOverlaySprite";
        overlayRenderer.sprite = sprite;
    }

    /// <summary>感知点を表す小さな丸を2つ作る。</summary>
    private void CreateSensorDots()
    {
        sensorLeft = CreateDot("SensorLeft");
        sensorRight = CreateDot("SensorRight");
    }

    private SpriteRenderer CreateDot(string dotName)
    {
        var go = new GameObject(dotName);
        go.transform.SetParent(transform, false);
        var renderer = go.AddComponent<SpriteRenderer>();
        renderer.sprite = dotSprite;
        renderer.color = dotColor;
        renderer.sortingOrder = sortingOrder + 10;   // アリより手前
        go.transform.localScale = Vector3.one * dotSize;
        go.SetActive(false);
        return renderer;
    }

    private void Update()
    {
        HandleKeys();
        UpdateOverlayTexture();
        UpdateSensorDots();
    }

    private void HandleKeys()
    {
        // Shift＋1：道しるべ、Shift＋2：巣の匂い、Shift＋3：感知点
        if (DebugKeys.WasPressed(1))
        {
            mode = mode == OverlayMode.Trail ? OverlayMode.None : OverlayMode.Trail;
            refreshTimer = 0f;
            ApplyVisibility();
        }
        if (DebugKeys.WasPressed(2))
        {
            mode = mode == OverlayMode.Nest ? OverlayMode.None : OverlayMode.Nest;
            refreshTimer = 0f;
            ApplyVisibility();
        }
        if (DebugKeys.WasPressed(3))
        {
            showSensors = !showSensors;
            ApplyVisibility();
        }
    }

    /// <summary>重ねる層を外から切り替える（F4 の塗りモードが道しるべを出すのに使う）。</summary>
    public void SetMode(OverlayMode newMode)
    {
        mode = newMode;
        refreshTimer = 0f;
        ApplyVisibility();
    }

    private void ApplyVisibility()
    {
        if (overlayRenderer != null) overlayRenderer.enabled = mode != OverlayMode.None;
        ShowFoodAmounts = mode == OverlayMode.Trail;
        if (!showSensors)
        {
            if (sensorLeft != null) sensorLeft.gameObject.SetActive(false);
            if (sensorRight != null) sensorRight.gameObject.SetActive(false);
        }
    }

    /// <summary>重ねる絵を作り直す。表示していないときは何もしない。</summary>
    private void UpdateOverlayTexture()
    {
        if (mode == OverlayMode.None || texture == null) return;

        refreshTimer -= Time.unscaledDeltaTime;
        if (refreshTimer > 0f) return;
        refreshTimer = settings != null ? settings.overlayRefreshInterval : 0.25f;

        float[] values;
        float max;
        Color color;

        if (mode == OverlayMode.Trail)
        {
            if (pheromones == null) return;
            values = pheromones.GetValues(PheromoneLayer.Trail);
            max = Mathf.Max(0.0001f, pheromones.GetMax(PheromoneLayer.Trail));
            color = trailColor;
        }
        else
        {
            if (nestField == null) return;
            values = nestField.Values;
            max = 1f;
            color = nestColor;
        }

        if (values == null) return;

        byte r = (byte)(color.r * 255f);
        byte g = (byte)(color.g * 255f);
        byte b = (byte)(color.b * 255f);

        for (int i = 0; i < pixels.Length && i < values.Length; i++)
        {
            float t = Mathf.Clamp01(values[i] / max);
            byte a = (byte)(t * overlayAlpha * 255f);
            pixels[i] = new Color32(r, g, b, a);
        }

        texture.SetPixelData(pixels, 0);
        texture.Apply(false);
    }

    /// <summary>選択中のアリの触角の感知点を描く。</summary>
    private void UpdateSensorDots()
    {
        if (sensorLeft == null || sensorRight == null) return;

        Ant ant = selector != null ? selector.Selected : null;
        bool visible = showSensors && ant != null && settings != null;

        if (sensorLeft.gameObject.activeSelf != visible) sensorLeft.gameObject.SetActive(visible);
        if (sensorRight.gameObject.activeSelf != visible) sensorRight.gameObject.SetActive(visible);
        if (!visible) return;

        // アリの頭は +Y。そこから左右に開いた2点
        Vector2 forward = ant.transform.up;
        Vector2 left = Rotate(forward, settings.sensorSpread) * settings.sensorForward;
        Vector2 right = Rotate(forward, -settings.sensorSpread) * settings.sensorForward;

        sensorLeft.transform.position = ant.Position + left;
        sensorRight.transform.position = ant.Position + right;
    }

    private static Vector2 Rotate(Vector2 v, float degrees)
    {
        float rad = degrees * Mathf.Deg2Rad;
        float cos = Mathf.Cos(rad);
        float sin = Mathf.Sin(rad);
        return new Vector2(v.x * cos - v.y * sin, v.x * sin + v.y * cos);
    }
}
