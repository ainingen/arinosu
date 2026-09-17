using UnityEngine;

/// <summary>
/// 女王の産卵（行動モデル.md 13-1）。
///
/// 移動や口移しは働きアリと同じ仕組み（Ant）をそのまま使い、
/// 女王に固有の「産卵」だけをここに置く。
/// 産卵のペースは女王の満腹度で決まり、これがコロニーの個体数を決める。
/// </summary>
[RequireComponent(typeof(Ant))]
[DisallowMultipleComponent]
public class Queen : MonoBehaviour
{
    [SerializeField] private BroodSettings settings;
    [SerializeField] private SoilGrid grid;
    [SerializeField] private BroodField broodField;
    [SerializeField] private GameClock clock;

    private Ant ant;
    private double lastElapsedDays;
    /// <summary>次の1個までに必要な「産卵の進み具合」（1.0 で1個産む）。</summary>
    private float layProgress;
    /// <summary>直前に産んでからの時間（気持ちの表示に使う）。</summary>
    private float sinceLastLay = 999f;

    /// <summary>産卵直後か（気持ち「卵を産んだ」に使う）。</summary>
    public bool JustLaid => sinceLastLay < 3f;
    /// <summary>今の産卵ペース（個/日）。</summary>
    public float LayRatePerDay { get; private set; }

    private void Awake()
    {
        ant = GetComponent<Ant>();
        if (grid == null) grid = FindFirstObjectByType<SoilGrid>();
        if (broodField == null) broodField = FindFirstObjectByType<BroodField>();
        if (clock == null) clock = FindFirstObjectByType<GameClock>();
    }

    private void Start()
    {
        if (clock != null) lastElapsedDays = clock.ElapsedDays;
    }

    private void Update()
    {
        if (settings == null || clock == null || broodField == null || grid == null) return;

        sinceLastLay += Time.deltaTime;

        double now = clock.ElapsedDays;
        float deltaDays = (float)(now - lastElapsedDays);
        lastElapsedDays = now;
        if (deltaDays <= 0f) return;

        // 満腹度からペースを決める。空いていれば産まない
        LayRatePerDay = settings.LayRatePerDay(ant.Crop);
        if (LayRatePerDay <= 0f) return;

        layProgress += LayRatePerDay * deltaDays;
        while (layProgress >= 1f)
        {
            layProgress -= 1f;
            if (!LayOneEgg()) break;
        }
    }

    /// <summary>いま立っているマスの隣の空洞に卵を1個置く。</summary>
    private bool LayOneEgg()
    {
        int x, y;
        if (!grid.WorldToCell(ant.Position, out x, out y)) return false;

        int targetX = x, targetY = y;
        if (!grid.IsPassable(x, y))
        {
            // 自分のマスが使えないときは隣を探す
            bool found = false;
            for (int i = 0; i < 4 && !found; i++)
            {
                int dx = i == 0 ? 1 : (i == 1 ? -1 : 0);
                int dy = i == 2 ? 1 : (i == 3 ? -1 : 0);
                if (!grid.IsPassable(x + dx, y + dy)) continue;
                targetX = x + dx;
                targetY = y + dy;
                found = true;
            }
            if (!found) return false;
        }

        broodField.Add(BroodStage.Egg, targetX, targetY);
        ant.ConsumeCrop(settings.layCost);
        sinceLastLay = 0f;
        return true;
    }
}
