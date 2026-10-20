using UnityEngine;

/// <summary>
/// **点数を持っておく場所。** ポケットに物資が入ると、ここに点が足される。
///
/// **将来プレイヤーが増えたときのために、最初から人数分の配列で持っている。**
/// いまは1人ぶんしか使わないが、Player Count を増やせばそのまま複数人に対応できる。
///
/// 表示は <see cref="FishingStatusUI"/> が担当する（ここは数を持つだけ）。
/// </summary>
public class ScoreBoard : MonoBehaviour
{
    [Header("プレイヤー")]
    [Tooltip("点数を数える人数。増やすと、その人数分の点数を別々に持つ")]
    [SerializeField] private int playerCount = 1;

    [Tooltip("画面に出す名前。人数より少なければ「プレイヤーN」が使われる")]
    [SerializeField] private string[] playerNames = { "プレイヤー1" };

    private int[] scores;

    /// <summary>点数を数えている人数。</summary>
    public int PlayerCount => scores != null ? scores.Length : 0;

    /// <summary>直前に起きたこと（「北のポケットに +1」など）。画面表示用。</summary>
    public string LastEvent { get; private set; } = string.Empty;

    /// <summary>
    /// 直前のことが起きた時刻。少しの間だけ表示するために使う。
    /// -999 は「まだ何も起きていない」の意味（十分に昔の時刻にしておけば、表示側は最初から消えたままになる）。
    /// </summary>
    public float LastEventTime { get; private set; } = -999f;

    private void Awake()
    {
        // インスペクターで 0 や負の数が入っていても、最低1人分は持つ
        scores = new int[Mathf.Max(1, playerCount)];
    }

    /// <summary>指定したプレイヤーの名前。</summary>
    public string NameOf(int playerIndex)
    {
        if (playerNames != null && playerIndex >= 0 && playerIndex < playerNames.Length
            && !string.IsNullOrEmpty(playerNames[playerIndex]))
        {
            return playerNames[playerIndex];
        }
        return $"プレイヤー{playerIndex + 1}";
    }

    /// <summary>指定したプレイヤーの点数。</summary>
    public int ScoreOf(int playerIndex)
    {
        if (scores == null || playerIndex < 0 || playerIndex >= scores.Length)
        {
            return 0;
        }
        return scores[playerIndex];
    }

    /// <summary>点を足す。<see cref="ScorePocket"/> から呼ばれる。</summary>
    public void AddScore(int playerIndex, int amount, string where)
    {
        if (scores == null || playerIndex < 0 || playerIndex >= scores.Length)
        {
            Debug.LogWarning($"{name}: プレイヤー番号 {playerIndex} は範囲外です。点を足しませんでした。", this);
            return;
        }

        scores[playerIndex] += amount;

        LastEvent = $"{where} に投入！　{NameOf(playerIndex)} +{amount}";
        LastEventTime = Time.time;
    }
}
