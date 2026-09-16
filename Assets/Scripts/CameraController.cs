using System;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// ホイールでズーム、右ドラッグ（またはホイール押しドラッグ）で画面を動かすカメラ。
/// 一時停止中（Time.timeScale = 0）でも動かせるように、時間は unscaled を使う。
/// </summary>
[RequireComponent(typeof(Camera))]
[DisallowMultipleComponent]
public class CameraController : MonoBehaviour
{
    [Tooltip("移動範囲の制限に使う世界。未指定ならシーンから探す")]
    [SerializeField] private SoilGrid grid;

    [Header("ズーム")]
    [Tooltip("一番寄ったときの表示の高さの半分（Unity単位 ＝ cm）")]
    [SerializeField] private float zoomMin = 1.5f;
    [Tooltip("一番引いたときの表示の高さの半分（Unity単位 ＝ cm）")]
    [SerializeField] private float zoomMax = 28f;
    [Tooltip("ホイール1目盛りでどれだけ拡縮するか")]
    [SerializeField] private float zoomStep = 0.15f;
    [Tooltip("大きいほどすぐ目標の倍率に追いつく")]
    [SerializeField] private float zoomSmooth = 14f;
    [Tooltip("開始時の表示の高さの半分")]
    [SerializeField] private float startZoom = 26f;

    [Header("画面の移動")]
    [SerializeField] private bool useRightDrag = true;
    [SerializeField] private bool useMiddleDrag = true;

    [Header("移動の制限")]
    [Tooltip("世界の外に出ないようにする")]
    [SerializeField] private bool clampToWorld = true;
    [Tooltip("世界の外側に許す余白（Unity単位）")]
    [SerializeField] private float worldMargin = 3f;

    [Header("追跡")]
    [Tooltip("選んだアリを追いかける速さ。大きいほどすぐ追いつく")]
    [SerializeField] private float followLerp = 8f;

    private Camera cam;
    private float targetZoom;
    private bool isDragging;
    /// <summary>ドラッグ開始時にカーソルがつかんだワールド座標。</summary>
    private Vector3 dragAnchorWorld;
    /// <summary>追いかけている相手。null なら追跡していない。</summary>
    private Transform followTarget;

    /// <summary>プレイヤーが画面を動かしたときに呼ばれる（段階2でアリの追跡を解除するのに使う）。</summary>
    public event Action OnUserPanned;

    /// <summary>今ドラッグ中か。</summary>
    public bool IsDragging => isDragging;

    /// <summary>今追いかけている相手（追跡していなければ null）。</summary>
    public Transform FollowTarget => followTarget;

    /// <summary>追いかける相手を決める。null を渡すと追跡をやめる。</summary>
    public void SetFollowTarget(Transform target)
    {
        followTarget = target;
    }

    private void Awake()
    {
        cam = GetComponent<Camera>();
        cam.orthographic = true;
        if (grid == null) grid = FindFirstObjectByType<SoilGrid>();
    }

    private void Start()
    {
        targetZoom = Mathf.Clamp(startZoom, zoomMin, zoomMax);
        cam.orthographicSize = targetZoom;

        // 最初は世界の真ん中を映す
        if (grid != null)
        {
            Vector2 center = (grid.WorldMin + grid.WorldMax) * 0.5f;
            transform.position = new Vector3(center.x, center.y, transform.position.z);
        }
        ClampPosition();
    }

    private void Update()
    {
        Mouse mouse = Mouse.current;
        if (mouse == null) return;

        HandleZoom(mouse);
        HandleDrag(mouse);
        FollowTargetStep();
        ClampPosition();
    }

    /// <summary>追いかけている相手へカメラを寄せる。</summary>
    private void FollowTargetStep()
    {
        if (followTarget == null) return;

        // 一時停止中でも追跡が止まらないように unscaled を使う
        float t = 1f - Mathf.Exp(-followLerp * Time.unscaledDeltaTime);
        Vector3 target = followTarget.position;
        Vector3 position = transform.position;
        position.x = Mathf.Lerp(position.x, target.x, t);
        position.y = Mathf.Lerp(position.y, target.y, t);
        transform.position = position;
    }

    /// <summary>ホイールでズームする。カーソルの下にある場所が動かないようにする。</summary>
    private void HandleZoom(Mouse mouse)
    {
        float scroll = mouse.scroll.ReadValue().y;
        if (Mathf.Abs(scroll) > 0.01f)
        {
            // 環境によって1目盛りが 120 だったり 1 だったりするので、どちらでも同じ感覚になるようにする
            float notches = Mathf.Abs(scroll) >= 10f ? scroll / 120f : scroll;
            targetZoom = Mathf.Clamp(targetZoom * Mathf.Exp(-notches * zoomStep), zoomMin, zoomMax);
        }

        if (Mathf.Approximately(cam.orthographicSize, targetZoom)) return;

        Vector2 screenPosition = mouse.position.ReadValue();
        Vector3 beforeWorld = cam.ScreenToWorldPoint(screenPosition);

        float t = 1f - Mathf.Exp(-zoomSmooth * Time.unscaledDeltaTime);
        cam.orthographicSize = Mathf.Lerp(cam.orthographicSize, targetZoom, t);

        Vector3 afterWorld = cam.ScreenToWorldPoint(screenPosition);
        Vector3 shift = beforeWorld - afterWorld;
        shift.z = 0f;
        transform.position += shift;
    }

    /// <summary>ドラッグで画面を動かす。</summary>
    private void HandleDrag(Mouse mouse)
    {
        bool pressed =
            (useRightDrag && mouse.rightButton.isPressed) ||
            (useMiddleDrag && mouse.middleButton.isPressed);

        if (!pressed)
        {
            isDragging = false;
            return;
        }

        Vector2 screenPosition = mouse.position.ReadValue();

        if (!isDragging)
        {
            isDragging = true;
            dragAnchorWorld = cam.ScreenToWorldPoint(screenPosition);
            return;
        }

        // つかんだ場所がカーソルの下から動かないようにカメラをずらす
        Vector3 currentWorld = cam.ScreenToWorldPoint(screenPosition);
        Vector3 shift = dragAnchorWorld - currentWorld;
        shift.z = 0f;
        if (shift.sqrMagnitude <= 0f) return;

        // 画面を自分で動かしたら追跡はやめる（仕様4章）
        followTarget = null;
        transform.position += shift;
        OnUserPanned?.Invoke();
    }

    /// <summary>世界から離れすぎないように位置を戻す。</summary>
    private void ClampPosition()
    {
        if (!clampToWorld || grid == null) return;

        Vector2 min = grid.WorldMin - Vector2.one * worldMargin;
        Vector2 max = grid.WorldMax + Vector2.one * worldMargin;

        float halfHeight = cam.orthographicSize;
        float halfWidth = halfHeight * cam.aspect;

        Vector3 position = transform.position;

        // 世界より画面のほうが大きいときは真ん中に置く
        position.x = (max.x - min.x) <= halfWidth * 2f
            ? (min.x + max.x) * 0.5f
            : Mathf.Clamp(position.x, min.x + halfWidth, max.x - halfWidth);

        position.y = (max.y - min.y) <= halfHeight * 2f
            ? (min.y + max.y) * 0.5f
            : Mathf.Clamp(position.y, min.y + halfHeight, max.y - halfHeight);

        transform.position = position;
    }
}
