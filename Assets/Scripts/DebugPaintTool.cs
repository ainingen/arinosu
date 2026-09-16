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
    [Tooltip("札に出す文字（道しるべを塗るモード）")]
    [SerializeField] private string paintModeMessage = "塗りモード（F4で解除）";
    [Tooltip("札に出す文字（餌を置くモード）")]
    [SerializeField] private string foodModeMessage = "餌置きモード（クリックで設置・F5で解除）";
    [Tooltip("入口に近すぎて置けないときの文。{0} に必要な距離が入る")]
    [SerializeField] private string foodTooCloseMessage = "巣の入口に近すぎます（{0}cm以上離してください）";
    [Tooltip("その列に地面がないときの文")]
    [SerializeField] private string foodNoGroundMessage = "ここには地面がありません";
    [Tooltip("世界の外をクリックしたときの文")]
    [SerializeField] private string foodOutsideMessage = "世界の外には置けません";
    [Tooltip("準備ができていないときの文")]
    [SerializeField] private string foodNotReadyMessage = "まだ餌を置けません";
    [Tooltip("理由を出しておく時間（秒）")]
    [SerializeField] private float messageSeconds = 2.5f;
    [SerializeField] private TMPro.TMP_Text paintModeText;
    [Tooltip("餌を置いてもらう係")]
    [SerializeField] private FoodSpawner foodSpawner;

    /// <summary>塗りモード中か。アリの選択と取り合わないように他から見られるようにしてある。</summary>
    public static bool PaintModeActive { get; private set; }
    /// <summary>餌置きモード中か。</summary>
    public static bool FoodModeActive { get; private set; }
    /// <summary>何かのデバッグ操作中か（アリの選択を止めるのに使う）。</summary>
    public static bool AnyModeActive => PaintModeActive || FoodModeActive;

    /// <summary>塗りモードに入る前の重ね表示。抜けたら戻す。</summary>
    private FieldDebugOverlay.OverlayMode modeBeforePaint = FieldDebugOverlay.OverlayMode.None;

    /// <summary>理由を出しておく残り時間。</summary>
    private float messageTimer;

    private void Awake()
    {
        if (targetCamera == null) targetCamera = Camera.main;
        if (pheromones == null) pheromones = FindFirstObjectByType<PheromoneField>();
        if (overlay == null) overlay = FindFirstObjectByType<FieldDebugOverlay>();
    }

    private void Start()
    {
        if (foodSpawner == null) foodSpawner = FindFirstObjectByType<FoodSpawner>();
        ApplyModeLabel();
    }

    private void OnDisable()
    {
        PaintModeActive = false;
        FoodModeActive = false;
        ApplyModeLabel();
    }

    /// <summary>今のモードに合わせて札を出したり消したりする。</summary>
    private void ApplyModeLabel()
    {
        messageTimer = 0f;
        if (paintModeText != null)
        {
            if (PaintModeActive) paintModeText.text = paintModeMessage;
            else if (FoodModeActive) paintModeText.text = foodModeMessage;
        }
        if (paintModeLabel != null) paintModeLabel.SetActive(AnyModeActive);
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
        // 理由の表示が終わったら、モードの札に戻す
        if (messageTimer > 0f)
        {
            messageTimer -= Time.unscaledDeltaTime;
            if (messageTimer <= 0f) ApplyModeLabel();
        }

        Keyboard keyboard = Keyboard.current;
        if (keyboard != null && keyboard.f4Key.wasPressedThisFrame)
        {
            PaintModeActive = !PaintModeActive;
            if (PaintModeActive) FoodModeActive = false;
            ApplyOverlay();
            ApplyModeLabel();
        }
        if (keyboard != null && keyboard.f5Key.wasPressedThisFrame)
        {
            FoodModeActive = !FoodModeActive;
            if (FoodModeActive && PaintModeActive)
            {
                PaintModeActive = false;
                ApplyOverlay();
            }
            ApplyModeLabel();
        }

        Mouse mouse = Mouse.current;
        if (mouse == null || targetCamera == null) return;

        if (FoodModeActive)
        {
            // 餌は押した瞬間に1個だけ置く
            if (!mouse.leftButton.wasPressedThisFrame) return;
            Vector3 point = targetCamera.ScreenToWorldPoint(mouse.position.ReadValue());
            PlaceFood(point);
            return;
        }

        if (!PaintModeActive) return;
        if (pheromones == null || settings == null) return;
        if (!mouse.leftButton.isPressed) return;

        Vector3 world = targetCamera.ScreenToWorldPoint(mouse.position.ReadValue());
        Paint(world);
    }

    /// <summary>カーソルの列の地表面に餌を1個置く。置けなければ理由を画面に出す。</summary>
    private void PlaceFood(Vector2 worldPosition)
    {
        if (foodSpawner == null) return;

        FoodPlacementResult result;
        FoodSource food = foodSpawner.SpawnAt(worldPosition, out result);
        if (food != null) return;

        ShowTemporaryMessage(DescribeFailure(result));
    }

    /// <summary>置けなかった理由の文。</summary>
    private string DescribeFailure(FoodPlacementResult result)
    {
        switch (result)
        {
            case FoodPlacementResult.TooCloseToEntrance:
                float distance = settings != null ? settings.foodMinDistanceFromEntrance : 5f;
                return string.Format(foodTooCloseMessage, distance.ToString("0.#"));
            case FoodPlacementResult.NoGround:
                return foodNoGroundMessage;
            case FoodPlacementResult.OutsideWorld:
                return foodOutsideMessage;
            default:
                return foodNotReadyMessage;
        }
    }

    /// <summary>札にしばらくのあいだ別の文を出す。</summary>
    private void ShowTemporaryMessage(string message)
    {
        if (paintModeText == null || paintModeLabel == null) return;
        paintModeText.text = message;
        paintModeLabel.SetActive(true);
        messageTimer = messageSeconds;
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
