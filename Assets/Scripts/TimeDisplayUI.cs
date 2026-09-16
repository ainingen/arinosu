using TMPro;
using UnityEngine;

/// <summary>
/// 画面に日時と速さを表示する。
/// 表示する文言はすべて BuildText() の中だけで作る。
/// 段階2で日本語にするときは、このメソッドを書き換えれば足りるようにしてある。
/// </summary>
[DisallowMultipleComponent]
public class TimeDisplayUI : MonoBehaviour
{
    [SerializeField] private GameClock clock;
    [SerializeField] private TimeSpeedController speedController;
    [Tooltip("表示先。未指定なら同じ GameObject から探す")]
    [SerializeField] private TMP_Text label;

    private void Awake()
    {
        if (label == null) label = GetComponent<TMP_Text>();
        if (clock == null) clock = FindFirstObjectByType<GameClock>();
        if (speedController == null) speedController = FindFirstObjectByType<TimeSpeedController>();
    }

    private void Update()
    {
        if (label == null) return;
        label.text = BuildText();
    }

    /// <summary>
    /// 表示する文字列を作る。ここが文言を作る唯一の場所。
    /// 例：「Day 3  14:00  Spring  x10」
    /// </summary>
    public string BuildText()
    {
        if (clock == null) return string.Empty;

        string day = clock.Day + "日目";
        string time = clock.Hour.ToString("00") + ":" + clock.Minute.ToString("00");
        string season = SeasonToText(clock.CurrentSeason);
        string speed = SpeedToText();

        return day + "　" + time + "　" + season + (speed.Length > 0 ? "　" + speed : string.Empty);
    }

    /// <summary>季節の表示名。</summary>
    private string SeasonToText(Season season)
    {
        switch (season)
        {
            case Season.Spring: return "春";
            case Season.Summer: return "夏";
            case Season.Autumn: return "秋";
            case Season.Winter: return "冬";
            default: return string.Empty;
        }
    }

    /// <summary>速さの表示名。</summary>
    private string SpeedToText()
    {
        if (speedController == null) return string.Empty;
        if (speedController.IsPaused) return "一時停止";
        return "×" + speedController.CurrentSpeed.ToString("0.##");
    }
}
