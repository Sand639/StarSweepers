using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// **カートの運転。**
///
/// | 操作 | 結果 |
/// | --- | --- |
/// | **W / ↑** | アクセル |
/// | **S / ↓** | ブレーキ。止まっている間は**バック** |
/// | **A / D（← →）** | ハンドル |
///
/// **本物の車の物理は使っていない。** 前に進む速さと向きを直接いじる、
/// いわゆるアーケード操作にしてある。
///
/// ## なぜ物理演算（Rigidbody）を使わないか
///
/// このプロジェクトでは、**物理と手動の動かし方を混ぜるとガタつく**ことが分かっている
/// （`Documents/AIの申し送り.md`）。
/// また通信を入れたとき、**物理は機械ごとに答えがずれる**ので合わせるのが難しい。
///
/// <see cref="CharacterController"/> なら、壁で止まる・坂を登るはそのまま使えて、
/// **位置は自分で決められる。**
/// </summary>
[RequireComponent(typeof(CharacterController))]
public class KartController : MonoBehaviour
{
    [Header("速さ")]
    [Tooltip("前に進む最高速度（1秒あたりのメートル）")]
    [SerializeField] private float maxSpeed = 20f;

    [Tooltip("バックの最高速度")]
    [SerializeField] private float maxReverseSpeed = 6f;

    [Tooltip("アクセルを踏んでから速くなる勢い（1秒でどれだけ速くなるか）")]
    [SerializeField] private float acceleration = 14f;

    [Tooltip("ブレーキの効き")]
    [SerializeField] private float brakePower = 28f;

    [Tooltip("何も押していないときに減る勢い")]
    [SerializeField] private float coastDeceleration = 7f;

    [Header("ハンドル")]
    [Tooltip("1秒あたりに回れる角度")]
    [SerializeField] private float turnSpeed = 115f;

    [Tooltip("この速さまでは、ハンドルの効きが弱い（止まっていると曲がらない）")]
    [SerializeField] private float turnWarmupSpeed = 4f;

    [Tooltip("速く走っているほどハンドルを鈍くする量。0で常に同じ効き")]
    [Range(0f, 1f)]
    [SerializeField] private float highSpeedTurnDamp = 0.35f;

    [Header("その他")]
    [Tooltip("落ちる強さ。マイナスの値にすること")]
    [SerializeField] private float gravity = -25f;

    [Tooltip("壁にぶつかったときに残る速さの割合。0で完全に止まる")]
    [Range(0f, 1f)]
    [SerializeField] private float wallSpeedKeep = 0.3f;

    [Header("つなぐもの")]
    [Tooltip("Assets/InputSystem_Actions を入れる")]
    [SerializeField] private InputActionAsset inputActions;

    [Tooltip("**同じオブジェクトの RaceRacer。** カウントダウン中に動けないようにするために見ている")]
    [SerializeField] private RaceRacer racer;

    /// <summary>いまの速さ（1秒あたりのメートル）。マイナスはバック。</summary>
    public float Speed { get; private set; }

    /// <summary>いまの速さ（km/h）。画面に出すため。</summary>
    public float SpeedKmh => Speed * 3.6f;

    private CharacterController controller;
    private InputActionMap playerMap;
    private InputAction moveAction;

    /// <summary>上下の速さ（1秒あたりのメートル）。落ちている間はマイナス。前後の <see cref="Speed"/> とは別に持つ。</summary>
    private float verticalVelocity;

    private void Awake()
    {
        controller = GetComponent<CharacterController>();

        if (racer == null)
        {
            racer = GetComponent<RaceRacer>();
        }

        if (inputActions == null)
        {
            Debug.LogError($"{name}: 入力の設定（InputSystem_Actions）が入っていません。インスペクターで設定してください。", this);
            enabled = false;
            return;
        }

        // **入力の設定は、このカート専用の複製を使う。**
        // 元のアセットをそのまま使うと、1つのシーンに複数台が出たとき
        // （オンラインや画面分割）に、**他の台の操作を止めると自分の入力まで一緒に止まる**
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
    }

    private void Update()
    {
        if (GamePause.BlocksInput)
        {
            return;
        }

        // カウントダウン中とゴール後は、アクセルを踏んでも進まない
        bool canDrive = racer == null || racer.ControlEnabled;
        Vector2 input = canDrive ? moveAction.ReadValue<Vector2>() : Vector2.zero;

        UpdateSpeed(input.y);
        UpdateSteering(input.x);
        ApplyMove();
    }

    /// <summary>**その場でぴたりと止める。** やり直しのときに使う。</summary>
    public void StopImmediately()
    {
        Speed = 0f;
        verticalVelocity = 0f;
    }

    /// <summary>
    /// アクセル・ブレーキで前後の速さを変える。
    /// <paramref name="throttle"/> は -1（S）〜 +1（W）。
    /// </summary>
    private void UpdateSpeed(float throttle)
    {
        float delta = Time.deltaTime;

        // ±0.01 は入力の遊び。これより小さい入力は「何も押していない」扱い
        if (throttle > 0.01f)
        {
            Speed += acceleration * throttle * delta;
        }
        else if (throttle < -0.01f)
        {
            // 前に進んでいる間はブレーキ。止まったら（0.1 m/s 以下）バックに変わる
            float power = Speed > 0.1f ? brakePower : acceleration;
            Speed += power * throttle * delta;
        }
        else
        {
            Speed = Mathf.MoveTowards(Speed, 0f, coastDeceleration * delta);
        }

        Speed = Mathf.Clamp(Speed, -maxReverseSpeed, maxSpeed);
    }

    /// <summary>
    /// ハンドルで向きを変える。<paramref name="steer"/> は -1（A）〜 +1（D）。
    /// 曲がる量は「動き出してからの効き」と「速すぎるときの鈍り」を掛けて決める。
    /// </summary>
    private void UpdateSteering(float steer)
    {
        // 入力の遊び（±0.01）か、ほぼ止まっている（0.05 m/s 未満）ときは回さない
        if (Mathf.Abs(steer) < 0.01f || Mathf.Abs(Speed) < 0.05f)
        {
            return;
        }

        // 止まっていると曲がらない。動き出すと効き始める
        float warmup = Mathf.Clamp01(Mathf.Abs(Speed) / Mathf.Max(0.01f, turnWarmupSpeed));

        // 速いほど少し鈍くする（高速で急に曲がるとコマのように回るため）
        float fast = Mathf.Clamp01(Mathf.Abs(Speed) / Mathf.Max(0.01f, maxSpeed));
        float damp = Mathf.Lerp(1f, 1f - highSpeedTurnDamp, fast);

        // バックのときはハンドルの向きを逆にする（車と同じ）
        float direction = Mathf.Sign(Speed);

        float angle = steer * turnSpeed * warmup * damp * direction * Time.deltaTime;
        transform.Rotate(Vector3.up, angle, Space.World);
    }

    /// <summary>
    /// 決めた速さと向きで、実際に動かす。
    /// 上下は重力、前後は <see cref="Speed"/> で決め、壁や坂の処理は <see cref="CharacterController"/> に任せる。
    /// </summary>
    private void ApplyMove()
    {
        float delta = Time.deltaTime;

        if (controller.isGrounded && verticalVelocity < 0f)
        {
            // 地面に押し付けておかないと、坂で浮いてガタつく
            verticalVelocity = -2f;
        }
        else
        {
            // 落ちる速さは、いまの重力に合わせて変わる（<see cref="WorldGravity"/>）
            verticalVelocity += gravity * WorldGravity.Scale * delta;
        }

        Vector3 velocity = transform.forward * Speed;
        velocity.y = verticalVelocity;

        Vector3 before = transform.position;
        controller.Move(velocity * delta);

        // **壁に当たったか調べる。**
        // 進もうとした距離より、実際に進んだ距離がずっと短ければ当たっている
        Vector3 moved = transform.position - before;
        moved.y = 0f;

        float wanted = Mathf.Abs(Speed) * delta;

        // 進めたのが狙いの半分未満なら「ぶつかった」とみなす（狙いがほぼ 0 のときは判定しない）
        if (wanted > 0.0001f && moved.magnitude < wanted * 0.5f)
        {
            Speed *= wallSpeedKeep;
        }
    }
}
