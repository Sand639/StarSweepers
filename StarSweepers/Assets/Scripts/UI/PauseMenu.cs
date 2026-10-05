using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

/// <summary>
/// **Escape で開くポーズ画面。**
///
/// | 項目 | 押すとどうなるか |
/// | **ゲームをつづける** | 画面を閉じて、続きから遊ぶ |
/// | **設定** | 音量とマウス感度を変える画面へ |
/// | **ロビーに戻る** | **宇宙ごみの試合中だけ出る。** 確かめてから試合を抜けてロビーへ（ホストなら全員で戻る）。`SpaceJunkLeaveMatch` |
/// | **ゲームをやめる** | 確かめてから、ゲームを終了する（エディタでは再生を止める） |
///
/// **マウスで選ぶ。** 項目に重なると**色が変わり、少し大きくなり、左に「▶」が出る**
/// （<see cref="MenuHoverHighlight"/>）。
///
/// ## 画面はコードで組み立てている
///
/// **プレハブに画面を作り込んでいない。** 再生したときに、この部品が自分で作る。
///
/// 理由は**日本語のため。** Unityに最初から入っている文字は日本語を持っていないので、
/// そのままだと項目名が四角（□□□）になってしまう。
/// **パソコンに入っている日本語フォントを、再生したときに借りてくる**必要があり、
/// それは保存できない（プレハブに残らない）ので、作るところまで含めてコードにしてある。
///
/// ## 置き方
///
/// **空のゲームオブジェクトにこの部品を付けるだけ。**
/// カメラもUIも要らない（足りない物はこちらで作る）。
/// メニューの `Tools > StarSweepers > アーカイブ > 共通の部品 > ポーズ画面を置く` でも置ける。
///
/// **置き忘れても動く。** シーンのどこにも無ければ、再生したときに自分で1つ作る
/// （<see cref="AutoCreate"/>）。
/// </summary>
// Escape を**他の部品より先に**受け取る。
// カメラ側も Escape を見ているので、順番が決まっていないと取り合いになる
[DefaultExecutionOrder(-100)]
public class PauseMenu : MonoBehaviour
{
    [Header("キーの割り当て")]
    [Tooltip("ポーズ画面を開く／閉じるキー")]
    [SerializeField] private Key pauseKey = Key.Escape;

    [Tooltip("ポーズ画面を開く／閉じる、コントローラーのボタン（Xbox の Start）")]
    [SerializeField] private GamepadButton pauseButton = GamepadButton.Start;

    [Tooltip("ひとつ前へ戻る、コントローラーのボタン（East＝Xbox の B）。設定の画面なら最初の画面へ、最初の画面なら閉じる")]
    [SerializeField] private GamepadButton backButton = GamepadButton.East;

    [Tooltip("ONだと、開いている間は**世界の時間も止まる**（1人用）。\n" +
             "**オンラインのシーンでは OFF にすること。** 自分のPCだけ時間を止めても" +
             "他の人は動き続けるので、閉じた瞬間に相手が飛んで見える")]
    [SerializeField] private bool freezeTime = true;

    [Header("つなぐもの")]
    [Tooltip("Assets/InputSystem_Actions を入れる。**UIのクリックに使う。**" +
             "シーンにすでに EventSystem があるなら空でもよい")]
    [SerializeField] private InputActionAsset inputActions;

    [Header("文字")]
    [Tooltip("使いたい日本語フォントの名前。上から順に、パソコンに入っているものを探す")]
    [SerializeField]
    private string[] fontNames =
    {
        "Yu Gothic UI", "Yu Gothic", "Meiryo UI", "Meiryo", "MS UI Gothic", "Noto Sans CJK JP",
    };

    [Header("見た目")]
    [Tooltip("後ろを暗くする色")]
    [SerializeField] private Color backdropColor = new Color(0f, 0f, 0f, 0.65f);

    [Tooltip("項目の色（ふだん）")]
    [SerializeField] private Color itemColor = new Color(0.16f, 0.18f, 0.22f, 0.95f);

    [Tooltip("項目の色（マウスが重なったとき）")]
    [SerializeField] private Color itemHoverColor = new Color(0.30f, 0.55f, 0.85f, 1f);

    [Tooltip("文字の色")]
    [SerializeField] private Color textColor = Color.white;

    [Tooltip("項目の文字の大きさ")]
    [Range(14, 60)]
    [SerializeField] private int fontSize = 28;

    /// <summary>いまポーズ画面が開いているか。</summary>
    public bool IsOpen { get; private set; }

    /// <summary>いま使われているポーズ画面。無ければ null。</summary>
    public static PauseMenu Current { get; private set; }

    /// <summary>
    /// **このゲームにポーズ画面があるか。**
    ///
    /// カメラ側がこれを見て、「Escape は自分のものではない」と判断している。
    /// </summary>
    public static bool Exists => Current != null;

    /// <summary>
    /// **「ロビーに戻る」を出してよいか。** 遊びの側（宇宙ごみなど）が入れる差し込み口。
    /// 入っていない・false なら、ボタンを出さない（釣りなど、ロビーへ戻る処理が無い遊び）。
    /// 宇宙ごみは <c>SpaceJunkLeaveMatch</c> が入れている。
    /// </summary>
    public static System.Func<bool> CanReturnToLobby;

    /// <summary>「ロビーに戻る」を決定したときに呼ぶ処理。遊びの側が入れる。</summary>
    public static System.Action ReturnToLobby;

    private GameObject root;
    private GameObject mainPage;
    private GameObject settingsPage;
    private GameObject confirmPage;
    private Font font;

    // 開く前のカーソルの状態（閉じたときに戻すため）
    private CursorLockMode cursorLockBeforeOpen = CursorLockMode.Locked;
    private bool cursorVisibleBeforeOpen;

    // 画面の作り。1920x1080 を基準にした大きさ
    private const float ItemWidth = 420f;
    private const float ItemHeight = 72f;
    private const float ItemGap = 16f;

    private void Awake()
    {
        // **1つだけにする。** 2つあると Escape で開いた直後に閉じてしまう
        if (Current != null && Current != this)
        {
            Debug.LogWarning("[UI] ポーズ画面が2つあったので、あとの1つを消しました。", gameObject);
            Destroy(gameObject);

            return;
        }

        Current = this;

        font = FindFont();

        Build();
        SetVisible(false);
    }

    private void OnDestroy()
    {
        if (Current == this)
        {
            Current = null;
        }
    }

    /// <summary>
    /// **シーンにポーズ画面が無ければ、再生したときに自分で作る。**
    ///
    /// 置き忘れると「Escape を押しても何も起きない」ことになり、
    /// 原因が分かりにくいため。手で置いてあれば何もしない。
    /// 一度作れば、シーンが変わっても残る。
    /// </summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AutoCreate()
    {
        if (FindFirstObjectByType<PauseMenu>(FindObjectsInactive.Include) != null)
        {
            return;
        }

        GameObject created = new GameObject("PauseMenu (自動)");
        created.AddComponent<PauseMenu>();
        DontDestroyOnLoad(created);

        Debug.Log("[UI] シーンにポーズ画面が無かったので、自動で用意しました。Escape で開きます。");
    }

    private void Update()
    {
        Keyboard keyboard = Keyboard.current;

        if ((keyboard != null && keyboard[pauseKey].wasPressedThisFrame) || GamepadInput.WasPressed(pauseButton))
        {
            Toggle();
        }
        else if (IsOpen && GamepadInput.WasPressed(backButton))
        {
            if (confirmPage != null && confirmPage.activeSelf)
            {
                // 確かめる画面からは、最初の画面へ戻る
                ShowSettings(false);
            }
            else if (settingsPage != null && settingsPage.activeSelf)
            {
                ShowSettings(false);
            }
            else
            {
                Close();
            }
        }
    }

    private void OnDisable()
    {
        // 開いたまま消えると、**止まったまま戻せなくなる**
        if (IsOpen)
        {
            Close();
        }
    }

    // ------------------------------------------------------------
    // 開く・閉じる
    // ------------------------------------------------------------

    /// <summary>開いていれば閉じ、閉じていれば開く。</summary>
    public void Toggle()
    {
        if (IsOpen)
        {
            Close();
        }
        else
        {
            Open();
        }
    }

    /// <summary>ポーズ画面を開く。**ゲームは止まる。**</summary>
    public void Open()
    {
        if (IsOpen)
        {
            return;
        }

        IsOpen = true;

        // **開くたびに確かめる。**
        // シーンが切り替わると、シーンに置かれていた EventSystem は消えてしまう。
        // 無ければここで作り直さないと、ボタンが反応しない
        EnsureEventSystem();

        // 「ロビーに戻る」を出すかは、開くときの状況（試合中か）で変わるので、毎回作り直す
        RebuildMainPage();

        SetVisible(true);
        ShowSettings(false);

        GamePause.SetPaused(true, freezeTime);

        // 閉じたときに元へ戻せるよう、開く前の状態を控えておく
        cursorLockBeforeOpen = Cursor.lockState;
        cursorVisibleBeforeOpen = Cursor.visible;

        // マウスで選ぶので、カーソルを出す
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    /// <summary>ポーズ画面を閉じる。**ゲームが動き出す。**</summary>
    public void Close()
    {
        if (!IsOpen)
        {
            return;
        }

        IsOpen = false;

        SetVisible(false);
        GamePause.SetPaused(false);

        // 遊びに戻るので、**開く前の状態に戻す**
        Cursor.lockState = cursorLockBeforeOpen;
        Cursor.visible = cursorVisibleBeforeOpen;
    }

    /// <summary>設定の画面と、最初の画面を切り替える。</summary>
    public void ShowSettings(bool show)
    {
        if (mainPage != null)
        {
            mainPage.SetActive(!show);
        }

        if (settingsPage != null)
        {
            settingsPage.SetActive(show);
        }

        CloseConfirm();

        SelectFirstForGamepad(show ? settingsPage : mainPage);
    }

    // ------------------------------------------------------------
    // 確かめる画面（ロビーに戻る・ゲームをやめる）
    // ------------------------------------------------------------

    /// <summary>
    /// **本当にいいか確かめる画面を出す。**
    /// 上の項目（<paramref name="cancelLabel"/>）は閉じて遊びに戻る、下の項目（<paramref name="okLabel"/>）で実行する。
    /// コントローラーでは**上の「つづける」側を選んだ状態**で開く（うっかり抜けないように）。
    /// </summary>
    private void ShowConfirm(string message, string note, string cancelLabel, string okLabel,
        UnityEngine.Events.UnityAction onOk)
    {
        CloseConfirm();

        mainPage.SetActive(false);
        settingsPage.SetActive(false);

        confirmPage = new GameObject("ConfirmPage", typeof(RectTransform));
        confirmPage.transform.SetParent(root.transform, false);
        Stretch(confirmPage.GetComponent<RectTransform>());

        GameObject messageObject = CreateText("Message", confirmPage.transform, message, fontSize + 4);
        PlaceText(messageObject, 150f, 56f);

        GameObject noteObject = CreateText("Note", confirmPage.transform, note, fontSize - 8);
        PlaceText(noteObject, 100f, 36f);

        CreateMenuItem(confirmPage.transform, cancelLabel, 10f, Close);
        CreateMenuItem(confirmPage.transform, okLabel, 10f - (ItemHeight + ItemGap), onOk);

        SelectFirstForGamepad(confirmPage);
    }

    /// <summary>確かめる画面を消す（出ていなければ何もしない）。</summary>
    private void CloseConfirm()
    {
        if (confirmPage != null)
        {
            Destroy(confirmPage);
            confirmPage = null;
        }
    }

    /// <summary>画面の真ん中の、高さ <paramref name="y"/> に横長の文字を置く。</summary>
    private static void PlaceText(GameObject textObject, float y, float height)
    {
        RectTransform rect = textObject.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = new Vector2(1200f, height);
        rect.anchoredPosition = new Vector2(0f, y);
    }

    /// <summary>「ロビーに戻る」を押したとき。確かめてから戻る。</summary>
    private void ConfirmReturnToLobby()
    {
        ShowConfirm(
            "試合から抜けてロビーに戻ります",
            "※ホストの場合は全員がロビーに戻されます",
            "試合をつづける",
            "ロビーに戻る",
            () =>
            {
                // 止めた時間やカーソルを元に戻してから移る
                Close();
                ReturnToLobby?.Invoke();
            });
    }

    /// <summary>「ゲームをやめる」を押したとき。確かめてから終わる。</summary>
    private void ConfirmQuit()
    {
        ShowConfirm(
            IsInMatch() ? "試合から抜けてゲームを終了します" : "ゲームを終了します",
            "※ホストの場合は全員がタイトル画面に戻されます",
            "ゲームをつづける",
            "ゲームをやめる",
            QuitGame);
    }

    /// <summary>いま「ロビーに戻る」を出せる場面（試合中）か。</summary>
    private static bool IsInMatch()
    {
        return CanReturnToLobby != null && ReturnToLobby != null && CanReturnToLobby();
    }

    /// <summary>
    /// **コントローラーがつながっていれば、その画面の最初の項目を選んでおく。**
    /// 何も選ばれていないと、十字キーやスティックを倒しても何も動かないため（2026/9/22）。
    /// 選んだ項目は A で決定できる。
    /// </summary>
    private void SelectFirstForGamepad(GameObject page)
    {
        if (Gamepad.current == null || page == null || !page.activeInHierarchy || EventSystem.current == null)
        {
            return;
        }

        Selectable first = page.GetComponentInChildren<Selectable>();
        EventSystem.current.SetSelectedGameObject(first != null ? first.gameObject : null);
    }

    /// <summary>
    /// ゲームを終了する。
    /// **エディタで遊んでいるときは、再生を止めるだけ**（エディタごと閉じたら困るため）。
    /// </summary>
    public void QuitGame()
    {
        Debug.Log("[UI] ゲームを終了します");

#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    private void SetVisible(bool visible)
    {
        if (root != null)
        {
            root.SetActive(visible);
        }
    }

    // ------------------------------------------------------------
    // 画面を組み立てる
    // ------------------------------------------------------------

    private void Build()
    {
        EnsureEventSystem();

        // ----- 一番外側（画面いっぱいに広がる） -----
        root = new GameObject("PauseCanvas", typeof(RectTransform), typeof(Canvas),
            typeof(CanvasScaler), typeof(GraphicRaycaster));
        root.transform.SetParent(transform, false);

        Canvas canvas = root.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;

        // ほかのUIより手前に出す
        canvas.sortingOrder = 100;

        CanvasScaler scaler = root.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        // ----- 後ろを暗くする板（クリックが後ろへ抜けるのも防ぐ） -----
        GameObject backdrop = CreateImage("Backdrop", root.transform, backdropColor);
        Stretch(backdrop.GetComponent<RectTransform>());

        mainPage = CreatePage("MainPage", "ポーズ");
        settingsPage = CreatePage("SettingsPage", "設定");

        BuildMainPage();
        BuildSettingsPage();
    }

    /// <summary>1ページ分の入れ物を作る（見出し付き）。</summary>
    private GameObject CreatePage(string name, string title)
    {
        GameObject page = new GameObject(name, typeof(RectTransform));
        page.transform.SetParent(root.transform, false);
        Stretch(page.GetComponent<RectTransform>());

        GameObject titleObject = CreateText("Title", page.transform, title, fontSize + 12);
        RectTransform titleRect = titleObject.GetComponent<RectTransform>();
        titleRect.anchorMin = new Vector2(0.5f, 0.5f);
        titleRect.anchorMax = new Vector2(0.5f, 0.5f);
        titleRect.sizeDelta = new Vector2(ItemWidth, ItemHeight);
        titleRect.anchoredPosition = new Vector2(0f, 190f);

        return page;
    }

    private void BuildMainPage()
    {
        float y = 90f;
        CreateMenuItem(mainPage.transform, "ゲームをつづける", y, Close);

        y -= ItemHeight + ItemGap;
        CreateMenuItem(mainPage.transform, "設定", y, () => ShowSettings(true));

        // 試合中だけ「ロビーに戻る」を出す（宇宙ごみ。釣りなどには出ない）
        if (IsInMatch())
        {
            y -= ItemHeight + ItemGap;
            CreateMenuItem(mainPage.transform, "ロビーに戻る", y, ConfirmReturnToLobby);
        }

        y -= ItemHeight + ItemGap;
        CreateMenuItem(mainPage.transform, "ゲームをやめる", y, ConfirmQuit);
    }

    /// <summary>最初の画面を作り直す（「ロビーに戻る」を出すかどうかを、いまの状況に合わせる）。</summary>
    private void RebuildMainPage()
    {
        if (mainPage != null)
        {
            // Destroy はフレームの終わりに消えるので、先に隠して、新しい画面と重ならないようにする
            mainPage.SetActive(false);
            Destroy(mainPage);
        }

        mainPage = CreatePage("MainPage", "ポーズ");
        BuildMainPage();
    }

    private void BuildSettingsPage()
    {
        CreateSlider(settingsPage.transform, "音量", 90f, GameSettings.Volume, 0f, 1f,
            value => GameSettings.Volume = value);

        CreateSlider(settingsPage.transform, "マウス感度", 90f - 96f, GameSettings.MouseSensitivity, 0.2f, 3f,
            value => GameSettings.MouseSensitivity = value);

        CreateMenuItem(settingsPage.transform, ControllerOperationLabel(), 90f - (ItemHeight + ItemGap) * 2f,
            ToggleControllerOperation);

        CreateMenuItem(settingsPage.transform, "もどる", 90f - (ItemHeight + ItemGap) * 3f,
            () => ShowSettings(false));
    }

    private static string ControllerOperationLabel()
    {
        return GameSettings.ControllerOperation == ControllerOperationType.TypeB
            ? "操作タイプ：タイプB"
            : "操作タイプ：タイプA";
    }

    private void ToggleControllerOperation()
    {
        GameSettings.ControllerOperation = GameSettings.ControllerOperation == ControllerOperationType.TypeA
            ? ControllerOperationType.TypeB
            : ControllerOperationType.TypeA;
        BuildSettingsPageRefresh();
    }

    // 表示中の設定ページを作り直し、切り替えた値をすぐに見せる。
    private void BuildSettingsPageRefresh()
    {
        settingsPage.SetActive(false);
        Destroy(settingsPage);
        settingsPage = CreatePage("SettingsPage", "設定");
        BuildSettingsPage();
        settingsPage.SetActive(true);
        SelectFirstForGamepad(settingsPage);
    }

    /// <summary>押せる項目を1つ作る。マウスが重なったときの見せ方もここで付ける。</summary>
    private void CreateMenuItem(Transform parent, string label, float y, UnityEngine.Events.UnityAction onClick)
    {
        GameObject item = CreateImage(label, parent, itemColor);

        RectTransform rect = item.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = new Vector2(ItemWidth, ItemHeight);
        rect.anchoredPosition = new Vector2(0f, y);

        Button button = item.AddComponent<Button>();
        button.targetGraphic = item.GetComponent<Image>();

        // 色は自分で変えるので、ボタンの色替えは切っておく（二重に変わると濁る）
        button.transition = Selectable.Transition.None;
        button.onClick.AddListener(onClick);

        CreateText("Label", item.transform, label, fontSize);

        // マウスが重なったときに出る印
        GameObject marker = CreateText("Marker", item.transform, "▶", fontSize);
        RectTransform markerRect = marker.GetComponent<RectTransform>();
        markerRect.anchorMin = new Vector2(0f, 0.5f);
        markerRect.anchorMax = new Vector2(0f, 0.5f);
        markerRect.sizeDelta = new Vector2(40f, ItemHeight);
        markerRect.anchoredPosition = new Vector2(26f, 0f);
        marker.SetActive(false);

        MenuHoverHighlight highlight = item.AddComponent<MenuHoverHighlight>();
        highlight.Setup(item.GetComponent<Image>(), itemColor, itemHoverColor, marker);
    }

    /// <summary>設定用のスライダーを1本作る。</summary>
    private void CreateSlider(Transform parent, string label, float y, float value,
        float min, float max, UnityEngine.Events.UnityAction<float> onChanged)
    {
        GameObject row = new GameObject(label, typeof(RectTransform));
        row.transform.SetParent(parent, false);

        RectTransform rowRect = row.GetComponent<RectTransform>();
        rowRect.anchorMin = new Vector2(0.5f, 0.5f);
        rowRect.anchorMax = new Vector2(0.5f, 0.5f);
        rowRect.sizeDelta = new Vector2(ItemWidth, ItemHeight);
        rowRect.anchoredPosition = new Vector2(0f, y);

        GameObject labelObject = CreateText("Label", row.transform, label, fontSize - 4);
        RectTransform labelRect = labelObject.GetComponent<RectTransform>();
        labelRect.anchorMin = new Vector2(0f, 0.5f);
        labelRect.anchorMax = new Vector2(0f, 0.5f);
        labelRect.sizeDelta = new Vector2(180f, ItemHeight);
        labelRect.anchoredPosition = new Vector2(100f, 0f);
        labelObject.GetComponent<Text>().alignment = TextAnchor.MiddleLeft;

        // ----- スライダー本体 -----
        GameObject sliderObject = new GameObject("Slider", typeof(RectTransform), typeof(Slider));
        sliderObject.transform.SetParent(row.transform, false);

        RectTransform sliderRect = sliderObject.GetComponent<RectTransform>();
        sliderRect.anchorMin = new Vector2(1f, 0.5f);
        sliderRect.anchorMax = new Vector2(1f, 0.5f);
        sliderRect.sizeDelta = new Vector2(200f, 16f);
        sliderRect.anchoredPosition = new Vector2(-110f, 0f);

        GameObject background = CreateImage("Background", sliderObject.transform, itemColor);
        Stretch(background.GetComponent<RectTransform>());

        GameObject fillArea = new GameObject("Fill Area", typeof(RectTransform));
        fillArea.transform.SetParent(sliderObject.transform, false);
        Stretch(fillArea.GetComponent<RectTransform>());

        GameObject fill = CreateImage("Fill", fillArea.transform, itemHoverColor);
        RectTransform fillRect = fill.GetComponent<RectTransform>();
        fillRect.anchorMin = Vector2.zero;
        fillRect.anchorMax = new Vector2(0f, 1f);
        fillRect.sizeDelta = new Vector2(10f, 0f);

        GameObject handle = CreateImage("Handle", sliderObject.transform, Color.white);
        RectTransform handleRect = handle.GetComponent<RectTransform>();
        handleRect.sizeDelta = new Vector2(20f, 32f);

        Slider slider = sliderObject.GetComponent<Slider>();
        slider.fillRect = fillRect;
        slider.handleRect = handleRect;
        slider.targetGraphic = handle.GetComponent<Image>();
        slider.minValue = min;
        slider.maxValue = max;
        slider.value = value;
        slider.onValueChanged.AddListener(onChanged);
    }

    // ------------------------------------------------------------
    // 部品づくり
    // ------------------------------------------------------------

    private static GameObject CreateImage(string name, Transform parent, Color color)
    {
        GameObject image = new GameObject(name, typeof(RectTransform), typeof(Image));
        image.transform.SetParent(parent, false);
        image.GetComponent<Image>().color = color;

        return image;
    }

    private GameObject CreateText(string name, Transform parent, string content, int size)
    {
        GameObject textObject = new GameObject(name, typeof(RectTransform), typeof(Text));
        textObject.transform.SetParent(parent, false);

        Text text = textObject.GetComponent<Text>();
        text.text = content;
        text.font = font;
        text.fontSize = size;
        text.color = textColor;
        text.alignment = TextAnchor.MiddleCenter;

        // 文字はクリックの邪魔をしない（下のボタンに通す）
        text.raycastTarget = false;

        Stretch(textObject.GetComponent<RectTransform>());

        return textObject;
    }

    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

    /// <summary>
    /// **パソコンに入っている日本語フォントを借りてくる。**
    ///
    /// Unityに最初から入っている文字は日本語を持っていないので、
    /// そのままだと項目名が四角（□□□）になってしまう。
    /// </summary>
    private Font FindFont()
    {
        return UiFont.Find(fontSize, fontNames);
    }

    /// <summary>
    /// **UIのクリックを受け取る仕組みがなければ作る。**
    ///
    /// このプロジェクトは新しい入力方式（Input System）を使っているので、
    /// 古い受け取り方（`StandaloneInputModule`）ではエラーになる。
    ///
    /// ## シーンが変わっても残るようにしてある（2026/9/20 修正）
    ///
    /// 以前はここで作った `EventSystem` に `DontDestroyOnLoad` を付けていなかった。
    /// ポーズ画面そのものは残るのに、**クリックを受け取る係だけがシーンと一緒に消える**ため、
    /// オンラインで会場へ移ったあと、**ポーズ画面は開くのにボタンが反応しない**状態になっていた
    /// （ロビーでは同じシーンにいるので気づけない）。
    ///
    /// 作り直せるよう、**開くたびに呼ぶ**ようにもしてある。
    /// シーンに置かれていた `EventSystem` が消えた場合にも、ここで作り直される。
    /// </summary>
    private void EnsureEventSystem()
    {
        if (EventSystem.current != null)
        {
            return;
        }

        GameObject eventSystem = new GameObject("EventSystem",
            typeof(EventSystem), typeof(InputSystemUIInputModule));

        // **ポーズ画面と一緒に生き残らせる。** これが無いと、
        // シーンを切り替えた先でボタンが押せなくなる
        DontDestroyOnLoad(eventSystem);

        InputSystemUIInputModule module = eventSystem.GetComponent<InputSystemUIInputModule>();

        if (inputActions != null)
        {
            // 入れてあれば、そこにある「UI」の設定を使う
            module.actionsAsset = inputActions;
        }
        else
        {
            // **入れ忘れ・自動で作ったときの手当て。**
            // 何も入っていないと、クリックが届かない
            module.AssignDefaultActions();
        }

        Debug.Log("[UI] UIのクリックを受け取る EventSystem を作りました");
    }
}
