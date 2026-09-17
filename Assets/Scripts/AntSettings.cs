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
    [Tooltip("これだけ外にいて何も見つからなければ空手で帰る（秒。フェロモンの半減期と同じ時計）")]
    public float giveUpSeconds = 90f;
    [Tooltip("匂いが途切れたときに小さく円を描く旋回の速さ（度/秒）")]
    public float circleTurnGain = 120f;

    [Header("移動")]
    public float walkSpeed = 2f;
    public float turnSpeed = 540f;
    public float fallSpeed = 5f;

    [Header("社会胃")]
    [Tooltip("開始時の社会胃の中身（この範囲でばらつかせる）。一斉に空腹・一斉に出発になるのを避ける")]
    [Range(0f, 1f)] public float startCropMin = 0.5f;
    [Range(0f, 1f)] public float startCropMax = 1f;
    [Tooltip("満腹から空腹になるまでの日数")]
    public float fullToEmptyDays = 2f;
    [Tooltip("動いているときの消費の倍率")]
    public float movingMetabolism = 1.5f;
    [Tooltip("餌を1回持ち帰るときの、配る元手の量")]
    public float carryLoad = 1f;

    [Header("体の蓄え（脂肪体。行動モデル.md 4章）")]
    [Tooltip("社会胃がこれより満ちている間、余りを体の蓄えへ回す")]
    [Range(0f, 1f)] public float reserveFillAbove = 0.7f;
    [Tooltip("1日あたり、社会胃から体の蓄えへ移す量")]
    public float reserveFillPerDay = 0.5f;
    [Tooltip("満タンの蓄えだけで何日生きられるか。社会胃が空の間はここから引く")]
    public float reserveDays = 14f;
    [Tooltip("開始時と羽化時の蓄え（この範囲でばらつかせる）")]
    [Range(0f, 1f)] public float startReserveMin = 0.3f;
    [Range(0f, 1f)] public float startReserveMax = 0.8f;

    [Header("口移し（栄養交換）")]
    [Tooltip("触れ合ったとみなす距離（cm）")]
    public float contactRange = 0.8f;
    [Tooltip("これ以上の差があれば分ける")]
    public float shareThreshold = 0.2f;
    [Tooltip("分け終わるまでの時間（ゲーム内秒）")]
    public float shareSeconds = 2f;
    [Tooltip("これより空腹なら仲間にねだる")]
    public float begThreshold = 0.6f;
    [Tooltip("ねだる相手を探す範囲（cm）")]
    public float begSearchRange = 6f;
    [Tooltip("近くのアリを探すときのマスの大きさ（cm）")]
    public float neighborCellSize = 2f;

    [Header("興奮（段階6で本格化）")]
    [Tooltip("1秒あたりに興奮が冷める量")]
    public float alarmDecayPerSecond = 0.2f;

    [Header("気持ちの判定（行動モデル.md 7章）")]
    [Tooltip("これより興奮していたら「何かがおかしい」")]
    [Range(0f, 1f)] public float moodAlarmThreshold = 0.5f;
    [Tooltip("これより空腹なら「お腹がすいた」")]
    [Range(0f, 1f)] public float moodHungryThreshold = 0.8f;
    [Tooltip("たどる確率がこれを超えていたら「匂いをたどっている」")]
    [Range(0f, 1f)] public float moodFollowThreshold = 0.5f;

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
    [Tooltip("入口付近の道しるべを見る範囲（マス）")]
    public int trailStimulusRadius = 3;
    [Tooltip("コロニー全体の値（採餌刺激など）を計算し直す間隔（秒）")]
    public float colonyUpdateInterval = 1f;

    [Header("掘削：掘る動機（行動モデル.md 12-1）")]
    [Tooltip("アリ1匹あたり、これだけの空洞マスがあれば足りている。巣の広さの目標を決める")]
    public float targetCellsPerAnt = 6f;
    [Tooltip("目標に対して何割足りないと刺激が最大になるか（0.5＝目標の半分の広さで最大）")]
    [Range(0.05f, 1f)] public float digShortfallRange = 0.5f;
    [Tooltip("掘削の閾値の初期値。日齢に依存しない一定値")]
    [Range(0f, 1f)] public float thetaDigInitial = 0.5f;
    [Tooltip("育児の閾値の初期値。日齢の曲線にするのは段階5b-2")]
    [Range(0f, 1f)] public float thetaNurseInitial = 0.5f;

    [Header("掘削：掘る場所（行動モデル.md 12-3）")]
    [Tooltip("どこでも掘る基礎の確率（知覚tickごと）")]
    [Range(0f, 1f)] public float digBase = 0.02f;
    [Tooltip("掘削跡の匂いが掘る確率を上げる強さ")]
    public float digMarkerGain = 0.08f;
    [Tooltip("まわりの混み具合が掘る確率を上げる強さ")]
    public float digCrowdGain = 0.08f;
    [Tooltip("まわりの混み具合を数える範囲（cm）")]
    public float digCrowdRadius = 1.5f;
    [Tooltip("この匹数で混み具合が最大（1.0）になる")]
    public float digCrowdFull = 5f;
    [Tooltip("1粒掘るのにかかる時間（秒）。その間アリは止まる")]
    public float digSeconds = 3f;
    [Tooltip("掘る場所が見つからないまま歩き続けたら、巣の仕事に戻るまでの時間（秒）")]
    public float digGiveUpSeconds = 20f;

    [Header("掘削：掘削跡の匂い（行動モデル.md 12-4）")]
    [Tooltip("掘った跡が半分になるまでの時間（秒）。短いほど先端だけが光る")]
    public float digHalfLife = 30f;
    [Tooltip("掘削跡は広げない。0 のままにする（広げると先端が埋もれる）")]
    [Range(0f, 1f)] public float digDiffusion = 0f;
    public float digMax = 10f;
    [Tooltip("掘ったマス1つに置く量")]
    public float depositDig = 3f;

    [Header("掘削：土の運び出しと塚（行動モデル.md 12-5）")]
    [Tooltip("入口からこれだけ離れてから土を置く（cm）")]
    public float dumpMinDistance = 5f;
    [Tooltip("隣の列よりこのマス数以上高くなったら、低いほうへ1粒転がす")]
    public int moundMaxStep = 2;
    [Tooltip("1粒が続けて転がれる回数の上限")]
    public int moundMaxRoll = 8;
    [Tooltip("入口の列の左右、このマス数ぶんには積まない")]
    public int entranceKeepClear = 2;
    [Tooltip("外に出てからこれだけ経っても置けなければ、その場で捨てる（秒）")]
    public float dumpGiveUpSeconds = 20f;

    [Header("子どもの匂い（行動モデル.md 13-3）")]
    [Tooltip("子どもの匂いが半分になるまでの時間（ゲーム内秒）")]
    public float broodHalfLife = 20f;
    [Tooltip("子どもの匂いを1回の拡散で隣へ渡す割合の合計")]
    [Range(0f, 1f)] public float broodDiffusion = 0.05f;
    public float broodMax = 10f;
    [Tooltip("幼虫の空腹の匂いが半分になるまでの時間（ゲーム内秒）")]
    public float larvaHungerHalfLife = 10f;
    [Tooltip("幼虫の空腹の匂いを1回の拡散で隣へ渡す割合の合計")]
    [Range(0f, 1f)] public float larvaHungerDiffusion = 0.05f;
    [Tooltip("1マスに乗る濃さの上限")]
    public float larvaHungerMax = 10f;

    [Header("餌")]
    [Tooltip("1日あたりに現れる餌の平均個数")]
    public float foodPerDay = 1f;
    [Tooltip("餌1個の残量（アリ何匹分か）")]
    public int foodAmountMin = 10;
    public int foodAmountMax = 30;
    [Tooltip("餌が現れてから消えるまでの日数。食べ切られなくても乾いて消える")]
    public float foodLifetimeDays = 2f;
    [Tooltip("巣の入口からこれだけ離れた場所に出す（cm）。自動で出るぶんも F5 の手置きも同じ値を見る")]
    public float foodMinDistanceFromEntrance = 5f;
    [Tooltip("餌の最小の直径（cm）。残量が減ってもこれより小さくしない")]
    public float foodMinDiameter = 0.8f;
    [Tooltip("餌の最大の直径（cm）")]
    public float foodMaxDiameter = 1.6f;
    [Tooltip("開始時に置いておく餌の数")]
    public int initialFoodCount = 1;
    [Tooltip("地表に同時に置ける餌の数。これに達しているあいだは新しい餌を出さない。"
        + "foodPerDay × foodLifetimeDays より十分大きくしないと、上限で詰まる")]
    public int maxFoodSources = 10;

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
