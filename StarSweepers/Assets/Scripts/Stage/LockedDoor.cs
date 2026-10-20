using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// **鍵で開く扉。**
///
/// 遊び方：
///
/// 1. どこかに置かれた**鍵**（<see cref="DoorKey"/>）を **F** で持つ
/// 2. 鍵を持ったまま、この扉に近づく（**扉の色が変わる**＝開けられる合図）
/// 3. **F** を押すと、**鍵が無くなって扉が開く**
///
/// **一度開けたあとは、鍵なしで開け閉めできる。**
/// 扉に近づいて **F** を押すたびに、**置いた場所へ戻る → また開く**を繰り返す。
/// 鍵は「その扉を使えるようにする」ためのもので、**開けっぱなしにするためのものではない。**
///
/// ## 開く動きは別の部品が持っている
///
/// この部品は「**開いてよいかどうか**」だけを決めている。
/// 実際に扉が動くのは <see cref="MoveObject"/>。
///
/// そのため、**上に上がる・下に沈む・横にスライドする・回って開く**のどれにでもできるし、
/// `Unlocked` に別の物を繋げば、**扉以外の物を動かすこともできる。**
/// </summary>
public class LockedDoor : MonoBehaviour
{
    [Header("鍵の種類")]
    [Tooltip("**この言葉と同じ鍵だけが使える。** 空なら、どの鍵でも開く（例：`赤` `倉庫`）")]
    [SerializeField] private string keyId = "";

    [Header("つなぐもの")]
    [Tooltip("開く扉。空なら、**同じ物に付いている MoveObject** を自動で使う")]
    [SerializeField] private MoveObject door;

    [Tooltip("**鍵穴の印。** 開いたときに消える。空でもよい")]
    [SerializeField] private GameObject lockIndicator;

    [Header("開いたときにすること")]
    [Tooltip("扉を動かす以外にやりたいことがあれば、ここに繋ぐ（音・演出など）")]
    [SerializeField] private UnityEvent unlocked = new UnityEvent();

    /// <summary>
    /// いまシーンに出ている鍵付きの扉の一覧。
    /// **探すときはここを見る**（ロープと同じ考え方。当たり判定を使わない）。
    /// </summary>
    public static readonly List<LockedDoor> All = new List<LockedDoor>();

    /// <summary>もう開いているか。</summary>
    public bool IsUnlocked { get; private set; }

    /// <summary>開いた瞬間に呼ばれる。</summary>
    public UnityEvent Unlocked => unlocked;

    private void Awake()
    {
        // 扉の欄が空なら、同じ物に付いている MoveObject を使う（扉そのものにこれを付けたときの手間を省く）
        if (door == null)
        {
            door = GetComponent<MoveObject>();
        }
    }

    private void OnEnable()
    {
        // 出ている間だけ一覧に載せる。止めた扉・消えた扉は、鍵を持つ側から見つからなくなる
        All.Add(this);
    }

    private void OnDisable()
    {
        All.Remove(this);
    }

    /// <summary>
    /// **その鍵で鍵を外せるか。**
    /// すでに鍵が外れている扉には使えない（鍵を無駄にしないため）。
    /// </summary>
    public bool Accepts(DoorKey key)
    {
        if (IsUnlocked || key == null)
        {
            return false;
        }

        // どちらかが空なら、種類を問わない
        return string.IsNullOrEmpty(keyId)
            || string.IsNullOrEmpty(key.KeyId)
            || keyId == key.KeyId;
    }

    /// <summary>
    /// **いま操作できるか。**
    ///
    /// - まだ鍵がかかっている … **合う鍵を持っているときだけ**
    /// - もう鍵が外れている … **いつでも**（開け閉めできる）
    /// </summary>
    public bool CanInteract(DoorKey heldKey)
    {
        return IsUnlocked || Accepts(heldKey);
    }

    /// <summary>
    /// **扉を操作する。**
    ///
    /// - まだ鍵がかかっていれば … **鍵を外して開ける**（鍵を消すのは呼んだ側の役目）
    /// - もう鍵が外れていれば … **開いていれば閉じ、閉じていれば開く**
    ///
    /// 戻り値は「**鍵を使ったか**」。true なら、呼んだ側が鍵を消す。
    /// </summary>
    public bool Interact()
    {
        if (IsUnlocked)
        {
            Toggle();
            return false;
        }

        Unlock();
        return true;
    }

    /// <summary>開いていれば閉じ、閉じていれば開く。**鍵が外れたあとだけ効く。**</summary>
    public void Toggle()
    {
        if (!IsUnlocked || door == null)
        {
            return;
        }

        door.Toggle();
    }

    /// <summary>鍵を外して扉を開ける。**鍵を消すのは呼んだ側の役目。**</summary>
    public void Unlock()
    {
        if (IsUnlocked)
        {
            return;
        }

        IsUnlocked = true;

        if (lockIndicator != null)
        {
            lockIndicator.SetActive(false);
        }

        if (door != null)
        {
            door.Open();
        }

        unlocked.Invoke();

        Debug.Log($"[STAGE] {name} の鍵が外れました");
    }

    /// <summary>もう一度鍵をかける。**鍵は戻らない。**ステージを作り直すときなどに使う。</summary>
    public void Relock()
    {
        IsUnlocked = false;

        if (lockIndicator != null)
        {
            lockIndicator.SetActive(true);
        }

        if (door != null)
        {
            door.Close();
        }
    }
}
