using TMPro;
using UnityEngine;

/// <summary>
/// 開発用：コロニーの状態を数字で出す（Shift＋6 で切り替え）。
/// 反応閾値モデルが効いているかは、目で見るだけでは分からないため。
/// </summary>
[DisallowMultipleComponent]
public class ColonyDebugUI : MonoBehaviour
{
    [SerializeField] private Colony colony;
    [Tooltip("餌までの距離を測るのに使う。未指定ならシーンから探す")]
    [SerializeField] private NestField nestField;
    [SerializeField] private TMP_Text label;
    [Tooltip("表示を作り直す間隔（秒）")]
    [SerializeField] private float refreshInterval = 0.25f;
    [Tooltip("最初から出しておくか")]
    [SerializeField] private bool visibleAtStart;

    [Tooltip("fps の表示をなめらかにする強さ（0〜1、小さいほどゆっくり動く）")]
    [SerializeField, Range(0.01f, 1f)] private float fpsSmoothing = 0.1f;

    private float timer;
    private float smoothedFps;

    private void Awake()
    {
        if (colony == null) colony = FindFirstObjectByType<Colony>();
        if (nestField == null) nestField = FindFirstObjectByType<NestField>();
    }

    private void Start()
    {
        if (label != null) label.gameObject.SetActive(visibleAtStart);
    }

    private void Update()
    {
        // fps は表示していなくても測っておく（出した瞬間から正しい値が見えるように）
        if (Time.unscaledDeltaTime > 0f)
        {
            float instant = 1f / Time.unscaledDeltaTime;
            smoothedFps = smoothedFps <= 0f ? instant : Mathf.Lerp(smoothedFps, instant, fpsSmoothing);
        }

        // Shift＋6：コロニーの状態表示
        if (DebugKeys.WasPressed(6) && label != null)
        {
            label.gameObject.SetActive(!label.gameObject.activeSelf);
        }

        if (label == null || !label.gameObject.activeSelf || colony == null) return;

        timer -= Time.unscaledDeltaTime;
        if (timer > 0f) return;
        timer = refreshInterval;
        label.text = BuildText();
    }

    /// <summary>出す文字列を作る。</summary>
    public string BuildText()
    {
        int total = Ant.All.Count;
        float cropSum = 0f;
        for (int i = 0; i < total; i++)
        {
            Ant ant = Ant.All[i];
            if (ant != null) cropSum += ant.Crop;
        }
        float cropAverage = total > 0 ? cropSum / total : 0f;

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("fps " + smoothedFps.ToString("0") + "（1フレーム " + (smoothedFps > 0f ? (1000f / smoothedFps).ToString("0.0") : "-") + "ms）／速さ ×" + Time.timeScale.ToString("0.##"));
        sb.AppendLine("アリ " + total + "匹（巣 " + colony.AntsInNest + " / 外 " + colony.AntsOutside
            + "）／これまでの死亡 " + colony.DeathCount + "匹");
        sb.AppendLine("採餌の刺激 S＝" + colony.ForageStimulus.ToString("0.00"));
        sb.AppendLine("　巣の空腹の平均＝" + colony.NestHungerAverage.ToString("0.00"));
        sb.AppendLine("　入口の道しるべ＝" + colony.EntranceTrail.ToString("0.0"));
        sb.AppendLine("社会胃の平均＝" + cropAverage.ToString("0.00"));
        sb.AppendLine("閾値θの平均＝" + colony.ThetaAverage.ToString("0.00"));

        int digging = 0;
        for (int i = 0; i < total; i++)
        {
            Ant ant = Ant.All[i];
            if (ant != null && ant.CurrentTask == AntTask.Dig) digging++;
        }
        sb.AppendLine("掘る刺激 S_dig＝" + colony.DigStimulus.ToString("0.00"));
        sb.AppendLine("　巣の空洞＝" + colony.CavityCells + " / 目標 " + colony.TargetCavityCells + "マス"
            + "／掘っている＝" + digging + "匹");
        sb.AppendLine("土：掘った " + colony.DugCells + "／塚に置いた " + colony.MoundCells
            + "／捨てた " + colony.DiscardedSoil);
        sb.AppendLine(BuildFoodText());

        // 生活環の内訳（段階5）
        var brood = FindFirstObjectByType<BroodField>();
        if (brood != null)
        {
            int queens = 0;
            for (int i = 0; i < total; i++)
            {
                Ant ant = Ant.All[i];
                if (ant != null && ant.IsQueen) queens++;
            }
            sb.Append("女王 " + queens + "／卵 " + brood.CountOf(BroodStage.Egg)
                + "／幼虫 " + brood.CountOf(BroodStage.Larva)
                + "／繭 " + brood.CountOf(BroodStage.Pupa)
                + "／羽化 " + brood.HatchedCount + "匹");
        }
        return sb.ToString();
    }

    /// <summary>
    /// 地表の餌の様子。
    /// 遠い餌が上限の枠を占めて新しい餌が出なくなる詰まりは、
    /// 画面を見ているだけでは分からないので数字で出す。
    /// </summary>
    private string BuildFoodText()
    {
        var foods = FoodSource.All;
        int total = 0;
        float nearest = -1f;
        bool hasEntrance = nestField != null && nestField.HasEntrance;
        float entranceX = hasEntrance ? nestField.EntranceWorld.x : 0f;

        for (int i = 0; i < foods.Count; i++)
        {
            FoodSource food = foods[i];
            if (food == null) continue;
            total += food.Amount;
            if (!hasEntrance) continue;
            float distance = Mathf.Abs(food.Position.x - entranceX);
            if (nearest < 0f || distance < nearest) nearest = distance;
        }

        return "餌：地表に " + foods.Count + "個／合計残量 " + total
            + "／最寄り " + (nearest < 0f ? "－" : nearest.ToString("0.0") + "cm")
            + "／乾いて消えた " + FoodSource.ExpiredCount;
    }
}
