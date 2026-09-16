using UnityEngine;

/// <summary>
/// 巣の中に働きアリを出す。
/// 匹数は行動の数値ではないので AntSettings ではなくここに置く。
/// 開発用に、Shift＋7 でまとめて増やせる（性能を測るときに使う）。
/// </summary>
[DisallowMultipleComponent]
public class AntSpawner : MonoBehaviour
{
    [SerializeField] private SoilGrid grid;
    [SerializeField] private NestField nestField;
    [SerializeField] private AntSettings settings;
    [SerializeField] private Ant antPrefab;
    [Tooltip("出したアリをまとめる親。未指定ならこの GameObject の下")]
    [SerializeField] private Transform antParent;

    [Header("出す数と場所")]
    [Tooltip("開始時に出す働きアリの数")]
    [SerializeField] private int workerCount = 50;
    [Tooltip("巣の中のどのあたりに出すか。この濃さ以上の空洞を選ぶ")]
    [SerializeField, Range(0f, 1f)] private float minNestValue = 0.85f;
    [Tooltip("置ける場所を探す試行回数（1匹あたり）")]
    [SerializeField] private int placementTries = 40;

    [Header("デバッグ")]
    [Tooltip("Shift＋7 でまとめて増やせるようにする（性能を測るとき用）")]
    [SerializeField] private bool allowDebugSpawnKey = true;
    [Tooltip("Shift＋7 を1回押すと何匹増やすか")]
    [SerializeField] private int debugSpawnBatch = 50;
    [Tooltip("これ以上は増やさない")]
    [SerializeField] private int debugSpawnLimit = 400;

    [Header("個体差")]
    [Tooltip("日齢のばらつき（日）")]
    [SerializeField] private float ageMinDays = 1f;
    [SerializeField] private float ageMaxDays = 30f;

    private void Awake()
    {
        if (grid == null) grid = FindFirstObjectByType<SoilGrid>();
        if (nestField == null) nestField = FindFirstObjectByType<NestField>();
        if (antParent == null) antParent = transform;
    }

    private void Start()
    {
        if (antPrefab == null || grid == null)
        {
            Debug.LogWarning("AntSpawner: アリのプレハブか土のグリッドが設定されていない", this);
            return;
        }

        // 巣の匂いができていないと、巣の中を選べない
        if (nestField != null) nestField.EnsureBuilt();

        for (int i = 0; i < workerCount; i++) SpawnOne();
    }

    private void Update()
    {
        // Shift＋7：アリをまとめて増やす（性能を測るとき用）
        if (!allowDebugSpawnKey) return;
        if (!DebugKeys.WasPressed(7)) return;

        int added = 0;
        for (int i = 0; i < debugSpawnBatch && Ant.All.Count < debugSpawnLimit; i++)
        {
            if (SpawnOne() == null) break;
            added++;
        }
        Debug.Log("デバッグ：アリを " + added + "匹 増やした（合計 " + Ant.All.Count + "匹）");
    }

    /// <summary>働きアリを1匹、巣の中に出す。</summary>
    public Ant SpawnOne()
    {
        Vector2 position;
        if (!FindSpawnPosition(out position)) return null;

        Ant ant = Instantiate(antPrefab, position, Quaternion.identity, antParent);

        // 個体差は日齢だけ。名前も能力値も付けない
        ant.SetStartAge(Random.Range(ageMinDays, ageMaxDays));
        ant.name = "Ant";
        return ant;
    }

    /// <summary>巣の中の、歩ける場所を探す。</summary>
    private bool FindSpawnPosition(out Vector2 position)
    {
        position = Vector2.zero;

        for (int attempt = 0; attempt < placementTries; attempt++)
        {
            int x = Random.Range(0, grid.Width);
            int y = Random.Range(0, grid.SurfaceRow);

            if (grid.GetCell(x, y) != CellType.Cavity) continue;
            if (!grid.HasSolidNeighbor(x, y)) continue;
            if (nestField != null && nestField.GetAt(x, y) < minNestValue) continue;

            position = grid.CellToWorld(x, y);
            return true;
        }

        // 見つからなければ、空洞ならどこでもよい
        for (int y = 0; y < grid.SurfaceRow; y++)
        {
            for (int x = 0; x < grid.Width; x++)
            {
                if (grid.GetCell(x, y) != CellType.Cavity) continue;
                if (!grid.HasSolidNeighbor(x, y)) continue;
                position = grid.CellToWorld(x, y);
                return true;
            }
        }

        return false;
    }
}
