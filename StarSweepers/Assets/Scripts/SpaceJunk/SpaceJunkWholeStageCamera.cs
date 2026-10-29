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
/// ・**「手で位置を決める」を ON にすると、1. 見る先・2. 見る先からの距離・3. カメラの位置で置ける**（2026/10/29・大槻さん）。
///   2 と 3 はつながっていて、どちらかを変えるともう片方も合わせて変わる（<see cref="OnValidate"/>）。
///   **3つのどれかを変えると、「手で位置を決める」は自動で ON になる**（OFF のままだと、再生中は自動の値で毎フレーム上書きされて動かせないため）。
///   - 再生中：OFF の間も自動で決めた値が欄に出るので、その見え方から調整を始められる。
///     **再生中に変えた値は、再生を止めると消える**ので、気に入ったら部品の︙ →「Copy Component」→ 止めてから「Paste Component Values」
///   - 再生していないとき：ON にすると、ステージの大きさから自動で決まる見え方が欄に入る。値を変えるとカメラがその位置へ動くので、
///     Game ビューで見え方を確かめながら決められる（シーンを保存すれば残る）
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

    [Header("手で位置を決める（2026/10/29）")]
    [Tooltip("ON：下の「1. 見る先」「2. 距離」「3. カメラの位置」で全体カメラを置く（自陣向き・見下ろす角度・すき間は使わない）。" +
             "OFF：ステージの大きさから自動で決める（今までどおり）。" +
             "下の3つのどれかを変えると、自動で ON になる。" +
             "再生中は、OFF の間も下の欄に自動で決めた値が出る。再生していないときは、ON にすると自動で決まる見え方が下の欄に入る")]
    [SerializeField] private bool manualPose = false;

    [Tooltip("1. 見る先（カメラが向く点）。変えると、カメラの位置はそのままで、距離が合わせて変わる")]
    [SerializeField] private Vector3 lookTarget = Vector3.zero;

    [Tooltip("2. 見る先からの距離（m）。変えると、カメラの位置が今の向きのまま近づく・離れる")]
    [Min(0.1f)]
    [SerializeField] private float distance = 40f;

    [Tooltip("3. カメラの位置。変えると、見る先からの距離が合わせて変わる")]
    [SerializeField] private Vector3 cameraPosition = new Vector3(0f, 34.64f, -20f);

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
        if (manualPose)
        {
            GetManualPose(out position, out rotation);
            EnsureFarClip(distance);
            return true;
        }

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
        float fitDistance = FitDistance(center, rotation, corners, screenMargin, 1f, 500f);
        position = center + rotation * Vector3.back * fitDistance;
        EnsureFarClip(fitDistance);

        // 自動で決めた値を「手で位置を決める」の欄に出しておく（再生中だけ。ON にしたとき、この見え方から調整を始められるように）
        lookTarget = center;
        distance = fitDistance;
        cameraPosition = position;
        RememberLinkedValues();

        return true;
    }

    /// <summary>手で決めた位置から、見る先を向く（見る先と位置が重なっているときは、向きが決まらないので下を向く）。</summary>
    private void GetManualPose(out Vector3 position, out Quaternion rotation)
    {
        position = cameraPosition;
        Vector3 look = lookTarget - cameraPosition;
        rotation = look.sqrMagnitude > 0.0001f ? Quaternion.LookRotation(look, Vector3.up) : Quaternion.Euler(90f, 0f, 0f);
    }

    /// <summary>
    /// 遠くに置くので、奥が切れないよう描く距離を広げておく
    /// （目安：ステージの奥の端までは、見る先までの距離の2倍あれば届く。50 は余裕のメートル数。狭めることはしない）。
    /// </summary>
    private void EnsureFarClip(float viewDistance)
    {
        float needFar = viewDistance * 2f + 50f;
        if (View.farClipPlane < needFar)
        {
            View.farClipPlane = needFar;
        }
    }

    // ------------------------------------------------------------
    // 「2. 距離」と「3. カメラの位置」をつなぐ（どちらかを変えたら、もう片方も合わせて変える）
    // ------------------------------------------------------------

    /// <summary>前に見たときの「見る先」「距離」「カメラの位置」「手で決めるか」。どれが変えられたかを見分けるために持つ。</summary>
    private Vector3 lastLookTarget;
    private float lastDistance;
    private Vector3 lastCameraPosition;
    private bool lastManualPose;

    /// <summary>前の値をもう覚えたか（読み込んだ直後の1回目は、覚えるだけにする）。</summary>
    private bool linkReady;

    /// <summary>
    /// **インスペクターで値が変えられたとき**（Unity が呼ぶ。再生中でも呼ばれる）。
    /// ・距離を変えた → カメラの位置を、見る先から今の向きのまま、その距離の所へ動かす
    /// ・カメラの位置か見る先を変えた → 距離を、見る先からカメラの位置までの長さに直す
    /// ・3つのどれかを変えた → 「手で位置を決める」を ON にする
    /// ・再生していないとき → ON にした瞬間は自動で決まる見え方を欄に入れ、ON の間はカメラをその位置へ動かす（Game ビューで確かめられるように）
    /// </summary>
    private void OnValidate()
    {
        if (!linkReady)
        {
            RememberLinkedValues();
            return;
        }

        bool distanceChanged = !Mathf.Approximately(distance, lastDistance);
        bool poseChanged = cameraPosition != lastCameraPosition || lookTarget != lastLookTarget;

        if (distanceChanged)
        {
            // 向きが決まらない（見る先と位置が重なっている）ときは、全体カメラの角度（Pitch）で南から見る向きにする
            Vector3 direction = cameraPosition - lookTarget;
            if (direction.sqrMagnitude < 0.0001f)
            {
                direction = Quaternion.Euler(pitch, 0f, 0f) * Vector3.back;
            }

            cameraPosition = lookTarget + direction.normalized * distance;
        }
        else if (poseChanged)
        {
            distance = Mathf.Max(0.1f, Vector3.Distance(cameraPosition, lookTarget));
        }

        if (distanceChanged || poseChanged)
        {
            // OFF のままだと、再生中は自動で決めた値で毎フレーム上書きされ、変えても元に戻ってしまう
            // （2026/10/29・大槻さん「位置が固定されてた」）
            manualPose = true;
        }
#if UNITY_EDITOR
        else if (manualPose && !lastManualPose && !Application.isPlaying)
        {
            CopyAutoPoseInEditor();
        }

        if (manualPose && !Application.isPlaying)
        {
            PreviewInEditor();
        }
#endif

        RememberLinkedValues();
    }

    /// <summary>いまの「見る先」「距離」「カメラの位置」「手で決めるか」を、次に見分けるために覚える。</summary>
    private void RememberLinkedValues()
    {
        lastLookTarget = lookTarget;
        lastDistance = distance;
        lastCameraPosition = cameraPosition;
        lastManualPose = manualPose;
        linkReady = true;
    }

#if UNITY_EDITOR
    /// <summary>
    /// 再生していないときに、ステージの大きさから自動で決まる見え方（北向き・Pitch の角度）を欄に入れる。
    /// プレハブの編集画面など、同じシーンにステージが無いときは、今の値のままにする。
    /// </summary>
    private void CopyAutoPoseInEditor()
    {
        if (!TryGetStageBounds(out Bounds stage))
        {
            return;
        }

        List<Vector3> points = new List<Vector3>();
        AddCorners(stage, points);

        Quaternion rotation = Quaternion.Euler(pitch, 0f, 0f);
        float fitDistance = FitDistance(stage.center, rotation, points, screenMargin, 1f, 500f);

        lookTarget = stage.center;
        distance = fitDistance;
        cameraPosition = stage.center + rotation * Vector3.back * fitDistance;
    }

    /// <summary>
    /// 再生していないときに、カメラを手で決めた位置へ動かす（Game ビューで見え方を確かめるため）。
    /// OnValidate の中で動かすと Unity が警告を出すことがあるので、次のエディタの更新（EditorApplication.update）で1回だけ動かす
    /// （delayCall は画面なしの Unity で2回目から呼ばれないことがあったので使わない）。
    /// 再生中は欄の値で置き直すので、ここで動かした位置が保存されなくても困らない。
    /// </summary>
    private void PreviewInEditor()
    {
        // 何回呼ばれても1回だけ動かすよう、外してから足す
        UnityEditor.EditorApplication.update -= ApplyPreviewInEditor;
        UnityEditor.EditorApplication.update += ApplyPreviewInEditor;
    }

    private void ApplyPreviewInEditor()
    {
        UnityEditor.EditorApplication.update -= ApplyPreviewInEditor;
        if (this == null || !manualPose || Application.isPlaying)
        {
            return;
        }

        GetManualPose(out Vector3 position, out Quaternion rotation);
        transform.SetPositionAndRotation(position, rotation);
    }
#endif

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
        corners.Clear();
        AddCorners(stage, corners);
    }

    /// <summary>箱の角8つ（下の面の4つ → 上の面の4つ）を <paramref name="into"/> に足す。</summary>
    private static void AddCorners(Bounds box, List<Vector3> into)
    {
        Vector3 min = box.min;
        Vector3 max = box.max;
        into.Add(new Vector3(min.x, min.y, min.z));
        into.Add(new Vector3(max.x, min.y, min.z));
        into.Add(new Vector3(min.x, min.y, max.z));
        into.Add(new Vector3(max.x, min.y, max.z));
        into.Add(new Vector3(min.x, max.y, min.z));
        into.Add(new Vector3(max.x, max.y, min.z));
        into.Add(new Vector3(min.x, max.y, max.z));
        into.Add(new Vector3(max.x, max.y, max.z));
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
