using System;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 時間の速さを切り替える。数字キー 0〜3、または UI ボタンから SetSpeedIndex() を呼ぶ。
/// </summary>
[DisallowMultipleComponent]
public class TimeSpeedController : MonoBehaviour
{
    [Tooltip("切り替えられる速さ。キー 0,1,2,3 の順に対応する")]
    [SerializeField] private float[] speeds = { 0f, 1f, 10f, 100f };
    [Tooltip("開始時にどの速さにするか（配列の番号）")]
    [SerializeField] private int startIndex = 1;
    [Tooltip("数字キーで切り替えられるようにする")]
    [SerializeField] private bool useKeyboard = true;

    private int currentIndex = -1;

    /// <summary>今選ばれている速さの番号。</summary>
    public int CurrentIndex => currentIndex;
    /// <summary>今の速さ（倍率）。</summary>
    public float CurrentSpeed => IsValidIndex(currentIndex) ? speeds[currentIndex] : 1f;
    /// <summary>一時停止中か。</summary>
    public bool IsPaused => CurrentSpeed <= 0f;
    /// <summary>選べる速さの数。</summary>
    public int SpeedCount => speeds != null ? speeds.Length : 0;

    /// <summary>速さが変わったときに呼ばれる。引数は速さの番号。</summary>
    public event Action<int> OnSpeedChanged;

    private void Start()
    {
        SetSpeedIndex(startIndex);
    }

    private void OnDisable()
    {
        // 止めたままエディタに戻らないように元へ戻す
        Time.timeScale = 1f;
    }

    private void Update()
    {
        if (!useKeyboard) return;

        Keyboard keyboard = Keyboard.current;
        if (keyboard == null) return;

        // Shift＋数字は開発用の表示切り替えに使っているので、速さは変えない
        if (DebugKeys.ShiftHeld) return;

        if (keyboard.digit0Key.wasPressedThisFrame || keyboard.numpad0Key.wasPressedThisFrame) SetSpeedIndex(0);
        else if (keyboard.digit1Key.wasPressedThisFrame || keyboard.numpad1Key.wasPressedThisFrame) SetSpeedIndex(1);
        else if (keyboard.digit2Key.wasPressedThisFrame || keyboard.numpad2Key.wasPressedThisFrame) SetSpeedIndex(2);
        else if (keyboard.digit3Key.wasPressedThisFrame || keyboard.numpad3Key.wasPressedThisFrame) SetSpeedIndex(3);
    }

    /// <summary>速さを番号で切り替える（UI ボタンからも呼べる）。</summary>
    public void SetSpeedIndex(int index)
    {
        if (!IsValidIndex(index)) return;
        if (currentIndex == index) return;

        currentIndex = index;
        Time.timeScale = speeds[index];
        OnSpeedChanged?.Invoke(index);
    }

    /// <summary>番号で指定された速さの値を返す。</summary>
    public float GetSpeedAt(int index)
    {
        return IsValidIndex(index) ? speeds[index] : 1f;
    }

    private bool IsValidIndex(int index)
    {
        return speeds != null && index >= 0 && index < speeds.Length;
    }
}
