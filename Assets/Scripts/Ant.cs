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
    private float crop = 1f;
    /// <summary>仲間へ配るために持ち帰っている分</summary>
    private float carryLoad;
    /// <summary>Explore の閾値（腰の重さ）。学習で動く</summary>
    private float theta = 0.5f;
    private float metabolismTimer;
    private float movedSinceMetabolism;
    private float starveSeconds;
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
    /// <summary>空腹度（0〜1）。社会胃の裏返しで、別には持たない。</summary>
    public float Hunger => 1f - crop;
    /// <summary>仲間へ配るために持ち帰っている分。</summary>
    public float CarryLoad => carryLoad;
    /// <summary>Explore の閾値。低いほど外へ出やすい。</summary>
    public float ExploreThreshold => theta;
    /// <summary>巣の中にいるか（巣の匂いの濃さで決まる）。</summary>
    public bool IsInNest => isInNest;
    /// <summary>今、仲間と口移しの最中か。</summary>
    public bool IsSharing => sharePartner != null;
    /// <summary>興奮度（0〜1）。</summary>
    public float Alarm => alarm;
    /// <summary>外勤寄りの度合い。閾値の裏返しで、情報パネルのゲージに出す。</summary>
    public float OutdoorTendency => 1f - theta;
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

            if (alarm > settings.moodAlarmThreshold) return AntMood.Alarmed;
            if (Hunger > settings.moodHungryThreshold) return AntMood.Hungry;
            if (trailLostTimer > 0f) return AntMood.TrailLost;
            if (task == AntTask.ReturnWithFood) return AntMood.CarryingFood;
            // 巣の外にいて、巣の匂いがほとんど届いていない
            if (!isInNest && nestHere < settings.nestFar) return AntMood.FarFromNest;
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
        if (grid == null) grid = FindFirstObjectByType<SoilGrid>();
        if (view == null) view = GetComponent<AntView>();
        if (pheromones == null) pheromones = FindFirstObjectByType<PheromoneField>();
        if (nestField == null) nestField = FindFirstObjectByType<NestField>();
        if (colony == null) colony = FindFirstObjectByType<Colony>();
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
            crop = Random.Range(settings.startCropMin, settings.startCropMax);

            // 若いほど閾値が高い＝外へ出にくい。これが齢間分業になる
            theta = Mathf.Clamp(1f - AgeDays / Mathf.Max(0.0001f, settings.matureDays), 0.15f, 0.95f);
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

        // 口移しの最中は、両方とも止まって触角を震わせる
        float moved = 0f;
        if (sharePartner != null) ShareStep(deltaTime);
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
            default:
                task = AntTask.RestInNest;
                break;
        }
    }

    /// <summary>巣の中で休む。奥（匂いの濃いほう）へゆっくり寄りながらうろつく。</summary>
    private void RestInNestStep(float tickTime, bool inside)
    {
        timeOutside = 0f;
        // 交換中は動かない
        if (sharePartner != null) return;

        // 触れられる距離に仲間がいて、持ち分に差があれば口移しが始まる
        if (TryStartSharing()) return;

        // 空腹なら、いちばん近い仲間へ寄っていく（相手の中身は知らない。触れて差があれば流れる）
        if (Hunger > settings.begThreshold && colony != null)
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

    // ---- 口移し（行動モデル.md 4章）----

    /// <summary>触れられる距離の仲間と、持ち分に差があれば口移しを始める。</summary>
    private bool TryStartSharing()
    {
        if (colony == null) return false;

        Ant other = colony.FindNearestNestmate(this, settings.contactRange);
        if (other == null || other.sharePartner != null || !other.isInNest) return false;

        // 「自分の持ち分」と「相手の社会胃」を比べて、多いほうが渡す側になる
        float myTotal = crop + carryLoad;
        float otherTotal = other.crop + other.carryLoad;

        if (myTotal - other.crop >= settings.shareThreshold)
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
        starveSeconds = 0f;
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

        crop = Mathf.Clamp01(crop - drainPerSecond * elapsed * (moving ? settings.movingMetabolism : 1f));

        if (crop > 0f)
        {
            starveSeconds = 0f;
            return;
        }

        // 空っぽのまま一定の日数が過ぎたら死ぬ（死体の扱いは段階6）
        starveSeconds += elapsed;
        if (starveSeconds >= settings.starveDays * secondsPerDay) Destroy(gameObject);
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

        if (task != AntTask.RestInNest) return;
        if (sharePartner != null) return;   // 分け合っている最中は出発しない

        float stimulus = colony != null ? colony.ForageStimulus : 0f;
        float probability = stimulus > 0f
            ? (stimulus * stimulus) / (stimulus * stimulus + theta * theta)
            : 0f;

        if (Random.value < probability)
        {
            // 外へ出た。次はもう少し出やすくなる
            task = AntTask.Explore;
            timeOutside = 0f;
            targetFood = null;
            theta = Mathf.Clamp(theta - settings.thetaLearn, settings.thetaMin, settings.thetaMax);
        }
        else
        {
            // 出なかった。少しずつ腰が重くなる
            theta = Mathf.Clamp(theta + settings.thetaForget, settings.thetaMin, settings.thetaMax);
        }
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
        starveSeconds = 0f;
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
        float movedDistance = 0f;
        float remainingTime = deltaTime;
        int steps = 0;

        while (remainingTime > 0f && steps < maxSubsteps)
        {
            steps++;
            bool supported = IsSupported(position);
            isFalling = !supported;

            float speed = supported ? settings.walkSpeed : settings.fallSpeed;
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
        return grid.HasSolidNeighbor(x, y);
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
