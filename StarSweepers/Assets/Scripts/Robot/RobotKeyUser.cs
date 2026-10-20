using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// **持っている鍵を、鍵付きの扉に使う。**
///
/// 遊び方：
///   鍵（<see cref="DoorKey"/>）を **F** で持つ
///   鍵を持ったまま扉に近づくと、**扉の色が変わる**（開けられる合図）
///   **F** を押すと、**鍵が無くなって扉が開く**
///   **一度開けた扉は、鍵なしで F を押すたびに開け閉めできる**
///
/// **鍵を持ち運ぶ仕組みは作っていない。**
/// 物を持つ機能（<see cref="RobotGrabber"/>）をそのまま使っているので、
/// **上半身と合体した体だけが鍵を運べる**（腕のある体だけ、という決まりが自動で効く）。
///
/// この部品がやっているのは、
///
/// 1. **持っている物が鍵かどうか**を見る
/// 2. 正面の近くに、**いま操作できる扉**があるかを探す
///    （鍵がかかっているなら合う鍵が要る。**外れているなら鍵は要らない**）
/// 3. F が押されたら、**扉を操作する**（鍵を使ったときだけ鍵を消す）
///
/// ## F キーの取り合い
///
/// 同じ F キーを、ロープ・物の持ち運び・この機能で使っている。
/// **操作できる扉を狙っているときだけ、この機能が受け取る。**
/// それ以外のときは、これまでどおり <see cref="RobotGrabber"/> が受け取る。
///
/// 扉の正面で物を置きたいときは、**扉から目をそらしてから F を押す**。
///
/// 使っているシーン：ロボット分離の検証シーン（`RobotSplitTest`）など、`RobotRig` を置いたシーン。
/// 扉そのものは <see cref="LockedDoor"/>、鍵は <see cref="DoorKey"/>。
/// </summary>
[RequireComponent(typeof(RobotController))]
[RequireComponent(typeof(RobotGrabber))]
public class RobotKeyUser : MonoBehaviour
{
    [Header("届く範囲")]
    [Tooltip("この距離まで近づくと、鍵を使える（メートル）")]
    [Range(0.5f, 6f)]
    [SerializeField] private float reach = 3f;

    [Tooltip("**見ている方向からどれだけずれていても使えるか**（度）。" +
             "頭を中心に、カメラの向きと、その水平の向きの2本で判断する")]
    [Range(10f, 180f)]
    [SerializeField] private float maxAngle = 80f;

    [Tooltip("ONにすると、**壁の向こうの扉は開けられなくなる**（間に何かあるかを調べる）。" +
             "ガラスのように透ける物は、間にあっても開けられる。OFFにすると壁越しに開く（前の動き）")]
    [SerializeField] private bool requireClearPath = true;

    [Header("キーの割り当て")]
    [Tooltip("鍵を使うキー。物を持つキーと同じにしてよい")]
    [SerializeField] private Key useKey = Key.F;

    [Header("見た目")]
    [Tooltip("画面中央のレティクル。開けられるときに色が変わる")]
    [SerializeField] private Reticle reticle;

    [Tooltip("開けられる扉を、この色に近づける")]
    [SerializeField] private Color highlightColor = new Color(1f, 0.8f, 0.2f);

    [Tooltip("どれくらい色を混ぜるか")]
    [Range(0f, 1f)]
    [SerializeField] private float highlightStrength = 0.5f;

    private RobotController controller;

    /// <summary>物を持つ側。「いま持っている物が鍵か」を見るのと、使った鍵を消すのに使う。</summary>
    private RobotGrabber grabber;

    /// <summary>開けられる扉の色を変える道具。物を持つ・ロープ側と同じ仕組みを使っている。</summary>
    private readonly InteractHighlight highlight = new InteractHighlight();

    /// <summary>いま狙えている扉。無ければ null。</summary>
    public LockedDoor Aimed { get; private set; }

    /// <summary>いま持っている鍵。持っていなければ null。</summary>
    public DoorKey HeldKey =>
        grabber != null && grabber.Held != null ? grabber.Held.GetComponent<DoorKey>() : null;

    /// <summary>
    /// **このフレームの F キーを、扉の操作が受け取るか。**
    /// <see cref="RobotGrabber"/> がこれを見て、物を離す処理を止める。
    /// </summary>
    public bool WantsInteract => Aimed != null;

    private void Awake()
    {
        controller = GetComponent<RobotController>();
        grabber = GetComponent<RobotGrabber>();
    }

    private void Update()
    {
        UpdateAim();

        if (WasUseKeyPressed() && WantsInteract)
        {
            UseKey();
        }
    }

    // ------------------------------------------------------------
    // 狙う
    // ------------------------------------------------------------

    /// <summary>
    /// 正面の近くにある「いま持っている鍵で開く扉」を探して、色を変える。
    /// （一度鍵を外した扉は、鍵を持っていなくても対象になる。<see cref="FindDoor"/> を参照）
    /// </summary>
    private void UpdateAim()
    {
        LockedDoor found = FindDoor();

        if (found == Aimed)
        {
            return;
        }

        Aimed = found;

        highlight.Clear();

        if (Aimed != null)
        {
            highlight.Apply(Aimed.gameObject, highlightColor, highlightStrength);
        }

        if (reticle != null)
        {
            reticle.SetHighlight(Aimed != null ? highlightColor : (Color?)null);
        }
    }

    /// <summary>
    /// 正面に近くて一番近い、**いま操作できる扉**を返す。
    ///
    /// **鍵がかかっている扉は、合う鍵を持っていないと対象にならない。**
    /// 開けられないのに色が変わると、「開けられそうなのに開かない」と勘違いさせるため。
    ///
    /// **鍵が外れた扉は、鍵を持っていなくても対象になる**（開け閉めできる）。
    /// </summary>
    private LockedDoor FindDoor()
    {
        DoorKey key = HeldKey;
        RobotBody body = controller.ActiveBody;

        if (body == null)
        {
            return null;
        }

        LockedDoor nearest = null;
        float nearestDistance = float.MaxValue;

        foreach (LockedDoor door in LockedDoor.All)
        {
            if (door == null || !door.CanInteract(key))
            {
                continue;
            }

            // 距離は**体の足元から、扉の一番近い場所まで**で測る
            // （物を持つ・ロープにつかまるほうは手から測っているので、測り方が違う）
            Vector3 point = ClosestPoint(door);
            float distance = Vector3.Distance(point, body.transform.position);

            // 届かない扉と、すでに見つけた扉より遠い扉は、狙いを調べる前に外す（調べる手間を省く）
            if (distance > reach || distance >= nearestDistance)
            {
                continue;
            }

            // **頭を中心に、見ている方向から大きく外れている扉は無視する**
            if (!AimCheck.IsAimed(body.HeadPosition, point, maxAngle, body.transform.forward))
            {
                continue;
            }

            // **壁の向こうの扉は開けられない**（ガラスなど透ける物は通す）
            if (requireClearPath &&
                !AimCheck.HasClearPath(body.HeadPosition, point, transform.root, door.transform))
            {
                continue;
            }

            nearest = door;
            nearestDistance = distance;
        }

        return nearest;
    }

    /// <summary>
    /// 扉のうち、**体から一番近い場所**を返す。
    ///
    /// 扉の中心で測ると、**大きな扉ほど届かなくなる**（端に立っても中心が遠いため）。
    /// 当たり判定の一番近い点で測ると、大きさに関係なく同じ感覚で使える。
    /// </summary>
    private Vector3 ClosestPoint(LockedDoor door)
    {
        Collider collider = door.GetComponentInChildren<Collider>();

        return collider != null
            ? collider.ClosestPoint(controller.ActiveBody.transform.position)
            : door.transform.position;
    }

    // ------------------------------------------------------------
    // 使う
    // ------------------------------------------------------------

    /// <summary>
    /// 狙っている扉を操作する。
    ///
    /// - **鍵がかかっていれば**… 鍵を使って開け、**鍵を消す**
    /// - **鍵が外れていれば**… **開け閉めするだけ**（鍵は要らない）
    /// </summary>
    public void UseKey()
    {
        LockedDoor door = Aimed;

        // 鍵はこのあと消すので、ログに名前を出すために先に控えておく
        DoorKey key = HeldKey;

        if (door == null)
        {
            return;
        }

        highlight.Clear();
        Aimed = null;

        if (reticle != null)
        {
            reticle.SetHighlight(null);
        }

        bool usedKey = door.Interact();

        if (!usedKey)
        {
            Debug.Log($"[ROBOT] {door.name} を開け閉めしました");
            return;
        }

        // **鍵は使い切り。** 持ったまま消すと持ち手の情報が残るので、
        // 離してから消す（`ConsumeHeld` が両方やってくれる）
        grabber.ConsumeHeld();

        Debug.Log($"[ROBOT] {(key != null ? key.name : "鍵")} を使って {door.name} の鍵を外しました");
    }

    /// <summary>このフレームに、鍵を使うキー（`useKey`）が押されたか。</summary>
    private bool WasUseKeyPressed()
    {
        Keyboard keyboard = Keyboard.current;
        return keyboard != null && keyboard[useKey].wasPressedThisFrame;
    }
}
