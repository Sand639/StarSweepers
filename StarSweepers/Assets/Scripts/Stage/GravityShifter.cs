using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// **時間が経つと、重力がひとりでに変わる。**
///
/// 決めた間隔ごとに、**用意しておいた候補の中からランダムに**新しい重力を選ぶ。
/// 月のように軽くなったり、いつもより重くなったりする。
///
/// | 段階 | 何が起きるか |
/// | --- | --- |
/// | 予告 | 「重力が変わる！」が出る（**身構える時間**） |
/// | 変化 | 決めた秒数をかけて、**じわじわ**変わる |
/// | しばらく | 次の予告まで、その重力のまま |
///
/// ## 使い方
///
/// **空のゲームオブジェクトに付けるだけ。** つなぐものは無い。
///
/// ## 軽くなると、どうなるか
///
/// **ジャンプの踏み切りの強さは変わらない。** 変わるのは落ちる速さだけ。
/// だから重力が弱いと**高く跳んで、ゆっくり落ちてくる。**
/// 「月面」らしさが出るのはこのため。
///
/// 中身は <see cref="WorldGravity"/> に入る。
///
/// ## オンラインでも全員そろう
///
/// **何番目にどの重力になるかは、「時計」から計算で決めている。**
/// 同じ時刻なら必ず同じ重力になるので、オンラインでは全員で共有している時計
/// （Netcode の ServerTime）を読むだけで、**通信で送らなくても全員の画面でそろう。**
/// そのため NetworkObject を付ける必要はない。1人用のシーンにもそのまま置ける
/// （つながっていないときは、そのPCの時計を使う）。
///
/// 動く障害物（<see cref="MovingObstacle"/>）と同じ考え方である。
/// **途中から参加した人も、いまの重力と次の予告にそのまま合流できる。**
/// </summary>
[DisallowMultipleComponent]
public class GravityShifter : MonoBehaviour
{
    [Header("重力の候補")]
    [Tooltip("**この中からランダムに1つ選ばれる。** 正の数で入れる。\n" +
             "月は約1.6、火星は約3.7、地球は9.81。\n" +
             "**同じ値を2つ入れれば、その重力が出やすくなる**")]
    [SerializeField]
    private List<float> gravityChoices = new List<float>
    {
        1.6f,   // 月
        3.7f,   // 火星
        6.0f,   // 少し軽い
        9.81f,  // 地球
        14.0f,  // 重い
    };

    [Tooltip("**同じ重力が続けて選ばれないようにする。** OFFだと「変わったのに何も変わらない」ことがある")]
    [SerializeField] private bool avoidSameTwice = true;

    [Header("いつ変わるか")]
    [Tooltip("次に変わるまでの、一番短い秒数")]
    [Min(0.5f)]
    [SerializeField] private float shortestInterval = 8f;

    [Tooltip("次に変わるまでの、一番長い秒数")]
    [Min(0.5f)]
    [SerializeField] private float longestInterval = 14f;

    [Tooltip("**変わる前に予告を出す秒数。** 0にすると予告なし")]
    [Min(0f)]
    [SerializeField] private float warningSeconds = 2f;

    [Tooltip("**変わりきるまでの秒数。** 0にすると一瞬で変わる（急に落ちるので危ない）")]
    [Min(0f)]
    [SerializeField] private float changeSeconds = 1.5f;

    [Header("その他")]
    [Tooltip("始めるときの重力。正の数で入れる")]
    [Min(0.1f)]
    [SerializeField] private float startGravity = 9.81f;

    [Tooltip("**並び順を決めるたね。** 同じ数字なら毎回同じ順番で変わる。\n" +
             "オンラインでは全員がこの数字を使うので、**ステージごとに1つ決めておけばよい**")]
    [SerializeField] private int scheduleSeed = 12345;

    [Tooltip("変わるたびにコンソールへ書き出す")]
    [SerializeField] private bool logChanges = true;

    /// <summary>いま動いている重力係。画面に出すときに見に来る。</summary>
    public static GravityShifter Current { get; private set; }

    /// <summary>**次に変わるまでの残り秒数。**</summary>
    public float NextChangeIn { get; private set; }

    /// <summary>**もうすぐ変わる**（予告の最中）か。</summary>
    public bool IsWarning => NextChangeIn <= warningSeconds && !IsChanging;

    /// <summary>いま、じわじわ変わっている最中か。</summary>
    public bool IsChanging { get; private set; }

    /// <summary>いまの重力の強さ（正の数）。画面に出すため。</summary>
    public float CurrentStrength => Mathf.Abs(WorldGravity.Value);

    /// <summary>
    /// **いまの重力を、ふつうと比べてどう言うか。**
    /// 「軽い」「ふつう」「重い」の3つ。
    /// </summary>
    public string StrengthLabel
    {
        get
        {
            // Scale は「ふつう（地球）の何倍か」。0.75倍未満なら軽い、1.25倍を超えたら重い
            float scale = WorldGravity.Scale;

            if (scale < 0.75f)
            {
                return "軽い";
            }

            return scale > 1.25f ? "重い" : "ふつう";
        }
    }

    // ---- いま何回目の変化の中にいるか（時計から計算する） ----
    // 「何回目か」と「その回が始まった時刻」を覚えておき、時刻が進んだぶんだけ先へ進める。
    // 時計が飛んだとき（オンラインに入った直後など）は、最初から数え直す

    /// <summary>いま何回目の変化の中にいるか。-1 は「まだ数えていない」（次の Update で数え直す）。</summary>
    private int roundIndex = -1;

    /// <summary>この回が始まった時刻（秒。<see cref="SharedTime"/> の時計で数える）。</summary>
    private double roundStartTime;

    /// <summary>この回の長さ（秒）。この時間が過ぎたら次の回へ進む。</summary>
    private float roundDuration;

    /// <summary>この回の重力（正の数）。</summary>
    private float roundGravity;

    /// <summary>ひとつ前の回の重力（正の数）。変わりきるまでの間、ここから寄せていく。</summary>
    private float previousGravity;

    /// <summary>数え直しが暴走しないための上限。1回およそ10秒なので、これで何時間ぶんも足りる。</summary>
    private const int MaxRoundsToWalk = 100000;

    // 選ぶときの作業用。毎回作り直さないよう、使い回している
    private readonly List<float> candidates = new List<float>();

    private void Awake()
    {
        // シーンに1つだけ置く前提。2つ置くと、あとから起きたほうが画面に出るうえ、
        // 両方が毎フレーム重力を書き換えて取り合いになる
        Current = this;
    }

    private void OnEnable()
    {
        // WorldGravity は下向きでマイナスの値なので、正の数で入れた値を下向きに直して入れる
        WorldGravity.Set(-Mathf.Abs(startGravity));

        // 次に Update が来たときに、時計から数え直す
        roundIndex = -1;
    }

    private void OnDisable()
    {
        // **付けっぱなしで次のシーンへ行かないように、必ず戻す**
        WorldGravity.Release();

        if (Current == this)
        {
            Current = null;
        }
    }

    private void Update()
    {
        if (GamePause.IsPaused)
        {
            return;
        }

        double now = SharedTime;

        AdvanceToTime(now);
        ApplyGravityAt(now);
    }

    /// <summary>
    /// **全員で共有している時計。** オンラインでつながっていれば ServerTime、
    /// そうでなければこのPCの時計を使う。
    ///
    /// ※ ポーズ中は、1人用ならこのPCの時計も止まる（`Time.timeAsDouble` は止めた影響を受けるため）。
    /// </summary>
    private static double SharedTime
    {
        get
        {
            NetworkManager network = NetworkManager.Singleton;

            if (network != null && network.IsListening)
            {
                return network.ServerTime.Time;
            }

            return Time.timeAsDouble;
        }
    }

    /// <summary>
    /// 時刻に合わせて「何回目の変化の中にいるか」を進める。
    ///
    /// **1回目は 0 から数え直す。** 途中から参加した人も、こうすれば同じ答えにたどり着く。
    /// </summary>
    private void AdvanceToTime(double now)
    {
        // まだ数えていない／時計が巻き戻った（別の試合が始まった等）ときは最初から。
        // **0回目は「始めるときの重力」のまま**。最初の変化は1回目から
        if (roundIndex < 0 || now < roundStartTime)
        {
            // 時計の 0秒を「0回目の始まり」とする。全員が同じ 0秒から数えるので、答えがそろう
            roundIndex = 0;
            roundStartTime = 0d;
            previousGravity = Mathf.Abs(startGravity);
            roundGravity = previousGravity;
            roundDuration = IntervalFor(0);
        }

        int startedAt = roundIndex;
        int walked = 0;

        // いまの時刻を含む回にたどり着くまで、1回ずつ先へ進める。
        // ふだんは1フレームに0回か1回。途中から参加した直後だけ、まとめて何回も進む
        while (now >= roundStartTime + roundDuration && walked < MaxRoundsToWalk)
        {
            roundStartTime += roundDuration;
            roundIndex++;
            walked++;

            previousGravity = roundGravity;
            roundGravity = PickGravityFor(roundIndex, previousGravity);
            roundDuration = IntervalFor(roundIndex);
        }

        // まとめて何回も進んだときは、最後の1回だけを書き出す
        if (logChanges && roundIndex != startedAt)
        {
            Debug.Log($"[GRAVITY] 重力が変わる：{previousGravity:0.0} → {roundGravity:0.0}（{roundIndex}回目）", this);
        }
    }

    /// <summary>いまの時刻の重力を決めて、実際に入れる。</summary>
    private void ApplyGravityAt(double now)
    {
        float sinceRoundStart = (float)(now - roundStartTime);
        float duration = Mathf.Max(0f, changeSeconds);

        // 回の始まりから changeSeconds の間が「じわじわ変わっている最中」
        IsChanging = sinceRoundStart < duration;

        if (IsChanging)
        {
            // 変わり具合。0 で前の回の重力、1 でこの回の重力
            float t = duration <= 0f ? 1f : Mathf.Clamp01(sinceRoundStart / duration);

            // 始めと終わりをゆるやかにする（急に体が重くなると操作しづらい）
            // （ここで持っている重力は正の数。WorldGravity は下向きでマイナスなので、符号を変えて入れる）
            WorldGravity.Set(-Mathf.Lerp(previousGravity, roundGravity, Mathf.SmoothStep(0f, 1f, t)));
        }
        else
        {
            WorldGravity.Set(-roundGravity);
        }

        NextChangeIn = Mathf.Max(0f, (float)(roundStartTime + roundDuration - now));
    }

    /// <summary>
    /// **次の重力を、すぐに変え始める。**
    /// 時計から決める作りになったので、**いまの回を早送りして次へ送る。**
    ///
    /// ※ 早送りするのは**呼んだPCだけ**なので、オンラインで呼ぶと、そのPCだけ予定がずれて
    /// 他の人と重力がそろわなくなる（2026/10/20 時点では、コードからは呼ばれていない）。
    /// </summary>
    public void BeginChange()
    {
        double now = SharedTime;

        // いまの回を「もう終わった」ことにして、次の回へ進める
        roundStartTime = now - roundDuration;

        AdvanceToTime(now);
        ApplyGravityAt(now);
    }

    /// <summary>
    /// **何回目かに合わせて、候補の中から1つ選ぶ。** 返すのは正の数。
    ///
    /// **同じ「たね」と同じ回数なら、どのPCでも必ず同じ答えになる**ので、
    /// オンラインでも全員の重力がそろう。
    ///
    /// 候補が空だったり、全部が今と同じ値だったりしても止まらないようにしてある。
    /// <paramref name="currentGravity"/> は、ひとつ前の回の重力（正の数）。
    /// </summary>
    private float PickGravityFor(int round, float currentGravity)
    {
        candidates.Clear();

        if (gravityChoices != null)
        {
            for (int i = 0; i < gravityChoices.Count; i++)
            {
                float value = Mathf.Abs(gravityChoices[i]);

                // 0は「重力なし」で戻ってこられなくなるので使わない
                if (value < 0.01f)
                {
                    continue;
                }

                // 同じ値が続くと「変わったのに何も起きない」になる
                if (avoidSameTwice && Mathf.Approximately(value, currentGravity))
                {
                    continue;
                }

                candidates.Add(value);
            }
        }

        if (candidates.Count > 0)
        {
            // 0以上1未満の数に候補の数を掛けて、何番目を使うか決める（salt 1 ＝ 重力用）。
            // Clamp は念のため（Hash01 は 1 にならないので、本来ははみ出さない）
            int index = Mathf.Clamp((int)(Hash01(round, 1) * candidates.Count), 0, candidates.Count - 1);
            return candidates[index];
        }

        // 候補が1つしか無い（＝いまと同じ）なら、そのまま続ける
        // （候補が全部 0 だったときも、ここに来る）
        if (gravityChoices != null && gravityChoices.Count > 0)
        {
            return currentGravity;
        }

        Debug.LogWarning($"{name}: 重力の候補が1つも入っていません。ふつうの重力のままにします。", this);

        return Mathf.Abs(WorldGravity.Standard);
    }

    /// <summary>
    /// **何回目かに合わせた、次までの秒数。**
    /// こちらも「たね」と回数だけで決まるので、どのPCでも同じ長さになる。
    /// </summary>
    private float IntervalFor(int round)
    {
        // 短いほう・長いほうを逆に入れられても動くように、小さいほうを「短い」とする
        float shortest = Mathf.Min(shortestInterval, longestInterval);
        float longest = Mathf.Max(shortestInterval, longestInterval);

        // salt 0 ＝ 間隔用
        float interval = Mathf.Lerp(shortest, longest, Hash01(round, 0));

        // 予告の時間と、変わりきるまでの時間は、この中に収める
        return Mathf.Max(Mathf.Max(warningSeconds, changeSeconds), interval);
    }

    /// <summary>
    /// **たねと回数から、0以上1未満の決まった数を作る。**
    ///
    /// `Random` を使うとPCごとに違う答えになってしまうので、
    /// **計算だけで決まる形**にしてある（同じ入力なら必ず同じ答え）。
    /// `salt` は「間隔用」「重力用」で別の数を出すための区別。
    /// </summary>
    private float Hash01(int round, int salt)
    {
        // unchecked … 掛け算で数があふれても、エラーにせずそのまま回り込ませる（ハッシュではそれが正しい動き）
        unchecked
        {
            // 3つの数に、それぞれ大きな数を掛けて混ぜる。
            // 数そのものに意味は無い（ハッシュでよく使われる値）。**変えると、全ステージの重力の並び順が変わる**
            uint h = (uint)(scheduleSeed * 73856093) ^ (uint)(round * 19349663) ^ (uint)(salt * 83492791);

            // ビットをずらして混ぜ直し、近い入力（1回目と2回目など）でも答えが大きく変わるようにする
            h ^= h >> 13;
            h *= 1274126177u;
            h ^= h >> 16;

            // 下の 24ビット（0〜16777215）だけを使い、2の24乗（16777216）で割って 0以上1未満にする。
            // 24ビットにしているのは、float がちょうど誤差なく表せる幅だから
            return (h & 0xFFFFFF) / (float)0x1000000;
        }
    }
}
