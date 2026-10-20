using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// **決まった道すじを行き来する障害物。**（長めの長方形の箱を想定）
///
/// インスペクターの <see cref="points"/> に通る場所を並べると、その順に動く。
/// プレイヤーは**通り抜けられず、動いてきた障害物に押し出される。**
/// 物資（Rigidbody）も押しのける。
///
/// **位置は「時計」から計算で決めている。**
/// 同じ時刻なら必ず同じ場所にいるので、オンラインでは全員で共有している時計
/// （Netcode の ServerTime）を読むだけで、**位置を通信で送らなくても全員の画面でそろう。**
/// そのため NetworkObject を付ける必要はない。1人用のシーンにもそのまま置ける。
///
/// 使い方：Cube などの BoxCollider が付いた物にこれを付けて、<see cref="points"/> を入れる。
/// ※ 回転は置いたときの向きのまま変わらない。**傾けずに、Y軸（水平）の回転だけで置くこと。**
/// </summary>
[RequireComponent(typeof(BoxCollider))]
[RequireComponent(typeof(Rigidbody))]
public class MovingObstacle : MonoBehaviour
{
    /// <summary>道すじの回り方。</summary>
    public enum PathMode
    {
        /// <summary>最後まで行ったら、来た道を戻る（A→B→C→B→A…）</summary>
        PingPong,

        /// <summary>最後まで行ったら、最初の場所へ直行してぐるぐる回る（A→B→C→A…）</summary>
        Loop
    }

    [Header("道すじ")]
    [Tooltip("通る場所。**置いた位置からのずれ（メートル。ワールドの東西＝X／南北＝Z）**で書く。" +
             "こうしておくと、障害物ごと動かしても道すじが一緒についてくる")]
    [SerializeField] private Vector3[] points =
    {
        new Vector3(-6f, 0f, 0f),
        new Vector3(6f, 0f, 0f)
    };

    [Tooltip("最後の場所まで行ったあと、戻るか（PingPong）、最初へ直行するか（Loop）")]
    [SerializeField] private PathMode mode = PathMode.PingPong;

    [Header("動き方")]
    [Tooltip("動く速さ（1秒あたりのメートル）")]
    [SerializeField] private float moveSpeed = 3f;

    [Tooltip("それぞれの場所に着いたとき、止まっている秒数")]
    [SerializeField] private float waitSeconds = 0.5f;

    [Tooltip("ONにすると、動き出しと止まる直前がゆっくりになる")]
    [SerializeField] private bool easeInOut = false;

    [Tooltip("動きを何秒ぶん先へずらすか。同じ道すじに2つ置くときに、間をあけるのに使う")]
    [SerializeField] private float startOffsetSeconds = 0f;

    [Header("プレイヤーを押し出す")]
    [Tooltip("押し出すときに、ぴったりより少しだけ余分に離す距離（メートル）。引っかかり防止")]
    [SerializeField] private float pushSkin = 0.02f;

    /// <summary>1区間ぶんの動き。「止まる → 動く」の順。</summary>
    private struct Leg
    {
        public Vector3 From;
        public Vector3 To;
        public double Wait;
        public double Duration;
    }

    /// <summary>道すじを「止まる→動く」の区間に分けたもの。1周ぶん並んでいる。</summary>
    private readonly List<Leg> legs = new List<Leg>();

    /// <summary>押し出す相手を探すときの入れ物。一度に拾えるのは 16 個まで（プレイヤー＋近くの物資）。</summary>
    private readonly Collider[] overlapHits = new Collider[16];

    private Rigidbody body;
    private BoxCollider box;

    /// <summary>置いたときの位置と向き。<see cref="points"/> はここからのずれで書かれている。</summary>
    private Vector3 basePosition;
    private Quaternion baseRotation;

    /// <summary>1周にかかる秒数（止まる時間も含む）。0 なら動かない。</summary>
    private double cycleSeconds;

    // ---- 時計 ----

    /// <summary>1人用のとき、動き始めた時刻（このPCの時計）。ここを 0 秒として数える。</summary>
    private double localStartTime;

    /// <summary>「全員で共有している時計 − このPCの時計」の差（秒）。オンラインのときだけ使う。</summary>
    private double serverClockOffset;

    /// <summary>いま共有の時計で動いているか（オンラインでつながっているか）。</summary>
    private bool useServerClock;

    /// <summary>共有の時計とのずれがこれ（秒）より大きければ、少しずつ寄せずに一気に合わせる。</summary>
    private const double ClockSnapThresholdSeconds = 0.5d;

    /// <summary>ふだんの時計合わせで、1フレームにずれの何割を寄せるか（0〜1。小さいほど滑らか）。</summary>
    private const double ClockBlendRate = 0.1d;

    private void Awake()
    {
        body = GetComponent<Rigidbody>();
        box = GetComponent<BoxCollider>();

        // **物理で押されて動くのではなく、自分で決めた場所へ動く**ので、キネマティックにする。
        // MovePosition で動かすと、ぶつかった物資はちゃんと押しのけられる
        body.isKinematic = true;
        body.useGravity = false;
        body.interpolation = RigidbodyInterpolation.Interpolate;

        basePosition = transform.position;
        baseRotation = transform.rotation;
        localStartTime = Time.timeAsDouble;

        BuildLegs();
    }

    /// <summary>通る場所の並びから、「止まる→動く」の区間の一覧を作る。</summary>
    private void BuildLegs()
    {
        legs.Clear();
        cycleSeconds = 0d;

        if (points == null || points.Length < 2)
        {
            Debug.LogWarning($"{name}: 通る場所（Points）が2つ以上ないので、障害物は動きません。", this);
            return;
        }

        if (moveSpeed <= 0f)
        {
            Debug.LogWarning($"{name}: 速さ（Move Speed）が0以下なので、障害物は動きません。", this);
            return;
        }

        int last = points.Length - 1;

        if (mode == PathMode.Loop)
        {
            for (int i = 0; i <= last; i++)
            {
                AddLeg(points[i], points[(i + 1) % points.Length]);
            }
        }
        else
        {
            // 行き
            for (int i = 0; i < last; i++)
            {
                AddLeg(points[i], points[i + 1]);
            }
            // 帰り
            for (int i = last; i > 0; i--)
            {
                AddLeg(points[i], points[i - 1]);
            }
        }
    }

    /// <summary>区間を1つ足し、1周の長さにもその時間を加える。止まる秒数がマイナスなら 0 として扱う。</summary>
    private void AddLeg(Vector3 from, Vector3 to)
    {
        var leg = new Leg
        {
            From = from,
            To = to,
            Wait = Mathf.Max(0f, waitSeconds),
            Duration = Vector3.Distance(from, to) / moveSpeed
        };

        legs.Add(leg);
        cycleSeconds += leg.Wait + leg.Duration;
    }

    /// <summary>時計合わせだけ Update で行う（共有の時計は画面の更新ごとにしか進まないため）。</summary>
    private void Update()
    {
        UpdateServerClockOffset();
    }

    /// <summary>物理の更新に合わせて位置を決める（Rigidbody の MovePosition は FixedUpdate で呼ぶ決まり）。</summary>
    private void FixedUpdate()
    {
        if (cycleSeconds <= 0d)
        {
            return;
        }

        Vector3 position = basePosition + EvaluateOffset(CurrentTime());

        body.MovePosition(position);

        // CharacterController は、動いてきた物に押されてくれない（自分で Move したときしか動かない）。
        // なので、**重なったプレイヤーをこちらから押し出す**
        PushCharacters(position);
    }

    // ------------------------------------------------------------
    // 時計
    // ------------------------------------------------------------

    /// <summary>
    /// **オンラインでつながっているときは、全員で共有している時計との差を覚えておく。**
    ///
    /// ServerTime は画面の更新（Update）ごとにしか進まないので、
    /// 物理の更新（FixedUpdate）でそのまま読むとカクつく。
    /// そこで「共有の時計 − このPCの時計」の差だけをここで測り、
    /// 動かすときは「このPCの時計 ＋ 差」で計算する。
    /// </summary>
    private void UpdateServerClockOffset()
    {
        NetworkManager network = NetworkManager.Singleton;

        if (network == null || !network.IsListening)
        {
            useServerClock = false;
            return;
        }

        double target = network.ServerTime.Time - Time.timeAsDouble;

        // つながった直後や大きくずれたときはそのまま合わせ、ふだんは少しずつ寄せる（ガタつき防止）
        if (!useServerClock || System.Math.Abs(target - serverClockOffset) > ClockSnapThresholdSeconds)
        {
            serverClockOffset = target;
        }
        else
        {
            serverClockOffset += (target - serverClockOffset) * ClockBlendRate;
        }

        useServerClock = true;
    }

    /// <summary>障害物の動きに使う、いまの時刻（秒）。</summary>
    private double CurrentTime()
    {
        double now = Time.fixedTimeAsDouble;
        double time = useServerClock ? now + serverClockOffset : now - localStartTime;
        return time + startOffsetSeconds;
    }

    // ------------------------------------------------------------
    // 位置の計算
    // ------------------------------------------------------------

    /// <summary>時刻 <paramref name="time"/> に、置いた位置からどれだけずれた場所にいるか。</summary>
    private Vector3 EvaluateOffset(double time)
    {
        // 1周の中のどこか（0〜cycleSeconds）に直す。
        // 先へずらす秒数（startOffsetSeconds）がマイナスだと余りもマイナスになるので、1周ぶん足して戻す
        double t = time % cycleSeconds;
        if (t < 0d)
        {
            t += cycleSeconds;
        }

        // 区間を頭から順にたどり、t がどの区間の「止まっている間」か「動いている間」かを探す
        foreach (Leg leg in legs)
        {
            if (t < leg.Wait)
            {
                return leg.From;
            }
            t -= leg.Wait;

            if (t < leg.Duration)
            {
                float progress = (float)(t / leg.Duration);
                if (easeInOut)
                {
                    progress = Mathf.SmoothStep(0f, 1f, progress);
                }
                return Vector3.Lerp(leg.From, leg.To, progress);
            }
            t -= leg.Duration;
        }

        // 小数の誤差でどの区間にも入らなかったときの保険（1周の終わり ＝ 最後の区間の終点）
        return legs[legs.Count - 1].To;
    }

    // ------------------------------------------------------------
    // プレイヤーを押し出す
    // ------------------------------------------------------------

    /// <summary>障害物に重なっている CharacterController を、障害物の外へ押し出す。</summary>
    private void PushCharacters(Vector3 position)
    {
        Vector3 scale = transform.lossyScale;
        Vector3 halfSize = Vector3.Scale(box.size, scale) * 0.5f;
        Vector3 center = position + baseRotation * Vector3.Scale(box.center, scale);

        // 少し広めに探して、触れかけのプレイヤーも拾う（横に 1 m・上下に 0.2 m 広げる）
        Vector3 searchHalf = halfSize + new Vector3(1f, 0.2f, 1f);
        int count = Physics.OverlapBoxNonAlloc(center, searchHalf, overlapHits, baseRotation,
            ~0, QueryTriggerInteraction.Ignore);

        for (int i = 0; i < count; i++)
        {
            // **有効な CharacterController だけ**を押す。
            // オンラインでは「他の人のぶん」の CharacterController は切ってあるので、
            // 各PCが自分のプレイヤーだけを押すことになる（押された位置は本人が全員に配る）
            if (overlapHits[i] is CharacterController character && character.enabled)
            {
                PushOut(character, center, halfSize);
            }
        }
    }

    /// <summary>
    /// 1人ぶんを押し出す。
    /// 真上から見て、**長方形とプレイヤーの円**が重なっていたら、重なりが無くなるだけ動かす。
    /// </summary>
    private void PushOut(CharacterController character, Vector3 center, Vector3 halfSize)
    {
        Vector3 characterCenter = character.transform.TransformPoint(character.center);

        // 高さが重なっていなければ押さない（障害物の上に乗っているときなど）
        Vector3 characterScale = character.transform.lossyScale;
        float halfHeight = character.height * characterScale.y * 0.5f;
        if (characterCenter.y - halfHeight > center.y + halfSize.y ||
            characterCenter.y + halfHeight < center.y - halfSize.y)
        {
            return;
        }

        // 真上から見たプレイヤーの円の半径（拡大率と、CharacterController の皮の厚み skinWidth を含める）
        float radius = character.radius * Mathf.Max(characterScale.x, characterScale.z)
                     + character.skinWidth;

        // 障害物の向きに合わせた座標で考える（こうすると長方形の辺が軸に揃う）
        Vector3 local = Quaternion.Inverse(baseRotation) * (characterCenter - center);

        // 長方形の中で、プレイヤーの中心に一番近い点を求め、そこからプレイヤーの中心へ向かうベクトル（away）を出す。
        // 中心が長方形の中にあれば away は 0 になる
        float closestX = Mathf.Clamp(local.x, -halfSize.x, halfSize.x);
        float closestZ = Mathf.Clamp(local.z, -halfSize.z, halfSize.z);
        Vector2 away = new Vector2(local.x - closestX, local.z - closestZ);
        float distance = away.magnitude;

        Vector3 pushLocal;

        if (distance > 0.0001f)
        {
            // 長方形の外側にいる：近すぎるぶんだけ離す
            if (distance >= radius)
            {
                return;
            }
            Vector2 push = away / distance * (radius - distance + pushSkin);
            pushLocal = new Vector3(push.x, 0f, push.y);
        }
        else
        {
            // 中心が長方形の中に入り込んでいる：一番近い辺の外へ出す
            float toSideX = halfSize.x - Mathf.Abs(local.x);
            float toSideZ = halfSize.z - Mathf.Abs(local.z);

            if (toSideX < toSideZ)
            {
                float sign = local.x >= 0f ? 1f : -1f;
                pushLocal = new Vector3(sign * (toSideX + radius + pushSkin), 0f, 0f);
            }
            else
            {
                float sign = local.z >= 0f ? 1f : -1f;
                pushLocal = new Vector3(0f, 0f, sign * (toSideZ + radius + pushSkin));
            }
        }

        character.Move(baseRotation * pushLocal);
    }

    // ------------------------------------------------------------
    // シーン画面での見え方
    // ------------------------------------------------------------

    /// <summary>シーン画面で、道すじと、各場所での障害物の大きさを線で描く（ゲーム画面には出ない）。</summary>
    private void OnDrawGizmosSelected()
    {
        if (points == null || points.Length == 0)
        {
            return;
        }

        Vector3 origin = Application.isPlaying ? basePosition : transform.position;
        Quaternion rotation = Application.isPlaying ? baseRotation : transform.rotation;
        BoxCollider gizmoBox = box != null ? box : GetComponent<BoxCollider>();
        Vector3 size = gizmoBox != null ? Vector3.Scale(gizmoBox.size, transform.lossyScale) : transform.lossyScale;

        Gizmos.color = new Color(1f, 0.5f, 0.1f, 1f);

        for (int i = 0; i < points.Length; i++)
        {
            Vector3 point = origin + points[i];

            Gizmos.matrix = Matrix4x4.TRS(point, rotation, Vector3.one);
            Gizmos.DrawWireCube(Vector3.zero, size);
            Gizmos.matrix = Matrix4x4.identity;

            bool hasNext = i < points.Length - 1 || mode == PathMode.Loop;
            if (hasNext && points.Length > 1)
            {
                Gizmos.DrawLine(point, origin + points[(i + 1) % points.Length]);
            }
        }
    }
}
