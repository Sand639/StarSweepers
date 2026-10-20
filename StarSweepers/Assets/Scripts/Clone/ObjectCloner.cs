using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 目の前のオブジェクトを複製して、好きな場所に置ける機能。
///
/// 遊び方：
///   E              … 見ているオブジェクトを複製する（プレビューが出る）
///   マウス移動     … 向きを決める。複製は**視線の先の一定距離**に付いてくる
///   マウスホイール … 置く距離を前後に動かす
///   Z / X / C      … 押しながらマウスを動かすと、X軸・Y軸・Z軸で回る（回る向きの輪が出る）
///   マウス中ボタン … 押しながら動かすと、3軸まとめて自由に回る
///   Q              … 押すたびに切り替え。ONだと視点を動かすと向きも一緒に回る
///                     （OFFのときは、地面と平行な向きで複製される）
///   T              … XYZすべて0の、地面と平行なまっすぐの向きに戻す
///   左クリック     … その場所に確定して置く
///   Escape         … 置くのをやめる
///   R              … 自分が置いたものを消す
///
/// 地面に貼り付けるのではなく視線の先に固定するので、**空中にも置ける**。
/// 置いた瞬間に物理が働くので、空中に置けばそこから落ちる。
/// 落とさずその場に留めたい場合は keepFloatingAfterPlace を ON にする。
///
/// 置ける数には上限があり、超えると**一番古いものが自動で消える**。
/// 消える少し前に点滅して知らせる。
///
/// PlayerRig に付けて使う。参照はインスペクターから受け取るので、
/// 他のオブジェクトを名前で探しにいかない。
///
/// ## 流れ（毎フレーム、Update で）
///
/// 1. **止まっている間は何もしない**（<see cref="GamePause.BlocksInput"/>）
/// 2. 消える予告の点滅を進める（<see cref="UpdateWarningBlink"/>）
/// 3. **プレビューが無いとき** … 狙っている物を強調し、E で複製を始める／R で消す（<see cref="UpdateAiming"/>）
/// 4. **プレビュー中** … 位置を決め直し、左クリックで確定／Escape でやめる（<see cref="UpdatePreview"/>）
///
/// 位置決め（<see cref="UpdatePreviewPosition"/>）の中身は
/// 「ホイールで距離 → キーで回転 → 壁の手前で止める → 重なるなら近くの空きを探す」の順。
/// 確定したら、上限を超えた分だけ一番古いものを点滅させてから消す。
/// </summary>
public class ObjectCloner : MonoBehaviour
{
    [Header("つなぐもの")]
    [Tooltip("狙いを決めるカメラ。PlayerRig の中の PlayerCamera を入れる")]
    [SerializeField] private Camera aimCamera;

    [Tooltip("自分自身。ここに含まれるものは狙いの対象から外す")]
    [SerializeField] private Transform playerRoot;

    [Tooltip("距離を測る基準にする場所。PlayerRig の中の CameraPivot（頭の位置）を入れる。" +
             "空のときはカメラの親を自動で使う")]
    [SerializeField] private Transform aimOrigin;

    [Tooltip("Assets/InputSystem_Actions を入れる")]
    [SerializeField] private InputActionAsset inputActions;

    [Header("狙える距離")]
    [Tooltip("複製したいものを狙える距離（メートル）")]
    [Range(1f, 30f)]
    [SerializeField] private float pickDistance = 10f;

    [Header("置く位置（視線の先に固定する）")]
    [Tooltip("自分から何メートル先に置くか。複製した直後の距離")]
    [Range(0.3f, 20f)]
    [SerializeField] private float placeDistance = 2.5f;

    [Tooltip("どこまで近づけられるか（メートル）。小さくすると手元に置ける")]
    [Range(0.2f, 5f)]
    [SerializeField] private float minPlaceDistance = 0.5f;

    [Tooltip("どこまで遠ざけられるか（メートル）")]
    [Range(1f, 30f)]
    [SerializeField] private float maxPlaceDistance = 12f;

    [Tooltip("マウスホイール1目盛りで動く距離（メートル）")]
    [Range(0.05f, 2f)]
    [SerializeField] private float distanceStep = 0.25f;

    [Tooltip("ONにすると、壁や物にぶつかる手前で止まる。OFFにすると壁の向こうにも置ける")]
    [SerializeField] private bool blockByWalls = true;

    [Tooltip("壁の手前で止まるときに、どれだけ隙間を空けるか（メートル）")]
    [Range(0f, 0.5f)]
    [SerializeField] private float wallMargin = 0.02f;

    [Tooltip("ONにすると、他の物と重なる場所には置けなくなる")]
    [SerializeField] private bool blockByOverlap = true;

    [Tooltip("重なってしまうとき、近くの置ける場所をどこまで探すか（メートル）。0にすると探さず、直前の置ける位置で止まる")]
    [Range(0f, 5f)]
    [SerializeField] private float searchDistance = 1.5f;

    [Tooltip("置ける場所を探すときの刻み幅（メートル）。小さくすると正確になるが、処理は重くなる")]
    [Range(0.05f, 1f)]
    [SerializeField] private float searchStep = 0.25f;

    [Header("置く向き")]
    [Tooltip("ONにすると、複製した瞬間にカメラの向きに合わせる。OFFにすると複製元と同じ向きで出てくる")]
    [SerializeField] private bool alignToCamera = true;

    [Tooltip("押すたびに切り替わる。ONの間は視点を動かすと物の向きも一緒に回り、OFFなら向きは固定される")]
    [SerializeField] private Key followViewKey = Key.Q;

    [Tooltip("最初から視点に追従する状態で始めるか")]
    [SerializeField] private bool followViewOnStart = false;

    [Header("置く前に回す（キーを押しながらマウス移動）")]
    [Tooltip("押しながらマウスを**上下**に動かすと、X軸で回る（前後に倒れる）")]
    [SerializeField] private Key rotateXKey = Key.Z;

    [Tooltip("押しながらマウスを**左右**に動かすと、Y軸で回る（その場で向きを変える）")]
    [SerializeField] private Key rotateYKey = Key.X;

    [Tooltip("押しながらマウスを**左右**に動かすと、Z軸で回る（左右に倒れる）")]
    [SerializeField] private Key rotateZKey = Key.C;

    [Tooltip("ONにすると、マウスの中ボタンを押しながらの移動で3軸まとめて自由に回せる")]
    [SerializeField] private bool useMiddleButtonFreeRotate = true;

    [Tooltip("XYZすべて0の、地面と平行なまっすぐの向きに戻すキー（視点追従もOFFになる）")]
    [SerializeField] private Key resetRotationKey = Key.T;

    [Tooltip("マウスを動かしたときの回る量。大きいほど速く回る")]
    [Range(0.05f, 3f)]
    [SerializeField] private float rotationSensitivity = 0.4f;

    [Tooltip("回転を何度単位で止めるか。0にすると滑らかに回る（例：15にすると15度ずつ）")]
    [Range(0f, 90f)]
    [SerializeField] private float rotationSnap = 0f;

    [Tooltip("ONだと世界のXYZ軸で回る。OFFだと物自身の向きを基準に回る")]
    [SerializeField] private bool rotateInWorldSpace = true;

    [Tooltip("回している間、視点が動かないよう止める相手。空でも動くが、視点も一緒に回ってしまう")]
    [SerializeField] private PlayerController playerController;

    [Header("置いたあとの動き")]
    [Tooltip("ONにすると、置いた物は空中でも落ちずにその場に留まる（足場向き）。OFFだと普通に落ちて、ぶつかり合う")]
    [SerializeField] private bool keepFloatingAfterPlace = false;

    [Header("置ける数")]
    [Tooltip("同時に置いておける数。超えると一番古いものが消える")]
    [Range(1, 30)]
    [SerializeField] private int maxPlacedObjects = 5;

    [Tooltip("消える前に点滅させる時間（秒）")]
    [Range(0f, 3f)]
    [SerializeField] private float warningSeconds = 0.5f;

    [Tooltip("消える予告の点滅の色")]
    [SerializeField] private Color warningColor = new Color(1f, 0.35f, 0.25f);

    [Header("置き方")]
    [Tooltip("プレビュー中の色。半透明にはせず色だけ変える")]
    [SerializeField] private Color previewColor = new Color(0.45f, 0.95f, 0.65f);

    [Header("狙っているものの強調")]
    [Tooltip("画面中央の照準。無くても動く")]
    [SerializeField] private Reticle reticle;

    [Tooltip("複製できるものを狙ったときの色（水色）")]
    [SerializeField] private Color highlightColor = new Color(0.45f, 0.85f, 1f);

    [Tooltip("自分が置いたもの（Rで消せる）を狙ったときの色")]
    [SerializeField] private Color removableColor = new Color(1f, 0.72f, 0.40f);

    [Header("キーの割り当て")]
    [Tooltip("複製するキー。入力設定の Interact（長押し設定つき）でも複製できるが、押した瞬間に反応させるためこちらも見ている")]
    [SerializeField] private Key pickKey = Key.E;

    [Tooltip("複製をやめるキー")]
    [SerializeField] private Key cancelKey = Key.Escape;

    [Tooltip("置いたものを消すキー")]
    [SerializeField] private Key removeKey = Key.R;

    /// <summary>
    /// 置ける場所を探すときに動かしてみる方向。
    /// 上下左右前後の6方向と、その斜め8方向。近い順に少しずつ広げて探す。
    /// </summary>
    private static readonly Vector3[] SearchDirections = BuildSearchDirections();

    /// <summary>複製元だと分かるタグ。</summary>
    public const string DuplicableTag = "Duplicable";

    /// <summary>自分が置いた複製だと分かるタグ。</summary>
    public const string PlacedCloneTag = "PlacedClone";

    /// <summary>消える予告の点滅で、色を入れ替える間隔（秒）。</summary>
    private const float WarningBlinkInterval = 0.08f;

    /// <summary>自分が置いた物。古い順に並んでいて、先頭が一番古い。</summary>
    private readonly Queue<GameObject> placedObjects = new Queue<GameObject>();

    /// <summary>狙いや壁の判定で、当たった物を受け取る入れ物（最大16個）。毎回作らずに使い回す。</summary>
    private readonly RaycastHit[] hitBuffer = new RaycastHit[16];

    // 色を変える前に元の色を控えておく。
    // 一度 renderer.material を触ると sharedMaterial も複製側を指すようになり、
    // 「元の色」を後から取り出せなくなるため。
    private readonly List<Renderer> previewRenderers = new List<Renderer>();
    private readonly List<Color> previewOriginalColors = new List<Color>();
    private readonly List<Renderer> warningRenderers = new List<Renderer>();
    private readonly List<Color> warningOriginalColors = new List<Color>();
    private readonly List<Renderer> highlightRenderers = new List<Renderer>();
    private readonly List<Color> highlightOriginalColors = new List<Color>();

    private GameObject highlightedObject;

    private InputActionMap playerMap;
    private InputAction pickAction;    // E（Interact）
    private InputAction placeAction;   // 左クリック（Attack）

    private GameObject previewObject;
    private GameObject warningObject;
    private float warningTimer;

    /// <summary>いま置こうとしている距離。マウスホイールで変わる。</summary>
    private float currentPlaceDistance;

    // 重なりを調べるための情報。プレビューを作ったときに1回だけ measure する
    private readonly Collider[] overlapBuffer = new Collider[16];
    private Vector3 previewHalfExtents;
    private Vector3 previewCenterOffset;

    // 最後に「置けた」位置。置けない場所を狙っている間は、ここで止めておく
    private Vector3 lastValidPosition;
    private Quaternion lastValidRotation;
    private bool hasValidPlacement;

    // 置く前に自分で回した分。複製するたびにリセットされる
    private Quaternion manualRotation = Quaternion.identity;

    // 基準の向き。ふだんは固定で、Qを押している間だけ視点に合わせて更新される
    private Quaternion currentBaseRotation = Quaternion.identity;

    // いまどの軸を回しているか。回る向きを示す輪の表示に使う
    private bool rotatingX;
    private bool rotatingY;
    private bool rotatingZ;
    private bool freeRotating;

    /// <summary>true の間、視点を動かすと物の向きも一緒に回る。Qキーで切り替わる。</summary>
    public bool FollowView { get; private set; }

    private RotationGizmo rotationGizmo;

    /// <summary>いま置いてある数。UIから見たいときのために公開している。</summary>
    public int PlacedCount => placedObjects.Count;

    /// <summary>置ける最大数。</summary>
    public int MaxPlacedObjects => maxPlacedObjects;

    /// <summary>いまプレビュー中かどうか。</summary>
    public bool IsPreviewing => previewObject != null;

    private void Awake()
    {
        if (aimCamera == null)
        {
            Debug.LogError($"{name}: カメラが入っていません。複製機能を止めます。", this);
            enabled = false;
            return;
        }

        if (inputActions == null)
        {
            Debug.LogError($"{name}: 入力の設定（InputSystem_Actions）が入っていません。", this);
            enabled = false;
            return;
        }

        // **入力の設定は、この体専用の複製を使う。**
        // 元のアセットをそのまま使うと、1つのシーンに複数の体が出たとき
        // （オンラインや画面分割）に、**他の体の操作を止めると自分の入力まで一緒に止まる**
        inputActions = Instantiate(inputActions);

        playerMap = inputActions.FindActionMap("Player", true);
        pickAction = playerMap.FindAction("Interact", true);
        placeAction = playerMap.FindAction("Attack", true);

        currentPlaceDistance = Mathf.Clamp(placeDistance, minPlaceDistance, maxPlaceDistance);
        FollowView = followViewOnStart;
        rotationGizmo = RotationGizmo.Create(transform);

        // 距離の基準が指定されていなければ、カメラの親（CameraPivot＝頭の位置）を使う。
        // これが無いとTPSのとき、カメラが後ろにある分だけ手前に置かれてしまう
        if (aimOrigin == null)
        {
            aimOrigin = aimCamera.transform.parent != null ? aimCamera.transform.parent : aimCamera.transform;
        }
    }

    /// <summary>
    /// カメラから「距離を測る基準の場所（頭）」までの、視線方向の距離を返す。
    ///
    /// FPSではカメラと頭がほぼ同じ位置なので 0 に近い。
    /// TPSではカメラが後ろに下がっている分だけ大きくなる。
    /// この分を足すことで、**どちらの視点でも「自分から何メートル先か」で置ける**。
    /// </summary>
    private float GetAimOriginOffset()
    {
        if (aimOrigin == null)
        {
            return 0f;
        }

        Transform camera = aimCamera.transform;
        return Mathf.Max(0f, Vector3.Dot(aimOrigin.position - camera.position, camera.forward));
    }

    private void OnEnable()
    {
        playerMap?.Enable();
    }

    private void OnDisable()
    {
        playerMap?.Disable();
        SetLookSuspended(false);
    }

    private void OnDestroy()
    {
        if (inputActions != null)
        {
            Destroy(inputActions);
        }
    }

    /// <summary>毎フレームの入り口。全体の流れはクラスの説明を参照。</summary>
    private void Update()
    {
        // **止まっている間と、閉じた瞬間のフレームは何も受け付けない。**
        // ポーズ画面を開く Escape で複製の下書きが消えてしまうのと、
        // 閉じた同じフレームに、その Escape やクリックをもう一度拾ってしまうのを防ぐ
        if (GamePause.BlocksInput)
        {
            return;
        }

        UpdateWarningBlink();

        // 複製する前でも切り替えられるようにしておく。
        // 「向きを固定するか、視点に合わせるか」を先に決めてから複製できる
        if (WasKeyPressed(followViewKey))
        {
            FollowView = !FollowView;
        }

        if (previewObject == null)
        {
            UpdateAiming();
        }
        else
        {
            UpdatePreview();
        }
    }

    /// <summary>プレビューが無いとき。狙っている物を強調し、E で複製を始める／R で置いた物を消す。</summary>
    private void UpdateAiming()
    {
        UpdateHighlight();

        // 入力設定の Interact は「長押し」設定になっているため、
        // 押した瞬間に反応するようキー直接の判定も併用している
        if (pickAction.WasPressedThisFrame() || WasKeyPressed(pickKey))
        {
            TryStartPreview();
        }

        if (WasKeyPressed(removeKey))
        {
            TryRemoveAimedClone();
        }
    }

    /// <summary>プレビュー中。位置を決め直し、左クリックで確定／Escape でやめる。</summary>
    private void UpdatePreview()
    {
        // プレビュー中は、置こうとしているもの自体が目印になるので強調はしない
        ClearHighlight();
        UpdatePreviewPosition();
        UpdateGizmo();

        if (placeAction.WasPressedThisFrame())
        {
            ConfirmPlacement();
        }
        else if (WasKeyPressed(cancelKey))
        {
            CancelPreview();
        }
    }

    // ------------------------------------------------------------
    // 狙っているものを強調する
    // ------------------------------------------------------------

    /// <summary>
    /// いま狙っているものの色を変えて、「これに操作できる」と分かるようにする。
    /// 複製できるものは水色、自分が置いたもの（Rで消せる）は橙色にする。
    /// </summary>
    private void UpdateHighlight()
    {
        GameObject target = null;
        Color color = highlightColor;

        if (TryAim(pickDistance, out RaycastHit hit))
        {
            if (hit.collider.CompareTag(DuplicableTag))
            {
                target = hit.collider.gameObject;
                color = highlightColor;
            }
            else if (hit.collider.CompareTag(PlacedCloneTag))
            {
                target = hit.collider.gameObject;
                color = removableColor;
            }
        }

        // 消える予告で点滅中のものは、そちらの表示を優先する
        if (target != null && target == warningObject)
        {
            target = null;
        }

        if (target == highlightedObject)
        {
            return;
        }

        ClearHighlight();

        if (target == null)
        {
            return;
        }

        highlightedObject = target;
        CaptureColors(target, highlightRenderers, highlightOriginalColors);
        Tint(highlightRenderers, color);

        if (reticle != null)
        {
            reticle.SetHighlight(color);
        }
    }

    /// <summary>強調をやめて、元の色に戻す。</summary>
    private void ClearHighlight()
    {
        // 強調していた相手が既に消えている場合もあるので、
        // 相手の有無ではなく「控えが残っているか」で判定する
        if (highlightedObject == null && highlightRenderers.Count == 0)
        {
            return;
        }

        RestoreColors(highlightRenderers, highlightOriginalColors);
        highlightedObject = null;

        if (reticle != null)
        {
            reticle.SetHighlight(null);
        }
    }

    // ------------------------------------------------------------
    // 1. 目の前のものを複製してプレビューに入る
    // ------------------------------------------------------------

    /// <summary>狙っている物が複製できる物（Duplicable タグ）なら、複製してプレビューを始める。</summary>
    private void TryStartPreview()
    {
        if (!TryAim(pickDistance, out RaycastHit hit))
        {
            return;
        }

        if (!hit.collider.CompareTag(DuplicableTag))
        {
            return;
        }

        GameObject source = hit.collider.gameObject;

        // 強調中の色のまま複製すると、その色が複製にも移ってしまう。
        // 元の色に戻してから複製する
        ClearHighlight();

        previewObject = Instantiate(source, source.transform.position, source.transform.rotation);
        previewObject.name = source.name + "_Clone";

        SetPhysicsEnabled(previewObject, false);
        StartPreviewLook();

        CachePreviewSize();
        hasValidPlacement = false;

        // 回した分はリセットし、基準の向きを決める
        manualRotation = Quaternion.identity;
        currentBaseRotation = GetInitialBaseRotation(source);

        // 作った直後に正しい位置へ移す。これが無いと1フレームだけ複製元の場所に出てしまう
        UpdatePreviewPosition();
    }

    /// <summary>
    /// 複製した直後の向きを決める。
    ///
    /// 視点追従がONなら、見ている向きそのまま（傾きも付く）。
    /// OFFなら**地面と平行**にして、向きだけ自分が見ている方向に合わせる。
    /// 見上げたり見下ろしたりしながら複製しても、傾いた状態で出てこない。
    /// </summary>
    private Quaternion GetInitialBaseRotation(GameObject source)
    {
        if (FollowView)
        {
            return aimCamera.transform.rotation;
        }

        if (!alignToCamera)
        {
            return source.transform.rotation;
        }

        // 上下の傾きを捨てて、左右の向きだけ使う
        float yaw = aimCamera.transform.eulerAngles.y;
        return Quaternion.Euler(0f, yaw, 0f);
    }

    /// <summary>プレビュー中は色を変えて「まだ置いていない」ことを分かるようにする。</summary>
    private void StartPreviewLook()
    {
        CaptureColors(previewObject, previewRenderers, previewOriginalColors);
        Tint(previewRenderers, previewColor);
    }

    // ------------------------------------------------------------
    // 2. 置く場所を決める
    // ------------------------------------------------------------

    /// <summary>
    /// 置く位置を決める。
    /// 地面に貼り付けるのではなく、**カメラの正面・一定の距離**に固定する。
    /// こうすると空中にも置ける。距離はマウスホイールで前後に動かせる。
    /// </summary>
    private void UpdatePreviewPosition()
    {
        Transform camera = aimCamera.transform;

        UpdatePlaceDistance();
        UpdateManualRotation(camera);

        // 向きを先に決める。傾きによって物の厚みが変わり、壁までの余裕も変わるため
        Quaternion rotation = GetPlacementRotation(camera);

        // 照準（画面中央）の線上に置きつつ、距離は自分の頭から数える。
        // TPSでカメラが後ろに下がっていても、置かれる場所が自分の位置にならない
        float originOffset = GetAimOriginOffset();
        float distance = originOffset + currentPlaceDistance;

        if (blockByWalls)
        {
            distance = ClampDistanceByWall(distance, originOffset, rotation);
        }

        Vector3 desired = camera.position + camera.forward * distance;

        if (TryFindFreePosition(desired, rotation, out Vector3 position))
        {
            // 置ける場所が見つかったので、位置と向きを更新して覚えておく
            previewObject.transform.SetPositionAndRotation(position, rotation);
            lastValidPosition = position;
            lastValidRotation = rotation;
            hasValidPlacement = true;
            return;
        }

        // 近くに置ける場所が無いので、**最後に置けた位置で止めておく**。
        // 動かし続けると、そのまま確定して物にめり込んでしまうため
        if (hasValidPlacement)
        {
            previewObject.transform.SetPositionAndRotation(lastValidPosition, lastValidRotation);
        }
    }

    /// <summary>
    /// 狙った場所に置けるならそこを、置けないなら**一番近い置ける場所**を返す。
    ///
    /// 狙った場所を中心に、少しずつ広げながら周りを調べていく。
    /// 近い距離から順に調べるので、最初に見つかったものが一番近い場所になる。
    /// どこにも置けなければ false を返す。
    /// </summary>
    private bool TryFindFreePosition(Vector3 desired, Quaternion rotation, out Vector3 result)
    {
        result = desired;

        if (!IsPlacementBlocked(desired, rotation))
        {
            return true;
        }

        if (searchDistance <= 0f || searchStep <= 0f)
        {
            return false;
        }

        int steps = Mathf.CeilToInt(searchDistance / searchStep);

        for (int step = 1; step <= steps; step++)
        {
            float radius = searchStep * step;

            foreach (Vector3 direction in SearchDirections)
            {
                Vector3 candidate = desired + direction * radius;

                if (IsPlacementBlocked(candidate, rotation))
                {
                    continue;
                }

                // 壁の向こうへ回り込んでいないか確かめる。
                // これが無いと、薄い壁をすり抜けて反対側に置けてしまう
                if (!HasClearPath(candidate))
                {
                    continue;
                }

                result = candidate;
                return true;
            }
        }

        return false;
    }

    /// <summary>自分から、その場所までの間に壁が無いかを調べる。</summary>
    private bool HasClearPath(Vector3 target)
    {
        Vector3 from = aimOrigin != null ? aimOrigin.position : aimCamera.transform.position;
        Vector3 offset = target - from;
        float length = offset.magnitude;

        if (length <= Mathf.Epsilon)
        {
            return true;
        }

        Ray ray = new Ray(from, offset / length);
        int count = Physics.RaycastNonAlloc(ray, hitBuffer, length, ~0, QueryTriggerInteraction.Ignore);

        for (int i = 0; i < count; i++)
        {
            if (ShouldIgnoreHit(hitBuffer[i].collider.transform))
            {
                continue;
            }

            return false;
        }

        return true;
    }

    /// <summary>探す方向を作る。上下左右前後の6方向と、斜め8方向。</summary>
    private static Vector3[] BuildSearchDirections()
    {
        return new[]
        {
            Vector3.up, Vector3.down,
            Vector3.right, Vector3.left,
            Vector3.forward, Vector3.back,

            new Vector3( 1f,  1f,  1f).normalized,
            new Vector3( 1f,  1f, -1f).normalized,
            new Vector3( 1f, -1f,  1f).normalized,
            new Vector3( 1f, -1f, -1f).normalized,
            new Vector3(-1f,  1f,  1f).normalized,
            new Vector3(-1f,  1f, -1f).normalized,
            new Vector3(-1f, -1f,  1f).normalized,
            new Vector3(-1f, -1f, -1f).normalized,
        };
    }

    /// <summary>
    /// その位置・その向きで置いたときに、他の物と重なるかを調べる。
    /// プレビュー自身は当たり判定を切ってあるので、ここには出てこない。
    /// </summary>
    private bool IsPlacementBlocked(Vector3 position, Quaternion rotation)
    {
        if (!blockByOverlap || previewHalfExtents == Vector3.zero)
        {
            return false;
        }

        Vector3 center = position + rotation * previewCenterOffset;

        // ぴったり接しているだけで「重なった」と判定されないよう、少しだけ小さく調べる
        Vector3 half = previewHalfExtents * 0.98f;

        int count = Physics.OverlapBoxNonAlloc(
            center, half, overlapBuffer, rotation, ~0, QueryTriggerInteraction.Ignore);

        for (int i = 0; i < count; i++)
        {
            Collider found = overlapBuffer[i];
            if (found == null)
            {
                continue;
            }

            if (ShouldIgnoreHit(found.transform))
            {
                continue;
            }

            return true;
        }

        return false;
    }

    /// <summary>
    /// プレビュー中の物の大きさを測って覚えておく。
    /// 回転していない状態で測るので、あとでどの向きに傾けても使える。
    /// </summary>
    private void CachePreviewSize()
    {
        previewHalfExtents = Vector3.zero;
        previewCenterOffset = Vector3.zero;

        Renderer[] renderers = previewObject.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0)
        {
            return;
        }

        Transform target = previewObject.transform;
        Quaternion savedRotation = target.rotation;
        target.rotation = Quaternion.identity;

        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
        {
            bounds.Encapsulate(renderers[i].bounds);
        }

        previewHalfExtents = bounds.extents;
        previewCenterOffset = bounds.center - target.position;

        target.rotation = savedRotation;
    }

    /// <summary>
    /// 置こうとしている先に壁や物があれば、**その手前で止まる距離**を返す。
    ///
    /// 何も無ければ、指定された距離をそのまま返す。
    /// 物の大きさ（傾きを含めた奥行き）を引いているので、壁にめり込まない。
    /// </summary>
    private float ClampDistanceByWall(float desiredDistance, float originOffset, Quaternion rotation)
    {
        Transform camera = aimCamera.transform;
        Ray ray = new Ray(camera.position, camera.forward);

        int count = Physics.RaycastNonAlloc(
            ray, hitBuffer, desiredDistance, ~0, QueryTriggerInteraction.Ignore);

        float nearest = float.MaxValue;
        bool found = false;

        for (int i = 0; i < count; i++)
        {
            RaycastHit hit = hitBuffer[i];

            // 自分より手前（カメラと自分の間）にある物は、背後にあるので無視する
            if (hit.distance < originOffset)
            {
                continue;
            }

            if (ShouldIgnoreHit(hit.collider.transform))
            {
                continue;
            }

            if (hit.distance < nearest)
            {
                nearest = hit.distance;
                found = true;
            }
        }

        if (!found)
        {
            return desiredDistance;
        }

        // 壁に当たった位置から、物の奥行きの半分だけ手前に下げる
        float halfDepth = GetExtentAlong(camera.forward, rotation);
        float limited = nearest - halfDepth - wallMargin;

        // 壁が近すぎても、自分の後ろには置かない
        return Mathf.Clamp(limited, originOffset + 0.1f, desiredDistance);
    }

    /// <summary>
    /// その向きに傾けたとき、物が指定した方向にどれだけの厚みを持つかを返す。
    /// 壁の手前で止める位置を決めるのに使う。
    /// </summary>
    private float GetExtentAlong(Vector3 direction, Quaternion rotation)
    {
        Vector3 half = previewHalfExtents;

        return Mathf.Abs(Vector3.Dot(direction, rotation * Vector3.right)) * half.x
             + Mathf.Abs(Vector3.Dot(direction, rotation * Vector3.up)) * half.y
             + Mathf.Abs(Vector3.Dot(direction, rotation * Vector3.forward)) * half.z;
    }

    /// <summary>
    /// 置く前に、キーで物を回す。
    /// Z/X/C でそれぞれ X軸・Y軸・Z軸まわりに回り、Shiftを押しながらだと逆に回る。
    /// T で回した分がまっすぐに戻る。
    ///
    /// ※ いまのコードに Shift の判定は無い（2026/10/20 確認）。逆に回したいときは、マウスを逆向きに動かす。
    /// </summary>
    private void UpdateManualRotation(Transform camera)
    {
        if (WasKeyPressed(resetRotationKey))
        {
            // XYZすべて0の、地面と平行なまっすぐの向きに戻す
            manualRotation = Quaternion.identity;
            currentBaseRotation = Quaternion.identity;

            // 視点追従のままだと、次の瞬間に視点の向きへ戻ってしまうので切る
            FollowView = false;
        }

        // 視点追従がONの間だけ、視点の動きに向きが付いてくる
        if (FollowView)
        {
            currentBaseRotation = camera.rotation;
        }

        Mouse mouse = Mouse.current;

        rotatingX = IsKeyHeld(rotateXKey);
        rotatingY = IsKeyHeld(rotateYKey);
        rotatingZ = IsKeyHeld(rotateZKey);
        freeRotating = useMiddleButtonFreeRotate && mouse != null && mouse.middleButton.isPressed;

        bool isRotating = rotatingX || rotatingY || rotatingZ || freeRotating;

        // 回している間は視点を止める。止めないと、物と視点が一緒に回ってしまう
        SetLookSuspended(isRotating);

        if (!isRotating || mouse == null)
        {
            return;
        }

        Vector2 move = mouse.delta.ReadValue() * rotationSensitivity;

        if (freeRotating)
        {
            // 見ている向きを基準に、つかんで転がすように回す。
            // カメラの軸で回すので、世界のXYZ 3軸すべてが同時に変わる
            AddManualRotation(camera.right, -move.y);
            AddManualRotation(camera.up, move.x);
            return;
        }

        // 上下に倒すのは縦の動き、向きを変える・横に倒すのは横の動きに合わせている
        if (rotatingX)
        {
            AddManualRotation(Vector3.right, -move.y);
        }

        if (rotatingY)
        {
            AddManualRotation(Vector3.up, move.x);
        }

        if (rotatingZ)
        {
            AddManualRotation(Vector3.forward, -move.x);
        }
    }

    /// <summary>回る向きを示す輪を、いまの状態に合わせて出し入れする。</summary>
    private void UpdateGizmo()
    {
        if (rotationGizmo == null)
        {
            return;
        }

        if (previewObject == null)
        {
            rotationGizmo.SetVisible(false, false, false);
            return;
        }

        // 自由回転のときは3軸すべてを出す
        bool showX = rotatingX || freeRotating;
        bool showY = rotatingY || freeRotating;
        bool showZ = rotatingZ || freeRotating;

        rotationGizmo.SetVisible(showX, showY, showZ);

        if (!showX && !showY && !showZ)
        {
            return;
        }

        Quaternion rotation = previewObject.transform.rotation;
        Vector3 center = previewObject.transform.position + rotation * previewCenterOffset;

        // 世界の軸で回す設定なら輪も世界の軸に合わせ、物基準なら物と一緒に傾ける
        Quaternion gizmoRotation = rotateInWorldSpace ? Quaternion.identity : rotation;

        // 物より少し大きめ（1.2倍）の輪にして、中に埋もれないようにする。小さな物でも最低 0.3m は確保する
        float radius = Mathf.Max(0.3f, previewHalfExtents.magnitude * 1.2f);

        rotationGizmo.Place(center, gizmoRotation, radius);
    }

    /// <summary>自分で回した分に、<paramref name="axis"/> まわりの回転を <paramref name="angle"/> 度ぶん足す。</summary>
    private void AddManualRotation(Vector3 axis, float angle)
    {
        if (Mathf.Approximately(angle, 0f))
        {
            return;
        }

        Quaternion delta = Quaternion.AngleAxis(angle, axis);

        // 世界の軸で回すか、物自身の軸で回すかで、掛ける順番が変わる
        manualRotation = rotateInWorldSpace ? delta * manualRotation : manualRotation * delta;
    }

    /// <summary>回している間、視点が動かないようにする。</summary>
    private void SetLookSuspended(bool suspended)
    {
        if (playerController != null)
        {
            playerController.LookSuspended = suspended;
        }
    }

    /// <summary>最終的に置く向きを返す。基準の向きに、自分で回した分を足したもの。</summary>
    private Quaternion GetPlacementRotation(Transform camera)
    {
        Quaternion baseRotation = currentBaseRotation;
        Quaternion manual = manualRotation;

        // 刻みが設定されていれば、その角度単位で止める
        if (rotationSnap > 0f)
        {
            Vector3 angles = manual.eulerAngles;
            angles.x = Mathf.Round(angles.x / rotationSnap) * rotationSnap;
            angles.y = Mathf.Round(angles.y / rotationSnap) * rotationSnap;
            angles.z = Mathf.Round(angles.z / rotationSnap) * rotationSnap;
            manual = Quaternion.Euler(angles);
        }

        return rotateInWorldSpace
            ? manual * baseRotation
            : baseRotation * manual;
    }

    /// <summary>マウスホイールで、置く距離を前後に動かす。</summary>
    private void UpdatePlaceDistance()
    {
        Mouse mouse = Mouse.current;
        if (mouse == null)
        {
            return;
        }

        float scroll = mouse.scroll.ReadValue().y;
        if (Mathf.Approximately(scroll, 0f))
        {
            return;
        }

        currentPlaceDistance = Mathf.Clamp(
            currentPlaceDistance + Mathf.Sign(scroll) * distanceStep,
            minPlaceDistance,
            maxPlaceDistance);
    }

    /// <summary>左クリックで確定。物理と色を元に戻し、置いた物の列に入れる。</summary>
    private void ConfirmPlacement()
    {
        // 空中に置けるようにするため、既定では落ちないまま固定する。
        // keepFloatingAfterPlace を OFF にすると、置いた瞬間に落ち始める
        // （※ インスペクターの初期値は OFF（false）なので、何も変えていなければ落ちる。2026/10/20 確認）
        SetPhysicsEnabled(previewObject, true, keepFloatingAfterPlace);
        RestoreOriginalColors();

        previewObject.tag = PlacedCloneTag;
        placedObjects.Enqueue(previewObject);

        previewObject = null;

        // 回している途中で置いた場合に、視点が止まったままにならないようにする
        SetLookSuspended(false);
        HideGizmo();

        UpdateOldestWarning();
    }

    /// <summary>Escape でやめる。プレビューを消して、何も無かったことにする。</summary>
    private void CancelPreview()
    {
        Destroy(previewObject);
        previewObject = null;
        previewRenderers.Clear();

        // 止めたままにすると視点が動かせなくなる
        SetLookSuspended(false);
        HideGizmo();
    }

    /// <summary>回る向きを示す輪を消す。</summary>
    private void HideGizmo()
    {
        rotatingX = false;
        rotatingY = false;
        rotatingZ = false;
        freeRotating = false;

        if (rotationGizmo != null)
        {
            rotationGizmo.SetVisible(false, false, false);
        }
    }

    /// <summary>プレビュー用に変えた色を元に戻す。</summary>
    private void RestoreOriginalColors()
    {
        RestoreColors(previewRenderers, previewOriginalColors);
    }

    // ------------------------------------------------------------
    // 3. 置ける数の制限と、古いものの自動削除
    // ------------------------------------------------------------

    /// <summary>
    /// 上限を超えていたら、一番古いものを点滅させてから消す。
    /// いきなり消えると「なぜ消えたか」が分からないため、予告してから消す。
    /// </summary>
    private void UpdateOldestWarning()
    {
        ClearWarning();

        if (placedObjects.Count <= maxPlacedObjects)
        {
            return;
        }

        warningObject = placedObjects.Peek();

        if (warningObject == null)
        {
            RemoveOldest();
            return;
        }

        warningTimer = warningSeconds;
        CaptureColors(warningObject, warningRenderers, warningOriginalColors);
    }

    /// <summary>消える予告の点滅を進め、時間が来たら一番古いものを消す。予告中でなければ何もしない。</summary>
    private void UpdateWarningBlink()
    {
        if (warningObject == null)
        {
            return;
        }

        warningTimer -= Time.deltaTime;

        // 点滅させる。0.08秒（WarningBlinkInterval）ごとに色を入れ替える
        bool highlight = Mathf.FloorToInt(warningTimer / WarningBlinkInterval) % 2 == 0;
        if (highlight)
        {
            Tint(warningRenderers, warningColor);
        }
        else
        {
            ApplyStoredColors(warningRenderers, warningOriginalColors);
        }

        if (warningTimer <= 0f)
        {
            RemoveOldest();
        }
    }

    /// <summary>一番古い（列の先頭の）物を消す。</summary>
    private void RemoveOldest()
    {
        ClearWarning();

        if (placedObjects.Count == 0)
        {
            return;
        }

        GameObject oldest = placedObjects.Dequeue();
        if (oldest != null)
        {
            Destroy(oldest);
        }
    }

    /// <summary>消える予告をやめて、色を元に戻す。</summary>
    private void ClearWarning()
    {
        RestoreColors(warningRenderers, warningOriginalColors);
        warningObject = null;
        warningTimer = 0f;
    }

    // ------------------------------------------------------------
    // 4. 手で消す
    // ------------------------------------------------------------

    /// <summary>R で、狙っている物が自分の置いた複製（PlacedClone タグ）なら消す。複製元や他の物は消さない。</summary>
    private void TryRemoveAimedClone()
    {
        if (!TryAim(pickDistance, out RaycastHit hit))
        {
            return;
        }

        if (!hit.collider.CompareTag(PlacedCloneTag))
        {
            return;
        }

        GameObject target = hit.collider.gameObject;

        // 消す相手を強調している場合があるので、先に解除しておく
        if (highlightedObject == target)
        {
            ClearHighlight();
        }

        RemoveFromQueue(target);
        Destroy(target);
    }

    /// <summary>
    /// 順番待ちの列から、指定のものだけを抜く。
    /// Queue は途中から抜けないので、作り直している
    /// （placedObjects は readonly で入れ替えられないため、一度空にして詰め直す）。
    /// ついでに、既に消えている（null の）ものも取り除く。
    /// </summary>
    private void RemoveFromQueue(GameObject target)
    {
        if (warningObject == target)
        {
            ClearWarning();
        }

        Queue<GameObject> rebuilt = new Queue<GameObject>();
        foreach (GameObject placed in placedObjects)
        {
            if (placed != null && placed != target)
            {
                rebuilt.Enqueue(placed);
            }
        }

        placedObjects.Clear();
        foreach (GameObject placed in rebuilt)
        {
            placedObjects.Enqueue(placed);
        }
    }

    // ------------------------------------------------------------
    // 共通の処理
    // ------------------------------------------------------------

    /// <summary>
    /// 当たった物が、**自分の体かプレビュー中の物**なら true。
    /// 狙い・壁・重なりのどの判定でも、この2つは無いものとして扱う
    /// （自分に当たって進まなくなる・プレビュー自身を壁とみなす、を防ぐ）。
    /// </summary>
    private bool ShouldIgnoreHit(Transform hitTransform)
    {
        if (playerRoot != null && hitTransform.IsChildOf(playerRoot))
        {
            return true;
        }

        return previewObject != null && hitTransform.IsChildOf(previewObject.transform);
    }

    /// <summary>
    /// 画面の中央から狙いの線を飛ばす。
    /// 自分自身とプレビュー中のものは無視する（自分に当たって進まなくなるのを防ぐ）。
    /// </summary>
    private bool TryAim(float distance, out RaycastHit result)
    {
        Ray ray = new Ray(aimCamera.transform.position, aimCamera.transform.forward);

        // TPSではカメラが後ろに下がっているので、その分を足してから距離を測る。
        // こうしないと、TPSのときだけ狙える距離が短くなる
        float originOffset = GetAimOriginOffset();
        int count = Physics.RaycastNonAlloc(
            ray, hitBuffer, originOffset + distance, ~0, QueryTriggerInteraction.Ignore);

        bool found = false;
        result = default;
        float nearest = float.MaxValue;

        for (int i = 0; i < count; i++)
        {
            RaycastHit hit = hitBuffer[i];

            // 自分より手前（カメラと自分の間）にあるものは、背後にあるので狙いの対象にしない
            if (hit.distance < originOffset)
            {
                continue;
            }

            if (ShouldIgnoreHit(hit.collider.transform))
            {
                continue;
            }

            if (hit.distance < nearest)
            {
                nearest = hit.distance;
                result = hit;
                found = true;
            }
        }

        return found;
    }

    /// <summary>
    /// 物理（落下と当たり判定）を入れたり切ったりする。
    /// keepFloating を true にすると、当たり判定は働くが**落ちない**状態になる。
    /// 空中に置いた足場をその場に留めるために使う。
    /// </summary>
    private void SetPhysicsEnabled(GameObject target, bool isEnabled, bool keepFloating = false)
    {
        foreach (Rigidbody body in target.GetComponentsInChildren<Rigidbody>(true))
        {
            body.isKinematic = !isEnabled || keepFloating;

            if (isEnabled && !keepFloating)
            {
                // 置いた瞬間に前の勢いが残らないようにする
                body.linearVelocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
            }
        }

        foreach (Collider collider in target.GetComponentsInChildren<Collider>(true))
        {
            collider.enabled = isEnabled;
        }
    }

    /// <summary>
    /// 対象の見た目をすべて集めて、いまの色を控える。
    /// 色を変える前に必ずこれを呼ぶこと。控えておかないと元の色に戻せなくなる。
    /// </summary>
    private static void CaptureColors(GameObject target, List<Renderer> renderers, List<Color> colors)
    {
        renderers.Clear();
        colors.Clear();

        if (target == null)
        {
            return;
        }

        target.GetComponentsInChildren(true, renderers);

        foreach (Renderer renderer in renderers)
        {
            colors.Add(renderer.sharedMaterial != null ? renderer.sharedMaterial.color : Color.white);
        }
    }

    /// <summary>控えておいた色に戻して、控えを空にする。</summary>
    private static void RestoreColors(List<Renderer> renderers, List<Color> colors)
    {
        ApplyStoredColors(renderers, colors);
        renderers.Clear();
        colors.Clear();
    }

    /// <summary>控えておいた色に戻す（控えは残す。点滅で往復させるため）。</summary>
    private static void ApplyStoredColors(List<Renderer> renderers, List<Color> colors)
    {
        for (int i = 0; i < renderers.Count && i < colors.Count; i++)
        {
            if (renderers[i] != null)
            {
                renderers[i].material.color = colors[i];
            }
        }
    }

    /// <summary>まとめて色を塗る。</summary>
    private static void Tint(List<Renderer> renderers, Color color)
    {
        foreach (Renderer renderer in renderers)
        {
            if (renderer != null)
            {
                // material を触ると、そのオブジェクト専用のマテリアルが作られる。
                // 元のマテリアル（他のオブジェクトと共用）は変わらない
                renderer.material.color = color;
            }
        }
    }

    /// <summary>そのキーを、このフレームに押したか。キーボードが無ければ false。</summary>
    private static bool WasKeyPressed(Key key)
    {
        Keyboard keyboard = Keyboard.current;
        return keyboard != null && keyboard[key].wasPressedThisFrame;
    }

    /// <summary>そのキーが今押されているか（押しっぱなしでも true）。</summary>
    private static bool IsKeyHeld(Key key)
    {
        Keyboard keyboard = Keyboard.current;
        return keyboard != null && keyboard[key].isPressed;
    }
}
