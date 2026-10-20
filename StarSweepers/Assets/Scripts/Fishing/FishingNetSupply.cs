using Unity.Netcode;
using UnityEngine;

/// <summary>
/// **オンラインの物資1つ分。** 誰が引っ掛けているかを整理し、投げを成立させる。
///
/// ## 誰が動かすか（ここが一番大事）
///
/// | 場面 | 動かす人 | 理由 |
/// | --- | --- | --- |
/// | 引き寄せ中 | **引っ掛けた本人** | ゲージと物資の動きがずれると、スキルチェックが成立しない |
/// | 投げたあとの飛び方 | **ホスト** | どこに落ちたか・どのゴールに入ったかを、全員で一致させる必要がある |
/// | 二重に引っ掛けられないか | **ホスト** | 早い者勝ちをホストが決める |
///
/// 引っ掛けた瞬間に**持ち主をその人へ移す**（`ChangeOwnership`）ので、
/// 引き寄せの動きは既存の <see cref="ThrowController"/> がそのまま動かせる。
/// 投げるときはホストへ知らせ、**ホストが力を加えて持ち主を取り戻す。**
///
/// 物理を回すのは「そのときの持ち主」だけ。それ以外のPCでは止めてある
/// （全員が物理を回すと、少しずつずれて位置がバラバラになるため）。
/// </summary>
[RequireComponent(typeof(NetworkObject))]
[RequireComponent(typeof(HookableObject))]
public class FishingNetSupply : NetworkBehaviour
{
    /// <summary>いま引っ掛けている人の参加番号。**ホストが決める。** -1で誰も掛けていない。</summary>
    private readonly NetworkVariable<int> hookedByPlayerIndex = new NetworkVariable<int>(-1);

    /// <summary>最後に投げた人の参加番号。**共通ゴールの点を誰に入れるか**の判断に使う。</summary>
    private readonly NetworkVariable<int> lastThrownByPlayerIndex = new NetworkVariable<int>(-1);

    // 爆発前の「まだ届いていなかった鉤・投げ・解除」の指示を、爆発後に採用しない。

    /// <summary>
    /// **この物資が「爆風などで強制的に外された」回数。ホストが増やして全員に配る。**
    /// 引っ掛け・投げ・離すの指示には、頼んだ時点のこの値を添えてもらい、
    /// ホストは**いまの値と違う指示は古いものとして捨てる**（爆発の前に出されて遅れて届いた指示を、爆発後に採用しないため）。
    /// </summary>
    private readonly NetworkVariable<uint> explosionRevision = new NetworkVariable<uint>(0);

    /// <summary>自分が最後に引っ掛けを頼んだときの <see cref="explosionRevision"/>（このPCの控え）。投げ・離すの指示に添える。</summary>
    private uint localClaimRevision;

    /// <summary>このPCから引っ掛けを頼んだ回数。断りの返事が「どの依頼へのものか」を見分けるのに使う。</summary>
    private uint localClaimAttempt;

    // 最後に引っ掛けを頼んだ人の参加番号（1台で複数人のとき、断りをその人のフックへ返すため）
    private int localClaimPlayerIndex = -1;

    /// <summary>いま誰かに引っ掛けられているか。</summary>
    public bool IsClaimed => hookedByPlayerIndex.Value >= 0;

    /// <summary>いま引っ掛けている人の参加番号（ホストの記録）。誰も掛けていなければ -1。</summary>
    public int HookedByPlayerIndex => hookedByPlayerIndex.Value;

    /// <summary>最後に投げた人の参加番号。誰も投げていなければ -1。</summary>
    public int LastThrownByPlayerIndex => lastThrownByPlayerIndex.Value;

    private HookableObject hookable;
    private Rigidbody body;

    private void Awake()
    {
        hookable = GetComponent<HookableObject>();
        body = GetComponent<Rigidbody>();
    }

    // 生まれたときと、持ち主が移るたびに「物理を回すのは誰か」を決め直す

    public override void OnNetworkSpawn()
    {
        ApplyPhysicsAuthority();
    }

    public override void OnGainedOwnership()
    {
        ApplyPhysicsAuthority();
    }

    public override void OnLostOwnership()
    {
        ApplyPhysicsAuthority();
    }

    /// <summary>
    /// **物理を回すのは、いまの持ち主のPCだけ。** それ以外では止める。
    /// 止めた側は `NetworkTransform` が配ってくる位置に従うだけになる。
    /// </summary>
    private void ApplyPhysicsAuthority()
    {
        if (body == null)
        {
            return;
        }

        // 引き寄せ中は ThrowController が自分で止める（キネマティックにする）ので、
        // ここでは「持ち主かどうか」だけで決める
        body.isKinematic = !IsOwner;

        if (!IsOwner)
        {
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
        }
    }

    // ------------------------------------------------------------
    // 引っ掛ける（本人 → ホスト）
    // ------------------------------------------------------------

    /// <summary>
    /// 引っ掛けたいとホストへ頼む。<see cref="HookController"/> から呼ばれる。
    /// **早い者勝ちの判定はホストが行う**ので、断られることがある。
    /// </summary>
    public void RequestClaim(int playerIndex)
    {
        localClaimRevision = explosionRevision.Value;
        localClaimAttempt += 1;
        localClaimPlayerIndex = playerIndex;
        RequestClaimServerRpc(playerIndex, localClaimRevision, localClaimAttempt);
    }

    /// <summary>
    /// ホスト側の受け口。まだ誰も掛けていなければ、**持ち主をその人へ移す。**
    ///
    /// `RequireOwnership = false` は「持ち主でなくても呼んでよい」の意味。
    /// 引っ掛ける前の持ち主はホストなので、これが無いと参加者から頼めない。
    /// </summary>
    [ServerRpc(RequireOwnership = false)]
    private void RequestClaimServerRpc(int playerIndex, uint claimRevision, uint claimAttempt,
        ServerRpcParams rpcParams = default)
    {
        // **同じ人からの掛け直しは受け付ける**（2026/10/6）。
        // 遅れて「離した」が届く前に同じ人がもう一度引っ掛けた場合や、
        // ホストの記録が「その人が引っ掛け中」のまま残っていた場合に、本人までつかめなくなっていた
        bool takenByOther = IsClaimed && hookedByPlayerIndex.Value != playerIndex;

        if (claimRevision != explosionRevision.Value || takenByOther || hookable.IsVanished)
        {
            // すでに誰かが掛けている。頼んできた人に断りを返す
            DenyClaimClientRpc(claimRevision, claimAttempt, RpcToSender(rpcParams));
            return;
        }

        ServerGrantClaim(playerIndex, rpcParams.Receive.SenderClientId);
    }

    /// <summary>
    /// **引っ掛けをその人のものにする**（ホストだけが呼ぶ）。記録を更新し、持ち主もその人のPCへ移す。
    /// 頼んできたのがホスト自身なら、持ち主はもうホストなので移さない。
    /// </summary>
    private void ServerGrantClaim(int playerIndex, ulong senderClientId)
    {
        hookedByPlayerIndex.Value = playerIndex;

        if (senderClientId != NetworkManager.ServerClientId)
        {
            NetworkObject.ChangeOwnership(senderClientId);
        }
    }

    /// <summary>頼んだ本人だけに「取られていた」と伝える。</summary>
    [ClientRpc]
    private void DenyClaimClientRpc(uint claimRevision, uint claimAttempt, ClientRpcParams rpcParams = default)
    {
        // 爆発後に掛け直していたら、古い依頼への断りで新しい引き寄せを取り消さない。
        if (claimRevision != localClaimRevision || claimAttempt != localClaimAttempt)
        {
            return;
        }

        HookController hook = FindLocalHookController(localClaimPlayerIndex);
        if (hook != null)
        {
            hook.CancelAttachBecauseTaken(hookable);
        }
    }

    // ------------------------------------------------------------
    // 投げる・離す（本人 → ホスト）
    // ------------------------------------------------------------

    /// <summary>
    /// 投げをホストへ知らせる。<see cref="ThrowController"/> から呼ばれる。
    /// **力を加えるのはホスト。** そうしないと、どこへ飛んだかが人によって変わる。
    /// </summary>
    public void RequestThrow(Vector3 direction, float force, float lift, int playerIndex,
        bool preserveVelocity = false, Vector3 inheritedVelocity = default,
        Vector3 inheritedAngularVelocity = default, bool hasReleasePosition = false, Vector3 releasePosition = default)
    {
        RequestThrowServerRpc(direction, force, lift, playerIndex, localClaimRevision,
            preserveVelocity, inheritedVelocity, inheritedAngularVelocity, hasReleasePosition, releasePosition);
    }

    /// <summary>
    /// ホスト側の受け口。持ち主を取り戻し、離した位置に置き直してから、ホストの物理で力を加える。
    /// </summary>
    [ServerRpc(RequireOwnership = false)]
    private void RequestThrowServerRpc(Vector3 direction, float force, float lift,
        int playerIndex, uint claimRevision, bool preserveVelocity, Vector3 inheritedVelocity,
        Vector3 inheritedAngularVelocity, bool hasReleasePosition, Vector3 releasePosition,
        ServerRpcParams rpcParams = default)
    {
        // 掛けている本人からの指示だけを受け付ける（爆発をまたいだ古い指示も捨てる）
        if (claimRevision != explosionRevision.Value || hookedByPlayerIndex.Value != playerIndex)
        {
            return;
        }

        TakeBackOwnership();

        // **手を離した位置から飛ばす**（2026/10/13）。ホストに届いている位置は少し遅れているので、
        // そのまま力を加えると、離した場所より手前から飛び出したように見える（引っ張った後の飛び方がおかしい、の一因）
        if (hasReleasePosition && body != null)
        {
            body.position = releasePosition;
            transform.position = releasePosition;
        }

        hookedByPlayerIndex.Value = -1;
        lastThrownByPlayerIndex.Value = playerIndex;

        // **ここからはホストが物資を動かすので、ホストでも「投げた本人の体に当たらない」ようにする**
        // （本人のPCだけでやっても、ホスト側で頭にぶつかって跳ねてしまう。2026/9/30）
        ThrowPassThrough.Apply(gameObject, FindThrowerCollider(playerIndex));

        if (body != null && !body.isKinematic)
        {
            if (!preserveVelocity)
            {
                body.linearVelocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
            }
            else
            {
                // タイプBは引きずっていた本人の速度をホストへ渡し、所有権移行後も慣性を保つ。
                body.linearVelocity = inheritedVelocity;
                body.angularVelocity = inheritedAngularVelocity;
            }
            body.AddForce(direction * force + Vector3.up * lift, ForceMode.Impulse);
        }
    }

    /// <summary>
    /// 引っ掛けをやめる（ミスしたとき・爆発したとき）ことをホストへ知らせる。
    /// <paramref name="pullForce"/> だけプレイヤー側へ引き寄せて終わる。
    /// </summary>
    public void RequestRelease(Vector3 direction, float pullForce, int playerIndex)
    {
        RequestReleaseServerRpc(direction, pullForce, playerIndex, localClaimRevision);
    }

    /// <summary>ホスト側の受け口。持ち主を取り戻して記録を消し、頼まれた力があればプレイヤーのほうへ引く。</summary>
    [ServerRpc(RequireOwnership = false)]
    private void RequestReleaseServerRpc(Vector3 direction, float pullForce,
        int playerIndex, uint claimRevision, ServerRpcParams rpcParams = default)
    {
        // 掛けている本人からの指示だけを受け付ける（爆発をまたいだ古い指示も捨てる）
        if (claimRevision != explosionRevision.Value || hookedByPlayerIndex.Value != playerIndex)
        {
            return;
        }

        TakeBackOwnership();
        hookedByPlayerIndex.Value = -1;

        if (body != null && !body.isKinematic && pullForce > 0f)
        {
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            body.AddForce(direction * pullForce, ForceMode.Impulse);
        }
    }

    // ------------------------------------------------------------
    // 横取り（宇宙ごみ式。2026/10/13・大槻さん）
    // ------------------------------------------------------------

    /// <summary>
    /// **横取りをホストに頼む。** ほかの人がつかんでいる物資にくっついて待っていた人が、引っ張ったときに呼ぶ
    /// （<see cref="ThrowController"/>）。返事は <see cref="StealResultClientRpc"/> で、頼んだ本人のPCに届く。
    /// </summary>
    public void RequestSteal(int playerIndex)
    {
        localClaimPlayerIndex = playerIndex;
        RequestStealServerRpc(playerIndex);
    }

    /// <summary>
    /// ホスト側の受け口。まだほかの人がつかんでいれば、**その人のフックを外して（爆風と同じ外し方。力は加えない）**、
    /// 頼んだ人のものにする（持ち主もその人へ移す）。もう誰もつかんでいなければ断る（くっついていたフックは戻る）。
    /// </summary>
    [ServerRpc(RequireOwnership = false)]
    private void RequestStealServerRpc(int playerIndex, ServerRpcParams rpcParams = default)
    {
        bool heldByOther = IsClaimed && hookedByPlayerIndex.Value != playerIndex;
        if (!heldByOther || hookable.IsVanished)
        {
            StealResultClientRpc(playerIndex, false, explosionRevision.Value, RpcToSender(rpcParams));
            return;
        }

        Debug.Log($"[FISH] 参加番号 {playerIndex} が、参加番号 {hookedByPlayerIndex.Value} のつかんでいる物資を横取りしました。");

        // 持っている人のフックを外す（全員のPCで、その人の引き寄せが止まる）
        ServerApplyExplosion(Vector3.zero);

        // 頼んだ人のものにする
        ServerGrantClaim(playerIndex, rpcParams.Receive.SenderClientId);

        StealResultClientRpc(playerIndex, true, explosionRevision.Value, RpcToSender(rpcParams));
    }

    /// <summary>横取りの返事（頼んだ本人のPCだけ）。許されたら、その人が引っ張り始める。</summary>
    [ClientRpc]
    private void StealResultClientRpc(int playerIndex, bool granted, uint revision, ClientRpcParams rpcParams = default)
    {
        if (granted)
        {
            // 外したあとの新しい番号で、自分の引っ掛けとして扱う（このあとの投げ・離すの知らせが受け付けられるように）
            localClaimRevision = revision;
            localClaimPlayerIndex = playerIndex;
        }

        HookController hook = FindLocalHookController(playerIndex);
        if (hook != null && hook.Thrower != null)
        {
            hook.Thrower.OnStealResult(hookable, granted);
        }
    }

    /// <summary>
    /// **ほかの人のフックが当たったので、つかんでいる人の引っ掛けを外してほしい**とホストへ頼む。
    /// 宇宙ごみ式（<see cref="ThrowStyle.TwoButtons"/>）の <see cref="HookController"/> だけが呼ぶ（2026/9/22）。
    /// 釣りでは呼ばれないので、釣りの動きは変わらない。
    /// </summary>
    public void RequestInterfere(int playerIndex)
    {
        RequestInterfereServerRpc(playerIndex);
    }

    /// <summary>ホスト側の受け口。ほかの人がつかんでいれば、爆風と同じ外し方（力 0）で外す。</summary>
    [ServerRpc(RequireOwnership = false)]
    private void RequestInterfereServerRpc(int playerIndex)
    {
        // 誰もつかんでいない／自分でつかんでいるなら何もしない
        if (!IsClaimed || hookedByPlayerIndex.Value == playerIndex)
        {
            return;
        }

        Debug.Log($"[FISH] 参加番号 {playerIndex} のフックが当たったので、" +
                  $"参加番号 {hookedByPlayerIndex.Value} の引っ掛けを外しました。");

        // 爆風と同じ外し方をする。**力は加えない**（その場に落ちる）
        ServerApplyExplosion(Vector3.zero);
    }

    /// <summary>
    /// 爆風で脱鉤して吹き飛ばす。各PCの爆弾が呼んでもホストだけが処理する。
    /// ほかの人のフックが当たって外すとき（<see cref="RequestInterfere"/>）も、力 0 でここを使う。
    /// </summary>
    public void ServerApplyExplosion(Vector3 velocity)
    {
        if (!IsSpawned || !IsServer || hookable.IsVanished || body == null)
        {
            return;
        }

        // 「強制的に外した回数」を進める。これより前の番号を添えて届く指示は、古いものとして捨てられる
        explosionRevision.Value += 1;
        ReleaseForExplosionClientRpc(explosionRevision.Value);
        // ホストと専用サーバーはここで手元の状態を戻す。
        ThrowController.ReleaseTargetForExplosion(hookable);
        hookedByPlayerIndex.Value = -1;
        TakeBackOwnership();
        body.AddForce(velocity, ForceMode.VelocityChange);
    }

    /// <summary>参加者の画面で、つかんでいた人の引き寄せを止める（ホストは <see cref="ServerApplyExplosion"/> の中で直接止めている）。</summary>
    [ClientRpc]
    private void ReleaseForExplosionClientRpc(uint releasedRevision)
    {
        // 爆発後の新しい鉤まで、遅れて届いた脱鉤の合図で外さない。
        if (!IsServer && localClaimRevision < releasedRevision)
        {
            ThrowController.ReleaseTargetForExplosion(hookable);
        }
    }

    /// <summary>参加番号 <paramref name="playerIndex"/> の人の体（CharacterController）。見つからなければ null。</summary>
    private static Collider FindThrowerCollider(int playerIndex)
    {
        foreach (FishingNetPlayer player in FishingNetPlayer.All)
        {
            if (player != null && player.PlayerIndex == playerIndex)
            {
                return player.GetComponent<CharacterController>();
            }
        }

        return null;
    }

    /// <summary>ホストが持ち主を取り戻し、物理を自分で回せる状態にする。</summary>
    private void TakeBackOwnership()
    {
        if (NetworkObject.OwnerClientId != NetworkManager.ServerClientId)
        {
            NetworkObject.ChangeOwnership(NetworkManager.ServerClientId);
        }

        ApplyPhysicsAuthority();
    }

    // ------------------------------------------------------------
    // 答え合わせ（遅延で知らせを取りこぼしても、ずれたまま残らないように。2026/10/6）
    // ------------------------------------------------------------

    /// <summary>
    /// **答え合わせで「ホストの記録では自分が引っ掛け中なのに、手に持っていない」と分かったとき**に、本人のPCから呼ぶ。
    /// 普通の「離した」（<see cref="RequestRelease"/>）と違い、爆発の回数（revision）が食い違っていても受け付ける
    /// （食い違っていたせいで「離した」が捨てられ、ずれたまま残ったときの立て直し用）。
    /// </summary>
    public void RequestReconcileRelease(int playerIndex)
    {
        RequestReconcileReleaseServerRpc(playerIndex);
    }

    /// <summary>ホスト側の受け口。頼んできた人が本当にその参加番号の人なら、記録を消して持ち主を取り戻す。</summary>
    [ServerRpc(RequireOwnership = false)]
    private void RequestReconcileReleaseServerRpc(int playerIndex, ServerRpcParams rpcParams = default)
    {
        if (hookedByPlayerIndex.Value != playerIndex)
        {
            return;
        }

        // **頼んできた人が、本当にその参加番号の人か**を確かめる（ほかの人の引っ掛けを外させない）
        // 1台で複数人のときは、同じPCに何人もいるので「参加番号の人が、頼んできたPCの人か」で確かめる
        FishingNetPlayer sender = FindPlayerByIndex(playerIndex);
        if (sender == null || sender.OwnerClientId != rpcParams.Receive.SenderClientId)
        {
            return;
        }

        Debug.LogWarning($"[NET][答え合わせ] {name}：参加番号 {playerIndex} の人は持っていないのに、" +
                         "ホストの記録では引っ掛け中のままでした。外しました。");

        hookedByPlayerIndex.Value = -1;
        TakeBackOwnership();
    }

    /// <summary>ホストが答え合わせをする間隔（秒）。</summary>
    private const float ServerCheckInterval = 1f;

    /// <summary>前回の答え合わせからの経過秒数。</summary>
    private float serverCheckTimer;

    /// <summary>「持ち主と引っ掛けた人が食い違う」が何回続いたか。</summary>
    private int ownerMismatchCount;

    /// <summary>
    /// ホストだけが動かす。誰かが引っ掛けている間、<see cref="ServerCheckInterval"/> 秒ごとに答え合わせをする。
    /// ポーズ中も数えるよう、時間は unscaledDeltaTime で測る。
    /// </summary>
    private void Update()
    {
        if (!IsSpawned || !IsServer || !IsClaimed || despawnScheduled)
        {
            ownerMismatchCount = 0;
            return;
        }

        serverCheckTimer += Time.unscaledDeltaTime;
        if (serverCheckTimer < ServerCheckInterval)
        {
            return;
        }
        serverCheckTimer = 0f;

        ServerCheckClaim();
    }

    /// <summary>
    /// **ホストの答え合わせ。** 引っ掛け中の記録がおかしくないかを見る。
    /// ・引っ掛けた人がもういない → 外す（誰もつかめないまま残らないように）
    /// ・物の持ち主（物理を動かす人）と、引っ掛けた人が続けて食い違う → 引っ掛けた人を持ち主にする
    /// </summary>
    private void ServerCheckClaim()
    {
        FishingNetPlayer claimer = FindPlayerByIndex(hookedByPlayerIndex.Value);

        if (claimer == null)
        {
            Debug.LogWarning($"[NET][答え合わせ] {name}：引っ掛けていた参加番号 {hookedByPlayerIndex.Value} の人が" +
                             "もういないので、引っ掛けを外しました。");
            hookedByPlayerIndex.Value = -1;
            TakeBackOwnership();
            ownerMismatchCount = 0;
            return;
        }

        if (NetworkObject.OwnerClientId == claimer.OwnerClientId)
        {
            ownerMismatchCount = 0;
            return;
        }

        // 引っ掛けた直後は、持ち主が移る途中のことがあるので、続けて食い違ったときだけ直す
        ownerMismatchCount++;
        if (ownerMismatchCount < 2)
        {
            return;
        }

        Debug.LogWarning($"[NET][答え合わせ] {name}：引っ掛けた人（参加番号 {claimer.PlayerIndex}）と" +
                         "物の持ち主が食い違っていたので、引っ掛けた人を持ち主にしました。");
        NetworkObject.ChangeOwnership(claimer.OwnerClientId);
        ownerMismatchCount = 0;
    }

    /// <summary>参加番号 <paramref name="playerIndex"/> の人。いなければ null。</summary>
    private static FishingNetPlayer FindPlayerByIndex(int playerIndex)
    {
        foreach (FishingNetPlayer player in FishingNetPlayer.All)
        {
            if (player != null && player.IsSpawned && player.PlayerIndex == playerIndex)
            {
                return player;
            }
        }
        return null;
    }

    // ------------------------------------------------------------
    // 消す（ホスト → 全員）
    // ------------------------------------------------------------

    /// <summary>
    /// 消えてから、全員の画面から実際に削除するまでの秒数。
    ///
    /// **すぐ削除しない理由：** 爆発物は各PCが自分で導火線を数えて爆発する。
    /// ホストの爆発と同時に削除すると、少し遅れて数えている参加者の画面では
    /// **爆発する前に消えてしまい、スタンや爆発の見た目が出ない**ため。
    /// </summary>
    private const float DespawnDelaySeconds = 1f;

    /// <summary>もう削除の予約をしたか（二重に予約しないため）。</summary>
    private bool despawnScheduled;

    /// <summary>
    /// **ホストだけが呼ぶ。** 全員の画面で見た目を消し、少し待ってから削除する。
    /// <see cref="HookableObject.Vanish"/> から呼ばれる。
    /// </summary>
    public void ServerDespawnSoon()
    {
        if (!IsServer || despawnScheduled)
        {
            return;
        }

        despawnScheduled = true;
        hookedByPlayerIndex.Value = -1;
        HideClientRpc();
        StartCoroutine(DespawnLater());
    }

    /// <summary><see cref="DespawnDelaySeconds"/> 秒待ってから、全員の画面から削除する。待っている間に消えていたら何もしない。</summary>
    private System.Collections.IEnumerator DespawnLater()
    {
        yield return new WaitForSeconds(DespawnDelaySeconds);

        if (IsSpawned)
        {
            NetworkObject.Despawn(true);
        }
    }

    /// <summary>参加者の画面でも見た目を消す（ゴールに入ったときなど、ホストだけが判定した場合）。</summary>
    [ClientRpc]
    private void HideClientRpc()
    {
        if (!IsServer)
        {
            hookable.Vanish();
        }
    }

    /// <summary>ホストが点を入れたあとに、投げた人の記録を消す。</summary>
    public void ClearLastThrower()
    {
        if (IsServer)
        {
            lastThrownByPlayerIndex.Value = -1;
        }
    }

    // ------------------------------------------------------------
    // 補助
    // ------------------------------------------------------------

    /// <summary>ClientRpc の宛先を「頼んできた人のPCだけ」にするための設定を作る（全員に配らないため）。</summary>
    private static ClientRpcParams RpcToSender(ServerRpcParams rpcParams)
    {
        return new ClientRpcParams
        {
            Send = new ClientRpcSendParams
            {
                TargetClientIds = new[] { rpcParams.Receive.SenderClientId }
            }
        };
    }

    /// <summary>
    /// このPCで自分が操作しているプレイヤーの HookController を探す。
    /// 1台で複数人のときは、参加番号 <paramref name="playerIndex"/> の人のもの（見つからなければ、このPCの誰か）。
    /// </summary>
    private static HookController FindLocalHookController(int playerIndex)
    {
        foreach (FishingNetPlayer player in FishingNetPlayer.All)
        {
            if (player != null && player.IsOwner && player.PlayerIndex == playerIndex)
            {
                return player.GetComponent<HookController>();
            }
        }

        foreach (FishingNetPlayer player in FishingNetPlayer.All)
        {
            if (player != null && player.IsOwner)
            {
                return player.GetComponent<HookController>();
            }
        }
        return null;
    }
}
