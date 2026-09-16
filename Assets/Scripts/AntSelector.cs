using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

/// <summary>
/// 左クリックでアリを選び、カメラに追わせて情報パネルを出す。
/// コライダーは使わず、クリックした場所と各アリの距離で判定する（数が増えても軽い）。
/// </summary>
[DisallowMultipleComponent]
public class AntSelector : MonoBehaviour
{
    [SerializeField] private Camera targetCamera;
    [SerializeField] private CameraController cameraController;
    [SerializeField] private AntInfoPanel infoPanel;

    [Header("クリックの当たり")]
    [Tooltip("アリを選べる範囲（Unity単位 ＝ cm）。体の大きさくらい")]
    [SerializeField] private float clickRadius = 0.7f;
    [Tooltip("引いているときでも押せるよう、画面上での最小の当たり半径（ピクセル）")]
    [SerializeField] private float minClickRadiusPixels = 20f;

    private Ant selected;

    /// <summary>今選ばれているアリ（いなければ null）。</summary>
    public Ant Selected => selected;

    /// <summary>選ばれたアリが変わったときに呼ばれる。</summary>
    public event Action<Ant> OnSelectionChanged;

    private void Awake()
    {
        if (targetCamera == null) targetCamera = Camera.main;
        if (cameraController == null && targetCamera != null)
            cameraController = targetCamera.GetComponent<CameraController>();
    }

    private void Start()
    {
        Select(null);
    }

    private void Update()
    {
        // 選択の操作は一時停止中でも効いてほしいので、時間の速さに左右されない Update で行う
        Keyboard keyboard = Keyboard.current;
        if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
        {
            Select(null);
            return;
        }

        Mouse mouse = Mouse.current;
        if (mouse == null || targetCamera == null) return;
        if (!mouse.leftButton.wasPressedThisFrame) return;
        if (IsPointerOverUI()) return;

        Vector3 world = targetCamera.ScreenToWorldPoint(mouse.position.ReadValue());
        Select(FindAntNear(world));
    }

    /// <summary>UI の上をクリックしたか（パネルを押したときにアリの選択を変えないため）。</summary>
    private bool IsPointerOverUI()
    {
        return EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
    }

    /// <summary>クリックした場所にいちばん近いアリを返す。範囲の外なら null。</summary>
    private Ant FindAntNear(Vector2 worldPosition)
    {
        // 引いているときは1匹が小さくなるので、画面上の見かけの大きさでも当たりを広げる
        float worldPerPixel = targetCamera.orthographicSize * 2f / Mathf.Max(1, Screen.height);
        float radius = Mathf.Max(clickRadius, minClickRadiusPixels * worldPerPixel);
        float radiusSq = radius * radius;

        Ant nearest = null;
        float nearestSq = float.MaxValue;

        var ants = Ant.All;
        for (int i = 0; i < ants.Count; i++)
        {
            Ant ant = ants[i];
            if (ant == null) continue;
            float distanceSq = (ant.Position - worldPosition).sqrMagnitude;
            if (distanceSq > radiusSq) continue;
            if (distanceSq >= nearestSq) continue;
            nearestSq = distanceSq;
            nearest = ant;
        }

        return nearest;
    }

    /// <summary>アリを選ぶ。null を渡すと選択を解除する。</summary>
    public void Select(Ant ant)
    {
        if (selected != null && selected.View != null) selected.View.SetSelected(false);

        selected = ant;

        if (selected != null && selected.View != null) selected.View.SetSelected(true);

        if (cameraController != null)
            cameraController.SetFollowTarget(selected != null ? selected.transform : null);

        if (infoPanel != null)
        {
            if (selected != null) infoPanel.Show(selected);
            else infoPanel.Hide();
        }

        OnSelectionChanged?.Invoke(selected);
    }
}
