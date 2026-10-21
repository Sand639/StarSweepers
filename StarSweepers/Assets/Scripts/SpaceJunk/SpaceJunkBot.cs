using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// **宇宙ごみを集めるボット（NPC）。** 試作・2026/10/21（大槻さんの依頼。「最初は簡易的なものでよい」）
///
/// ## 何をするか
///
/// 1. いちばん近い素材（<see cref="SpaceJunkMaterial"/> が付いた物）へ歩いていく
/// 2. 手が届いたら**頭の上に持ち上げる**
/// 3. 自分のチームのゴールへ歩き、**投げる距離まで来たら放物線で放り込む**
/// 4. 1 に戻る
///
/// 点はプレイヤーと同じく <see cref="SpaceJunkGoal"/> が数える（ゴールの中に決めた秒数いつづけたら1点）。
///
/// ## 横取りできる
///
/// 持ち上げている素材も、**プレイヤーがフックで引っ掛ければ奪える**（ボットは手を離して次の素材を探す）。
///
/// ## 考えるのはホストだけ
///
/// ボットの頭（どこへ行くか・何を持つか）は**ホストのPCだけ**で動かす。
/// ほかのPCには、ボットの位置は `NetworkTransform`、素材の位置は素材側の `NetworkTransform` で届く。
/// **つないでいないとき（検証シーン）は、そのPCで動く。**
///
/// ## 道は探さない
///
/// 目的地へ**まっすぐ**歩くだけ（簡易版）。穴や壁は避けない。
/// 進めなくなったら、その素材をしばらくあきらめて別の素材を探す。落ちたら最初の場所へ戻る。
/// </summary>
[RequireComponent(typeof(CharacterController))]
public class SpaceJunkBot : NetworkBehaviour
{
    [Header("チーム")]
    [Tooltip("どのチームのゴールへ運ぶか（0=青 / 1=赤 / 2=緑 / 3=黄）。そのチームのゴールが無いマップでは何もしない")]
    [SerializeField] private int team = 0;

    [Tooltip("ゴールを直接決める（検証シーン用）。入っていれば、チームに関係なくここへ運ぶ")]
    [SerializeField] private Transform goalOverride;

    [Tooltip("チームの色に塗る見た目。空なら色を変えない")]
    [SerializeField] private Renderer bodyRenderer;

    [Header("歩く")]
    [Tooltip("歩く速さ（m/秒）")]
    [SerializeField] private float moveSpeed = 3.5f;

    [Tooltip("向きを変える速さ（度/秒）")]
    [SerializeField] private float turnSpeed = 540f;

    [Header("拾う")]
    [Tooltip("素材にここまで近づいたら（水平の距離、m）持ち上げる")]
    [SerializeField] private float pickupDistance = 1.6f;

    [Tooltip("持ち上げた素材を置く高さ（足元から、m）")]
    [SerializeField] private float carryHeight = 2.6f;

    [Tooltip("ゴールからこの距離（m）より近い素材は拾わない（もうすぐ点になる物を持ち出さないため）")]
    [SerializeField] private float ignoreNearGoalDistance = 2.5f;

    [Header("投げる")]
    [Tooltip("ゴールまでこの距離（水平、m）に来たら投げる")]
    [SerializeField] private float throwDistance = 4f;

    [Tooltip("投げた素材の横向きの速さ（m/秒）。小さいほど高く山なりに飛ぶ")]
    [SerializeField] private float throwHorizontalSpeed = 6f;

    [Tooltip("投げたあと、次の素材を探し始めるまでの秒数")]
    [SerializeField] private float afterThrowPause = 0.6f;

    [Header("困ったとき")]
    [Tooltip("この秒数のあいだ目的地へ近づけなかったら、その素材をあきらめる")]
    [SerializeField] private float stuckSeconds = 3f;

    [Tooltip("あきらめた素材を、また狙うまでの秒数")]
    [SerializeField] private float giveUpSeconds = 6f;

    [Tooltip("この高さより下に落ちたら、最初の場所へ戻る（プレイヤーと同じ -8m）")]
    [SerializeField] private float fallResetHeight = -8f;

    /// <summary>どのボットかが狙っている素材。**2体が同じ素材を取り合わない**ために使う。</summary>
    private static readonly HashSet<HookableObject> Reserved = new HashSet<HookableObject>();

    private CharacterController controller;
    private Vector3 startPosition;
    private Quaternion startRotation;
    private float verticalSpeed;

    /// <summary>狙っている素材（持ち上げる前）。</summary>
    private HookableObject target;

    /// <summary>持ち上げている素材。</summary>
    private HookableObject carried;

    /// <summary>あきらめた素材と、また狙ってよくなる時刻。</summary>
    private readonly Dictionary<HookableObject, float> givenUp = new Dictionary<HookableObject, float>();

    private float pauseUntil;

    // 進めなくなったかを見るための、前回の「目的地までの距離」と、その時刻
    private float bestDistance = float.MaxValue;
    private float bestDistanceTime;

    /// <summary>どのチームのボットか。</summary>
    public int Team => team;

    private void Awake()
    {
        controller = GetComponent<CharacterController>();
        startPosition = transform.position;
        startRotation = transform.rotation;

        if (bodyRenderer != null)
        {
            bodyRenderer.material.color = SpaceJunkTeams.TeamColor(team);
        }
    }

    public override void OnDestroy()
    {
        Drop();
        SetTarget(null);

        // シーンを移ると素材ごと消えるので、消えた物の印を片付けておく
        Reserved.RemoveWhere(item => item == null);
        base.OnDestroy();
    }

    /// <summary>
    /// **このPCでボットを考えさせてよいか。**
    /// つないでいる → ホストだけ。つないでいない（検証シーン）→ このPCで動かす。
    /// </summary>
    private bool CanThink()
    {
        if (IsSpawned)
        {
            return IsServer;
        }

        return NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening;
    }

    private void Update()
    {
        if (!CanThink())
        {
            return;
        }

        if (transform.position.y < fallResetHeight)
        {
            ResetToStart();
            return;
        }

        // ラウンドの始まりの演出中・ラウンドの合間は止まる（持っている物も離す）
        if (!SpaceJunkRound.PlayAllowed)
        {
            Drop();
            SetTarget(null);
            Move(Vector3.zero);
            return;
        }

        if (Time.time < pauseUntil)
        {
            Move(Vector3.zero);
            return;
        }

        if (carried != null)
        {
            CarryToGoal();
        }
        else
        {
            GoToMaterial();
        }
    }

    /// <summary>持ち上げている素材を頭の上に置き続ける（物理の計算に合わせて動かす）。</summary>
    private void FixedUpdate()
    {
        // 横取りされた直後は、もう引っ掛けた人が動かしているので触らない（手を離すのは Update で行う）
        if (carried == null || !CanThink() || !IsStillMine(carried))
        {
            return;
        }

        carried.Body.MovePosition(transform.position + Vector3.up * carryHeight);
    }

    // ------------------------------------------------------------
    // 素材を探して拾う
    // ------------------------------------------------------------

    private void GoToMaterial()
    {
        if (!IsPickable(target))
        {
            SetTarget(FindNearestMaterial());
        }

        if (target == null)
        {
            Move(Vector3.zero);
            return;
        }

        float distance = FlatDistance(transform.position, target.transform.position);

        if (distance <= pickupDistance)
        {
            PickUp(target);
            return;
        }

        if (IsStuck(distance))
        {
            givenUp[target] = Time.time + giveUpSeconds;
            SetTarget(null);
            return;
        }

        Move(target.transform.position - transform.position);
    }

    /// <summary>拾ってよい素材のうち、いちばん近いもの。無ければ null。</summary>
    private HookableObject FindNearestMaterial()
    {
        Vector3? goal = GoalPosition();
        HookableObject nearest = null;
        float nearestDistance = float.MaxValue;

        foreach (HookableObject item in HookableObject.All)
        {
            if (!IsPickable(item) || Reserved.Contains(item))
            {
                continue;
            }

            if (givenUp.TryGetValue(item, out float until) && Time.time < until)
            {
                continue;
            }

            // もうゴールの近くにある物は、そのまま点になるので触らない
            if (goal.HasValue && FlatDistance(item.transform.position, goal.Value) < ignoreNearGoalDistance)
            {
                continue;
            }

            float distance = FlatDistance(transform.position, item.transform.position);
            if (distance < nearestDistance)
            {
                nearest = item;
                nearestDistance = distance;
            }
        }

        return nearest;
    }

    /// <summary>
    /// **ボットが拾ってよい素材か。**
    /// 素材であること・消えていないこと・誰も引っ掛けていないこと・重いデブリでないこと・
    /// いまホストが動かしている（誰かの手元で動いていない）こと。
    /// </summary>
    private bool IsPickable(HookableObject item)
    {
        if (item == null || item.IsVanished || item.IsHooked)
        {
            return false;
        }

        if (item.GetComponent<SpaceJunkMaterial>() == null || item.GetComponent<HeavyHookable>() != null)
        {
            return false;
        }

        FishingNetSupply netSupply = item.GetComponent<FishingNetSupply>();
        if (netSupply != null && netSupply.IsSpawned && (netSupply.IsClaimed || !netSupply.IsOwner))
        {
            return false;
        }

        return true;
    }

    /// <summary>素材を持ち上げる。物理を止めて、自分の体とはぶつからないようにする。</summary>
    private void PickUp(HookableObject item)
    {
        carried = item;
        SetTarget(null);
        Reserved.Add(item);

        if (!item.Body.isKinematic)
        {
            item.Body.linearVelocity = Vector3.zero;
            item.Body.angularVelocity = Vector3.zero;
        }
        item.Body.isKinematic = true;

        SetIgnoreCollision(item, true);
        ResetStuck();
    }

    // ------------------------------------------------------------
    // ゴールへ運んで投げる
    // ------------------------------------------------------------

    private void CarryToGoal()
    {
        // **プレイヤーに横取りされた／消えた**ら、手を離して次を探す
        if (!IsStillMine(carried))
        {
            Forget();
            return;
        }

        Vector3? goal = GoalPosition();
        if (!goal.HasValue)
        {
            // 自分のチームのゴールが無い（チーム数が足りないマップなど）。持ったまま待つ
            Move(Vector3.zero);
            return;
        }

        float distance = FlatDistance(transform.position, goal.Value);

        if (distance <= throwDistance)
        {
            ThrowTo(goal.Value);
            return;
        }

        if (IsStuck(distance))
        {
            // ゴールへ近づけない。その場に落として、しばらくその素材はあきらめる
            HookableObject item = carried;
            Drop();
            givenUp[item] = Time.time + giveUpSeconds;
            return;
        }

        Move(goal.Value - transform.position);
    }

    /// <summary>持っている素材が、まだボットの物か（横取りされていない・消えていない）。</summary>
    private static bool IsStillMine(HookableObject item)
    {
        if (item == null || item.IsVanished || item.IsHooked)
        {
            return false;
        }

        FishingNetSupply netSupply = item.GetComponent<FishingNetSupply>();
        return netSupply == null || !netSupply.IsSpawned || (!netSupply.IsClaimed && netSupply.IsOwner);
    }

    /// <summary>
    /// **ゴールへ放物線で放り込む。**
    /// 飛ぶ時間を「水平の距離 ÷ 横向きの速さ」で決め、その時間でちょうど届く上向きの速さを計算する。
    /// </summary>
    private void ThrowTo(Vector3 goal)
    {
        HookableObject item = carried;
        Release();

        Vector3 from = item.transform.position;
        Vector3 flat = new Vector3(goal.x - from.x, 0f, goal.z - from.z);
        float time = Mathf.Max(0.4f, flat.magnitude / Mathf.Max(0.1f, throwHorizontalSpeed));

        // y = vy*t + g*t*t/2 を vy について解く（g は下向きなのでマイナス）
        float rise = goal.y - from.y;
        float verticalVelocity = rise / time - 0.5f * Physics.gravity.y * time;

        item.Body.linearVelocity = flat / time + Vector3.up * verticalVelocity;

        pauseUntil = Time.time + afterThrowPause;
    }

    /// <summary>
    /// 運ぶ先（ゴールの真ん中）。Goal Override があればそこ、無ければ**自分のチームが持っているゴール**。
    /// 見つからなければ null。
    /// </summary>
    private Vector3? GoalPosition()
    {
        if (goalOverride != null)
        {
            return goalOverride.position;
        }

        foreach (SpaceJunkGoal goal in SpaceJunkGoal.All)
        {
            if (goal != null && !goal.IsClosed && goal.OwnerTeam == team)
            {
                Collider area = goal.GetComponent<Collider>();
                return area != null ? area.bounds.center : goal.transform.position;
            }
        }

        return null;
    }

    // ------------------------------------------------------------
    // 手を離す
    // ------------------------------------------------------------

    /// <summary>持っている素材を、その場に落とす。</summary>
    private void Drop()
    {
        if (carried == null)
        {
            return;
        }

        HookableObject item = carried;
        Release();

        if (item != null)
        {
            item.Body.linearVelocity = Vector3.zero;
        }
    }

    /// <summary>素材の物理を動かし直し、ぶつかるようにも戻して、手放す。</summary>
    private void Release()
    {
        HookableObject item = carried;
        Forget();

        if (item == null)
        {
            return;
        }

        item.Body.isKinematic = false;
        item.Body.angularVelocity = Vector3.zero;
    }

    /// <summary>
    /// 持っている素材のことを忘れる（物理には触らない）。
    /// **横取りされたとき用。** そのときは物理を動かすのは引っ掛けた人の係なので、こちらからはいじらない。
    /// </summary>
    private void Forget()
    {
        if (carried != null)
        {
            SetIgnoreCollision(carried, false);
            Reserved.Remove(carried);
        }

        carried = null;
        ResetStuck();
    }

    // ------------------------------------------------------------
    // 小さな部品
    // ------------------------------------------------------------

    /// <summary>狙う素材を変える（ほかのボットに「狙っている」と知らせる印も付け替える）。</summary>
    private void SetTarget(HookableObject item)
    {
        if (target == item)
        {
            return;
        }

        if (target != null && target != carried)
        {
            Reserved.Remove(target);
        }

        target = item;

        if (target != null)
        {
            Reserved.Add(target);
        }

        ResetStuck();
    }

    /// <summary>
    /// 水平の向き <paramref name="direction"/> へ歩く（ゼロなら止まる）。重力もここでかける。
    /// </summary>
    private void Move(Vector3 direction)
    {
        direction.y = 0f;

        if (controller.isGrounded && verticalSpeed < 0f)
        {
            verticalSpeed = -1f; // 地面に押しつけておく（坂で浮かないように）
        }
        verticalSpeed += Physics.gravity.y * Time.deltaTime;

        Vector3 velocity = Vector3.up * verticalSpeed;

        if (direction.sqrMagnitude > 0.0001f)
        {
            direction.Normalize();
            velocity += direction * moveSpeed;

            Quaternion look = Quaternion.LookRotation(direction, Vector3.up);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, look, turnSpeed * Time.deltaTime);
        }

        controller.Move(velocity * Time.deltaTime);
    }

    /// <summary>
    /// **進めなくなったか。** 目的地までの距離が <see cref="stuckSeconds"/> のあいだ 0.3m も縮まなければ true。
    /// </summary>
    private bool IsStuck(float distance)
    {
        if (distance < bestDistance - 0.3f)
        {
            bestDistance = distance;
            bestDistanceTime = Time.time;
            return false;
        }

        return Time.time - bestDistanceTime > stuckSeconds;
    }

    private void ResetStuck()
    {
        bestDistance = float.MaxValue;
        bestDistanceTime = Time.time;
    }

    /// <summary>落ちたとき：持っている物を離し、最初の場所へ戻る。</summary>
    private void ResetToStart()
    {
        Drop();
        SetTarget(null);
        verticalSpeed = 0f;

        // CharacterController が付いていると位置を書き換えられないので、いったん切る
        controller.enabled = false;
        transform.SetPositionAndRotation(startPosition, startRotation);
        controller.enabled = true;
    }

    /// <summary>持ち上げた素材と自分の体がぶつからないようにする（頭の上で引っかからないため）。</summary>
    private void SetIgnoreCollision(HookableObject item, bool ignore)
    {
        // 消える途中（シーンを移るときなど）は、体のほうが先に消えていることがある
        if (item == null || controller == null)
        {
            return;
        }

        foreach (Collider part in item.GetComponentsInChildren<Collider>(true))
        {
            Physics.IgnoreCollision(controller, part, ignore);
        }
    }

    private static float FlatDistance(Vector3 a, Vector3 b)
    {
        a.y = 0f;
        b.y = 0f;
        return Vector3.Distance(a, b);
    }
}
