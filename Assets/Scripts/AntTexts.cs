/// <summary>アリの仕事。段階3で反応閾値モデルから決まるようになる。</summary>
public enum AntTask
{
    /// <summary>うろついている（段階2の仮の状態）</summary>
    Wander,
    /// <summary>落ちている</summary>
    Fall,
    /// <summary>休んでいる</summary>
    Rest,
    /// <summary>巣の中にいる（行動モデル.md 6章）</summary>
    RestInNest,
    /// <summary>外を探している</summary>
    Explore,
    /// <summary>餌を持って帰っている</summary>
    ReturnWithFood,
    /// <summary>空手で帰っている</summary>
    ReturnEmpty,
    /// <summary>巣を掘り広げている（行動モデル.md 12章）</summary>
    Dig,
    /// <summary>掘った土を外へ運び出している</summary>
    CarrySoilOut,
    /// <summary>採餌</summary>
    Forage,
    /// <summary>幼虫の世話</summary>
    Nurse,
    /// <summary>見張り</summary>
    Guard,
}

/// <summary>アリが死んだ理由。段階6で寒さ・溺れ・崩落が加わる。</summary>
public enum AntDeathCause
{
    /// <summary>餓死</summary>
    Starvation,
}

/// <summary>アリの種類（カースト）。</summary>
public enum AntCaste
{
    Queen,
    WorkerMajor,
    WorkerMinor,
}

/// <summary>今いる場所。土のマスの形から決まる。</summary>
public enum AntPlace
{
    Surface,
    Tunnel,
    Chamber,
}

/// <summary>運んでいるもの。</summary>
public enum AntCarry
{
    None,
    Food,
    Soil,
    Egg,
    Larva,
    Corpse,
}

/// <summary>気持ち。段階3で内部状態から選ぶ。</summary>
public enum AntMood
{
    /// <summary>特に何もない</summary>
    Calm,
    /// <summary>空腹度が高い</summary>
    Hungry,
    /// <summary>道しるべの匂いが途切れた</summary>
    TrailLost,
    /// <summary>巣から遠い</summary>
    FarFromNest,
    /// <summary>警報フェロモンが濃い</summary>
    Alarmed,
    /// <summary>餌を運搬中</summary>
    CarryingFood,
    /// <summary>寒い</summary>
    Cold,
    /// <summary>道しるべをたどっている</summary>
    FollowingTrail,
    /// <summary>餌を探している</summary>
    Searching,
    /// <summary>栄養交換中</summary>
    Sharing,
    /// <summary>掘っている</summary>
    Digging,
    /// <summary>掘った土を外へ運んでいる</summary>
    CarryingSoil,
}

/// <summary>
/// 画面に出るアリ関連の文言をまとめた表。
/// 文言を変えたいときは、このファイルだけを見ればよい。
/// 段階3で形が固まったら ScriptableObject にすることを検討する。
/// </summary>
public static class AntTexts
{
    // ---- 情報パネルの見出し ----
    public const string HeadingStatus = "いまの状況";
    public const string HeadingNature = "性質";
    public const string HeadingMood = "気持ち";

    public const string LabelTask = "仕事";
    public const string LabelPlace = "場所";
    public const string LabelCarry = "運んでいるもの";
    public const string LabelCaste = "種類";
    public const string LabelAge = "日齢";
    public const string LabelRole = "役割の傾向";

    public const string RoleIndoor = "内勤寄り";
    public const string RoleOutdoor = "外勤寄り";

    /// <summary>日齢の表し方。</summary>
    public static string AgeInDays(int days)
    {
        return days + "日";
    }

    /// <summary>仕事の名前。</summary>
    public static string Task(AntTask task)
    {
        switch (task)
        {
            case AntTask.Wander: return "うろついている";
            case AntTask.Fall: return "落ちている";
            case AntTask.Rest: return "休んでいる";
            case AntTask.RestInNest: return "巣の中にいる";
            case AntTask.Explore: return "餌を探している";
            case AntTask.ReturnWithFood: return "餌を持ち帰っている";
            case AntTask.ReturnEmpty: return "巣へ戻っている";
            case AntTask.Dig: return "掘っている";
            case AntTask.CarrySoilOut: return "土を運び出している";
            case AntTask.Forage: return "採餌中";
            case AntTask.Nurse: return "幼虫の世話";
            case AntTask.Guard: return "見張り";
            default: return "－";
        }
    }

    /// <summary>場所の名前。</summary>
    public static string Place(AntPlace place)
    {
        switch (place)
        {
            case AntPlace.Surface: return "地表";
            case AntPlace.Tunnel: return "トンネル";
            case AntPlace.Chamber: return "部屋";
            default: return "－";
        }
    }

    /// <summary>運んでいるものの名前。</summary>
    public static string Carry(AntCarry carry)
    {
        switch (carry)
        {
            case AntCarry.None: return "なし";
            case AntCarry.Food: return "餌";
            case AntCarry.Soil: return "土";
            case AntCarry.Egg: return "卵";
            case AntCarry.Larva: return "幼虫";
            case AntCarry.Corpse: return "仲間の死骸";
            default: return "－";
        }
    }

    /// <summary>死んだ理由の名前。</summary>
    public static string DeathCause(AntDeathCause cause)
    {
        switch (cause)
        {
            case AntDeathCause.Starvation: return "餓死";
            default: return "－";
        }
    }

    /// <summary>種類の名前。</summary>
    public static string Caste(AntCaste caste)
    {
        switch (caste)
        {
            case AntCaste.Queen: return "女王";
            case AntCaste.WorkerMajor: return "働きアリ（大型）";
            case AntCaste.WorkerMinor: return "働きアリ（小型）";
            default: return "－";
        }
    }

    /// <summary>気持ちの一文。仕様8章の表をそのまま持つ。</summary>
    public static string Mood(AntMood mood)
    {
        switch (mood)
        {
            case AntMood.Hungry: return "お腹がすいた。誰かに分けてもらいたい";
            case AntMood.TrailLost: return "匂いが途切れた。引き返したい";
            case AntMood.FarFromNest: return "巣の匂いが薄い。戻りたい";
            case AntMood.Alarmed: return "何かがおかしい。落ち着かない";
            case AntMood.CarryingFood: return "これを巣へ持ち帰る";
            case AntMood.Cold: return "寒い。深いところへ行きたい";
            case AntMood.FollowingTrail: return "匂いをたどっている";
            case AntMood.Searching: return "餌を探している";
            case AntMood.Sharing: return "分け合っている";
            case AntMood.Digging: return "ここを広げる";
            case AntMood.CarryingSoil: return "土を外へ運ぶ";
            case AntMood.Calm: return "今は穏やかだ";
            default: return string.Empty;
        }
    }
}
