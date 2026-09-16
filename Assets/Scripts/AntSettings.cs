using UnityEngine;

/// <summary>
/// 行動モデルの調整値をまとめた設定。行動モデル.md の11章の一覧に対応する。
/// 数値はすべて【仮】で、Inspector で調整する前提。
/// 見た目の値（色や大きさ）はここに入れず、各コンポーネントに残す。
/// </summary>
[CreateAssetMenu(fileName = "AntSettings", menuName = "arinosu/Ant Settings")]
public class AntSettings : ScriptableObject
{
    [Header("時間の刻み（ゲーム内秒）")]
    [Tooltip("触角で匂いを読み、進路を直す間隔")]
    public float perceptionInterval = 0.1f;
    [Tooltip("仕事を選び直す間隔")]
    public float decisionInterval = 5f;
    [Tooltip("フェロモンを拡散させる間隔")]
    public float diffusionInterval = 0.5f;
    [Tooltip("社会胃を減らす間隔")]
    public float metabolismInterval = 1f;
    [Tooltip("1フレームで拡散を回す上限。×100 のとき効く")]
    public int maxDiffusionStepsPerFrame = 8;

    [Header("フェロモン：道しるべ")]
    [Tooltip("濃さが半分になるまでの時間（ゲーム内秒）")]
    public float trailHalfLife = 60f;
    [Tooltip("1回の拡散で隣4マスへ渡す割合の合計")]
    [Range(0f, 1f)] public float trailDiffusion = 0.05f;
    [Tooltip("1マス通過ごとに置く量")]
    public float depositTrail = 1f;
    [Tooltip("1マスに乗る濃さの上限")]
    public float trailMax = 10f;

    [Header("フェロモン：警報")]
    public float alarmHalfLife = 10f;
    [Range(0f, 1f)] public float alarmDiffusion = 0.2f;
    public float alarmMax = 10f;

    [Header("巣の匂い")]
    [Tooltip("入口（空洞と空気の境目）での濃さ")]
    [Range(0f, 1f)] public float nestEntranceValue = 0.8f;
    [Tooltip("巣のいちばん奥での濃さ")]
    [Range(0f, 1f)] public float nestDeepValue = 1f;
    [Tooltip("この距離ぶん奥へ入ると、いちばん奥の濃さになる（cm）")]
    public float nestDeepDistance = 10f;
    [Tooltip("地表で匂いがほぼ消えるまでの距離（cm）")]
    public float nestSurfaceFalloff = 15f;
    [Tooltip("これより薄いと「巣から遠い」")]
    public float nestFar = 0.05f;
    [Tooltip("これより濃ければ巣の内部にいる")]
    public float nestInside = 0.8f;
    [Tooltip("巣の匂いを作り直す最短の間隔（秒）。掘削が続いても重くならないように")]
    public float nestRebuildInterval = 0.5f;

    [Header("触角（感知）")]
    [Tooltip("感知点の前方への距離（cm）")]
    public float sensorForward = 0.6f;
    [Tooltip("感知点の左右への開き（度）")]
    public float sensorSpread = 35f;

    [Header("道しるべのたどり方")]
    [Tooltip("たどる確率が半分になる濃さ")]
    public float kTrail = 2f;
    [Tooltip("濃い側へ曲がる速さ（度/秒）")]
    public float turnGain = 40f;
    [Tooltip("たどらないときのふらつき（度/秒）")]
    public float wanderSigma = 25f;
    [Tooltip("巣から遠いときに巣へ戻ろうとする弱い偏り（度/秒）")]
    public float homingBias = 10f;
    [Tooltip("「匂いが途切れた」と感じている時間（秒）")]
    public float trailLostDuration = 3f;

    [Header("探索")]
    [Tooltip("餌に気づく範囲（cm）")]
    public float foodSenseRange = 1.5f;
    [Tooltip("これだけ外にいて何も見つからなければ空手で帰る（ゲーム内分）")]
    public float giveUpMinutes = 20f;

    [Header("移動（段階3bで Ant に効かせる）")]
    public float walkSpeed = 2f;
    public float turnSpeed = 540f;
    public float fallSpeed = 5f;

    [Header("社会胃")]
    [Tooltip("満腹から空腹になるまでの日数")]
    public float fullToEmptyDays = 2f;
    [Tooltip("動いているときの消費の倍率")]
    public float movingMetabolism = 1.5f;
    [Tooltip("空っぽのまま何日で餓死するか")]
    public float starveDays = 1f;
    [Tooltip("餌を1回持ち帰るときの、配る元手の量")]
    public float carryLoad = 1f;

    [Header("口移し（栄養交換）")]
    [Tooltip("触れ合ったとみなす距離（cm）")]
    public float contactRange = 0.8f;
    [Tooltip("これ以上の差があれば分ける")]
    public float shareThreshold = 0.2f;
    [Tooltip("分け終わるまでの時間（ゲーム内秒）")]
    public float shareSeconds = 2f;
    [Tooltip("これより空腹なら仲間にねだる")]
    public float begThreshold = 0.6f;
    [Tooltip("近くのアリを探すときのマスの大きさ（cm）")]
    public float neighborCellSize = 2f;

    [Header("反応閾値モデル")]
    [Tooltip("この日齢で閾値が下限に近づく")]
    public float matureDays = 20f;
    [Tooltip("外へ出るたびに閾値が下がる量")]
    public float thetaLearn = 0.05f;
    [Tooltip("外へ出なかったときに閾値が戻る量")]
    public float thetaForget = 0.01f;
    [Range(0f, 1f)] public float thetaMin = 0.05f;
    [Range(0f, 1f)] public float thetaMax = 0.95f;
    [Tooltip("入口付近の道しるべが採餌刺激に加わる強さ")]
    public float trailStimulusWeight = 0.5f;

    [Header("餌")]
    [Tooltip("1日あたりに現れる餌の平均個数")]
    public float foodPerDay = 1f;
    [Tooltip("餌1個の残量（アリ何匹分か）")]
    public int foodAmountMin = 3;
    public int foodAmountMax = 8;
    [Tooltip("巣の入口からこれだけ離れた場所に出す（cm）")]
    public float foodMinDistanceFromEntrance = 5f;
    [Tooltip("開始時に置いておく餌の数")]
    public int initialFoodCount = 1;

    [Header("デバッグ表示")]
    [Tooltip("重ね表示を作り直す間隔（秒）")]
    public float overlayRefreshInterval = 0.25f;
    [Tooltip("F4 の塗りモードで1回に置く道しるべの量")]
    public float debugPaintAmount = 2f;

    /// <summary>半減期から、1秒あたりの減り方（λ）を求める。</summary>
    public static float HalfLifeToLambda(float halfLife)
    {
        if (halfLife <= 0f) return 0f;
        return Mathf.Log(2f) / halfLife;
    }
}
