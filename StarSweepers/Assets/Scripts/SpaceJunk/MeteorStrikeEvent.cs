using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// 1回分の隕石攻撃。予告、落下、爆風、見た目の後片付けまでを受け持つ。
///
/// <see cref="MeteorManager"/> がホストで1発ごとに出すプレハブ（`Assets/Prefabs/SpaceJunk/Gimmick/Meteor/`）。
/// 置かれた位置が爆心。流れは **地面の予告を出す → 着弾の Fall Seconds 前から隕石が上空から落ちてくる →
/// 着弾で爆風（吹き飛ばし・スタン・導火線の点火）と爆発の見た目 → 見た目が消えたら自分を消す**。
///
/// ## オンラインのとき
///
/// ・攻撃の条件（着弾の時刻・半径・飛ばす強さ）はホストが <see cref="InitializeServer"/> で決め、NetworkVariable で全員へ配る
/// ・**着弾の時刻は全員で同期された時計（ServerTime）で持つ**ので、予告・落下・着弾がどのPCでも同じタイミングになる
/// ・プレイヤーの吹き飛ばしは**それぞれのPCが自分の体だけ**受け取る（<c>FishingPlayerController.Launch</c> が他人の体では何もしない）
/// ・素材・爆弾を飛ばすのは**ホストだけ**（物資の動きはホストが決めているため）。消すのもホストだけ
///
/// 1人用（NetworkManager が無いとき）でも、同じ流れでそのまま動く。
/// </summary>
[RequireComponent(typeof(NetworkObject))]
public class MeteorStrikeEvent : NetworkBehaviour
{
    [Header("見た目")]
    [Tooltip("地面に出す予告（危険範囲）の見た目。X と Z を爆風の直径に合わせて拡大する")]
    [SerializeField] private GameObject warningPrefab;

    [Tooltip("落ちてくる隕石の見た目。落ちている間だけ表示する")]
    [SerializeField] private GameObject meteorPrefab;

    [Tooltip("着弾したときの爆発の見た目。Impact Vfx Lifetime 秒後に消す")]
    [SerializeField] private GameObject impactVfxPrefab;

    [Tooltip("隕石が落ち始める高さ（爆心からの m）")]
    [Min(0f)] [SerializeField] private float meteorStartHeight = 25f;

    [Tooltip("爆発の見た目を出しておく秒数。これが過ぎたら攻撃は終わり")]
    [Min(0f)] [SerializeField] private float impactVfxLifetime = 3f;

    [Header("当たり判定")]
    [Tooltip("爆風に当たるレイヤー。初期値はすべて")]
    [SerializeField] private LayerMask hitLayers = ~0;

    // ---- 攻撃の条件。ホストが InitializeServer で入れ、全員へ配られる ----

    /// <summary>条件が入ったか。参加者のPCは、これが true になってから予告を出し始める。</summary>
    private readonly NetworkVariable<bool> configured = new NetworkVariable<bool>(false);

    /// <summary>着弾する時刻（全員で同期された時計 ServerTime の秒）。</summary>
    private readonly NetworkVariable<double> impactServerTime = new NetworkVariable<double>(0d);

    /// <summary>爆風（予告・当たり判定）の半径（m）。</summary>
    private readonly NetworkVariable<float> radius = new NetworkVariable<float>(4f);

    /// <summary>着弾の何秒前から隕石が落ちて見えるか。</summary>
    private readonly NetworkVariable<float> fallDuration = new NetworkVariable<float>(1f);

    private readonly NetworkVariable<float> playerHorizontal = new NetworkVariable<float>(12f);
    private readonly NetworkVariable<float> playerLift = new NetworkVariable<float>(6f);
    private readonly NetworkVariable<float> playerStunDuration = new NetworkVariable<float>(2f);
    private readonly NetworkVariable<float> objectHorizontal = new NetworkVariable<float>(14f);
    private readonly NetworkVariable<float> objectLift = new NetworkVariable<float>(7f);

    /// <summary>このPCに出した予告の見た目（まだ出していなければ null）。</summary>
    private GameObject warningInstance;

    /// <summary>このPCに出した隕石の見た目（まだ出していなければ null）。</summary>
    private GameObject meteorInstance;

    /// <summary>このPCで着弾の処理を済ませたか（1回しか起こさないため）。</summary>
    private bool impactApplied;

    /// <summary>攻撃を終わりにしてよい時刻（着弾 + 爆発の見た目の秒数）。</summary>
    private double finishTime;

    /// <summary>Host上で攻撃が完全に終わったときだけ呼ばれる。</summary>
    public event Action<MeteorStrikeEvent> ServerFinished;

    public override void OnNetworkSpawn()
    {
        // 参加者のPCでは、条件が届いたタイミングで予告を出す。
        // あとから入った人は生まれた時点で条件が届いていることがあるので、その場合はすぐ出す
        configured.OnValueChanged += OnConfiguredChanged;
        if (configured.Value)
        {
            CreateWarningAndMeteor();
        }
    }

    public override void OnNetworkDespawn()
    {
        configured.OnValueChanged -= OnConfiguredChanged;
        CleanupVisuals();
    }

    public override void OnDestroy()
    {
        CleanupVisuals();
        base.OnDestroy();
    }

    /// <summary>HostがSpawn直後に、全PCへ共有する攻撃条件を設定する。</summary>
    public void InitializeServer(
        float warningSeconds,
        float fallSeconds,
        float impactRadius,
        float playerKnockbackSpeed,
        float playerKnockbackLift,
        float playerStunSeconds,
        float objectKnockbackSpeed,
        float objectKnockbackLift)
    {
        // 通信中の参加者のPCでは NetworkVariable に書けない（ホストの値が正）
        if (NetworkManager != null && NetworkManager.IsListening && IsSpawned && !IsServer)
        {
            return;
        }

        // 「今から Warning Seconds 後」を着弾の時刻にする。落ちて見える時間は予告の時間より長くはできない
        impactServerTime.Value = SharedTime() + Math.Max(0d, warningSeconds);
        radius.Value = Mathf.Max(0.01f, impactRadius);
        fallDuration.Value = Mathf.Clamp(fallSeconds, 0f, Mathf.Max(0f, warningSeconds));
        playerHorizontal.Value = Mathf.Max(0f, playerKnockbackSpeed);
        playerLift.Value = Mathf.Max(0f, playerKnockbackLift);
        playerStunDuration.Value = Mathf.Max(0f, playerStunSeconds);
        objectHorizontal.Value = Mathf.Max(0f, objectKnockbackSpeed);
        objectLift.Value = Mathf.Max(0f, objectKnockbackLift);
        configured.Value = true;

        // NetworkManagerを使わない単体確認ではOnNetworkSpawnが来ない。
        CreateWarningAndMeteor();
    }

    private void OnConfiguredChanged(bool before, bool after)
    {
        if (after)
        {
            CreateWarningAndMeteor();
        }
    }

    /// <summary>
    /// 毎フレーム、全員共通の時計と着弾時刻を比べて進める。
    /// 予告中 → 落下の見た目 → 着弾（1回だけ）→ 爆発の見た目が消えるのを待って終わり。
    /// </summary>
    private void Update()
    {
        if (!configured.Value)
        {
            return;
        }

        // まだ出していなければここで出す（OnNetworkSpawn で出しそこねた場合の保険）
        CreateWarningAndMeteor();
        double now = SharedTime();
        double remaining = impactServerTime.Value - now;

        UpdateMeteorLook((float)remaining);

        if (!impactApplied && remaining <= 0d)
        {
            impactApplied = true;
            HideWarningAndMeteor();
            SpawnImpactVfx();
            ApplyImpact();
            // 見た目の秒数が 0 でも、少なくとも 0.05 秒は待ってから消す
            finishTime = now + Math.Max(0.05d, impactVfxLifetime);
        }

        // 消すのはホストだけ（参加者のPCは Despawn で一緒に消える）
        if (impactApplied && now >= finishTime && CanFinishOnThisPC())
        {
            Finish();
        }
    }

    /// <summary>
    /// 予告の見た目と隕石の見た目をこのPCに出す（それぞれ1回だけ）。
    /// 予告は X と Z を爆風の直径（半径×2）に合わせる。隕石は落ち始めるまで隠しておく。
    /// </summary>
    private void CreateWarningAndMeteor()
    {
        if (!configured.Value)
        {
            return;
        }

        if (warningInstance == null && warningPrefab != null)
        {
            warningInstance = Instantiate(warningPrefab, transform.position, transform.rotation);
            Vector3 scale = warningInstance.transform.localScale;
            scale.x = radius.Value * 2f;
            scale.z = radius.Value * 2f;
            warningInstance.transform.localScale = scale;
        }

        if (meteorInstance == null && meteorPrefab != null)
        {
            meteorInstance = Instantiate(
                meteorPrefab,
                transform.position + Vector3.up * meteorStartHeight,
                meteorPrefab.transform.rotation);
            meteorInstance.SetActive(false);
        }
    }

    /// <summary>
    /// 隕石の見た目を動かす。着弾の Fall Duration 秒前から見え始め、
    /// Meteor Start Height の高さから爆心まで一直線に落ちて、着弾の瞬間に爆心へ着く。
    /// </summary>
    private void UpdateMeteorLook(float remainingSeconds)
    {
        if (meteorInstance == null)
        {
            return;
        }

        float fallSeconds = fallDuration.Value;
        bool falling = remainingSeconds <= fallSeconds && remainingSeconds > 0f;
        if (meteorInstance.activeSelf != falling)
        {
            meteorInstance.SetActive(falling);
        }

        if (!falling)
        {
            return;
        }

        // 0＝落ち始め、1＝着弾。Fall Duration がほぼ 0 なら最初から着弾位置
        float progress = fallSeconds <= 0.0001f
            ? 1f
            : 1f - Mathf.Clamp01(remainingSeconds / fallSeconds);
        meteorInstance.transform.position = Vector3.Lerp(
            transform.position + Vector3.up * meteorStartHeight,
            transform.position,
            progress);
    }

    private void HideWarningAndMeteor()
    {
        if (warningInstance != null)
        {
            warningInstance.SetActive(false);
        }
        if (meteorInstance != null)
        {
            meteorInstance.SetActive(false);
        }
    }

    private void SpawnImpactVfx()
    {
        if (impactVfxPrefab == null)
        {
            return;
        }

        GameObject effect = Instantiate(impactVfxPrefab, transform.position, Quaternion.identity);
        Destroy(effect, Mathf.Max(0.05f, impactVfxLifetime));
    }

    /// <summary>
    /// 着弾の瞬間に、爆心から半径の中にいるプレイヤーと物資（素材・爆弾）を吹き飛ばす。
    /// 1人・1個につき1回だけ（当たり判定が複数あっても二重に飛ばさない）。
    /// プレイヤーは全員のPCで処理する（自分の体だけが実際に飛ぶ）。物資はホストだけが処理する。
    /// </summary>
    private void ApplyImpact()
    {
        Collider[] hits = Physics.OverlapSphere(
            transform.position,
            radius.Value,
            hitLayers,
            QueryTriggerInteraction.Ignore);

        HashSet<GameObject> handledPlayers = new HashSet<GameObject>();
        HashSet<GameObject> handledItems = new HashSet<GameObject>();
        bool canMoveItems = CanMoveItemsOnThisPC();

        foreach (Collider hit in hits)
        {
            FishingPlayerController player = hit.GetComponentInParent<FishingPlayerController>();
            if (player != null && player.isActiveAndEnabled && handledPlayers.Add(player.gameObject))
            {
                // 爆心のちょうど真上にいて向きが決まらないときは、その人の向いている方へ飛ばす
                Vector3 velocity = CalculateKnockbackVelocity(
                    transform.position,
                    player.transform.position,
                    player.transform.forward,
                    playerHorizontal.Value,
                    playerLift.Value);
                ApplyPlayerImpact(
                    player,
                    player.GetComponent<PlayerStun>(),
                    velocity,
                    playerStunDuration.Value);
                continue;
            }

            if (!canMoveItems)
            {
                continue;
            }

            HookableObject item = hit.GetComponentInParent<HookableObject>();
            if (item == null || item.IsVanished || item.Body == null || !handledItems.Add(item.gameObject))
            {
                continue;
            }

            Vector3 itemVelocity = CalculateKnockbackVelocity(
                transform.position,
                item.Body.worldCenterOfMass,
                transform.forward,
                objectHorizontal.Value,
                objectLift.Value);

            // オンラインの物資（FishingNetSupply）は、引っ掛けを外してから飛ばすところまでホスト側の処理に任せる。
            // 1人用の物資は、ここで引っ掛けを外し、物理を動かして飛ばす
            FishingNetSupply netSupply = item.GetComponent<FishingNetSupply>();
            if (netSupply != null && netSupply.IsSpawned)
            {
                netSupply.ServerApplyExplosion(itemVelocity);
            }
            else
            {
                ThrowController.ReleaseTargetForExplosion(item);
                item.Body.isKinematic = false;
                item.Body.AddForce(itemVelocity, ForceMode.VelocityChange);
            }

            // 爆弾なら導火線に火をつける（オンライン用の爆弾は全員へ同期される口を使う）
            FishingNetBomb netBomb = item.GetComponent<FishingNetBomb>();
            if (netBomb != null && netBomb.IsSpawned)
            {
                netBomb.RequestLightFuse();
            }
            else
            {
                item.GetComponent<ExplosiveObject>()?.LightFuse();
            }
        }
    }

    /// <summary>プレイヤーを吹き飛ばし、既存のスタン機能で一定時間操作不能にする。</summary>
    public static void ApplyPlayerImpact(
        ILaunchable launchable,
        PlayerStun stun,
        Vector3 velocity,
        float stunSeconds)
    {
        if (launchable == null)
        {
            return;
        }

        launchable.Launch(velocity);
        if (stun != null && stunSeconds > 0f)
        {
            stun.Stun(stunSeconds);
        }
    }

    /// <summary>このPCが物資を飛ばしてよいか。1人用なら自分、通信中ならホストだけ。</summary>
    private bool CanMoveItemsOnThisPC()
    {
        NetworkManager manager = NetworkManager.Singleton;
        return manager == null || !manager.IsListening || manager.IsServer;
    }

    /// <summary>このPCが攻撃を終わらせて（消して）よいか。1人用なら自分、通信中ならホストだけ。</summary>
    private bool CanFinishOnThisPC()
    {
        NetworkManager manager = NetworkManager.Singleton;
        return manager == null || !manager.IsListening || IsServer;
    }

    /// <summary>攻撃を終わりにする。管理役（<see cref="MeteorManager"/>）に知らせてから、自分を消す。</summary>
    private void Finish()
    {
        ServerFinished?.Invoke(this);

        if (IsSpawned)
        {
            NetworkObject.Despawn(true);
        }
        else
        {
            Destroy(gameObject);
        }
    }

    /// <summary>全員で同じ時計。通信中は ServerTime、1人用のときは再生を始めてからの秒数。</summary>
    private double SharedTime()
    {
        NetworkManager manager = NetworkManager.Singleton;
        if (manager != null && manager.IsListening)
        {
            return manager.ServerTime.Time;
        }
        return Time.timeAsDouble;
    }

    /// <summary>このPCに出した予告と隕石の見た目を消す（見た目は通信オブジェクトでないので、PCごとに消す）。</summary>
    private void CleanupVisuals()
    {
        if (warningInstance != null)
        {
            Destroy(warningInstance);
            warningInstance = null;
        }
        if (meteorInstance != null)
        {
            Destroy(meteorInstance);
            meteorInstance = null;
        }
    }

    /// <summary>爆心から対象の外側へ向かう、横方向＋上方向の速度を計算する。</summary>
    public static Vector3 CalculateKnockbackVelocity(
        Vector3 impactPosition,
        Vector3 targetPosition,
        Vector3 fallbackDirection,
        float horizontalSpeed,
        float lift)
    {
        Vector3 direction = targetPosition - impactPosition;
        direction.y = 0f;

        // 爆心の真上にいて向きが決まらないときは、渡された予備の向き → それも無ければ奥（+Z）
        if (direction.sqrMagnitude < 0.0001f)
        {
            direction = fallbackDirection;
            direction.y = 0f;
        }
        if (direction.sqrMagnitude < 0.0001f)
        {
            direction = Vector3.forward;
        }

        return direction.normalized * Mathf.Max(0f, horizontalSpeed)
             + Vector3.up * Mathf.Max(0f, lift);
    }

    private void OnDrawGizmosSelected()
    {
        // 爆風の範囲を、シーン画面に赤い球で出す
        Gizmos.color = new Color(1f, 0.2f, 0.05f, 0.25f);
        Gizmos.DrawSphere(transform.position, radius.Value);
        Gizmos.color = new Color(1f, 0.2f, 0.05f, 0.9f);
        Gizmos.DrawWireSphere(transform.position, radius.Value);
    }
}
