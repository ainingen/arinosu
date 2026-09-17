using UnityEngine;

/// <summary>
/// 生活環（女王・卵・幼虫・蛹）の調整値（行動モデル.md 13章）。
/// AntSettings が大きくなってきたので、こちらに分けてある。
/// 数値はすべて【仮】。
/// </summary>
[CreateAssetMenu(fileName = "BroodSettings", menuName = "arinosu/Brood Settings")]
public class BroodSettings : ScriptableObject
{
    [Header("女王")]
    [Tooltip("これより空腹なら、最寄りの働きアリへ寄ってねだる")]
    [Range(0f, 1f)] public float queenBegThreshold = 0.4f;
    [Tooltip("女王の社会胃の消費倍率（産卵中の代謝）")]
    public float queenMetabolism = 1.5f;
    [Tooltip("女王の歩く速さの倍率")]
    public float queenWalkSpeed = 0.3f;
    [Tooltip("女王がふだん動かないときに、歩き出す確率（知覚tickあたり）")]
    [Range(0f, 1f)] public float queenWanderChance = 0.01f;
    [Tooltip("歩き出したとき、歩き続ける時間（ゲーム内秒）")]
    public float queenWanderSeconds = 2f;
    [Tooltip("歩くときに巣の奥へ寄る強さ（働きアリの homingBias の何倍か）")]
    public float queenNestBias = 3f;
    [Tooltip("女王が進めるのは、巣の匂いがこれ以上のマスだけ（奥から出ない）")]
    [Range(0f, 1f)] public float queenMinNest = 0.9f;
    [Tooltip("空腹のときに置く匂いの量の倍率（量は hunger × これ）")]
    public float queenHungerDeposit = 1f;

    [Header("産卵（行動モデル.md 13-1）")]
    [Tooltip("満腹のときに1日あたり産む数")]
    public float layPerDay = 2f;
    [Tooltip("これを下回ると産卵が止まる")]
    [Range(0f, 1f)] public float layMinCrop = 0.5f;
    [Tooltip("1個産むごとに減る社会胃")]
    public float layCost = 0.05f;

    [Header("生活環の長さ（日）")]
    public float eggDays = 12f;
    public float larvaDays = 20f;
    public float pupaDays = 18f;
    [Tooltip("確認のために短くするための倍率。1.0 が本来の長さ")]
    public float stageDurationScale = 1f;

    [Header("幼虫")]
    [Tooltip("段階5aでは幼虫を常に満腹として扱う（給餌は段階5b）")]
    public bool larvaAlwaysFed = true;
    [Tooltip("幼虫が満腹から空腹になるまでの日数")]
    public float larvaFullToEmptyDays = 1f;
    [Tooltip("もらった量のどれだけが体の大きさになるか")]
    public float growthPerCrop = 0.35f;
    [Tooltip("空腹のまま何日で死ぬか")]
    public float larvaStarveDays = 2f;

    [Header("新しく羽化した働きアリ")]
    [Range(0f, 1f)] public float newAdultCrop = 0.5f;

    [Header("開始時の子ども")]
    public int initialEggs = 8;
    public int initialLarvae = 6;
    public int initialPupae = 4;

    [Header("寿命（行動モデル.md 13-10）")]
    [Tooltip("働きアリの寿命（日）")]
    public float workerLifespanDays = 180f;
    [Tooltip("寿命のばらつき（±の割合）")]
    [Range(0f, 1f)] public float lifespanVariation = 0.2f;

    [Header("見た目")]
    [Tooltip("1マスに重ねて描く最大数。大きさは素材の実寸（PPU）で決まる")]
    public int maxDrawnPerCell = 4;

    /// <summary>その段階の長さ（日）。確認用の倍率を掛けたもの。</summary>
    public float StageDuration(BroodStage stage)
    {
        float days;
        switch (stage)
        {
            case BroodStage.Egg: days = eggDays; break;
            case BroodStage.Larva: days = larvaDays; break;
            default: days = pupaDays; break;
        }
        return days * Mathf.Max(0.01f, stageDurationScale);
    }

    /// <summary>
    /// 女王の満腹度から、1日あたりの産卵数を求める（行動モデル.md 13-1）。
    /// 満腹なら layPerDay、layMinCrop を下回ると 0。
    /// </summary>
    public float LayRatePerDay(float queenCrop)
    {
        float span = 1f - layMinCrop;
        if (span <= 0f) return queenCrop >= layMinCrop ? layPerDay : 0f;
        return layPerDay * Mathf.Clamp01((queenCrop - layMinCrop) / span);
    }
}
