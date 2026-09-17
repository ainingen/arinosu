using UnityEngine;

/// <summary>
/// 働きアリ1匹（行動モデル.md 6章）。
///
/// 層を2つに分けてある。
///   ・移動層：固体に接している面に沿って歩く。向きは受け取るだけ
///   ・行動層：0.1秒ごとの知覚tickで、触角2点の匂いから向きを決める
/// 「目的地へ向かう」処理は書かない。その場で感じられるものだけで決める。
///
/// 段階3bでは仕事は Explore 固定の往復。社会胃・口移し・反応閾値は段階3c。
/// </summary>
[DisallowMultipleComponent]
public class Ant : MonoBehaviour
{
    [Header("参照")]
    [SerializeField] private SoilGrid grid;
    [SerializeField] private AntView view;
    [SerializeField] private AntSettings settings;
    [SerializeField] private PheromoneField pheromones;
    [SerializeField] private NestField nestField;
    [Tooltip("コロニー全体の値（採餌刺激）。未指定ならシーンから探す")]
    [SerializeField] private Colony colony;
    [Tooltip("運び出した土を積む係。未指定ならシーンから探す")]
    [SerializeField] private MoundBuilder mound;
    [Tooltip("生活環の設定（女王と寿命に使う）。未指定ならシーンから探す")]
    [SerializeField] private BroodSettings broodSettings;
    [Tooltip("子どもの一覧（育児に使う）。未指定ならシーンから探す")]
    [SerializeField] private BroodField broodField;

    [Header("移動の刻み")]
    [Tooltip("1回の計算で進む最大距離。1マス(0.2cm)の半分までなら壁をすり抜けない")]
    [SerializeField] private float maxStepDistance = 0.1f;
    [Tooltip("1フレームで刻む最大回数。×100 で必要な距離を賄えるだけ要る")]
    [SerializeField] private int maxSubsteps = 40;

    [Header("行き止まりの避け方")]
    [Tooltip("進めるかどうかを、どれだけ先まで調べるか（cm）")]
    [SerializeField] private float probeDistance = 0.12f;
    [Tooltip("行き止まりのときに向きを変える刻み（度）")]
    [SerializeField] private float turnStepAngle = 20f;
    [SerializeField] private int maxTurnTries = 17;

    [Header("置き直し")]
    [Tooltip("土の中に置かれたとき、歩ける場所を探す範囲（マス）")]
    [SerializeField] private int snapSearchRadius = 60;

    [Header("性質（段階5の生活環で本物になる。今は仮）")]
    [SerializeField] private AntCaste caste = AntCaste.WorkerMinor;
    [Tooltip("生まれてからの日数の初期値")]
    [SerializeField] private float startAgeDays = 10f;

    [Header("場所の見分け方")]
    [SerializeField] private int placeSampleRadius = 2;
    [Tooltip("この数より広ければ部屋とみなす（まわりの通れるマスの数）")]
    [SerializeField] private int chamberOpenCells = 14;

    /// <summary>生きているアリの一覧。クリック判定や近くの仲間探しに使う。</summary>
    private static readonly System.Collections.Generic.List<Ant> all = new System.Collections.Generic.List<Ant>();
    public static System.Collections.Generic.IReadOnlyList<Ant> All => all;

    private GameClock clock;
    private double bornAtElapsedDays;

    // ---- 位置と向き ----
    private Vector2 position;
    private Vector2 direction = Vector2.right;
    private bool isFalling;
    private int lastCellIndex = -1;

    // ---- 行動の状態 ----
    private AntTask task = AntTask.RestInNest;
    private AntCarry carrying = AntCarry.None;
    private float perceptionAccum;
    private float timeOutside;

    // ---- 社会胃と閾値（行動モデル.md 2章・4章・6章）----
    /// <summary>社会胃の中身。1で満腹</summary>
    // エディタで実行中にスクリプトを組み直すと、保存されない値は消えてしまう。
    // 社会胃・体の蓄え・閾値・寿命は測定の土台なので、持ち越せるようにしてある
    // （Inspector には出さない。調整する値ではないため）
    [HideInInspector, SerializeField] private float crop = 1f;
    /// <summary>体の蓄え（脂肪体）。社会胃が空になってから使う。口移しでは分けない</summary>
    [HideInInspector, SerializeField] private float reserve;
    /// <summary>社会胃の初期値が外から指定されたか（羽化した個体）</summary>
    private bool cropOverridden;
    /// <summary>仲間へ配るために持ち帰っている分</summary>
    private float carryLoad;
    /// <summary>仕事ごとの閾値（腰の重さ）。学習で動く（行動モデル.md 12-2）</summary>
    [HideInInspector, SerializeField] private float[] theta = new float[ThetaCount];
    /// <summary>判定する順番（毎回混ぜる）</summary>
    private readonly int[] thetaOrder = { ThetaExplore, ThetaDig, ThetaNurse };
    private const int ThetaExplore = 0;
    private const int ThetaDig = 1;
    private const int ThetaNurse = 2;
    private const int ThetaCount = 3;

    // ---- 掘削（行動モデル.md 12章）----
    /// <summary>掘り終わるまでの残り時間。0 より大きいあいだは止まって掘っている</summary>
    private float digTimer;
    /// <summary>掘る場所を探して歩いている時間。長すぎたら巣の仕事に戻る</summary>
    private float digSearchTimer;
    /// <summary>土を持って外に出てからの時間。長すぎたら捨てる</summary>
    private float dumpTimer;
    /// <summary>今掘っているマス</summary>
    private int digCellX, digCellY;

    // ---- 育児（行動モデル.md 13-4）----
    /// <summary>世話する相手を探して歩いている時間。長すぎたら巣の仕事に戻る</summary>
    private float nurseSearchTimer;
    /// <summary>今まさに幼虫へ食べさせたところか（気持ちの表示に使う）</summary>
    private bool feedingNow;
    /// <summary>運んでいる子ども（いなければ null）</summary>
    private BroodItem carriedBrood;
    /// <summary>子どもを持ってからの時間。長すぎたらその場に置く</summary>
    private float carryBroodTimer;

    // ---- 女王（行動モデル.md 13-1）----
    /// <summary>女王が歩いている残り時間。0 のあいだはその場から動かない</summary>
    private float queenWalkTimer;
    private float metabolismTimer;
    private float movedSinceMetabolism;
    private float decisionTimer;
    private bool isInNest = true;
    /// <summary>今いる場所の巣の匂いの濃さ（知覚tickで更新）。</summary>
    private float nestHere;

    // ---- 口移し（行動モデル.md 4章）----
    private Ant sharePartner;
    private bool isGiver;
    private float shareRemaining;
    private float shareRate;

    /// <summary>興奮度。警報フェロモンで上がる（段階6で本格化）。</summary>
    private float alarm;

    // ---- 寿命（行動モデル.md 13-10）----
    /// <summary>この日齢になったら寿命で死ぬ。女王は死なない（段階5）</summary>
    [HideInInspector, SerializeField] private float lifespanDays = float.MaxValue;

    /// <summary>女王か。</summary>
    public bool IsQueen => caste == AntCaste.Queen;
    /// <summary>寿命（日）。</summary>
    public float LifespanDays => lifespanDays;
    private float trailLostTimer;
    private float lastFollowProbability;
    private float circleSign = 1f;
    private FoodSource targetFood;

    // ---- 外から見える値 ----
    public Vector2 Position => position;
    public bool IsFalling => isFalling;
    public AntView View => view;
    public AntCaste Caste => caste;
    public AntCarry Carrying => carrying;
    /// <summary>社会胃の中身（0〜1）。</summary>
    public float Crop => crop;
    /// <summary>体の蓄え（0〜1）。</summary>
    public float Reserve => reserve;
    /// <summary>空腹度（0〜1）。社会胃の裏返しで、別には持たない。</summary>
    public float Hunger => 1f - crop;
    /// <summary>仲間へ配るために持ち帰っている分。</summary>
    public float CarryLoad => carryLoad;
    /// <summary>Explore の閾値。低いほど外へ出やすい。</summary>
    public float ExploreThreshold => theta[ThetaExplore];
    /// <summary>Dig の閾値。低いほど掘りやすい。</summary>
    public float DigThreshold => theta[ThetaDig];
    /// <summary>育児の腰の重さ。</summary>
    public float NurseThreshold => theta[ThetaNurse];
    /// <summary>今、土を掘っている最中か。</summary>
    public bool IsDigging => digTimer > 0f;
    /// <summary>巣の中にいるか（巣の匂いの濃さで決まる）。</summary>
    public bool IsInNest => isInNest;
    /// <summary>今、仲間と口移しの最中か。</summary>
    public bool IsSharing => sharePartner != null;
    /// <summary>興奮度（0〜1）。</summary>
    public float Alarm => alarm;
    /// <summary>外勤寄りの度合い。閾値の裏返しで、情報パネルのゲージに出す。</summary>
    public float OutdoorTendency => 1f - theta[ThetaExplore];
    /// <summary>道しるべをどれくらいたどっているか（0〜1）。段階3cの「気持ち」で使う。</summary>
    public float FollowProbability => lastFollowProbability;
    /// <summary>匂いが途切れたと感じている最中か。</summary>
    public bool TrailLost => trailLostTimer > 0f;
    /// <summary>巣の外に出てからの時間（秒）。</summary>
    public float TimeOutside => timeOutside;

    public float AgeDays
    {
        get
        {
            if (clock == null) return startAgeDays;
            return startAgeDays + (float)(clock.ElapsedDays - bornAtElapsedDays);
        }
    }

    /// <summary>今やっている仕事。</summary>
    public AntTask CurrentTask => isFalling ? AntTask.Fall : task;

    /// <summary>
    /// 気持ち（行動モデル.md 7章）。上から順に見て、最初に当てはまったものを返す。
    /// 感情の変数は持たず、内部状態をそのまま言葉に訳しているだけ。
    /// </summary>
    public AntMood CurrentMood
    {
        get
        {
            if (settings == null) return AntMood.Calm;

            // 女王は専用の文（行動モデル.md 13-9）
            if (IsQueen)
            {
                var queen = GetComponent<Queen>();
                if (queen != null && queen.JustLaid) return AntMood.QueenLaid;
                float threshold = broodSettings != null ? broodSettings.queenBegThreshold : 0.4f;
                if (Hunger > threshold) return AntMood.QueenHungry;
                return AntMood.QueenCalm;
            }

            if (alarm > settings.moodAlarmThreshold) return AntMood.Alarmed;
            if (Hunger > settings.moodHungryThreshold) return AntMood.Hungry;
            if (trailLostTimer > 0f) return AntMood.TrailLost;
            if (task == AntTask.ReturnWithFood) return AntMood.CarryingFood;
            if (task == AntTask.CarrySoilOut) return AntMood.CarryingSoil;
            // 巣の外にいて、巣の匂いがほとんど届いていない
            if (!isInNest && nestHere < settings.nestFar) return AntMood.FarFromNest;
            if (task == AntTask.Dig) return AntMood.Digging;
            if (carriedBrood != null) return AntMood.CarryingBrood;
            if (task == AntTask.Nurse) return feedingNow ? AntMood.Feeding : AntMood.SeekingHungryBrood;
            if (task == AntTask.Explore && lastFollowProbability > settings.moodFollowThreshold) return AntMood.FollowingTrail;
            if (task == AntTask.Explore) return AntMood.Searching;
            if (sharePartner != null) return AntMood.Sharing;
            return AntMood.Calm;
        }
    }

    /// <summary>今いる場所の種類。まわりの土の形から決める。</summary>
    public AntPlace CurrentPlace
    {
        get
        {
            if (grid == null) return AntPlace.Surface;
            int x, y;
            if (!grid.WorldToCell(position, out x, out y)) return AntPlace.Surface;
            if (y >= grid.SurfaceRow) return AntPlace.Surface;

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
        // 保存された配列の長さが合わないことがあるので、ここで整える
        if (theta == null || theta.Length != ThetaCount) theta = new float[ThetaCount];

        if (grid == null) grid = FindFirstObjectByType<SoilGrid>();
        if (view == null) view = GetComponent<AntView>();
        if (pheromones == null) pheromones = FindFirstObjectByType<PheromoneField>();
        if (nestField == null) nestField = FindFirstObjectByType<NestField>();
        if (colony == null) colony = FindFirstObjectByType<Colony>();
        if (mound == null) mound = FindFirstObjectByType<MoundBuilder>();
        if (broodField == null) broodField = FindFirstObjectByType<BroodField>();
        if (broodSettings == null && broodField != null) broodSettings = broodField.Settings;
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
        // 運んでいた子どもを持ったまま消えないように、その場へ置く
        DropBroodHere();
        // 交換の途中で消えたら、相手を解放しておく
        EndShare();
    }

    private void Start()
    {
        position = transform.position;
        SnapToNearestWalkable();

        direction = Rotate(Vector2.right, Random.Range(0f, 360f));
        circleSign = Random.value < 0.5f ? -1f : 1f;

        // 個体ごとに知覚と判断の時刻をずらす（全員が同じフレームで考えないように）
        if (settings != null)
        {
            perceptionAccum = Random.Range(0f, settings.perceptionInterval);
            decisionTimer = Random.Range(0f, settings.decisionInterval);

            // 開始時の社会胃をばらつかせる。全員が同時に空腹になって一斉に出るのを避ける
            if (!cropOverridden) crop = Random.Range(settings.startCropMin, settings.startCropMax);

            // 体の蓄えは個体ごとにばらつかせる。一斉に尽きるのを避ける
            reserve = Random.Range(settings.startReserveMin, settings.startReserveMax);

            // 仕事ごとの「日齢の曲線」から始める。これが齢間分業になる（行動モデル.md 13-5）
            for (int i = 0; i < ThetaCount; i++) theta[i] = AgeThreshold(i);
        }

        // 寿命を個体ごとにばらつかせる（女王は段階5では死なない）
        if (broodSettings != null && !IsQueen)
        {
            float variation = broodSettings.lifespanVariation;
            lifespanDays = broodSettings.workerLifespanDays * Random.Range(1f - variation, 1f + variation);
        }

        ApplyTransform(0f);
    }

    private void Update()
    {
        if (grid == null || settings == null) return;

        float deltaTime = Time.deltaTime;
        if (deltaTime <= 0f)
        {
            if (view != null) view.UpdateGait(0f, 0f);
            return;
        }

        if (alarm > 0f) alarm = Mathf.Max(0f, alarm - settings.alarmDecayPerSecond * deltaTime);

        PerceptionStep(deltaTime);

        // 口移しの最中も、土を掘っている最中も、その場で止まる
        float moved = 0f;
        if (sharePartner != null) ShareStep(deltaTime);
        else if (digTimer > 0f) DigProgress(deltaTime);
        else moved = MoveStep(deltaTime);

        MetabolismStep(deltaTime, moved);
        DecisionStep(deltaTime);
        ApplyTransform(deltaTime);

        if (view != null)
        {
            view.SetAntennating(sharePartner != null);
            view.UpdateGait(moved, deltaTime);
        }
    }

    // ============================================================
    // 行動層：0.1秒ごとの知覚tick
    // ============================================================

    private void PerceptionStep(float deltaTime)
    {
        if (trailLostTimer > 0f) trailLostTimer -= deltaTime;

        perceptionAccum += deltaTime;
        float interval = Mathf.Max(0.0001f, settings.perceptionInterval);
        if (perceptionAccum < interval) return;

        // 速さを上げて何回分も追い越したときは、まとめて1回で済ませる（行動モデル.md 1章）
        float tickTime = perceptionAccum;
        perceptionAccum = 0f;
        Perceive(tickTime);
    }

    private void Perceive(float tickTime)
    {
        nestHere = nestField != null ? nestField.Sample(position) : 0f;
        bool inside = nestHere >= settings.nestInside;
        isInNest = inside;

        switch (task)
        {
            case AntTask.RestInNest:
                RestInNestStep(tickTime, inside);
                break;
            case AntTask.Explore:
                ExploreStep(tickTime, nestHere, inside);
                break;
            case AntTask.ReturnWithFood:
            case AntTask.ReturnEmpty:
                ReturnStep(tickTime, inside);
                break;
            case AntTask.Dig:
                DigStep(tickTime, inside);
                break;
            case AntTask.CarrySoilOut:
                CarrySoilOutStep(tickTime, inside);
                break;
            case AntTask.Nurse:
                NurseStep(tickTime, inside);
                break;
            default:
                task = AntTask.RestInNest;
                break;
        }
    }

    /// <summary>巣の中で休む。奥（匂いの濃いほう）へゆっくり寄りながらうろつく。</summary>
    private void RestInNestStep(float tickTime, bool inside)
    {
        // 女王は奥の部屋から動かない（行動モデル.md 13-1）
        if (IsQueen)
        {
            QueenRestStep(tickTime);
            return;
        }

        timeOutside = 0f;
        // 交換中は動かない
        if (sharePartner != null) return;

        // 触れられる距離に仲間がいて、持ち分に差があれば口移しが始まる
        if (TryStartSharing()) return;

        // 持ち帰った分は、まず空腹の子どもと女王へ回す（行動モデル.md 4章）
        if (carryLoad > 0f && TryDeliverLoad(tickTime)) return;

        // 空腹なら、いちばん近い仲間へ寄っていく（相手の中身は知らない。触れて差があれば流れる）
        float begThreshold = IsQueen && broodSettings != null
            ? broodSettings.queenBegThreshold
            : settings.begThreshold;
        if (Hunger > begThreshold && colony != null)
        {
            Ant nearest = colony.FindNearestNestmate(this, settings.begSearchRange);
            if (nearest != null)
            {
                Vector2 toNeighbor = nearest.Position - position;
                if (toNeighbor.sqrMagnitude > 0f) direction = toNeighbor.normalized;
                AddWanderNoise(tickTime, settings.wanderSigma * 0.3f);
                return;
            }
        }

        SteerAlongNest(tickTime, true, settings.homingBias);
        AddWanderNoise(tickTime, settings.wanderSigma);

        // 外へ出るかどうかは DecisionStep（反応閾値）が決める
    }

    /// <summary>
    /// 女王の過ごし方（行動モデル.md 13-1）。
    ///
    /// 女王はふだん奥の部屋で静止している。空腹でも自分から働きアリを探しには行かず、
    /// その場に空腹の匂い（幼虫と同じ層）を置いて、世話を待つ。
    /// たまにだけ少し歩き、そのときは巣の匂いの濃い側（奥）へ強く偏る。
    /// </summary>
    private void QueenRestStep(float tickTime)
    {
        timeOutside = 0f;
        if (sharePartner != null) return;

        // 触れた相手と差があれば口移しは起きる（働きアリ側は女王を優先する）
        if (TryStartSharing()) return;

        // 空腹なら、その場に匂いを置く。たどる側は段階5bで作る
        float begThreshold = broodSettings != null ? broodSettings.queenBegThreshold : settings.begThreshold;
        if (Hunger > begThreshold) DepositHunger();

        // 歩いている途中なら、奥へ強く寄りながら歩き続ける
        if (queenWalkTimer > 0f)
        {
            queenWalkTimer -= tickTime;
            float bias = broodSettings != null ? broodSettings.queenNestBias : 1f;
            SteerAlongNest(tickTime, true, settings.homingBias * bias);
            AddWanderNoise(tickTime, settings.wanderSigma * 0.3f);
            return;
        }

        // 止まっているときに、たまに歩き出す
        if (broodSettings == null) return;
        if (Random.value < broodSettings.queenWanderChance)
            queenWalkTimer = broodSettings.queenWanderSeconds;
    }

    /// <summary>
    /// 持ち帰った餌（carryLoad）を、空腹の子どもと女王へ直接渡す（行動モデル.md 4章）。
    ///
    /// 餌が細るときほど、少ない持ち帰りが働きアリ同士で薄まる前に
    /// 女王と幼虫へ届くようにするための道順。
    /// 空腹の匂いを感じないか、持ち分を使い切ったら、従来どおりの口移しに戻る。
    /// </summary>
    private bool TryDeliverLoad(float tickTime)
    {
        if (broodField == null || broodSettings == null || pheromones == null) return false;

        // まわりに空腹の匂いがなければ、わざわざ運ばない
        if (pheromones.Sample(position, PheromoneLayer.LarvaHunger) < broodSettings.deliverHungerMin)
            return false;

        // 触れた相手が女王なら、そちらを優先して流す
        if (TryStartSharing(true)) return true;

        BroodItem larva = broodField.FindHungryLarva(position, broodSettings.broodSenseRange,
            broodSettings.larvaHungerThreshold);
        if (larva != null)
        {
            if (WithinContact(larva))
            {
                float given = broodField.Feed(larva, Mathf.Min(broodSettings.feedAmount, carryLoad));
                carryLoad = Mathf.Max(0f, carryLoad - given);
                feedingNow = true;
                return true;
            }
            SteerTowardCell(larva.cellX, larva.cellY, tickTime);
            return true;
        }

        // 空腹の匂いの濃い側へ登る
        SteerAlongLarvaHunger(tickTime);
        AddWanderNoise(tickTime, settings.wanderSigma * 0.5f);
        return true;
    }

    /// <summary>今いるマスに空腹の匂いを置く（行動モデル.md 13-3 の larvaHunger 層）。</summary>
    private void DepositHunger()
    {
        if (pheromones == null || grid == null) return;
        int x, y;
        if (!grid.WorldToCell(position, out x, out y)) return;

        float scale = broodSettings != null ? broodSettings.queenHungerDeposit : 1f;
        pheromones.DepositAt(x, y, PheromoneLayer.LarvaHunger, Hunger * scale);
    }

    // ---- 掘削（行動モデル.md 12章）----

    /// <summary>
    /// 掘る仕事。目的地は持たない。
    /// いつもどおり壁沿いに歩き、隣り合った土のマスを確率で掘る。
    /// </summary>
    private void DigStep(float tickTime, bool inside)
    {
        timeOutside = 0f;

        // 掘っている最中は動かない（進み具合は Update 側で数える）
        if (digTimer > 0f) return;

        // 巣の外に出てしまっていたら、まず巣へ戻る
        if (!inside)
        {
            SteerAlongNest(tickTime, true, settings.turnGain);
            AddWanderNoise(tickTime, settings.wanderSigma * 0.5f);
            return;
        }

        // 掘れる場所が見つからないまま歩き続けたら、いったん巣の仕事に戻る
        digSearchTimer += tickTime;
        if (digSearchTimer > settings.digGiveUpSeconds)
        {
            task = AntTask.RestInNest;
            digSearchTimer = 0f;
            return;
        }

        // 壁沿いを歩く（移動層がすでに面に沿わせるので、ここでは向きを揺らすだけ）
        AddWanderNoise(tickTime, settings.wanderSigma);

        int x, y;
        if (!grid.WorldToCell(position, out x, out y)) return;

        // 掘る候補を決める（行動モデル.md 12-3）
        int candidateX, candidateY;
        float marker;
        if (!FindDigTarget(x, y, out candidateX, out candidateY, out marker)) return;

        float markerMax = pheromones != null ? Mathf.Max(0.0001f, pheromones.GetMax(PheromoneLayer.Dig)) : 1f;
        int neighbors = colony != null ? colony.CountNestmatesNear(this, settings.digCrowdRadius) : 0;
        float crowd = Mathf.Clamp01(neighbors / Mathf.Max(0.0001f, settings.digCrowdFull));

        // 跡をたどっているときだけ、跡の項が効く。
        // 跡がないとき（新しいトンネルの起点）は基礎の確率だけで掘る
        float probability = settings.digBase + settings.digCrowdGain * crowd;
        if (marker > 0f) probability += settings.digMarkerGain * (marker / markerMax);

        if (Random.value >= probability) return;

        digCellX = candidateX;
        digCellY = candidateY;
        digTimer = settings.digSeconds;
    }

    /// <summary>
    /// 掘る土のマスを決める（行動モデル.md 12-3）。
    ///
    /// 自分の隣で掘削跡がいちばん濃い空洞を見つけ、その**反対側**の土を掘る。
    /// 掘りたての空洞ほど跡が濃いので、直前に掘られた向きへ穴が伸びる。
    /// 反対側が土でなければ（石・空洞・空気なら）掘らない。
    ///
    /// 跡のある空洞が隣にひとつもないときだけ、隣接する土からランダムに選ぶ。
    /// これが新しいトンネルの起点になる。
    /// </summary>
    private bool FindDigTarget(int x, int y, out int targetX, out int targetY, out float marker)
    {
        targetX = 0;
        targetY = 0;
        marker = 0f;

        // 隣の空洞のうち、跡がいちばん濃いもの（土と石は常に 0 なので自然に除かれる）
        int markedDX = 0, markedDY = 0;
        float best = 0f;
        for (int i = 0; i < 4; i++)
        {
            int dx = i == 0 ? 1 : (i == 1 ? -1 : 0);
            int dy = i == 2 ? 1 : (i == 3 ? -1 : 0);
            float value = pheromones != null ? pheromones.GetAt(x + dx, y + dy, PheromoneLayer.Dig) : 0f;
            if (value <= best) continue;
            best = value;
            markedDX = dx;
            markedDY = dy;
        }

        if (best > 0f)
        {
            // 跡のある空洞の反対側を掘る。これで穴が一直線に伸びる
            int tx = x - markedDX;
            int ty = y - markedDY;
            if (!grid.IsInside(tx, ty)) return false;
            if (grid.GetCell(tx, ty) != CellType.Soil) return false;

            targetX = tx;
            targetY = ty;
            marker = best;
            return true;
        }

        // 跡がない：隣接する土からランダムに1つ選ぶ（新しいトンネルの起点）
        int candidateCount = 0;
        for (int i = 0; i < 4; i++)
        {
            int dx = i == 0 ? 1 : (i == 1 ? -1 : 0);
            int dy = i == 2 ? 1 : (i == 3 ? -1 : 0);
            int nx = x + dx;
            int ny = y + dy;
            if (!grid.IsInside(nx, ny)) continue;
            if (grid.GetCell(nx, ny) != CellType.Soil) continue;

            candidateCount++;
            // 数えながら等確率で1つを選ぶ
            if (Random.Range(0, candidateCount) != 0) continue;
            targetX = nx;
            targetY = ny;
        }
        return candidateCount > 0;
    }

    /// <summary>掘る時間を進める。掘り終わったらマスを空洞に変える。</summary>
    private void DigProgress(float deltaTime)
    {
        digTimer -= deltaTime;
        if (digTimer > 0f) return;
        digTimer = 0f;

        // 掘っているあいだに、ほかのアリが掘り終えているかもしれない
        if (grid.GetCell(digCellX, digCellY) != CellType.Soil) return;

        grid.SetCell(digCellX, digCellY, CellType.Cavity);

        // 掘った跡の匂いを、新しくできた空洞1マスだけに置く。
        // まわりに広げると先端が埋もれて、穴が伸びずに横に広がってしまう
        if (pheromones != null)
        {
            pheromones.DepositAt(digCellX, digCellY, PheromoneLayer.Dig, settings.depositDig);
        }

        if (colony != null) colony.ReportDug();

        // 掘った土を1粒持って、外へ運び出す（行動モデル.md 12-5）
        carrying = AntCarry.Soil;
        task = AntTask.CarrySoilOut;
        digSearchTimer = 0f;
        dumpTimer = 0f;
    }

    // ---- 土の運び出しと塚（行動モデル.md 12-5）----

    /// <summary>
    /// 掘った土を外へ運び出す。
    /// 巣の匂いの勾配を下って入口を出て、入口から離れたところで地表に積む。
    /// 置けなければ歩いて別の場所で試し、それでも駄目なら捨てる。
    /// </summary>
    private void CarrySoilOutStep(float tickTime, bool inside)
    {
        if (inside)
        {
            // まだ巣の中：匂いの薄いほう（＝入口）へ向かう
            SteerAlongNest(tickTime, false, settings.turnGain);
            AddWanderNoise(tickTime, settings.wanderSigma * 0.5f);
            return;
        }

        dumpTimer += tickTime;

        // 入口から十分離れていれば、その場に積んでみる
        bool farEnough = nestField == null || !nestField.HasEntrance
            || Mathf.Abs(position.x - nestField.EntranceWorld.x) >= settings.dumpMinDistance;

        if (farEnough && mound != null)
        {
            MoundPlacementResult placement;
            if (mound.TryPlaceSoil(position, out placement))
            {
                FinishCarryingSoil();
                return;
            }
        }

        // 置けなかった：歩いて別の場所を探す
        if (dumpTimer > settings.dumpGiveUpSeconds)
        {
            // 諦めてその場で捨てる
            if (colony != null) colony.ReportDiscard();
            FinishCarryingSoil();
            return;
        }

        // 入口から離れる向き（巣の匂いが薄くなるほう）へ歩く
        SteerAlongNest(tickTime, false, settings.homingBias);
        AddWanderNoise(tickTime, settings.wanderSigma);
    }

    /// <summary>土を手放して巣の仕事に戻る。</summary>
    private void FinishCarryingSoil()
    {
        carrying = AntCarry.None;
        task = AntTask.RestInNest;
        dumpTimer = 0f;
    }

    // ---- 口移し（行動モデル.md 4章）----

    /// <summary>触れられる距離の仲間と、持ち分に差があれば口移しを始める。</summary>
    private bool TryStartSharing(bool queenOnly = false)
    {
        if (colony == null) return false;

        Ant other = colony.FindNearestNestmate(this, settings.contactRange);
        if (other == null || other.sharePartner != null || !other.isInNest) return false;
        if (queenOnly && !other.IsQueen) return false;

        // 「自分の持ち分」と「相手の社会胃」を比べて、多いほうが渡す側になる
        float myTotal = crop + carryLoad;
        float otherTotal = other.crop + other.carryLoad;

        // 相手が女王なら、差の大きさを問わず流す（女王優先。行動モデル.md 13-1）
        float threshold = other.IsQueen ? 0f : settings.shareThreshold;

        if (myTotal - other.crop > threshold)
        {
            BeginShare(this, other, (myTotal - other.crop) * 0.5f);
            return true;
        }
        if (otherTotal - crop >= settings.shareThreshold)
        {
            BeginShare(other, this, (otherTotal - crop) * 0.5f);
            return true;
        }
        return false;
    }

    /// <summary>渡す側と受け取る側を決めて、口移しを始める。</summary>
    private static void BeginShare(Ant giver, Ant taker, float amount)
    {
        giver.sharePartner = taker;
        giver.isGiver = true;
        giver.shareRemaining = amount;
        giver.shareRate = amount / Mathf.Max(0.0001f, giver.settings.shareSeconds);

        taker.sharePartner = giver;
        taker.isGiver = false;
    }

    /// <summary>口移しを少しずつ進める。渡す側だけが中身を動かす。</summary>
    private void ShareStep(float deltaTime)
    {
        if (sharePartner == null) return;

        // 相手が死んだ、巣の外へ出た、離れた などで続けられなくなったら終わり
        if (sharePartner.sharePartner != this || !isInNest || !sharePartner.isInNest
            || (sharePartner.Position - position).sqrMagnitude > settings.contactRange * settings.contactRange * 4f)
        {
            EndShare();
            return;
        }

        if (!isGiver) return;   // 受け取る側は止まっているだけ

        float amount = Mathf.Min(shareRemaining, shareRate * deltaTime);
        if (amount <= 0f)
        {
            EndShare();
            return;
        }

        // 持ち帰った分を先に使い、足りなければ自分の社会胃から出す
        float fromLoad = Mathf.Min(carryLoad, amount);
        carryLoad -= fromLoad;
        float fromCrop = Mathf.Min(crop, amount - fromLoad);
        crop -= fromCrop;

        float given = fromLoad + fromCrop;
        if (given <= 0f)
        {
            EndShare();
            return;
        }

        sharePartner.ReceiveShare(given);
        shareRemaining -= given;
        if (shareRemaining <= 0f) EndShare();
    }

    /// <summary>口移しで受け取る。</summary>
    private void ReceiveShare(float amount)
    {
        crop = Mathf.Clamp01(crop + amount);
    }

    /// <summary>口移しを終える（両方の状態を戻す）。</summary>
    private void EndShare()
    {
        if (sharePartner != null && sharePartner.sharePartner == this)
        {
            sharePartner.sharePartner = null;
            sharePartner.shareRemaining = 0f;
        }
        sharePartner = null;
        shareRemaining = 0f;
    }

    /// <summary>
    /// 社会胃を減らす（行動モデル.md 4章）。
    /// 動いているほうが多く減る。空っぽのまま続くと餓死する。
    /// </summary>
    private void MetabolismStep(float deltaTime, float movedDistance)
    {
        metabolismTimer += deltaTime;
        movedSinceMetabolism += movedDistance;

        float interval = Mathf.Max(0.0001f, settings.metabolismInterval);
        if (metabolismTimer < interval) return;

        float elapsed = metabolismTimer;
        metabolismTimer = 0f;

        float secondsPerDay = clock != null ? clock.SecondsPerDay : 300f;
        float drainPerSecond = 1f / Mathf.Max(0.0001f, settings.fullToEmptyDays * secondsPerDay);
        bool moving = movedSinceMetabolism > 0.01f;
        movedSinceMetabolism = 0f;

        float multiplier = moving ? settings.movingMetabolism : 1f;
        // 女王は産卵のぶん多く消費する
        if (IsQueen && broodSettings != null) multiplier *= broodSettings.queenMetabolism;

        // まず社会胃から引く。足りない分は体の蓄えでまかなう（行動モデル.md 4章）
        float demand = drainPerSecond * elapsed * multiplier;
        if (crop >= demand)
        {
            crop -= demand;
        }
        else
        {
            float shortage = demand - crop;
            crop = 0f;
            // 足りない分の「時間」を蓄えで払う。蓄えは社会胃より reserveDays/fullToEmptyDays 倍もつ
            float rate = settings.fullToEmptyDays / Mathf.Max(0.0001f, settings.reserveDays);
            reserve = Mathf.Max(0f, reserve - shortage * rate);
        }

        // 社会胃に余りがあれば、少しずつ体の蓄えへ回す
        FillReserve(elapsed / secondsPerDay);

        // 寿命で死ぬ
        if (AgeDays >= lifespanDays)
        {
            if (colony != null) colony.ReportDeath(this, AntDeathCause.OldAge);
            Destroy(gameObject);
            return;
        }

        // 社会胃も蓄えも尽きたら死ぬ（死体の扱いは段階6）
        if (crop > 0f || reserve > 0f) return;

        if (colony != null) colony.ReportDeath(this, AntDeathCause.Starvation);
        Destroy(gameObject);
    }

    /// <summary>
    /// 社会胃の余りを体の蓄えへ移す（行動モデル.md 4章）。
    /// 口移しで配る分（crop）を減らしすぎないよう、満ちているときだけ回す。
    /// </summary>
    private void FillReserve(float elapsedDays)
    {
        if (reserve >= 1f) return;
        if (crop <= settings.reserveFillAbove) return;

        float move = settings.reserveFillPerDay * elapsedDays;
        move = Mathf.Min(move, crop - settings.reserveFillAbove);
        move = Mathf.Min(move, 1f - reserve);
        if (move <= 0f) return;

        crop -= move;
        reserve += move;
    }

    /// <summary>
    /// 仕事を選び直す（行動モデル.md 6章の反応閾値モデル）。
    /// 巣の中で休んでいるときだけ、外へ出るかを確率で決める。
    /// </summary>
    private void DecisionStep(float deltaTime)
    {
        decisionTimer -= deltaTime;
        if (decisionTimer > 0f) return;
        decisionTimer = Mathf.Max(0.0001f, settings.decisionInterval);

        if (IsQueen) return;        // 女王は仕事を選ばない。産卵だけをする
        if (task != AntTask.RestInNest) return;
        if (sharePartner != null) return;   // 分け合っている最中は出発しない

        // 仕事ごとに P = S²/(S²+θ²) を出し、順番の偏りが出ないよう順序をランダムにして判定する
        ShuffleThetaOrder();
        for (int i = 0; i < ThetaCount; i++)
        {
            int index = thetaOrder[i];
            float stimulus = StimulusOf(index);
            if (stimulus <= 0f) continue;

            float probability = (stimulus * stimulus) / (stimulus * stimulus + theta[index] * theta[index]);
            if (Random.value >= probability) continue;

            // この仕事に就いた。次はもう少し就きやすくなる
            task = TaskOf(index);
            timeOutside = 0f;
            targetFood = null;
            nurseSearchTimer = 0f;
            theta[index] = Mathf.Clamp(theta[index] - settings.thetaLearn, settings.thetaMin, settings.thetaMax);

            // 選ばなかった仕事の腰は重くなる
            for (int j = 0; j < ThetaCount; j++)
            {
                if (j == index) continue;
                theta[j] = Mathf.Clamp(theta[j] + settings.thetaForget, settings.thetaMin, settings.thetaMax);
            }
            ApplyAgeDrift();
            return;
        }

        // どれも選ばなかった。すべての仕事で少しずつ腰が重くなる
        for (int i = 0; i < ThetaCount; i++)
        {
            theta[i] = Mathf.Clamp(theta[i] + settings.thetaForget, settings.thetaMin, settings.thetaMax);
        }
        ApplyAgeDrift();
    }

    /// <summary>
    /// その日齢なら本来どれくらいの腰の重さか（行動モデル.md 13-5）。
    /// 若いうちは育児に反応しやすく、年を取るほど外へ出やすい。
    /// </summary>
    private float AgeThreshold(int thetaIndex)
    {
        float t = Mathf.Clamp01(AgeDays / Mathf.Max(0.0001f, settings.matureDays));
        switch (thetaIndex)
        {
            case ThetaDig:
                // 掘削は内勤と外勤の中間なので、日齢では決めない
                return settings.thetaDigInitial;
            case ThetaNurse:
                return Mathf.Clamp(
                    Mathf.Lerp(settings.thetaNurseYoung, settings.thetaNurseOld, t), 0.1f, 0.9f);
            default:
                return Mathf.Clamp(1f - t, 0.15f, 0.95f);
        }
    }

    /// <summary>
    /// 学習で動いた閾値を、日齢の曲線へ少しだけ引き戻す（行動モデル.md 13-5）。
    /// 若いうちに育児で固まった個体も、年を取れば徐々に外勤へ移る。
    /// </summary>
    private void ApplyAgeDrift()
    {
        float drift = settings.ageDrift;
        if (drift <= 0f) return;

        for (int i = 0; i < ThetaCount; i++)
        {
            theta[i] = Mathf.Clamp(Mathf.MoveTowards(theta[i], AgeThreshold(i), drift),
                settings.thetaMin, settings.thetaMax);
        }
    }

    /// <summary>その仕事の刺激（コロニーが計算したもの）。</summary>
    private float StimulusOf(int thetaIndex)
    {
        if (colony == null) return 0f;
        switch (thetaIndex)
        {
            case ThetaDig: return colony.DigStimulus;
            case ThetaNurse: return colony.NurseStimulus;
            default: return colony.ForageStimulus;
        }
    }

    /// <summary>閾値の番号に対応する仕事。</summary>
    private AntTask TaskOf(int thetaIndex)
    {
        switch (thetaIndex)
        {
            case ThetaDig: return AntTask.Dig;
            case ThetaNurse: return AntTask.Nurse;
            default: return AntTask.Explore;
        }
    }

    /// <summary>判定する順番を混ぜる（先に見た仕事が有利にならないように）。</summary>
    private void ShuffleThetaOrder()
    {
        for (int i = ThetaCount - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            int tmp = thetaOrder[i];
            thetaOrder[i] = thetaOrder[j];
            thetaOrder[j] = tmp;
        }
    }

    // ---- 育児（行動モデル.md 13-4）----

    /// <summary>
    /// 幼虫の世話。空腹の匂いを登り、見つけた幼虫に口移しで食べさせる。
    /// 1回食べさせたら巣の仕事に戻る（掘削と同じ作り）。
    /// 相手が見つからないまま歩き回っていると、そのうち諦める。
    /// </summary>
    private void NurseStep(float tickTime, bool inside)
    {
        timeOutside = 0f;
        if (sharePartner != null) return;

        // 子どもを持っているあいだは、置く場所を探すのが仕事になる
        if (carriedBrood != null)
        {
            CarryBroodStep(tickTime, inside);
            return;
        }

        // 触れた相手と差があれば口移しは起きる。空腹の女王も同じ匂いを出すので、
        // 育児係が匂いをたどってたどり着き、接触して流れる（行動モデル.md 13-1）
        if (TryStartSharing()) return;

        if (!inside)
        {
            SteerAlongNest(tickTime, true, settings.turnGain);
            AddWanderNoise(tickTime, settings.wanderSigma * 0.5f);
            return;
        }

        nurseSearchTimer += tickTime;
        if (nurseSearchTimer > NurseGiveUpSeconds)
        {
            task = AntTask.RestInNest;
            nurseSearchTimer = 0f;
            return;
        }

        // 分けられる分がなくなったら、育児はいったんやめる（体の蓄えは分けない）
        if (broodField == null || broodSettings == null || crop <= broodSettings.nurseMinCrop)
        {
            task = AntTask.RestInNest;
            nurseSearchTimer = 0f;
            return;
        }

        BroodItem larva = broodField.FindHungryLarva(position, broodSettings.broodSenseRange,
            broodSettings.larvaHungerThreshold);
        if (larva != null)
        {
            if (WithinContact(larva))
            {
                FeedLarva(larva);
                return;
            }
            // 感知範囲に入っていれば、匂いではなく相手そのものへ寄っていく
            SteerTowardCell(larva.cellX, larva.cellY, tickTime);
            return;
        }
        feedingNow = false;

        // 感知範囲に空腹の幼虫がいないときだけ、はぐれた子どもを運ぶ（行動モデル.md 13-6）
        if (TryPickBrood()) return;

        SearchForBrood(tickTime);
    }

    /// <summary>そのマスへ向かって歩く（近くまで来て相手が分かっているとき）。</summary>
    private void SteerTowardCell(int cellX, int cellY, float tickTime)
    {
        Vector2 target = grid.CellToWorld(cellX, cellY);
        Vector2 to = target - position;
        if (to.sqrMagnitude > 0.0001f) direction = to.normalized;
        AddWanderNoise(tickTime, settings.wanderSigma * 0.3f);
    }

    /// <summary>
    /// 世話する相手を探して歩く。
    /// 空腹の匂いは薄く広がらないので、感じられるときだけ登り、
    /// 感じられないときは巣の奥（子どもの塊ができやすい側）へ寄りながら探す。
    /// </summary>
    private void SearchForBrood(float tickTime)
    {
        float here = pheromones != null
            ? pheromones.Sample(position, PheromoneLayer.LarvaHunger)
            : 0f;

        if (here > 0f) SteerAlongLarvaHunger(tickTime);
        else SteerAlongNest(tickTime, true, settings.homingBias);

        AddWanderNoise(tickTime, settings.wanderSigma);
    }

    /// <summary>
    /// はぐれた子どもを拾う（行動モデル.md 13-6）。
    /// まわりに子どもが少ないマスほど拾いやすい。
    /// </summary>
    private bool TryPickBrood()
    {
        int x, y;
        if (!grid.WorldToCell(position, out x, out y)) return false;

        // 自分のマスと隣を見る
        for (int i = 0; i < 5; i++)
        {
            int dx = i == 1 ? 1 : (i == 2 ? -1 : 0);
            int dy = i == 3 ? 1 : (i == 4 ? -1 : 0);
            int cx = x + dx;
            int cy = y + dy;

            var list = broodField.GetAtCell(cx, cy);
            if (list == null || list.Count == 0) continue;

            float f = Crowdedness(cx, cy);
            float k = broodSettings.kPick;
            float p = (k / (k + f)) * (k / (k + f));
            if (Random.value >= p) continue;

            carriedBrood = broodField.PickUp(cx, cy, this);
            if (carriedBrood == null) continue;

            carryBroodTimer = 0f;
            carrying = CarryOf(carriedBrood.stage);
            return true;
        }
        return false;
    }

    /// <summary>
    /// 子どもを持って巣の中を歩き、子どもが多い場所ほど置きやすい（行動モデル.md 13-6）。
    /// </summary>
    private void CarryBroodStep(float tickTime, bool inside)
    {
        carryBroodTimer += tickTime;

        int x, y;
        bool hasCell = grid.WorldToCell(position, out x, out y);

        // 長く持ちすぎたら、その場に置く。ただし巣の外には置かない
        if (carryBroodTimer > broodSettings.carryGiveUpSeconds && hasCell && inside)
        {
            DropBrood(x, y);
            return;
        }

        if (!inside)
        {
            SteerAlongNest(tickTime, true, settings.turnGain);
            AddWanderNoise(tickTime, settings.wanderSigma * 0.5f);
            return;
        }

        if (hasCell && grid.IsPassable(x, y))
        {
            float f = Crowdedness(x, y);
            float k = broodSettings.kDrop;
            float p = (f / (k + f)) * (f / (k + f));
            if (Random.value < p)
            {
                DropBrood(x, y);
                return;
            }
        }

        // 置き場所を探して、巣の奥寄りを歩く
        SteerAlongNest(tickTime, true, settings.homingBias);
        AddWanderNoise(tickTime, settings.wanderSigma);
    }

    /// <summary>そのマスのまわりが子どもでどれくらい混んでいるか（0〜1）。</summary>
    private float Crowdedness(int cellX, int cellY)
    {
        int near = broodField.CountNear(cellX, cellY, broodSettings.broodSenseRadius);
        return Mathf.Clamp01(near / Mathf.Max(0.0001f, broodSettings.broodFull));
    }

    /// <summary>持っている子どもをそのマスへ置き、巣の仕事に戻る。</summary>
    private void DropBrood(int cellX, int cellY)
    {
        broodField.PutDown(carriedBrood, cellX, cellY);
        carriedBrood = null;
        carryBroodTimer = 0f;
        carrying = AntCarry.None;
        task = AntTask.RestInNest;
        nurseSearchTimer = 0f;
    }

    /// <summary>今いるマスに子どもを置く（消えるときに使う）。</summary>
    private void DropBroodHere()
    {
        if (carriedBrood == null || grid == null || broodField == null) return;
        int x, y;
        if (grid.WorldToCell(position, out x, out y)) broodField.PutDown(carriedBrood, x, y);
        else carriedBrood.carriedBy = null;
        carriedBrood = null;
        carrying = AntCarry.None;
    }

    /// <summary>運んでいるものの表示。</summary>
    private static AntCarry CarryOf(BroodStage stage)
    {
        switch (stage)
        {
            case BroodStage.Egg: return AntCarry.Egg;
            case BroodStage.Larva: return AntCarry.Larva;
            default: return AntCarry.Cocoon;
        }
    }

    /// <summary>諦めるまでの時間（設定がなければ既定値）。</summary>
    private float NurseGiveUpSeconds => broodSettings != null ? broodSettings.nurseGiveUpSeconds : 20f;

    /// <summary>その幼虫に口が届くか。</summary>
    private bool WithinContact(BroodItem larva)
    {
        if (grid == null) return false;
        Vector2 target = grid.CellToWorld(larva.cellX, larva.cellY);
        return (target - position).sqrMagnitude <= settings.contactRange * settings.contactRange;
    }

    /// <summary>幼虫に一定量を流す。流した分は自分の社会胃から引く。</summary>
    private void FeedLarva(BroodItem larva)
    {
        // 社会胃は nurseMinCrop まで分けてよい（自分は体の蓄えでしのげる）
        float spare = Mathf.Max(0f, crop - broodSettings.nurseMinCrop);
        float given = broodField.Feed(larva, Mathf.Min(broodSettings.feedAmount, spare));
        crop = Mathf.Clamp01(crop - given);
        feedingNow = true;

        // 1回で満たなくても、いったん巣の仕事に戻って選び直す
        task = AntTask.RestInNest;
        nurseSearchTimer = 0f;
    }

    /// <summary>幼虫の空腹の匂いの濃い側へ曲がる。</summary>
    private void SteerAlongLarvaHunger(float tickTime)
    {
        if (pheromones == null) return;
        Vector2 leftPoint, rightPoint;
        GetSensorPoints(out leftPoint, out rightPoint);
        SteerByGradient(
            pheromones.Sample(leftPoint, PheromoneLayer.LarvaHunger),
            pheromones.Sample(rightPoint, PheromoneLayer.LarvaHunger),
            true, settings.turnGain, tickTime);
    }

    /// <summary>外を探す。</summary>
    private void ExploreStep(float tickTime, float nestHere, bool inside)
    {
        if (inside)
        {
            // まだ巣の中：匂いの薄いほう（＝入口）へ向かう
            timeOutside = 0f;
            SteerAlongNest(tickTime, false, settings.turnGain);
            AddWanderNoise(tickTime, settings.wanderSigma * 0.5f);
            return;
        }

        timeOutside += tickTime;

        // 餌が近くにあれば、そちらへ真っすぐ寄る
        FoodSource food = FindFoodInRange();
        if (food != null)
        {
            targetFood = food;
            Vector2 toFood = food.Position - position;
            if (toFood.sqrMagnitude <= settings.contactRange * settings.contactRange)
            {
                Eat(food);
                return;
            }
            if (toFood.sqrMagnitude > 0f) direction = toFood.normalized;
            return;
        }
        targetFood = null;

        // 匂いが途切れた直後は、その場で小さく円を描いて探す
        if (trailLostTimer > 0f)
        {
            direction = Rotate(direction, settings.circleTurnGain * circleSign * tickTime);
            AddWanderNoise(tickTime, settings.wanderSigma);
            CheckGiveUp();
            return;
        }

        // 触角2点で道しるべを読む
        float left, right;
        SampleTrail(out left, out right);
        float c = Mathf.Max(left, right);
        float k = Mathf.Max(0.0001f, settings.kTrail);
        float follow = c * c / (k * k + c * c);

        // 前はたどれていたのに急に薄くなったら「途切れた」
        if (lastFollowProbability > 0.5f && c < settings.kTrail * 0.3f)
        {
            trailLostTimer = settings.trailLostDuration;
            circleSign = Random.value < 0.5f ? -1f : 1f;
        }
        lastFollowProbability = follow;

        if (c > 0f && Random.value < follow)
        {
            SteerByGradient(left, right, true, settings.turnGain, tickTime);
        }
        else
        {
            AddWanderNoise(tickTime, settings.wanderSigma);
            // 巣から遠いと感じたら、匂いが濃くなるほうへ弱く寄る
            if (nestHere < settings.nestFar) SteerAlongNest(tickTime, true, settings.homingBias);
        }

        CheckGiveUp();
    }

    private void CheckGiveUp()
    {
        if (timeOutside <= settings.giveUpSeconds) return;
        task = AntTask.ReturnEmpty;
        lastFollowProbability = 0f;
    }

    /// <summary>餌を食べて、持ち帰りに移る。</summary>
    private void Eat(FoodSource food)
    {
        if (!food.TakeOne())
        {
            targetFood = null;
            return;
        }
        // その場で社会胃を満たし、さらに仲間へ配る分を持って帰る（行動モデル.md 4章）
        crop = 1f;
        carryLoad = settings.carryLoad;

        carrying = AntCarry.Food;
        task = AntTask.ReturnWithFood;
        targetFood = null;
        lastFollowProbability = 0f;
        trailLostTimer = 0f;
    }

    /// <summary>巣へ帰る。巣の匂いの濃いほうへ登る。</summary>
    private void ReturnStep(float tickTime, bool inside)
    {
        if (inside)
        {
            Arrive();
            return;
        }

        SteerAlongNest(tickTime, true, settings.turnGain);
        AddWanderNoise(tickTime, settings.wanderSigma * 0.4f);
    }

    /// <summary>巣に着いた。</summary>
    private void Arrive()
    {
        carrying = AntCarry.None;
        // 持ち帰った分（carryLoad）はそのまま持っている。
        // 巣の中で出会った仲間へ、口移しで配っていく
        task = AntTask.RestInNest;
        timeOutside = 0f;
        lastFollowProbability = 0f;
    }

    // ---- 触角（感知点）----

    /// <summary>触角の位置の道しるべの濃さを読む。</summary>
    private void SampleTrail(out float left, out float right)
    {
        left = 0f;
        right = 0f;
        if (pheromones == null) return;
        Vector2 leftPoint, rightPoint;
        GetSensorPoints(out leftPoint, out rightPoint);
        left = pheromones.Sample(leftPoint, PheromoneLayer.Trail);
        right = pheromones.Sample(rightPoint, PheromoneLayer.Trail);
    }

    /// <summary>触角の2点（体の前方、左右に開いた位置）。</summary>
    public void GetSensorPoints(out Vector2 left, out Vector2 right)
    {
        float forward = settings != null ? settings.sensorForward : 0.6f;
        float spread = settings != null ? settings.sensorSpread : 35f;
        left = position + Rotate(direction, spread) * forward;
        right = position + Rotate(direction, -spread) * forward;
    }

    /// <summary>巣の匂いの勾配に沿って曲がる。ascend が true なら濃いほうへ。</summary>
    private void SteerAlongNest(float tickTime, bool ascend, float gain)
    {
        if (nestField == null) return;
        Vector2 leftPoint, rightPoint;
        GetSensorPoints(out leftPoint, out rightPoint);
        SteerByGradient(nestField.Sample(leftPoint), nestField.Sample(rightPoint), ascend, gain, tickTime);
    }

    /// <summary>左右の値の差だけ曲がる。触角で比べているだけで、方角は知らない。</summary>
    private void SteerByGradient(float left, float right, bool ascend, float gain, float tickTime)
    {
        float sum = left + right;
        if (sum <= 0f) return;

        float difference = (left - right) / sum;   // 左が濃いと正
        float turn = gain * difference * tickTime;
        if (!ascend) turn = -turn;

        // 速さを上げて1tickが長くなっても、回りすぎないようにする
        turn = Mathf.Clamp(turn, -90f, 90f);
        direction = Rotate(direction, turn);
    }

    /// <summary>向きにガウス雑音を足す（相関ランダムウォーク）。</summary>
    private void AddWanderNoise(float tickTime, float sigmaPerSecond)
    {
        if (sigmaPerSecond <= 0f) return;
        // 角度のばらつきは時間の平方根で増える
        float sigma = sigmaPerSecond * Mathf.Sqrt(Mathf.Max(0f, tickTime));
        direction = Rotate(direction, NextGaussian() * sigma);
    }

    /// <summary>感知範囲にある、いちばん近い餌。</summary>
    private FoodSource FindFoodInRange()
    {
        var foods = FoodSource.All;
        if (foods.Count == 0) return null;

        float range = settings.foodSenseRange;
        float rangeSq = range * range;
        FoodSource nearest = null;
        float nearestSq = float.MaxValue;

        for (int i = 0; i < foods.Count; i++)
        {
            FoodSource food = foods[i];
            if (food == null || food.Amount <= 0) continue;
            float distanceSq = (food.Position - position).sqrMagnitude;
            if (distanceSq > rangeSq || distanceSq >= nearestSq) continue;
            nearestSq = distanceSq;
            nearest = food;
        }
        return nearest;
    }

    // ============================================================
    // 移動層：固体に接している面に沿って歩く
    // ============================================================

    private float MoveStep(float deltaTime)
    {
        // 女王は歩くと決めたときだけ動く（行動モデル.md 13-1）。
        // 足場がないときは落ちてほしいので、支えられている間だけ止める
        if (IsQueen && queenWalkTimer <= 0f && IsSupported(position)) return 0f;

        float movedDistance = 0f;
        float remainingTime = deltaTime;
        int steps = 0;

        while (remainingTime > 0f && steps < maxSubsteps)
        {
            steps++;
            bool supported = IsSupported(position);
            isFalling = !supported;

            float speed = supported ? settings.walkSpeed : settings.fallSpeed;
            // 女王はゆっくり歩く
            if (IsQueen && supported && broodSettings != null) speed *= broodSettings.queenWalkSpeed;
            if (speed <= 0f) break;

            float stepTime = Mathf.Min(remainingTime, maxStepDistance / speed);
            remainingTime -= stepTime;
            float stepDistance = speed * stepTime;

            if (supported) movedDistance += WalkSubstep(stepDistance);
            else FallSubstep(stepDistance);
        }

        DepositTrailIfNeeded();
        return movedDistance;
    }

    /// <summary>接している面に沿って1歩進む。進めた距離を返す。</summary>
    private float WalkSubstep(float stepDistance)
    {
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

        direction = -direction;
        return 0f;
    }

    private void FallSubstep(float stepDistance)
    {
        Vector2 target = position + Vector2.down * stepDistance;
        if (IsPassableAt(target)) position = target;
    }

    /// <summary>餌を持って帰るあいだ、1マス通るごとに道しるべを置く。</summary>
    private void DepositTrailIfNeeded()
    {
        if (pheromones == null || grid == null) return;

        int x, y;
        if (!grid.WorldToCell(position, out x, out y))
        {
            lastCellIndex = -1;
            return;
        }

        int index = y * grid.Width + x;
        if (index == lastCellIndex) return;
        lastCellIndex = index;

        if (task != AntTask.ReturnWithFood) return;
        pheromones.DepositAt(x, y, PheromoneLayer.Trail, settings.depositTrail);
    }

    // ---- 地形の判定 ----

    private bool CanStandAt(Vector2 worldPosition)
    {
        int x, y;
        if (!grid.WorldToCell(worldPosition, out x, out y)) return false;
        if (!grid.IsPassable(x, y)) return false;
        if (!grid.HasSolidNeighbor(x, y)) return false;

        // 子どもを運んでいるあいだは巣から出ない（行動モデル.md 13-6）
        if (carriedBrood != null && nestField != null
            && nestField.Sample(worldPosition) < settings.nestInside) return false;

        // 女王は巣の奥から出ない（行動モデル.md 13-1）
        if (IsQueen && nestField != null)
        {
            float minNest = broodSettings != null ? broodSettings.queenMinNest : settings.nestInside;
            if (nestField.Sample(worldPosition) < minNest) return false;
        }

        return true;
    }

    private bool IsPassableAt(Vector2 worldPosition)
    {
        int x, y;
        if (!grid.WorldToCell(worldPosition, out x, out y)) return false;
        return grid.IsPassable(x, y);
    }

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
        float turnSpeed = settings != null ? settings.turnSpeed : 540f;
        float nextAngle = deltaTime > 0f
            ? Mathf.MoveTowardsAngle(currentAngle, targetAngle, turnSpeed * deltaTime)
            : targetAngle;
        transform.rotation = Quaternion.Euler(0f, 0f, nextAngle);
    }

    /// <summary>好きな場所へ置き直す。</summary>
    public void Teleport(Vector2 worldPosition)
    {
        position = worldPosition;
        SnapToNearestWalkable();
        lastCellIndex = -1;
        ApplyTransform(0f);
    }

    /// <summary>社会胃を減らす（女王が産卵で使う）。</summary>
    public void ConsumeCrop(float amount)
    {
        crop = Mathf.Clamp01(crop - Mathf.Max(0f, amount));
    }

    /// <summary>社会胃の初期値を決める（羽化した個体に使う）。</summary>
    public void SetStartCrop(float value)
    {
        crop = Mathf.Clamp01(value);
        cropOverridden = true;   // Start のランダム初期化で上書きさせない
    }

    /// <summary>生まれてからの日数を決める（出すときに個体ごとにばらつかせる）。</summary>
    public void SetStartAge(float days)
    {
        startAgeDays = days;
        bornAtElapsedDays = clock != null ? clock.ElapsedDays : 0.0;
    }

    /// <summary>仕事を外から決める（デバッグや段階3cの判定で使う）。</summary>
    public void SetTask(AntTask newTask)
    {
        task = newTask;
        if (newTask == AntTask.Explore) timeOutside = 0f;
    }

    private static Vector2 Rotate(Vector2 v, float degrees)
    {
        float rad = degrees * Mathf.Deg2Rad;
        float cos = Mathf.Cos(rad);
        float sin = Mathf.Sin(rad);
        return new Vector2(v.x * cos - v.y * sin, v.x * sin + v.y * cos);
    }

    /// <summary>平均0・標準偏差1の乱数（ボックス＝ミュラー法）。</summary>
    private static float NextGaussian()
    {
        float u1 = Mathf.Max(0.0001f, Random.value);
        float u2 = Random.value;
        return Mathf.Sqrt(-2f * Mathf.Log(u1)) * Mathf.Cos(2f * Mathf.PI * u2);
    }
}
