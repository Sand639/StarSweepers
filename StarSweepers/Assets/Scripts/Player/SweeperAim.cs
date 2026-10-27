using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// **プレイヤー（本実装）の狙う向き。**（2026/10/27・小野田さんの仕様）
///
/// ・**マウス**…プレイヤーを中心にした**半径1m（Cursor Radius）の円の中だけ**を動く仮のカーソルを、マウスの動きで動かす。
///   Windows のカーソルは画面の中に閉じ込めて隠す（ポーズ中は戻す）
/// ・**右スティック**…倒した方向にカーソルが出る（倒しきると円のふち）。離すとその場に残る
/// ・**最後に触ったほう**が使われる
/// ・1台で複数人（席）・ボットのときは、画面上のポインターの位置を地面に映し、円の中に収めて使う
///
/// プレイヤーから見たカーソルの方向が <see cref="Direction"/>（光線を撃つ向き）になる。
/// カーソルはプレイヤーと一緒に動く（プレイヤーから見た位置を覚えている）。
/// </summary>
[DefaultExecutionOrder(-10)]
public class SweeperAim : MonoBehaviour
{
    [Header("カーソルが動ける範囲")]
    [Tooltip("プレイヤーの中心から、カーソルが動ける円の半径（m）")]
    [Min(0.1f)]
    [SerializeField] private float cursorRadius = 1f;

    [Header("マウス")]
    [Tooltip("マウスを画面の高さぶん動かしたとき、カーソルが何m動くか")]
    [Min(0.01f)]
    [SerializeField] private float mouseSensitivity = 3f;

    [Tooltip("ON：遊んでいる間、Windows のカーソルを画面の中に閉じ込める（隠す）。ポーズ中は外す")]
    [SerializeField] private bool lockMouseCursor = true;

    [Header("右スティック")]
    [Tooltip("これより小さい倒し方は無視する（スティックの遊び）")]
    [Range(0f, 0.9f)]
    [SerializeField] private float stickDeadZone = 0.2f;

    [Header("見た目")]
    [Tooltip("カーソルの目印（足元の高さで、カーソルの位置に動かす）。空なら目印は出ない")]
    [SerializeField] private Transform cursorVisual;

    [Tooltip("目印を床からどれだけ浮かせるか（m）")]
    [Min(0f)]
    [SerializeField] private float cursorLift = 0.03f;

    private PlayerInputSource input;

    /// <summary>プレイヤーから見たカーソルの位置（水平。長さは Cursor Radius 以下）。</summary>
    private Vector3 cursorOffset;

    /// <summary>最後にちゃんと決まった向き。カーソルがプレイヤーの真上に来たときは、これを使い続ける。</summary>
    private Vector3 lastDirection = Vector3.forward;

    /// <summary>撃つ向き（水平・長さ1）。</summary>
    public Vector3 Direction => lastDirection;

    /// <summary>カーソルのワールド座標（足元の高さ）。</summary>
    public Vector3 CursorPoint => transform.position + cursorOffset;

    /// <summary>カーソルが動ける円の半径（m）。</summary>
    public float CursorRadius => cursorRadius;

    /// <summary>最後に触ったのがコントローラーか。</summary>
    public bool UsingStick { get; private set; }

    private void Awake()
    {
        input = PlayerInputSource.Get(this);

        // 最初は体の前に置いておく
        lastDirection = FlatForward(transform.forward);
        cursorOffset = lastDirection * cursorRadius;
    }

    private void OnDisable()
    {
        if (lockMouseCursor)
        {
            Cursor.lockState = CursorLockMode.None;
        }
    }

    private void Update()
    {
        // ポーズ中・タイトルやロビーの設定画面などが開いている間は、遊びの狙いを止める（マウスは画面のボタンに使う）
        bool playing = !GamePause.BlocksInput && !UiPointer.MenuActive;

        UpdateMouseLock(playing);

        if (playing)
        {
            ReadInput();

            // 地面にカーソルを出しているので、画面のポインター（UiPointer）は出さないでもらう
            UiPointer.ReportGameplayAim();
        }

        if (cursorOffset.sqrMagnitude > 0.0025f)
        {
            lastDirection = cursorOffset.normalized;
        }
    }

    /// <summary>カーソルの目印を出す・隠す（オンラインのほかの人の体では隠す）。</summary>
    public void SetCursorVisible(bool visible)
    {
        if (cursorVisual != null)
        {
            cursorVisual.gameObject.SetActive(visible);
        }
    }

    private void LateUpdate()
    {
        if (cursorVisual != null)
        {
            cursorVisual.position = CursorPoint + Vector3.up * cursorLift;
            cursorVisual.rotation = Quaternion.LookRotation(lastDirection, Vector3.up);
        }
    }

    /// <summary>遊んでいる間はカーソルを閉じ込め、ポーズ中・画面が開いている間は外す（ボタンを押せるように）。</summary>
    private void UpdateMouseLock(bool playing)
    {
        if (!lockMouseCursor || input.UsesSeat || input.UsesVirtual)
        {
            return;
        }

        CursorLockMode wanted = playing ? CursorLockMode.Locked : CursorLockMode.None;
        if (Cursor.lockState != wanted)
        {
            Cursor.lockState = wanted;
        }
    }

    private void ReadInput()
    {
        // ---- 右スティック ----
        Gamepad pad = input.Gamepad;
        if (pad != null)
        {
            Vector2 stick = pad.rightStick.ReadValue();
            if (stick.magnitude > stickDeadZone)
            {
                Vector3 offset = ToWorld(stick) * cursorRadius;
                cursorOffset = Vector3.ClampMagnitude(offset, cursorRadius);
                UsingStick = true;
                return;
            }
        }

        // ---- 1台で複数人・ボット：画面上のポインターを地面に映す ----
        if (input.UsesSeat || input.UsesVirtual)
        {
            if (input.TryReadPointer(out Vector2 screenPoint) && TryProjectToFeet(screenPoint, out Vector3 point))
            {
                Vector3 offset = point - transform.position;
                offset.y = 0f;
                cursorOffset = Vector3.ClampMagnitude(offset, cursorRadius);
            }
            return;
        }

        // ---- マウス：動いた量だけ、円の中でカーソルを動かす ----
        Mouse mouse = Mouse.current;
        if (mouse == null)
        {
            return;
        }

        Vector2 delta = mouse.delta.ReadValue();
        if (delta.sqrMagnitude < 0.01f)
        {
            return;
        }

        UsingStick = false;
        float metersPerPixel = mouseSensitivity / Mathf.Max(1f, Screen.height);
        cursorOffset = Vector3.ClampMagnitude(cursorOffset + ToWorld(delta) * metersPerPixel, cursorRadius);
    }

    /// <summary>画面の上下左右（スティックやマウスの向き）を、カメラの向きに合わせた地面の向きにする。</summary>
    private static Vector3 ToWorld(Vector2 screenDirection)
    {
        Vector3 direction = new Vector3(screenDirection.x, 0f, screenDirection.y);
        Camera view = Camera.main;
        if (view != null)
        {
            direction = Quaternion.Euler(0f, view.transform.eulerAngles.y, 0f) * direction;
        }
        return direction;
    }

    /// <summary>画面上の点を、足元の高さの水平な面に映す。</summary>
    private bool TryProjectToFeet(Vector2 screenPoint, out Vector3 point)
    {
        point = Vector3.zero;
        Camera view = Camera.main;
        if (view == null)
        {
            return false;
        }

        Ray ray = view.ScreenPointToRay(screenPoint);
        Plane feet = new Plane(Vector3.up, transform.position);
        if (!feet.Raycast(ray, out float distance))
        {
            return false;
        }

        point = ray.GetPoint(distance);
        return true;
    }

    private static Vector3 FlatForward(Vector3 forward)
    {
        forward.y = 0f;
        return forward.sqrMagnitude > 0.0001f ? forward.normalized : Vector3.forward;
    }
}
