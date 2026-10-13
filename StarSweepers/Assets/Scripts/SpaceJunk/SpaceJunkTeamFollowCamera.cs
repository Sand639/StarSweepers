using UnityEngine;

/// <summary>
/// **自分の陣地がいつも画面の手前に来るように向きを回す、自分だけを追いかけるカメラ。**（切り替えの名前は「自分」）
///
/// ワイルドリフトの赤チームのように、チームによって画面の向きが変わる
/// （2026/9/22・大槻さんの依頼）。見下ろす角度・高さ・後ろへの距離はふつうのカメラと同じで、
/// **水平の向きだけ**がチームで変わる。向きの決め方は <see cref="SpaceJunkCameraMode.OwnSideYaw"/>。
///
/// 味方も一緒に映したいときは、チームカメラ（<see cref="SpaceJunkTeamGroupCamera"/>）を使う。
///
/// ## 移動の向き
///
/// カメラを回すと、そのままでは W が画面の上にならない。
/// 移動（`FishingPlayerController`）は**カメラの向きを基準に**動くようにしてあるので、
/// どのチームでも **W＝画面の奥**になる。
///
/// ## 置いてある場所
///
/// 宇宙ごみのマップ（`SpaceJunkMap01TeamCam`。これだけで動く）と、
/// STAGE_01〜05 のカメラ（`SpaceJunkStageCamera.prefab`。C キーで切り替え → <see cref="SpaceJunkCameraModeSwitch"/>）。
/// 2026/10/7 に、カメラの種類の共通部品（<see cref="SpaceJunkCameraMode"/>）に乗せ替えた（動きは前と同じ）。
/// </summary>
public class SpaceJunkTeamFollowCamera : SpaceJunkCameraMode
{
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

    protected override string DefaultName => "自分";

    private Vector3 position;

    public override bool TryGetPose(float deltaTime, out Vector3 outPosition, out Quaternion outRotation)
    {
        outPosition = transform.position;
        outRotation = transform.rotation;

        Transform self = LocalTarget();
        if (self == null)
        {
            return false;
        }

        if (SnapNext)
        {
            // 相手が決まった瞬間・この種類に切り替わった瞬間は、なめらかにせずその場所・その向きへ
            Yaw = OwnSideYaw();
            position = Goal(self);
            SnapNext = false;
        }
        else
        {
            // 回る向きは、少しずつ寄せる（ラウンドの始めにゴールの持ち主が決まった瞬間に、
            // 画面がいきなり回ると酔いやすいため）
            Yaw = Mathf.LerpAngle(Yaw, OwnSideYaw(), Smooth(turnSharpness, deltaTime));
            position = Vector3.Lerp(position, Goal(self), Smooth(followSharpness, deltaTime));
        }

        outPosition = position;
        outRotation = Quaternion.Euler(pitch, Yaw, 0f);
        return true;
    }

    private Vector3 Goal(Transform self)
    {
        return self.position + Quaternion.Euler(0f, Yaw, 0f) * offset;
    }
}
