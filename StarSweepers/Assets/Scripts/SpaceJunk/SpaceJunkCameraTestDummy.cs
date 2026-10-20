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

    /// <summary>重力加速度（メートル／秒²）。ジャンプの放物線に使う。</summary>
    private const float Gravity = 9.81f;

    /// <summary>最初に置かれた位置。勝手に歩き回る範囲の中心であり、Y が「地面の高さ」になる。</summary>
    private Vector3 home;

    /// <summary>勝手に歩くときの、いま向かっている場所。</summary>
    private Vector3 wanderGoal;

    /// <summary>勝手に歩くときの、着いてから次の場所を決めるまでの残り秒数。</summary>
    private float wanderWait;

    /// <summary>地面からの高さ（メートル）。ジャンプ中だけ 0 より大きい。</summary>
    private float height;

    /// <summary>上向きの速さ（メートル／秒）。落ちているときはマイナス。</summary>
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

        // キーを押している（入力の長さがほぼ 0 でない）ときだけ、キーの向きに歩く
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

        // 原点から areaRadius の円の中に収める（床から落ちないように）。flat は (X, Z) の2つ組
        Vector3 nextPosition = transform.position + move * (speed * Time.deltaTime);
        Vector2 flat = Vector2.ClampMagnitude(new Vector2(nextPosition.x, nextPosition.z), areaRadius);

        // ジャンプ（地面の高さは最初の高さ）。初速は「jumpHeight までちょうど届く速さ」（v² = 2gh）
        if (jump && height <= 0f)
        {
            verticalSpeed = Mathf.Sqrt(2f * Gravity * jumpHeight);
        }
        verticalSpeed -= Gravity * Time.deltaTime;
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

    /// <summary>
    /// 「動かし方」に応じたキーを読む。X が右（＋）／左（−）、Y が奥（＋）／手前（−）。
    /// `Still` のときは何も読まない（0 のまま）。
    /// </summary>
    private Vector2 ReadInput(out bool jump)
    {
        jump = false;
        Keyboard keyboard = Keyboard.current;
        if (keyboard == null)
        {
            return Vector2.zero;
        }

        Vector2 input = Vector2.zero;

        switch (control)
        {
            case Control.Wasd:
                if (keyboard.wKey.isPressed) input.y += 1f;
                if (keyboard.sKey.isPressed) input.y -= 1f;
                if (keyboard.dKey.isPressed) input.x += 1f;
                if (keyboard.aKey.isPressed) input.x -= 1f;
                jump = keyboard.spaceKey.wasPressedThisFrame;
                break;

            case Control.Arrows:
                if (keyboard.upArrowKey.isPressed) input.y += 1f;
                if (keyboard.downArrowKey.isPressed) input.y -= 1f;
                if (keyboard.rightArrowKey.isPressed) input.x += 1f;
                if (keyboard.leftArrowKey.isPressed) input.x -= 1f;
                jump = keyboard.rightShiftKey.wasPressedThisFrame;
                break;
        }

        return input;
    }

    /// <summary>決めた場所へ歩き、着いたら少し止まって次の場所を決める。</summary>
    private Vector3 Wander()
    {
        Vector3 gap = wanderGoal - transform.position;
        gap.y = 0f;

        // 目的地まで 0.5m 以内なら「着いた」とみなし、0.3〜1.5 秒止まってから次の場所を決める
        if (gap.magnitude < 0.5f)
        {
            wanderWait -= Time.deltaTime;
            if (wanderWait <= 0f)
            {
                Vector2 offset = Random.insideUnitCircle * wanderRadius;
                wanderGoal = home + new Vector3(offset.x, 0f, offset.y);
                wanderWait = Random.Range(0.3f, 1.5f);
            }
            return Vector3.zero;
        }

        return gap.normalized;
    }
}
