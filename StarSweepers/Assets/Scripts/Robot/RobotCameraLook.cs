using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// ロボットのカメラ。マウスで向きを変え、操作している体を追いかける。
///
/// **体の子にはしない。** 体の子にすると、体が動くたびにカメラが引っ張られて
/// 見た目がカクつく。代わりに、毎フレーム体の位置へ寄せていく形にしている。
///
/// **体そのものは回さない。** カメラだけが回り、体は進みたい方向へ自分で向き直る。
/// （「見ている方向を向く」設定（<see cref="RobotViewSwitcher"/>、初期値は ON）のときは、
/// 体がこのカメラの水平の向きに自分で揃う。その場合も、回しているのは体の側）
///
/// ## 作り
///
/// - **左右の向き**… この部品が付いた物（CameraRig）ごと回す
/// - **上下の向き**… その下の首（`pitchPivot`）だけを回す
///
/// こうしておくと、この部品の向きは**いつも水平**になり、移動の向き（<see cref="FlatForward"/>）がそのまま求まる。
///
/// 使っているシーン：`RobotRig` を置いたシーン（ロボット分離の検証シーンなど）。
/// </summary>
public class RobotCameraLook : MonoBehaviour
{
    [Header("つなぐもの")]
    [Tooltip("上下の首振りをさせる場所。この下にカメラを置く")]
    [SerializeField] private Transform pitchPivot;

    [Tooltip("Assets/InputSystem_Actions を入れる")]
    [SerializeField] private InputActionAsset inputActions;

    [Header("見え方")]
    [Tooltip("マウスを動かしたときに回る量")]
    [Range(0.02f, 1f)]
    [SerializeField] private float sensitivity = 0.12f;

    [Tooltip("上を向ける限界の角度")]
    [Range(0f, 89f)]
    [SerializeField] private float maxLookUp = 70f;

    [Tooltip("下を向ける限界の角度")]
    [Range(0f, 89f)]
    [SerializeField] private float maxLookDown = 70f;

    [Header("追いかけ方")]
    [Tooltip("追いかける相手の足元から、どれだけ上を見るか（メートル）")]
    [SerializeField] private float targetHeight = 1.4f;

    [Tooltip("追いつく速さ。大きいほどぴったり付いてくる。0にすると瞬時に付いてくる")]
    [Range(0f, 30f)]
    [SerializeField] private float followSmooth = 14f;

    [Header("マウスカーソル")]
    [Tooltip("再生したらカーソルを消して画面に固定する")]
    [SerializeField] private bool lockCursorOnStart = true;

    private InputActionMap playerMap;
    private InputAction lookAction;

    /// <summary>上下の向き（度）。マイナスが上向き、プラスが下向き（Unity の X 軸の回転と同じ向き）。</summary>
    private float pitch;

    private Transform followTarget;

    /// <summary>
    /// 左右の向き（水平方向）。移動の向きを決めるのに使う。
    /// 上下の傾きを含まないので、そのまま地面に沿った方向になる。
    /// </summary>
    public Vector3 FlatForward
    {
        get
        {
            Vector3 forward = transform.forward;
            forward.y = 0f;
            return forward.sqrMagnitude > 0.0001f ? forward.normalized : Vector3.forward;
        }
    }

    /// <summary>右方向（水平）。移動の向きを決めるのに使う。</summary>
    public Vector3 FlatRight
    {
        get
        {
            Vector3 right = transform.right;
            right.y = 0f;
            return right.sqrMagnitude > 0.0001f ? right.normalized : Vector3.right;
        }
    }

    private void Awake()
    {
        if (inputActions == null)
        {
            Debug.LogError($"{name}: 入力の設定（InputSystem_Actions）が入っていません。", this);
            enabled = false;
            return;
        }

        // **入力の設定は、このカメラ専用の複製を使う。**
        // 元のアセットをそのまま使うと、1つのシーンに複数の体が出たとき
        // （オンラインや画面分割）に、**他の体の操作を止めると自分の入力まで一緒に止まる**
        inputActions = Instantiate(inputActions);

        playerMap = inputActions.FindActionMap("Player", true);
        lookAction = playerMap.FindAction("Look", true);
    }

    private void OnDestroy()
    {
        if (inputActions != null)
        {
            Destroy(inputActions);
        }
    }

    private void OnEnable()
    {
        playerMap?.Enable();
    }

    private void OnDisable()
    {
        playerMap?.Disable();
    }

    private void Start()
    {
        if (lockCursorOnStart)
        {
            SetCursorLocked(true);
        }
    }

    /// <summary>
    /// 追いかける相手の足元から、どれだけ上を見るか（メートル）。
    ///
    /// **一人称と三人称で変える**ため、外から書き換えられるようにしてある。
    /// 一人称のときは、その体の目の高さに合わせる。
    /// </summary>
    public float TargetHeight
    {
        get => targetHeight;
        set => targetHeight = value;
    }

    /// <summary>追いかける相手を決める。切り替わっても位置は滑らかに移る。</summary>
    public void SetTarget(Transform target)
    {
        followTarget = target;
    }

    /// <summary>
    /// 体の位置へ寄せていく。
    /// 体を動かしたあとに動かしたいので、Update ではなく LateUpdate で行う。
    /// </summary>
    private void LateUpdate()
    {
        if (followTarget == null)
        {
            return;
        }

        Vector3 wanted = followTarget.position + Vector3.up * targetHeight;

        if (followSmooth <= 0f)
        {
            transform.position = wanted;
            return;
        }

        // 毎フレーム、残りの距離の一定の割合だけ近づける。
        // `1 - Exp(-速さ × 経過時間)` の形にすると、フレームの速さが違っても同じ速さで追いつく
        transform.position = Vector3.Lerp(
            transform.position, wanted, 1f - Mathf.Exp(-followSmooth * Time.deltaTime));
    }

    private void Update()
    {
        // **止まっている間は、視点もカーソルも触らない。**
        // ポーズ画面がカーソルを出しているので、ここで奪い返すと選べなくなる
        if (GamePause.BlocksInput)
        {
            return;
        }

        UpdateCursor();

        // 設定画面で変えた感度を掛ける（初期値は1なので、変えなければ今までどおり）
        Vector2 look = lookAction.ReadValue<Vector2>() * (sensitivity * GameSettings.MouseSensitivity);

        // 左右はこのオブジェクトごと回す（体は回さない）
        transform.Rotate(Vector3.up, look.x, Space.World);

        // 上下は首の部分だけ回す
        // （マウスを上へ動かすと look.y がプラスになり、pitch が減って上を向く）
        pitch = Mathf.Clamp(pitch - look.y, -maxLookUp, maxLookDown);
        if (pitchPivot != null)
        {
            pitchPivot.localRotation = Quaternion.Euler(pitch, 0f, 0f);
        }
    }

    /// <summary>
    /// Escape でカーソルを出し、画面をクリックすると再び固定する。
    ///
    /// **ポーズ画面がある場合、Escape はそちらのもの**になる
    /// （<see cref="GamePause"/> が true の間は、この処理自体を通らない）。
    /// ポーズ画面を置いていないシーンでは、これまでどおり Escape でカーソルが出る。
    /// </summary>
    private void UpdateCursor()
    {
        // **ポーズ画面があるときは、Escape はそちらのもの。**
        // 同じフレームで両方が動くと、閉じた直後にカーソルが出たままになる
        Keyboard keyboard = Keyboard.current;
        if (!PauseMenu.Exists && keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
        {
            SetCursorLocked(false);
        }

        Mouse mouse = Mouse.current;
        if (mouse != null && mouse.leftButton.wasPressedThisFrame && Cursor.lockState != CursorLockMode.Locked)
        {
            SetCursorLocked(true);
        }
    }

    /// <summary>カーソルを画面に固定して消す（true）か、自由に動かせるよう出す（false）。</summary>
    private static void SetCursorLocked(bool locked)
    {
        Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible = !locked;
    }
}
