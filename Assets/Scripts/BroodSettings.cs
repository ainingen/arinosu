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
    [Tooltip("いちばん奥の濃さからこれだけ浅ければ、女王はさらに奥へ寄る（13-15）")]
    public float queenDeepTolerance = 0.02f;
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
    [Tooltip("幼虫を常に満腹として扱う（段階5a の確認用。5b からはオフ）")]
    public bool larvaAlwaysFed = true;
    [Tooltip("幼虫が満腹から空腹になるまでの日数")]
    public float larvaFullToEmptyDays = 1f;
    [Tooltip("もらった量のどれだけが体の大きさになるか")]
    public float growthPerCrop = 0.35f;
    [Tooltip("空腹のまま何日で死ぬか")]
    public float larvaStarveDays = 2f;

    [Header("育児（行動モデル.md 13-4／13-6）")]
    [Tooltip("1回の給餌で幼虫へ流す量。育児係の社会胃から引く")]
    public float feedAmount = 0.2f;
    [Tooltip("これより空腹な幼虫を、給餌の相手として探す")]
    [Range(0f, 1f)] public float larvaHungerThreshold = 0.2f;
    [Tooltip("育児係はここまで社会胃を分ける。体の蓄えがあるので空に近くてよい")]
    [Range(0f, 1f)] public float nurseMinCrop = 0.02f;
    [Tooltip("持ち帰った餌を子どもへ直接配るとき、この濃さ以上の空腹の匂いを感じていれば向かう")]
    public float deliverHungerMin = 0.05f;
    [Tooltip("給餌の相手を見つけられる範囲（cm）")]
    public float broodSenseRange = 1.5f;
    [Tooltip("相手が見つからないまま歩き続けたら、巣の仕事に戻るまでの時間（秒）")]
    public float nurseGiveUpSeconds = 20f;
    [Tooltip("採餌の刺激に幼虫の空腹を混ぜるときの重み（働きアリ1匹＝1.0）")]
    [Range(0f, 2f)] public float larvaForageWeight = 0.5f;
    [Tooltip("育児の刺激のうち、幼虫の空腹が占める割合")]
    [Range(0f, 1f)] public float nurseHungerWeight = 0.7f;
    [Tooltip("育児の刺激のうち、はぐれた子どもの割合が占める割合")]
    [Range(0f, 1f)] public float nurseIsolationWeight = 0.3f;

    [Header("子どもの匂いの置き方（行動モデル.md 13-3）")]
    [Tooltip("子ども1個が1回に置く「子どもの匂い」の量")]
    public float broodDeposit = 1f;
    [Tooltip("空腹な幼虫が置く量の倍率（量は hunger × これ）")]
    public float larvaHungerDeposit = 1f;
    [Tooltip("匂いを置き直す間隔（ゲーム内秒）")]
    public float depositInterval = 0.1f;

    [Header("飢饉の共食い（行動モデル.md 13-6）")]
    [Tooltip("体の蓄えがこれを下回り、かつ社会胃も尽きたとき、子どもを食べて栄養に戻す")]
    [Range(0f, 1f)] public float cannibalReserve = 0.2f;
    [Tooltip("食べてよい幼虫の大きさの上限。育ちかけの幼虫は食べない")]
    [Range(0f, 1f)] public float cannibalMaxSize = 0.3f;
    [Tooltip("卵1個から戻る量")]
    public float cannibalGainEgg = 0.15f;
    [Tooltip("若い幼虫1匹から戻る量")]
    public float cannibalGainLarva = 0.4f;

    [Header("集積（行動モデル.md 13-6）")]
    [Tooltip("はぐれた子どもほど拾いやすくなる。小さいほど拾いにくい")]
    public float kPick = 0.3f;
    [Tooltip("子どもが多い場所ほど置きやすくなる。小さいほど置きやすい")]
    public float kDrop = 0.3f;
    [Tooltip("まわりの子どもを数える範囲（マス）")]
    public int broodSenseRadius = 2;
    [Tooltip("この数で「まわりが子どもでいっぱい」とみなす")]
    public float broodFull = 8f;
    [Tooltip("持ったままこれだけ経ったら、その場に置く（秒）")]
    public float carryGiveUpSeconds = 30f;
    [Tooltip("巣の広さの目標に、幼虫1匹をアリ何匹分として数えるか")]
    public float broodSpaceWeight = 0.5f;

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

    [Header("好む深さ（行動モデル.md 13-15）")]
    [Tooltip("卵と若い幼虫が好む巣の匂いの濃さ（奥ほど濃い）")]
    [Range(0.8f, 1f)] public float preferredNestEgg = 0.94f;
    [Tooltip("育った幼虫が好む濃さ")]
    [Range(0.8f, 1f)] public float preferredNestLarva = 0.90f;
    [Tooltip("繭が好む濃さ（いちばん入口寄り）")]
    [Range(0.8f, 1f)] public float preferredNestPupa = 0.84f;
    [Tooltip("好みからこれだけ外れると、居場所として合わないとみなす")]
    public float preferredNestTolerance = 0.06f;
    [Tooltip("これ未満の幼虫は、卵と同じ深さを好む")]
    [Range(0f, 1f)] public float youngLarvaSize = 0.3f;
    [Tooltip("違う段階が混ざっている場所ほど置きにくくする強さ")]
    [Range(0f, 1f)] public float mixPenalty = 0.7f;

    [Header("部屋の名前（行動モデル.md 13-7。表示だけ）")]
    [Tooltip("まわりを見る範囲（cm）")]
    public float roomSenseRadius = 2f;
    [Tooltip("この数以上の子どもがあれば、その段階の部屋と呼ぶ")]
    public int roomBroodMin = 5;
    [Tooltip("この数以上のアリが休んでいれば「休憩所」と呼ぶ")]
    public int restRoomMin = 5;

    [Header("見た目")]
    [Tooltip("1マスに重ねて描く最大数。大きさは素材の実寸（PPU）で決まる")]
    public int maxDrawnPerCell = 4;

    /// <summary>
    /// その子どもが好む巣の匂いの濃さ（行動モデル.md 13-15）。
    /// 卵と若い幼虫はいちばん奥、育った幼虫は中ほど、繭は入口寄りを好む。
    /// </summary>
    public float PreferredNest(BroodItem item)
    {
        if (item == null) return preferredNestLarva;
        switch (item.stage)
        {
            case BroodStage.Egg:
                return preferredNestEgg;
            case BroodStage.Larva:
                return item.size < youngLarvaSize ? preferredNestEgg : preferredNestLarva;
            default:
                return preferredNestPupa;
        }
    }

    /// <summary>
    /// その濃さが、その子どもの居場所としてどれだけ合っているか（0〜1）。
    /// 1 なら好みどおり、0 なら合わない。
    /// </summary>
    public float NestFit(BroodItem item, float nestValue)
    {
        float gap = Mathf.Abs(nestValue - PreferredNest(item));
        return Mathf.Clamp01(1f - gap / Mathf.Max(0.0001f, preferredNestTolerance));
    }

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
