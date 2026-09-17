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
    /// <summary>女王（いなければ null）。部屋の判定で毎回探さずに済むよう控えておく。</summary>
    public Ant Queen { get; private set; }
    /// <summary>働きアリの社会胃の平均。飢えているときは掘らせない（行動モデル.md 12-1）。</summary>
    public float WorkerCropAverage { get; private set; }
    /// <summary>縦穴の目標の深さ（cm）。総個体数で伸びる。</summary>
    public float ShaftTargetDepth { get; private set; }

    /// <summary>
    /// 「この深さに、この広さの部屋がほしい」という注文（行動モデル.md 12-2）。
    /// 深さ・必要な広さ・縦穴のどちら側に出すか、の3つで1つの部屋を表す。
    /// </summary>
    private struct RoomRequest
    {
        public float depthCm;
        /// <summary>中身に見合う空洞のマス数</summary>
        public int wantCells;
        /// <summary>縦穴のどちら側に出すか（+1＝右、-1＝左）</summary>
        public int side;
    }

    private readonly System.Collections.Generic.List<RoomRequest> roomRequests
        = new System.Collections.Generic.List<RoomRequest>();

    /// <summary>その深さに部屋がほしい、と伝える。近い注文はまとめる。</summary>
    private void RequestRoom(float depthCm, int wantCells, int side)
    {
        for (int i = 0; i < roomRequests.Count; i++)
        {
            if (Mathf.Abs(roomRequests[i].depthCm - depthCm) < 2f) return;
        }
        if (roomRequests.Count >= 6) roomRequests.RemoveAt(0);
        roomRequests.Add(new RoomRequest { depthCm = depthCm, wantCells = wantCells, side = side });
    }

    /// <summary>注文の数。</summary>
    public int RoomRequestCount => roomRequests.Count;

    /// <summary>
    /// 深さごとに一度決めた「どちら側に出すか」の控え。
    /// 注文の一覧は数秒ごとに作り直すので、これがないと掘りかけの部屋が
    /// 途中で反対側に移ってしまう。
    /// </summary>
    private struct RoomSideMemo
    {
        public float depthCm;
        public int side;
    }

    private readonly System.Collections.Generic.List<RoomSideMemo> roomSides
        = new System.Collections.Generic.List<RoomSideMemo>();

    /// <summary>次に新しい部屋を出す向き（注文ごとに交互）</summary>
    private int nextRoomSide = 1;

    /// <summary>いま欲求を置いている掘削ポイントの数（確認用）。</summary>
    public int DigSiteCount { get; private set; }

    /// <summary>掘削欲求を置き直すまでの残り時間</summary>
    private float desireTimer;

    /// <summary>
    /// 「必要なもの」から掘削欲求の場を作る（行動モデル.md 12-2）。
    ///
    /// コロニーは一覧（縦穴が浅い／この深さに部屋がほしい）だけを持ち、
    /// それを場に翻訳する。アリは場しか見ない。設計図は誰も持たない。
    /// 置き直しで保つので、必要がなくなれば場は数秒で消える。
    /// </summary>
    private void UpdateDigDesire(float deltaTime)
    {
        if (pheromones == null || grid == null || nestField == null || settings == null) return;

        desireTimer -= deltaTime;
        if (desireTimer > 0f) return;
        desireTimer = settings.perceptionInterval;

        DigSiteCount = 0;
        desireSeeds.Clear();
        int maxSites = Mathf.Max(1, settings.maxDigSites);

        // 1．縦穴：最深の真下と斜め下に置く
        if (nestField.HasDeepestCell && nestField.MaxCavityDepthCm < ShaftTargetDepth)
        {
            int x = nestField.DeepestCellX;
            int y = nestField.DeepestCellY;
            PlaceDesire(x, y - 1, settings.desireShaftDown);
            PlaceDesire(x - 1, y - 1, settings.desireShaftDiagonal);
            PlaceDesire(x + 1, y - 1, settings.desireShaftDiagonal);
            DigSiteCount++;
        }

        // 2．部屋の注文：その深さの長方形の中の土にだけ置く
        for (int i = 0; i < roomRequests.Count && DigSiteCount < maxSites; i++)
        {
            // 置けなければ、縦穴がまだその深さに届いていない。
            // 注文はそのまま残り、ShaftTargetDepth を深くして縦穴を呼び寄せる
            if (PlaceRoomDesire(roomRequests[i])) DigSiteCount++;
        }

        // 掘り場までの道しるべになる「導き」を、空洞をたどって置く
        SpreadDesireGuide();
    }

    /// <summary>部屋を探すときに縦穴の左右何マスまで見るか。</summary>
    private int RoomScanRange()
    {
        return Mathf.CeilToInt(settings.roomRadius / grid.CellSize) * 4;
    }

    /// <summary>掘削欲求を置き直すまでの残り時間（必要なものの一覧のほう）</summary>
    private float roomNeedTimer;

    /// <summary>
    /// 「この深さに部屋がほしい」を、子どもと休む場所から出す（行動モデル.md 12-2）。
    ///
    /// 「その深さに空洞がない」ではなく「その深さの空洞が中身に見合う広さに足りない」で測る。
    /// 縦穴はすべての深さを貫くので、「空洞がない」は決して真にならないため。
    /// </summary>
    private void UpdateRoomNeeds(float deltaTime)
    {
        if (grid == null || nestField == null || settings == null) return;

        roomNeedTimer -= deltaTime;
        if (roomNeedTimer > 0f) return;
        roomNeedTimer = Mathf.Max(0.5f, settings.roomNeedInterval);

        if (broodField == null) broodField = FindFirstObjectByType<BroodField>();
        var broodSettings = broodField != null ? broodField.Settings : null;

        // 見たい深さ：子ども3段階＋休む場所2か所＋女王の部屋
        float[] depths = new float[6];
        int[] want = new int[6];

        if (broodField != null && broodSettings != null)
        {
            depths[0] = broodSettings.preferredDepthEgg;
            depths[1] = broodSettings.preferredDepthLarva;
            depths[2] = broodSettings.preferredDepthPupa;

            int[] counts = new int[3];
            for (int i = 0; i < broodField.All.Count; i++)
            {
                float d = broodSettings.PreferredDepth(broodField.All[i]);
                int best = 0;
                for (int k = 1; k < 3; k++)
                {
                    if (Mathf.Abs(depths[k] - d) < Mathf.Abs(depths[best] - d)) best = k;
                }
                counts[best]++;
            }

            for (int k = 0; k < 3; k++)
            {
                // 少ししかいない段階のために部屋は掘らない
                if (counts[k] < broodSettings.roomBroodMin) continue;
                want[k] = Mathf.RoundToInt(counts[k] * settings.roomCellsPerBrood) + settings.roomBaseCells;
            }
        }

        depths[3] = settings.restDepthLowCm;
        depths[4] = settings.restDepthHighCm;

        // 休む場所は、巣にいるアリが浅い側と深い側に半分ずつ分かれる想定
        int resting = Mathf.Max(1, AntsInNest / 2);
        want[3] = Mathf.RoundToInt(resting * settings.roomCellsPerAnt);
        want[4] = want[3];

        // 女王の部屋も同じ形の注文にする
        if (Queen != null && grid != null)
        {
            int qx, qy;
            if (grid.WorldToCell(Queen.Position, out qx, out qy))
            {
                depths[5] = nestField.GetDepthCm(qx, qy);
                want[5] = settings.roomQueenCells;
            }
        }

        // 一覧は毎回ここで作り直す。足りている深さは並ばないので、注文は自然に消える
        roomRequests.Clear();
        for (int k = 0; k < depths.Length; k++)
        {
            if (want[k] <= 0) continue;

            int rowY;
            if (!FindRoomAnchorRow(depths[k], out rowY))
            {
                // 縦穴がまだその深さに届いていない。広さは測れないが、部屋は要る。
                // 注文を残すことで ShaftTargetDepth が深くなり、縦穴が下りてくる
                RequestRoom(depths[k], want[k], nextRoomSide);
                continue;
            }

            // 広さは、その注文の長方形の中の空洞で測る（12-2）
            int side = GetRoomSide(depths[k], rowY);
            var probe = new RoomRequest { depthCm = depths[k], wantCells = want[k], side = side };

            RoomRect rect;
            if (!TryGetRoomRect(probe, out rect)) continue;
            if (CountCavityInRect(rect) >= want[k]) continue;

            RequestRoom(depths[k], want[k], side);
        }
    }

    /// <summary>
    /// 部屋の注文に対応する長方形（行動モデル.md 12-2）。
    /// 縦穴の空洞のすぐ外の土を起点に、高さ roomHeightCells、
    /// 幅＝必要な広さ÷高さ で横に伸びる。部屋はこの中だけで掘られる。
    /// </summary>
    private struct RoomRect
    {
        public int minX, maxX, minY, maxY;
    }

    /// <summary>注文の長方形を求める。縦穴がその深さに届いていなければ false。</summary>
    private bool TryGetRoomRect(RoomRequest request, out RoomRect rect)
    {
        rect = new RoomRect();

        int rowY;
        if (!FindRoomAnchorRow(request.depthCm, out rowY)) return false;

        int wallX;
        if (!FindShaftWall(rowY, request.side, out wallX)) return false;

        int height = Mathf.Max(1, settings.roomHeightCells);
        int width = Mathf.Max(1, Mathf.CeilToInt((float)request.wantCells / height));

        // 高さは注文の深さの行を挟むように取る
        rect.minY = rowY - height / 2;
        rect.maxY = rect.minY + height - 1;

        int farX = wallX + request.side * (width - 1);
        rect.minX = Mathf.Min(wallX, farX);
        rect.maxX = Mathf.Max(wallX, farX);
        return true;
    }

    /// <summary>その深さに当たる縦穴の行。空洞がなければ false。</summary>
    private bool FindRoomAnchorRow(float depthCm, out int rowY)
    {
        rowY = -1;
        int shaftX = nestField.DeepestCellX;
        if (shaftX < 0) return false;

        float bestGap = float.MaxValue;
        int range = RoomScanRange();

        for (int y = grid.SurfaceRow - 1; y >= 1; y--)
        {
            float gap = Mathf.Abs(nestField.GetDepthCm(shaftX, y) - depthCm);
            if (gap >= bestGap) continue;

            // その行に縦穴（空洞）があることが条件
            bool hasCavity = false;
            for (int dx = -range; dx <= range && !hasCavity; dx++)
            {
                int x = shaftX + dx;
                if (grid.IsInside(x, y) && grid.GetCell(x, y) == CellType.Cavity) hasCavity = true;
            }
            if (!hasCavity) continue;

            bestGap = gap;
            rowY = y;
        }

        // 深さが合う行が近くにないなら、まだ縦穴が届いていない
        return rowY >= 0 && bestGap <= 1f;
    }

    /// <summary>
    /// その行で、縦穴の空洞の side 側のすぐ外にある土の列。
    /// ここが部屋の入口になる。長方形の外＝縦穴の壁には部屋の欲求を置かないので、
    /// 縦穴そのものは太らない。
    /// </summary>
    private bool FindShaftWall(int rowY, int side, out int wallX)
    {
        wallX = 0;
        int shaftX = nestField.DeepestCellX;
        int range = RoomScanRange();

        // 縦穴の中心にいちばん近い空洞を探す
        int nearest = int.MinValue;
        for (int dx = 0; dx <= range; dx++)
        {
            if (grid.IsInside(shaftX + dx, rowY) && grid.GetCell(shaftX + dx, rowY) == CellType.Cavity)
            {
                nearest = shaftX + dx;
                break;
            }
            if (grid.IsInside(shaftX - dx, rowY) && grid.GetCell(shaftX - dx, rowY) == CellType.Cavity)
            {
                nearest = shaftX - dx;
                break;
            }
        }
        if (nearest == int.MinValue) return false;

        // そこから side 側へ、空洞が続くあいだ進む
        int x2 = nearest;
        while (grid.IsInside(x2 + side, rowY) && grid.GetCell(x2 + side, rowY) == CellType.Cavity)
        {
            x2 += side;
        }

        wallX = x2 + side;
        return grid.IsInside(wallX, rowY);
    }

    /// <summary>長方形の中の空洞マス数。部屋の広さはこれで測る。</summary>
    private int CountCavityInRect(RoomRect rect)
    {
        int count = 0;
        for (int y = rect.minY; y <= rect.maxY; y++)
        {
            for (int x = rect.minX; x <= rect.maxX; x++)
            {
                if (grid.IsInside(x, y) && grid.GetCell(x, y) == CellType.Cavity) count++;
            }
        }
        return count;
    }

    /// <summary>
    /// 注文の長方形の中の土に、部屋の欲求を置く（12-2）。
    /// 上下には広げない。長方形の外には置かないので、縦穴は太らない。
    /// </summary>
    private bool PlaceRoomDesire(RoomRequest request)
    {
        RoomRect rect;
        if (!TryGetRoomRect(request, out rect)) return false;

        bool placed = false;
        for (int y = rect.minY; y <= rect.maxY; y++)
        {
            for (int x = rect.minX; x <= rect.maxX; x++)
            {
                if (!grid.IsInside(x, y)) continue;
                if (grid.GetCell(x, y) != CellType.Soil) continue;

                PlaceDesire(x, y, settings.desireRoomSide);
                placed = true;
            }
        }
        return placed;
    }

    /// <summary>
    /// その深さの部屋をどちら側に出すか（12-2）。
    /// 注文ごとに交互。すでに部屋がある側が当たったら反対側にする。
    /// 一度決めたら、その深さの注文が続くあいだは変えない。
    /// </summary>
    private int GetRoomSide(float depthCm, int rowY)
    {
        for (int i = 0; i < roomSides.Count; i++)
        {
            if (Mathf.Abs(roomSides[i].depthCm - depthCm) < 1f) return roomSides[i].side;
        }

        int side = nextRoomSide;
        if (HasRoomOnSide(rowY, side) && !HasRoomOnSide(rowY, -side)) side = -side;

        nextRoomSide = -side;
        if (roomSides.Count >= 8) roomSides.RemoveAt(0);
        roomSides.Add(new RoomSideMemo { depthCm = depthCm, side = side });
        return side;
    }

    /// <summary>その行の side 側に、もう部屋があるか（縦穴の外に空洞が広がっているか）。</summary>
    private bool HasRoomOnSide(int rowY, int side)
    {
        int wallX;
        if (!FindShaftWall(rowY, side, out wallX)) return false;

        int height = Mathf.Max(1, settings.roomHeightCells);
        int top = rowY - height / 2;
        int open = 0;
        for (int y = top; y <= top + height - 1; y++)
        {
            for (int d = 0; d < height; d++)
            {
                int x = wallX + side * d;
                if (grid.IsInside(x, y) && grid.GetCell(x, y) == CellType.Cavity) open++;
            }
        }
        return open >= 3;
    }

    /// <summary>土なら欲求を置く（石には置かない。石の隣に残るので自然に回り込む）。</summary>
    private void PlaceDesire(int x, int y, float amount)
    {
        if (!grid.IsInside(x, y)) return;
        if (grid.GetCell(x, y) != CellType.Soil) return;

        // 地表の近くは掘らせない（掘り抜け防止。13-15）
        int keep = Mathf.RoundToInt(settings.surfaceKeepDepth / grid.CellSize);
        if (grid.DepthFromSurface(x, y) < keep) return;

        // 加算ではなく置き直し。加算だと毎tickで上限に張り付き、勾配が消える（12-2）
        pheromones.SetAtLeast(x, y, PheromoneLayer.DigDesire, amount * settings.digDesireMax);
        desireSeeds.Add(y * grid.Width + x);
    }

    /// <summary>今回欲求を置いた土のマス（導きを広げる起点）</summary>
    private readonly System.Collections.Generic.List<int> desireSeeds
        = new System.Collections.Generic.List<int>();

    /// <summary>導きを広げるときの作業用（同じマスを二度通らないための印）</summary>
    private int[] guideStamp;
    private int guideStampValue;
    private readonly System.Collections.Generic.Queue<int> guideQueue
        = new System.Collections.Generic.Queue<int>();
    private readonly System.Collections.Generic.Queue<float> guideValue
        = new System.Collections.Generic.Queue<float>();

    /// <summary>
    /// 掘りたい土から、巣の空洞をたどって「導き」の欲求を置く（行動モデル.md 12-2）。
    ///
    /// 欲求は土の上にしか乗らないので、そのままでは離れた場所のアリの触角に何も届かない。
    /// 掘りたい土のそばの空洞から順に、1マスたどるごとに薄めた値を置いていくと、
    /// 巣のどこにいても「濃いほうへ曲がる」だけで掘り場にたどり着ける。
    /// 広がるのは空洞の中だけで、拡散ではなく毎tickの置き直しで作る。
    /// </summary>
    private void SpreadDesireGuide()
    {
        if (desireSeeds.Count == 0) return;

        int count = grid.Width * grid.Height;
        if (guideStamp == null || guideStamp.Length != count) guideStamp = new int[count];
        guideStampValue++;
        guideQueue.Clear();
        guideValue.Clear();

        float falloff = Mathf.Clamp(settings.desireGuideFalloff, 0.5f, 0.99f);
        float start = settings.digDesireMax * falloff;

        // 掘りたい土に接している空洞が出発点
        for (int i = 0; i < desireSeeds.Count; i++)
        {
            int si = desireSeeds[i];
            EnqueueGuideNeighbors(si % grid.Width, si / grid.Width, start);
        }

        while (guideQueue.Count > 0)
        {
            int index = guideQueue.Dequeue();
            float value = guideValue.Dequeue();
            if (value < 0.01f) continue;
            EnqueueGuideNeighbors(index % grid.Width, index / grid.Width, value * falloff);
        }
    }

    /// <summary>まだ通っていない隣の空洞に導きを置いて、続きの行き先に加える。</summary>
    private void EnqueueGuideNeighbors(int x, int y, float value)
    {
        if (value < 0.01f) return;

        for (int k = 0; k < 4; k++)
        {
            int nx = x + (k == 0 ? 1 : (k == 1 ? -1 : 0));
            int ny = y + (k == 2 ? 1 : (k == 3 ? -1 : 0));
            if (!grid.IsInside(nx, ny)) continue;
            if (!grid.IsPassable(nx, ny)) continue;

            int ni = ny * grid.Width + nx;
            if (guideStamp[ni] == guideStampValue) continue;
            guideStamp[ni] = guideStampValue;

            pheromones.SetAtLeast(nx, ny, PheromoneLayer.DigDesire, value);
            guideQueue.Enqueue(ni);
            guideValue.Enqueue(value);
        }
    }

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

        UpdateRoomNeeds(deltaTime);
        UpdateDigDesire(deltaTime);

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

        if (Queen == null)
        {
            for (int i = 0; i < ants.Count; i++)
            {
                if (ants[i] != null && ants[i].IsQueen) { Queen = ants[i]; break; }
            }
        }

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

        int workers = 0;
        float cropSum = 0f;
        for (int i = 0; i < ants.Count; i++)
        {
            if (ants[i] == null || ants[i].IsQueen) continue;
            workers++;
            cropSum += ants[i].Crop;
        }
        WorkerCropAverage = workers > 0 ? cropSum / workers : 0f;

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

        // 縦穴の目標の深さ。個体数が増えれば深くなる（12-2）
        if (settings != null)
        {
            float need = settings.shaftBaseDepth + settings.shaftDepthPerAnt * totalAnts;
            // 部屋の注文が今の縦穴より深ければ、そこまで縦穴を伸ばす。
            // 壁がなければ部屋は掘れないので、注文は縦穴を深くする理由にもなる（12-2）
            for (int i = 0; i < roomRequests.Count; i++)
            {
                need = Mathf.Max(need, roomRequests[i].depthCm + settings.shaftRoomMargin);
            }
            ShaftTargetDepth = need;
        }

        // 住む場所が足りないうちは、飢えていても掘る（12-1）
        bool tooCramped = settings != null && CavityCells < totalAnts * settings.digEmergencyCells;

        // 飢えているときは掘らない。掘削は重い仕事で、その間は採餌ができない（12-1）
        if (!tooCramped && settings != null && WorkerCropAverage < settings.digMinCrop)
        {
            DigStimulus = 0f;
            TargetCavityCells = Mathf.RoundToInt(totalAnts * (settings != null ? settings.targetCellsPerAnt : 8f));
            return;
        }

        // 掘る理由は2つある（行動モデル.md 12-1）。
        //  (a) 住む場所の広さが足りない
        //  (b) コロニーが「必要なもの」を抱えている（縦穴が浅い／この深さに部屋がほしい）
        // (b) を入れないと、広さが足りているだけで注文が残っていても誰も掘りに行かない
        float needStimulus = 0f;
        if (settings != null)
        {
            bool shaftTooShallow = nestField != null && nestField.MaxCavityDepthCm < ShaftTargetDepth;
            if (shaftTooShallow || roomRequests.Count > 0) needStimulus = settings.digNeedStimulus;
        }

        float cellsPerAnt = settings != null ? Mathf.Max(0.0001f, settings.targetCellsPerAnt) : 6f;

        // 子どもも場所を取る。幼虫の数 × broodSpaceWeight をアリの数に足して目標を決める（13-4）
        float broodMouths = 0f;
        var broodSettings = broodField != null ? broodField.Settings : null;
        if (broodField != null && broodSettings != null)
            broodMouths = broodField.LarvaCount * broodSettings.broodSpaceWeight;

        TargetCavityCells = Mathf.RoundToInt((totalAnts + broodMouths) * cellsPerAnt);

        if (TargetCavityCells <= 0 || CavityCells >= TargetCavityCells)
        {
            DigStimulus = needStimulus;
            return;
        }

        float range = settings != null ? Mathf.Clamp(settings.digShortfallRange, 0.05f, 1f) : 0.5f;
        float shortfall = TargetCavityCells - CavityCells;
        DigStimulus = Mathf.Max(needStimulus, Mathf.Clamp01(shortfall / (TargetCavityCells * range)));
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
    /// <summary>
    /// その場所の近くで休んでいるアリの数（行動モデル.md 13-15）。
    /// 休んでいるアリの集まりが「休憩所」になる。
    /// </summary>
    public int CountRestingNear(Vector2 position, float radius)
    {
        var ants = Ant.All;
        float radiusSq = radius * radius;
        int count = 0;
        for (int i = 0; i < ants.Count; i++)
        {
            Ant ant = ants[i];
            if (ant == null || ant.IsQueen) continue;
            if (ant.CurrentTask != AntTask.RestInNest) continue;
            if ((ant.Position - position).sqrMagnitude > radiusSq) continue;
            count++;
        }
        return count;
    }

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
