using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// **カーソルが指している地面の一点**を計算して、みんなに配る係。
///
/// 見下ろし視点なので「画面のカーソル位置」だけでは方向が決まらない。
/// カメラからカーソルへ向かう線を伸ばし、地面にぶつかった場所を「狙点」とする。
///
/// この狙点から見たプレイヤーの向きが、このゲームの「前方向」になる（仕様6）。
/// プレイヤーの向き・フックの発射方向・投げる方向は、すべてここの値を読む。
///
/// ## コントローラー（2026/9/22・大槻さんの依頼）
///
/// **右スティックで、画面上のカーソルを動かせる。** マウスと同じく「画面の一点」を動かすだけなので、
/// 狙点の計算はマウスのときとまったく同じ。
///
/// - 右スティックを倒すと**コントローラーのカーソル**に切り替わる。マウスのカーソルは隠し、
///   代わりに**地面へ照準マーク**（<see cref="reticleSprite"/>）を描く
/// - マウスを動かすかクリックすると、**マウスのカーソル**に戻る
/// - **照準マークは、マウスのときも地面に出す**（2026/10/11。Windows のカーソルは <see cref="UiPointer"/> が隠している）
///
/// 使い方：プレイヤーに付けて、見下ろしカメラを Aim Camera に入れるだけ。
/// </summary>
public class PlayerAimController : MonoBehaviour
{
    [Header("参照")]
    [Tooltip("見下ろしカメラ。空なら Camera.main を自動で使う")]
    [SerializeField] private Camera aimCamera;

    [Header("地面の判定")]
    [Tooltip("この高さの水平な床があるものとして狙点を出す（下のレイヤー判定に当たらなかったときの保険）")]
    [SerializeField] private float groundHeight = 0f;

    [Tooltip("ここに入れたレイヤーに線が当たれば、その場所を優先して狙点にする")]
    [SerializeField] private LayerMask groundMask = ~0;

    [Tooltip("カメラから伸ばす線の長さ")]
    [SerializeField] private float rayDistance = 300f;

    [Header("コントローラー（右スティックでカーソルを動かす）")]
    [Tooltip("右スティックを倒しきったときに、カーソルが1秒で動く量（画面の高さに対する割合）")]
    [Min(0f)]
    [SerializeField] private float stickCursorSpeed = 1.2f;

    [Tooltip("これより小さい倒し方は無視する（スティックの遊び）")]
    [Range(0f, 0.9f)]
    [SerializeField] private float stickDeadZone = 0.2f;

    [Header("照準マーク（コントローラーのときだけ地面に出る）")]
    [Tooltip("地面に描く照準マークの画像。空なら照準マークは出さない")]
    [SerializeField] private Sprite reticleSprite;

    [Tooltip("照準マークの大きさ（メートル）")]
    [Min(0.1f)]
    [SerializeField] private float reticleSize = 1.5f;

    [Tooltip("照準マークの色。白なら画像の色のまま")]
    [SerializeField] private Color reticleColor = Color.white;

    /// <summary>カーソルが指している地面のワールド座標。</summary>
    public Vector3 AimPoint { get; private set; }

    /// <summary>プレイヤーから狙点へ向かう、水平に倒した向き（長さ1）。これが「前方向」。</summary>
    public Vector3 AimDirection { get; private set; } = Vector3.forward;

    /// <summary>今フレーム、狙点をちゃんと計算できたか。</summary>
    public bool HasAim { get; private set; }

    /// <summary>いま、コントローラーの右スティックでカーソルを動かしているか。</summary>
    public bool UsingStickCursor { get; private set; }

    // コントローラーのときのカーソル位置（画面の座標）
    private Vector2 stickCursor;

    // 地面に描く照準マーク。プレイヤーの子にすると体と一緒に回ってしまうので、外に置く
    private SpriteRenderer reticle;

    private void Awake()
    {
        if (aimCamera == null)
        {
            aimCamera = Camera.main;
        }
    }

    /// <summary>
    /// 使うカメラを後から決める。
    /// **オンラインではプレハブから生まれるため、シーンのカメラを入れておけない。**
    /// 生まれた時点で <see cref="FishingNetPlayer"/> から渡される。
    /// </summary>
    public void SetCamera(Camera camera)
    {
        aimCamera = camera;
    }

    private void Update()
    {
        Gamepad pad = Gamepad.current;
        if (pad != null && GameSettings.ControllerOperation == ControllerOperationType.TypeB)
        {
            UpdateTypeBAim(pad);
            UpdateReticle(typeB: true);
            return;
        }

        UpdateCursorSource();
        UpdateAim();
        UpdateReticle();
    }

    /// <summary>
    /// タイプBでは左スティックを移動と向きに共用する。
    /// 通常は移動方向へ向き、LT中は移動を止めて向き調整に使う。
    /// 操作の割り当てだけをここで扱い、移動の実行は FishingPlayerController に任せる。
    /// </summary>
    private void UpdateTypeBAim(Gamepad pad)
    {
        Vector2 stick = pad.leftStick.ReadValue();
        if (stick.magnitude < stickDeadZone)
        {
            UsingStickCursor = false;
            return;
        }

        Vector3 direction = new Vector3(stick.x, 0f, stick.y);
        Camera view = aimCamera != null ? aimCamera : Camera.main;
        if (view != null)
        {
            direction = Quaternion.Euler(0f, view.transform.eulerAngles.y, 0f) * direction;
        }

        direction.y = 0f;
        if (direction.sqrMagnitude > 0.0001f)
        {
            AimDirection = direction.normalized;
            AimPoint = transform.position + AimDirection;
            HasAim = true;
        }

        UsingStickCursor = false;
    }

    private void OnDisable()
    {
        // 他の人のぶん（オンラインでは無効にされる）や、消えたときに照準マークを残さない
        if (reticle != null)
        {
            reticle.enabled = false;
        }

        if (UsingStickCursor)
        {
            UsingStickCursor = false;
        }
    }

    private void OnDestroy()
    {
        if (reticle != null)
        {
            Destroy(reticle.gameObject);
        }
    }

    /// <summary>
    /// **マウスとコントローラーの、どちらのカーソルを使うかを決める。**
    /// 最後に触ったほうを使う。
    /// </summary>
    private void UpdateCursorSource()
    {
        Mouse mouse = Mouse.current;

        // マウスを動かした／クリックした → マウスに戻す
        if (mouse != null && UsingStickCursor &&
            (mouse.delta.ReadValue().sqrMagnitude > 1f ||
             mouse.leftButton.wasPressedThisFrame || mouse.rightButton.wasPressedThisFrame))
        {
            UsingStickCursor = false;
        }

        // ポーズ中はスティックでカーソルを動かさない（ポーズ画面の選択に使うため）
        Gamepad pad = Gamepad.current;
        // タイトル・ロビーの設定画面などが開いている間は、右スティックは画面のポインター（UiPointer）に使う
        if (pad == null || GamePause.BlocksInput || UiPointer.MenuActive)
        {
            return;
        }

        Vector2 stick = pad.rightStick.ReadValue();
        if (stick.magnitude < stickDeadZone)
        {
            return;
        }

        if (!UsingStickCursor)
        {
            // 切り替えた瞬間は、いまの狙点（なければ画面の中央）から動かし始める
            UsingStickCursor = true;
            stickCursor = StartPosition();
        }

        stickCursor += stick * (stickCursorSpeed * Screen.height * Time.unscaledDeltaTime);
        stickCursor.x = Mathf.Clamp(stickCursor.x, 0f, Screen.width);
        stickCursor.y = Mathf.Clamp(stickCursor.y, 0f, Screen.height);
    }

    /// <summary>コントローラーのカーソルを出し始める位置。いまの狙点の場所、なければ画面の中央。</summary>
    private Vector2 StartPosition()
    {
        if (aimCamera != null && HasAim)
        {
            Vector3 screen = aimCamera.WorldToScreenPoint(AimPoint);
            if (screen.z > 0f)
            {
                return screen;
            }
        }

        return new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
    }

    private void UpdateAim()
    {
        HasAim = false;

        if (aimCamera == null)
        {
            return;
        }

        Vector2 screenPoint;
        if (UsingStickCursor)
        {
            screenPoint = stickCursor;
        }
        else
        {
            Mouse mouse = Mouse.current;
            if (mouse == null)
            {
                return;
            }

            screenPoint = mouse.position.ReadValue();
        }

        Ray ray = aimCamera.ScreenPointToRay(screenPoint);

        // まず、指定レイヤーの実際の床に当たるか試す
        if (Physics.Raycast(ray, out RaycastHit hit, rayDistance, groundMask, QueryTriggerInteraction.Ignore))
        {
            AimPoint = hit.point;
            HasAim = true;
        }
        else
        {
            // 当たらなければ、高さ groundHeight の水平な面との交点を使う
            Plane plane = new Plane(Vector3.up, new Vector3(0f, groundHeight, 0f));
            if (plane.Raycast(ray, out float enter))
            {
                AimPoint = ray.GetPoint(enter);
                HasAim = true;
            }
        }

        if (HasAim)
        {
            Vector3 flat = AimPoint - transform.position;
            flat.y = 0f;

            // カーソルがプレイヤーの真上あたりに来て向きが定まらないときは、前の向きを保つ
            if (flat.sqrMagnitude > 0.0001f)
            {
                AimDirection = flat.normalized;
            }
        }
    }

    /// <summary>
    /// **地面に照準マークを描く。** マウスでもコントローラーでも出す（2026/10/11 から。Windows のカーソルは
    /// <see cref="UiPointer"/> がいつも隠しているので、マウスのときもこれが狙いの目印になる）。
    /// タイプB（左スティックで向きを決める）のときは、指す場所が無いので出さない。
    /// 遊びの照準を出している間は、画面のポインター（<see cref="UiPointer"/>）に知らせて、そちらは隠してもらう。
    /// </summary>
    private void UpdateReticle(bool typeB = false)
    {
        bool playing = !GamePause.BlocksInput && !UiPointer.MenuActive;
        bool show = !typeB && playing && HasAim && reticleSprite != null;

        // 照準マークの絵が入っていないプレイヤー（古い検証シーンなど）では知らせない。画面のポインターが代わりに出る
        if (show || (typeB && playing))
        {
            UiPointer.ReportGameplayAim();
        }

        if (!show)
        {
            if (reticle != null)
            {
                reticle.enabled = false;
            }

            return;
        }

        if (reticle == null)
        {
            reticle = CreateReticle();
        }

        reticle.enabled = true;
        reticle.color = reticleColor;

        // 地面すれすれに寝かせる。画面の上下と絵の上下がそろうよう、カメラの水平の向きに合わせる
        float yaw = aimCamera != null ? aimCamera.transform.eulerAngles.y : 0f;
        reticle.transform.SetPositionAndRotation(
            AimPoint + Vector3.up * 0.05f,
            Quaternion.Euler(90f, yaw, 0f));

        float spriteWidth = Mathf.Max(0.0001f, reticleSprite.bounds.size.x);
        reticle.transform.localScale = Vector3.one * (reticleSize / spriteWidth);
    }

    private SpriteRenderer CreateReticle()
    {
        GameObject created = new GameObject($"{name} の照準マーク");
        SpriteRenderer renderer = created.AddComponent<SpriteRenderer>();
        renderer.sprite = reticleSprite;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        return renderer;
    }
}
