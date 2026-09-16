using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// SoilGrid を1枚のテクスチャとして描く。
/// 1マスを pixelsPerCell 四方のピクセルで塗り、変わったマスだけを塗り直す。
/// </summary>
[RequireComponent(typeof(SpriteRenderer))]
[DisallowMultipleComponent]
public class SoilGridRenderer : MonoBehaviour
{
    [Tooltip("描画するグリッド。未指定なら同じ GameObject から探す")]
    [SerializeField] private SoilGrid grid;

    [Tooltip("1マスを何ピクセルで描くか")]
    [SerializeField] private int pixelsPerCell = 4;

    [Header("マスの色")]
    [SerializeField] private Color airColor = new Color(0.64f, 0.79f, 0.92f, 1f);
    [SerializeField] private Color soilColor = new Color(0.42f, 0.29f, 0.19f, 1f);
    [SerializeField] private Color cavityColor = new Color(0.15f, 0.11f, 0.08f, 1f);
    [SerializeField] private Color stoneColor = new Color(0.55f, 0.54f, 0.52f, 1f);
    [SerializeField] private Color waterColor = new Color(0.24f, 0.45f, 0.75f, 1f);

    [Header("土・石の色のばらつき")]
    [Tooltip("0 にすると全部同じ色になる")]
    [SerializeField, Range(0f, 0.3f)] private float colorNoise = 0.05f;
    [SerializeField] private int colorNoiseSeed = 7777;

    private SpriteRenderer spriteRenderer;
    private Texture2D texture;
    /// <summary>1マス分の色を入れておく使い回しの配列。</summary>
    private Color32[] blockBuffer;

    private void Awake()
    {
        spriteRenderer = GetComponent<SpriteRenderer>();
        if (grid == null) grid = GetComponent<SoilGrid>();
    }

    private void OnEnable()
    {
        if (grid != null) grid.OnGridRebuilt += RedrawAll;
    }

    private void OnDisable()
    {
        if (grid != null) grid.OnGridRebuilt -= RedrawAll;
    }

    private void Start()
    {
        CreateTexture();
        RedrawAll();
    }

    private void LateUpdate()
    {
        RedrawDirtyCells();
    }

    /// <summary>グリッドの大きさに合わせてテクスチャとスプライトを作る。</summary>
    private void CreateTexture()
    {
        if (grid == null)
        {
            Debug.LogError("SoilGridRenderer: SoilGrid が設定されていない");
            enabled = false;
            return;
        }

        pixelsPerCell = Mathf.Max(1, pixelsPerCell);
        int pixelWidth = grid.Width * pixelsPerCell;
        int pixelHeight = grid.Height * pixelsPerCell;

        texture = new Texture2D(pixelWidth, pixelHeight, TextureFormat.RGBA32, false);
        texture.name = "SoilGridTexture";
        // マスの境目をぼかさない
        texture.filterMode = FilterMode.Point;
        texture.wrapMode = TextureWrapMode.Clamp;

        // 1マス cellSize 単位 ＝ pixelsPerCell ピクセルになるようにする
        float pixelsPerUnit = pixelsPerCell / grid.CellSize;
        Sprite sprite = Sprite.Create(
            texture,
            new Rect(0f, 0f, pixelWidth, pixelHeight),
            Vector2.zero, // 左下を基準にして、この GameObject の位置＝世界の左下にする
            pixelsPerUnit,
            0,
            SpriteMeshType.FullRect);
        sprite.name = "SoilGridSprite";
        spriteRenderer.sprite = sprite;

        blockBuffer = new Color32[pixelsPerCell * pixelsPerCell];
    }

    /// <summary>全マスを塗り直す。</summary>
    public void RedrawAll()
    {
        if (texture == null || grid == null) return;

        int pixelWidth = texture.width;
        Color32[] pixels = new Color32[pixelWidth * texture.height];

        for (int y = 0; y < grid.Height; y++)
        {
            for (int x = 0; x < grid.Width; x++)
            {
                Color32 color = GetCellColor(x, y);
                int blockStart = (y * pixelsPerCell) * pixelWidth + x * pixelsPerCell;
                for (int py = 0; py < pixelsPerCell; py++)
                {
                    int rowStart = blockStart + py * pixelWidth;
                    for (int px = 0; px < pixelsPerCell; px++)
                    {
                        pixels[rowStart + px] = color;
                    }
                }
            }
        }

        texture.SetPixelData(pixels, 0);
        texture.Apply(false);
        grid.ClearDirty();
    }

    /// <summary>変わったマスだけを塗り直す。</summary>
    private void RedrawDirtyCells()
    {
        if (texture == null || grid == null) return;

        IReadOnlyList<int> dirty = grid.DirtyCells;
        if (dirty.Count == 0) return;

        int gridWidth = grid.Width;
        for (int i = 0; i < dirty.Count; i++)
        {
            int index = dirty[i];
            int x = index % gridWidth;
            int y = index / gridWidth;

            Color32 color = GetCellColor(x, y);
            for (int p = 0; p < blockBuffer.Length; p++)
            {
                blockBuffer[p] = color;
            }
            texture.SetPixels32(x * pixelsPerCell, y * pixelsPerCell, pixelsPerCell, pixelsPerCell, blockBuffer);
        }

        texture.Apply(false);
        grid.ClearDirty();
    }

    /// <summary>マスの状態から色を決める。</summary>
    private Color GetCellColor(int x, int y)
    {
        CellType type = grid.GetCell(x, y);

        Color color;
        switch (type)
        {
            case CellType.Air: color = airColor; break;
            case CellType.Soil: color = soilColor; break;
            case CellType.Cavity: color = cavityColor; break;
            case CellType.Stone: color = stoneColor; break;
            case CellType.Water: color = waterColor; break;
            default: color = soilColor; break;
        }

        // 土と石は、のっぺりしないようにマスごとに少しだけ明るさを変える
        if (colorNoise > 0f && (type == CellType.Soil || type == CellType.Stone))
        {
            float shift = (Hash01(x, y) - 0.5f) * 2f * colorNoise;
            color = new Color(
                Mathf.Clamp01(color.r + shift),
                Mathf.Clamp01(color.g + shift),
                Mathf.Clamp01(color.b + shift),
                color.a);
        }

        return color;
    }

    /// <summary>マスの位置から 0〜1 の決まった値を作る（保存せずに何度でも同じ値が出る）。</summary>
    private float Hash01(int x, int y)
    {
        unchecked
        {
            uint h = (uint)(x * 73856093) ^ (uint)(y * 19349663) ^ (uint)colorNoiseSeed;
            h ^= h >> 13;
            h *= 0x85EBCA6Bu;
            h ^= h >> 16;
            return (h & 0xFFFFFF) / (float)0xFFFFFF;
        }
    }
}
