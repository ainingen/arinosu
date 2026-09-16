using UnityEngine;

/// <summary>
/// 餌を地表に出す（行動モデル.md 5章）。
/// 平均して1日に foodPerDay 個。出る間隔は指数分布なので、固まったり空いたりする。
/// 巣の入口の近くには出さない。
/// </summary>
[DisallowMultipleComponent]
public class FoodSpawner : MonoBehaviour
{
    [SerializeField] private SoilGrid grid;
    [SerializeField] private NestField nestField;
    [SerializeField] private GameClock clock;
    [SerializeField] private AntSettings settings;
    [SerializeField] private FoodSource foodPrefab;
    [Tooltip("出した餌をまとめる親。未指定ならこの GameObject の下")]
    [SerializeField] private Transform foodParent;

    [Tooltip("同時に置いておける数の上限（負荷よけ）")]
    [SerializeField] private int maxFoodCount = 12;
    [Tooltip("置ける場所を探す試行回数")]
    [SerializeField] private int placementTries = 30;

    private double nextSpawnDay;

    private void Awake()
    {
        if (grid == null) grid = FindFirstObjectByType<SoilGrid>();
        if (nestField == null) nestField = FindFirstObjectByType<NestField>();
        if (clock == null) clock = FindFirstObjectByType<GameClock>();
        if (foodParent == null) foodParent = transform;
    }

    private void Start()
    {
        if (settings == null || foodPrefab == null) return;

        // 入口の位置が決まっていないと、入口の近くに餌を置いてしまう
        if (nestField != null) nestField.EnsureBuilt();

        for (int i = 0; i < settings.initialFoodCount; i++) SpawnOne();
        ScheduleNext();
    }

    private void Update()
    {
        if (settings == null || clock == null || foodPrefab == null) return;
        if (clock.ElapsedDays < nextSpawnDay) return;

        if (FoodSource.All.Count < maxFoodCount) SpawnOne();
        ScheduleNext();
    }

    /// <summary>次に餌が出る日を決める（指数分布）。</summary>
    private void ScheduleNext()
    {
        float perDay = settings != null ? Mathf.Max(0.0001f, settings.foodPerDay) : 1f;
        // -ln(U) / 率 で、平均 1/率 日の間隔になる
        float u = Mathf.Max(0.0001f, Random.value);
        double interval = -Mathf.Log(u) / perDay;
        nextSpawnDay = clock.ElapsedDays + interval;
    }

    /// <summary>餌を1個どこかに置く。</summary>
    public FoodSource SpawnOne()
    {
        if (grid == null || foodPrefab == null || settings == null) return null;

        float minDistance = settings.foodMinDistanceFromEntrance;
        bool hasEntrance = nestField != null && nestField.HasEntrance;
        Vector2 entrance = hasEntrance ? nestField.EntranceWorld : Vector2.zero;

        for (int attempt = 0; attempt < placementTries; attempt++)
        {
            int x = Random.Range(0, grid.Width);
            int groundY = FindGroundTop(x);
            if (groundY < 0) continue;

            Vector2 position = grid.CellToWorld(x, groundY);
            if (hasEntrance && Mathf.Abs(position.x - entrance.x) < minDistance) continue;

            FoodSource food = Instantiate(foodPrefab, position, Quaternion.identity, foodParent);
            food.Setup(Random.Range(settings.foodAmountMin, settings.foodAmountMax + 1));
            return food;
        }

        return null;
    }

    /// <summary>その列の地面の上（餌を置けるマス）を探す。見つからなければ -1。</summary>
    private int FindGroundTop(int x)
    {
        for (int y = grid.Height - 2; y >= 0; y--)
        {
            if (!grid.IsSolid(x, y)) continue;
            int above = y + 1;
            if (!grid.IsInside(x, above)) return -1;
            if (!grid.IsPassable(x, above)) return -1;
            return above;
        }
        return -1;
    }
}
