using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// **フックで引っ掛けられる「物資」に付ける目印。**
///
/// これが付いている物だけが、フックの対象になる。
/// フィールドに置いた箱などに付けて使う。
///
/// 引き寄せ・投げの処理そのものは <see cref="ThrowController"/> にある。
/// こちらは「引っ掛けられる物かどうか」「糸を結ぶ位置」「点数」「消える」だけを持つ。
///
/// **物資はスポナー（<see cref="FishingObjectSpawner"/>）が次々に出す。**
/// 消えた物資は戻ってこない（Respawn Seconds が 0 のとき）。
/// </summary>
[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(Collider))]
public class HookableObject : MonoBehaviour
{
    /// <summary>
    /// いまシーンにある物資の一覧。**スポナーが「マップに何個あるか」を数える**のに使う。
    /// </summary>
    public static readonly List<HookableObject> All = new List<HookableObject>();

    [Header("糸・フックの結び目")]
    [Tooltip("物の中心から、どれだけずらした位置に糸とフックを結ぶか（物のローカル座標）")]
    [SerializeField] private Vector3 hookAnchorOffset = Vector3.zero;

    [Header("点数")]
    [Tooltip("ポケットに入れたときに入る点数。0にするとポケットが無視する（爆発物など）")]
    [SerializeField] private int scoreValue = 1;

    [Header("消えたあとの復活")]
    [Tooltip("消えてから元の場所に戻ってくるまでの秒数。0にすると戻ってこない（そのまま削除）。" +
             "スポナーで出す物資は0にする。オンラインでは常に削除される")]
    [SerializeField] private float respawnSeconds = 0f;

    [Header("場外に落ちたとき")]
    [Tooltip("この高さより下に落ちたら消える（勢いよく枠の外へ飛び出した物を片付けるため）")]
    [SerializeField] private float fallVanishHeight = -5f;

    /// <summary>この物の Rigidbody。投げるときに力を加える相手。</summary>
    public Rigidbody Body { get; private set; }

    /// <summary>いまフックされているか。**二重に引っ掛けるのを防ぐ**ために使う。</summary>
    public bool IsHooked { get; private set; }

    /// <summary>いま消えている（復活や削除を待っている）か。</summary>
    public bool IsVanished { get; private set; }

    /// <summary>ポケットに入れたときに入る点数。</summary>
    public int ScoreValue => scoreValue;

    /// <summary>糸とフックを結ぶワールド座標。</summary>
    public Vector3 AnchorPoint => transform.TransformPoint(hookAnchorOffset);

    /// <summary>最初に置かれていた位置と向き。復活（Respawn Seconds が 0 より大きいとき）に戻す先。</summary>
    private Vector3 startPosition;
    private Quaternion startRotation;

    private void Awake()
    {
        Body = GetComponent<Rigidbody>();

        // 復活させる場所として、最初に置かれていた位置を覚えておく
        startPosition = transform.position;
        startRotation = transform.rotation;
    }

    private void OnEnable()
    {
        if (!All.Contains(this))
        {
            All.Add(this);
        }
    }

    private void OnDisable()
    {
        All.Remove(this);
    }

    private void Update()
    {
        // 枠の外へ飛び出して、下へ落ちていった物を片付ける
        if (!IsVanished && transform.position.y < fallVanishHeight)
        {
            Vanish();
        }
    }

    /// <summary>消えていない物資が、いまシーンにいくつあるか。</summary>
    public static int CountActive()
    {
        int count = 0;
        foreach (HookableObject item in All)
        {
            if (item != null && !item.IsVanished)
            {
                count++;
            }
        }
        return count;
    }

    /// <summary>フックされた／外れた、を記録する。<see cref="HookController"/> から呼ばれる。</summary>
    public void SetHooked(bool hooked)
    {
        IsHooked = hooked;
    }

    /// <summary>
    /// **この物を消す。** ポケットに入ったとき・爆発に巻き込まれたとき・場外に落ちたときに呼ばれる。
    ///
    /// ・オンライン … 見た目を消し、**ホストが少し待ってから全員の画面から削除する**
    ///   （<see cref="FishingNetSupply.ServerDespawnSoon"/>）
    /// ・1人用 … Respawn Seconds が 0 なら削除。0 より大きければ、その秒数後に元の場所へ戻る
    /// </summary>
    public void Vanish()
    {
        if (IsVanished)
        {
            return;
        }

        // 消える物を誰かが引っ掛けたままにしない（フック側が「外れた」と気づけるように）
        SetHooked(false);

        // ---- オンライン ----
        FishingNetSupply netSupply = GetComponent<FishingNetSupply>();
        if (netSupply != null && netSupply.IsSpawned)
        {
            // オンラインで勝手に Destroy すると通信側がエラーになるので、
            // ここでは見えなくするだけにして、削除はホストに任せる
            IsVanished = true;
            HideParts();

            if (netSupply.IsServer)
            {
                netSupply.ServerDespawnSoon();
            }
            else
            {
                // 参加者のPCだけが「爆発に巻き込まれた」と判断した場合（位置のずれ）に備え、
                // しばらく待ってもホストから削除されなければ、見た目を元に戻す
                StartCoroutine(RestoreIfNotRemoved());
            }
            return;
        }

        // ---- 1人用 ----
        if (respawnSeconds <= 0f)
        {
            IsVanished = true;
            Destroy(gameObject);
            return;
        }

        IsVanished = true;
        StartCoroutine(VanishRoutine());
    }

    /// <summary>1人用：見た目を消して待ち、元の場所に戻して物理も動かし直す。</summary>
    private IEnumerator VanishRoutine()
    {
        HideParts();

        yield return new WaitForSeconds(respawnSeconds);

        transform.SetPositionAndRotation(startPosition, startRotation);

        // HideParts で止めていた物理を戻す。前の勢い（投げられた速度など）が残らないよう速度もゼロにする
        Body.isKinematic = false;
        Body.linearVelocity = Vector3.zero;
        Body.angularVelocity = Vector3.zero;
        SetPartsEnabled(true);

        IsVanished = false;
    }

    /// <summary>オンラインの参加者側：ホストに消されなかったら、見た目を戻す。</summary>
    private IEnumerator RestoreIfNotRemoved()
    {
        yield return new WaitForSeconds(ClientRestoreSeconds);

        // ここまで来た＝ホストはこの物を消していない。参加者側では物理は回さないので止めたままでよい
        SetPartsEnabled(true);
        IsVanished = false;
    }

    /// <summary>参加者側で消えたと判断してから、ホストの削除を待つ秒数。</summary>
    private const float ClientRestoreSeconds = 3f;

    /// <summary>見た目と当たり判定を消し、その場に止める。</summary>
    private void HideParts()
    {
        SetPartsEnabled(false);

        // キネマティックな Rigidbody に速度を入れると Unity が警告を出すので、動いているときだけ止める
        if (!Body.isKinematic)
        {
            Body.linearVelocity = Vector3.zero;
            Body.angularVelocity = Vector3.zero;
        }
        Body.isKinematic = true;
    }

    /// <summary>見た目と当たり判定をまとめて出す／消す。オブジェクト自体は動かし続ける（復活を数えるため）。</summary>
    private void SetPartsEnabled(bool enabledState)
    {
        foreach (Renderer renderer in GetComponentsInChildren<Renderer>(true))
        {
            renderer.enabled = enabledState;
        }

        foreach (Collider collider in GetComponentsInChildren<Collider>(true))
        {
            collider.enabled = enabledState;
        }
    }
}
