using System.Collections.Generic;
using UnityEngine;

/// <summary>子どもの段階（行動モデル.md 13-2）。</summary>
public enum BroodStage
{
    Egg,
    Larva,
    Pupa,
}

/// <summary>
/// 子ども1個。GameObject は持たず、この配列だけで状態を持つ（13-11）。
/// エディタで実行中にスクリプトを組み直すと、保存できない値は消えてしまうので、
/// Unity が持ち越せるように [Serializable] にしてある。
/// </summary>
[System.Serializable]
public class BroodItem
{
    public BroodStage stage;
    /// <summary>今の段階に入ってからの日数</summary>
    public float ageInStage;
    /// <summary>幼虫のみ。社会胃の中身（0〜1）</summary>
    public float crop = 1f;
    /// <summary>幼虫のみ。食べた量の累積で育つ（0〜1）</summary>
    public float size;
    /// <summary>置かれているマス</summary>
    public int cellX, cellY;
    /// <summary>運んでいる働きアリ（段階5bで使う。いなければ null）</summary>
    public Ant carriedBy;
    /// <summary>空腹が続いている日数</summary>
    public float starveDays;

    /// <summary>塊の中でのずらし方（単位円の中。描くときに幅を掛ける）。1個ごとに固定</summary>
    public Vector2 drawUnitOffset;
    /// <summary>描くときの向き（度）。1個ごとに固定</summary>
    public float drawAngle;

    public float Hunger => 1f - crop;
}

/// <summary>
/// 巣の中の子どもをまとめて進める（行動モデル.md 13-2）。
/// 卵 → 幼虫 → 蛹 → 羽化 の進行と、マスごとの索引を持つ。
/// </summary>
[DisallowMultipleComponent]
public class BroodField : MonoBehaviour
{
    [SerializeField] private SoilGrid grid;
    [SerializeField] private BroodSettings settings;
    [SerializeField] private GameClock clock;
    [SerializeField] private AntSpawner antSpawner;
    [SerializeField] private Colony colony;

    [SerializeField] private List<BroodItem> items = new List<BroodItem>();
    /// <summary>マス（index）→ そこにある子ども。描画とクリック判定に使う。</summary>
    private readonly Dictionary<int, List<BroodItem>> byCell = new Dictionary<int, List<BroodItem>>();
    private readonly Stack<List<BroodItem>> listPool = new Stack<List<BroodItem>>();

    private double lastElapsedDays;

    /// <summary>子どもの一覧。</summary>
    public IReadOnlyList<BroodItem> All => items;
    /// <summary>生活環の設定（女王や寿命からも参照する）。</summary>
    public BroodSettings Settings => settings;
    /// <summary>並びが変わるたびに増える。描画側が作り直す判断に使う。</summary>
    public int Version { get; private set; }
    /// <summary>これまでに羽化した数。</summary>
    public int HatchedCount { get; private set; }
    /// <summary>これまでに死んだ子どもの数。</summary>
    public int BroodDeaths { get; private set; }

    private void Awake()
    {
        if (grid == null) grid = FindFirstObjectByType<SoilGrid>();
        if (clock == null) clock = FindFirstObjectByType<GameClock>();
        if (antSpawner == null) antSpawner = FindFirstObjectByType<AntSpawner>();
        if (colony == null) colony = FindFirstObjectByType<Colony>();
    }

    private void Start()
    {
        if (clock != null) lastElapsedDays = clock.ElapsedDays;
    }

    /// <summary>子どもを1個置く。</summary>
    public BroodItem Add(BroodStage stage, int cellX, int cellY)
    {
        var item = new BroodItem
        {
            stage = stage,
            cellX = cellX,
            cellY = cellY,
            crop = 1f,
            size = stage == BroodStage.Larva ? 0.5f : 0f,
            // 置き方は最初に決めて変えない（毎フレーム動くと塊がちらつく）
            drawUnitOffset = Random.insideUnitCircle,
            drawAngle = Random.Range(0f, 360f),
        };
        items.Add(item);
        AddToCell(item);
        Version++;
        return item;
    }

    /// <summary>段階ごとの数を数える。</summary>
    public int CountOf(BroodStage stage)
    {
        int count = 0;
        for (int i = 0; i < items.Count; i++) if (items[i].stage == stage) count++;
        return count;
    }

    /// <summary>
    /// マスごとの索引を作り直す（必要なときだけ）。
    /// 索引は保存できない形なので、実行中の再コンパイルのあとは空になっている。
    /// </summary>
    private void EnsureIndex()
    {
        if (byCell.Count > 0 || items.Count == 0) return;
        for (int i = 0; i < items.Count; i++) AddToCell(items[i]);
    }

    /// <summary>そのマスにある子ども（なければ null）。</summary>
    public List<BroodItem> GetAtCell(int x, int y)
    {
        if (grid == null) return null;
        EnsureIndex();
        List<BroodItem> list;
        return byCell.TryGetValue(y * grid.Width + x, out list) ? list : null;
    }

    private void Update()
    {
        if (settings == null || clock == null || grid == null) return;
        EnsureIndex();

        double now = clock.ElapsedDays;
        float deltaDays = (float)(now - lastElapsedDays);
        lastElapsedDays = now;
        if (deltaDays <= 0f) return;

        for (int i = items.Count - 1; i >= 0; i--)
        {
            BroodItem item = items[i];
            item.ageInStage += deltaDays;

            if (item.stage == BroodStage.Larva) UpdateLarva(item, deltaDays, i);
            if (i >= items.Count || items[i] != item) continue;   // 死んで取り除かれた

            AdvanceStage(item, i);
        }
    }

    /// <summary>幼虫の飢えと成長。段階5aでは常に満腹として扱う。</summary>
    private void UpdateLarva(BroodItem item, float deltaDays, int index)
    {
        if (settings.larvaAlwaysFed)
        {
            item.crop = 1f;
            item.size = 1f;
            item.starveDays = 0f;
            return;
        }

        float drain = deltaDays / Mathf.Max(0.0001f, settings.larvaFullToEmptyDays);
        item.crop = Mathf.Clamp01(item.crop - drain);

        if (item.crop > 0f)
        {
            item.starveDays = 0f;
            return;
        }

        item.starveDays += deltaDays;
        if (item.starveDays >= settings.larvaStarveDays) RemoveAt(index, true);
    }

    /// <summary>期間が過ぎたら次の段階へ進める。</summary>
    private void AdvanceStage(BroodItem item, int index)
    {
        if (item.ageInStage < settings.StageDuration(item.stage)) return;

        switch (item.stage)
        {
            case BroodStage.Egg:
                item.stage = BroodStage.Larva;
                item.ageInStage = 0f;
                item.size = 0f;
                Version++;
                break;

            case BroodStage.Larva:
                // 栄養が足りていなければ、育つまで幼虫のまま（13-2）
                if (item.size < 1f) return;
                item.stage = BroodStage.Pupa;
                item.ageInStage = 0f;
                Version++;
                break;

            case BroodStage.Pupa:
                Hatch(item, index);
                break;
        }
    }

    /// <summary>羽化して働きアリになる。</summary>
    private void Hatch(BroodItem item, int index)
    {
        if (antSpawner != null)
        {
            Vector2 position = grid.CellToWorld(item.cellX, item.cellY);
            antSpawner.SpawnAdult(position, 0f, settings.newAdultCrop);
        }
        HatchedCount++;
        RemoveAt(index, false);
    }

    private void RemoveAt(int index, bool died)
    {
        BroodItem item = items[index];
        RemoveFromCell(item);
        items.RemoveAt(index);
        if (died) BroodDeaths++;
        Version++;
    }

    // ---- マスごとの索引 ----

    private void AddToCell(BroodItem item)
    {
        int key = item.cellY * grid.Width + item.cellX;
        List<BroodItem> list;
        if (!byCell.TryGetValue(key, out list))
        {
            list = listPool.Count > 0 ? listPool.Pop() : new List<BroodItem>();
            byCell[key] = list;
        }
        list.Add(item);
    }

    private void RemoveFromCell(BroodItem item)
    {
        int key = item.cellY * grid.Width + item.cellX;
        List<BroodItem> list;
        if (!byCell.TryGetValue(key, out list)) return;
        list.Remove(item);
        if (list.Count > 0) return;
        byCell.Remove(key);
        list.Clear();
        listPool.Push(list);
    }

    /// <summary>子どもを別のマスへ移す（段階5bの運搬で使う）。</summary>
    public void MoveTo(BroodItem item, int cellX, int cellY)
    {
        RemoveFromCell(item);
        item.cellX = cellX;
        item.cellY = cellY;
        AddToCell(item);
        Version++;
    }
}
