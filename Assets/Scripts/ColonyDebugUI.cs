using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 開発用：コロニーの状態を数字で出す（F6 で切り替え）。
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

    private float timer;

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
        Keyboard keyboard = Keyboard.current;
        if (keyboard != null && keyboard.f6Key.wasPressedThisFrame && label != null)
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
        sb.AppendLine("アリ " + total + "匹（巣 " + colony.AntsInNest + " / 外 " + colony.AntsOutside + "）");
        sb.AppendLine("採餌の刺激 S＝" + colony.ForageStimulus.ToString("0.00"));
        sb.AppendLine("　巣の空腹の平均＝" + colony.NestHungerAverage.ToString("0.00"));
        sb.AppendLine("　入口の道しるべ＝" + colony.EntranceTrail.ToString("0.0"));
        sb.AppendLine("社会胃の平均＝" + cropAverage.ToString("0.00"));
        sb.Append("閾値θの平均＝" + colony.ThetaAverage.ToString("0.00"));
        return sb.ToString();
    }
}
