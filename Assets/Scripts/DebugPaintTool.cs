using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 開発用：F4 で塗りモードに入り、左ドラッグで道しるべを置ける。
/// 段階3a の時点ではまだ道しるべを出すアリがいないので、
/// 蒸発と拡散の見え方を確かめるために使う。
/// </summary>
[DisallowMultipleComponent]
public class DebugPaintTool : MonoBehaviour
{
    [SerializeField] private Camera targetCamera;
    [SerializeField] private PheromoneField pheromones;
    [SerializeField] private AntSettings settings;
    [Tooltip("一度に塗る半径（マス）")]
    [SerializeField] private int brushRadius = 1;

    [Header("塗りモード中の表示")]
    [Tooltip("重ね表示。塗りモードに入ったら道しるべ（F1）を自動で出す")]
    [SerializeField] private FieldDebugOverlay overlay;
    [Tooltip("画面の隅に出す札")]
    [SerializeField] private GameObject paintModeLabel;
    [Tooltip("札に出す文字")]
    [SerializeField] private string paintModeMessage = "塗りモード（F4で解除）";
    [SerializeField] private TMPro.TMP_Text paintModeText;

    /// <summary>塗りモード中か。アリの選択と取り合わないように他から見られるようにしてある。</summary>
    public static bool PaintModeActive { get; private set; }

    /// <summary>塗りモードに入る前の重ね表示。抜けたら戻す。</summary>
    private FieldDebugOverlay.OverlayMode modeBeforePaint = FieldDebugOverlay.OverlayMode.None;

    private void Awake()
    {
        if (targetCamera == null) targetCamera = Camera.main;
        if (pheromones == null) pheromones = FindFirstObjectByType<PheromoneField>();
        if (overlay == null) overlay = FindFirstObjectByType<FieldDebugOverlay>();
    }

    private void Start()
    {
        if (paintModeText != null) paintModeText.text = paintModeMessage;
        ApplyPaintModeLabel();
    }

    private void OnDisable()
    {
        PaintModeActive = false;
        ApplyPaintModeLabel();
    }

    /// <summary>札を出したり消したりする。</summary>
    private void ApplyPaintModeLabel()
    {
        if (paintModeLabel != null) paintModeLabel.SetActive(PaintModeActive);
    }

    /// <summary>塗りモードの入り切りに合わせて、道しるべの重ね表示を出す・戻す。</summary>
    private void ApplyOverlay()
    {
        if (overlay == null) return;

        if (PaintModeActive)
        {
            modeBeforePaint = overlay.Mode;
            if (overlay.Mode != FieldDebugOverlay.OverlayMode.Trail)
                overlay.SetMode(FieldDebugOverlay.OverlayMode.Trail);
            return;
        }

        // 塗っている間に人が F2 などへ変えていたら、その選択を尊重して触らない
        if (overlay.Mode == FieldDebugOverlay.OverlayMode.Trail)
            overlay.SetMode(modeBeforePaint);
    }

    private void Update()
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard != null && keyboard.f4Key.wasPressedThisFrame)
        {
            PaintModeActive = !PaintModeActive;
            ApplyOverlay();
            ApplyPaintModeLabel();
        }

        if (!PaintModeActive) return;

        Mouse mouse = Mouse.current;
        if (mouse == null || targetCamera == null || pheromones == null || settings == null) return;
        if (!mouse.leftButton.isPressed) return;

        Vector3 world = targetCamera.ScreenToWorldPoint(mouse.position.ReadValue());
        Paint(world);
    }

    /// <summary>その場所のまわりに道しるべを置く。</summary>
    private void Paint(Vector2 worldPosition)
    {
        var grid = pheromones.GetComponent<SoilGrid>();
        if (grid == null) grid = FindFirstObjectByType<SoilGrid>();
        if (grid == null) return;

        int cx, cy;
        if (!grid.WorldToCell(worldPosition, out cx, out cy)) return;

        for (int dy = -brushRadius; dy <= brushRadius; dy++)
        {
            for (int dx = -brushRadius; dx <= brushRadius; dx++)
            {
                if (dx * dx + dy * dy > brushRadius * brushRadius) continue;
                pheromones.DepositAt(cx + dx, cy + dy, PheromoneLayer.Trail, settings.debugPaintAmount);
            }
        }
    }
}
