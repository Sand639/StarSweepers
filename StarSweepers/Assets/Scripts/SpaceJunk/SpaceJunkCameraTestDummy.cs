using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// **カメラの検証シーン用の、キーで動かせる人形。**（2026/10/7・小野田さん）
///
/// 本物のプレイヤー（通信が必要）の代わりに、1台のPCで A と B を別々に動かして、
/// 「A が止まっているのに B が動いたとき、A のカメラがどう動くか」を確かめるために使う。
/// 移動は本物と同じく**カメラの向きを基準**にする（W＝画面の奥）。
/// **本番のステージには置かないこと。**
/// </summary>
public class SpaceJunkCameraTestDummy : MonoBehaviour
{
    /// <summary>動かし方。</summary>
    public enum Control
    {
        /// <summary>WASD（Space でジャンプ）</summary>
        Wasd = 0,

        /// <summary>矢印キー（右 Shift でジャンプ）</summary>
        Arrows = 1,

        /// <summary>動かない</summary>
        Still = 2,
    }

    [Tooltip("動かし方")]
    [SerializeField] private Control control = Control.Wasd;

    [Tooltip("ON にすると、キーを押していない間、勝手に歩き回る（味方だけが動く場面を作るため）")]
    [SerializeField] private bool wander;

    [Tooltip("歩く速さ（メートル／秒）")]
    [SerializeField] private float speed = 6f;

    [Tooltip("ジャンプの高さ（メートル）")]
    [SerializeField] private float jumpHeight = 2f;

    [Tooltip("勝手に歩くときに、歩き回る範囲の半径（メートル。最初の位置から）")]
    [SerializeField] private float wanderRadius = 12f;

    [Tooltip("歩ける範囲の半径（メートル。原点から）。床から落ちないように")]
    [SerializeField] private float areaRadius = 38f;

    private Vector3 home;
    private Vector3 wanderGoal;
    private float wanderWait;
    private float height;
    private float verticalSpeed;

    private void Start()
    {
        home = transform.position;
        wanderGoal = home;
    }

    private void Update()
    {
        Vector2 input = ReadInput(out bool jump);
        Vector3 move;

        if (input.sqrMagnitude > 0.01f)
        {
            // 画面の向きに合わせる（本物の FishingPlayerController と同じ）
            Camera view = Camera.main;
            float yaw = view != null ? view.transform.eulerAngles.y : 0f;
            move = Quaternion.Euler(0f, yaw, 0f) * new Vector3(input.x, 0f, input.y).normalized;
        }
        else if (wander)
        {
            move = Wander();
        }
        else
        {
            move = Vector3.zero;
        }

        Vector3 p = transform.position + move * (speed * Time.deltaTime);
        Vector2 flat = Vector2.ClampMagnitude(new Vector2(p.x, p.z), areaRadius);

        // ジャンプ（地面の高さは最初の高さ）
        if (jump && height <= 0f)
        {
            verticalSpeed = Mathf.Sqrt(2f * 9.81f * jumpHeight);
        }
        verticalSpeed -= 9.81f * Time.deltaTime;
        height = Mathf.Max(0f, height + verticalSpeed * Time.deltaTime);
        if (height <= 0f)
        {
            verticalSpeed = 0f;
        }

        transform.position = new Vector3(flat.x, home.y + height, flat.y);

        if (move.sqrMagnitude > 0.01f)
        {
            transform.rotation = Quaternion.LookRotation(move);
        }
    }

    private Vector2 ReadInput(out bool jump)
    {
        jump = false;
        Keyboard k = Keyboard.current;
        if (k == null)
        {
            return Vector2.zero;
        }

        Vector2 input = Vector2.zero;

        switch (control)
        {
            case Control.Wasd:
                if (k.wKey.isPressed) input.y += 1f;
                if (k.sKey.isPressed) input.y -= 1f;
                if (k.dKey.isPressed) input.x += 1f;
                if (k.aKey.isPressed) input.x -= 1f;
                jump = k.spaceKey.wasPressedThisFrame;
                break;

            case Control.Arrows:
                if (k.upArrowKey.isPressed) input.y += 1f;
                if (k.downArrowKey.isPressed) input.y -= 1f;
                if (k.rightArrowKey.isPressed) input.x += 1f;
                if (k.leftArrowKey.isPressed) input.x -= 1f;
                jump = k.rightShiftKey.wasPressedThisFrame;
                break;
        }

        return input;
    }

    /// <summary>決めた場所へ歩き、着いたら少し止まって次の場所を決める。</summary>
    private Vector3 Wander()
    {
        Vector3 gap = wanderGoal - transform.position;
        gap.y = 0f;

        if (gap.magnitude < 0.5f)
        {
            wanderWait -= Time.deltaTime;
            if (wanderWait <= 0f)
            {
                Vector2 r = Random.insideUnitCircle * wanderRadius;
                wanderGoal = home + new Vector3(r.x, 0f, r.y);
                wanderWait = Random.Range(0.3f, 1.5f);
            }
            return Vector3.zero;
        }

        return gap.normalized;
    }
}
