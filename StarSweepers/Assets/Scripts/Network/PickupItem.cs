using System.Collections;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// 触ると点が入るアイテム。しばらくすると復活する。
///
/// **「取った」の判定はホストだけが行う。**
/// 参加者のPCでは、近づいても何も起きない。
/// ホストが「取られた」と決めて、それを全員に配る形にしている。
///
/// これがあると、**2人が同時に触ったときでも二重取りにならない。**
/// （ホストという1つの場所でしか判定していないため）
///
/// ## 当たり判定ではなく「距離」で見ている理由
///
/// 最初は当たり判定（トリガー）で取っていたが、**ホスト以外が取れない不具合が出た。**
/// 自分以外のキャラクターは `CharacterController` を切ってあり、
/// **切ると当たり判定ごと無くなる**ためだった。
///
/// 位置は全員分きちんと届いているので、**距離を測るほうが確実**と判断して作り直した。
/// </summary>
[RequireComponent(typeof(NetworkObject))]
public class PickupItem : NetworkBehaviour
{
    [Header("取れる範囲")]
    [Tooltip("この距離まで近づくと取れる（メートル）")]
    [Range(0.5f, 5f)]
    [SerializeField] private float pickupRadius = 1.2f;

    [Tooltip("高さの差を見るか。OFFだと真上や真下からでも取れる")]
    [SerializeField] private bool checkHeight = true;

    [Tooltip("高さの差をどこまで許すか（メートル）")]
    [Range(0.5f, 5f)]
    [SerializeField] private float heightTolerance = 2f;

    [Header("得点")]
    [Tooltip("取ったときに入る点数")]
    [Range(1, 100)]
    [SerializeField] private int scoreAmount = 1;

    [Header("復活")]
    [Tooltip("取られてから、また出てくるまでの秒数")]
    [Range(1f, 30f)]
    [SerializeField] private float respawnSeconds = 5f;

    [Header("見た目")]
    [Tooltip("回す速さ（1秒あたりの角度）。目立たせるため")]
    [SerializeField] private float spinSpeed = 90f;

    [Tooltip("隠したり出したりする見た目。子の Visual を入れる")]
    [SerializeField] private GameObject visual;

    /// <summary>
    /// いま取れる状態か。**ホストだけが書き換える。**
    /// 参加者側は、この値が変わったのを受け取って見た目を切り替える。
    /// </summary>
    private readonly NetworkVariable<bool> isAvailable = new NetworkVariable<bool>(true);

    public override void OnNetworkSpawn()
    {
        isAvailable.OnValueChanged += HandleAvailableChanged;
        ApplyVisual(isAvailable.Value);

        // つないだ側の画面にも、ちゃんと出てきたかを確かめるための記録
        Debug.Log($"[LAN] アイテム {name} が同期されました（ホスト：{IsServer}）");
    }

    public override void OnNetworkDespawn()
    {
        isAvailable.OnValueChanged -= HandleAvailableChanged;
    }

    private void Update()
    {
        // 見た目を回すだけ。通信には関係しないので、全員のPCで動かしてよい
        if (visual != null && visual.activeSelf)
        {
            visual.transform.Rotate(Vector3.up, spinSpeed * Time.deltaTime, Space.World);
        }

        // ★ここが肝心。取った判定はホストだけが行う
        if (IsServer && isAvailable.Value)
        {
            CheckPickup();
        }
    }

    /// <summary>近くにいるプレイヤーを探して、いれば取らせる。</summary>
    private void CheckPickup()
    {
        PlayerScore nearest = FindNearestPlayerInRange();

        if (nearest == null)
        {
            return;
        }

        // 一番近い1人にだけ入る。同時に触っても二重取りにならない
        nearest.AddScore(scoreAmount);
        isAvailable.Value = false;

        StartCoroutine(RespawnAfterDelay());
    }

    /// <summary>取れる範囲にいるプレイヤーのうち、一番近い1人。誰もいなければ null。</summary>
    private PlayerScore FindNearestPlayerInRange()
    {
        PlayerScore nearest = null;
        float nearestDistance = float.MaxValue;

        foreach (PlayerScore player in PlayerScore.All)
        {
            if (player == null || !player.IsSpawned)
            {
                continue;
            }

            Vector3 gap = player.transform.position - transform.position;

            if (checkHeight && Mathf.Abs(gap.y) > heightTolerance)
            {
                continue;
            }

            // 距離は水平（真上から見た）で測る。高さの差は、上で別に見ている
            gap.y = 0f;
            float distance = gap.magnitude;

            if (distance <= pickupRadius && distance < nearestDistance)
            {
                nearest = player;
                nearestDistance = distance;
            }
        }

        return nearest;
    }

    /// <summary>
    /// 復活までの待ち。待っている間に切断された・消された場合は何もしない
    /// （ホストのままで、まだ出ているときだけ書き換える）。
    /// </summary>
    private IEnumerator RespawnAfterDelay()
    {
        yield return new WaitForSeconds(respawnSeconds);

        if (IsServer && IsSpawned)
        {
            isAvailable.Value = true;
        }
    }

    /// <summary>ホストが <see cref="isAvailable"/> を書き換えると、全員のPCでこれが呼ばれる。</summary>
    private void HandleAvailableChanged(bool previous, bool current)
    {
        ApplyVisual(current);
    }

    /// <summary>取れる状態なら見た目を出し、取られていれば隠す。物そのものは消さない（復活するため）。</summary>
    private void ApplyVisual(bool available)
    {
        if (visual != null)
        {
            visual.SetActive(available);
        }
    }

    /// <summary>取れる範囲を、シーン画面に丸で表示する（編集中だけ見える）。</summary>
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, pickupRadius);
    }
}
