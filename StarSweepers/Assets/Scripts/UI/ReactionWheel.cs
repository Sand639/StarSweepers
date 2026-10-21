using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// **8分割のリアクションホイール。**（試作・2026/10/21。本実装するかは未定）
///
/// ## 使い方（遊ぶ人）
///
/// 1. **マウスのホイール（中ボタン）か、コントローラーの RB を長押し**すると、画面の真ん中に円が出る
/// 2. 押したまま**マウスを動かした方向**か、**右スティックを倒した方向**の項目が選ばれる（真ん中に小さな点が動く）
/// 3. **離すと**、選んだリアクションを全員に知らせる（<see cref="ReactionRelay"/>）
///    - マウス：真ん中のあたりで離すと、何も出さずに閉じる
///    - コントローラー：スティックを戻しても選んだものは残る。**B で取り消し**
///
/// ## 置き方
///
/// ふつうは **`ReactionSystem.prefab`** の中にある（<see cref="ReactionRelay"/> が選んだものを全員に知らせ、
/// 画面の端に「名前＋スタンプ」を出す）。
/// **Bubble** に、頭の上に出す係（プレイヤーに付けた <see cref="ReactionBubble"/>）を渡すと、頭の上にも出す（検証シーン用）。
/// 円の見た目は再生したときにこのスクリプトが作るので、Canvas などを置く必要はない。
///
/// ## まだやっていないこと
///
/// - 開いている間も、マウスや右スティックで狙いが動く（<see cref="IsOpen"/> を見て止めてもらう必要がある）
/// - 1台のPCで複数人（席ごとの入力）には対応していない（どのコントローラーの RB でも開く）
/// </summary>
public class ReactionWheel : MonoBehaviour
{
    /// <summary>ホイールの分かれ目の数。</summary>
    public const int SegmentCount = 8;

    /// <summary>どれかのホイールが開いているか。カメラや狙いの操作を止めたいときに見る。</summary>
    public static bool IsOpen { get; private set; }

    /// <summary>リアクションを選んだときに呼ばれる（中身は項目の番号 0〜7。0 が真上で、時計回り）。</summary>
    public event Action<int> Chosen;

    [Tooltip("選んだリアクションを頭の上に出す係（プレイヤーに付けた ReactionBubble）")]
    [SerializeField] private ReactionBubble bubble;

    [Tooltip("8つのリアクション。0 が真上で、時計回りに並ぶ")]
    [SerializeField] private ReactionEntry[] entries =
    {
        new ReactionEntry("いいね", new Color(0.25f, 0.65f, 1f)),
        new ReactionEntry("ナイス！", new Color(1f, 0.75f, 0.15f)),
        new ReactionEntry("こっち！", new Color(0.3f, 0.85f, 0.45f)),
        new ReactionEntry("ありがと", new Color(1f, 0.5f, 0.7f)),
        new ReactionEntry("助けて！", new Color(0.95f, 0.3f, 0.25f)),
        new ReactionEntry("ごめん", new Color(0.6f, 0.55f, 0.9f)),
        new ReactionEntry("笑", new Color(1f, 0.6f, 0.25f)),
        new ReactionEntry("？", new Color(0.55f, 0.6f, 0.65f)),
    };

    [Header("操作")]
    [Tooltip("何秒押し続けたら開くか（短く押しただけでは開かない）")]
    [SerializeField] private float holdSeconds = 0.15f;

    [Tooltip("マウスを動かした量の何倍で、真ん中の点を動かすか")]
    [SerializeField] private float pointerSensitivity = 1f;

    [Tooltip("右スティックをどれだけ倒したら選ぶか（0〜1）")]
    [Range(0.1f, 0.95f)]
    [SerializeField] private float stickThreshold = 0.5f;

    [Tooltip("真ん中の何割までを「選んでいない」とするか（円の半径に対する割合）")]
    [Range(0.05f, 0.9f)]
    [SerializeField] private float deadZone = 0.3f;

    [Header("見た目")]
    [Tooltip("円の半径（1920x1080 の画面での大きさ）")]
    [SerializeField] private float radius = 300f;

    [Tooltip("区切りのすき間（度）")]
    [SerializeField] private float gapDegrees = 2f;

    [SerializeField] private Color segmentColor = new Color(0.08f, 0.1f, 0.14f, 0.75f);
    [SerializeField] private Color selectedColor = new Color(1f, 1f, 1f, 0.35f);

    private GameObject wheelRoot;
    private RectTransform wheelRect;
    private readonly Image[] segments = new Image[SegmentCount];
    private readonly RectTransform[] iconRects = new RectTransform[SegmentCount];
    private Text centerLabel;
    private RectTransform pointerDot;

    private bool open;
    private float pressedAt = -1f;
    private float openedAt;

    /// <summary>B で取り消した。ボタンを離すまで開き直さない。</summary>
    private bool cancelled;
    private Vector2 pointer;
    private int selected = -1;

    private void Awake()
    {
        BuildWheel();
        wheelRoot.SetActive(false);
    }

    private void OnDisable()
    {
        Close();
        pressedAt = -1f;
        cancelled = false;
    }

    /// <summary>i 番目のリアクション。番号が範囲の外なら null。</summary>
    public ReactionEntry EntryAt(int index)
    {
        return index >= 0 && index < entries.Length ? entries[index] : null;
    }

    private void Update()
    {
        Mouse mouse = Mouse.current;
        Gamepad pad = Gamepad.current;

        // ポーズ画面などのメニューが開いている間は使わない（開いていたら閉じる）
        if (GamePause.BlocksInput || UiPointer.MenuActive)
        {
            Close();
            pressedAt = -1f;
            return;
        }

        // マウスのホイール（中ボタン）か、コントローラーの RB。どちらで押してもよい
        bool mouseHeld = mouse != null && mouse.middleButton.isPressed;
        bool padHeld = pad != null && pad.rightShoulder.isPressed;
        bool held = mouseHeld || padHeld;

        if (held && pressedAt < 0f)
        {
            pressedAt = Time.unscaledTime;
        }

        if (held)
        {
            if (!open && !cancelled && Time.unscaledTime - pressedAt >= holdSeconds)
            {
                Open();
            }

            if (open)
            {
                if (mouse != null)
                {
                    MovePointer(mouse.delta.ReadValue());
                }

                if (pad != null)
                {
                    AimWithStick(pad.rightStick.ReadValue());

                    // B で取り消し（何も出さずに閉じる。RB を離すまで開き直さない）
                    if (pad.buttonEast.wasPressedThisFrame)
                    {
                        Close();
                        cancelled = true;
                    }
                }
            }
        }
        else if (pressedAt >= 0f)
        {
            // 離した。開いていたら、選んでいたものを出す
            pressedAt = -1f;
            cancelled = false;
            if (open)
            {
                int choice = selected;
                Close();
                Choose(choice);
            }
        }

        if (open)
        {
            AnimateOpen();
        }
    }

    private void Open()
    {
        open = true;
        IsOpen = true;
        openedAt = Time.unscaledTime;
        pointer = Vector2.zero;
        wheelRoot.SetActive(true);
        Select(-1);
    }

    private void Close()
    {
        if (open)
        {
            IsOpen = false;
        }

        open = false;
        if (wheelRoot != null)
        {
            wheelRoot.SetActive(false);
        }
    }

    private void Choose(int index)
    {
        if (index < 0 || index >= entries.Length)
        {
            return;
        }

        if (bubble != null)
        {
            bubble.Show(entries[index]);
        }

        Chosen?.Invoke(index);
    }

    /// <summary>
    /// マウスの動きで真ん中の点を動かし、点のある方向の項目を選ぶ。
    /// マウスの位置ではなく**動いた量**を見るので、カーソルが画面に固定されていても選べる。
    /// </summary>
    private void MovePointer(Vector2 delta)
    {
        pointer = Vector2.ClampMagnitude(pointer + delta * pointerSensitivity, radius);
        SelectByPointer();
    }

    /// <summary>
    /// 右スティックを倒した方向の項目を選ぶ。
    /// **スティックを戻しても選んだものは残る**（スティックを離してから RB を離すことが多いため）。
    /// </summary>
    private void AimWithStick(Vector2 stick)
    {
        if (stick.magnitude < stickThreshold)
        {
            return;
        }

        pointer = stick.normalized * radius * 0.8f;
        SelectByPointer();
    }

    /// <summary>点のある方向の項目を選ぶ。真ん中に近ければ、何も選ばない。</summary>
    private void SelectByPointer()
    {
        pointerDot.anchoredPosition = pointer;

        if (pointer.magnitude < radius * deadZone)
        {
            Select(-1);
            return;
        }

        // 真上を 0 度として時計回りの角度にし、45度ずつに分ける（真上の項目は -22.5〜22.5 度）
        float angle = Mathf.Atan2(pointer.x, pointer.y) * Mathf.Rad2Deg;
        float step = 360f / SegmentCount;
        int index = Mathf.FloorToInt(Mathf.Repeat(angle + step * 0.5f, 360f) / step) % SegmentCount;
        Select(index);
    }

    private void Select(int index)
    {
        selected = index < entries.Length ? index : -1;

        for (int i = 0; i < SegmentCount; i++)
        {
            bool isSelected = i == selected;
            segments[i].color = isSelected ? selectedColor : segmentColor;
            iconRects[i].localScale = Vector3.one * (isSelected ? 1.2f : 1f);
        }

        centerLabel.text = selected >= 0 ? entries[selected].Label : "リアクション";
    }

    /// <summary>開いた直後に、少し小さい所から広がる。</summary>
    private void AnimateOpen()
    {
        float t = Mathf.Clamp01((Time.unscaledTime - openedAt) / 0.1f);
        wheelRect.localScale = Vector3.one * Mathf.Lerp(0.85f, 1f, 1f - (1f - t) * (1f - t));
    }

    /// <summary>画面の真ん中に出す円（8つの区切り・アイコン・真ん中の文字・点）を作る。</summary>
    private void BuildWheel()
    {
        wheelRoot = new GameObject("ReactionWheelCanvas", typeof(Canvas), typeof(CanvasScaler));
        wheelRoot.transform.SetParent(transform, false);

        Canvas canvas = wheelRoot.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 500;

        CanvasScaler scaler = wheelRoot.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        GameObject wheel = new GameObject("Wheel", typeof(RectTransform));
        wheel.transform.SetParent(wheelRoot.transform, false);
        wheelRect = (RectTransform)wheel.transform;
        ReactionUi.Center(wheelRect, Vector2.zero, radius * 2f);

        float step = 360f / SegmentCount;
        float fillDegrees = step - gapDegrees;

        for (int i = 0; i < SegmentCount; i++)
        {
            // 丸の画像を「真上から時計回りに fillDegrees 度だけ」塗り、i 番目の向きへ回す。
            // UI の回転はプラスが反時計回りなので、時計回りに回すときはマイナスにする
            Image segment = ReactionUi.CreateImage($"Segment{i}", wheelRect, ReactionUi.CircleSprite, segmentColor);
            segment.type = Image.Type.Filled;
            segment.fillMethod = Image.FillMethod.Radial360;
            segment.fillOrigin = (int)Image.Origin360.Top;
            segment.fillClockwise = true;
            segment.fillAmount = fillDegrees / 360f;
            ReactionUi.Stretch(segment.rectTransform, 0f);
            segment.rectTransform.localEulerAngles = new Vector3(0f, 0f, fillDegrees * 0.5f - i * step);
            segments[i] = segment;
        }

        // 真ん中の穴（ここで離すと取り消し）
        Image hole = ReactionUi.CreateImage("Center", wheelRect, ReactionUi.CircleSprite, new Color(0.02f, 0.03f, 0.05f, 0.9f));
        ReactionUi.Center(hole.rectTransform, Vector2.zero, radius * 2f * 0.36f);

        centerLabel = ReactionUi.CreateText("CenterLabel", wheelRect, 34, Color.white);
        ReactionUi.Center(centerLabel.rectTransform, Vector2.zero, radius * 0.6f);

        // 各項目のアイコン（色の丸＋文字。画像があれば画像）
        float iconDistance = radius * 0.68f;
        float iconDiameter = radius * 0.34f;
        for (int i = 0; i < SegmentCount; i++)
        {
            float rad = i * step * Mathf.Deg2Rad;
            Vector2 position = new Vector2(Mathf.Sin(rad), Mathf.Cos(rad)) * iconDistance;

            GameObject item = new GameObject($"Item{i}", typeof(RectTransform));
            item.transform.SetParent(wheelRect, false);
            RectTransform itemRect = (RectTransform)item.transform;
            ReactionUi.Center(itemRect, position, iconDiameter);
            iconRects[i] = itemRect;

            ReactionEntry entry = i < entries.Length ? entries[i] : null;
            if (entry == null)
            {
                continue;
            }

            Image circle = ReactionUi.CreateImage("Circle", itemRect, ReactionUi.CircleSprite, entry.Color);
            ReactionUi.Stretch(circle.rectTransform, 0f);

            if (entry.Icon != null)
            {
                Image icon = ReactionUi.CreateImage("Icon", itemRect, entry.Icon, Color.white);
                icon.preserveAspect = true;
                ReactionUi.Stretch(icon.rectTransform, iconDiameter * 0.12f);
            }
            else
            {
                Text text = ReactionUi.CreateText("Label", itemRect, 22, Color.white);
                ReactionUi.Stretch(text.rectTransform, 0f);
                text.text = entry.Label;
            }
        }

        // マウスで動かす点（どの方向を選んでいるかが分かるように）
        Image dot = ReactionUi.CreateImage("Pointer", wheelRect, ReactionUi.CircleSprite, Color.white);
        pointerDot = dot.rectTransform;
        ReactionUi.Center(pointerDot, Vector2.zero, 18f);
    }
}
