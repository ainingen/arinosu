using UnityEngine;

/// <summary>
/// 働きアリ1匹。
/// 段階2では「固体（土・石）に接している面に沿って歩く」だけを行う。
/// 段階3で反応閾値モデルに置き換える前提の、仮の歩き方。
/// アリの当たり判定は1点として扱う（体の大きさは見た目だけ）。
/// </summary>
[DisallowMultipleComponent]
public class Ant : MonoBehaviour
{
    [Tooltip("歩く世界。未指定ならシーンから探す")]
    [SerializeField] private SoilGrid grid;
    [Tooltip("見た目。未指定なら同じ GameObject から探す")]
    [SerializeField] private AntView view;

    [Header("歩く速さ")]
    [Tooltip("1秒あたりに進む距離（Unity単位 ＝ cm）")]
    [SerializeField] private float walkSpeed = 2f;
    [Tooltip("体の向きが進行方向に追いつく速さ（度/秒）")]
    [SerializeField] private float turnSpeed = 540f;

    [Header("ふらつき")]
    [Tooltip("一度に向きを変えるゆらぎの大きさ（度）")]
    [SerializeField] private float wanderAngle = 20f;
    [Tooltip("ゆらぐ間隔（秒）")]
    [SerializeField] private float wanderInterval = 0.5f;

    [Header("行き止まりの避け方")]
    [Tooltip("進めるかどうかを、どれだけ先まで調べるか（Unity単位）")]
    [SerializeField] private float probeDistance = 0.12f;
    [Tooltip("行き止まりのときに向きを変える刻み（度）")]
    [SerializeField] private float turnStepAngle = 20f;
    [Tooltip("向きを変えて試す回数")]
    [SerializeField] private int maxTurnTries = 17;

    [Header("落下")]
    [Tooltip("どこにも接していないときに落ちる速さ")]
    [SerializeField] private float fallSpeed = 5f;

    [Header("速さを上げたときの刻み")]
    [Tooltip("1回の計算で進む最大距離。大きすぎると壁をすり抜ける")]
    [SerializeField] private float maxStepDistance = 0.08f;
    [Tooltip("1フレームで刻む最大回数")]
    [SerializeField] private int maxSubsteps = 24;

    [Header("置き直し")]
    [Tooltip("土の中に置かれたとき、歩ける場所を探す範囲（マス）")]
    [SerializeField] private int snapSearchRadius = 60;

    [Header("性質（段階5の生活環で本物になる。今は仮）")]
    [SerializeField] private AntCaste caste = AntCaste.WorkerMinor;
    [Tooltip("生まれてからの日数の初期値")]
    [SerializeField] private float startAgeDays = 10f;
    [Tooltip("外勤寄りの度合い（0＝内勤、1＝外勤）。段階5で日齢から決める")]
    [SerializeField, Range(0f, 1f)] private float outdoorTendency = 0.5f;

    [Header("場所の見分け方")]
    [Tooltip("まわりを何マス見て、トンネルか部屋かを決めるか")]
    [SerializeField] private int placeSampleRadius = 2;
    [Tooltip("この数より広ければ部屋とみなす（まわりの通れるマスの数）")]
    [SerializeField] private int chamberOpenCells = 14;

    /// <summary>生きているアリの一覧。クリック判定などで使う（毎回探すより軽い）。</summary>
    private static readonly System.Collections.Generic.List<Ant> all = new System.Collections.Generic.List<Ant>();
    public static System.Collections.Generic.IReadOnlyList<Ant> All => all;

    private GameClock clock;
    private double bornAtElapsedDays;

    private Vector2 position;
    private Vector2 direction = Vector2.right;
    private float wanderTimer;
    private bool isFalling;

    /// <summary>今いる場所（ワールド座標）。</summary>
    public Vector2 Position => position;
    /// <summary>落ちている最中か。</summary>
    public bool IsFalling => isFalling;
    /// <summary>見た目。</summary>
    public AntView View => view;
    /// <summary>種類（カースト）。</summary>
    public AntCaste Caste => caste;
    /// <summary>外勤寄りの度合い（0〜1）。</summary>
    public float OutdoorTendency => outdoorTendency;

    /// <summary>生まれてからの日数。</summary>
    public float AgeDays
    {
        get
        {
            if (clock == null) return startAgeDays;
            return startAgeDays + (float)(clock.ElapsedDays - bornAtElapsedDays);
        }
    }

    /// <summary>今やっている仕事。段階3で反応閾値モデルから決まるようになる。</summary>
    public AntTask CurrentTask => isFalling ? AntTask.Fall : AntTask.Wander;

    /// <summary>運んでいるもの。段階3以降で中身が入る。</summary>
    public AntCarry Carrying => AntCarry.None;

    /// <summary>気持ち。段階3で内部状態から選ぶ。今は落下中かどうかだけ。</summary>
    public AntMood CurrentMood => isFalling ? AntMood.Alarmed : AntMood.Calm;

    /// <summary>今いる場所の種類。まわりの土の形から決める。</summary>
    public AntPlace CurrentPlace
    {
        get
        {
            if (grid == null) return AntPlace.Surface;
            int x, y;
            if (!grid.WorldToCell(position, out x, out y)) return AntPlace.Surface;
            if (y >= grid.SurfaceRow) return AntPlace.Surface;

            // まわりに通れるマスが多ければ部屋、少なければトンネル
            int open = 0;
            for (int dy = -placeSampleRadius; dy <= placeSampleRadius; dy++)
            {
                for (int dx = -placeSampleRadius; dx <= placeSampleRadius; dx++)
                {
                    if (grid.IsPassable(x + dx, y + dy)) open++;
                }
            }
            return open >= chamberOpenCells ? AntPlace.Chamber : AntPlace.Tunnel;
        }
    }

    private void Awake()
    {
        if (grid == null) grid = FindFirstObjectByType<SoilGrid>();
        if (view == null) view = GetComponent<AntView>();
        clock = FindFirstObjectByType<GameClock>();
        if (clock != null) bornAtElapsedDays = clock.ElapsedDays;
    }

    private void OnEnable()
    {
        if (!all.Contains(this)) all.Add(this);
    }

    private void OnDisable()
    {
        all.Remove(this);
    }

    private void Start()
    {
        position = transform.position;
        SnapToNearestWalkable();

        float angle = Random.Range(0f, 360f);
        direction = Rotate(Vector2.right, angle);
        wanderTimer = Random.Range(0f, wanderInterval);

        ApplyTransform(0f);
    }

    private void Update()
    {
        if (grid == null) return;

        float deltaTime = Time.deltaTime;
        if (deltaTime <= 0f)
        {
            // 一時停止中は脚も止める
            if (view != null) view.UpdateGait(0f, 0f);
            return;
        }

        float movedDistance = 0f;
        float remainingTime = deltaTime;
        int steps = 0;

        // 速度を上げたときに壁をすり抜けないよう、細かく刻んで進める
        while (remainingTime > 0f && steps < maxSubsteps)
        {
            steps++;
            bool supported = IsSupported(position);
            isFalling = !supported;

            float speed = supported ? walkSpeed : fallSpeed;
            if (speed <= 0f) break;

            float stepTime = Mathf.Min(remainingTime, maxStepDistance / speed);
            remainingTime -= stepTime;
            float stepDistance = speed * stepTime;

            if (supported) movedDistance += WalkStep(stepDistance, stepTime);
            else FallStep(stepDistance);
        }

        ApplyTransform(deltaTime);
        if (view != null) view.UpdateGait(movedDistance, deltaTime);
    }

    /// <summary>接している面に沿って1歩進む。進めた距離を返す。</summary>
    private float WalkStep(float stepDistance, float stepTime)
    {
        // ときどき向きをゆらす
        wanderTimer -= stepTime;
        if (wanderTimer <= 0f)
        {
            wanderTimer = wanderInterval;
            direction = Rotate(direction, Random.Range(-wanderAngle, wanderAngle));
        }

        Vector2 target = position + direction * stepDistance;
        if (CanStandAt(target))
        {
            position = target;
            return stepDistance;
        }

        // 進めない：向きを少しずつ変えて、歩ける方向を探す
        float firstSign = Random.value < 0.5f ? -1f : 1f;
        for (int i = 1; i <= maxTurnTries; i++)
        {
            float sign = (i % 2 == 1) ? firstSign : -firstSign;
            float angle = turnStepAngle * ((i + 1) / 2) * sign;
            Vector2 candidate = Rotate(direction, angle);
            if (CanStandAt(position + candidate * probeDistance))
            {
                direction = candidate;
                return 0f;
            }
        }

        // どこにも行けなければ引き返す
        direction = -direction;
        return 0f;
    }

    /// <summary>どこにも接していないときに落ちる。</summary>
    private void FallStep(float stepDistance)
    {
        Vector2 target = position + Vector2.down * stepDistance;
        if (IsPassableAt(target)) position = target;
        // 通れない場所にぶつかったらそこで止まる（次の計算で接地と判定される）
    }

    /// <summary>その場所に立てるか（通れるマスで、まわりに固体がある）。</summary>
    private bool CanStandAt(Vector2 worldPosition)
    {
        int x, y;
        if (!grid.WorldToCell(worldPosition, out x, out y)) return false;
        if (!grid.IsPassable(x, y)) return false;
        return grid.HasSolidNeighbor(x, y);
    }

    /// <summary>その場所が通れるマスか（接しているかは問わない）。</summary>
    private bool IsPassableAt(Vector2 worldPosition)
    {
        int x, y;
        if (!grid.WorldToCell(worldPosition, out x, out y)) return false;
        return grid.IsPassable(x, y);
    }

    /// <summary>今いる場所で体を接していられるか。</summary>
    private bool IsSupported(Vector2 worldPosition)
    {
        int x, y;
        if (!grid.WorldToCell(worldPosition, out x, out y)) return false;
        return grid.HasSolidNeighbor(x, y);
    }

    /// <summary>土の中などに置かれていたら、いちばん近い歩ける場所へ移す。</summary>
    private void SnapToNearestWalkable()
    {
        if (CanStandAt(position)) return;

        int cx, cy;
        grid.WorldToCell(position, out cx, out cy);

        for (int r = 1; r <= snapSearchRadius; r++)
        {
            for (int dy = -r; dy <= r; dy++)
            {
                for (int dx = -r; dx <= r; dx++)
                {
                    // 正方形の外周だけを調べる
                    if (Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dy)) != r) continue;
                    int x = cx + dx;
                    int y = cy + dy;
                    if (!grid.IsInside(x, y)) continue;
                    if (!grid.IsPassable(x, y)) continue;
                    if (!grid.HasSolidNeighbor(x, y)) continue;
                    position = grid.CellToWorld(x, y);
                    return;
                }
            }
        }

        Debug.LogWarning("Ant: 歩ける場所が見つからなかった", this);
    }

    /// <summary>位置と向きを見た目に反映する。頭（+Y）を進行方向へ向ける。</summary>
    private void ApplyTransform(float deltaTime)
    {
        Vector3 p = transform.position;
        transform.position = new Vector3(position.x, position.y, p.z);

        if (direction.sqrMagnitude <= 0f) return;

        float targetAngle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg - 90f;
        float currentAngle = transform.eulerAngles.z;
        float nextAngle = deltaTime > 0f
            ? Mathf.MoveTowardsAngle(currentAngle, targetAngle, turnSpeed * deltaTime)
            : targetAngle;
        transform.rotation = Quaternion.Euler(0f, 0f, nextAngle);
    }

    /// <summary>好きな場所へ置き直す（デバッグや段階3以降で使う）。</summary>
    public void Teleport(Vector2 worldPosition)
    {
        position = worldPosition;
        SnapToNearestWalkable();
        ApplyTransform(0f);
    }

    /// <summary>ベクトルを角度（度）だけ回す。</summary>
    private static Vector2 Rotate(Vector2 v, float degrees)
    {
        float rad = degrees * Mathf.Deg2Rad;
        float cos = Mathf.Cos(rad);
        float sin = Mathf.Sin(rad);
        return new Vector2(v.x * cos - v.y * sin, v.x * sin + v.y * cos);
    }
}
