using System;
using UnityEngine;

/// <summary>季節。</summary>
public enum Season
{
    Spring = 0,
    Summer = 1,
    Autumn = 2,
    Winter = 3,
}

/// <summary>
/// ゲーム内の時間を進める。等速で realSecondsPerDay 秒 ＝ ゲーム内1日。
/// 速さの切り替えは Time.timeScale で行うので、ここは Time.deltaTime を足すだけでよい。
/// </summary>
[DisallowMultipleComponent]
public class GameClock : MonoBehaviour
{
    [Header("時間の進み方")]
    [Tooltip("等速のとき、現実の何秒でゲーム内1日が過ぎるか")]
    [SerializeField] private float realSecondsPerDay = 300f;

    [Header("開始時刻")]
    [Tooltip("開始時の日数（1日目から）")]
    [SerializeField] private int startDay = 1;
    [Tooltip("開始時の時刻（0〜24）")]
    [SerializeField, Range(0f, 24f)] private float startHour = 6f;
    [SerializeField] private Season startSeason = Season.Spring;

    [Header("季節")]
    [Tooltip("1つの季節が続く日数")]
    [SerializeField] private int daysPerSeason = 30;

    /// <summary>開始してから過ぎたゲーム内日数。</summary>
    private double elapsedDays;
    private int lastReportedDay;

    /// <summary>今が何日目か（1日目から数える）。</summary>
    public int Day => startDay + (int)elapsedDays;
    /// <summary>その日のどこまで進んだか（0〜1）。</summary>
    public float DayProgress01 => (float)(elapsedDays - Math.Floor(elapsedDays));
    /// <summary>今の時（0〜23）。</summary>
    public int Hour => Mathf.Clamp((int)(DayProgress01 * 24f), 0, 23);
    /// <summary>今の分（0〜59）。</summary>
    public int Minute => Mathf.Clamp((int)(DayProgress01 * 24f * 60f) % 60, 0, 59);
    /// <summary>今の季節。</summary>
    public Season CurrentSeason
    {
        get
        {
            int seasonsPassed = daysPerSeason > 0 ? (int)elapsedDays / daysPerSeason : 0;
            return (Season)(((int)startSeason + seasonsPassed) % 4);
        }
    }
    /// <summary>開始してから過ぎたゲーム内日数（小数）。</summary>
    public double ElapsedDays => elapsedDays;
    /// <summary>昼か夜か（段階6で使う）。</summary>
    public bool IsDaytime => Hour >= 6 && Hour < 18;

    /// <summary>日付が変わったときに呼ばれる。引数は新しい日数。</summary>
    public event Action<int> OnDayChanged;

    private void Awake()
    {
        elapsedDays = startHour / 24f;
        lastReportedDay = Day;
    }

    private void Update()
    {
        if (realSecondsPerDay <= 0f) return;

        // Time.deltaTime は Time.timeScale の影響を受けるので、速度切り替えがそのまま効く
        elapsedDays += Time.deltaTime / realSecondsPerDay;

        int day = Day;
        if (day != lastReportedDay)
        {
            lastReportedDay = day;
            OnDayChanged?.Invoke(day);
        }
    }
}
