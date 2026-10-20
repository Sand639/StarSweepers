using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// **踏むと信号を送るボタン。**
///
/// 土台の上に赤いボタンが乗っていて、
///
/// - **上に乗るとボタンが沈み、信号が1回送られる**
/// - **降りるとボタンが浮き上がる**
/// - **浮き上がりきるまでは、もう一度踏んでも信号は送られない**（沈みはする）
/// - **浮き上がったあとに踏めば、また信号が送られる**
///
/// 信号を受け取って何をするかは、**このボタンは知らない。**
/// インスペクターで繋いだ相手に伝えるだけなので、
/// **扉を開ける・壁を動かす・音を鳴らすなど、何にでも使い回せる。**
///
/// ## 何が乗ったかは問わない
///
/// ロボットでも、持てる箱でも、**当たり判定を持つ物なら何でも押せる。**
/// 箱を乗せておけば**押しっぱなし**にできる。
///
/// ## 「ぶつかった合図」ではなく「重なっているか」で見ている
///
/// `OnTriggerEnter` のような**ぶつかった合図**は使わず、
/// **毎フレーム、ボタンの上の箱の中に物があるかを調べている。**
///
/// 合図に頼ると、物が消えたり、当たり判定が切られたりしたときに
/// **「乗ったまま」で固まってしまう**（通信の実装でも同じ失敗をしている）。
/// 重なりを毎回調べる形なら、そういう状態にならない。
/// </summary>
public class PressButton : MonoBehaviour
{
    [Header("つなぐもの")]
    [Tooltip("沈むボタンの見た目。土台ではなく、**上に乗っている赤い部分**を入れる")]
    [SerializeField] private Transform buttonVisual;

    [Header("反応する範囲")]
    [Tooltip("この箱の中に物が入ると「踏まれた」と見なす。土台の上を覆う大きさにする")]
    [SerializeField] private Vector3 detectSize = new Vector3(1.1f, 0.4f, 1.1f);

    [Tooltip("反応する箱の位置（この物の中心から見た位置）")]
    [SerializeField] private Vector3 detectOffset = new Vector3(0f, 0.3f, 0f);

    [Tooltip("反応させたい物の種類。普通は初期値のままでよい")]
    [SerializeField] private LayerMask detectMask = ~0;

    [Header("沈み方")]
    [Tooltip("どれだけ沈むか（メートル）")]
    [Range(0.01f, 0.5f)]
    [SerializeField] private float pressDepth = 0.07f;

    [Tooltip("沈みきるまでの時間（秒）。短いほどキビキビ沈む")]
    [Range(0.01f, 2f)]
    [SerializeField] private float pressTime = 0.08f;

    [Tooltip("**浮き上がるまでの時間（秒）。この間はもう一度踏んでも信号が出ない。**" +
             "「連打できない間隔」がこの値で決まる")]
    [Range(0.05f, 5f)]
    [SerializeField] private float riseTime = 0.8f;

    [Header("見た目")]
    [Tooltip("ONにすると、**次の信号を送れないあいだ、ボタンの色が暗くなる**。" +
             "遊ぶ人に「まだ押せない」と伝わる")]
    [SerializeField] private bool dimWhileUsed = true;

    [Tooltip("送れないあいだの色")]
    [SerializeField] private Color usedColor = new Color(0.25f, 0.05f, 0.05f);

    [Header("踏まれたときにすること")]
    [Tooltip("**踏まれた瞬間に呼ぶ処理。** ここに相手の物を入れて、動かしたい機能を選ぶ。" +
             "**動く壁なら MoveObject.Toggle を選べば、押すたびに上下する**")]
    [SerializeField] private UnityEvent pressed = new UnityEvent();

    [Tooltip("また踏めるようになった（浮き上がりきった）瞬間に呼ぶ処理。使わなくてよい")]
    [SerializeField] private UnityEvent readyAgain = new UnityEvent();

    [Tooltip("踏まれたときに**出したり消したりする物。** " +
             "止めてある機能を動かしたいときは、ここに入れるのが手っ取り早い")]
    [SerializeField] private GameObject[] toggleObjects = new GameObject[0];

    [Tooltip("踏まれたときに**出すプレハブ。** 空なら何も出さない")]
    [SerializeField] private GameObject spawnPrefab;

    [Tooltip("プレハブを出す場所。空ならボタンの真上に出る")]
    [SerializeField] private Transform spawnPoint;

    /// <summary>踏まれた瞬間に呼ばれる。コードから繋ぎたいときに使う。</summary>
    public UnityEvent Pressed => pressed;

    /// <summary>また踏めるようになった瞬間に呼ばれる。</summary>
    public UnityEvent ReadyAgain => readyAgain;

    /// <summary>いま誰かが乗っているか。</summary>
    public bool IsOccupied { get; private set; }

    /// <summary>**いま踏めば信号が出る状態か。** 浮き上がりきると true に戻る。</summary>
    public bool CanSignal { get; private set; } = true;

    /// <summary>沈み具合。0で浮いた状態、1で沈みきった状態。</summary>
    public float PressAmount { get; private set; }

    /// <summary>乗っている物を探すときの入れ物。毎フレーム作り直さないよう使い回す。一度に拾えるのは16個まで。</summary>
    private readonly Collider[] hits = new Collider[16];

    /// <summary>押せないあいだ、ボタンを暗い色にするための係（色を戻すときのために、元の色も覚えている）。</summary>
    private readonly InteractHighlight usedTint = new InteractHighlight();

    /// <summary>ボタンが浮いているときの位置。ここから下へ沈める。</summary>
    private Vector3 visualRestPosition;

    private void Awake()
    {
        if (buttonVisual != null)
        {
            visualRestPosition = buttonVisual.localPosition;
        }
    }

    private void Update()
    {
        bool wasOccupied = IsOccupied;

        IsOccupied = CheckOccupied();

        // **乗った瞬間だけ**信号を出す。乗り続けても1回きり
        // （浮き上がりきる前に乗り直した場合は CanSignal が false なので、沈むだけで信号は出ない）
        if (IsOccupied && !wasOccupied && CanSignal)
        {
            Signal();
        }

        UpdateVisual();
    }

    // ------------------------------------------------------------
    // 乗っているか調べる
    // ------------------------------------------------------------

    /// <summary>ボタンの上の箱の中に、何か入っているかを調べる。</summary>
    private bool CheckOccupied()
    {
        // すり抜ける当たり判定（トリガー。見えない範囲など）は、乗った物として数えない
        int count = Physics.OverlapBoxNonAlloc(
            transform.TransformPoint(detectOffset),
            detectSize * 0.5f,
            hits,
            transform.rotation,
            detectMask,
            QueryTriggerInteraction.Ignore);

        for (int i = 0; i < count; i++)
        {
            Collider hit = hits[i];

            if (hit == null)
            {
                continue;
            }

            // 自分自身（土台やボタンの見た目）は数えない
            if (hit.transform.IsChildOf(transform))
            {
                continue;
            }

            return true;
        }

        return false;
    }

    // ------------------------------------------------------------
    // 信号を送る
    // ------------------------------------------------------------

    /// <summary>
    /// 信号を1回送る。**外から押したことにしたいとき**にも使える。
    /// </summary>
    public void Signal()
    {
        // 浮き上がりきるまで、次の信号を出さない
        CanSignal = false;

        foreach (GameObject target in toggleObjects)
        {
            if (target != null)
            {
                target.SetActive(!target.activeSelf);
            }
        }

        if (spawnPrefab != null)
        {
            Transform point = spawnPoint != null ? spawnPoint : transform;
            Instantiate(spawnPrefab, point.position, point.rotation);
        }

        pressed.Invoke();

        Debug.Log($"[STAGE] {name} が押されました");
    }

    // ------------------------------------------------------------
    // 見た目
    // ------------------------------------------------------------

    /// <summary>沈み具合を進めて、ボタンの位置と色に反映する。</summary>
    private void UpdateVisual()
    {
        float target = IsOccupied ? 1f : 0f;

        // 沈むときと浮くときで速さが違う。
        // **浮くのが遅いほど、次に押せるまでの間が長くなる**
        // speed は1秒あたりに進む沈み具合。0秒が入っていたときは 0.01秒で動ききる速さにする（0 で割らないため）
        float time = IsOccupied ? pressTime : riseTime;
        float speed = time > 0f ? 1f / time : 100f;

        PressAmount = Mathf.MoveTowards(PressAmount, target, speed * Time.deltaTime);

        if (buttonVisual != null)
        {
            buttonVisual.localPosition = visualRestPosition + Vector3.down * (PressAmount * pressDepth);
        }

        // **浮き上がりきったら、また押せるようになる**
        // （0.0001 以下＝ほぼ 0。小数の誤差でぴったり 0 にならなくても戻れるように）
        if (!CanSignal && !IsOccupied && PressAmount <= 0.0001f)
        {
            CanSignal = true;
            readyAgain.Invoke();
        }

        UpdateTint();
    }

    /// <summary>押せないあいだ、ボタンの色を暗くする。</summary>
    private void UpdateTint()
    {
        if (!dimWhileUsed || buttonVisual == null)
        {
            return;
        }

        bool shouldDim = !CanSignal;

        // もうその色になっていれば何もしない（切り替わった瞬間だけ色を変える）
        if (shouldDim == usedTint.IsActive)
        {
            return;
        }

        if (shouldDim)
        {
            // 1 は「暗い色そのものにする」（元の色と混ぜない）
            usedTint.Apply(buttonVisual.gameObject, usedColor, 1f);
        }
        else
        {
            usedTint.Clear();
        }
    }

    /// <summary>シーンビューに、反応する範囲を描く。置くときに大きさが分かるように。</summary>
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 0.4f, 0.4f, 0.35f);
        Gizmos.matrix = Matrix4x4.TRS(
            transform.TransformPoint(detectOffset), transform.rotation, Vector3.one);
        Gizmos.DrawCube(Vector3.zero, detectSize);
    }
}
