using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// **プレイヤー（本実装）の移動と向き。**（2026/10/27・小野田さんの仕様）
///
/// ・左スティック／WASD で動く。**画面の上下左右に合わせる**（カメラが水平に回っていたら、そのぶん入力も回す）
/// ・体は**移動している方向へ向き直す**
/// ・狙っている間（<see cref="FaceAim"/> が ON）は、**狙う方向**を向く
/// ・光線を撃ってから投げ終わるまでは、<see cref="SweeperShooter"/> が <see cref="MoveLocked"/> を ON にして止める
///
/// 釣り・宇宙ごみのプレイヤー（<see cref="FishingPlayerController"/>）とは別の部品。あちらは変えていない。
///
/// **同じ体に <see cref="FishingPlayerController"/> が付いているとき**（宇宙ごみのオンラインの体 `SpaceJunkSweeperPlayer.prefab`。2026/10/28）は、
/// 重力と吹き飛ばし（ベルトコンベア・爆弾など）はあちらに任せ、ここは**歩くことと向きだけ**を受け持つ（重力が二重にかからないように）。
/// </summary>
[DefaultExecutionOrder(-20)]
[RequireComponent(typeof(CharacterController))]
public class SweeperMover : MonoBehaviour
{
    [Header("移動")]
    [Tooltip("歩く速さ（メートル／秒）")]
    [Min(0f)]
    [SerializeField] private float moveSpeed = 6f;

    [Tooltip("重力（下向きの加速度。マイナスの値）")]
    [SerializeField] private float gravity = -20f;

    [Header("向き")]
    [Tooltip("向き直る速さ（度／秒）。大きいほどすぐ向く")]
    [Min(0f)]
    [SerializeField] private float turnSpeed = 900f;

    [Header("参照")]
    [Tooltip("狙う向き。空なら同じ体から探す")]
    [SerializeField] private SweeperAim aim;

    [Tooltip("Assets/InputSystem_Actions を入れる")]
    [SerializeField] private InputActionAsset inputActions;

    private CharacterController characterController;
    private PlayerStun stun;
    private PlayerInputSource input;
    private InputActionMap playerMap;
    private InputAction moveAction;

    /// <summary>重力と吹き飛ばしを任せる相手（宇宙ごみのオンラインの体）。いなければ null で、重力もここでかける。</summary>
    private FishingPlayerController sharedBody;

    /// <summary>上下方向の速さ（メートル／秒）。</summary>
    private float verticalVelocity;

    /// <summary>ON の間は横に動かない（向きを変えることと、落ちることはできる）。</summary>
    public bool MoveLocked { get; set; }

    /// <summary>ON の間は、移動方向ではなく狙う方向（<see cref="SweeperAim.Direction"/>）を向く。</summary>
    public bool FaceAim { get; set; }

    /// <summary>歩く速さ（メートル／秒）。</summary>
    public float MoveSpeed
    {
        get => moveSpeed;
        set => moveSpeed = Mathf.Max(0f, value);
    }

    /// <summary>いまの横の速さ（メートル／秒）。アニメーションの切り替えに使う。</summary>
    public float CurrentSpeed { get; private set; }

    /// <summary>いま向いている方向（水平）。</summary>
    public Vector3 Facing
    {
        get
        {
            Vector3 forward = transform.forward;
            forward.y = 0f;
            return forward.sqrMagnitude > 0.0001f ? forward.normalized : Vector3.forward;
        }
    }

    private void Awake()
    {
        characterController = GetComponent<CharacterController>();
        stun = GetComponent<PlayerStun>();
        input = PlayerInputSource.Get(this);
        sharedBody = GetComponent<FishingPlayerController>();

        if (aim == null)
        {
            aim = GetComponent<SweeperAim>();
        }

        if (inputActions == null)
        {
            Debug.LogError($"{name}: 入力の設定（InputSystem_Actions）が入っていません。", this);
            enabled = false;
            return;
        }

        // 入力の設定は、この体専用の複製を使う（ほかの体が止めたときに、自分の入力まで止まらないように）
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

    private void OnEnable()
    {
        playerMap?.Enable();
    }

    private void OnDisable()
    {
        playerMap?.Disable();
        verticalVelocity = 0f;
        CurrentSpeed = 0f;
    }

    private void Update()
    {
        // 当たり判定が切られている間（ほかの人の体・始まりの演出で運ばれている間）は動かさない
        if (!characterController.enabled)
        {
            return;
        }

        Vector3 direction = Vector3.zero;

        bool canMove = !GamePause.BlocksInput && !MoveLocked && (stun == null || !stun.IsStunned);
        if (canMove)
        {
            Vector2 move = input.ReadMove(moveAction);
            direction = new Vector3(move.x, 0f, move.y);
            if (direction.sqrMagnitude > 1f)
            {
                direction.Normalize();
            }

            // 画面の上下左右に合わせる（カメラが北向きなら回す量は 0）
            Camera view = Camera.main;
            if (view != null)
            {
                direction = Quaternion.Euler(0f, view.transform.eulerAngles.y, 0f) * direction;
            }
        }

        if (sharedBody != null)
        {
            // 重力は FishingPlayerController がかける。ここでは少しだけ下へ押して、地面に着いているかが正しく分かるようにする
            // （このあとあちらが isGrounded を見るので、横にだけ動かすと「浮いている」と思われて重力がたまり続ける）
            verticalVelocity = -2f;
        }
        else
        {
            // 地面に着いているときは少し下向きに押し付けておく（段差で isGrounded がちらつかないように）
            if (characterController.isGrounded && verticalVelocity < 0f)
            {
                verticalVelocity = -2f;
            }
            verticalVelocity += gravity * Time.deltaTime;
        }

        Vector3 velocity = direction * moveSpeed;
        velocity.y = verticalVelocity;
        characterController.Move(velocity * Time.deltaTime);

        CurrentSpeed = direction.magnitude * moveSpeed;

        Turn(direction);
    }

    /// <summary>狙っている間は狙う方向へ、それ以外は動いている方向へ向き直る。止まっているときは今の向きのまま。</summary>
    private void Turn(Vector3 moveDirection)
    {
        Vector3 look = Vector3.zero;

        if (FaceAim && aim != null)
        {
            look = aim.Direction;
        }
        else if (moveDirection.sqrMagnitude > 0.0001f)
        {
            look = moveDirection;
        }

        look.y = 0f;
        if (look.sqrMagnitude < 0.0001f)
        {
            return;
        }

        Quaternion target = Quaternion.LookRotation(look.normalized, Vector3.up);
        transform.rotation = Quaternion.RotateTowards(transform.rotation, target, turnSpeed * Time.deltaTime);
    }
}
