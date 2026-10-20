using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// 移動床（<see cref="PullAblePlate"/>）の柱の向き。床から見た向き（ローカル）で、
/// Front＝奥（+Z）、Right＝右（+X）。斜めはその2つを足した向き。
/// 床を回して置いても、柱はその回転に合わせた向きへ引っ張る。
/// </summary>
public enum PullAblePlateDirection
{
    Front,
    FrontRight,
    Right,
    BackRight,
    Back,
    BackLeft,
    Left,
    FrontLeft
}

/// <summary>
/// **8方向の柱をフックで引っ張ると、その向きへ一定距離だけ動く床。上に乗っているプレイヤーも一緒に運ぶ。**
/// STAGE_05 で使っている。設計のいきさつは `Documents/機能ドキュメント/引っ張れる移動床の再設計案.md`。
///
/// 部品の分担：
/// ・柱（<see cref="PullAblePlatePullPoint"/>）… フックの引っ掛け先。引っ張り終わると <see cref="Pull"/> を呼ぶ
/// ・乗っている人の判定（<see cref="PullAblePlatePassengerArea"/>）… 床の上面のトリガー。<see cref="RegisterPassenger"/> で登録してくる
/// ・場外のアンカー（<see cref="PullAblePlateAnchor"/>）… 床の上の人が引っ張ると、アンカーの向きへ床ごと動く（<see cref="PullToward"/>）
///
/// ## 動かし方の決まり
///
/// ・固定の地形や別の床にぶつかる手前で止まる（動く前に Rigidbody を進行方向へ Sweep して確かめる）
/// ・プレイヤーは `CharacterController` で動いているので、床を動かしても勝手には付いてこない。
///   **床が動いたぶんだけ、乗っている人の `CharacterController.Move` にも同じ量を渡す**
/// ・床の `Rigidbody` はキネマティック。`MovePosition` で動かすので、乗っている物資は物理で押される
///
/// ## オンラインのとき
///
/// **床を動かすのはホストだけ。** 参加者が引っ張ると、ServerRpc でホストに頼む。
/// 床の位置は `NetworkTransform`（ホスト権限）で全員に配られる。
/// 乗っている人は、**それぞれのPCが「床が1フレームで動いた量」を見て、自分のプレイヤーだけを動かす**
/// （<see cref="LateUpdate"/>）。オンラインでも1人用でも「動いているか」は <see cref="IsMoving"/> で見られる。
/// </summary>
[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(NetworkObject))]
public class PullAblePlate : NetworkBehaviour
{
    /// <summary>ぶつかる相手の手前で止めるときの、すき間（m）。ぴったり付けると次の移動で重なり扱いになるため。</summary>
    private const float CollisionSkin = 0.01f;

    /// <summary>
    /// いま有効な床の一覧。場外のアンカーが「このプレイヤーはどの床に乗っているか」を探すのに使う
    /// （<see cref="FindPlateCarrying"/>）。
    /// </summary>
    private static readonly HashSet<PullAblePlate> EnabledPlates = new();

    [Header("拉扯移動")]
    [Tooltip("1回引っ張ると動く距離（m）")]
    [Min(0f)]
    [SerializeField] private float moveDistance = 2f;

    [Tooltip("1回の移動にかける秒数。0 なら一瞬で動く")]
    [Min(0f)]
    [SerializeField] private float moveDuration = 0.35f;

    // OFF にした柱は GameObject ごと非表示になり、フックにも当たらない（OnValidate と SetPullPointActive で反映）
    [Header("有効な柱")]
    [SerializeField] private bool frontActive = true;
    [SerializeField] private bool frontRightActive = true;
    [SerializeField] private bool rightActive = true;
    [SerializeField] private bool backRightActive = true;
    [SerializeField] private bool backActive = true;
    [SerializeField] private bool backLeftActive = true;
    [SerializeField] private bool leftActive = true;
    [SerializeField] private bool frontLeftActive = true;

    // プレハブの子にある8本の柱。上のチェックに合わせて表示／非表示を切り替えるために持つ
    [Header("柱の参照")]
    [SerializeField] private PullAblePlatePullPoint frontPullPoint;
    [SerializeField] private PullAblePlatePullPoint frontRightPullPoint;
    [SerializeField] private PullAblePlatePullPoint rightPullPoint;
    [SerializeField] private PullAblePlatePullPoint backRightPullPoint;
    [SerializeField] private PullAblePlatePullPoint backPullPoint;
    [SerializeField] private PullAblePlatePullPoint backLeftPullPoint;
    [SerializeField] private PullAblePlatePullPoint leftPullPoint;
    [SerializeField] private PullAblePlatePullPoint frontLeftPullPoint;

    /// <summary>いま床に乗っているプレイヤー（根元の Transform → その CharacterController）。登録してくるのは PassengerArea。</summary>
    private readonly Dictionary<Transform, CharacterController> passengers = new();

    /// <summary>オンラインでの「動いている最中か」。ホストが書き、全員へ配られる。柱はこれを見て、動いている間は引っ掛からなくする。</summary>
    private readonly NetworkVariable<bool> networkIsMoving = new(false);

    private Rigidbody body;

    /// <summary>1人用（通信していないとき）の「動いている最中か」。</summary>
    private bool localIsMoving;

    /// <summary>
    /// オンラインで、前のフレームの床の位置を控えているか（<see cref="observedPosition"/> が有効か）。
    /// 生まれた直後や有効にした直後は false にして、**最初のフレームで大きくワープした量を乗客に渡さない**ようにする。
    /// </summary>
    private bool hasObservedPosition;

    /// <summary>オンラインで、前のフレームの床の位置。今のフレームとの差が、乗客を動かす量になる。</summary>
    private Vector3 observedPosition;

    /// <summary>動いている最中か。オンラインではホストの値（全員同じ）、1人用では自分の値。</summary>
    public bool IsMoving => IsSpawned ? networkIsMoving.Value : localIsMoving;

    private void Awake()
    {
        body = GetComponent<Rigidbody>();
    }

    private void OnEnable()
    {
        EnabledPlates.Add(this);
        ResetPositionObservation();
        RefreshPullPointStates();
    }

    private void OnDisable()
    {
        // 動いている途中で止められても、次に有効にしたときにまた引っ張れるように「動いていない」へ戻す
        StopAllCoroutines();
        SetMoving(false);
        hasObservedPosition = false;
        EnabledPlates.Remove(this);
        passengers.Clear();
    }

    public override void OnNetworkSpawn()
    {
        localIsMoving = false;
        ResetPositionObservation();
        if (IsServer)
        {
            networkIsMoving.Value = false;
        }
    }

    public override void OnNetworkDespawn()
    {
        StopAllCoroutines();
        localIsMoving = false;
        hasObservedPosition = false;
    }

    /// <summary>
    /// **オンラインのときに、乗っている人を床と一緒に運ぶ。**
    ///
    /// 床の位置はホストが決め、`NetworkTransform` が全員のPCで `transform.position` を動かす。
    /// そこで各PCは「前のフレームからどれだけ動いたか」を見て、その量を自分の乗客に渡す
    /// （ホストも参加者も同じやり方。誰を動かすかは <see cref="ShouldMovePassengerOnThisPeer"/> で決める）。
    ///
    /// 1人用（通信していないとき）は、床を動かす <see cref="ApplyMoveStep"/> の中で乗客も一緒に動かすので、ここでは何もしない。
    /// </summary>
    private void LateUpdate()
    {
        if (!IsSpawned)
        {
            hasObservedPosition = false;
            return;
        }

        Vector3 currentPosition = transform.position;
        Vector3 delta = CalculateObservedDelta(
            hasObservedPosition,
            observedPosition,
            currentPosition);
        observedPosition = currentPosition;
        hasObservedPosition = true;
        MovePassengers(delta);
    }

    private void OnValidate()
    {
        RefreshPullPointStates();
    }

    /// <summary>
    /// 柱の向きへ1回ぶん動かす（柱の <see cref="PullAblePlatePullPoint.CompletePull"/> から呼ばれる）。
    /// 参加者のPCならホストに頼むだけ（頼めたら true）。ホストと1人用は、その場で動き始める。
    /// 無効な柱・動いている最中・ぶつかっている等で動けなければ false。
    /// </summary>
    public bool Pull(PullAblePlateDirection direction)
    {
        if (!IsDirectionActive(direction))
        {
            return false;
        }

        if (IsSpawned && !IsServer)
        {
            RequestPullServerRpc(direction);
            return true;
        }

        return StartMove(CalculateDirection(direction, transform.rotation));
    }

    /// <summary>
    /// 指定した場所（場外のアンカー）へ向かって1回ぶん動かす。高さは無視して水平に動く。
    /// 参加者のPCならホストに頼むだけ。<see cref="PullAblePlateAnchor.CompletePull"/> から呼ばれる。
    /// </summary>
    public bool PullToward(Vector3 target)
    {
        if (IsSpawned && !IsServer)
        {
            RequestPullTowardServerRpc(target);
            return true;
        }

        return StartMoveToward(target);
    }

    // 床の持ち主はホストだが、引っ張るのは別のプレイヤーなので「持ち主でなくても呼べる」にしておく（RequireOwnership = false）
    [ServerRpc(RequireOwnership = false)]
    private void RequestPullServerRpc(PullAblePlateDirection direction)
    {
        // 参加者側でも確かめているが、ホストでもう一度確かめる（ホストの設定が正）
        if (!IsDirectionActive(direction))
        {
            return;
        }

        StartMove(CalculateDirection(direction, transform.rotation));
    }

    [ServerRpc(RequireOwnership = false)]
    private void RequestPullTowardServerRpc(Vector3 target)
    {
        StartMoveToward(target);
    }

    /// <summary>床の中心から目標へ向かう水平な向きで動き始める。目標が真上・真下など水平に差が無ければ動かない。</summary>
    private bool StartMoveToward(Vector3 target)
    {
        Vector3 direction = target - transform.position;
        direction.y = 0f;
        return StartMove(direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector3.zero);
    }

    /// <summary>
    /// 「このプレイヤーは床に乗っている」と登録する。<see cref="PullAblePlatePassengerArea"/> が、
    /// 足元の判定で床の上にいると分かるたびに呼ぶ（同じ人を何度登録しても1人ぶん）。
    /// </summary>
    public void RegisterPassenger(Transform playerRoot, CharacterController controller)
    {
        if (playerRoot == null || controller == null)
        {
            return;
        }

        EnabledPlates.Add(this);
        passengers[playerRoot] = controller;
    }

    /// <summary>「床から降りた」として登録を外す。</summary>
    public void UnregisterPassenger(Transform playerRoot)
    {
        if (playerRoot != null)
        {
            passengers.Remove(playerRoot);
        }
    }

    /// <summary>そのプレイヤーがこの床に乗っているか。柱が「自分の床の柱は引っ掛けられない」の判定に使う。</summary>
    public bool IsPlayerOnPlate(Transform playerRoot)
    {
        return playerRoot != null && passengers.ContainsKey(playerRoot);
    }

    /// <summary>そのプレイヤーを乗せている床を探す。乗っていなければ null。場外のアンカーが使う。</summary>
    public static PullAblePlate FindPlateCarrying(Transform playerRoot)
    {
        if (playerRoot == null)
        {
            return null;
        }

        foreach (PullAblePlate plate in EnabledPlates)
        {
            if (plate != null && plate.isActiveAndEnabled && plate.IsPlayerOnPlate(playerRoot))
            {
                return plate;
            }
        }

        return null;
    }

    /// <summary>その向きの柱が有効（インスペクターのチェックが ON）か。</summary>
    public bool IsDirectionActive(PullAblePlateDirection direction)
    {
        return direction switch
        {
            PullAblePlateDirection.Front => frontActive,
            PullAblePlateDirection.FrontRight => frontRightActive,
            PullAblePlateDirection.Right => rightActive,
            PullAblePlateDirection.BackRight => backRightActive,
            PullAblePlateDirection.Back => backActive,
            PullAblePlateDirection.BackLeft => backLeftActive,
            PullAblePlateDirection.Left => leftActive,
            PullAblePlateDirection.FrontLeft => frontLeftActive,
            _ => false
        };
    }

    /// <summary>その向きの柱を、実行中に有効／無効にする。柱の GameObject の表示もあわせて切り替える。</summary>
    public void SetPullPointActive(PullAblePlateDirection direction, bool active)
    {
        switch (direction)
        {
            case PullAblePlateDirection.Front:
                frontActive = active;
                break;
            case PullAblePlateDirection.FrontRight:
                frontRightActive = active;
                break;
            case PullAblePlateDirection.Right:
                rightActive = active;
                break;
            case PullAblePlateDirection.BackRight:
                backRightActive = active;
                break;
            case PullAblePlateDirection.Back:
                backActive = active;
                break;
            case PullAblePlateDirection.BackLeft:
                backLeftActive = active;
                break;
            case PullAblePlateDirection.Left:
                leftActive = active;
                break;
            case PullAblePlateDirection.FrontLeft:
                frontLeftActive = active;
                break;
        }

        ApplyPullPointState(GetPullPoint(direction), active);
    }

    /// <summary>
    /// 柱の向き（床のローカル）を、床の回転を掛けてワールドの水平な向き（長さ1）にする。
    /// 床が傾いていても上下には動かさない。static なのはテストから直接呼ぶため。
    /// </summary>
    public static Vector3 CalculateDirection(PullAblePlateDirection direction, Quaternion rotation)
    {
        Vector3 localDirection = direction switch
        {
            PullAblePlateDirection.Front => Vector3.forward,
            PullAblePlateDirection.FrontRight => new Vector3(1f, 0f, 1f),
            PullAblePlateDirection.Right => Vector3.right,
            PullAblePlateDirection.BackRight => new Vector3(1f, 0f, -1f),
            PullAblePlateDirection.Back => Vector3.back,
            PullAblePlateDirection.BackLeft => new Vector3(-1f, 0f, -1f),
            PullAblePlateDirection.Left => Vector3.left,
            PullAblePlateDirection.FrontLeft => new Vector3(-1f, 0f, 1f),
            _ => Vector3.zero
        };

        Vector3 worldDirection = rotation * localDirection;
        worldDirection.y = 0f;
        return worldDirection.sqrMagnitude > 0.0001f ? worldDirection.normalized : Vector3.zero;
    }

    /// <summary>
    /// **このPCで、その乗客を動かしてよいか。**
    ///
    /// | 状況 | 動かすか |
    /// | --- | --- |
    /// | 床が通信していない（1人用） | 動かす |
    /// | 乗客が通信オブジェクトでない（テスト用のダミーなど） | 動かす |
    /// | 乗客が自分のプレイヤー | 動かす |
    /// | 乗客が他の人のプレイヤー | **動かさない**（その人のPCが動かし、位置は NetworkTransform で届く） |
    ///
    /// static なのはテストから直接呼ぶため。
    /// </summary>
    public static bool ShouldMovePassengerOnThisPeer(
        bool plateIsOnline,
        bool passengerIsOnline,
        bool passengerIsOwner)
    {
        return !plateIsOnline || !passengerIsOnline || passengerIsOwner;
    }

    /// <summary>
    /// 前のフレームからの床の移動量。前の位置を控えていなければ 0（最初のフレームでワープした量を乗客に渡さないため）。
    /// static なのはテストから直接呼ぶため。
    /// </summary>
    public static Vector3 CalculateObservedDelta(
        bool hasPreviousPosition,
        Vector3 previousPosition,
        Vector3 currentPosition)
    {
        return hasPreviousPosition ? currentPosition - previousPosition : Vector3.zero;
    }

    /// <summary>
    /// 指定の向きへ Move Distance だけ動き始める（ホストと1人用だけ。参加者のPCでは何もしない）。
    /// Move Duration が 0 のとき、またはエディタで再生していないとき（テスト）は、その場で一瞬で動く。
    /// </summary>
    private bool StartMove(Vector3 direction)
    {
        // 参加者のPC・動いている最中・向きがほぼ0・距離が0 のときは動かない
        if ((IsSpawned && !IsServer) ||
            IsMoving ||
            direction.sqrMagnitude <= 0.0001f ||
            moveDistance <= 0f)
        {
            return false;
        }

        if (body == null)
        {
            body = GetComponent<Rigidbody>();
        }

        Vector3 destination = body.position + direction.normalized * moveDistance;
        if (moveDuration <= 0f || !Application.isPlaying)
        {
            ApplyMoveStep(destination);
            return true;
        }

        StartCoroutine(MoveTo(destination));
        return true;
    }

    /// <summary>
    /// Move Duration をかけて目的地までなめらかに動かすコルーチン。
    /// `Rigidbody.MovePosition` は物理の更新で反映されるので、物理の更新（FixedUpdate）ごとに1歩ずつ進める。
    /// 途中で何かにぶつかったら、その場で止めて終わる（残りの距離は進まない）。
    /// </summary>
    private IEnumerator MoveTo(Vector3 destination)
    {
        SetMoving(true);
        Vector3 start = body.position;
        Vector3 current = start;
        float elapsed = 0f;

        while (elapsed < moveDuration)
        {
            yield return new WaitForFixedUpdate();
            elapsed += Time.fixedDeltaTime;
            // 動き出しはゆっくり、真ん中で速く、最後はゆっくり止まる
            float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / moveDuration));
            Vector3 next = Vector3.Lerp(start, destination, t);
            if (ApplyMoveStep(next, current))
            {
                SetMoving(false);
                yield break;
            }

            current = next;
        }

        ApplyMoveStep(destination, current);
        SetMoving(false);
    }

    /// <summary>「動いている最中」を切り替える。ホストなら全員に配る値（<see cref="networkIsMoving"/>）も更新する。</summary>
    private void SetMoving(bool moving)
    {
        localIsMoving = moving;
        if (IsSpawned && IsServer && networkIsMoving.Value != moving)
        {
            networkIsMoving.Value = moving;
        }
    }

    private bool ApplyMoveStep(Vector3 nextPosition)
    {
        return ApplyMoveStep(nextPosition, body.position);
    }

    /// <summary>
    /// 床を1歩ぶん動かす。ぶつかる相手がいればその手前までで止める。戻り値は「ぶつかって止まったか」。
    /// 1人用のときは、動いた量をここで乗客にも渡す（オンラインでは <see cref="LateUpdate"/> が渡す）。
    /// </summary>
    private bool ApplyMoveStep(Vector3 nextPosition, Vector3 currentPosition)
    {
        Vector3 requestedDelta = nextPosition - currentPosition;
        Vector3 allowedDelta = LimitMoveByCollision(requestedDelta, out bool blocked);
        Vector3 allowedPosition = currentPosition + allowedDelta;

        body.MovePosition(allowedPosition);
        if (!Application.isPlaying)
        {
            // 再生していないとき（テスト）は物理の更新が来ないので、MovePosition だけでは位置が変わらない
            transform.position = allowedPosition;
        }

        if (!IsSpawned)
        {
            MovePassengers(allowedDelta);
        }

        return blocked;
    }

    /// <summary>
    /// **進む先に固定の地形や別の床があれば、その手前（<see cref="CollisionSkin"/> のすき間）までに移動量を縮める。**
    /// 床の Rigidbody 全体（子の PlateBody も含む）を進行方向へ Sweep して調べる。
    ///
    /// 次のものは「止まる相手」にしない：
    /// ・トリガー（柱・PassengerArea・フックなど）
    /// ・プレイヤー（CharacterController）… 床はプレイヤーを押しのけて進む
    /// ・自分の子（自分自身にぶつかった扱いにしないため）
    /// ・進行方向を向いていない面（すでに触れている相手から離れる向きの移動まで止めないため）
    /// </summary>
    private Vector3 LimitMoveByCollision(Vector3 requestedDelta, out bool blocked)
    {
        blocked = false;
        float requestedDistance = requestedDelta.magnitude;
        if (requestedDistance <= 0.0001f)
        {
            return Vector3.zero;
        }

        Vector3 direction = requestedDelta / requestedDistance;
        RaycastHit[] hits = body.SweepTestAll(
            direction,
            requestedDistance + CollisionSkin,
            QueryTriggerInteraction.Ignore);

        float allowedDistance = requestedDistance;
        foreach (RaycastHit hit in hits)
        {
            Collider other = hit.collider;
            if (other == null ||
                other is CharacterController ||
                other.transform.IsChildOf(transform) ||
                Vector3.Dot(direction, hit.normal) >= -0.0001f)
            {
                continue;
            }

            // いちばん近い相手の手前まで。複数当たっていれば、いちばん短い距離が残る
            float distanceBeforeContact = Mathf.Max(0f, hit.distance - CollisionSkin);
            if (distanceBeforeContact >= allowedDistance)
            {
                continue;
            }

            allowedDistance = distanceBeforeContact;
            blocked = true;
        }

        return direction * allowedDistance;
    }

    /// <summary>
    /// 乗っている人を、床が動いた量だけ `CharacterController.Move` で動かす。
    /// 消えたプレイヤーの登録はここで片付ける。操作が止まっている人（CharacterController が無効。オンラインの他人の体など）は飛ばす。
    /// </summary>
    private void MovePassengers(Vector3 delta)
    {
        if (delta.sqrMagnitude <= 0f)
        {
            return;
        }

        List<Transform> invalid = null;
        foreach (KeyValuePair<Transform, CharacterController> passenger in passengers)
        {
            if (passenger.Key == null || passenger.Value == null)
            {
                // foreach の最中に Dictionary から消せないので、後でまとめて消す
                invalid ??= new List<Transform>();
                invalid.Add(passenger.Key);
                continue;
            }

            if (!passenger.Value.enabled || !passenger.Value.gameObject.activeInHierarchy)
            {
                continue;
            }

            NetworkObject passengerNetworkObject = passenger.Key.GetComponentInParent<NetworkObject>();
            bool passengerIsOnline = passengerNetworkObject != null && passengerNetworkObject.IsSpawned;
            bool passengerIsOwner = passengerNetworkObject == null || passengerNetworkObject.IsOwner;
            if (!ShouldMovePassengerOnThisPeer(IsSpawned, passengerIsOnline, passengerIsOwner))
            {
                continue;
            }

            passenger.Value.Move(delta);
        }

        if (invalid == null)
        {
            return;
        }

        foreach (Transform playerRoot in invalid)
        {
            passengers.Remove(playerRoot);
        }
    }

    /// <summary>「前のフレームの位置」の控えを捨てる。次の <see cref="LateUpdate"/> では移動量 0 から数え直す。</summary>
    private void ResetPositionObservation()
    {
        observedPosition = transform.position;
        hasObservedPosition = false;
    }

    /// <summary>8本の柱すべての表示／非表示を、インスペクターのチェックに合わせる。</summary>
    private void RefreshPullPointStates()
    {
        foreach (PullAblePlateDirection direction in System.Enum.GetValues(typeof(PullAblePlateDirection)))
        {
            ApplyPullPointState(GetPullPoint(direction), IsDirectionActive(direction));
        }
    }

    private PullAblePlatePullPoint GetPullPoint(PullAblePlateDirection direction)
    {
        return direction switch
        {
            PullAblePlateDirection.Front => frontPullPoint,
            PullAblePlateDirection.FrontRight => frontRightPullPoint,
            PullAblePlateDirection.Right => rightPullPoint,
            PullAblePlateDirection.BackRight => backRightPullPoint,
            PullAblePlateDirection.Back => backPullPoint,
            PullAblePlateDirection.BackLeft => backLeftPullPoint,
            PullAblePlateDirection.Left => leftPullPoint,
            PullAblePlateDirection.FrontLeft => frontLeftPullPoint,
            _ => null
        };
    }

    /// <summary>柱の GameObject ごと表示／非表示にする（非表示ならフックにも当たらない）。変わらないときは触らない。</summary>
    private static void ApplyPullPointState(PullAblePlatePullPoint pullPoint, bool active)
    {
        if (pullPoint != null && pullPoint.gameObject.activeSelf != active)
        {
            pullPoint.gameObject.SetActive(active);
        }
    }
}
