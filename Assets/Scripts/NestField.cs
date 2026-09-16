using UnityEngine;

/// <summary>
/// 巣の匂い（行動モデル.md 3章）。
/// 入口（空洞と空気の境目）を基準に、空洞の中は奥ほど濃く、
/// 地表の空気は入口から離れるほど薄くなる。土の中は伝わらない。
/// 地形が変わったときだけ作り直す。
/// </summary>
[DisallowMultipleComponent]
public class NestField : MonoBehaviour
{
    [SerializeField] private SoilGrid grid;
    [SerializeField] private AntSettings settings;

    /// <summary>地表の減衰で「ほぼゼロ」とみなす割合。</summary>
    private const float NearZero = 0.02f;

    private float[] values;
    private int[] queue;
    private int[] distances;
    private bool[] visited;

    private int width, height;
    private int terrainVersion = -1;
    private float rebuildTimer;
    private bool built;

    private Vector2 entranceWorld;
    private bool hasEntrance;

    /// <summary>巣の入口（いちばん高い開口）のワールド座標。</summary>
    public Vector2 EntranceWorld => entranceWorld;
    /// <summary>入口が見つかっているか。</summary>
    public bool HasEntrance => hasEntrance;
    /// <summary>層の中身（デバッグ表示用。書き換えないこと）。</summary>
    public float[] Values => values;

    private void Awake()
    {
        // ここでは参照を拾うだけ。地形がまだ作られていない場合があるので計算はしない
        if (grid == null) grid = GetComponent<SoilGrid>();
        if (grid == null) grid = FindFirstObjectByType<SoilGrid>();
    }

    private void Start()
    {
        EnsureBuilt();
    }

    /// <summary>
    /// まだ作っていなければ作る。
    /// 他のコンポーネント（餌を出す係など）から先に使われても平気なように、公開しておく。
    /// </summary>
    public void EnsureBuilt()
    {
        if (grid == null) return;
        grid.EnsureGenerated();
        if (values == null) Allocate();
        if (!built) Rebuild();
    }

    private void Allocate()
    {
        if (grid == null) return;
        width = grid.Width;
        height = grid.Height;
        int count = width * height;
        values = new float[count];
        queue = new int[count];
        distances = new int[count];
        visited = new bool[count];
    }

    private void Update()
    {
        if (grid == null || values == null) return;
        if (grid.Version == terrainVersion) return;

        // 掘削が続いても重くならないよう、作り直す間隔をあける
        rebuildTimer -= Time.unscaledDeltaTime;
        if (rebuildTimer > 0f) return;
        rebuildTimer = settings != null ? settings.nestRebuildInterval : 0.5f;
        Rebuild();
    }

    /// <summary>巣の匂いを作り直す。</summary>
    public void Rebuild()
    {
        if (grid == null) return;
        grid.EnsureGenerated();
        if (values == null) Allocate();
        if (values == null) return;
        terrainVersion = grid.Version;
        built = true;

        System.Array.Clear(values, 0, values.Length);
        hasEntrance = false;

        float entranceValue = settings != null ? settings.nestEntranceValue : 0.8f;
        float deepValue = settings != null ? settings.nestDeepValue : 1f;
        float deepDistanceCells = (settings != null ? settings.nestDeepDistance : 10f) / grid.CellSize;
        float falloffCells = (settings != null ? settings.nestSurfaceFalloff : 15f) / grid.CellSize;
        // 入口から falloff の距離で NearZero になるような減衰の長さ
        float lambda = falloffCells / Mathf.Log(1f / NearZero);
        if (lambda <= 0f) lambda = 1f;

        // 空洞と空気の境目を探す
        int cavitySeedCount = 0;
        int airSeedCount = 0;
        int bestEntranceIndex = -1;

        System.Array.Clear(visited, 0, visited.Length);
        int head = 0, tail = 0;

        for (int y = 0; y < height; y++)
        {
            int row = y * width;
            for (int x = 0; x < width; x++)
            {
                if (grid.GetCell(x, y) != CellType.Cavity) continue;
                if (!HasAirNeighbor(x, y)) continue;

                int i = row + x;
                visited[i] = true;
                distances[i] = 0;
                queue[tail++] = i;
                cavitySeedCount++;
            }
        }

        // 空洞の中：奥ほど濃くする
        while (head < tail)
        {
            int index = queue[head++];
            int x = index % width;
            int y = index / width;
            int distance = distances[index];

            float t = deepDistanceCells > 0f ? Mathf.Clamp01(distance / deepDistanceCells) : 1f;
            values[index] = Mathf.Lerp(entranceValue, deepValue, t);

            PushCavityNeighbors(x, y, distance, ref tail);
        }

        // 空気：入口から離れるほど薄くする
        System.Array.Clear(visited, 0, visited.Length);
        head = 0; tail = 0;

        for (int y = 0; y < height; y++)
        {
            int row = y * width;
            for (int x = 0; x < width; x++)
            {
                if (grid.GetCell(x, y) != CellType.Air) continue;
                if (!HasCavityNeighbor(x, y)) continue;

                int i = row + x;
                visited[i] = true;
                distances[i] = 0;
                queue[tail++] = i;
                airSeedCount++;

                // いちばん高い開口を入口とみなす
                if (bestEntranceIndex < 0 || y > bestEntranceIndex / width) bestEntranceIndex = i;
            }
        }

        while (head < tail)
        {
            int index = queue[head++];
            int x = index % width;
            int y = index / width;
            int distance = distances[index];

            values[index] = entranceValue * Mathf.Exp(-distance / lambda);

            PushAirNeighbors(x, y, distance, ref tail);
        }

        if (bestEntranceIndex >= 0)
        {
            hasEntrance = true;
            entranceWorld = grid.CellToWorld(bestEntranceIndex % width, bestEntranceIndex / width);
        }

        if (cavitySeedCount == 0 && airSeedCount == 0)
        {
            // 空洞が空気につながっていない（まだ巣がない）
            hasEntrance = false;
        }
    }

    private bool HasAirNeighbor(int x, int y)
    {
        return grid.GetCell(x + 1, y) == CellType.Air
            || grid.GetCell(x - 1, y) == CellType.Air
            || grid.GetCell(x, y + 1) == CellType.Air
            || grid.GetCell(x, y - 1) == CellType.Air;
    }

    private bool HasCavityNeighbor(int x, int y)
    {
        return grid.GetCell(x + 1, y) == CellType.Cavity
            || grid.GetCell(x - 1, y) == CellType.Cavity
            || grid.GetCell(x, y + 1) == CellType.Cavity
            || grid.GetCell(x, y - 1) == CellType.Cavity;
    }

    private void PushCavityNeighbors(int x, int y, int distance, ref int tail)
    {
        TryPush(x + 1, y, distance, CellType.Cavity, ref tail);
        TryPush(x - 1, y, distance, CellType.Cavity, ref tail);
        TryPush(x, y + 1, distance, CellType.Cavity, ref tail);
        TryPush(x, y - 1, distance, CellType.Cavity, ref tail);
    }

    private void PushAirNeighbors(int x, int y, int distance, ref int tail)
    {
        TryPush(x + 1, y, distance, CellType.Air, ref tail);
        TryPush(x - 1, y, distance, CellType.Air, ref tail);
        TryPush(x, y + 1, distance, CellType.Air, ref tail);
        TryPush(x, y - 1, distance, CellType.Air, ref tail);
    }

    private void TryPush(int x, int y, int distance, CellType type, ref int tail)
    {
        if (!grid.IsInside(x, y)) return;
        int i = y * width + x;
        if (visited[i]) return;
        if (grid.GetCell(x, y) != type) return;
        visited[i] = true;
        distances[i] = distance + 1;
        queue[tail++] = i;
    }

    /// <summary>その場所の巣の匂いの濃さ。</summary>
    public float Sample(Vector2 worldPosition)
    {
        if (values == null) return 0f;
        int x, y;
        if (!grid.WorldToCell(worldPosition, out x, out y)) return 0f;
        return values[y * width + x];
    }

    /// <summary>マスを指定して読む。</summary>
    public float GetAt(int x, int y)
    {
        if (values == null || !grid.IsInside(x, y)) return 0f;
        return values[y * width + x];
    }
}
