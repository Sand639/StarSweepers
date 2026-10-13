using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

/// <summary>
/// **狙いをつけなくても、フックが物資へ飛んでいくようにする係（オートエイム）。**
///
/// 狙い方は3つあり、十字キーで切り替える。
///
/// | キー | 狙い方 | フックの飛び方 |
/// | --- | --- | --- |
/// | 十字キー左（キーボード 1） | **近くを狙う** | 届く範囲で一番近い物資へ飛ぶ |
/// | 十字キー上（キーボード 2） | **手動**（今までどおり） | マウスの方向へ飛ぶ |
/// | 十字キー右（キーボード 3） | **遠くを狙う** | 届く範囲で一番遠い物資へ飛ぶ |
///
/// ※ キーボードの矢印キーは移動（Move）に使われているので、代わりに 1・2・3 にしている。
///
/// ・画面左下に、いまの狙い方を出す（自分の画面だけ）
/// ・狙っている物資の上に**黄緑の逆三角の目印**を出す。
///   **オンラインでは他の人の目印も見える**（狙っている物資を <see cref="FishingNetPlayer"/> が配る）
///
/// 実際にフックを飛ばすのは <see cref="HookController"/>。
/// こちらは「どれを狙うか」と「見せ方」だけを持つ。
///
/// **プレイヤーに付いていなければ、<see cref="HookController"/> が自動で付ける**ので、
/// 既存のシーン・プレハブを作り直さなくても動く。
/// </summary>
public class HookAimAssist : MonoBehaviour
{
    /// <summary>狙い方。</summary>
    public enum AimMode
    {
        Manual,     // 手動（マウスの方向へ飛ぶ。今までの動き）
        Nearest,    // 近くの物資を狙う
        Farthest    // 遠くの物資を狙う
    }

    [Header("狙い方")]
    [Tooltip("始まったときの狙い方")]
    [SerializeField] private AimMode startMode = AimMode.Manual;

    [Tooltip("爆発物も狙うか。OFFにすると普通の物資だけを狙う")]
    [SerializeField] private bool includeExplosives = true;

    [Header("キーの割り当て（Input System の書き方）")]
    [Tooltip("「近くを狙う」に切り替えるキー")]
    [SerializeField] private string[] nearestBindings = { "<Gamepad>/dpad/left", "<Keyboard>/1" };

    [Tooltip("「手動」に切り替えるキー")]
    [SerializeField] private string[] manualBindings = { "<Gamepad>/dpad/up", "<Keyboard>/2" };

    [Tooltip("「遠くを狙う」に切り替えるキー")]
    [SerializeField] private string[] farthestBindings = { "<Gamepad>/dpad/right", "<Keyboard>/3" };

    [Header("表示")]
    [Tooltip("左下に狙い方の表示を出すか")]
    [SerializeField] private bool showPanel = true;

    [Tooltip("狙っている物資の上に目印を出すか（デバッグ用）")]
    [SerializeField] private bool showMarker = true;

    [Tooltip("表示の大きさ（1920×1080 のときの倍率。窓が小さいと自動で縮む）")]
    [Range(1f, 4f)]
    [SerializeField] private float uiScale = 1.6f;

    [Tooltip("目印の幅（倍率をかける前の大きさ）")]
    [SerializeField] private float markerSize = 18f;

    [Tooltip("物資のてっぺんから、どれだけ上に目印を出すか（メートル）")]
    [SerializeField] private float markerHeight = 0.4f;

    /// <summary>目印と、選んでいる狙い方の色（黄緑）。</summary>
    private static readonly Color MarkColor = new Color(0.6f, 1f, 0.2f);

    private HookController hook;
    private FishingNetPlayer netPlayer;
    private PlayerInputSource input;

    private InputAction nearestAction;
    private InputAction manualAction;
    private InputAction farthestAction;

    /// <summary>自分が操作しているときに、いま狙っている物資。</summary>
    private HookableObject localTarget;

    /// <summary>いまの狙い方。</summary>
    public AimMode Mode { get; private set; }

    /// <summary>
    /// **このPCで操作しているプレイヤーか。**
    /// 1人用では常に true。オンラインでは自分のプレイヤーだけ true（他の人のぶんは目印を出すだけ）。
    /// </summary>
    public bool IsLocallyControlled
    {
        get
        {
            if (netPlayer == null)
            {
                return true;
            }
            return netPlayer.IsSpawned && netPlayer.IsOwner;
        }
    }

    /// <summary>
    /// 目印を出す物資。自分のプレイヤーなら手元で選んだもの、
    /// 他の人のプレイヤーなら、その人から送られてきたもの。
    /// </summary>
    public HookableObject DisplayTarget
    {
        get
        {
            if (IsLocallyControlled)
            {
                return localTarget;
            }
            return netPlayer != null && netPlayer.IsSpawned ? netPlayer.AimTarget : null;
        }
    }

    private void Awake()
    {
        hook = GetComponent<HookController>();
        netPlayer = GetComponent<FishingNetPlayer>();
        Mode = startMode;
        input = PlayerInputSource.Get(this);

        // 入力はこの体専用に作る（アセットを共有しないので、他の人のぶんと干渉しない）
        nearestAction = MakeAction("AimNearest", nearestBindings);
        manualAction = MakeAction("AimManual", manualBindings);
        farthestAction = MakeAction("AimFarthest", farthestBindings);
    }

    private static InputAction MakeAction(string actionName, string[] bindings)
    {
        InputAction action = new InputAction(actionName, InputActionType.Button);

        if (bindings != null)
        {
            foreach (string path in bindings)
            {
                if (!string.IsNullOrEmpty(path))
                {
                    action.AddBinding(path);
                }
            }
        }

        return action;
    }

    private void OnEnable()
    {
        nearestAction?.Enable();
        manualAction?.Enable();
        farthestAction?.Enable();
    }

    private void OnDisable()
    {
        nearestAction?.Disable();
        manualAction?.Disable();
        farthestAction?.Disable();
    }

    private void OnDestroy()
    {
        nearestAction?.Dispose();
        manualAction?.Dispose();
        farthestAction?.Dispose();
    }

    private void Update()
    {
        // 他の人のプレイヤーは、送られてきた目印を出すだけ（OnGUI）
        if (!IsLocallyControlled)
        {
            return;
        }

        if (!GamePause.IsPaused)
        {
            ReadModeInput();
        }

        UpdateLocalTarget();

        // オンラインなら、狙っている物資を他の人へ配る
        if (netPlayer != null)
        {
            netPlayer.SetAimTarget(localTarget);
        }
    }

    private void ReadModeInput()
    {
        // 1台で複数人（席で読む）ときは、その席のキーボードの 1・2・3 とコントローラの十字キーだけを見る
        if (input.UsesSeat)
        {
            if (input.WasPressed(Key.Digit1, GamepadButton.DpadLeft))
            {
                Mode = AimMode.Nearest;
            }
            else if (input.WasPressed(Key.Digit2, GamepadButton.DpadUp))
            {
                Mode = AimMode.Manual;
            }
            else if (input.WasPressed(Key.Digit3, GamepadButton.DpadRight))
            {
                Mode = AimMode.Farthest;
            }
            return;
        }

        if (nearestAction.WasPressedThisFrame())
        {
            Mode = AimMode.Nearest;
        }
        else if (manualAction.WasPressedThisFrame())
        {
            Mode = AimMode.Manual;
        }
        else if (farthestAction.WasPressedThisFrame())
        {
            Mode = AimMode.Farthest;
        }
    }

    /// <summary>フックの段階に合わせて、目印を出す物資を決める。</summary>
    private void UpdateLocalTarget()
    {
        if (Mode == AimMode.Manual || hook == null)
        {
            localTarget = null;
            return;
        }

        switch (hook.Phase)
        {
            case HookController.HookPhase.Flying:
                // 飛んでいる間は、発射したときに決めた相手のまま（途中で切り替えない）
                localTarget = hook.AutoTarget;
                break;

            case HookController.HookPhase.Attached:
                // 引っ掛けたあとは狙いの目印を出さない
                localTarget = null;
                break;

            default:
                // 待機・チャージ・戻り中は、「いま撃ったらどれに飛ぶか」を出しておく
                localTarget = PickTarget();
                break;
        }
    }

    /// <summary>
    /// **いまの狙い方で、狙う物資を選ぶ。** 手動のとき・届く範囲に無いときは null。
    /// <see cref="HookController"/> が発射する瞬間にも呼ぶ（1フレーム古い結果を使わないため）。
    /// </summary>
    public HookableObject PickTarget()
    {
        if (Mode == AimMode.Manual || hook == null)
        {
            return null;
        }

        Vector3 origin = transform.position;
        float rangeSqr = hook.MaxRange * hook.MaxRange;

        HookableObject best = null;
        float bestSqr = 0f;

        foreach (HookableObject item in HookableObject.All)
        {
            if (!IsTargetable(item))
            {
                continue;
            }

            // 距離は水平で測る（フックの飛距離と同じ測り方）
            Vector3 flat = item.transform.position - origin;
            flat.y = 0f;
            float distanceSqr = flat.sqrMagnitude;

            if (distanceSqr > rangeSqr)
            {
                continue;
            }

            bool better = best == null
                || (Mode == AimMode.Nearest ? distanceSqr < bestSqr : distanceSqr > bestSqr);

            if (better)
            {
                best = item;
                bestSqr = distanceSqr;
            }
        }

        return best;
    }

    /// <summary>狙ってよい物資か。消えている・引っ掛けられている物は狙わない。</summary>
    private bool IsTargetable(HookableObject item)
    {
        if (item == null || !item.isActiveAndEnabled || item.IsVanished || item.IsHooked)
        {
            return false;
        }

        if (!includeExplosives && item.GetComponent<ExplosiveObject>() != null)
        {
            return false;
        }

        // オンラインで、他の人が引き寄せている物資は狙わない
        FishingNetSupply netSupply = item.GetComponent<FishingNetSupply>();
        if (netSupply != null && netSupply.IsSpawned && netSupply.IsClaimed)
        {
            return false;
        }

        return true;
    }

    /// <summary>フックが向かう先（物資の当たり判定の中心）。</summary>
    public static Vector3 AimPointOf(HookableObject item)
    {
        Collider collider = item.GetComponent<Collider>();
        return collider != null && collider.enabled ? collider.bounds.center : item.transform.position;
    }

    // ------------------------------------------------------------
    // 表示
    // ------------------------------------------------------------

    private GUIStyle titleStyle;
    private GUIStyle cellStyle;
    private GUIStyle tagStyle;

    private void OnGUI()
    {
        Matrix4x4 savedMatrix = GUI.matrix;
        Color savedColor = GUI.color;

        PrepareStyles();

        if (showMarker)
        {
            GUI.matrix = Matrix4x4.identity;
            DrawMarker();
        }

        // 狙い方の表示は画面に1つ。1台で複数人のときは、そのPCの1人目のぶんだけ出す（重なって読めなくなるため）
        bool firstLocal = netPlayer == null || netPlayer.LocalSeat == 0;
        if (showPanel && IsLocallyControlled && firstLocal && hook != null)
        {
            DrawPanel();
        }

        GUI.matrix = savedMatrix;
        GUI.color = savedColor;
    }

    private void PrepareStyles()
    {
        if (titleStyle == null)
        {
            titleStyle = new GUIStyle(GUI.skin.label) { fontSize = 12, alignment = TextAnchor.MiddleCenter };
        }
        if (cellStyle == null)
        {
            cellStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 13,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter
            };
        }
        if (tagStyle == null)
        {
            tagStyle = new GUIStyle(GUI.skin.label) { fontSize = 11, fontStyle = FontStyle.Bold };
        }
    }

    /// <summary>
    /// 左下に、十字キーの形で狙い方を並べる。いま選んでいるものが黄緑になる。
    ///
    ///          [↑ 手動]
    ///   [← 近く]      [遠く →]
    /// </summary>
    private void DrawPanel()
    {
        float scale = DraggableGuiPanel.FitScale(uiScale);
        GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));

        float viewHeight = Screen.height / scale;

        const float cellWidth = 70f;
        const float cellHeight = 24f;
        const float gap = 4f;
        float panelWidth = cellWidth * 2f + gap * 3f + 10f;
        float panelHeight = 20f + cellHeight * 2f + gap * 3f;

        // 一番下の行は操作説明（FishingStatusUI）が使っているので、その上に出す
        float left = 10f;
        float top = viewHeight - 34f - panelHeight;

        GUI.color = new Color(0f, 0f, 0f, 0.55f);
        GUI.DrawTexture(new Rect(left, top, panelWidth, panelHeight), Texture2D.whiteTexture);

        GUI.color = Color.white;
        GUI.Label(new Rect(left, top + 2f, panelWidth, 18f), "フックの狙い（十字キー / 1・2・3）", titleStyle);

        float centerX = left + panelWidth * 0.5f;
        float row1 = top + 20f + gap;
        float row2 = row1 + cellHeight + gap;

        DrawCell(new Rect(centerX - cellWidth * 0.5f, row1, cellWidth, cellHeight), "↑ 手動", Mode == AimMode.Manual);
        DrawCell(new Rect(centerX - gap * 0.5f - cellWidth, row2, cellWidth, cellHeight), "← 近く", Mode == AimMode.Nearest);
        DrawCell(new Rect(centerX + gap * 0.5f, row2, cellWidth, cellHeight), "遠く →", Mode == AimMode.Farthest);
    }

    private void DrawCell(Rect rect, string text, bool selected)
    {
        GUI.color = selected ? MarkColor : new Color(0.25f, 0.25f, 0.25f, 0.9f);
        GUI.DrawTexture(rect, Texture2D.whiteTexture);

        GUI.color = selected ? Color.black : new Color(1f, 1f, 1f, 0.7f);
        GUI.Label(rect, text, cellStyle);
        GUI.color = Color.white;
    }

    /// <summary>
    /// 狙っている物資の上に、黄緑の逆三角を出す。
    /// **何人かが同じ物資を狙っているときは、上へ積み重ねて**全員ぶん見えるようにする。
    /// </summary>
    private void DrawMarker()
    {
        HookableObject target = DisplayTarget;
        if (target == null || target.IsVanished)
        {
            return;
        }

        Camera camera = Camera.main;
        if (camera == null)
        {
            return;
        }

        Collider collider = target.GetComponent<Collider>();
        Vector3 top = collider != null && collider.enabled
            ? new Vector3(collider.bounds.center.x, collider.bounds.max.y, collider.bounds.center.z)
            : target.transform.position;

        Vector3 screen = camera.WorldToScreenPoint(top + Vector3.up * markerHeight);
        if (screen.z <= 0f)
        {
            return;
        }

        float scale = DraggableGuiPanel.FitScale(uiScale);
        float width = markerSize * scale;
        float height = width * 0.85f;
        float bob = Mathf.Abs(Mathf.Sin(Time.time * 5f)) * 4f * scale;
        int stack = StackIndexOnTarget(target);

        float x = screen.x - width * 0.5f;
        float y = Screen.height - screen.y - height - bob - stack * (height + 3f * scale);

        GUI.color = Color.white;
        GUI.DrawTexture(new Rect(x, y, width, height), MarkerTexture);

        // オンラインでは、誰の目印か分かるように番号を添える
        if (netPlayer != null && netPlayer.IsSpawned && netPlayer.PlayerIndex >= 0)
        {
            GUI.color = MarkColor;
            GUI.Label(new Rect(x + width + 2f, y - 2f, 40f, height + 4f), $"P{netPlayer.PlayerIndex + 1}", tagStyle);
            GUI.color = Color.white;
        }
    }

    /// <summary>同じ物資を狙っている人のうち、自分より番号の小さい人が何人いるか（目印を積む段数）。</summary>
    private int StackIndexOnTarget(HookableObject target)
    {
        if (netPlayer == null)
        {
            return 0;
        }

        int count = 0;

        foreach (FishingNetPlayer other in FishingNetPlayer.All)
        {
            if (other == null || other == netPlayer || other.PlayerIndex >= netPlayer.PlayerIndex)
            {
                continue;
            }

            HookAimAssist otherAssist = other.GetComponent<HookAimAssist>();
            if (otherAssist != null && otherAssist.DisplayTarget == target)
            {
                count++;
            }
        }

        return count;
    }

    // ---- 逆三角の絵（素材を用意しなくて済むように、コードで1回だけ作る）----

    private static Texture2D markerTexture;

    private static Texture2D MarkerTexture
    {
        get
        {
            if (markerTexture != null)
            {
                return markerTexture;
            }

            const int size = 64;
            const float edge = 0.08f; // ふちの太さ（絵の大きさに対する割合）

            Color32 fill = MarkColor;
            Color32 outline = new Color32(20, 45, 5, 255);
            Color32 clear = new Color32(0, 0, 0, 0);
            Color32[] pixels = new Color32[size * size];

            for (int py = 0; py < size; py++)
            {
                // v … 0が下（とがった先）、1が上（平らな辺）
                float v = (py + 0.5f) / size;

                for (int px = 0; px < size; px++)
                {
                    float u = (px + 0.5f) / size;
                    float halfWidth = v * 0.5f;
                    float dx = Mathf.Abs(u - 0.5f);

                    Color32 color = clear;

                    if (dx <= halfWidth)
                    {
                        // ななめの辺までの距離（1.118 は辺の傾きの補正）と、上の辺までの距離
                        bool inside = (halfWidth - dx) / 1.118f >= edge && v <= 1f - edge;
                        color = inside ? fill : outline;
                    }

                    pixels[py * size + px] = color;
                }
            }

            markerTexture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave
            };
            markerTexture.SetPixels32(pixels);
            markerTexture.Apply();

            return markerTexture;
        }
    }
}
