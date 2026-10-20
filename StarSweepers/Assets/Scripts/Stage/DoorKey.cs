using UnityEngine;

/// <summary>
/// **鍵。** 持ち運んで、鍵付きの扉を開けるための物。
///
/// **持ち運びは「持てる物」の仕組みをそのまま使っている**（<see cref="Grabbable"/>）。
/// つまり、
///
/// - 上半身（と合体した体）が **F で持てる**
/// - 持ったまま歩ける。**下半身に切り替えても、上半身が持ったまま待っている**
///
/// 鍵として特別なのは、**<see cref="LockedDoor"/> に使える**という一点だけ。
/// 扉に使うと、**その場で無くなる**（<see cref="RobotKeyUser"/> が消す）。
///
/// ## 鍵の種類を分けたいとき
///
/// `keyId` に同じ言葉を入れた鍵と扉だけが合う。
/// **空のままなら、どの鍵でもどの扉でも開く**ので、1組しか使わないなら空でよい。
///
/// ## 落としても詰まないようにしてある
///
/// 鍵は使い切りなので、**穴や場外に落とすと進めなくなる**（`リスクリスト.md` 2026/9/7 登録）。
/// そのため、**決めた高さより下に落ちたら、置いてあった場所へ戻る。**
/// 戻したくない作りにするなら `Return When Fallen` を OFF にする。
/// </summary>
[RequireComponent(typeof(Grabbable))]
public class DoorKey : MonoBehaviour
{
    [Header("鍵の種類")]
    [Tooltip("**同じ言葉を入れた扉だけが開く。** 空なら、どの扉でも開く（例：`赤` `倉庫`）")]
    [SerializeField] private string keyId = "";

    [Header("落としたときに戻す")]
    [Tooltip("ONだと、下に落ちた鍵が最初に置いてあった場所へ戻る。**取り返せない場所に落として詰むのを防ぐ**")]
    [SerializeField] private bool returnWhenFallen = true;

    [Tooltip("この高さより下に落ちたら戻す（メートル）")]
    [SerializeField] private float returnHeight = -10f;

    /// <summary>この鍵の種類。空なら、どの扉にも合う。</summary>
    public string KeyId => keyId;

    /// <summary>持てる物の仕組み。持たれているかどうかを見るために使う。</summary>
    private Grabbable grabbable;

    /// <summary>鍵の物理。戻すときに、落ちる勢いを消すために使う（無くても動く）。</summary>
    private Rigidbody body;

    // 置いてあった場所。戻す先に使う
    private Vector3 homePosition;
    private Quaternion homeRotation;

    private void Awake()
    {
        grabbable = GetComponent<Grabbable>();
        body = GetComponent<Rigidbody>();

        homePosition = transform.position;
        homeRotation = transform.rotation;
    }

    /// <summary>毎フレーム、決めた高さより下に落ちていないかを見張る。</summary>
    private void Update()
    {
        if (!returnWhenFallen || GamePause.IsPaused)
        {
            return;
        }

        // 持たれている間は戻さない（持ったまま穴の上を通ることがあるため）
        if (grabbable != null && grabbable.IsHeld)
        {
            return;
        }

        if (transform.position.y < returnHeight)
        {
            ReturnHome();
        }
    }

    /// <summary>
    /// 置いてあった場所へ戻す。**落ちる勢いも消す**（残っていると、戻った先でまた落ちていく）。
    /// ステージ側から呼んでもよい。
    /// </summary>
    public void ReturnHome()
    {
        if (body != null)
        {
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
        }

        transform.SetPositionAndRotation(homePosition, homeRotation);

        Debug.Log($"{name}: 落ちたので、置いてあった場所へ戻しました。", this);
    }
}
