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
        sb.Append("土：掘った " + colony.DugCells + "／塚に置いた " + colony.MoundCells
            + "／捨てた " + colony.DiscardedSoil);
        return sb.ToString();
    }
}
