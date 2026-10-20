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

    /// <summary>切り替えに入れる種類（カメラに付いている順）。<see cref="RefreshModes"/> で集め直す。</summary>
    private readonly List<SpaceJunkCameraMode> modes = new List<SpaceJunkCameraMode>();

    /// <summary>案内の文字を毎フレーム組み立てるときの入れ物（使い回してごみを減らす）。</summary>
    private readonly System.Text.StringBuilder summary = new System.Text.StringBuilder();

    /// <summary>案内に出す、切り替えのキーとボタンの名前（例：「C／Back」）。Awake で1回だけ作る。</summary>
    private string toggleLabel;

    // ── 切り替えのつなぎ ──
    // 切り替えた瞬間のカメラの位置・向きを控えておき、新しい種類が求めた位置へ blendSeconds かけて寄せる。
    // 新しい種類の側は SnapNext で「その場所へ飛ぶ」ので、なめらかさはこちらだけが受け持つ

    /// <summary>切り替えた瞬間のカメラの位置（つなぎの出発点）。</summary>
    private Vector3 blendFromPosition;

    /// <summary>切り替えた瞬間のカメラの向き（つなぎの出発点）。</summary>
    private Quaternion blendFromRotation;

    /// <summary>切り替えてから経った秒数。</summary>
    private float blendTime;

    /// <summary>いま、つなぎの途中か。</summary>
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

        // 最初のカメラに指定された種類が「切り替えに入れない」設定なら、指定を無視して順でいちばん上から
        SpaceJunkCameraMode start = firstMode != null && firstMode.InCycle ? firstMode : null;
        if (start == null && modes.Count > 0)
        {
            start = modes[0];
        }

        // 始まりはつなぎ無しでその場所へ（start が null なら SetMode は何もしない）
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

    /// <summary>
    /// いまの種類に「置きたい位置」を聞いて、カメラを動かす。プレイヤーが動いたあと（LateUpdate）に行う。
    /// 置き場所が決まらないとき（相手がまだいない など）はカメラをそのままにする。
    /// </summary>
    private void LateUpdate()
    {
        if (Current == null || !Current.TryGetPose(Time.deltaTime, out Vector3 position, out Quaternion rotation))
        {
            return;
        }

        if (blending)
        {
            // 切り替えた瞬間の位置から、新しい種類が求めた位置へ寄せる。
            // k は 0（出発点）〜1（新しい位置）の進み具合。SmoothStep で、出だしと終わりをゆるやかにしている
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

        // いまの種類が切り替えから外れていた（再生中に OFF にした など）ときは IndexOf が -1 を返すので、
        // 自然と先頭（0 番）へ行く
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

        // つなぎの出発点は「いまカメラがある場所」（前の種類が最後に置いた位置）
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

    /// <summary>案内の文字「いま：チーム（全体／チーム／自分）」を組み立てる。種類が1つだけなら括弧は付けない。</summary>
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
