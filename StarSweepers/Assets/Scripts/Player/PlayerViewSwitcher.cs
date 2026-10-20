using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// カメラの位置を切り替えて、TPS（三人称）と FPS（一人称）を行き来する。
///
/// TPS … キャラクターの後ろからカメラが見る。カプセルが見える
/// FPS … キャラクターの目の位置から見る。カプセルは自動で隠れる
///
/// 既定では V キーで切り替わる。切り替えるキーはインスペクターで変えられる。
/// </summary>
public class PlayerViewSwitcher : MonoBehaviour
{
    public enum ViewMode
    {
        /// <summary>三人称。後ろから見る</summary>
        ThirdPerson,

        /// <summary>一人称。目の位置から見る</summary>
        FirstPerson,
    }

    [Header("つなぐもの")]
    [Tooltip("動かすカメラ。PlayerRig の中の PlayerCamera を入れる")]
    [SerializeField] private Transform cameraTransform;

    [Tooltip("カプセルの見た目。TPSのときだけ表示する（中の目印もまとめて隠れる）")]
    [SerializeField] private GameObject thirdPersonBody;

    [Tooltip("一人称のときに手前に見える腕。FPSのときだけ表示する")]
    [SerializeField] private GameObject firstPersonArms;

    [Header("切り替え")]
    [Tooltip("再生したときにどちらで始めるか")]
    [SerializeField] private ViewMode startMode = ViewMode.ThirdPerson;

    [Tooltip("切り替えに使うキー")]
    [SerializeField] private Key switchKey = Key.V;

    [Header("TPS（三人称）のとき")]
    [Tooltip("キャラクターからカメラまでの距離（メートル）")]
    [SerializeField] private float distance = 4f;

    [Tooltip("目の高さからどれだけ上にずらすか（メートル）")]
    [SerializeField] private float heightOffset = 0.4f;

    [Header("FPS（一人称）のとき")]
    [Tooltip("目の位置からの微調整。基本はそのままでよい")]
    [SerializeField] private Vector3 firstPersonOffset = new Vector3(0f, 0f, 0.1f);

    [Header("見た目")]
    [Tooltip("切り替わるときの滑らかさ。0にすると一瞬で切り替わる")]
    [SerializeField] private float switchSmooth = 12f;

    /// <summary>
    /// 壁に遮られたときにカメラを寄せる部品。カメラに付いていれば自動で見つける。
    /// 無くても動く（そのときは壁の向こうが見えることがある）。
    /// </summary>
    private CameraObstacleAvoid obstacleAvoid;

    /// <summary>今どちらのモードか。</summary>
    public ViewMode CurrentMode { get; private set; }

    private void Awake()
    {
        CurrentMode = startMode;

        if (cameraTransform == null)
        {
            Debug.LogError($"{name}: カメラが入っていません。TPS/FPSの切り替えができません。", this);
            enabled = false;
            return;
        }

        obstacleAvoid = cameraTransform.GetComponent<CameraObstacleAvoid>();

        // 開始時は滑らかにせず、いきなり目的の位置に置く
        cameraTransform.localPosition = GetTargetPosition();
        ApplyVisibility();
    }

    private void Update()
    {
        ReadSwitchKey();
    }

    private void LateUpdate()
    {
        // カメラの動きは、キャラクターが動いたあとに処理する
        Vector3 target = GetTargetPosition();

        if (switchSmooth <= 0f)
        {
            cameraTransform.localPosition = target;
        }
        else
        {
            // 毎フレーム「残りの距離の一定の割合」だけ近づける。
            // Exp を使うと、フレームレートが変わっても同じ速さで寄っていく
            cameraTransform.localPosition = Vector3.Lerp(
                cameraTransform.localPosition,
                target,
                1f - Mathf.Exp(-switchSmooth * Time.deltaTime));
        }

        // **位置を決めたすぐあとに、壁に遮られていないか調べる。**
        // 間に壁があれば、その手前までカメラを寄せる（向きは変えない）
        if (obstacleAvoid != null)
        {
            obstacleAvoid.Apply();
        }
    }

    /// <summary>切り替えキー（既定は V）が押されたら、TPS と FPS を入れ替える。</summary>
    private void ReadSwitchKey()
    {
        var keyboard = Keyboard.current;
        if (keyboard == null)
        {
            return;
        }

        if (keyboard[switchKey].wasPressedThisFrame)
        {
            ToggleView();
        }
    }

    /// <summary>TPS と FPS を入れ替える。ボタンなどから呼んでもよい。</summary>
    public void ToggleView()
    {
        SetView(CurrentMode == ViewMode.ThirdPerson ? ViewMode.FirstPerson : ViewMode.ThirdPerson);
    }

    /// <summary>指定したモードに切り替える。</summary>
    public void SetView(ViewMode mode)
    {
        CurrentMode = mode;
        ApplyVisibility();
    }

    /// <summary>
    /// いまのモードでカメラが居るべき位置（首振りの中心 CameraPivot から見た相対位置）。
    /// TPS は「少し上・後ろ」、FPS は目の位置そのまま（＋微調整）。
    /// </summary>
    private Vector3 GetTargetPosition()
    {
        if (CurrentMode == ViewMode.FirstPerson)
        {
            return firstPersonOffset;
        }

        // z がマイナス＝キャラクターの後ろ
        return new Vector3(0f, heightOffset, -distance);
    }

    /// <summary>
    /// 見えるものを切り替える。
    /// TPS … カプセルの体を出す。腕は消す
    /// FPS … 体をまるごと消す（中から見ると視界が埋まるため）。代わりに腕を出す
    /// </summary>
    private void ApplyVisibility()
    {
        bool isThirdPerson = CurrentMode == ViewMode.ThirdPerson;

        if (thirdPersonBody != null)
        {
            thirdPersonBody.SetActive(isThirdPerson);
        }

        if (firstPersonArms != null)
        {
            firstPersonArms.SetActive(!isThirdPerson);
        }
    }
}
