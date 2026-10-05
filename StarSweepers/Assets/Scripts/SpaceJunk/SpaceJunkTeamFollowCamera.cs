using Unity.Netcode;
using UnityEngine;

/// <summary>
/// **自分の陣地がいつも画面の手前に来るように向きを回す、追いかけるカメラ。**
///
/// ワイルドリフトの赤チームのように、チームによって画面の向きが変わる
/// （2026/9/22・大槻さんの依頼）。見下ろす角度・高さ・後ろへの距離はふつうのカメラと同じで、
/// **水平の向きだけ**がチームで変わる。
///
/// ## 向きの決め方
///
/// **自分のチームのゴールが全部ある側から、ステージの中心を見る。**
/// 自分のゴールそれぞれの「中心からの方向」を足し合わせて、その反対を向く。
///
/// | 自分のゴール | 向き |
/// | --- | --- |
/// | 1つ（2〜4チームのとき） | そのゴールの辺の**真正面**から見る |
/// | 隣り合う2つ（いまのルールでは出ない） | 2つのゴールの間の角から、**斜め45度**で見る |
/// | 4つ全部（1チームのとき） | 方向が打ち消し合って決まらないので、**北向きのまま** |
///
/// ゴールの持ち主はラウンドが始まってから決まるので、**毎フレーム計算し直す**（4つしか無いので軽い）。
///
/// ## 移動の向き
///
/// カメラを回すと、そのままでは W が画面の上にならない。
/// 移動（`FishingPlayerController`）は**カメラの向きを基準に**動くようにしてあるので、
/// どのチームでも **W＝画面の奥**になる。
///
/// ## ふつうのカメラ（TopDownCameraFollow）との関係
///
/// 追いかけ方は同じで、**向きを回す部分だけを足したもの。**
/// 釣りのカメラは変えていない。このカメラは宇宙ごみのマップ（`SpaceJunkMap01TeamCam`）と、
/// STAGE_01〜05 のカメラ（`SpaceJunkStageCamera.prefab`。全体カメラと C キーで切り替え → <see cref="SpaceJunkCameraModeSwitch"/>）に置いてある。
/// 置くのは `Tools > StarSweepers > 宇宙ごみの自陣向きカメラのマップを作る（Map01を複製）`。
/// </summary>
public class SpaceJunkTeamFollowCamera : MonoBehaviour
{
    [Header("参照")]
    [Tooltip("追いかける相手（自分のプレイヤー）。生まれた時点で自動で入る")]
    [SerializeField] private Transform target;

    [Header("位置")]
    [Tooltip("北向きのときの、相手から見たカメラの位置。Y を上げるほど高く、Z をマイナスにするほど後ろから見る。" +
             "**チームによって、これが水平に回される**")]
    [SerializeField] private Vector3 offset = new Vector3(0f, 16f, -9f);

    [Tooltip("見下ろす角度（度）")]
    [SerializeField] private float pitch = 60f;

    [Tooltip("追従のなめらかさ。大きいほどキビキビ、小さいほどゆっくり付いてくる")]
    [SerializeField] private float followSharpness = 10f;

    [Tooltip("向きが変わるときの回りのなめらかさ。大きいほどすぐ回る")]
    [SerializeField] private float turnSharpness = 8f;

    /// <summary>いまカメラが向いている水平の角度（度）。北向きが 0。</summary>
    public float Yaw { get; private set; }

    /// <summary>
    /// 追いかける相手を決める。**オンラインでは、自分のプレイヤーが生まれた時点で渡される。**
    /// 渡した瞬間は、なめらかにせずその場所・その向きへ飛ばす。
    /// </summary>
    public void SetTarget(Transform newTarget)
    {
        target = newTarget;

        if (target == null)
        {
            return;
        }

        // 止めてある（全体カメラのモード中。SpaceJunkCameraModeSwitch）なら、カメラを飛ばさない。
        // 自陣カメラに切り替えたときに、そこからなめらかに追いかけ始める
        if (!isActiveAndEnabled)
        {
            return;
        }

        Yaw = TargetYaw();
        Apply(1f);
    }

    private void LateUpdate()
    {
        if (target == null)
        {
            return;
        }

        // 回る向きは、少しずつ寄せる（ラウンドの始めにゴールの持ち主が決まった瞬間に、
        // 画面がいきなり回ると酔いやすいため）
        float turn = 1f - Mathf.Exp(-turnSharpness * Time.deltaTime);
        Yaw = Mathf.LerpAngle(Yaw, TargetYaw(), turn);

        float follow = 1f - Mathf.Exp(-followSharpness * Time.deltaTime);
        Apply(follow);
    }

    /// <summary>位置と向きを当てはめる。<paramref name="t"/> が 1 なら、その場所へ飛ぶ。</summary>
    private void Apply(float t)
    {
        Quaternion spin = Quaternion.Euler(0f, Yaw, 0f);
        Vector3 goal = target.position + spin * offset;

        transform.position = Vector3.Lerp(transform.position, goal, t);
        transform.rotation = Quaternion.Euler(pitch, Yaw, 0f);
    }

    /// <summary>
    /// **向くべき水平の角度を求める。**
    /// 自分のゴールの方向を足し合わせ、その反対（ステージの奥）を向く。決まらなければ北向き（0度）。
    /// </summary>
    private float TargetYaw()
    {
        SpaceJunkSession session = SpaceJunkSession.Current;
        NetworkManager manager = NetworkManager.Singleton;

        if (session == null || manager == null || SpaceJunkGoal.All.Count == 0)
        {
            return 0f;
        }

        int myTeam = session.TeamOf(manager.LocalClientId);

        // ステージの中心＝ゴール全部の真ん中
        Vector3 center = Vector3.zero;
        foreach (SpaceJunkGoal goal in SpaceJunkGoal.All)
        {
            center += goal.transform.position;
        }
        center /= SpaceJunkGoal.All.Count;

        // 自分のゴールが、中心から見てどちらにあるか（全部足す）
        Vector3 ownSide = Vector3.zero;
        foreach (SpaceJunkGoal goal in SpaceJunkGoal.All)
        {
            if (goal.OwnerTeam != myTeam)
            {
                continue;
            }

            Vector3 away = goal.transform.position - center;
            away.y = 0f;

            if (away.sqrMagnitude > 0.0001f)
            {
                ownSide += away.normalized;
            }
        }

        // 自分のゴールが無い／全部持っていて打ち消し合った → 北向きのまま
        if (ownSide.sqrMagnitude < 0.01f)
        {
            return 0f;
        }

        // 自陣側から奥を見る＝自陣の方向の反対を向く
        Vector3 forward = -ownSide.normalized;
        return Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg;
    }
}
