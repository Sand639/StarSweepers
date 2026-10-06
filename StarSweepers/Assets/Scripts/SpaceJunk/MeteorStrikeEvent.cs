using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// 1回分の隕石攻撃。予告、落下、爆風、見た目の後片付けまでを受け持つ。
/// </summary>
[RequireComponent(typeof(NetworkObject))]
public class MeteorStrikeEvent : NetworkBehaviour
{
    [Header("見た目")]
    [SerializeField] private GameObject warningPrefab;
    [SerializeField] private GameObject meteorPrefab;
    [SerializeField] private GameObject impactVfxPrefab;
    [Min(0f)] [SerializeField] private float meteorStartHeight = 25f;
    [Min(0f)] [SerializeField] private float impactVfxLifetime = 3f;

    [Header("当たり判定")]
    [SerializeField] private LayerMask hitLayers = ~0;

    private readonly NetworkVariable<bool> configured = new NetworkVariable<bool>(false);
    private readonly NetworkVariable<double> impactServerTime = new NetworkVariable<double>(0d);
    private readonly NetworkVariable<float> radius = new NetworkVariable<float>(4f);
    private readonly NetworkVariable<float> fallDuration = new NetworkVariable<float>(1f);
    private readonly NetworkVariable<float> playerHorizontal = new NetworkVariable<float>(12f);
    private readonly NetworkVariable<float> playerLift = new NetworkVariable<float>(6f);
    private readonly NetworkVariable<float> playerStunDuration = new NetworkVariable<float>(2f);
    private readonly NetworkVariable<float> objectHorizontal = new NetworkVariable<float>(14f);
    private readonly NetworkVariable<float> objectLift = new NetworkVariable<float>(7f);

    private GameObject warningInstance;
    private GameObject meteorInstance;
    private bool impactApplied;
    private double finishTime;

    /// <summary>Host上で攻撃が完全に終わったときだけ呼ばれる。</summary>
    public event Action<MeteorStrikeEvent> ServerFinished;

    public override void OnNetworkSpawn()
    {
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
        if (NetworkManager != null && NetworkManager.IsListening && IsSpawned && !IsServer)
        {
            return;
        }

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

    private void Update()
    {
        if (!configured.Value)
        {
            return;
        }

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
            finishTime = now + Math.Max(0.05d, impactVfxLifetime);
        }

        if (impactApplied && now >= finishTime && CanFinishOnThisPC())
        {
            Finish();
        }
    }

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

    private bool CanMoveItemsOnThisPC()
    {
        NetworkManager manager = NetworkManager.Singleton;
        return manager == null || !manager.IsListening || manager.IsServer;
    }

    private bool CanFinishOnThisPC()
    {
        NetworkManager manager = NetworkManager.Singleton;
        return manager == null || !manager.IsListening || IsServer;
    }

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

    private double SharedTime()
    {
        NetworkManager manager = NetworkManager.Singleton;
        if (manager != null && manager.IsListening)
        {
            return manager.ServerTime.Time;
        }
        return Time.timeAsDouble;
    }

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
        Gizmos.color = new Color(1f, 0.2f, 0.05f, 0.25f);
        Gizmos.DrawSphere(transform.position, radius.Value);
        Gizmos.color = new Color(1f, 0.2f, 0.05f, 0.9f);
        Gizmos.DrawWireSphere(transform.position, radius.Value);
    }
}
