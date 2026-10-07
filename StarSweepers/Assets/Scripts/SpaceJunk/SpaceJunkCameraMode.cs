using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// **宇宙ごみのカメラの「種類」の共通部品。**（2026/10/7・小野田さん「カメラを細かく調整できるように構造を整理してほしい」）
///
/// カメラの種類（全体・チーム・自分だけ …）は、それぞれこれを継いだ部品として**カメラに並べて付ける。**
/// 種類ごとの部品は「**いまカメラをどこに置きたいか**」を答えるだけで、カメラを直接は動かさない。
/// 実際に動かすのは、同じカメラに付いた切り替え部品（<see cref="SpaceJunkCameraModeSwitch"/>）。
///
/// | 部品 | 種類 |
/// | --- | --- |
/// | <see cref="SpaceJunkWholeStageCamera"/> | 全体カメラ（ステージ全体が収まる位置から見下ろす） |
/// | <see cref="SpaceJunkTeamGroupCamera"/> | チームカメラ（自陣向きのまま、味方も一緒に映す） |
/// | <see cref="SpaceJunkTeamFollowCamera"/> | 自分カメラ（自陣向きのまま、自分だけを追う。前からある「自陣カメラ」） |
///
/// ## 新しい種類を足すとき
///
/// 1. これを継いだ部品を作り、<see cref="TryGetPose"/> で「置きたい位置と向き」を返す
/// 2. カメラ（`SpaceJunkStageCamera.prefab`）に付ける。**付けた順が切り替えの順**になる
///
/// 切り替え部品が無いカメラ（`SpaceJunkMap01TeamCam` など）に付けた場合は、**自分でカメラを動かす**。
/// </summary>
[RequireComponent(typeof(Camera))]
public abstract class SpaceJunkCameraMode : MonoBehaviour
{
    [Header("参照")]
    [Tooltip("追いかける相手（自分のプレイヤー）。生まれた時点で自動で入る")]
    [SerializeField] protected Transform target;

    [Header("切り替え")]
    [Tooltip("画面の案内に出す名前。空なら決まった名前（全体／チーム／自分）")]
    [SerializeField] private string displayName = string.Empty;

    [Tooltip("C キー（Back）の切り替えに入れるか。OFF にすると、この種類には切り替わらない")]
    [SerializeField] private bool inCycle = true;

    /// <summary>画面の案内に出す名前。</summary>
    public string DisplayName => string.IsNullOrEmpty(displayName) ? DefaultName : displayName;

    /// <summary>切り替えに入れるか（部品が OFF のときも入れない）。</summary>
    public bool InCycle => inCycle && enabled;

    /// <summary>名前が空のときに出す名前。</summary>
    protected abstract string DefaultName { get; }

    /// <summary>このカメラ。</summary>
    protected Camera View { get; private set; }

    /// <summary>
    /// 次に位置を求めるときに、**なめらかにせずその場所へ飛ぶ**か。
    /// 相手が決まったとき・この種類に切り替わったときに立つ（切り替えのなめらかさは切り替え部品が受け持つ）。
    /// </summary>
    protected bool SnapNext { get; set; } = true;

    private SpaceJunkCameraModeSwitch owner;

    protected virtual void Awake()
    {
        View = GetComponent<Camera>();
        owner = GetComponent<SpaceJunkCameraModeSwitch>();
    }

    /// <summary>
    /// 追いかける相手を決める。**オンラインでは、自分のプレイヤーが生まれた時点で渡される**
    /// （<c>SpaceJunkPlayerSetup.FollowWithCamera</c>）。渡されなくても、自分のプレイヤーを自分で探す。
    /// </summary>
    public virtual void SetTarget(Transform newTarget)
    {
        target = newTarget;
        SnapNext = true;
    }

    /// <summary>この種類に切り替わったときに呼ばれる。</summary>
    public virtual void Activate()
    {
        SnapNext = true;
    }

    /// <summary>
    /// **いまカメラを置きたい位置と向きを求める。** 毎フレーム、この種類が選ばれている間だけ呼ばれる。
    /// 置き場所が決まらない（相手がまだいない など）ときは false を返す（カメラはそのまま）。
    /// </summary>
    public abstract bool TryGetPose(float deltaTime, out Vector3 position, out Quaternion rotation);

    /// <summary>切り替え部品が無いときだけ、自分でカメラを動かす。</summary>
    protected virtual void LateUpdate()
    {
        if (owner != null)
        {
            return;
        }

        if (TryGetPose(Time.deltaTime, out Vector3 position, out Quaternion rotation))
        {
            transform.SetPositionAndRotation(position, rotation);
        }
    }

    // ------------------------------------------------------------
    // 誰を映すか
    // ------------------------------------------------------------

    /// <summary>自分のプレイヤー。渡されていなければ探す（検証シーンでは検証用の相手）。</summary>
    protected Transform LocalTarget()
    {
        if (SpaceJunkCameraTestTargets.Active != null)
        {
            return SpaceJunkCameraTestTargets.Active.Self;
        }

        if (target != null)
        {
            return target;
        }

        foreach (FishingNetPlayer player in FishingNetPlayer.All)
        {
            if (player != null && player.IsOwner)
            {
                return player.transform;
            }
        }

        return null;
    }

    /// <summary>
    /// **自分と同じチームの、自分以外のプレイヤー**を <paramref name="into"/> に入れる。
    /// 検証シーンでは、検証用の相手を入れる。
    /// </summary>
    protected static void GatherTeammates(Transform self, List<Transform> into)
    {
        into.Clear();

        if (SpaceJunkCameraTestTargets.Active != null)
        {
            SpaceJunkCameraTestTargets.Active.GetTeammates(into);
            return;
        }

        SpaceJunkSession session = SpaceJunkSession.Current;
        NetworkManager manager = NetworkManager.Singleton;
        if (session == null || manager == null)
        {
            return;
        }

        int myTeam = session.TeamOf(manager.LocalClientId);

        foreach (FishingNetPlayer player in FishingNetPlayer.All)
        {
            if (player == null || player.IsOwner || player.transform == self)
            {
                continue;
            }

            if (session.TeamOf(player.OwnerClientId) == myTeam)
            {
                into.Add(player.transform);
            }
        }
    }

    // ------------------------------------------------------------
    // 向き
    // ------------------------------------------------------------

    /// <summary>
    /// **自陣が画面の手前に来る水平の角度（度）。北向きが 0。**
    ///
    /// 自分のチームのゴールが全部ある側から、ステージの中心を見る。
    /// 自分のゴールそれぞれの「中心からの方向」を足し合わせて、その反対を向く。
    ///
    /// | 自分のゴール | 向き |
    /// | --- | --- |
    /// | 1つ（2〜4チームのとき） | そのゴールの辺の**真正面**から見る |
    /// | 隣り合う2つ（いまのルールでは出ない） | 2つのゴールの間の角から、**斜め45度**で見る |
    /// | 4つ全部（1チームのとき） | 方向が打ち消し合って決まらないので、**北向きのまま** |
    ///
    /// ゴールの持ち主はラウンドが始まってから決まるので、**毎フレーム計算し直してよい**（4つしか無いので軽い）。
    /// </summary>
    protected static float OwnSideYaw()
    {
        if (SpaceJunkCameraTestTargets.Active != null)
        {
            return SpaceJunkCameraTestTargets.Active.Yaw;
        }

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

    // ------------------------------------------------------------
    // 画面に入るか
    // ------------------------------------------------------------

    /// <summary>
    /// **カメラを <paramref name="position"/>・<paramref name="rotation"/> に置いたとき、
    /// 点が画面の端から <paramref name="margin"/> 以上内側に映るか。**
    /// カメラを実際に動かさずに計算する（毎フレーム何十回も試すため）。
    /// </summary>
    protected bool IsInView(Vector3 position, Quaternion rotation, Vector3 point, float margin)
    {
        // カメラから見た位置（Unity のカメラは -Z が前）
        Vector3 local = Quaternion.Inverse(rotation) * (point - position);
        Vector4 clip = View.projectionMatrix * new Vector4(local.x, local.y, -local.z, 1f);

        if (clip.w <= 0.0001f || local.z <= View.nearClipPlane)
        {
            return false;
        }

        float x = clip.x / clip.w * 0.5f + 0.5f;
        float y = clip.y / clip.w * 0.5f + 0.5f;

        return x >= margin && x <= 1f - margin && y >= margin && y <= 1f - margin;
    }

    /// <summary>
    /// **見る点 <paramref name="focus"/> から後ろへ下がる距離のうち、点が全部画面に入るいちばん近い距離。**
    /// <paramref name="near"/>〜<paramref name="far"/> の間で二分探索する。far でも入らなければ far を返す。
    /// </summary>
    protected float FitDistance(Vector3 focus, Quaternion rotation, List<Vector3> points, float margin, float near, float far)
    {
        Vector3 back = rotation * Vector3.back;

        if (AllInView(focus + back * near, rotation, points, margin))
        {
            return near;
        }

        if (!AllInView(focus + back * far, rotation, points, margin))
        {
            return far;
        }

        for (int i = 0; i < 24; i++)
        {
            float middle = (near + far) * 0.5f;

            if (AllInView(focus + back * middle, rotation, points, margin))
            {
                far = middle;
            }
            else
            {
                near = middle;
            }
        }

        return far;
    }

    private bool AllInView(Vector3 position, Quaternion rotation, List<Vector3> points, float margin)
    {
        foreach (Vector3 point in points)
        {
            if (!IsInView(position, rotation, point, margin))
            {
                return false;
            }
        }
        return true;
    }

    /// <summary>なめらかに寄せるときの割合（フレームの長さに左右されない形）。</summary>
    protected static float Smooth(float sharpness, float deltaTime)
    {
        return 1f - Mathf.Exp(-sharpness * deltaTime);
    }
}
