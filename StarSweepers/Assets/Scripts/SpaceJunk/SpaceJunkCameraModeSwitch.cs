using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

/// <summary>
/// **全体カメラと自陣カメラを、キーで切り替える部品。** 宇宙ごみの STAGE_01〜05 のカメラに付ける（2026/10/6・大槻さん）。
///
/// | モード | 見え方 |
/// | --- | --- |
/// | 全体カメラ（**最初はこちら**） | **ステージ全体が画面に収まる位置**から、北向きに見下ろす。動かない |
/// | 自陣カメラ | 自分のプレイヤーを追いかけ、**自陣が画面の手前に来る**ように回る（<see cref="SpaceJunkTeamFollowCamera"/>） |
///
/// ・**C キー**（コントローラーは **Back**）で切り替える。切り替えるとカメラがなめらかに移る
/// ・切り替えは**自分の画面だけ**（ほかの人のカメラは変わらない）
/// ・全体カメラの位置は、**シーンが始まったときにステージの大きさを測って自動で決める**（ステージの形を変えても直さなくてよい）。
///   プレイヤー・素材・爆弾・列車など、動く物は測る対象に入れない
///
/// 移動（W＝画面の奥）はカメラの向きに合わせて回るので、どちらのモードでも画面どおりに動ける。
/// </summary>
[RequireComponent(typeof(Camera))]
public class SpaceJunkCameraModeSwitch : MonoBehaviour
{
    /// <summary>カメラのモード。</summary>
    public enum Mode
    {
        /// <summary>ステージ全体を映す</summary>
        WholeStage = 0,

        /// <summary>自陣が手前に来るように、自分を追いかける</summary>
        Team = 1,
    }

    [Header("切り替え")]
    [Tooltip("始まったときのモード")]
    [SerializeField] private Mode startMode = Mode.WholeStage;

    [Tooltip("切り替えるキー")]
    [SerializeField] private Key toggleKey = Key.C;

    [Tooltip("切り替えるコントローラーのボタン")]
    [SerializeField] private GamepadButton toggleButton = GamepadButton.Select;

    [Header("全体カメラ")]
    [Tooltip("見下ろす角度（度）")]
    [SerializeField] private float wholePitch = 60f;

    [Tooltip("画面の端に空けるすき間（画面の幅を1としたときの割合）")]
    [SerializeField] private float screenMargin = 0.04f;

    [Tooltip("これより大きい物は、ステージの大きさを測るときに入れない（遠くの背景など）")]
    [SerializeField] private float ignoreLargerThan = 200f;

    [Tooltip("モードを切り替えたときに、カメラが移るなめらかさ。大きいほどすぐ移る")]
    [SerializeField] private float moveSharpness = 6f;

    /// <summary>いまのモード。</summary>
    public Mode CurrentMode { get; private set; }

    /// <summary>画面の案内に出す、切り替えのキーの名前（例：「C／Back」）。カメラが無ければ空。</summary>
    public static string LocalToggleKeyName => current != null ? current.toggleLabel : string.Empty;

    /// <summary>いま動いている切り替え部品（シーンが切り替わる間は、古い物と新しい物が一瞬重なるため）。</summary>
    private static SpaceJunkCameraModeSwitch current;

    private string toggleLabel;

    private Camera view;
    private SpaceJunkTeamFollowCamera teamCamera;
    private Vector3 wholePosition;
    private Quaternion wholeRotation;
    private bool wholeReady;

    private void Awake()
    {
        view = GetComponent<Camera>();
        teamCamera = GetComponent<SpaceJunkTeamFollowCamera>();
        toggleLabel = $"{toggleKey}／{GamepadInput.Label(toggleButton)}";
        current = this;
    }

    private void OnDestroy()
    {
        // 自分が出している案内のときだけ消す（新しいシーンのカメラの案内を消さないように）
        if (current == this)
        {
            current = null;
        }
    }

    private void Start()
    {
        MeasureWholeStage();
        SetMode(startMode, snap: true);
    }

    private void Update()
    {
        Keyboard keyboard = Keyboard.current;
        bool pressed = (keyboard != null && keyboard[toggleKey].wasPressedThisFrame) ||
                       GamepadInput.WasPressed(toggleButton);

        if (pressed)
        {
            SetMode(CurrentMode == Mode.WholeStage ? Mode.Team : Mode.WholeStage, snap: false);
        }
    }

    private void LateUpdate()
    {
        if (CurrentMode != Mode.WholeStage || !wholeReady)
        {
            return;
        }

        float t = 1f - Mathf.Exp(-moveSharpness * Time.deltaTime);
        transform.SetPositionAndRotation(
            Vector3.Lerp(transform.position, wholePosition, t),
            Quaternion.Slerp(transform.rotation, wholeRotation, t));
    }

    /// <summary>モードを変える。<paramref name="snap"/> なら、なめらかにせずその場所へ飛ぶ。</summary>
    public void SetMode(Mode mode, bool snap)
    {
        CurrentMode = mode;

        // 自陣カメラは、自陣モードのときだけ動かす（全体モードでは位置を決めない）
        if (teamCamera != null)
        {
            teamCamera.enabled = mode == Mode.Team;
        }

        if (mode == Mode.WholeStage && snap && wholeReady)
        {
            transform.SetPositionAndRotation(wholePosition, wholeRotation);
        }
    }

    // ------------------------------------------------------------
    // 全体カメラの位置を決める
    // ------------------------------------------------------------

    /// <summary>
    /// **ステージの見た目が占める範囲を測り、その角8つが全部画面に収まる、いちばん近い位置を求める。**
    /// （宇宙ごみの全体固定カメラのマップを作ったときと同じ求め方。こちらは実行したときに測る）
    /// </summary>
    private void MeasureWholeStage()
    {
        if (!TryGetStageBounds(out Bounds stage))
        {
            // 測れなければ、置いてある位置を全体カメラの位置にする
            wholePosition = transform.position;
            wholeRotation = transform.rotation;
            wholeReady = true;
            return;
        }

        wholeRotation = Quaternion.Euler(wholePitch, 0f, 0f);
        Vector3 back = wholeRotation * Vector3.back;
        Vector3[] corners = CornersOf(stage);

        Vector3 savedPosition = transform.position;
        Quaternion savedRotation = transform.rotation;

        float near = 1f;
        float far = 500f;

        for (int i = 0; i < 40; i++)
        {
            float middle = (near + far) * 0.5f;
            transform.SetPositionAndRotation(stage.center + back * middle, wholeRotation);

            if (AllInView(corners))
            {
                far = middle;
            }
            else
            {
                near = middle;
            }
        }

        transform.SetPositionAndRotation(savedPosition, savedRotation);

        wholePosition = stage.center + back * far;
        wholeReady = true;

        // 遠くに置くので、奥が切れないよう描く距離を広げておく
        view.farClipPlane = Mathf.Max(view.farClipPlane, far + stage.extents.magnitude * 2f);
    }

    /// <summary>ステージの見た目の範囲。動く物（プレイヤー・物資・列車）や画面の表示は入れない。</summary>
    private bool TryGetStageBounds(out Bounds bounds)
    {
        bounds = default;
        bool found = false;

        foreach (Renderer renderer in FindObjectsByType<Renderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            if (renderer == null || !renderer.enabled || !renderer.gameObject.scene.IsValid() ||
                renderer.gameObject.scene != gameObject.scene)
            {
                continue;
            }

            if (renderer.transform.IsChildOf(transform) ||
                renderer.GetComponentInParent<Canvas>() != null ||
                renderer.GetComponentInParent<HookableObject>() != null ||
                renderer.GetComponentInParent<FishingPlayerController>() != null ||
                renderer.GetComponentInParent<SpaceJunkTrain>() != null)
            {
                continue;
            }

            Bounds b = renderer.bounds;
            if (b.size.x > ignoreLargerThan || b.size.z > ignoreLargerThan)
            {
                continue;
            }

            if (!found)
            {
                bounds = b;
                found = true;
            }
            else
            {
                bounds.Encapsulate(b);
            }
        }

        return found;
    }

    private bool AllInView(Vector3[] corners)
    {
        foreach (Vector3 corner in corners)
        {
            Vector3 v = view.WorldToViewportPoint(corner);

            if (v.z <= view.nearClipPlane ||
                v.x < screenMargin || v.x > 1f - screenMargin ||
                v.y < screenMargin || v.y > 1f - screenMargin)
            {
                return false;
            }
        }
        return true;
    }

    private static Vector3[] CornersOf(Bounds b)
    {
        Vector3 min = b.min;
        Vector3 max = b.max;
        return new[]
        {
            new Vector3(min.x, min.y, min.z), new Vector3(max.x, min.y, min.z),
            new Vector3(min.x, min.y, max.z), new Vector3(max.x, min.y, max.z),
            new Vector3(min.x, max.y, min.z), new Vector3(max.x, max.y, min.z),
            new Vector3(min.x, max.y, max.z), new Vector3(max.x, max.y, max.z),
        };
    }
}
