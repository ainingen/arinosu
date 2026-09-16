using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 選ばれたアリの情報を出すパネル（仕様8章）。
/// 表示する文言は AntTexts にまとめてあり、ここでは組み立てるだけ。
/// 段階2では仕事・運搬物・日齢は仮の値。場所だけは土の形から本当に決まる。
/// </summary>
[DisallowMultipleComponent]
public class AntInfoPanel : MonoBehaviour
{
    [Tooltip("出したり消したりする本体。未指定ならこの GameObject")]
    [SerializeField] private GameObject panelRoot;
    [SerializeField] private TMP_Text titleText;
    [SerializeField] private TMP_Text statusText;
    [SerializeField] private TMP_Text roleText;
    [SerializeField] private TMP_Text moodText;
    [Tooltip("「気持ち」の見出し。文言は AntTexts から入れる")]
    [SerializeField] private TMP_Text moodHeadingText;
    [Tooltip("内勤⇔外勤のゲージ（Image の Fill Amount で表す）")]
    [SerializeField] private Image roleGaugeFill;

    [Tooltip("表示を作り直す間隔（秒）。0 なら毎フレーム")]
    [SerializeField] private float refreshInterval = 0.2f;

    private Ant target;
    private float timer;

    /// <summary>今表示しているアリ。</summary>
    public Ant Target => target;

    private void Awake()
    {
        if (panelRoot == null) panelRoot = gameObject;
        // 見出しも文言表から入れる（変えたいときは AntTexts だけを見ればよい）
        if (moodHeadingText != null) moodHeadingText.text = AntTexts.HeadingMood;
    }

    /// <summary>アリの情報を表示する。</summary>
    public void Show(Ant ant)
    {
        target = ant;
        if (panelRoot != null) panelRoot.SetActive(ant != null);
        timer = 0f;
        Refresh();
    }

    /// <summary>パネルを閉じる。</summary>
    public void Hide()
    {
        target = null;
        if (panelRoot != null) panelRoot.SetActive(false);
    }

    private void Update()
    {
        if (target == null) return;

        // 毎フレーム文字列を作り直すと無駄なので間引く
        timer -= Time.unscaledDeltaTime;
        if (timer > 0f) return;
        timer = refreshInterval;
        Refresh();
    }

    /// <summary>今の値で表示を作り直す。</summary>
    public void Refresh()
    {
        if (target == null) return;

        if (titleText != null) titleText.text = AntTexts.Caste(target.Caste);
        if (statusText != null) statusText.text = BuildStatusText(target);
        if (roleText != null) roleText.text = BuildRoleText();
        if (moodText != null) moodText.text = AntTexts.Mood(target.CurrentMood);
        if (roleGaugeFill != null) roleGaugeFill.fillAmount = Mathf.Clamp01(target.OutdoorTendency);
    }

    /// <summary>「いまの状況」と「性質」の本文を作る。</summary>
    public string BuildStatusText(Ant ant)
    {
        var sb = new System.Text.StringBuilder();

        sb.AppendLine(AntTexts.HeadingStatus);
        AppendRow(sb, AntTexts.LabelTask, AntTexts.Task(ant.CurrentTask));
        AppendRow(sb, AntTexts.LabelPlace, AntTexts.Place(ant.CurrentPlace));
        AppendRow(sb, AntTexts.LabelCarry, AntTexts.Carry(ant.Carrying));
        sb.AppendLine();

        sb.AppendLine(AntTexts.HeadingNature);
        AppendRow(sb, AntTexts.LabelCaste, AntTexts.Caste(ant.Caste));
        AppendRow(sb, AntTexts.LabelAge, AntTexts.AgeInDays(Mathf.FloorToInt(ant.AgeDays)));

        return sb.ToString();
    }

    /// <summary>ゲージに添える「内勤寄り ⇔ 外勤寄り」の行。</summary>
    private string BuildRoleText()
    {
        return AntTexts.LabelRole + "：" + AntTexts.RoleIndoor + " ⇔ " + AntTexts.RoleOutdoor;
    }

    private void AppendRow(System.Text.StringBuilder sb, string label, string value)
    {
        sb.Append("  ").Append(label).Append("：").AppendLine(value);
    }
}
