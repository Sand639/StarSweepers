using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// **プレイヤー（本実装）を、宇宙ごみのオンラインで動かすための上乗せ。**（2026/10/28・小野田さんの依頼）
///
/// 宇宙ごみのオンラインの体（`Assets/Prefabs/SpaceJunk/Online/SpaceJunkSweeperPlayer.prefab`）にだけ付ける。
/// 参加番号・名前・チーム分け・出てくる場所・カメラは、今の体と同じ部品（<see cref="FishingNetPlayer"/>・<see cref="SpaceJunkPlayerSetup"/>）がそのまま受け持つ。
/// ここでやるのは次の4つ。
///
/// | すること | どのPCで |
/// | --- | --- |
/// | **名前・チームの色・リングの形**を、Sweeper の見た目に入れる（形はチームの中で何人目か） | 全員のぶん |
/// | **狙い・光線の様子を配る**（段階・向き・長さ・光線の先・巻き取っている物）。クリティカルの光も知らせる | 自分が操作しているぶん |
/// | 配られた様子で、**狙いの帯と光線を出す**（操作はしない） | ほかの人のぶん |
/// | 試合・始まりの演出・ロビーの端末・復活時間で**体が止められたら、Sweeper の操作も止める** | 自分が操作しているぶん |
///
/// ### 体の止め方について
///
/// 試合の係などは、今の体の歩く部品（<see cref="FishingPlayerController"/>）の ON/OFF で体を止める。
/// 新しい体にもその部品を「重力と吹き飛ばしの受け口」として残してあるので、**その ON/OFF を見て、Sweeper の操作（歩く・狙う・撃つ）も同じにする。**
/// 止まった瞬間に、出していた光線はしまう（つかんでいた物資は投げずに離す）。
/// </summary>
[RequireComponent(typeof(FishingNetPlayer))]
public class SweeperNetSync : NetworkBehaviour
{
    /// <summary>「巻き取っている物が無い」を表す値。</summary>
    private const ulong NoItem = ulong.MaxValue;

    /// <summary>1フレームでこれより遠くへ動いたら「出てくる場所へ戻された」とみなして、光線をしまう（m）。</summary>
    private const float TeleportDistance = 3f;

    /// <summary>名前・チーム・リングの形を見直す間隔（秒）。毎フレーム見るほどは変わらないため。</summary>
    private const float ProfileInterval = 0.25f;

    /// <summary>「ホストの記録では自分が引いているのに、手に持っていない」を見る間隔（秒）。</summary>
    private const float ReconcileInterval = 1f;

    // ---- 本人が書き、全員が読むもの ----

    private readonly NetworkVariable<int> shotPhase = new NetworkVariable<int>(
        (int)SweeperShooter.ShotPhase.Idle, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

    private readonly NetworkVariable<Vector3> shotDirection = new NetworkVariable<Vector3>(
        Vector3.forward, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

    private readonly NetworkVariable<float> shotReach = new NetworkVariable<float>(
        0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

    private readonly NetworkVariable<Vector3> beamTip = new NetworkVariable<Vector3>(
        Vector3.zero, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

    private readonly NetworkVariable<ulong> reelItemId = new NetworkVariable<ulong>(
        NoItem, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

    private FishingNetPlayer netPlayer;
    private SweeperPlayer profile;
    private SweeperRing ring;
    private SweeperMover mover;
    private SweeperAim aim;
    private SweeperShooter shooter;
    private SweeperReel reel;

    /// <summary>重力と吹き飛ばしを受け持つ、今の体の歩く部品。これの ON/OFF に Sweeper の操作を合わせる。</summary>
    private FishingPlayerController body;

    private bool controlAllowed = true;
    private Vector3 lastPosition;
    private float profileTimer;
    private string lastName;
    private int lastTeam = int.MinValue;
    private int lastShape = -1;
    private float reconcileTimer;

    /// <summary>前回の見直しで「ホストの記録と食い違っていた」物資。2回続けて食い違ったら直す。</summary>
    private readonly HashSet<ulong> suspectedItems = new HashSet<ulong>();
    private readonly HashSet<ulong> suspectedNow = new HashSet<ulong>();

    private ulong cachedItemId = NoItem;
    private HookableObject cachedItem;

    private void Awake()
    {
        netPlayer = GetComponent<FishingNetPlayer>();
        profile = GetComponent<SweeperPlayer>();
        ring = GetComponent<SweeperRing>();
        mover = GetComponent<SweeperMover>();
        aim = GetComponent<SweeperAim>();
        shooter = GetComponent<SweeperShooter>();
        reel = GetComponent<SweeperReel>();
        body = GetComponent<FishingPlayerController>();
    }

    public override void OnNetworkSpawn()
    {
        lastPosition = transform.position;

        if (IsOwner)
        {
            if (reel != null)
            {
                reel.EffectShown += OnEffectShown;
            }
        }
        else
        {
            // ほかの人の体：狙いのカーソルは本人にしか要らない
            if (aim != null)
            {
                aim.SetCursorVisible(false);
            }
        }

        ApplyProfile();
    }

    public override void OnNetworkDespawn()
    {
        if (reel != null)
        {
            reel.EffectShown -= OnEffectShown;
        }
    }

    private void Update()
    {
        profileTimer -= Time.unscaledDeltaTime;
        if (profileTimer <= 0f)
        {
            profileTimer = ProfileInterval;
            ApplyProfile();
        }

        if (!IsSpawned || !IsOwner)
        {
            return;
        }

        FollowBodyLock();
        CancelShotWhenTeleported();
        ReconcileClaims();
    }

    private void LateUpdate()
    {
        if (!IsSpawned || shooter == null)
        {
            return;
        }

        if (IsOwner)
        {
            SendShot();
        }
        else
        {
            ShowShot();
        }
    }

    // ------------------------------------------------------------
    // 名前・チームの色・リングの形（全員のぶん）
    // ------------------------------------------------------------

    /// <summary>ホストが決めた名前とチームを Sweeper の見た目に入れ、リングの形をチームの中で何人目かで決める。</summary>
    private void ApplyProfile()
    {
        if (netPlayer == null || profile == null || !netPlayer.IsSpawned)
        {
            return;
        }

        SpaceJunkSession session = SpaceJunkSession.Current;
        int team = Mathf.Max(0, session != null ? session.TeamOf(netPlayer.PlayerKey) : netPlayer.TeamIndex);
        string displayName = netPlayer.DisplayName;

        if (team != lastTeam || displayName != lastName)
        {
            lastTeam = team;
            lastName = displayName;
            profile.SetProfile(displayName, team);
        }

        int shape = ShapeIndex(session, team);
        if (ring != null && shape != lastShape)
        {
            lastShape = shape;
            ring.Shape = (SweeperRing.RingShape)shape;
        }
    }

    /// <summary>
    /// リングの形の番号。**チームの中で何人目か**（ロビーで入った順）で、円 → 三角 → 四角 → 星 → 六角形。
    /// チームの席が無ければ参加番号で決める。どのPCで計算しても同じになるので、通信では配らない。
    /// </summary>
    private int ShapeIndex(SpaceJunkSession session, int team)
    {
        int shapeCount = System.Enum.GetValues(typeof(SweeperRing.RingShape)).Length;

        if (session != null)
        {
            int order = 0;
            foreach (SpaceJunkPlayerSlot slot in session.Slots)
            {
                if (slot.Team != team)
                {
                    continue;
                }

                if (slot.ClientId == netPlayer.PlayerKey)
                {
                    return order % shapeCount;
                }

                order++;
            }
        }

        return Mathf.Max(0, netPlayer.PlayerIndex) % shapeCount;
    }

    // ------------------------------------------------------------
    // 体が止められたとき（自分のぶん）
    // ------------------------------------------------------------

    /// <summary>今の体の歩く部品が止められていたら、Sweeper の操作（歩く・狙う・撃つ）も止める。戻ったら戻す。</summary>
    private void FollowBodyLock()
    {
        bool allowed = body == null || body.enabled;
        if (allowed == controlAllowed)
        {
            return;
        }

        controlAllowed = allowed;

        // 撃つ部品は止まるときに光線をしまう（つかんでいた物資は投げずに離す）
        if (shooter != null) shooter.enabled = allowed;
        if (mover != null) mover.enabled = allowed;
        if (aim != null) aim.enabled = allowed;
    }

    /// <summary>出てくる場所へ戻された（R キー・落下・ラウンドの始まり）ら、出していた光線をしまう。</summary>
    private void CancelShotWhenTeleported()
    {
        Vector3 position = transform.position;
        if ((position - lastPosition).sqrMagnitude > TeleportDistance * TeleportDistance && shooter != null)
        {
            shooter.CancelShot();
        }
        lastPosition = position;
    }

    /// <summary>
    /// **ホストの記録では自分が引いているのに、手に持っていない物資**があれば、離したと送り直す。
    /// 通信の遅れで「離した」が捨てられたときに、誰も引けないまま残らないようにするため（今の体の <see cref="SpaceJunkLocalStateCheck"/> と同じ考え）。
    /// </summary>
    private void ReconcileClaims()
    {
        reconcileTimer += Time.unscaledDeltaTime;
        if (reconcileTimer < ReconcileInterval || netPlayer == null)
        {
            return;
        }
        reconcileTimer = 0f;

        int me = Mathf.Max(0, netPlayer.PlayerIndex);
        HookableObject holding = reel != null ? reel.Item : null;
        suspectedNow.Clear();

        foreach (HookableObject item in HookableObject.All)
        {
            if (item == null || item.IsVanished || item == holding)
            {
                continue;
            }

            FishingNetSupply netSupply = item.GetComponent<FishingNetSupply>();
            if (netSupply == null || !netSupply.IsSpawned || netSupply.HookedByPlayerIndex != me)
            {
                continue;
            }

            ulong id = netSupply.NetworkObjectId;
            if (suspectedItems.Contains(id))
            {
                Debug.LogWarning($"[NET][答え合わせ] {item.name}：ホストの記録では自分（新しい体）が引いていましたが、" +
                                 "手に持っていないので「離した」を送り直しました。");
                netSupply.RequestReconcileRelease(me);
            }
            else
            {
                suspectedNow.Add(id);
            }
        }

        suspectedItems.Clear();
        suspectedItems.UnionWith(suspectedNow);
    }

    // ------------------------------------------------------------
    // 狙い・光線の様子（自分のぶん → ほかの人のぶん）
    // ------------------------------------------------------------

    /// <summary>自分の光線の様子を配る。何もしていない間は、段階だけ（ほかの値は送らない）。</summary>
    private void SendShot()
    {
        SweeperShooter.ShotPhase phase = shooter.Phase;
        if (shotPhase.Value != (int)phase)
        {
            shotPhase.Value = (int)phase;
        }

        if (phase == SweeperShooter.ShotPhase.Idle)
        {
            if (reelItemId.Value != NoItem)
            {
                reelItemId.Value = NoItem;
            }
            return;
        }

        shotDirection.Value = shooter.ShotDirection;
        shotReach.Value = shooter.CurrentReach;
        beamTip.Value = shooter.BeamTip;

        ulong itemId = IdOf(shooter.ReelItem);
        if (reelItemId.Value != itemId)
        {
            reelItemId.Value = itemId;
        }
    }

    /// <summary>ほかの人の体に、配られた光線の様子を出す。</summary>
    private void ShowShot()
    {
        shooter.ShowRemote((SweeperShooter.ShotPhase)shotPhase.Value, shotDirection.Value, shotReach.Value,
            beamTip.Value, ItemOf(reelItemId.Value));
    }

    private static ulong IdOf(HookableObject item)
    {
        if (item == null)
        {
            return NoItem;
        }

        NetworkObject networkObject = item.GetComponent<NetworkObject>();
        return networkObject != null && networkObject.IsSpawned ? networkObject.NetworkObjectId : NoItem;
    }

    /// <summary>通信上の番号から物資を探す（同じ番号が続く間は覚えておく）。</summary>
    private HookableObject ItemOf(ulong id)
    {
        if (id == NoItem || NetworkManager == null)
        {
            return null;
        }

        if (id == cachedItemId && cachedItem != null)
        {
            return cachedItem;
        }

        cachedItemId = id;
        cachedItem = NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(id, out NetworkObject networkObject) && networkObject != null
            ? networkObject.GetComponent<HookableObject>()
            : null;
        return cachedItem;
    }

    // ------------------------------------------------------------
    // クリティカルの光（自分のPC → ホスト → ほかの人のPC）
    // ------------------------------------------------------------

    private void OnEffectShown(SweeperReel.Effect kind, HookableObject item)
    {
        ulong id = IdOf(item);
        if (id != NoItem)
        {
            EffectServerRpc((int)kind, id);
        }
    }

    [ServerRpc]
    private void EffectServerRpc(int kind, ulong itemId)
    {
        EffectClientRpc(kind, itemId);
    }

    [ClientRpc]
    private void EffectClientRpc(int kind, ulong itemId)
    {
        // 本人の画面にはもう出ている
        if (IsOwner || reel == null)
        {
            return;
        }

        reel.ShowEffect((SweeperReel.Effect)kind, ItemOf(itemId));
    }
}
