using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// **ラウンドの途中で起きるイベントの種類。**
///
/// イベントを増やすときは、ここに1つ足して、
/// <see cref="SpaceJunkEvents"/> に名前と説明の初期値を、<c>SpaceJunkRoundEvents.cs</c> に中身を書く。
/// 画面に出す名前は、<see cref="SpaceJunkEventEntry.displayName"/> で自由に変えられる。
/// </summary>
public enum SpaceJunkEventKind
{
    /// <summary>イベントなし（候補に入れると「今回は何も起きない」の当たりになる）</summary>
    None = 0,

    /// <summary>
    /// **デブリセット**（2026/9/29・大槻さん）。
    /// チームごとにデブリ4〜6個のお題が出て、そのうち3個をゴールに入れると10点。次のお題に変わる。
    /// 順番どおりか順不同かは設定で切り替える（初期値は順不同）
    /// </summary>
    DebrisSet = 1,

    /// <summary>
    /// **期間限定高価値デブリ**（2026/9/29・大槻さん）。
    /// デブリの種類が1つ選ばれ、制限時間のあいだ、その種類を入れると +2点
    /// </summary>
    HighValueDebris = 2,

    /// <summary>
    /// **特殊デブリ**（2026/9/29・大槻さん）。
    /// 重さを決めた大きなデブリがいくつか出る。投げられず、引きずることしかできない。
    /// ゴールに入れると高い得点
    /// </summary>
    HeavyDebris = 3
}

/// <summary>イベントの名前と説明の初期値。**候補の一覧で名前・説明を空にしたときに使う。**</summary>
public static class SpaceJunkEvents
{
    /// <summary>イベントの名前の初期値。</summary>
    public static string DefaultName(SpaceJunkEventKind kind)
    {
        switch (kind)
        {
            case SpaceJunkEventKind.DebrisSet: return "デブリセット";
            case SpaceJunkEventKind.HighValueDebris: return "期間限定高価値デブリ";
            case SpaceJunkEventKind.HeavyDebris: return "特殊デブリ";
            default: return "なし";
        }
    }

    /// <summary>イベントが始まったときに出す、一言の説明の初期値。</summary>
    public static string DefaultDescription(SpaceJunkEventKind kind, SpaceJunkEventSettings settings)
    {
        switch (kind)
        {
            case SpaceJunkEventKind.DebrisSet:
                return settings.debrisSetOrdered
                    ? $"お題のデブリを先頭から順番に {settings.debrisSetGoal} 個入れると {settings.debrisSetBonus} 点！"
                    : $"お題のデブリをどれでも {settings.debrisSetGoal} 個入れると {settings.debrisSetBonus} 点！（順番は自由）";
            case SpaceJunkEventKind.HighValueDebris:
                return $"選ばれた種類のデブリが、時間内だけ +{settings.highValueBonus} 点！";
            case SpaceJunkEventKind.HeavyDebris:
                return settings.heavyWeightBonus
                    ? $"重い特殊デブリが出現！　引きずってゴールへ入れると {settings.heavyPoints} 点〜、重いほど高得点！（投げられない）"
                    : $"重い特殊デブリが出現！　引きずってゴールへ入れると {settings.heavyPoints} 点！（投げられない）";
            default:
                return string.Empty;
        }
    }
}

/// <summary>
/// **イベントの候補の1行ぶん。** どのイベントか・画面に出す名前・説明・続く時間・選ばれやすさ。
/// </summary>
[System.Serializable]
public class SpaceJunkEventEntry
{
    [Tooltip("どのイベントか")]
    public SpaceJunkEventKind kind = SpaceJunkEventKind.DebrisSet;

    [Tooltip("画面に出す名前。**空なら初期の名前**を使う")]
    public string displayName = "";

    [Tooltip("始まったときに出す一言の説明。**空なら初期の説明**を使う")]
    public string description = "";

    [Tooltip("イベントが続く秒数。**0 ならラウンドの終わりまで続く**")]
    [Min(0f)]
    public float durationSeconds = 0f;

    [Tooltip("選ばれやすさ。ほかの候補と比べた比で決まる（1 と 2 なら、2 のほうが2倍選ばれやすい）。0 なら選ばれない")]
    [Min(0)]
    public int weight = 1;

    public SpaceJunkEventEntry()
    {
    }

    public SpaceJunkEventEntry(SpaceJunkEventKind kind, string displayName, float durationSeconds)
    {
        this.kind = kind;
        this.displayName = displayName;
        this.durationSeconds = durationSeconds;
    }

    /// <summary>画面に出す名前（空なら初期の名前）。</summary>
    public string NameOrDefault =>
        string.IsNullOrWhiteSpace(displayName) ? SpaceJunkEvents.DefaultName(kind) : displayName;

    /// <summary>画面に出す説明（空なら初期の説明）。</summary>
    public string DescriptionOrDefault(SpaceJunkEventSettings settings) =>
        string.IsNullOrWhiteSpace(description) ? SpaceJunkEvents.DefaultDescription(kind, settings) : description;
}

/// <summary>
/// **イベントの設定。** <see cref="SpaceJunkSession"/>（プレハブ）のインスペクターで変える。
/// ホストの値が使われる（名前と説明も、ホストの値が全員へ配られる）。
/// </summary>
[System.Serializable]
public class SpaceJunkEventSettings
{
    [Tooltip("ラウンドが始まってから、何秒後にイベントを始めるか。" +
             "**ラウンドの最大時間より長いと、イベントは起きない**")]
    [Min(0f)]
    public float startSeconds = 20f;

    [Tooltip("起きるイベントの候補。**選ばれやすさ（Weight）の比で1つをランダムに選ぶ。**空ならイベントは起きない。" +
             "名前（Display Name）を変えると、画面に出る名前が変わる")]
    public List<SpaceJunkEventEntry> pool = new List<SpaceJunkEventEntry>
    {
        new SpaceJunkEventEntry(SpaceJunkEventKind.DebrisSet, "デブリセット", 0f),
        new SpaceJunkEventEntry(SpaceJunkEventKind.HighValueDebris, "期間限定高価値デブリ", 15f),
        new SpaceJunkEventEntry(SpaceJunkEventKind.HeavyDebris, "特殊デブリ", 0f),
    };

    [Header("デバッグ用")]
    [Tooltip("**デバッグ用。** None 以外にすると、抽選せずに**毎回そのイベントを出す**（Pool の選ばれやすさは無視）。\n" +
             "Pool にその種類の行があれば、その行の名前・説明・続く秒数を使う。無ければ初期の名前で、ラウンドの終わりまで続く。\n" +
             "**確かめ終わったら None に戻すこと**（戻し忘れに気づけるよう、ホストの Console に警告が出る）")]
    public SpaceJunkEventKind debugForceEvent = SpaceJunkEventKind.None;

    [Header("デブリセット")]
    [Tooltip("OFF＝順番は関係なく、お題に入っているデブリなら進む（初期値）。\n" +
             "ON＝**お題の先頭から順番どおりに入れたときだけ進む**（違う種類を入れても進まない。戻りもしない）")]
    public bool debrisSetOrdered = false;

    [Tooltip("お題のデブリのいちばん少ない数。**個数はイベントの始まりに1回だけ決め、全チーム同じ**（中身はチームごとにばらばら）")]
    [Min(1)]
    public int debrisSetMinSize = 4;

    [Tooltip("お題のデブリのいちばん多い数（8個まで）")]
    [Min(1)]
    public int debrisSetMaxSize = 6;

    [Tooltip("お題のうち、何個ゴールに入れたらそろったことになるか（お題の個数より多くはならない）")]
    [Min(1)]
    public int debrisSetGoal = 3;

    [Tooltip("そろえたときのボーナス")]
    [Min(0)]
    public int debrisSetBonus = 10;

    [Header("期間限定高価値デブリ")]
    [Tooltip("始まったときにデブリの種類が1つランダムに選ばれ、続く秒数のあいだ、その種類を1個入れるごとに **1点とは別に入る得点**")]
    [Min(0)]
    public int highValueBonus = 2;

    [Header("特殊デブリ（重いデブリ）")]
    [Tooltip("**イベントが始まったときに出す、重いデブリの数。** 1回のイベントで出るのはこの数だけ（途中で増えない）")]
    [Min(1)]
    [FormerlySerializedAs("heavyCount")]
    public int heavyInitialCount = 3;

    [Tooltip("重さのいちばん軽い値（X）。**1個ずつ、X〜Y の間からランダムに決まる**（小数第1位まで）。\n" +
             "X と Y を同じにすると、全部その重さになる。\n" +
             "重さ1が基準：ゲージのど真ん中で、引っ張る強さ（初期値6m）ぶん引きずれる。重いほど引きずれる距離が短く（重さで割る）、" +
             "ゲージが速く、見た目が大きい。ゲージの速さと引きずる距離の細かい値は、プレイヤーの ThrowController の「重い物」の欄")]
    [Min(0.1f)]
    public float heavyWeightMin = 1f;

    [Tooltip("重さのいちばん重い値（Y）。X より小さくしたときは、X と Y を入れ替えて使う")]
    [Min(0.1f)]
    public float heavyWeightMax = 3f;

    /// <summary>重さを X〜Y の間からランダムに1つ決める（小数第1位まで）。X と Y が同じなら、その値。</summary>
    public float PickHeavyWeight()
    {
        float low = Mathf.Max(0.1f, Mathf.Min(heavyWeightMin, heavyWeightMax));
        float high = Mathf.Max(0.1f, Mathf.Max(heavyWeightMin, heavyWeightMax));

        if (Mathf.Approximately(low, high))
        {
            return low;
        }

        return Mathf.Clamp(Mathf.Round(Random.Range(low, high) * 10f) / 10f, low, high);
    }

    [Tooltip("重いデブリを1個入れたときの得点（ふつうは1点。**これがそのまま入る**）。" +
             "下の「重さボーナス」が OFF なら、重さに関係なくこの点")]
    [Min(1)]
    public int heavyPoints = 20;

    [Tooltip("**重さボーナスを付けるか。** ON にすると、重いほど得点が上がる：\n" +
             "得点 ＝ 上の得点 ＋ 重さ1あたりのボーナス ×（重さ − 1）（小数は四捨五入）\n" +
             "例：得点20・ボーナス10 なら、重さ1＝20点、重さ2＝30点、重さ2.5＝35点、重さ3＝40点")]
    public bool heavyWeightBonus = false;

    [Tooltip("重さボーナスが ON のとき、重さが1増えるごとに足す得点")]
    [Min(0)]
    public int heavyBonusPerWeight = 10;

    /// <summary>その重さの重いデブリを1個入れたときの得点（重さボーナスが OFF なら、重さに関係なく同じ）。</summary>
    public int HeavyPointsFor(float weight)
    {
        int points = heavyPoints;

        if (heavyWeightBonus)
        {
            points += Mathf.RoundToInt(heavyBonusPerWeight * Mathf.Max(0f, weight - 1f));
        }

        return Mathf.Max(1, points);
    }

    [Tooltip("重さ1のときの大きさ（ふつうのデブリの何倍か）")]
    [Min(0.1f)]
    public float heavyBaseScale = 1.6f;

    [Tooltip("重さが1増えるごとに、どれだけ大きくするか（0.3 なら重さ2で 1.3倍、重さ3で 1.6倍。上の大きさに掛ける）")]
    [Min(0f)]
    public float heavyScalePerWeight = 0.3f;

    [Tooltip("重いデブリの色")]
    public Color heavyColor = new Color(0.35f, 0.85f, 1f);

    /// <summary>その重さのときの見た目の大きさ（ふつうのデブリの何倍か）。</summary>
    public float HeavyScale(float weight)
    {
        return heavyBaseScale * (1f + heavyScalePerWeight * Mathf.Max(0f, weight - 1f));
    }

    /// <summary>
    /// 候補から、選ばれやすさの比で1つ選ぶ。選べなければ null。
    /// **デバッグ用の固定（<see cref="debugForceEvent"/>）が入っていれば、抽選せずにそれを返す。**
    /// </summary>
    public SpaceJunkEventEntry PickRandom()
    {
        if (debugForceEvent != SpaceJunkEventKind.None)
        {
            Debug.LogWarning($"[JUNK] デバッグ用の固定で、イベント「{SpaceJunkEvents.DefaultName(debugForceEvent)}」を出します。" +
                             "確かめ終わったら SpaceJunkSession の Debug Force Event を None に戻してください。");

            return FindForcedEntry();
        }

        if (pool == null)
        {
            return null;
        }

        int total = TotalWeight();
        if (total <= 0)
        {
            return null;
        }

        // 0〜合計 の中から1つ引き、候補の選ばれやすさを順に引いていって、0 を下回ったところの候補にする
        // （選ばれやすさが大きい候補ほど、引いた数がその候補の範囲に入りやすい）
        int roll = Random.Range(0, total);

        foreach (SpaceJunkEventEntry entry in pool)
        {
            if (entry == null)
            {
                continue;
            }

            roll -= Mathf.Max(0, entry.weight);
            if (roll < 0)
            {
                return entry;
            }
        }

        return null;
    }

    /// <summary>
    /// デバッグ用の固定イベント（<see cref="debugForceEvent"/>）の行。
    /// Pool にその種類の行があればその行（名前・説明・続く秒数を使う）。無ければ、初期の名前でラウンドの終わりまで続く行を作る。
    /// </summary>
    private SpaceJunkEventEntry FindForcedEntry()
    {
        if (pool != null)
        {
            foreach (SpaceJunkEventEntry entry in pool)
            {
                if (entry != null && entry.kind == debugForceEvent)
                {
                    return entry;
                }
            }
        }

        return new SpaceJunkEventEntry(debugForceEvent, string.Empty, 0f);
    }

    /// <summary>候補の選ばれやすさ（Weight）の合計。空の行は数えない。</summary>
    private int TotalWeight()
    {
        int total = 0;
        foreach (SpaceJunkEventEntry entry in pool)
        {
            if (entry != null)
            {
                total += Mathf.Max(0, entry.weight);
            }
        }

        return total;
    }
}
