using UnityEngine;

/// <summary>
/// **プレイヤー（本実装）の巻き取りと投げ。**（2026/10/27・小野田さんの仕様）
///
/// 光線が物資（<see cref="HookableObject"/>）に当たると、<see cref="SweeperShooter"/> が <see cref="Begin"/> を呼ぶ。
///
/// | いつ | どうなる |
/// | --- | --- |
/// | 当たった瞬間から | 物資を**床に置いたまま**、プレイヤーの手前まで引きずる。**距離に関係なく Reel Seconds（初期値1.5秒）で**着く |
/// | 途中でボタンを押す | **後ろへ投げる。** 押した瞬間の位置から、プレイヤーの頭を越える放物線を描いて後ろへ飛ぶ |
/// | 決まった瞬間（クリティカルの受付）に押す | **クリティカルプル。** 光が出て、もっと高く遠くへ飛ぶ |
/// | 最後まで押さない | 手前に着いたところで、**向いている方向（前）へ投げる** |
///
/// 引く時間を固定しているのは、ボタンを押すタイミングを毎回同じにするため。
/// クリティカルの受付が始まった瞬間に、物資の足元に光の輪が広がる（これが合図。ゲージなどの画面の表示は出さない）。
/// 1回押したら、それで決まる（連打で当てられないように）。
///
/// ## オンライン（宇宙ごみのオンラインの体。2026/10/28）
///
/// 物資に <see cref="FishingNetSupply"/> が付いていて通信中なら、今の物資の通信の決まりに合わせる。
/// ・当たったら**ホストに引っ掛けを頼み**、認められて持ち主が自分に移ってから巻き取る（Reel Seconds はそこから数える）。
///   ほかの人が先に引っ掛けていた・返事が来ないときは、投げずにやめる
/// ・途中でホストに外された（爆風など）ら、投げずにやめる
/// ・投げるときは、ここで計算した速さを**そのままホストへ渡す**（落ちた場所・ゴールの判定はホスト）
/// </summary>
public class SweeperReel : MonoBehaviour
{
    [Header("巻き取り")]
    [Tooltip("当たってから手前に着くまでの秒数。距離に関係なく、いつもこの時間で着く")]
    [Min(0.1f)]
    [SerializeField] private float reelSeconds = 1.5f;

    [Tooltip("物資を止める位置（プレイヤーの中心からの距離 m）")]
    [Min(0.3f)]
    [SerializeField] private float stopDistance = 1.4f;

    [Tooltip("引いている間の物資の回転を弱める強さ（0 で弱めない）。転がりすぎるときに上げる")]
    [Min(0f)]
    [SerializeField] private float spinDamping = 6f;

    [Header("後ろへ投げる（途中で押したとき）")]
    [Tooltip("プレイヤーの中心から、後ろのどこに落とすか（m）")]
    [Min(0f)]
    [SerializeField] private float backDistance = 6f;

    [Tooltip("放物線のいちばん高い所（プレイヤーの足元から m）。頭（約2m）より高くする")]
    [Min(0.5f)]
    [SerializeField] private float backArcHeight = 3.2f;

    [Header("前へ投げる（最後まで押さなかったとき）")]
    [Tooltip("前へ飛ばす速さ（m/秒）")]
    [Min(0f)]
    [SerializeField] private float forwardSpeed = 12f;

    [Tooltip("前へ飛ばすときの上向きの速さ（m/秒）。小さいほど床すれすれ")]
    [Min(0f)]
    [SerializeField] private float forwardLift = 1.5f;

    [Header("クリティカルプル")]
    [Tooltip("当たってから、クリティカルの受付が始まるまでの秒数（Reel Seconds より短くする）")]
    [Min(0f)]
    [SerializeField] private float criticalStart = 1.0f;

    [Tooltip("クリティカルの受付の長さ（秒）。短いほど難しい")]
    [Min(0.01f)]
    [SerializeField] private float criticalLength = 0.2f;

    [Tooltip("クリティカルのとき、後ろのどこに落とすか（m）")]
    [Min(0f)]
    [SerializeField] private float criticalBackDistance = 11f;

    [Tooltip("クリティカルのときの放物線のいちばん高い所（足元から m）")]
    [Min(0.5f)]
    [SerializeField] private float criticalArcHeight = 4.5f;

    [Header("クリティカルのエフェクト")]
    [Tooltip("受付が始まった瞬間に、物資の位置に出すエフェクトのプレハブ。空なら仮の光の輪を出す")]
    [SerializeField] private GameObject criticalCuePrefab;

    [Tooltip("クリティカルが決まった瞬間に出すエフェクトのプレハブ。空なら仮の光の輪を出す")]
    [SerializeField] private GameObject criticalHitPrefab;

    [Tooltip("仮の光の輪の色")]
    [SerializeField] private Color flashColor = new Color(1f, 0.82f, 0.2f, 1f);

    [Tooltip("仮の光の輪に使うマテリアル（Art/Materials/SweeperRing.mat）。空なら再生したときに作る")]
    [SerializeField] private Material flashMaterial;

    private Collider playerCollider;
    private HookableObject item;
    private Rigidbody body;
    private float startTime;
    private bool cueShown;

    /// <summary>自分の参加番号を持つ通信上のプレイヤー（オフラインの検証シーンでは null）。</summary>
    private FishingNetPlayer netPlayer;

    /// <summary>巻き取っている物資の通信の部品。オンラインで巻き取っている間だけ入る。</summary>
    private FishingNetSupply supply;

    /// <summary>ホストの返事（引っ掛けてよいか）を待っている間。この間は引かない。</summary>
    private bool waitingForClaim;

    /// <summary>返事を待ち始めてからの秒数。</summary>
    private float claimWait;

    /// <summary>これだけ待っても返事が来なければ、あきらめて光線を戻す（秒）。</summary>
    private const float ClaimTimeout = 1.5f;

    /// <summary>エフェクトの種類（オンラインでほかの人の画面にも出すときに使う）。</summary>
    public enum Effect
    {
        CriticalCue,
        CriticalHit
    }

    /// <summary>エフェクトを出した瞬間に呼ばれる（<see cref="SweeperNetSync"/> がほかの人へ知らせる）。</summary>
    public event System.Action<Effect, HookableObject> EffectShown;

    /// <summary>巻き取っている最中か。</summary>
    public bool IsActive => item != null;

    /// <summary>巻き取っている物資。</summary>
    public HookableObject Item => item;

    /// <summary>当たってから（オンラインでは、ホストに認められてから）の秒数。</summary>
    public float Elapsed => IsActive && !waitingForClaim ? Time.time - startTime : 0f;

    /// <summary>いまクリティカルの受付中か。</summary>
    public bool InCriticalWindow => IsActive && !waitingForClaim && Elapsed >= criticalStart && Elapsed <= criticalStart + criticalLength;

    /// <summary>
    /// その物資を引っ掛けてよいか。オンラインで**ほかの人が引っ掛けている物は false**（すり抜ける）。
    /// オフラインの検証シーンでは、いつも true。
    /// </summary>
    public bool CanHook(HookableObject target)
    {
        FishingNetSupply netSupply = target != null ? target.GetComponent<FishingNetSupply>() : null;
        if (netSupply == null || !netSupply.IsSpawned || !IsOnline)
        {
            return true;
        }

        return !netSupply.IsClaimed || netSupply.HookedByPlayerIndex == MyIndex;
    }

    /// <summary>通信上のプレイヤーとして動いているか。</summary>
    private bool IsOnline => netPlayer != null && netPlayer.IsSpawned;

    /// <summary>自分の参加番号（物資の通信で「誰が引っ掛けているか」に使う）。</summary>
    private int MyIndex => netPlayer != null ? Mathf.Max(0, netPlayer.PlayerIndex) : 0;

    /// <summary>最後の投げがクリティカルだったか（エフェクトや検証用の表示に使う）。</summary>
    public bool LastWasCritical { get; private set; }

    /// <summary>最後の投げの結果（「後ろ」「前」「クリティカル」）。検証用の表示に使う。</summary>
    public string LastResult { get; private set; } = "";

    private void Awake()
    {
        playerCollider = GetComponent<Collider>();
        netPlayer = GetComponent<FishingNetPlayer>();
    }

    /// <summary>巻き取りを始める。物資の Rigidbody の物理はそのまま使う（床の上を引きずる）。</summary>
    public void Begin(HookableObject target)
    {
        if (target == null || target.Body == null)
        {
            return;
        }

        item = target;
        body = target.Body;
        startTime = Time.time;
        cueShown = false;
        waitingForClaim = false;
        supply = null;

        // オンラインなら、ホストに引っ掛けを頼む。認められるまでは引かない
        FishingNetSupply netSupply = target.GetComponent<FishingNetSupply>();
        if (IsOnline && netSupply != null && netSupply.IsSpawned)
        {
            supply = netSupply;
            waitingForClaim = true;
            claimWait = 0f;
            supply.RequestClaim(MyIndex);
        }

        item.SetHooked(true);
        // 手前まで来たときに、自分の体とぶつかって跳ねないようにする
        ThrowPassThrough.Hold(item.gameObject, playerCollider);
        body.WakeUp();
    }

    /// <summary>
    /// 巻き取りの途中でボタンが押された。後ろへ投げる（受付中ならクリティカル）。
    /// オンラインでホストの返事を待っている間（ほんの一瞬）は、押しても何もしない。
    /// </summary>
    public void Press()
    {
        if (!IsActive || waitingForClaim)
        {
            return;
        }

        bool critical = InCriticalWindow;
        ThrowBack(critical);
        Finish(critical ? "クリティカル" : "後ろ", critical, false);
    }

    /// <summary>投げずにやめる（物資を離すだけ）。オンラインでは、離したことをホストへ知らせる。</summary>
    public void Cancel()
    {
        if (IsActive)
        {
            Finish("やめた", false, true);
        }
    }

    private void Update()
    {
        if (!IsActive)
        {
            return;
        }

        // 物資が消えた（ゴールに入った・爆発した・場外に落ちた）ら、投げずに終わる
        if (item.IsVanished || !item.gameObject.activeInHierarchy)
        {
            Finish("消えた", false, false);
            return;
        }

        if (supply != null && !UpdateOnlineClaim())
        {
            return;
        }

        if (!cueShown && Elapsed >= criticalStart)
        {
            cueShown = true;
            PlayEffect(Effect.CriticalCue);
        }

        if (Elapsed >= reelSeconds)
        {
            ThrowForward();
            Finish("前", false, false);
        }
    }

    /// <summary>
    /// オンラインで、ホストの記録を見る。**巻き取ってよければ true。**
    /// 返事待ちのうちは false（認められた瞬間から時間を数える）。ほかの人に取られた・ホストに外されたら、投げずにやめて false。
    /// </summary>
    private bool UpdateOnlineClaim()
    {
        bool mine = supply.IsOwner && supply.HookedByPlayerIndex == MyIndex;

        if (waitingForClaim)
        {
            if (mine)
            {
                waitingForClaim = false;
                startTime = Time.time;
                body.WakeUp();
                return true;
            }

            claimWait += Time.deltaTime;
            bool takenByOther = supply.IsClaimed && supply.HookedByPlayerIndex != MyIndex;
            if (takenByOther)
            {
                Finish("取られた", false, false);
            }
            else if (claimWait >= ClaimTimeout)
            {
                // 返事が来ない。あとから認められても持ったままにならないよう、離したと送っておく
                Finish("返事なし", false, true);
            }
            return false;
        }

        if (!mine)
        {
            // 爆風などで、ホストに外された
            Finish("外れた", false, false);
            return false;
        }

        return true;
    }

    private void FixedUpdate()
    {
        if (!IsActive || body == null || waitingForClaim)
        {
            return;
        }

        // 手前の止める位置を、毎回いまの位置関係から決める（プレイヤーが向きを変えても、物資は引かれる方へ来る）
        Vector3 player = transform.position;
        Vector3 fromPlayer = body.position - player;
        fromPlayer.y = 0f;
        Vector3 toward = fromPlayer.sqrMagnitude > 0.0001f ? fromPlayer.normalized : transform.forward;
        Vector3 goal = player + toward * stopDistance;

        Vector3 rest = goal - body.position;
        rest.y = 0f;

        // 残りの距離を残りの時間で割る＝距離に関係なく、決まった時間で着く速さ
        float remaining = Mathf.Max(Time.fixedDeltaTime, startTime + reelSeconds - Time.time);
        Vector3 velocity = rest / remaining;

        if (body.isKinematic)
        {
            body.MovePosition(body.position + velocity * Time.fixedDeltaTime);
            return;
        }

        // 上下は物理（重力）のまま、横だけ決める＝床に置いたまま引きずる
        body.linearVelocity = new Vector3(velocity.x, body.linearVelocity.y, velocity.z);

        if (spinDamping > 0f)
        {
            body.angularVelocity *= Mathf.Exp(-spinDamping * Time.fixedDeltaTime);
        }
    }

    /// <summary>押した瞬間の位置から、プレイヤーの頭を越える放物線で後ろへ投げる。</summary>
    private void ThrowBack(bool critical)
    {
        if (body == null)
        {
            return;
        }

        float distance = critical ? criticalBackDistance : backDistance;
        float arcHeight = critical ? criticalArcHeight : backArcHeight;

        Vector3 from = body.position;
        Vector3 back = -Flat(transform.forward);
        Vector3 landing = transform.position + back * distance;
        landing.y = from.y;

        float gravity = Mathf.Max(0.1f, -Physics.gravity.y);
        float apexY = Mathf.Max(transform.position.y + arcHeight, from.y + 0.5f);

        // いちばん高い所まで上がる時間と、そこから落ちる時間を足したのが、飛んでいる時間
        float upSpeed = Mathf.Sqrt(2f * gravity * (apexY - from.y));
        float flightTime = upSpeed / gravity + Mathf.Sqrt(2f * (apexY - landing.y) / gravity);

        Vector3 across = landing - from;
        across.y = 0f;
        Vector3 velocity = across / flightTime + Vector3.up * upSpeed;

        Launch(velocity);

        if (critical)
        {
            PlayEffect(Effect.CriticalHit);
        }
    }

    /// <summary>向いている方向（前）へ、床すれすれに投げる。</summary>
    private void ThrowForward()
    {
        if (body == null)
        {
            return;
        }

        Launch(Flat(transform.forward) * forwardSpeed + Vector3.up * forwardLift);
    }

    private void Launch(Vector3 velocity)
    {
        if (supply != null)
        {
            // オンライン：飛ばすのはホスト。計算した速さをそのまま渡し、離した位置から飛ばしてもらう
            supply.RequestThrow(Vector3.zero, 0f, 0f, MyIndex, true, velocity, body.angularVelocity, true, body.position);
        }

        if (body.isKinematic)
        {
            return;
        }

        // 自分の画面でも同じ速さで飛ばしておく（ホストに持ち主が戻るまでの間、止まって見えないように）
        body.linearVelocity = velocity;
        body.WakeUp();
    }

    /// <summary>
    /// 巻き取りを終える。<paramref name="releaseOnline"/> が true なら、オンラインで「離した」とホストへ知らせる
    /// （投げたとき・取られた／外されたとき・消えたときは知らせない）。
    /// </summary>
    private void Finish(string result, bool critical, bool releaseOnline)
    {
        LastResult = result;
        LastWasCritical = critical;

        if (releaseOnline && supply != null && supply.IsSpawned)
        {
            supply.RequestRelease(Vector3.zero, 0f, MyIndex);
        }

        if (item != null)
        {
            item.SetHooked(false);
            // 投げた直後に自分の体に当たらないようにする（離れたら元に戻る）
            ThrowPassThrough.Apply(item.gameObject, playerCollider);
        }

        item = null;
        body = null;
        supply = null;
        waitingForClaim = false;
    }

    /// <summary>巻き取っている物資にエフェクトを出し、出したことを知らせる（オンラインではほかの人の画面にも出る）。</summary>
    private void PlayEffect(Effect kind)
    {
        if (item == null)
        {
            return;
        }

        ShowEffect(kind, item);
        EffectShown?.Invoke(kind, item);
    }

    /// <summary>
    /// **その物資にエフェクトを出す。** 合図（<see cref="Effect.CriticalCue"/>）は物資に付いていき、決まったときの光はその場に出る。
    /// オンラインのほかの人の体では、本人から知らせが来たときに <see cref="SweeperNetSync"/> が呼ぶ。
    /// </summary>
    public void ShowEffect(Effect kind, HookableObject target)
    {
        if (target == null)
        {
            return;
        }

        if (kind == Effect.CriticalCue)
        {
            SpawnEffect(target, criticalCuePrefab, 1.2f, 3.2f, criticalLength + 0.15f, true);
        }
        else
        {
            SpawnEffect(target, criticalHitPrefab, 1.5f, 6f, 0.5f, false);
        }
    }

    /// <summary>エフェクトのプレハブがあればそれを、無ければ仮の光の輪を、物資の足元に出す。<paramref name="followItem"/> なら光の輪が物資に付いていく。</summary>
    private void SpawnEffect(HookableObject target, GameObject prefab, float startSize, float endSize, float duration, bool followItem)
    {
        Vector3 position = target.transform.position;
        Bounds bounds = default;
        Collider itemCollider = target.GetComponent<Collider>();
        if (itemCollider != null)
        {
            bounds = itemCollider.bounds;
            position.y = bounds.min.y + 0.03f;
        }

        if (prefab != null)
        {
            Instantiate(prefab, position, Quaternion.identity);
            return;
        }

        float scale = itemCollider != null ? Mathf.Max(bounds.size.x, bounds.size.z) : 1f;
        SweeperFlash.Spawn(position, followItem ? target.transform : null, flashColor, startSize * scale, endSize * scale, duration, flashMaterial);
    }

    private static Vector3 Flat(Vector3 direction)
    {
        direction.y = 0f;
        return direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector3.forward;
    }
}
