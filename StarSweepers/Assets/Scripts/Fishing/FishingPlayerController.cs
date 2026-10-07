using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// **見下ろし視点のプレイヤー操作。**
/// WASD で地面を移動し、体はいつもマウスカーソルの方向を向く。
///
/// 向きの計算は <see cref="PlayerAimController"/> に任せ、ここは「動かす」だけに絞っている。
/// フックの発射などはこのスクリプトには入れない（<see cref="HookController"/> が担当）。
///
/// 使い方：CharacterController の付いた体に、これと PlayerAimController を付ける。
/// Input Actions に Assets/InputSystem_Actions を入れる。
/// </summary>
[RequireComponent(typeof(CharacterController))]
public class FishingPlayerController : MonoBehaviour, ILaunchable
{
    [Header("移動")]
    [Tooltip("歩く速さ（1秒あたりのメートル）")]
    [SerializeField] private float moveSpeed = 6f;

    [Tooltip("落ちる強さ。マイナスの値にすること")]
    [SerializeField] private float gravity = -20f;

    [Header("外から与えられた勢い")]
    [Tooltip("吹き飛ばされた横方向の勢いが弱まる速さ。大きいほど早く止まる")]
    [Min(0f)]
    [SerializeField] private float launchDamping = 4f;

    [Header("向き")]
    [Tooltip("マウス方向へ向き直る速さ（1秒あたりの度）。大きいほどキビキビ振り向く")]
    [SerializeField] private float turnSpeed = 900f;

    [Header("参照")]
    [Tooltip("同じ体に付いている PlayerAimController を入れる")]
    [SerializeField] private PlayerAimController aim;

    [Tooltip("Assets/InputSystem_Actions を入れる")]
    [SerializeField] private InputActionAsset inputActions;

    [Tooltip("スタン（動けない状態）の管理。スタン中は移動できなくなる")]
    [SerializeField] private PlayerStun stun;

    private CharacterController characterController;
    private InputActionMap playerMap;
    private InputAction moveAction;
    private PlayerInputSource input;
    private float verticalVelocity;
    private Vector3 launchVelocity;
    private bool anchorMovementActive;

    /// <summary>アンカー移動中は通常の入力移動と重力を止める。</summary>
    public void SetAnchorPulling(bool isPulling)
    {
        anchorMovementActive = isPulling;
        if (isPulling)
        {
            verticalVelocity = 0f;
            launchVelocity = Vector3.zero;
        }
    }

    private void Awake()
    {
        characterController = GetComponent<CharacterController>();

        if (aim == null)
        {
            aim = GetComponent<PlayerAimController>();
        }

        if (stun == null)
        {
            stun = GetComponent<PlayerStun>();
        }

        // 操作の読み口。単体操作か、1台のPCで複数人（席）かをここで切り替える
        input = PlayerInputSource.Get(this);

        if (inputActions == null)
        {
            Debug.LogError($"{name}: 入力の設定（InputSystem_Actions）が入っていません。", this);
            enabled = false;
            return;
        }

        // **入力の設定は、この体専用の複製を使う。**
        // 元のアセットをそのまま使うと、オンラインで「他の人のぶん」の操作を止めたとき
        // （OnDisable → Disable）に、**自分の入力まで一緒に止まって1人しか動かなくなる**
        inputActions = Instantiate(inputActions);

        playerMap = inputActions.FindActionMap("Player", true);
        moveAction = playerMap.FindAction("Move", true);
    }

    private void OnDestroy()
    {
        if (inputActions != null)
        {
            Destroy(inputActions);
        }
    }

    private void Start()
    {
        // 見下ろしでマウスを狙いに使うので、カーソルは固定せず表示したままにする。
        // （PlayerRig 由来の FPS 操作が万一残っていてカーソルをロックしても、ここで戻す）
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    private void OnEnable()
    {
        playerMap?.Enable();
    }

    private void OnDisable()
    {
        playerMap?.Disable();
        launchVelocity = Vector3.zero;
        verticalVelocity = 0f;
    }

    private void Update()
    {
        // ポーズ中は何もしない（既存の PlayerController と同じ作法）
        if (GamePause.BlocksInput)
        {
            return;
        }

        Move();
        FaceCursor();
    }

    /// <summary>WASD で水平移動し、重力で下に落とす。**スタン中は横に動かない。**</summary>
    private void Move()
    {
        Vector3 direction = Vector3.zero;

        // スタン中は移動禁止（向きを変えることと、落ちることはできる）。
        // 試合が終わったあと（結果画面）も動かせない
        if (!anchorMovementActive && (stun == null || !stun.IsStunned) && FishingMatch.PlayAllowed)
        {
            Gamepad pad = input.Gamepad;
            bool typeB = pad != null && GameSettings.ControllerOperation == ControllerOperationType.TypeB;
            Vector2 move = typeB ? pad.leftStick.ReadValue() : input.ReadMove(moveAction);

            // タイプBはLTを押している間、左スティックを向き調整だけに使う。
            if (typeB && pad.leftTrigger.isPressed)
            {
                move = Vector2.zero;
            }

            direction = new Vector3(move.x, 0f, move.y);
            if (direction.sqrMagnitude > 1f)
            {
                direction.Normalize();
            }

            // **画面の上下左右に合わせる。** カメラが水平に回っていたら、そのぶん入力も回す。
            //
            // 以前は「画面の上＝ワールドの北」と決め打ちしていた。釣りのカメラは常に北向きなので
            // それで合っていたが、宇宙ごみの「自陣が手前に来るカメラ」（SpaceJunkTeamFollowCamera）は
            // チームによって向きが回るので、決め打ちだと W で画面の下や横へ進んでしまう
            // （2026/9/22）。**カメラが北向きなら回す量は 0 なので、釣りの動きは変わらない。**
            Camera view = Camera.main;
            if (view != null)
            {
                direction = Quaternion.Euler(0f, view.transform.eulerAngles.y, 0f) * direction;
            }
        }

        if (anchorMovementActive)
        {
            verticalVelocity = 0f;
        }
        else if (characterController.isGrounded && verticalVelocity < 0f)
        {
            verticalVelocity = -2f;
        }
        if (!anchorMovementActive)
        {
            verticalVelocity += gravity * Time.deltaTime;
        }

        Vector3 velocity = anchorMovementActive ? Vector3.zero : direction * moveSpeed + launchVelocity;
        velocity.y = verticalVelocity;

        CollisionFlags collisions = characterController.Move(velocity * Time.deltaTime);
        if ((collisions & CollisionFlags.Above) != 0 && verticalVelocity > 0f)
        {
            verticalVelocity = 0f;
        }

        if (anchorMovementActive)
        {
            launchVelocity = Vector3.zero;
        }
        else
        {
            launchVelocity = Vector3.Lerp(launchVelocity, Vector3.zero,
                1f - Mathf.Exp(-launchDamping * Time.deltaTime));
        }
    }

    /// <summary>スタンせずに吹き飛ばす。オンラインでは自分の体だけが受け取る。</summary>
    public void Launch(Vector3 velocity)
    {
        if (anchorMovementActive || !isActiveAndEnabled || characterController == null || !characterController.enabled)
        {
            return;
        }

        launchVelocity = new Vector3(velocity.x, 0f, velocity.z);
        if (velocity.y > 0f)
        {
            verticalVelocity = velocity.y;
        }
    }

    /// <summary>体をマウスカーソルの方向へ向ける。</summary>
    private void FaceCursor()
    {
        if (aim == null || !aim.HasAim)
        {
            return;
        }

        Quaternion target = Quaternion.LookRotation(aim.AimDirection, Vector3.up);
        transform.rotation = Quaternion.RotateTowards(
            transform.rotation, target, turnSpeed * Time.deltaTime);
    }
}
