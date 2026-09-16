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

    private float updateTimer;

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
        float interval = settings != null ? Mathf.Max(0.05f, settings.colonyUpdateInterval) : 1f;
        updateTimer -= Time.deltaTime;
        if (updateTimer > 0f) return;
        updateTimer = interval;
        Recalculate();
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
        NestHungerAverage = inNest > 0 ? hungerSum / inNest : 0f;
        ThetaAverage = ants.Count > 0 ? thetaSum / ants.Count : 0f;
        EntranceTrail = SampleEntranceTrail();

        float weight = settings != null ? settings.trailStimulusWeight : 0.5f;
        float trailMax = pheromones != null ? Mathf.Max(0.0001f, pheromones.GetMax(PheromoneLayer.Trail)) : 1f;

        // 前半：巣が空腹なら出る。後半：行列ができていれば釣られて出る
        ForageStimulus = Mathf.Clamp01(NestHungerAverage + EntranceTrail / trailMax * weight);
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
}
