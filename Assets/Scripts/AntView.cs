using UnityEngine;

/// <summary>
/// アリの見た目。色の切り替えと、脚・触角の動きだけを受け持つ。
/// どこへ歩くかは Ant が決め、ここは「どれだけ進んだか」を受け取って脚を振るだけ。
/// </summary>
[DisallowMultipleComponent]
public class AntView : MonoBehaviour
{
    [Header("パーツ")]
    [Tooltip("三脚歩行の組A：左前・右中・左後")]
    [SerializeField] private Transform[] legsGroupA;
    [Tooltip("三脚歩行の組B：右前・左中・右後")]
    [SerializeField] private Transform[] legsGroupB;
    [SerializeField] private Transform[] antennae;

    [Header("色")]
    [SerializeField] private Color normalColor = new Color(0.10f, 0.08f, 0.07f, 1f);
    [Tooltip("選択されているときの色（反転色）")]
    [SerializeField] private Color selectedColor = new Color(1f, 0.95f, 0.75f, 1f);

    [Header("歩き方の見た目")]
    [Tooltip("脚を前後に振る角度（度）")]
    [SerializeField] private float legSwingAngle = 15f;
    [Tooltip("1単位（＝1cm）進むあいだの歩数")]
    [SerializeField] private float stepsPerUnit = 4f;

    [Header("触角")]
    [SerializeField] private bool animateAntennae = true;
    [SerializeField] private float antennaSwingAngle = 8f;
    [SerializeField] private float antennaSwingSpeed = 3f;

    private SpriteRenderer[] renderers;
    /// <summary>各パーツの最初の角度。ここを基準に振る。</summary>
    private float[] legBaseAnglesA;
    private float[] legBaseAnglesB;
    private float[] antennaBaseAngles;
    /// <summary>左側のパーツは振る向きを反転させるための符号。</summary>
    private float[] legSwingSignsA;
    private float[] legSwingSignsB;

    /// <summary>歩幅の位相。進んだ距離に比例して増える。</summary>
    private float gaitPhase;
    private float antennaPhase;
    private bool isSelected;

    /// <summary>選択されているか。</summary>
    public bool IsSelected => isSelected;

    private void Awake()
    {
        renderers = GetComponentsInChildren<SpriteRenderer>(true);
        legBaseAnglesA = CaptureBaseAngles(legsGroupA);
        legBaseAnglesB = CaptureBaseAngles(legsGroupB);
        antennaBaseAngles = CaptureBaseAngles(antennae);
        legSwingSignsA = CaptureSwingSigns(legsGroupA);
        legSwingSignsB = CaptureSwingSigns(legsGroupB);
        ApplyColor();
    }

    /// <summary>
    /// 左側のパーツは形も角度も反転しているので、振る向きも反転させる。
    /// こうしないと、同時に動くはずの3本のうち左右で前後がちぐはぐになる。
    /// </summary>
    private float[] CaptureSwingSigns(Transform[] parts)
    {
        if (parts == null) return new float[0];
        float[] signs = new float[parts.Length];
        for (int i = 0; i < parts.Length; i++)
        {
            signs[i] = (parts[i] != null && parts[i].localPosition.x < 0f) ? -1f : 1f;
        }
        return signs;
    }

    /// <summary>今の角度を基準として覚える。</summary>
    private float[] CaptureBaseAngles(Transform[] parts)
    {
        if (parts == null) return new float[0];
        float[] angles = new float[parts.Length];
        for (int i = 0; i < parts.Length; i++)
        {
            angles[i] = parts[i] != null ? parts[i].localEulerAngles.z : 0f;
        }
        return angles;
    }

    /// <summary>選択状態を切り替える（色が反転する）。</summary>
    public void SetSelected(bool selected)
    {
        if (isSelected == selected) return;
        isSelected = selected;
        ApplyColor();
    }

    /// <summary>全パーツを今の状態の色で塗る。</summary>
    private void ApplyColor()
    {
        if (renderers == null) return;
        Color color = isSelected ? selectedColor : normalColor;
        for (int i = 0; i < renderers.Length; i++)
        {
            if (renderers[i] != null) renderers[i].color = color;
        }
    }

    /// <summary>
    /// 進んだぶんだけ脚を動かす。Ant が毎フレーム呼ぶ。
    /// </summary>
    /// <param name="distanceMoved">このフレームで進んだ距離（Unity単位）</param>
    /// <param name="deltaTime">経過時間（触角の揺れに使う）</param>
    public void UpdateGait(float distanceMoved, float deltaTime)
    {
        gaitPhase += distanceMoved * stepsPerUnit;

        // 組Aと組Bが逆向きに振れる（三脚歩行）
        float swing = Mathf.Sin(gaitPhase * Mathf.PI * 2f) * legSwingAngle;
        ApplySwing(legsGroupA, legBaseAnglesA, legSwingSignsA, swing);
        ApplySwing(legsGroupB, legBaseAnglesB, legSwingSignsB, -swing);

        if (animateAntennae && antennae != null && antennae.Length > 0)
        {
            antennaPhase += deltaTime * antennaSwingSpeed;
            // 左右の触角を互い違いに揺らす
            for (int i = 0; i < antennae.Length; i++)
            {
                if (antennae[i] == null) continue;
                float phase = antennaPhase + (i % 2 == 0 ? 0f : Mathf.PI);
                float angle = Mathf.Sin(phase) * antennaSwingAngle;
                SetLocalZ(antennae[i], antennaBaseAngles[i] + angle);
            }
        }
    }

    private void ApplySwing(Transform[] parts, float[] baseAngles, float[] signs, float swing)
    {
        if (parts == null) return;
        for (int i = 0; i < parts.Length; i++)
        {
            if (parts[i] == null) continue;
            SetLocalZ(parts[i], baseAngles[i] + swing * signs[i]);
        }
    }

    private void SetLocalZ(Transform part, float angleZ)
    {
        Vector3 euler = part.localEulerAngles;
        euler.z = angleZ;
        part.localEulerAngles = euler;
    }
}
