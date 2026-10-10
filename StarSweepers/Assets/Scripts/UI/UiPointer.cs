using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.UI;

/// <summary>
/// **画面のポインター（スコープレンズの絵）。** マウスでもコントローラーでも、同じ絵で画面の一点を指す（2026/10/11・大槻さんの依頼）。
///
/// ・**Windows のカーソルはいつも隠す。** 代わりにこの絵を出す
/// ・**タイトル画面・ロビーの設定画面・ポーズ画面**では、画面にこの絵を出す
///   （遊んでいる間は、プレイヤーの照準 <see cref="PlayerAimController"/> が地面に同じ絵を出すので、こちらは出さない）
/// ・**マウス**…マウスの位置に出る。クリックはいつもどおり
/// ・**コントローラー**…**右スティックで動かし、LT（左クリックと同じ）か A で押す**。十字キーの上下でスクロール
///
/// ## コントローラーで押せる仕組み
///
/// コントローラー用に**見えないマウスをもう1つ**作り、右スティックで動かしてボタンを押させている。
/// 画面の部品（ボタン・入力欄・スライダー・スクロール）は、本物のマウスと同じように反応する。
/// 見えないマウスは、画面が開いている間だけ作り、閉じたら消す（遊びの操作がマウスの位置を読み違えないように）。
///
/// どのシーンにも置かなくてよい。**ゲームが始まると自動で1つ作られ、シーンをまたいで残る**
/// （見た目の設定は `Resources/UiPointer.prefab`）。
/// </summary>
[DefaultExecutionOrder(10000)]
public class UiPointer : MonoBehaviour
{
    /// <summary>見た目の設定を入れたプレハブ（Resources フォルダ）。</summary>
    public const string PrefabResourcePath = "UiPointer";

    [Header("見た目")]
    [Tooltip("ポインターの絵（スコープレンズ）")]
    [SerializeField] private Sprite pointerSprite;

    [Tooltip("ポインターの大きさ（画面の高さ 1080 のときのピクセル）")]
    [Min(4f)]
    [SerializeField] private float pointerSize = 56f;

    [Tooltip("ポインターの色。白なら絵の色のまま")]
    [SerializeField] private Color pointerColor = Color.white;

    [Header("コントローラー")]
    [Tooltip("右スティックを倒しきったときに、ポインターが1秒で動く量（画面の高さに対する割合）")]
    [Min(0f)]
    [SerializeField] private float stickSpeed = 1.2f;

    [Tooltip("これより小さい倒し方は無視する（スティックの遊び）")]
    [Range(0f, 0.9f)]
    [SerializeField] private float stickDeadZone = 0.2f;

    [Tooltip("十字キーの上下で、1秒にどれだけスクロールするか（マウスのホイールの目盛りの数）")]
    [Min(0f)]
    [SerializeField] private float scrollNotchesPerSecond = 8f;

    /// <summary>ゲームの中に1つだけあるポインター。</summary>
    public static UiPointer Current { get; private set; }

    /// <summary>
    /// **ポインターで操作する画面が開いているか。**（タイトル画面・ロビーの設定画面・ポーズ画面）
    /// 開いている間は、右スティックはポインターを動かすのに使う（照準は動かさない）。
    /// </summary>
    public static bool MenuActive =>
        TitleScreen.IsVisible || SpaceJunkLobbyScreen.IsSettingsOpen || GamePause.IsPaused;

    /// <summary>いま、コントローラーの右スティックでポインターを動かしているか。</summary>
    public static bool UsingStick => Current != null && Current.usingStick;

    // 遊びの照準（地面のスコープレンズ）が出ていたフレーム。出ている間は画面のポインターを出さない
    private static int gameplayAimFrame = -10;

    // OnGUI の小さな枠（DraggableGuiPanel）の上にマウスがあったフレーム
    private static int panelHoverFrame = -10;

    private bool usingStick;
    private Vector2 stickPosition;

    private Mouse virtualMouse;
    private Vector2 lastSentPosition;
    private bool lastSentLeft;

    private RectTransform pointerRect;
    private Image pointerImage;
    private Canvas canvas;

    /// <summary>
    /// **遊びの照準を地面に出した**ことを知らせる（<see cref="PlayerAimController"/> から毎フレーム呼ぶ）。
    /// 知らせがある間は、画面のポインターは出さず、右スティックは照準に使う。
    /// </summary>
    public static void ReportGameplayAim()
    {
        gameplayAimFrame = Time.frameCount;
    }

    /// <summary>マウスが OnGUI の小さな枠の上にあることを知らせる（<see cref="DraggableGuiPanel"/> から呼ぶ）。</summary>
    public static void ReportPanelHover()
    {
        panelHoverFrame = Time.frameCount;
    }

    // ------------------------------------------------------------
    // 自動で作る
    // ------------------------------------------------------------

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        // 「再生するたびに作り直さない」設定（ドメインの再読み込みなし）でも、前の再生の値を残さない
        Current = null;
        gameplayAimFrame = -10;
        panelHoverFrame = -10;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void CreateOnStart()
    {
        if (Current != null)
        {
            return;
        }

        GameObject prefab = Resources.Load<GameObject>(PrefabResourcePath);
        GameObject created = prefab != null ? Instantiate(prefab) : new GameObject("UiPointer", typeof(UiPointer));
        created.name = "UiPointer";
        DontDestroyOnLoad(created);
    }

    private void Awake()
    {
        if (Current != null && Current != this)
        {
            Destroy(gameObject);
            return;
        }

        Current = this;
        BuildPointer();
    }

    private void OnDestroy()
    {
        if (Current == this)
        {
            Current = null;
            Cursor.visible = true;
        }

        RemoveVirtualMouse();
    }

    /// <summary>いちばん手前に描く画面と、ポインターの絵を作る。</summary>
    private void BuildPointer()
    {
        GameObject canvasObject = new GameObject("PointerCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
        canvasObject.transform.SetParent(transform, false);

        canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = short.MaxValue; // ほかのどの画面よりも手前

        CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 1f;

        // GraphicRaycaster は付けない（ポインターの絵がクリックを横取りしないように）
        GameObject imageObject = new GameObject("Pointer", typeof(RectTransform), typeof(Image));
        imageObject.transform.SetParent(canvasObject.transform, false);

        pointerRect = imageObject.GetComponent<RectTransform>();
        pointerRect.anchorMin = Vector2.zero;
        pointerRect.anchorMax = Vector2.zero;
        pointerRect.pivot = new Vector2(0.5f, 0.5f);
        pointerRect.sizeDelta = new Vector2(pointerSize, pointerSize);

        pointerImage = imageObject.GetComponent<Image>();
        pointerImage.sprite = pointerSprite;
        pointerImage.color = pointerColor;
        pointerImage.preserveAspect = true;
        pointerImage.raycastTarget = false;
        pointerImage.enabled = false;
    }

    // ------------------------------------------------------------
    // 毎フレーム
    // ------------------------------------------------------------

    private void LateUpdate()
    {
        // **Windows のカーソルはいつも隠す。**（ほかの画面が出しても、最後にここで隠す）
        Cursor.visible = false;

        bool menu = MenuActive;

        // カーソルを画面の真ん中に閉じ込めている（一人称視点の検証シーンなど）間も、遊びの照準とみなす
        bool gameplayAim = !menu &&
                           (gameplayAimFrame >= Time.frameCount - 1 || Cursor.lockState == CursorLockMode.Locked);

        Mouse realMouse = RealMouse();
        if (usingStick && RealMouseWasUsed(realMouse))
        {
            // マウスを触ったら、マウスに戻す
            usingStick = false;
            SendVirtual(lastSentPosition, false, 0f);
        }

        if (gameplayAim)
        {
            // 遊んでいる間は、右スティックは照準に使う。見えないマウスは片付ける
            usingStick = false;
            RemoveVirtualMouse();
        }
        else
        {
            UpdateStick(realMouse);
        }

        if (usingStick)
        {
            KeepSelectionForPointer();
        }

        Vector2 position = usingStick ? stickPosition : RealMousePosition(realMouse);

        // 遊んでいる間でも、マウスが OnGUI の小さな枠（カメラの切り替えなど）の上にあるときは出す
        bool overPanel = !usingStick && panelHoverFrame >= Time.frameCount - 2;
        bool show = pointerSprite != null && (!gameplayAim || overPanel);

        pointerImage.enabled = show;
        if (show)
        {
            float scale = canvas.scaleFactor > 0f ? canvas.scaleFactor : 1f;
            pointerRect.anchoredPosition = position / scale;
            pointerRect.sizeDelta = new Vector2(pointerSize, pointerSize);
            pointerImage.color = pointerColor;
        }
    }

    /// <summary>右スティックでポインターを動かし、LT／A で押す。十字キーの上下でスクロールする。</summary>
    private void UpdateStick(Mouse realMouse)
    {
        Gamepad pad = Gamepad.current;
        if (pad == null)
        {
            usingStick = false;
            RemoveVirtualMouse();
            return;
        }

        Vector2 stick = pad.rightStick.ReadValue();
        bool moved = stick.magnitude >= stickDeadZone;

        if (moved && !usingStick)
        {
            // 切り替えた瞬間は、いまマウスが指している場所から動かし始める
            usingStick = true;
            stickPosition = RealMousePosition(realMouse);
        }

        if (!usingStick)
        {
            return;
        }

        if (moved)
        {
            stickPosition += stick * (stickSpeed * Screen.height * Time.unscaledDeltaTime);
            stickPosition.x = Mathf.Clamp(stickPosition.x, 0f, Screen.width);
            stickPosition.y = Mathf.Clamp(stickPosition.y, 0f, Screen.height);
        }

        bool left = pad.leftTrigger.isPressed || pad.buttonSouth.isPressed;

        float scrollDirection = (pad.dpad.up.isPressed ? 1f : 0f) - (pad.dpad.down.isPressed ? 1f : 0f);
        // Windows のホイール1目盛りは 120
        float scroll = scrollDirection * scrollNotchesPerSecond * 120f * Time.unscaledDeltaTime;

        if (stickPosition != lastSentPosition || left != lastSentLeft || scroll != 0f || virtualMouse == null)
        {
            SendVirtual(stickPosition, left, scroll);
        }
    }

    /// <summary>見えないマウスに「この位置・このボタン・このスクロール」を送る。</summary>
    private void SendVirtual(Vector2 position, bool left, float scroll)
    {
        if (virtualMouse == null)
        {
            if (!left && scroll == 0f && !usingStick)
            {
                return;
            }

            virtualMouse = InputSystem.AddDevice<Mouse>("ScopePointerMouse");
            lastSentPosition = position;
        }

        MouseState state = new MouseState
        {
            position = position,
            delta = position - lastSentPosition,
            scroll = new Vector2(0f, scroll),
        }.WithButton(MouseButton.Left, left);

        InputSystem.QueueStateEvent(virtualMouse, state);

        lastSentPosition = position;
        lastSentLeft = left;
    }

    private void RemoveVirtualMouse()
    {
        if (virtualMouse != null)
        {
            if (virtualMouse.added)
            {
                InputSystem.RemoveDevice(virtualMouse);
            }
            virtualMouse = null;
            lastSentLeft = false;
        }
    }

    /// <summary>
    /// コントローラーで押すときは、**ポインターが指している物だけ**が反応するようにする。
    ///
    /// 画面の部品は、コントローラーの A で「選ばれている部品」を押す仕組みも持っている（十字キーで選ぶ操作）。
    /// 選ばれたままだと、A でポインターの先と選ばれた部品の**両方が押されてしまう**ので、選んだ状態を外す。
    /// ただし**入力欄は外さない**（選ばれていないと文字を打てない）。
    /// </summary>
    private static void KeepSelectionForPointer()
    {
        EventSystem eventSystem = EventSystem.current;
        GameObject selected = eventSystem != null ? eventSystem.currentSelectedGameObject : null;
        if (selected == null)
        {
            return;
        }

        if (selected.GetComponent<InputField>() != null)
        {
            return;
        }

        eventSystem.SetSelectedGameObject(null);
    }

    // ------------------------------------------------------------
    // 本物のマウス
    // ------------------------------------------------------------

    /// <summary>本物のマウス（見えないマウスではないほう）。</summary>
    private Mouse RealMouse()
    {
        Mouse current = Mouse.current;
        if (current != null && current != virtualMouse)
        {
            return current;
        }

        foreach (InputDevice device in InputSystem.devices)
        {
            if (device is Mouse mouse && mouse != virtualMouse)
            {
                return mouse;
            }
        }

        return null;
    }

    private static bool RealMouseWasUsed(Mouse mouse)
    {
        return mouse != null &&
               (mouse.delta.ReadValue().sqrMagnitude > 1f ||
                mouse.leftButton.wasPressedThisFrame ||
                mouse.rightButton.wasPressedThisFrame ||
                mouse.scroll.ReadValue().sqrMagnitude > 0f);
    }

    private static Vector2 RealMousePosition(Mouse mouse)
    {
        return mouse != null ? mouse.position.ReadValue() : new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
    }
}
