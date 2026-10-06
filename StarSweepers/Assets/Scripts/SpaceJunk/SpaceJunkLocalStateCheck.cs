using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// **自分のPCの状態を、ホストの記録と1秒ごとに答え合わせする部品。**（2026/10/6・大槻さん「遅延で、決まったタイミングに処理できていない」）
///
/// 通信の知らせ（RPC）は必ず届くが、**遅れて届くと、そのとき相手の準備ができておらず、ずれたまま残る**ことがある。
/// 原因が何であっても、ずれが残り続けないように、定期的に確かめて直す。**直したときは必ずログを出す**
/// （黙って直すと、本当の原因が見えなくなるため。ログは `[NET][答え合わせ]` で始まる）。
///
/// | 確かめること | ずれていたら |
/// | --- | --- |
/// | ホストの記録では**自分が引っ掛け中**なのに、手に持っていない | ホストへ「離した」を送り直す |
/// | 手に持っているのに、ホストの記録では**別の人・誰でもない** | 自分の引っ掛けを外す（ホストが正） |
/// | **動けるべきなのに動けない／止まっているべきなのに動ける** | 「動けるべきか」（<see cref="SpaceJunkPlayerSetup.ControlShouldBeEnabled"/>）に合わせる |
///
/// **1回ずれていただけでは直さない。続けて（2回）ずれていたときだけ直す。**
/// 知らせが届く途中の一瞬のずれ（引っ掛けた直後など）まで直すと、かえっておかしくなるため。
///
/// 自分が操作しているプレイヤーにだけ、<see cref="SpaceJunkPlayerSetup"/> が付ける。
/// </summary>
public class SpaceJunkLocalStateCheck : MonoBehaviour
{
    /// <summary>答え合わせの間隔（秒）。</summary>
    private const float CheckInterval = 1f;

    /// <summary>何回続けてずれていたら直すか。</summary>
    private const int MismatchesBeforeFix = 2;

    private SpaceJunkPlayerSetup setup;
    private HookController hookController;
    private FishingPlayerController mover;
    private HookAimAssist aimAssist;

    private float timer;

    /// <summary>物資ごとの「続けてずれていた回数」（キーは NetworkObjectId）。</summary>
    private readonly Dictionary<ulong, int> hookMismatches = new Dictionary<ulong, int>();
    private readonly HashSet<ulong> seenThisCheck = new HashSet<ulong>();

    private int controlMismatches;

    private void Awake()
    {
        setup = GetComponent<SpaceJunkPlayerSetup>();
        hookController = GetComponent<HookController>();
        mover = GetComponent<FishingPlayerController>();
        aimAssist = GetComponent<HookAimAssist>();
    }

    private void Update()
    {
        timer += Time.unscaledDeltaTime;
        if (timer < CheckInterval)
        {
            return;
        }
        timer = 0f;

        CheckHooks();
        CheckControl();
    }

    // ------------------------------------------------------------
    // 物の引っ掛け
    // ------------------------------------------------------------

    private void CheckHooks()
    {
        if (hookController == null || !hookController.IsOnline)
        {
            hookMismatches.Clear();
            return;
        }

        int me = hookController.LocalPlayerIndex;
        HookableObject holding = hookController.AttachedItem;
        seenThisCheck.Clear();

        foreach (HookableObject item in HookableObject.All)
        {
            if (item == null || item.IsVanished)
            {
                continue;
            }

            FishingNetSupply netSupply = item.GetComponent<FishingNetSupply>();
            if (netSupply == null || !netSupply.IsSpawned)
            {
                continue;
            }

            ulong id = netSupply.NetworkObjectId;
            bool claimedByMe = netSupply.HookedByPlayerIndex == me;
            bool holdingIt = holding == item;

            if (claimedByMe == holdingIt)
            {
                continue; // 合っている
            }

            seenThisCheck.Add(id);
            hookMismatches.TryGetValue(id, out int count);
            count++;

            if (count < MismatchesBeforeFix)
            {
                hookMismatches[id] = count;
                continue;
            }

            hookMismatches.Remove(id);

            if (claimedByMe)
            {
                // ホストは「自分が引っ掛け中」のまま。手には持っていないので、離したと送り直す
                Debug.LogWarning($"[NET][答え合わせ] {item.name}：ホストの記録では自分が引っ掛け中でしたが、" +
                                 "手に持っていないので「離した」を送り直しました。");
                netSupply.RequestReconcileRelease(me);
            }
            else
            {
                // 手に持っているのに、ホストの記録は別の人・誰でもない。ホストに合わせて外す
                string holder = netSupply.HookedByPlayerIndex >= 0
                    ? $"参加番号 {netSupply.HookedByPlayerIndex} の人"
                    : "誰でもない";
                Debug.LogWarning($"[NET][答え合わせ] {item.name}：手に持っていましたが、ホストの記録では{holder}でした。" +
                                 "引っ掛けを外しました。");
                hookController.CancelAttachBecauseTaken(item);
            }
        }

        // 今回ずれていなかった物の数え直し（続けてずれていたときだけ直すため）
        if (hookMismatches.Count > 0)
        {
            List<ulong> stale = null;
            foreach (ulong id in hookMismatches.Keys)
            {
                if (!seenThisCheck.Contains(id))
                {
                    (stale ??= new List<ulong>()).Add(id);
                }
            }

            if (stale != null)
            {
                foreach (ulong id in stale)
                {
                    hookMismatches.Remove(id);
                }
            }
        }
    }

    // ------------------------------------------------------------
    // 動けるか
    // ------------------------------------------------------------

    private void CheckControl()
    {
        if (setup == null || mover == null || hookController == null)
        {
            return;
        }

        bool expected = setup.ControlShouldBeEnabled;

        if (mover.enabled == expected && hookController.enabled == expected)
        {
            controlMismatches = 0;
            return;
        }

        controlMismatches++;
        if (controlMismatches < MismatchesBeforeFix)
        {
            return;
        }

        controlMismatches = 0;

        Debug.LogWarning($"[NET][答え合わせ] 操作の状態がずれていました（移動 {mover.enabled}・フック {hookController.enabled}" +
                         $" → どちらも {expected}）。直しました。");

        mover.enabled = expected;
        hookController.enabled = expected;

        if (aimAssist != null)
        {
            aimAssist.enabled = expected;
        }
    }
}
