using UnityEngine.InputSystem;

/// <summary>
/// 開発用の切り替えキーをまとめた場所。
///
/// Web版（ブラウザ）では F キーがブラウザに取られてしまう
/// （F1＝ヘルプ、F3＝検索、F5＝再読み込み、F7＝キャレットブラウズ など）。
/// そのため、開発用の切り替えは Shift＋数字に統一する。
///
/// 数字キー単体は時間の速さの切り替えに使っているので、
/// Shift を押している間はそちらが反応しないようにしてある。
/// </summary>
public static class DebugKeys
{
    /// <summary>Shift を押しているか。</summary>
    public static bool ShiftHeld
    {
        get
        {
            Keyboard keyboard = Keyboard.current;
            return keyboard != null && keyboard.shiftKey.isPressed;
        }
    }

    /// <summary>Shift＋その数字が、このフレームで押されたか。</summary>
    public static bool WasPressed(int digit)
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard == null || !keyboard.shiftKey.isPressed) return false;

        switch (digit)
        {
            case 1: return keyboard.digit1Key.wasPressedThisFrame;
            case 2: return keyboard.digit2Key.wasPressedThisFrame;
            case 3: return keyboard.digit3Key.wasPressedThisFrame;
            case 4: return keyboard.digit4Key.wasPressedThisFrame;
            case 5: return keyboard.digit5Key.wasPressedThisFrame;
            case 6: return keyboard.digit6Key.wasPressedThisFrame;
            case 7: return keyboard.digit7Key.wasPressedThisFrame;
            case 8: return keyboard.digit8Key.wasPressedThisFrame;
            case 9: return keyboard.digit9Key.wasPressedThisFrame;
            case 0: return keyboard.digit0Key.wasPressedThisFrame;
            default: return false;
        }
    }
}
