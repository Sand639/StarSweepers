using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// **検証用のプレイヤー（PlayerRig）の移動と視点。** カプセルのキャラクターを WASD で動かし、マウスで視点を回す。
/// TPS / FPS の切り替えは <see cref="PlayerViewSwitcher"/> が担当する。
///
/// 使い方：`Prefabs/Common/PlayerRig` をシーンに置くだけ。
/// 参照はすべてインスペクターから受け取るので、他のオブジェクトを名前で探しにいかない。
///
/// 本番のステージ（釣り・スペースジャンク）では、これの代わりに <c>FishingPlayerController</c> が同じ役目をしている。
/// </summary>
[RequireComponent(typeof(CharacterController))]
public class PlayerController : MonoBehaviour
{
    [Header("移動")]
    [Tooltip("歩く速さ（1秒あたりのメートル）")]
    [SerializeField] private float walkSpeed = 4f;

    [Tooltip("ダッシュ中の速さ（1秒あたりのメートル）")]
    [SerializeField] private float sprintSpeed = 7f;

    [Tooltip("ジャンプで上がる高さ（メートル）")]
    [SerializeField] private float jumpHeight = 1.2f;

    [Tooltip("落ちる強さ。マイナスの値にすること")]
    [SerializeField] private float gravity = -20f;

    [Header("視点")]
    [Tooltip("マウスを動かしたときに回る量。大きいほど速く回る")]
    [SerializeField] private float mouseSensitivity = 0.12f;

    [Tooltip("上を向ける限界の角度")]
    [SerializeField] private float maxLookUp = 80f;

    [Tooltip("下を向ける限界の角度")]
    [SerializeField] private float maxLookDown = 80f;

    [Tooltip("上下の首振りをさせる場所。PlayerRig の中の CameraPivot を入れる")]
    [SerializeField] private Transform cameraPivot;

    [Header("入力")]
    [Tooltip("Assets/InputSystem_Actions を入れる")]
    [SerializeField] private InputActionAsset inputActions;

    [Header("マウスカーソル")]
    [Tooltip("再生したらカーソルを消して画面に固定する")]
    [SerializeField] private bool lockCursorOnStart = true;

    private CharacterController characterController;
    private InputActionMap playerMap;
    private InputAction moveAction;
    private InputAction lookAction;
    private InputAction jumpAction;
    private InputAction sprintAction;

    /// <summary>上下の首振り角度。カメラ側から見たいときのために公開している。</summary>
    public float Pitch { get; private set; }

    /// <summary>
    /// true の間、マウスで視点が動かなくなる。
    /// 「マウスで物を回す」ような操作をしたい機能から、一時的に止めるために使う。
    /// </summary>
    public bool LookSuspended { get; set; }

    /// <summary>上下の速さ（1秒あたりのメートル）。マイナスで落ちている。ジャンプで一気にプラスになる。</summary>
    private float verticalVelocity;

    private void Awake()
    {
        characterController = GetComponent<CharacterController>();

        if (inputActions == null)
        {
            Debug.LogError($"{name}: 入力の設定（InputSystem_Actions）が入っていません。インスペクターで設定してください。", this);
            enabled = false;
            return;
        }

        // **入力の設定は、この体専用の複製を使う。**
        // 元のアセットをそのまま使うと、1つのシーンに複数の体が出たとき
        // （オンラインや画面分割）に、**他の体の操作を止めると自分の入力まで一緒に止まる**
        inputActions = Instantiate(inputActions);

        playerMap = inputActions.FindActionMap("Player", true);
        moveAction = playerMap.FindAction("Move", true);
        lookAction = playerMap.FindAction("Look", true);
        jumpAction = playerMap.FindAction("Jump", true);
        sprintAction = playerMap.FindAction("Sprint", true);

        if (cameraPivot == null)
        {
            Debug.LogError($"{name}: CameraPivot が入っていません。上下の首振りができません。", this);
        }
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

    private void Update()
    {
        // **止まっている間は、視点もカーソルも触らない。**
        // ポーズ画面がカーソルを出しているので、ここで奪い返すと選べなくなる
        if (GamePause.BlocksInput)
        {
            return;
        }

        UpdateCursor();

        // 他の機能がマウスを使っている間は、視点を動かさない
        if (!LookSuspended)
        {
            Look();
        }

        Move();
    }

    /// <summary>マウスの動きで、体を左右に・カメラを上下に回す。</summary>
    private void Look()
    {
        // 設定画面で変えた感度を掛ける（初期値は1なので、変えなければ今までどおり）
        Vector2 look = lookAction.ReadValue<Vector2>() * (mouseSensitivity * GameSettings.MouseSensitivity);

        // 左右は体ごと回す
        transform.Rotate(Vector3.up, look.x, Space.World);

        // 上下はカメラの根元だけ回す（体は傾けない）。
        // マウスを上へ動かすと look.y がプラスになるので、引き算で角度をマイナス（＝上向き）にする
        Pitch = Mathf.Clamp(Pitch - look.y, -maxLookUp, maxLookDown);
        if (cameraPivot != null)
        {
            cameraPivot.localRotation = Quaternion.Euler(Pitch, 0f, 0f);
        }
    }

    /// <summary>WASD で移動し、重力とジャンプを処理する。</summary>
    private void Move()
    {
        Vector2 input = moveAction.ReadValue<Vector2>();
        Vector3 direction = transform.right * input.x + transform.forward * input.y;

        // 斜め移動が速くなりすぎないようにする
        if (direction.sqrMagnitude > 1f)
        {
            direction.Normalize();
        }

        float speed = sprintAction.IsPressed() ? sprintSpeed : walkSpeed;

        if (characterController.isGrounded)
        {
            // 地面に押し付けておかないと、坂で浮いてガタつく
            verticalVelocity = -2f;

            if (jumpAction.WasPressedThisFrame())
            {
                // 踏み切りの強さは、重力が変わっても変えない（弱いほど高く跳ぶ）
                verticalVelocity = Mathf.Sqrt(jumpHeight * -2f * gravity);
            }
        }
        else
        {
            // 落ちる速さは、いまの重力に合わせて変わる（<see cref="WorldGravity"/>）
            verticalVelocity += gravity * WorldGravity.Scale * Time.deltaTime;
        }

        Vector3 velocity = direction * speed;
        velocity.y = verticalVelocity;

        characterController.Move(velocity * Time.deltaTime);
    }

    /// <summary>Escape でカーソルを出し、画面をクリックすると再び固定する。</summary>
    private void UpdateCursor()
    {
        // **ポーズ画面があるときは、Escape はそちらのもの。**
        // 同じフレームで両方が動くと、閉じた直後にカーソルが出たままになる
        var keyboard = Keyboard.current;
        if (!PauseMenu.Exists && keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
        {
            SetCursorLocked(false);
        }

        var mouse = Mouse.current;
        if (mouse != null && mouse.leftButton.wasPressedThisFrame && Cursor.lockState != CursorLockMode.Locked)
        {
            SetCursorLocked(true);
        }
    }

    /// <summary>カーソルを画面の中央に固定して消す（true）か、出して自由に動かせるようにする（false）か。</summary>
    private void SetCursorLocked(bool locked)
    {
        Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible = !locked;
    }
}
