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
    [Tooltip("女王のプレハブ（段階5）")]
    [SerializeField] private Ant queenPrefab;
    [SerializeField] private BroodSettings broodSettings;
    [SerializeField] private BroodField broodField;
    [Tooltip("出したアリをまとめる親。未指定ならこの GameObject の下")]
    [SerializeField] private Transform antParent;

    [Header("出す数と場所")]
    [Tooltip("開始時に出す働きアリの数")]
    [SerializeField] private int workerCount = 50;
    [Tooltip("巣の中のどのあたりに出すか。この濃さ以上の空洞を選ぶ")]
    [SerializeField, Range(0f, 1f)] private float minNestValue = 0.85f;

    [Header("デバッグ（負荷試験用）")]
    [Tooltip("Shift＋7 でまとめて増やせるようにする")]
    [SerializeField] private bool allowDebugSpawnKey = true;
    [Tooltip("Shift＋7 を1回押すと何匹増やすか")]
    [SerializeField] private int debugSpawnBatch = 50;
    [Tooltip("この数まで増やせる（これ以上は増えない）")]
    [SerializeField] private int debugSpawnLimit = 1000;

    [Header("個体差")]
    [Tooltip("日齢のばらつき（日）")]
    [SerializeField] private float ageMinDays = 1f;
    [SerializeField] private float ageMaxDays = 30f;

    private void Awake()
    {
        if (grid == null) grid = FindFirstObjectByType<SoilGrid>();
        if (nestField == null) nestField = FindFirstObjectByType<NestField>();
        if (broodField == null) broodField = FindFirstObjectByType<BroodField>();
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
        SpawnQueenAndBrood();
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

    /// <summary>
    /// 女王1匹と、開始時の子どもを巣のいちばん奥に置く（行動モデル.md 13-1）。
    /// </summary>
    private void SpawnQueenAndBrood()
    {
        if (broodSettings == null || grid == null) return;

        // 巣のいちばん奥（巣の匂いがもっとも濃い空洞）を探す
        int deepX = -1, deepY = -1;
        float best = -1f;
        for (int y = 0; y < grid.SurfaceRow; y++)
        {
            for (int x = 0; x < grid.Width; x++)
            {
                if (grid.GetCell(x, y) != CellType.Cavity) continue;
                if (!grid.HasSolidNeighbor(x, y)) continue;
                float value = nestField != null ? nestField.GetAt(x, y) : 1f;
                if (value <= best) continue;
                best = value;
                deepX = x;
                deepY = y;
            }
        }
        if (deepX < 0) return;

        Vector2 deep = grid.CellToWorld(deepX, deepY);

        if (queenPrefab != null)
        {
            Ant queen = Instantiate(queenPrefab, deep, Quaternion.identity, antParent);
            queen.name = "Queen";
        }

        if (broodField == null) return;
        PlaceBroodAround(deepX, deepY, BroodStage.Egg, broodSettings.initialEggs);
        PlaceBroodAround(deepX, deepY, BroodStage.Larva, broodSettings.initialLarvae);
        PlaceBroodAround(deepX, deepY, BroodStage.Pupa, broodSettings.initialPupae);
    }

    /// <summary>巣の奥のまわりの空洞に子どもを散らして置く。</summary>
    private void PlaceBroodAround(int centerX, int centerY, BroodStage stage, int count)
    {
        for (int i = 0; i < count; i++)
        {
            int x = centerX, y = centerY;
            for (int attempt = 0; attempt < 20; attempt++)
            {
                int tx = centerX + Random.Range(-3, 4);
                int ty = centerY + Random.Range(-3, 4);
                if (grid.GetCell(tx, ty) != CellType.Cavity) continue;
                x = tx; y = ty;
                break;
            }
            broodField.Add(stage, x, y);
        }
    }

    /// <summary>
    /// 指定した場所に働きアリを1匹出す（羽化に使う）。
    /// 巣の中の歩ける場所へは Ant 側が自分で寄せる。
    /// </summary>
    public Ant SpawnAdult(Vector2 position, float ageDays, float crop)
    {
        if (antPrefab == null) return null;
        Ant ant = Instantiate(antPrefab, position, Quaternion.identity, antParent);
        ant.SetStartAge(ageDays);
        ant.SetStartCrop(crop);
        ant.name = "Ant";
        return ant;
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

    /// <summary>巣の中の、出せるマスの一覧。地形が変わったときだけ作り直す。</summary>
    private readonly System.Collections.Generic.List<int> spawnCells = new System.Collections.Generic.List<int>();
    private int spawnCellsVersion = -1;

    /// <summary>
    /// 出せるマスを集め直す。
    /// 巣の空洞はグリッド全体から見ればごく一部なので、
    /// 場所をランダムに引いて試すやり方だと、まとめて出すときにほとんど外れてしまう。
    /// 先に候補を集めておき、そこから選ぶ。
    /// </summary>
    private void RefreshSpawnCells()
    {
        spawnCellsVersion = grid.Version;
        spawnCells.Clear();

        for (int y = 0; y < grid.SurfaceRow; y++)
        {
            for (int x = 0; x < grid.Width; x++)
            {
                if (grid.GetCell(x, y) != CellType.Cavity) continue;
                if (!grid.HasSolidNeighbor(x, y)) continue;
                if (nestField != null && nestField.GetAt(x, y) < minNestValue) continue;
                spawnCells.Add(y * grid.Width + x);
            }
        }

        if (spawnCells.Count > 0) return;

        // 条件に合う場所がなければ、空洞ならどこでもよいことにする
        for (int y = 0; y < grid.SurfaceRow; y++)
        {
            for (int x = 0; x < grid.Width; x++)
            {
                if (grid.GetCell(x, y) != CellType.Cavity) continue;
                if (!grid.HasSolidNeighbor(x, y)) continue;
                spawnCells.Add(y * grid.Width + x);
            }
        }
    }

    /// <summary>巣の中の、歩ける場所をひとつ選ぶ。</summary>
    private bool FindSpawnPosition(out Vector2 position)
    {
        position = Vector2.zero;

        if (spawnCellsVersion != grid.Version) RefreshSpawnCells();
        if (spawnCells.Count == 0) return false;

        int index = spawnCells[Random.Range(0, spawnCells.Count)];
        position = grid.CellToWorld(index % grid.Width, index / grid.Width);
        return true;
    }
}
