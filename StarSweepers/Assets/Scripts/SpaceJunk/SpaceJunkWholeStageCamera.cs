using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// **全体カメラ。ステージ全体が画面に収まる位置から見下ろす。動かない。**（切り替えの名前は「全体」）
///
/// 2026/10/6 に大槻さんの依頼で <see cref="SpaceJunkCameraModeSwitch"/> の中に作ったものを、
/// 2026/10/7 に部品として分けた（小野田さん「カメラを細かく調整できるように構造を整理してほしい」）。
///
/// ・全体の位置は、**始まったときにステージの大きさを測って自動で決める**（ステージの形を変えても直さなくてよい）。
///   プレイヤー・素材・爆弾・列車など、動く物は測る対象に入れない
/// ・「自陣を手前にする」を ON にすると、チームカメラと同じ向き（自陣が手前）で全体を映す。
///   OFF なら全員が北向き（前からの動き）
/// </summary>
public class SpaceJunkWholeStageCamera : SpaceJunkCameraMode
{
    [Header("全体カメラ")]
    [Tooltip("見下ろす角度（度）")]
    [SerializeField] private float pitch = 60f;

    [Tooltip("画面の端に空けるすき間（画面の幅を1としたときの割合）")]
    [SerializeField] private float screenMargin = 0.04f;

    [Tooltip("これより大きい物は、ステージの大きさを測るときに入れない（遠くの背景など）")]
    [SerializeField] private float ignoreLargerThan = 200f;

    [Tooltip("ON：自陣が画面の手前に来るように回す（チームカメラと同じ向き）。OFF：全員北向き")]
    [SerializeField] private bool faceOwnSide = false;

    [Tooltip("自陣向きにするとき、向きが変わる回りのなめらかさ。大きいほどすぐ回る")]
    [SerializeField] private float turnSharpness = 4f;

    protected override string DefaultName => "全体";

    private readonly List<Vector3> corners = new List<Vector3>();
    private bool measured;
    private bool hasStage;
    private Vector3 center;
    private Vector3 fallbackPosition;
    private Quaternion fallbackRotation;
    private float yaw;

    private void Start()
    {
        Measure();
    }

    public override bool TryGetPose(float deltaTime, out Vector3 position, out Quaternion rotation)
    {
        Measure();

        if (!hasStage)
        {
            // 測れなければ、置いてある位置を全体カメラの位置にする
            position = fallbackPosition;
            rotation = fallbackRotation;
            return true;
        }

        float goalYaw = faceOwnSide ? OwnSideYaw() : 0f;
        yaw = SnapNext ? goalYaw : Mathf.LerpAngle(yaw, goalYaw, Smooth(turnSharpness, deltaTime));
        SnapNext = false;

        rotation = Quaternion.Euler(pitch, yaw, 0f);
        float distance = FitDistance(center, rotation, corners, screenMargin, 1f, 500f);
        position = center + rotation * Vector3.back * distance;

        // 遠くに置くので、奥が切れないよう描く距離を広げておく
        float needFar = distance * 2f + 50f;
        if (View.farClipPlane < needFar)
        {
            View.farClipPlane = needFar;
        }

        return true;
    }

    // ------------------------------------------------------------
    // ステージの大きさを測る
    // ------------------------------------------------------------

    /// <summary>**ステージの見た目が占める範囲を測り、その角8つを覚える。** 1回だけ行う。</summary>
    private void Measure()
    {
        if (measured)
        {
            return;
        }

        measured = true;
        fallbackPosition = transform.position;
        fallbackRotation = transform.rotation;

        hasStage = TryGetStageBounds(out Bounds stage);
        if (!hasStage)
        {
            return;
        }

        center = stage.center;
        Vector3 min = stage.min;
        Vector3 max = stage.max;
        corners.Clear();
        corners.Add(new Vector3(min.x, min.y, min.z));
        corners.Add(new Vector3(max.x, min.y, min.z));
        corners.Add(new Vector3(min.x, min.y, max.z));
        corners.Add(new Vector3(max.x, min.y, max.z));
        corners.Add(new Vector3(min.x, max.y, min.z));
        corners.Add(new Vector3(max.x, max.y, min.z));
        corners.Add(new Vector3(min.x, max.y, max.z));
        corners.Add(new Vector3(max.x, max.y, max.z));
    }

    /// <summary>ステージの見た目の範囲。動く物（プレイヤー・物資・列車）や画面の表示は入れない。</summary>
    private bool TryGetStageBounds(out Bounds bounds)
    {
        bounds = default;
        bool found = false;

        foreach (Renderer renderer in FindObjectsByType<Renderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            if (renderer == null || !renderer.enabled || !renderer.gameObject.scene.IsValid() ||
                renderer.gameObject.scene != gameObject.scene)
            {
                continue;
            }

            if (renderer.transform.IsChildOf(transform) ||
                renderer.GetComponentInParent<Canvas>() != null ||
                renderer.GetComponentInParent<HookableObject>() != null ||
                renderer.GetComponentInParent<FishingPlayerController>() != null ||
                renderer.GetComponentInParent<SpaceJunkTrain>() != null ||
                renderer.GetComponentInParent<SpaceJunkCameraTestDummy>() != null)
            {
                continue;
            }

            Bounds b = renderer.bounds;
            if (b.size.x > ignoreLargerThan || b.size.z > ignoreLargerThan)
            {
                continue;
            }

            if (!found)
            {
                bounds = b;
                found = true;
            }
            else
            {
                bounds.Encapsulate(b);
            }
        }

        return found;
    }
}
