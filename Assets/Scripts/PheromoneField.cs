using UnityEngine;

/// <summary>フェロモンの種類。</summary>
public enum PheromoneLayer
{
    /// <summary>道しるべ</summary>
    Trail = 0,
    /// <summary>警報</summary>
    Alarm = 1,
    /// <summary>掘削跡（掘った場所に集中させる。行動モデル.md 12-4）</summary>
    Dig = 2,
    /// <summary>
    /// 幼虫の空腹の匂い（行動モデル.md 13-3）。
    /// 段階5aでは、空腹の女王がその場に置くだけで、たどる側はまだいない。
    /// </summary>
    LarvaHunger = 3,
}

/// <summary>
/// 土のグリッドに重ねるフェロモンの層（行動モデル.md 3章）。
/// 濃度を持てるのは通れるマス（空気・空洞）だけで、土と石は常に 0。
/// 蒸発は経過時間から解析的に計算するので、時間の速さを変えても結果が変わらない。
/// 拡散は1フレームの回数に上限があるので、×100 では少し弱くなる（設計どおり）。
/// </summary>
[DisallowMultipleComponent]
public class PheromoneField : MonoBehaviour
{
    [SerializeField] private SoilGrid grid;
    [SerializeField] private AntSettings settings;

    [Tooltip("これより薄くなったら 0 にする（計算する範囲を狭く保つため）")]
    [SerializeField] private float minConcentration = 0.001f;

    /// <summary>1つの層のデータ。</summary>
    private class Layer
    {
        public float[] values;
        public float[] buffer;
        public float halfLife;
        public float diffusion;
        public float max;

        /// <summary>濃度が 0 でないマスを囲む四角。ここだけ計算すれば済む。</summary>
        public int minX, minY, maxX, maxY;
        public bool isEmpty = true;
    }

    private Layer[] layers;
    /// <summary>通れるマスかどうかの控え。地形が変わったときだけ作り直す。</summary>
    private bool[] passable;
    private int terrainVersion = -1;
    private float diffusionTimer;

    private int width, height;

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

    /// <summary>まだ作っていなければ層を作る。他から先に使われても平気なように公開しておく。</summary>
    public void EnsureBuilt()
    {
        if (layers == null) Build();
    }

    /// <summary>層を作り直す。</summary>
    private void Build()
    {
        if (grid == null) return;
        grid.EnsureGenerated();
        width = grid.Width;
        height = grid.Height;
        int count = width * height;

        layers = new Layer[4];
        layers[(int)PheromoneLayer.Trail] = new Layer { values = new float[count], buffer = new float[count] };
        layers[(int)PheromoneLayer.Alarm] = new Layer { values = new float[count], buffer = new float[count] };
        layers[(int)PheromoneLayer.Dig] = new Layer { values = new float[count], buffer = new float[count] };
        layers[(int)PheromoneLayer.LarvaHunger] = new Layer { values = new float[count], buffer = new float[count] };
        passable = new bool[count];
        ApplySettings();
        RefreshPassable();
    }

    /// <summary>設定の値を層に写す（Inspector で変えてもすぐ効くように毎フレーム呼ぶ）。</summary>
    private void ApplySettings()
    {
        if (settings == null || layers == null) return;
        var trail = layers[(int)PheromoneLayer.Trail];
        trail.halfLife = settings.trailHalfLife;
        trail.diffusion = settings.trailDiffusion;
        trail.max = settings.trailMax;

        var alarm = layers[(int)PheromoneLayer.Alarm];
        alarm.halfLife = settings.alarmHalfLife;
        alarm.diffusion = settings.alarmDiffusion;
        alarm.max = settings.alarmMax;

        var dig = layers[(int)PheromoneLayer.Dig];
        dig.halfLife = settings.digHalfLife;
        dig.diffusion = settings.digDiffusion;
        dig.max = settings.digMax;

        var larvaHunger = layers[(int)PheromoneLayer.LarvaHunger];
        larvaHunger.halfLife = settings.larvaHungerHalfLife;
        larvaHunger.diffusion = settings.larvaHungerDiffusion;
        larvaHunger.max = settings.larvaHungerMax;
    }

    /// <summary>通れるマスの控えを作り直し、固体になったマスの濃度を消す。</summary>
    private void RefreshPassable()
    {
        if (grid == null) return;
        terrainVersion = grid.Version;
        for (int y = 0; y < height; y++)
        {
            int row = y * width;
            for (int x = 0; x < width; x++)
            {
                int i = row + x;
                bool canPass = grid.IsPassable(x, y);
                passable[i] = canPass;
                if (canPass) continue;
                // 土や石になったマスには匂いを残さない
                for (int l = 0; l < layers.Length; l++) layers[l].values[i] = 0f;
            }
        }
    }

    private void Update()
    {
        if (grid == null || settings == null) return;
        EnsureBuilt();
        if (layers == null) return;

        ApplySettings();
        if (grid.Version != terrainVersion) RefreshPassable();

        float deltaTime = Time.deltaTime;
        if (deltaTime <= 0f) return;

        for (int l = 0; l < layers.Length; l++) Evaporate(layers[l], deltaTime);

        // 拡散は決まった間隔で回す。速さを上げたときは回数に上限をつける
        diffusionTimer += deltaTime;
        float interval = Mathf.Max(0.0001f, settings.diffusionInterval);
        int steps = Mathf.FloorToInt(diffusionTimer / interval);
        if (steps <= 0) return;
        diffusionTimer -= steps * interval;
        steps = Mathf.Min(steps, Mathf.Max(0, settings.maxDiffusionStepsPerFrame));

        for (int s = 0; s < steps; s++)
        {
            for (int l = 0; l < layers.Length; l++) Diffuse(layers[l]);
        }
    }

    /// <summary>蒸発。c *= exp(-λ·dt) なので、何秒まとめても結果は同じ。</summary>
    private void Evaporate(Layer layer, float deltaTime)
    {
        if (layer.isEmpty) return;
        float lambda = AntSettings.HalfLifeToLambda(layer.halfLife);
        if (lambda <= 0f) return;
        float factor = Mathf.Exp(-lambda * deltaTime);

        int newMinX = int.MaxValue, newMinY = int.MaxValue, newMaxX = int.MinValue, newMaxY = int.MinValue;

        for (int y = layer.minY; y <= layer.maxY; y++)
        {
            int row = y * width;
            for (int x = layer.minX; x <= layer.maxX; x++)
            {
                int i = row + x;
                float v = layer.values[i];
                if (v <= 0f) continue;
                v *= factor;
                if (v < minConcentration)
                {
                    layer.values[i] = 0f;
                    continue;
                }
                layer.values[i] = v;
                if (x < newMinX) newMinX = x;
                if (x > newMaxX) newMaxX = x;
                if (y < newMinY) newMinY = y;
                if (y > newMaxY) newMaxY = y;
            }
        }

        if (newMaxX < newMinX)
        {
            layer.isEmpty = true;
            return;
        }
        layer.minX = newMinX; layer.maxX = newMaxX;
        layer.minY = newMinY; layer.maxY = newMaxY;
    }

    /// <summary>拡散。隣4マスへ合計 diffusion の割合を渡す。渡せない向き（土・石）のぶんは自分に残る。</summary>
    private void Diffuse(Layer layer)
    {
        if (layer.isEmpty || layer.diffusion <= 0f) return;

        int x0 = Mathf.Max(0, layer.minX - 1);
        int x1 = Mathf.Min(width - 1, layer.maxX + 1);
        int y0 = Mathf.Max(0, layer.minY - 1);
        int y1 = Mathf.Min(height - 1, layer.maxY + 1);

        // 書き込み先を消す（使う範囲だけ）
        for (int y = y0; y <= y1; y++)
        {
            int row = y * width;
            for (int x = x0; x <= x1; x++) layer.buffer[row + x] = 0f;
        }

        float share = layer.diffusion * 0.25f;

        for (int y = layer.minY; y <= layer.maxY; y++)
        {
            int row = y * width;
            for (int x = layer.minX; x <= layer.maxX; x++)
            {
                int i = row + x;
                float c = layer.values[i];
                if (c <= 0f) continue;

                float amount = c * share;
                float moved = 0f;

                if (x > 0 && passable[i - 1]) { layer.buffer[i - 1] += amount; moved += amount; }
                if (x < width - 1 && passable[i + 1]) { layer.buffer[i + 1] += amount; moved += amount; }
                if (y > 0 && passable[i - width]) { layer.buffer[i - width] += amount; moved += amount; }
                if (y < height - 1 && passable[i + width]) { layer.buffer[i + width] += amount; moved += amount; }

                layer.buffer[i] += c - moved;
            }
        }

        float[] swap = layer.values;
        layer.values = layer.buffer;
        layer.buffer = swap;

        layer.minX = x0; layer.maxX = x1;
        layer.minY = y0; layer.maxY = y1;
    }

    /// <summary>その場所にフェロモンを置く。</summary>
    public void Deposit(Vector2 worldPosition, PheromoneLayer layerType, float amount)
    {
        if (layers == null || amount <= 0f) return;
        int x, y;
        if (!grid.WorldToCell(worldPosition, out x, out y)) return;
        DepositAt(x, y, layerType, amount);
    }

    /// <summary>マスを指定してフェロモンを置く。</summary>
    public void DepositAt(int x, int y, PheromoneLayer layerType, float amount)
    {
        EnsureBuilt();
        if (layers == null || amount <= 0f) return;
        if (!grid.IsInside(x, y)) return;
        int i = y * width + x;
        if (!passable[i]) return;   // 土や石には乗らない

        Layer layer = layers[(int)layerType];
        layer.values[i] = Mathf.Min(layer.values[i] + amount, layer.max);

        if (layer.isEmpty)
        {
            layer.isEmpty = false;
            layer.minX = layer.maxX = x;
            layer.minY = layer.maxY = y;
            return;
        }
        if (x < layer.minX) layer.minX = x;
        if (x > layer.maxX) layer.maxX = x;
        if (y < layer.minY) layer.minY = y;
        if (y > layer.maxY) layer.maxY = y;
    }

    /// <summary>その場所の濃さ。世界の外や土の中は 0。</summary>
    public float Sample(Vector2 worldPosition, PheromoneLayer layerType)
    {
        if (layers == null) return 0f;
        int x, y;
        if (!grid.WorldToCell(worldPosition, out x, out y)) return 0f;
        return GetAt(x, y, layerType);
    }

    /// <summary>マスを指定して濃さを読む。</summary>
    public float GetAt(int x, int y, PheromoneLayer layerType)
    {
        if (layers == null || !grid.IsInside(x, y)) return 0f;
        int i = y * width + x;
        if (!passable[i]) return 0f;
        return layers[(int)layerType].values[i];
    }

    /// <summary>層の中身をそのまま渡す（デバッグ表示用。書き換えないこと）。</summary>
    public float[] GetValues(PheromoneLayer layerType)
    {
        return layers != null ? layers[(int)layerType].values : null;
    }

    /// <summary>その層の濃さの上限。</summary>
    public float GetMax(PheromoneLayer layerType)
    {
        return layers != null ? layers[(int)layerType].max : 1f;
    }
}
