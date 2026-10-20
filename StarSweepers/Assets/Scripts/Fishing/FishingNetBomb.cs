using Unity.Netcode;
using UnityEngine;

/// <summary>
/// **オンラインの爆発物の、導火線を全員でそろえる部品。**
///
/// 火をつけるのは引っ掛けた本人だけなので、そのままだと
/// **本人の画面でしか導火線が燃えず、他の人には突然爆発したように見える。**
///
/// そこで「火がついた」だけをホスト経由で全員に配る。
/// **数え方（残り秒数・点滅・爆発）は各PCがそれぞれ行う。**
/// 同じ秒数を同じタイミングから数えるので、通信量をかけずにだいたいそろう。
///
/// 爆発の被害（物資が消える・プレイヤーがスタンする）も各PCが自分で計算する。
/// 位置は同期されているため、どのPCでも同じ結果になる。
/// **自分がスタンするかどうかは自分のPCが決める**ので、操作が止まるまでの遅れも無い。
/// </summary>
[RequireComponent(typeof(NetworkObject))]
[RequireComponent(typeof(ExplosiveObject))]
public class FishingNetBomb : NetworkBehaviour
{
    /// <summary>導火線に火がついたか。**ホストが決めて全員に配る。**</summary>
    private readonly NetworkVariable<bool> lit = new NetworkVariable<bool>(false);

    private ExplosiveObject explosive;

    private void Awake()
    {
        explosive = GetComponent<ExplosiveObject>();
    }

    public override void OnNetworkSpawn()
    {
        lit.OnValueChanged += OnLitChanged;

        // 途中から入ってきた人が、すでに燃えている爆弾を見たとき
        if (lit.Value)
        {
            explosive.LightFuse();
        }
    }

    public override void OnNetworkDespawn()
    {
        lit.OnValueChanged -= OnLitChanged;
    }

    /// <summary>
    /// ホストが <see cref="lit"/> を変えると、**ホスト自身も含めた全員のPC**で呼ばれる。
    /// 火がついたときだけ各PCの導火線を燃やし始める（消えたときは <see cref="ExplosiveObject"/> が自分で片付けている）。
    /// </summary>
    private void OnLitChanged(bool before, bool after)
    {
        if (after)
        {
            explosive.LightFuse();
        }
    }

    /// <summary>
    /// 火をつけたいとホストへ頼む。<see cref="HookController"/> から呼ばれる。
    /// **ホストが認めると、全員の画面で導火線が燃え始める。**
    /// </summary>
    public void RequestLightFuse()
    {
        RequestLightFuseServerRpc();
    }

    // 爆弾の持ち主（Owner）はホストだが、火をつけるのは引っ掛けた参加者なので、
    // 持ち主でなくても呼べるように RequireOwnership = false にしてある
    [ServerRpc(RequireOwnership = false)]
    private void RequestLightFuseServerRpc()
    {
        // すでに燃えていれば何もしない（同じ値を入れ直すと無駄な通知が飛ぶ）
        if (!lit.Value)
        {
            lit.Value = true;
        }
    }

    /// <summary>爆発したあと、また使えるように火を消す。**ホストだけが呼べる。**</summary>
    public void ServerResetFuse()
    {
        if (IsServer)
        {
            lit.Value = false;
        }
    }
}
