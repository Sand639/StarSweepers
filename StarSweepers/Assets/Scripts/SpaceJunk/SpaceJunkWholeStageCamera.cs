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

    /// <summary>ステージを囲む箱の角8つ。これが全部画面に入る距離まで下がる。</summary>
    private readonly List<Vector3> corners = new List<Vector3>();

    /// <summary>もう測ったか（測るのは1回だけ）。</summary>
    private bool measured;

    /// <summary>ステージの大きさを測れたか。測れなければ、置いてある位置をそのまま使う。</summary>
    private bool hasStage;

    /// <summary>ステージを囲む箱の真ん中。カメラはここを見る。</summary>
    private Vector3 center;

    /// <summary>測れなかったときに使う、シーンに置いてあった位置と向き（測る前に控えておく）。</summary>
    private Vector3 fallbackPosition;
    private Quaternion fallbackRotation;

    /// <summary>いまの水平の角度（度）。北向きが 0。</summary>
    private float yaw;

    private void Start()
    {
        // シーンが始まったところで測っておく（TryGetPose が先に呼ばれたときは、そちらで測る。2回目からは何もしない）
        Measure();
    }

    /// <summary>
    /// ステージ全体が入る位置を求める。向きが自陣向きのときだけ毎フレーム変わり、距離はそのたびに測り直す。
    /// 測れなかったときも、置いてある位置を返す（false にはしない）。
    /// </summary>
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

        // 箱の角が全部入る、いちばん近い距離（1〜500 メートルの間で探す）。真ん中からカメラの向きの真後ろへ下がる
        float distance = FitDistance(center, rotation, corners, screenMargin, 1f, 500f);
        position = center + rotation * Vector3.back * distance;

        // 遠くに置くので、奥が切れないよう描く距離を広げておく
        // （目安：ステージの奥の端までは、真ん中までの距離の2倍あれば届く。50 は余裕のメートル数。狭めることはしない）
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

        // 箱の角8つ（下の面の4つ → 上の面の4つ）
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
            // このカメラと同じシーンの物だけ（シーンをまたいで残る物や、ほかに読み込まれているシーンの物は入れない）
            if (renderer == null || !renderer.enabled || !renderer.gameObject.scene.IsValid() ||
                renderer.gameObject.scene != gameObject.scene)
            {
                continue;
            }

            // 測らない物：カメラ自身の子・画面の表示（Canvas）・物資（HookableObject）・プレイヤー・列車・カメラ検証用の人形
            if (renderer.transform.IsChildOf(transform) ||
                renderer.GetComponentInParent<Canvas>() != null ||
                renderer.GetComponentInParent<HookableObject>() != null ||
                renderer.GetComponentInParent<FishingPlayerController>() != null ||
                renderer.GetComponentInParent<SpaceJunkTrain>() != null ||
                renderer.GetComponentInParent<SpaceJunkCameraTestDummy>() != null)
            {
                continue;
            }

            // 横か奥行きが大きすぎる物（遠くの背景など）は入れない（高さは見ない）
            Bounds rendererBounds = renderer.bounds;
            if (rendererBounds.size.x > ignoreLargerThan || rendererBounds.size.z > ignoreLargerThan)
            {
                continue;
            }

            // 1つ目はそのまま、2つ目からは今までの範囲を広げて包む
            if (!found)
            {
                bounds = rendererBounds;
                found = true;
            }
            else
            {
                bounds.Encapsulate(rendererBounds);
            }
        }

        return found;
    }
}
