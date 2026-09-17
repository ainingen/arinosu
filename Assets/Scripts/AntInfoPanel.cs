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
    [Tooltip("体の蓄え（脂肪体）の見出し")]
    [SerializeField] private TMP_Text reserveText;
    [Tooltip("体の蓄えのゲージ（Image の Fill Amount で表す）")]
    [SerializeField] private Image reserveGaugeFill;

    [Tooltip("表示を作り直す間隔（秒）。0 なら毎フレーム")]
    [SerializeField] private float refreshInterval = 0.2f;

    private Ant target;
    private BroodItem targetBrood;
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

    /// <summary>子どもの情報を表示する（行動モデル.md 13-8）。</summary>
    public void ShowBrood(BroodItem brood)
    {
        target = null;
        targetBrood = brood;
        if (panelRoot != null) panelRoot.SetActive(brood != null);
        timer = 0f;
        Refresh();
    }

    /// <summary>パネルを閉じる。</summary>
    public void Hide()
    {
        target = null;
        targetBrood = null;
        if (panelRoot != null) panelRoot.SetActive(false);
    }

    private void Update()
    {
        if (target == null && targetBrood == null) return;

        // 毎フレーム文字列を作り直すと無駄なので間引く
        timer -= Time.unscaledDeltaTime;
        if (timer > 0f) return;
        timer = refreshInterval;
        Refresh();
    }

    /// <summary>今の値で表示を作り直す。</summary>
    public void Refresh()
    {
        if (targetBrood != null)
        {
            RefreshBrood();
            return;
        }
        if (target == null) return;

        if (titleText != null) titleText.text = AntTexts.Caste(target.Caste);
        if (statusText != null) statusText.text = BuildStatusText(target);
        if (roleText != null) roleText.text = BuildRoleText();
        if (moodText != null) moodText.text = AntTexts.Mood(target.CurrentMood);
        if (roleGaugeFill != null) roleGaugeFill.fillAmount = Mathf.Clamp01(target.OutdoorTendency);

        // 体の蓄え（脂肪体）。社会胃と違って口移しでは動かない
        ShowReserve(true);
        if (reserveText != null) reserveText.text = AntTexts.LabelReserve;
        if (reserveGaugeFill != null) reserveGaugeFill.fillAmount = Mathf.Clamp01(target.Reserve);
    }

    /// <summary>子どもの表示を作り直す。</summary>
    private void RefreshBrood()
    {
        BroodItem brood = targetBrood;
        if (titleText != null) titleText.text = AntTexts.BroodStageName(brood.stage);

        var sb = new System.Text.StringBuilder();
        sb.AppendLine(AntTexts.HeadingBrood);
        sb.Append("  ").Append(AntTexts.LabelStage).Append("：").AppendLine(AntTexts.BroodStageName(brood.stage));
        sb.Append("  ").Append(AntTexts.LabelDaysInStage).Append("：")
            .AppendLine(AntTexts.AgeInDays(Mathf.FloorToInt(brood.ageInStage)));

        if (brood.stage == BroodStage.Larva)
        {
            sb.AppendLine();
            sb.AppendLine(AntTexts.HeadingNature);
            sb.Append("  ").Append(AntTexts.LabelLarvaSize).Append("：")
                .AppendLine((brood.size * 100f).ToString("0") + "%");
        }

        if (statusText != null) statusText.text = sb.ToString();
        if (roleText != null) roleText.text = string.Empty;
        ShowReserve(false);
        if (roleGaugeFill != null) roleGaugeFill.fillAmount = brood.stage == BroodStage.Larva ? brood.crop : 0f;
        if (moodText != null) moodText.text = brood.stage == BroodStage.Larva && brood.Hunger > 0.5f
            ? "お腹をすかせている"
            : "眠っている";
    }

    /// <summary>体の蓄えの行を出すかどうか（子どもには出さない）。</summary>
    private void ShowReserve(bool visible)
    {
        if (reserveText != null && reserveText.gameObject.activeSelf != visible)
            reserveText.gameObject.SetActive(visible);
        if (reserveGaugeFill == null) return;

        // ゲージは枠ごと出し入れする（枠だけ残ると空の帯に見えるため）
        Transform frame = reserveGaugeFill.transform.parent != null
            ? reserveGaugeFill.transform.parent : reserveGaugeFill.transform;
        if (frame.gameObject.activeSelf != visible) frame.gameObject.SetActive(visible);
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
