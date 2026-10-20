using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 導火線・見た目は通常の爆弾と同じ。
/// プレイヤーにはスタンを与えず、物資も消さずに爆弾から離れる向きに吹き飛ばす。
/// ExplosiveObject を継承するため、既存のフック・通信・カウントダウンがそのまま使える。
/// </summary>
/// <remarks>
/// プレハブ：`Assets/Prefabs/Sou/KnockBackBoom.prefab`（通常の爆弾のスクリプトをこれに差し替えたもの）。
/// 親の <see cref="ExplosiveObject"/> が「範囲内の誰に・何に当たったか」を調べ、
/// ここは**当たった相手に何をするか**だけを差し替えている。
/// 親から継承して見える Player Stun Seconds は、この爆弾では使わない。
/// 詳しくは `Documents/機能ドキュメント/釣りの吹き飛ばす爆弾.md`。
/// </remarks>
public class KnockBackExplosiveObject : ExplosiveObject
{
    [Header("吹き飛ばす強さ（スタンなし）")]
    [Tooltip("横方向に吹き飛ばす速さ（1秒あたりのメートル）")]
    [Min(0f)]
    [SerializeField] private float knockbackSpeed = 10f;

    [Tooltip("上向きに飛ばす速さ。0なら横方向だけ（1秒あたりのメートル）")]
    [Min(0f)]
    [SerializeField] private float knockbackLift = 5f;

    [Header("物資を吹き飛ばす強さ")]
    [Tooltip("鉤で引っ掛けられる物資に加える横方向の速さ（m/s）。重さによらず同じ勢いを加える")]
    [Min(0f)]
    [SerializeField] private float objectKnockbackSpeed = 12f;

    [Tooltip("物資に加える上方向の速さ（m/s）。0なら横方向だけ")]
    [Min(0f)]
    [SerializeField] private float objectKnockbackLift = 6f;

    /// <summary>当たったプレイヤーを、爆弾から離れる向き（水平）＋上向きに吹き飛ばす。スタンはさせない。</summary>
    protected override void ApplyPlayerEffect(Collider hit, HashSet<GameObject> handled)
    {
        // 操作が止まっている体（オンラインの他人の体など）には効かせない。
        // handled.Add が false ＝ この爆発でもう処理した体（当たり判定が複数あっても1回だけ）
        FishingPlayerController player = hit.GetComponentInParent<FishingPlayerController>();
        if (player == null || !player.isActiveAndEnabled || !handled.Add(player.gameObject))
        {
            return;
        }

        // 爆弾からプレイヤーへの水平方向。0.0001（1cm の2乗）未満なら「ほぼ真上／真下」とみなす
        Vector3 direction = player.transform.position - transform.position;
        direction.y = 0f;
        // 真上で爆発した場合も、止まらずプレイヤーの前方へ飛ばす。
        if (direction.sqrMagnitude < 0.0001f)
        {
            direction = player.transform.forward;
            direction.y = 0f;
        }
        // 前方も水平成分が無い（真上や真下を向いている）ときの最後の保険
        if (direction.sqrMagnitude < 0.0001f)
        {
            direction = Vector3.forward;
        }

        player.Launch(direction.normalized * knockbackSpeed + Vector3.up * knockbackLift);
    }

    /// <summary>当たった物資を、消さずに爆弾から離れる向き＋上向きに吹き飛ばす。</summary>
    protected override void ApplyHookableEffect(HookableObject other)
    {
        if (other.IsVanished || other.Body == null)
        {
            return;
        }

        // 当たり判定の中心ではなく重心から向きを取る（縦に長い物でも自然に飛ぶ）
        Vector3 direction = other.Body.worldCenterOfMass - transform.position;
        direction.y = 0f;
        if (direction.sqrMagnitude < 0.0001f)
        {
            direction = Vector3.forward;
        }
        Vector3 velocity = direction.normalized * objectKnockbackSpeed
                         + Vector3.up * objectKnockbackLift;

        FishingNetSupply netSupply = other.GetComponent<FishingNetSupply>();
        if (netSupply != null && netSupply.IsSpawned)
        {
            // 爆発は各PCで起きるが、物資を飛ばすのはホストだけ。
            // （ホスト側で糸を外し、持ち主を戻してから飛ばす。参加者のPCで呼んでも中で無視される）
            netSupply.ServerApplyExplosion(velocity);
            return;
        }

        // 1人用：誰かが引き寄せ中ならまず糸を外す（引き寄せ中は isKinematic で止めてあるため、
        // 外したうえで物理を戻さないと飛ばない）
        ThrowController.ReleaseTargetForExplosion(other);
        other.Body.isKinematic = false;
        // VelocityChange ＝ 質量に関係なく、同じ速さをそのまま足す
        other.Body.AddForce(velocity, ForceMode.VelocityChange);
    }
}
