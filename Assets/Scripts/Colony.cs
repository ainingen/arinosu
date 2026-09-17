using UnityEngine;

/// <summary>
/// コロニー全体で1回だけ計算する値を持つ（行動モデル.md 6章）。
///
/// 個体は「巣の中で感じられるもの」としてこの値を受け取る。
/// 段階3では素直にコロニーの平均を使うが、あとで局所化しやすいよう
/// 計算はこのクラスの中だけに閉じてある。
/// </summary>
[DisallowMultipleComponent]
public class Colony : MonoBehaviour
{
    [SerializeField] private AntSettings settings;
    [SerializeField] private NestField nestField;
    [SerializeField] private PheromoneField pheromones;
    [SerializeField] private SoilGrid grid;
    [Tooltip("育児の刺激に使う。未指定ならシーンから探す")]
    [SerializeField] private BroodField broodField;

    [Tooltip("巣の中のアリの位置表を作り直す間隔（秒）。口移しの相手探しに使う")]
    [SerializeField] private float neighborRebuildInterval = 0.25f;

    private float updateTimer;
    private float neighborTimer;

    /// <summary>巣の中のアリを、粗いマスに振り分けた表。総当たりを避けるため（9章）。</summary>
    private readonly System.Collections.Generic.Dictionary<long, System.Collections.Generic.List<Ant>> buckets
        = new System.Collections.Generic.Dictionary<long, System.Collections.Generic.List<Ant>>();
    /// <summary>使い回すリストの置き場。毎回作り直すとごみが増えるため。</summary>
    private readonly System.Collections.Generic.Stack<System.Collections.Generic.List<Ant>> listPool
        = new System.Collections.Generic.Stack<System.Collections.Generic.List<Ant>>();

    /// <summary>採餌の刺激（0〜1くらい）。巣の空腹と、入口付近の道しるべから作る。</summary>
    public float ForageStimulus { get; private set; }
    /// <summary>巣の中にいるアリの空腹度の平均（0〜1）。</summary>
    public float NestHungerAverage { get; private set; }
    /// <summary>巣の中にいるアリの数。</summary>
    public int AntsInNest { get; private set; }
    /// <summary>巣の外にいるアリの数。</summary>
    public int AntsOutside { get; private set; }
    /// <summary>入口付近の道しるべの濃さ。</summary>
    public float EntranceTrail { get; private set; }
    /// <summary>閾値の平均（学習で下がっていくのが見える）。</summary>
    public float ThetaAverage { get; private set; }

    /// <summary>掘る刺激（0〜1）。巣が目標の広さより狭いほど大きい。</summary>
    public float DigStimulus { get; private set; }
    /// <summary>育児の刺激 S_nurse（行動モデル.md 13-4）。</summary>
    public float NurseStimulus { get; private set; }
    /// <summary>混雑度（コロニーの総個体数 ÷ 空洞のマス数）。出入りでは変わらない。</summary>
    public float Crowding { get; private set; }
    /// <summary>巣の空洞のマス数。</summary>
    public int CavityCells { get; private set; }
    /// <summary>今の個体数に対する、巣の広さの目標（マス数）。</summary>
    public int TargetCavityCells { get; private set; }
    /// <summary>始まってから死んだ数の合計。</summary>
    public int DeathCount { get; private set; }

    // ---- 土の出入り（行動モデル.md 12-5）----
    /// <summary>掘ったマス数の合計。</summary>
    public int DugCells { get; private set; }
    /// <summary>塚に置いたマス数の合計。</summary>
    public int MoundCells { get; private set; }
    /// <summary>置き場が見つからず捨てた回数。</summary>
    public int DiscardedSoil { get; private set; }

    /// <summary>土を1粒掘った。</summary>
    public void ReportDug() { DugCells++; }
    /// <summary>土を1粒、塚に置いた。</summary>
    public void ReportMound() { MoundCells++; }
    /// <summary>土を1粒捨てた。</summary>
    public void ReportDiscard() { DiscardedSoil++; }

    private int cavityVersion = -1;

    private void Awake()
    {
        if (nestField == null) nestField = FindFirstObjectByType<NestField>();
        if (pheromones == null) pheromones = FindFirstObjectByType<PheromoneField>();
        if (grid == null) grid = FindFirstObjectByType<SoilGrid>();
    }

    private void Start()
    {
        Recalculate();
    }

    private void Update()
    {
        float deltaTime = Time.deltaTime;

        neighborTimer -= deltaTime;
        if (neighborTimer <= 0f)
        {
            neighborTimer = Mathf.Max(0.02f, neighborRebuildInterval);
            RebuildNeighborBuckets();
        }

        float interval = settings != null ? Mathf.Max(0.05f, settings.colonyUpdateInterval) : 1f;
        updateTimer -= deltaTime;
        if (updateTimer > 0f) return;
        updateTimer = interval;
        Recalculate();
    }

    // ============================================================
    // 巣の中の近くのアリを探す（口移しとねだりに使う）
    // ============================================================

    private float BucketSize => settings != null ? Mathf.Max(0.5f, settings.neighborCellSize) : 2f;

    private long BucketKey(Vector2 position)
    {
        float size = BucketSize;
        int x = Mathf.FloorToInt(position.x / size);
        int y = Mathf.FloorToInt(position.y / size);
        return ((long)x << 32) ^ (uint)y;
    }

    /// <summary>巣の中のアリを粗いマスに振り分け直す。</summary>
    private void RebuildNeighborBuckets()
    {
        foreach (var pair in buckets)
        {
            pair.Value.Clear();
            listPool.Push(pair.Value);
        }
        buckets.Clear();

        var ants = Ant.All;
        for (int i = 0; i < ants.Count; i++)
        {
            Ant ant = ants[i];
            if (ant == null || !ant.IsInNest) continue;

            long key = BucketKey(ant.Position);
            System.Collections.Generic.List<Ant> list;
            if (!buckets.TryGetValue(key, out list))
            {
                list = listPool.Count > 0 ? listPool.Pop() : new System.Collections.Generic.List<Ant>();
                buckets[key] = list;
            }
            list.Add(ant);
        }
    }

    /// <summary>
    /// 巣の中で、その範囲にいるいちばん近い仲間を返す（自分は除く）。
    /// 近くのマスだけを見るので、匹数が増えても重くならない。
    /// </summary>
    public Ant FindNearestNestmate(Ant self, float maxDistance)
    {
        if (self == null) return null;

        float size = BucketSize;
        int centerX = Mathf.FloorToInt(self.Position.x / size);
        int centerY = Mathf.FloorToInt(self.Position.y / size);
        int range = Mathf.Max(1, Mathf.CeilToInt(maxDistance / size));

        Ant nearest = null;
        float nearestSq = maxDistance * maxDistance;

        for (int dy = -range; dy <= range; dy++)
        {
            for (int dx = -range; dx <= range; dx++)
            {
                long key = ((long)(centerX + dx) << 32) ^ (uint)(centerY + dy);
                System.Collections.Generic.List<Ant> list;
                if (!buckets.TryGetValue(key, out list)) continue;

                for (int i = 0; i < list.Count; i++)
                {
                    Ant other = list[i];
                    if (other == null || other == self) continue;
                    float distanceSq = (other.Position - self.Position).sqrMagnitude;
                    if (distanceSq >= nearestSq) continue;
                    nearestSq = distanceSq;
                    nearest = other;
                }
            }
        }

        return nearest;
    }

    /// <summary>採餌刺激を計算し直す。ここが S を作る唯一の場所。</summary>
    public void Recalculate()
    {
        var ants = Ant.All;
        int inNest = 0;
        int outside = 0;
        float hungerSum = 0f;
        float thetaSum = 0f;

        for (int i = 0; i < ants.Count; i++)
        {
            Ant ant = ants[i];
            if (ant == null) continue;
            thetaSum += ant.ExploreThreshold;

            if (ant.IsInNest)
            {
                inNest++;
                hungerSum += ant.Hunger;
            }
            else outside++;
        }

        AntsInNest = inNest;
        AntsOutside = outside;

        // 巣の空腹には、女王（巣の中のアリなのでそのまま入る）と幼虫も混ぜる。
        // 子どもが飢えると、育児係だけでなく採餌にも人手が回るようにするため（13-4）
        if (broodField == null) broodField = FindFirstObjectByType<BroodField>();
        float larvaWeight = 0f;
        float larvaHungerSum = 0f;
        var broodSettings = broodField != null ? broodField.Settings : null;
        if (broodField != null && broodSettings != null && broodField.LarvaCount > 0)
        {
            larvaWeight = broodField.LarvaCount * broodSettings.larvaForageWeight;
            larvaHungerSum = broodField.LarvaHungerAverage * larvaWeight;
        }

        float mouths = inNest + larvaWeight;
        NestHungerAverage = mouths > 0f ? (hungerSum + larvaHungerSum) / mouths : 0f;
        ThetaAverage = ants.Count > 0 ? thetaSum / ants.Count : 0f;
        EntranceTrail = SampleEntranceTrail();

        float weight = settings != null ? settings.trailStimulusWeight : 0.5f;
        float trailMax = pheromones != null ? Mathf.Max(0.0001f, pheromones.GetMax(PheromoneLayer.Trail)) : 1f;

        // 前半：巣が空腹なら出る。後半：行列ができていれば釣られて出る
        ForageStimulus = Mathf.Clamp01(NestHungerAverage + EntranceTrail / trailMax * weight);

        RecalculateNurse(broodSettings);
        RecalculateDig();
    }

    /// <summary>
    /// 育児の刺激を計算し直す（行動モデル.md 13-4）。
    /// 幼虫が空腹なほど、また子どもがはぐれているほど強くなる。
    /// </summary>
    private void RecalculateNurse(BroodSettings broodSettings)
    {
        if (broodField == null || broodSettings == null)
        {
            NurseStimulus = 0f;
            return;
        }

        NurseStimulus = Mathf.Clamp01(
            broodField.LarvaHungerAverage * broodSettings.nurseHungerWeight
            + broodField.IsolatedRatio * broodSettings.nurseIsolationWeight);
    }

    /// <summary>
    /// 掘る刺激を計算し直す（行動モデル.md 12-1）。
    ///
    /// 巣の広さが「総個体数 × targetCellsPerAnt」に届いていないほど強くなる。
    /// 巣の中にいる数ではなく**総個体数**で見るので、採餌の出入りで刺激が揺れない。
    /// 目標に達したら 0 になり、掘削は止まる。
    /// </summary>
    private void RecalculateDig()
    {
        if (grid == null) return;

        if (cavityVersion != grid.Version) CountCavityCells();

        int totalAnts = Ant.All.Count;
        Crowding = CavityCells > 0 ? (float)totalAnts / CavityCells : 0f;

        float cellsPerAnt = settings != null ? Mathf.Max(0.0001f, settings.targetCellsPerAnt) : 6f;

        // 子どもも場所を取る。幼虫の数 × broodSpaceWeight をアリの数に足して目標を決める（13-4）
        float broodMouths = 0f;
        var broodSettings = broodField != null ? broodField.Settings : null;
        if (broodField != null && broodSettings != null)
            broodMouths = broodField.LarvaCount * broodSettings.broodSpaceWeight;

        TargetCavityCells = Mathf.RoundToInt((totalAnts + broodMouths) * cellsPerAnt);

        if (TargetCavityCells <= 0 || CavityCells >= TargetCavityCells)
        {
            DigStimulus = 0f;
            return;
        }

        float range = settings != null ? Mathf.Clamp(settings.digShortfallRange, 0.05f, 1f) : 0.5f;
        float shortfall = TargetCavityCells - CavityCells;
        DigStimulus = Mathf.Clamp01(shortfall / (TargetCavityCells * range));
    }

    /// <summary>
    /// アリが死んだことを記録し、Console に1行で残す。
    /// どこで、何をしていた個体が、どんな腰の重さで死んだのかを後から追えるようにする。
    /// </summary>
    public void ReportDeath(Ant ant, AntDeathCause cause)
    {
        DeathCount++;
        if (ant == null) return;

        var clock = FindFirstObjectByType<GameClock>();
        string day = clock != null ? clock.ElapsedDays.ToString("0.0") : "?";
        string place = ant.IsInNest ? "巣の中" : "地表";

        Debug.Log("死亡：" + day + "日目 " + AntTexts.DeathCause(cause)
            + " 場所=" + place
            + " 直前の仕事=" + AntTexts.Task(ant.CurrentTask)
            + " θ[Explore]=" + ant.ExploreThreshold.ToString("0.00")
            + " θ[Dig]=" + ant.DigThreshold.ToString("0.00")
            + " 蓄え=" + ant.Reserve.ToString("0.00")
            + " （通算 " + DeathCount + "匹目）");
    }

    /// <summary>巣の空洞のマス数を数える。地形が変わったときだけ。</summary>
    private void CountCavityCells()
    {
        cavityVersion = grid.Version;
        int count = 0;
        for (int y = 0; y < grid.SurfaceRow; y++)
        {
            for (int x = 0; x < grid.Width; x++)
            {
                if (grid.GetCell(x, y) == CellType.Cavity) count++;
            }
        }
        CavityCells = count;
    }

    /// <summary>入口のまわりの道しるべの濃さ（いちばん濃いマスを見る）。</summary>
    private float SampleEntranceTrail()
    {
        if (pheromones == null || nestField == null || grid == null) return 0f;
        if (!nestField.HasEntrance) return 0f;

        int cx, cy;
        if (!grid.WorldToCell(nestField.EntranceWorld, out cx, out cy)) return 0f;

        int radius = settings != null ? Mathf.Max(0, settings.trailStimulusRadius) : 3;
        float best = 0f;
        for (int dy = -radius; dy <= radius; dy++)
        {
            for (int dx = -radius; dx <= radius; dx++)
            {
                if (dx * dx + dy * dy > radius * radius) continue;
                float value = pheromones.GetAt(cx + dx, cy + dy, PheromoneLayer.Trail);
                if (value > best) best = value;
            }
        }
        return best;
    }

    /// <summary>
    /// 巣の中で、その範囲にいるアリの数（自分は除く）。
    /// 掘削で「アリがたまっている場所ほど掘られる」を判定するのに使う。
    /// </summary>
    public int CountNestmatesNear(Ant self, float radius)
    {
        if (self == null) return 0;

        float size = BucketSize;
        int centerX = Mathf.FloorToInt(self.Position.x / size);
        int centerY = Mathf.FloorToInt(self.Position.y / size);
        int range = Mathf.Max(1, Mathf.CeilToInt(radius / size));
        float radiusSq = radius * radius;

        int count = 0;
        for (int dy = -range; dy <= range; dy++)
        {
            for (int dx = -range; dx <= range; dx++)
            {
                long key = ((long)(centerX + dx) << 32) ^ (uint)(centerY + dy);
                System.Collections.Generic.List<Ant> list;
                if (!buckets.TryGetValue(key, out list)) continue;

                for (int i = 0; i < list.Count; i++)
                {
                    Ant other = list[i];
                    if (other == null || other == self) continue;
                    if ((other.Position - self.Position).sqrMagnitude <= radiusSq) count++;
                }
            }
        }
        return count;
    }
}
