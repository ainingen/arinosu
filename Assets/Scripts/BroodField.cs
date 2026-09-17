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
    [Tooltip("子どもの匂いを置く先。未指定ならシーンから探す")]
    [SerializeField] private PheromoneField pheromones;

    [SerializeField] private List<BroodItem> items = new List<BroodItem>();
    /// <summary>マス（index）→ そこにある子ども。描画とクリック判定に使う。</summary>
    private readonly Dictionary<int, List<BroodItem>> byCell = new Dictionary<int, List<BroodItem>>();
    private readonly Stack<List<BroodItem>> listPool = new Stack<List<BroodItem>>();

    private double lastElapsedDays;
    private float depositTimer;

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
    /// <summary>これまでに働きアリが食べた子どもの数（飢饉の共食い）。</summary>
    public int CannibalizedCount { get; private set; }
    /// <summary>幼虫の空腹の平均（S_nurse に使う）。幼虫がいなければ 0。</summary>
    public float LarvaHungerAverage { get; private set; }
    /// <summary>今いる幼虫の数（S_forage の重み付けに使う）。</summary>
    public int LarvaCount { get; private set; }
    /// <summary>まわりに仲間がいない子どもの割合（S_nurse に使う）。</summary>
    public float IsolatedRatio { get; private set; }
    /// <summary>これまでに幼虫へ口移しした回数（育児が回っているかの目安）。</summary>
    public int FeedCount { get; private set; }
    /// <summary>これまでに幼虫へ流した量の合計。</summary>
    public float FedTotal { get; private set; }

    private void Awake()
    {
        if (grid == null) grid = FindFirstObjectByType<SoilGrid>();
        if (clock == null) clock = FindFirstObjectByType<GameClock>();
        if (antSpawner == null) antSpawner = FindFirstObjectByType<AntSpawner>();
        if (colony == null) colony = FindFirstObjectByType<Colony>();
        if (pheromones == null) pheromones = FindFirstObjectByType<PheromoneField>();
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

        depositTimer += Time.deltaTime;
        bool deposit = depositTimer >= Mathf.Max(0.0001f, settings.depositInterval);
        if (deposit) depositTimer = 0f;

        float hungerSum = 0f;
        int larvae = 0;
        int isolated = 0;

        for (int i = items.Count - 1; i >= 0; i--)
        {
            BroodItem item = items[i];
            item.ageInStage += deltaDays;

            if (item.stage == BroodStage.Larva) UpdateLarva(item, deltaDays, i);
            if (i >= items.Count || items[i] != item) continue;   // 死んで取り除かれた

            if (item.stage == BroodStage.Larva)
            {
                larvae++;
                hungerSum += item.Hunger;
            }
            if (deposit && item.carriedBy == null)
            {
                Deposit(item);
                if (IsIsolated(item)) isolated++;
            }

            AdvanceStage(item, i);
        }

        LarvaCount = larvae;
        LarvaHungerAverage = larvae > 0 ? hungerSum / larvae : 0f;
        if (deposit) IsolatedRatio = items.Count > 0 ? (float)isolated / items.Count : 0f;
    }

    /// <summary>
    /// 子どもが置かれているマスに匂いを置く（行動モデル.md 13-3）。
    /// 運ばれている間は置かない（塊の判定が運搬中のアリについて回らないように）。
    /// </summary>
    private void Deposit(BroodItem item)
    {
        if (pheromones == null || item.carriedBy != null) return;

        pheromones.DepositAt(item.cellX, item.cellY, PheromoneLayer.Brood, settings.broodDeposit);

        if (item.stage != BroodStage.Larva) return;
        float hunger = item.Hunger;
        if (hunger <= 0f) return;
        pheromones.DepositAt(item.cellX, item.cellY, PheromoneLayer.LarvaHunger,
            hunger * settings.larvaHungerDeposit);
    }

    /// <summary>まわり1マスに他の子どもがいないか（行動モデル.md 13-4）。</summary>
    private bool IsIsolated(BroodItem item)
    {
        for (int dy = -1; dy <= 1; dy++)
        {
            for (int dx = -1; dx <= 1; dx++)
            {
                List<BroodItem> list = GetAtCell(item.cellX + dx, item.cellY + dy);
                if (list == null) continue;
                // 自分だけのマスは「いない」と同じ
                if (dx == 0 && dy == 0 && list.Count <= 1) continue;
                return false;
            }
        }
        return true;
    }

    /// <summary>
    /// 近くでいちばん空腹な幼虫を返す（行動モデル.md 13-4）。
    /// 運ばれている子どもは相手にしない。
    /// </summary>
    public BroodItem FindHungryLarva(Vector2 worldPosition, float range, float hungerThreshold)
    {
        if (grid == null) return null;
        float rangeSq = range * range;
        BroodItem best = null;
        float bestHunger = hungerThreshold;

        for (int i = 0; i < items.Count; i++)
        {
            BroodItem item = items[i];
            if (item.stage != BroodStage.Larva || item.carriedBy != null) continue;
            if (item.Hunger <= bestHunger) continue;
            if ((grid.CellToWorld(item.cellX, item.cellY) - worldPosition).sqrMagnitude > rangeSq) continue;
            bestHunger = item.Hunger;
            best = item;
        }
        return best;
    }

    /// <summary>
    /// そのマスを中心に、半径 radius マスの中にある子どもの数（運ばれている分は数えない）。
    /// 集積の規則（13-6）の「まわりの混み具合」に使う。
    /// </summary>
    public int CountNear(int cellX, int cellY, int radius)
    {
        int count = 0;
        for (int dy = -radius; dy <= radius; dy++)
        {
            for (int dx = -radius; dx <= radius; dx++)
            {
                List<BroodItem> list = GetAtCell(cellX + dx, cellY + dy);
                if (list == null) continue;
                for (int i = 0; i < list.Count; i++) if (list[i].carriedBy == null) count++;
            }
        }
        return count;
    }

    /// <summary>
    /// そのマスにある子どもを1個持ち上げる（行動モデル.md 13-6）。
    /// 置かれているマスの情報は持ったままにして、置くときに書き換える。
    /// </summary>
    public BroodItem PickUp(int cellX, int cellY, Ant carrier)
    {
        List<BroodItem> list = GetAtCell(cellX, cellY);
        if (list == null || carrier == null) return null;

        for (int i = list.Count - 1; i >= 0; i--)
        {
            if (list[i].carriedBy != null) continue;
            list[i].carriedBy = carrier;
            Version++;
            return list[i];
        }
        return null;
    }

    /// <summary>持っている子どもを、そのマスへ置く。</summary>
    public void PutDown(BroodItem item, int cellX, int cellY)
    {
        if (item == null) return;
        item.carriedBy = null;
        MoveTo(item, cellX, cellY);
    }

    /// <summary>
    /// 飢饉のときに食べる相手を探す（行動モデル.md 13-6）。
    /// 対象は卵と、まだ育っていない幼虫だけ。繭と育ちかけの幼虫は食べない。
    /// </summary>
    public BroodItem FindCannibalTarget(Vector2 worldPosition, float range, float maxSize)
    {
        if (grid == null) return null;
        float rangeSq = range * range;
        BroodItem best = null;
        float bestSq = float.MaxValue;

        for (int i = 0; i < items.Count; i++)
        {
            BroodItem item = items[i];
            if (item.carriedBy != null) continue;
            if (item.stage == BroodStage.Pupa) continue;
            if (item.stage == BroodStage.Larva && item.size >= maxSize) continue;

            float distanceSq = (grid.CellToWorld(item.cellX, item.cellY) - worldPosition).sqrMagnitude;
            if (distanceSq > rangeSq || distanceSq >= bestSq) continue;
            bestSq = distanceSq;
            best = item;
        }
        return best;
    }

    /// <summary>
    /// その場所のまわり（半径 range cm）にある子どもの数。部屋の名前に使う（13-7）。
    /// </summary>
    public int CountWithin(Vector2 worldPosition, float range)
    {
        if (grid == null) return 0;
        float rangeSq = range * range;
        int count = 0;
        for (int i = 0; i < items.Count; i++)
        {
            BroodItem item = items[i];
            if (item.carriedBy != null) continue;
            if ((grid.CellToWorld(item.cellX, item.cellY) - worldPosition).sqrMagnitude > rangeSq) continue;
            count++;
        }
        return count;
    }

    /// <summary>その子どもを食べる（取り除く）。餓死とは別に数える。</summary>
    public void Consume(BroodItem item)
    {
        int index = items.IndexOf(item);
        if (index < 0) return;
        CannibalizedCount++;
        RemoveAt(index, false);
    }

    /// <summary>
    /// 幼虫に口移しで食べさせる（行動モデル.md 13-2）。実際に入った量を返す。
    /// もらった量の growthPerCrop 倍だけ体が育つ。
    /// </summary>
    public float Feed(BroodItem item, float amount)
    {
        if (item == null || settings == null || amount <= 0f) return 0f;
        float accepted = Mathf.Min(amount, 1f - item.crop);
        if (accepted <= 0f) return 0f;

        item.crop += accepted;
        item.starveDays = 0f;
        FeedCount++;
        FedTotal += accepted;
        item.size = Mathf.Clamp01(item.size + accepted * settings.growthPerCrop);
        return accepted;
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
