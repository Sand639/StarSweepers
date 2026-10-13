using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

/// <summary>
/// **カメラの種類を、キーで順に切り替える部品。** 宇宙ごみの STAGE_01〜05 のカメラに付ける。
///
/// 2026/10/6（大槻さん）に「全体カメラ ⇔ 自陣カメラ」の切り替えとして作り、
/// 2026/10/7（小野田さん）に**カメラの種類を部品に分けた**。この部品は「どれを使うか」と「切り替えのつなぎ」だけを受け持つ。
///
/// | 種類（同じカメラに付いている部品） | 見え方 |
/// | --- | --- |
/// | 全体（<see cref="SpaceJunkWholeStageCamera"/>） | ステージ全体が画面に収まる位置から見下ろす |
/// | チーム（<see cref="SpaceJunkTeamGroupCamera"/>） | 自陣が手前のまま、自分と味方がいつも画面に入る |
/// | 自分（<see cref="SpaceJunkTeamFollowCamera"/>） | 自陣が手前のまま、自分だけを追う |
///
/// ・**C キー**（コントローラーは **Back**）で、**カメラに付いている順に**次の種類へ切り替える
/// ・切り替えるとき、前のカメラの位置から「切り替えの秒数」かけてなめらかに移る
/// ・切り替えは**自分の画面だけ**（ほかの人のカメラは変わらない）
/// ・各種類の「切り替えに入れる」を OFF にすると、その種類は飛ばす。順番はインスペクターで部品を上下に動かして変える
///
/// 移動（W＝画面の奥）はカメラの向きに合わせて回るので、どの種類でも画面どおりに動ける。
/// </summary>
[RequireComponent(typeof(Camera))]
public class SpaceJunkCameraModeSwitch : MonoBehaviour
{
    [Header("切り替え")]
    [Tooltip("始まったときのカメラ（同じカメラに付いている種類の部品をドラッグする）。空なら切り替えの順でいちばん上")]
    [SerializeField] private SpaceJunkCameraMode firstMode;

    [Tooltip("切り替えるキー")]
    [SerializeField] private Key toggleKey = Key.C;

    [Tooltip("切り替えるコントローラーのボタン")]
    [SerializeField] private GamepadButton toggleButton = GamepadButton.Select;

    [Tooltip("切り替えたときに、前のカメラの位置から移り終わるまでの秒数。0 ですぐ切り替わる")]
    [Min(0f)]
    [SerializeField] private float blendSeconds = 0.6f;

    /// <summary>いまの種類。</summary>
    public SpaceJunkCameraMode Current { get; private set; }

    /// <summary>画面の案内に出す、切り替えのキーの名前（例：「C／Back」）。カメラが無ければ空。</summary>
    public static string LocalToggleKeyName => current != null ? current.toggleLabel : string.Empty;

    /// <summary>画面の案内に出す、いまの種類と切り替えられる種類（例：「いま：チーム（全体／チーム／自分）」）。</summary>
    public static string LocalModeSummary => current != null ? current.Summary() : string.Empty;

    /// <summary>いま動いている切り替え部品（シーンが切り替わる間は、古い物と新しい物が一瞬重なるため）。</summary>
    private static SpaceJunkCameraModeSwitch current;

    private readonly List<SpaceJunkCameraMode> modes = new List<SpaceJunkCameraMode>();
    private readonly System.Text.StringBuilder summary = new System.Text.StringBuilder();
    private string toggleLabel;

    private Vector3 blendFromPosition;
    private Quaternion blendFromRotation;
    private float blendTime;
    private bool blending;

    private void Awake()
    {
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
        RefreshModes();

        SpaceJunkCameraMode start = firstMode != null && firstMode.InCycle ? firstMode : null;
        if (start == null && modes.Count > 0)
        {
            start = modes[0];
        }

        SetMode(start, snap: true);
    }

    private void Update()
    {
        Keyboard keyboard = Keyboard.current;
        bool pressed = (keyboard != null && keyboard[toggleKey].wasPressedThisFrame) ||
                       GamepadInput.WasPressed(toggleButton);

        if (pressed)
        {
            Next();
        }
    }

    private void LateUpdate()
    {
        if (Current == null || !Current.TryGetPose(Time.deltaTime, out Vector3 position, out Quaternion rotation))
        {
            return;
        }

        if (blending)
        {
            blendTime += Time.deltaTime;
            float k = blendSeconds > 0f ? Mathf.SmoothStep(0f, 1f, blendTime / blendSeconds) : 1f;
            position = Vector3.Lerp(blendFromPosition, position, k);
            rotation = Quaternion.Slerp(blendFromRotation, rotation, k);
            blending = k < 1f;
        }

        transform.SetPositionAndRotation(position, rotation);
    }

    /// <summary>次の種類へ切り替える（付いている順。最後の次は最初に戻る）。</summary>
    public void Next()
    {
        RefreshModes();
        if (modes.Count == 0)
        {
            return;
        }

        int index = modes.IndexOf(Current);
        SetMode(modes[(index + 1) % modes.Count], snap: false);
    }

    /// <summary>種類を変える。<paramref name="snap"/> なら、なめらかにせずその場所へ飛ぶ。</summary>
    public void SetMode(SpaceJunkCameraMode mode, bool snap)
    {
        if (mode == null)
        {
            return;
        }

        Current = mode;
        mode.Activate();

        blending = !snap && blendSeconds > 0f;
        blendTime = 0f;
        blendFromPosition = transform.position;
        blendFromRotation = transform.rotation;
    }

    /// <summary>
    /// 追いかける相手を、付いている種類**全部**に渡す。
    /// （<c>SpaceJunkPlayerSetup</c> は自分カメラにだけ渡しているが、ほかの種類は自分のプレイヤーを自分で探すので、呼ばなくても動く）
    /// </summary>
    public void SetTarget(Transform target)
    {
        foreach (SpaceJunkCameraMode mode in GetComponents<SpaceJunkCameraMode>())
        {
            mode.SetTarget(target);
        }
    }

    /// <summary>切り替えに入れる種類を、付いている順に集め直す（再生中に ON/OFF を変えても効くように）。</summary>
    private void RefreshModes()
    {
        modes.Clear();

        foreach (SpaceJunkCameraMode mode in GetComponents<SpaceJunkCameraMode>())
        {
            if (mode.InCycle)
            {
                modes.Add(mode);
            }
        }
    }

    private string Summary()
    {
        if (Current == null)
        {
            return string.Empty;
        }

        summary.Clear();
        summary.Append("いま：").Append(Current.DisplayName);

        if (modes.Count > 1)
        {
            summary.Append("（");
            for (int i = 0; i < modes.Count; i++)
            {
                if (i > 0)
                {
                    summary.Append("／");
                }
                summary.Append(modes[i].DisplayName);
            }
            summary.Append("）");
        }

        return summary.ToString();
    }
}
