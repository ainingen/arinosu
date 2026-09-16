using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>マス1つの状態。</summary>
public enum CellType
{
    /// <summary>空気（地表より上）</summary>
    Air = 0,
    /// <summary>土（掘れる）</summary>
    Soil = 1,
    /// <summary>空洞（掘られた跡。アリが通れる）</summary>
    Cavity = 2,
    /// <summary>石（掘れない）</summary>
    Stone = 3,
    /// <summary>水</summary>
    Water = 4,
}

/// <summary>
/// 世界の土をマス目で持つ。左下が原点 (0,0) で、右が +X、上が +Y。
/// このクラスはデータだけを持ち、見た目は SoilGridRenderer が担当する。
/// </summary>
[DisallowMultipleComponent]
public class SoilGrid : MonoBehaviour
{
    [Header("グリッドの大きさ")]
    [Tooltip("横のマス数")]
    [SerializeField] private int width = 200;
    [Tooltip("縦のマス数（地表＋地中）")]
    [SerializeField] private int height = 250;
    [Tooltip("地表の高さ（マス）。この値より下が地中、以上が空気")]
    [SerializeField] private int surfaceRow = 200;
    [Tooltip("1マスの大きさ（Unity単位 ＝ cm）")]
    [SerializeField] private float cellSize = 0.2f;

    [Header("初期地形")]
    [Tooltip("同じ値なら毎回同じ地形になる")]
    [SerializeField] private int randomSeed = 12345;
    [Tooltip("ばらまく石のかたまりの数")]
    [SerializeField] private int stoneCount = 60;
    [SerializeField] private int stoneMinRadius = 1;
    [SerializeField] private int stoneMaxRadius = 3;

    [Header("最初の巣穴")]
    [Tooltip("入口の X 位置（マス）。負の値なら中央")]
    [SerializeField] private int nestEntranceX = -1;
    [Tooltip("縦穴の幅（マス）")]
    [SerializeField] private int nestTunnelWidth = 3;
    [Tooltip("縦穴の深さ（マス）")]
    [SerializeField] private int nestTunnelDepth = 25;
    [Tooltip("縦穴の先にある部屋の半径（マス）")]
    [SerializeField] private int nestChamberRadius = 5;

    /// <summary>マスの状態。index = y * width + x</summary>
    private CellType[] cells;

    /// <summary>前回の描画以降に変わったマスの index。</summary>
    private readonly List<int> dirtyCells = new List<int>();

    public int Width => width;
    public int Height => height;
    /// <summary>地表の高さ（マス）。これ以上の y は最初は空気。</summary>
    public int SurfaceRow => surfaceRow;
    public float CellSize => cellSize;

    /// <summary>世界の左下（ワールド座標）。</summary>
    public Vector2 WorldMin => transform.position;
    /// <summary>世界の右上（ワールド座標）。</summary>
    public Vector2 WorldMax => WorldMin + WorldSize;
    /// <summary>世界の大きさ（Unity単位）。</summary>
    public Vector2 WorldSize => new Vector2(width * cellSize, height * cellSize);

    /// <summary>変わったマスの一覧。描画側が読んだら ClearDirty() を呼ぶ。</summary>
    public IReadOnlyList<int> DirtyCells => dirtyCells;

    /// <summary>グリッド全体を作り直したときに呼ばれる。</summary>
    public event Action OnGridRebuilt;

    /// <summary>
    /// 地形が変わるたびに増える版番号。
    /// 「読んだら消す」変更リストと違い、見る人が何人いても取りこぼさない。
    /// 巣の匂いの作り直しなどは、自分が見た版と比べて判断する。
    /// </summary>
    public int Version => version;
    private int version;

    private void Awake()
    {
        EnsureGenerated();
    }

    /// <summary>
    /// まだ作られていなければ地形を作る。
    /// 他のコンポーネントの Awake がこちらより先に走っても困らないようにするため、
    /// マスを読む前に必ずここを通す。
    /// </summary>
    public void EnsureGenerated()
    {
        if (cells == null) Generate();
    }

    /// <summary>初期地形を作り直す。</summary>
    public void Generate()
    {
        cells = new CellType[width * height];

        // 地中は土、地表より上は空気
        for (int y = 0; y < height; y++)
        {
            CellType type = y < surfaceRow ? CellType.Soil : CellType.Air;
            int rowStart = y * width;
            for (int x = 0; x < width; x++)
            {
                cells[rowStart + x] = type;
            }
        }

        // 石をばらまく（土のマスだけ置き換える）
        System.Random rng = new System.Random(randomSeed);
        int stoneTop = Mathf.Max(1, surfaceRow);
        for (int i = 0; i < stoneCount; i++)
        {
            int cx = rng.Next(0, width);
            int cy = rng.Next(0, stoneTop);
            int r = rng.Next(stoneMinRadius, stoneMaxRadius + 1);
            FillCircle(cx, cy, r, CellType.Stone, CellType.Soil);
        }

        CarveStartingNest();

        dirtyCells.Clear();
        version++;
        OnGridRebuilt?.Invoke();
    }

    /// <summary>最初の巣穴（縦穴＋部屋）を1つ掘る。</summary>
    private void CarveStartingNest()
    {
        int entranceX = nestEntranceX >= 0 ? nestEntranceX : width / 2;
        int half = Mathf.Max(0, nestTunnelWidth / 2);
        int bottom = Mathf.Clamp(surfaceRow - nestTunnelDepth, 1, surfaceRow - 1);

        // 縦穴
        for (int y = bottom; y < surfaceRow; y++)
        {
            for (int x = entranceX - half; x <= entranceX + half; x++)
            {
                SetCellSilent(x, y, CellType.Cavity);
            }
        }

        // 縦穴の先の部屋
        FillCircle(entranceX, bottom, nestChamberRadius, CellType.Cavity, null);
    }

    /// <summary>円形にマスを塗る。replaceOnly を指定すると、その状態のマスだけを置き換える。</summary>
    private void FillCircle(int centerX, int centerY, int radius, CellType type, CellType? replaceOnly)
    {
        int rSq = radius * radius;
        for (int y = centerY - radius; y <= centerY + radius; y++)
        {
            for (int x = centerX - radius; x <= centerX + radius; x++)
            {
                int dx = x - centerX;
                int dy = y - centerY;
                if (dx * dx + dy * dy > rSq) continue;
                if (!IsInside(x, y)) continue;
                if (replaceOnly.HasValue && cells[y * width + x] != replaceOnly.Value) continue;
                SetCellSilent(x, y, type);
            }
        }
    }

    /// <summary>マスが世界の中にあるか。</summary>
    public bool IsInside(int x, int y)
    {
        return x >= 0 && x < width && y >= 0 && y < height;
    }

    /// <summary>マスの状態を取る。外側は Air を返す。</summary>
    public CellType GetCell(int x, int y)
    {
        if (!IsInside(x, y)) return CellType.Air;
        if (cells == null) EnsureGenerated();
        return cells[y * width + x];
    }

    /// <summary>マスの状態を変える。変わったマスは描画側へ伝わる。</summary>
    public void SetCell(int x, int y, CellType type)
    {
        if (!IsInside(x, y)) return;
        if (cells == null) EnsureGenerated();
        int index = y * width + x;
        if (cells[index] == type) return;
        cells[index] = type;
        dirtyCells.Add(index);
        version++;
    }

    /// <summary>描画側へ伝えずに書き換える（地形生成中に使う）。</summary>
    private void SetCellSilent(int x, int y, CellType type)
    {
        if (!IsInside(x, y)) return;
        cells[y * width + x] = type;
    }

    /// <summary>アリが通れるマスか（段階2以降で使う）。</summary>
    public bool IsPassable(int x, int y)
    {
        CellType type = GetCell(x, y);
        return type == CellType.Air || type == CellType.Cavity;
    }

    /// <summary>固体（アリが足をつけられるマス）か。世界の外は固体として扱う。</summary>
    public bool IsSolid(int x, int y)
    {
        // 地表より上の外側は固体にしない。
        // ここを固体にすると、世界の左右の端に沿ってアリが空へ登っていってしまう
        if (y >= surfaceRow && !IsInside(x, y)) return false;
        // 地中の左右と下の外側は固体扱い。観察キットのガラス壁だと思えばよい
        if (!IsInside(x, y)) return true;
        CellType type = GetCell(x, y);
        return type == CellType.Soil || type == CellType.Stone;
    }

    /// <summary>まわり8マスのどれかが固体か（アリが体を接していられるか）。</summary>
    public bool HasSolidNeighbor(int x, int y)
    {
        for (int dy = -1; dy <= 1; dy++)
        {
            for (int dx = -1; dx <= 1; dx++)
            {
                if (dx == 0 && dy == 0) continue;
                if (IsSolid(x + dx, y + dy)) return true;
            }
        }
        return false;
    }

    /// <summary>変更マスの一覧を空にする。描画側が描き終えたら呼ぶ。</summary>
    public void ClearDirty()
    {
        dirtyCells.Clear();
    }

    /// <summary>マスの中心のワールド座標。</summary>
    public Vector2 CellToWorld(int x, int y)
    {
        Vector2 origin = WorldMin;
        return new Vector2(origin.x + (x + 0.5f) * cellSize, origin.y + (y + 0.5f) * cellSize);
    }

    /// <summary>ワールド座標がどのマスかを求める。世界の外なら false。</summary>
    public bool WorldToCell(Vector2 worldPosition, out int x, out int y)
    {
        Vector2 local = worldPosition - WorldMin;
        x = Mathf.FloorToInt(local.x / cellSize);
        y = Mathf.FloorToInt(local.y / cellSize);
        return IsInside(x, y);
    }
}
