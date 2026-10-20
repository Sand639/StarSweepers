using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// **触れた瞬間に爆発して、触れた人を吹っ飛ばす爆弾。**
///
/// | 決まり | 内容 |
/// | --- | --- |
/// | 置き方 | **空中にも置ける。落ちてこない**（重力の影響を受けない） |
/// | 爆発 | **人が触れたら、その瞬間に爆発する** |
/// | 飛ぶ向き | **爆弾の中心から、触れた人の中心へ向かう向き**。上を向きすぎるときは決めた角度までねかせる（踏んでも真上に飛ばない） |
/// | 飛ぶ強さ | **重力が重いほど弱く、軽いほど強い** |
///
/// ## 使い方
///
/// **プレハブ（`Assets/Prefabs/GravityRace/Bomb.prefab`）を置くだけ。** つなぐものは無い。
/// メニューの `Tools > StarSweepers > アーカイブ > 爆弾 > 爆弾を置く` でも置ける。
///
/// ## 「触れた」の見分け方
///
/// **爆弾の周りの球の中に、人（CharacterController）が入ったか**を毎フレーム調べている。
/// 爆弾そのものには**当たり判定を付けていない。**
/// 付けると、人が爆弾に押し戻されて触れる前に止まってしまったり、
/// カメラが爆弾を「壁」と思って寄ってきたりするため。
///
/// ## 吹っ飛ばせる相手
///
/// **<see cref="ILaunchable"/> を持っている人だけ。** 今はロボットの体が持っている。
/// 持っていない人が触れても爆発はするが、飛ばない（コンソールに1回だけ知らせる）。
/// </summary>
public class Bomb : MonoBehaviour
{
    [Header("触れたと判定する範囲")]
    [Tooltip("**爆弾の中心から、この距離まで人が近づいたら爆発する**（メートル）。見た目より少し大きめにする")]
    [Min(0.05f)]
    [SerializeField] private float touchRadius = 0.6f;

    [Header("吹っ飛ばす強さ")]
    [Tooltip("**ふつうの重力（9.81）のときに飛ばす速さ**（1秒あたりのメートル）")]
    [Min(0f)]
    [SerializeField] private float launchSpeed = 9f;

    [Tooltip("**重力にどれだけ左右されるか。**\n" +
             "1 … 重力が半分なら2倍飛ぶ（反比例）\n" +
             "0.5 … 重力が半分なら約1.4倍飛ぶ（ゆるやか）\n" +
             "0 … 重力に関係なく同じ強さ")]
    [Range(0f, 2f)]
    [SerializeField] private float gravityInfluence = 1f;

    [Tooltip("重力がどれだけ重くても、**ふつうの何倍までは弱くしない**か")]
    [Range(0.05f, 1f)]
    [SerializeField] private float minMultiplier = 0.25f;

    [Tooltip("重力がどれだけ軽くても、**ふつうの何倍までしか強くしない**か。月（1.6）だと計算上は約6倍になるので、ここで抑える")]
    [Range(1f, 10f)]
    [SerializeField] private float maxMultiplier = 3f;

    [Tooltip("**飛ぶ向きに足す、上向きの量。** 0なら「爆弾から人へ」の向きそのまま。\n" +
             "地面の爆弾で真横に滑るだけになるときは、0.3 くらい入れると浮き上がる")]
    [Range(0f, 1f)]
    [SerializeField] private float upwardBias;

    [Header("飛ぶ角度")]
    [Tooltip("**飛ぶ向きの、地面からの角度の上限**（度）。\n" +
             "爆弾を踏んだときに真上へ飛ばず、**この角度でななめに飛ぶ**。\n" +
             "45 … ななめ45度まで　60 … 少し急　90 … 上限なし（真上にも飛ぶ）")]
    [Range(0f, 90f)]
    [SerializeField] private float maxLaunchAngle = 45f;

    [Header("見た目")]
    [Tooltip("爆発の見た目（広がって消える球）のマテリアル")]
    [SerializeField] private Material blastMaterial;

    [Tooltip("爆発の見た目が広がる大きさ（直径・メートル）")]
    [Min(0.1f)]
    [SerializeField] private float blastSize = 2.5f;

    [Tooltip("爆発の見た目が消えるまでの秒数")]
    [Min(0.05f)]
    [SerializeField] private float blastSeconds = 0.35f;

    [Tooltip("**浮いていると分かるように、ゆっくり上下させる幅**（メートル）。0で止まる")]
    [Min(0f)]
    [SerializeField] private float bobHeight = 0.08f;

    [Header("元に戻る")]
    [Tooltip("**爆発してから元に戻るまでの秒数。** 0なら戻らない（やり直しのときだけ戻る）")]
    [Min(0f)]
    [SerializeField] private float respawnSeconds;

    [Tooltip("爆発するたびにコンソールへ書き出す")]
    [SerializeField] private bool logExplosions = true;

    /// <summary>もう爆発したか。</summary>
    public bool HasExploded { get; private set; }

    /// <summary>
    /// 近くの当たり判定を探すときの入れ物。**毎フレーム作り直さないよう、全部の爆弾で使い回している。**
    /// 一度に拾えるのは16個まで。
    /// </summary>
    private static readonly Collider[] Hits = new Collider[16];

    /// <summary>
    /// 「吹っ飛ばせない人が触れた」とコンソールに知らせたか。
    /// **全部の爆弾で1回きり**にするため static にしている（毎回出すとコンソールが埋まる）。
    /// </summary>
    private static bool warnedNotLaunchable;

    /// <summary>この爆発で、もう飛ばした相手。1人を2回飛ばさないために覚えておく。</summary>
    private readonly HashSet<ILaunchable> launchedThisTime = new HashSet<ILaunchable>();

    /// <summary>爆弾の見た目（子も含む）。爆発したら消し、戻ったら出す。</summary>
    private Renderer[] visuals;

    /// <summary>置いた位置。上下に揺らすときの中心で、戻るときもここへ戻す。</summary>
    private Vector3 restLocalPosition;

    /// <summary>上下の揺れの始まりのずれ（ラジアン）。</summary>
    private float bobPhase;

    /// <summary>爆発してから元に戻るまでの残り秒数。</summary>
    private float respawnTimer;

    private void Awake()
    {
        visuals = GetComponentsInChildren<Renderer>(true);
        restLocalPosition = transform.localPosition;

        // 並べて置いたときに、みんなで同じ動きにならないようにずらす
        bobPhase = Random.Range(0f, Mathf.PI * 2f);
    }

    private void OnEnable()
    {
        StageReset.Requested += Restore;
    }

    private void OnDisable()
    {
        StageReset.Requested -= Restore;
    }

    private void Update()
    {
        if (GamePause.IsPaused)
        {
            return;
        }

        if (HasExploded)
        {
            UpdateRespawn();
            return;
        }

        Bob();
        CheckTouch();
    }

    // ------------------------------------------------------------
    // 触れたか
    // ------------------------------------------------------------

    /// <summary>
    /// 爆発する前、毎フレーム呼ぶ。**周りの球の中に人が1人でも入っていたら、爆発させる。**
    /// </summary>
    private void CheckTouch()
    {
        int count = CollectNearby(transform.position);

        for (int i = 0; i < count; i++)
        {
            if (Hits[i] is CharacterController)
            {
                Explode();
                return;
            }
        }
    }

    /// <summary>
    /// <paramref name="center"/> から <see cref="touchRadius"/> の球の中にある当たり判定を、<see cref="Hits"/> に集める。
    /// 戻り値は見つかった数。~0 は「すべてのレイヤー」で、すり抜ける当たり判定（トリガー）は数えない。
    /// </summary>
    private int CollectNearby(Vector3 center)
    {
        return Physics.OverlapSphereNonAlloc(
            center, touchRadius, Hits, ~0, QueryTriggerInteraction.Ignore);
    }

    // ------------------------------------------------------------
    // 爆発
    // ------------------------------------------------------------

    /// <summary>
    /// **爆発させる。** 外から爆発させたいとき（連鎖など）にも呼べる。
    /// 触れている人を全員吹っ飛ばす。
    /// </summary>
    public void Explode()
    {
        if (HasExploded)
        {
            return;
        }

        HasExploded = true;
        respawnTimer = respawnSeconds;

        Vector3 center = transform.position;
        float speed = CurrentLaunchSpeed();

        launchedThisTime.Clear();

        int count = CollectNearby(center);

        for (int i = 0; i < count; i++)
        {
            if (!(Hits[i] is CharacterController character))
            {
                continue;
            }

            LaunchCharacter(character, center, speed);
        }

        SetVisible(false);
        SpawnBlast(center);

        if (logExplosions)
        {
            Debug.Log($"[BOMB] {name}：爆発（重力 {Mathf.Abs(WorldGravity.Value):0.0} → 飛ばす速さ {speed:0.0}）", this);
        }
    }

    /// <summary>
    /// **いまの重力で、どれだけの速さで飛ばすか。**
    ///
    /// ふつうの重力を基準に、`(ふつう ÷ いま) の gravityInfluence 乗` を掛ける。
    /// 重いほど小さく、軽いほど大きくなる。行き過ぎないように上下を抑えている。
    /// </summary>
    public float CurrentLaunchSpeed()
    {
        // Scale は「ふつうの何倍の重力か」（地球なら 1）。0 で割らないよう、下を 0.01 で止める
        float scale = Mathf.Max(0.01f, WorldGravity.Scale);
        float multiplier = Mathf.Pow(1f / scale, gravityInfluence);

        return launchSpeed * Mathf.Clamp(multiplier, minMultiplier, maxMultiplier);
    }

    /// <summary>
    /// 触れていた1人を、<paramref name="center"/>（爆弾の中心）から離れる向きへ、<paramref name="speed"/> の速さで飛ばす。
    /// 実際に飛ぶ動きは、相手の <see cref="ILaunchable"/> に任せている。
    /// </summary>
    private void LaunchCharacter(CharacterController character, Vector3 center, float speed)
    {
        // 当たり判定は体の子に付いていることがあるので、親までさかのぼって探す
        ILaunchable target = character.GetComponentInParent<ILaunchable>();

        if (target == null)
        {
            if (!warnedNotLaunchable)
            {
                warnedNotLaunchable = true;
                Debug.LogWarning($"[BOMB] {character.name} は吹っ飛ばせません（ILaunchable を持っていない）。" +
                                 "爆発はしましたが、飛びません。", character);
            }

            return;
        }

        // 1つの体に当たり判定が2つあっても、1回だけ飛ばす
        if (!launchedThisTime.Add(target))
        {
            return;
        }

        // **爆弾の中心 → 人の中心** の向き。真上に重なっていたら真上へ
        Vector3 direction = character.bounds.center - center;

        if (direction.sqrMagnitude < 0.0001f)
        {
            direction = Vector3.up;
        }

        // 上向きを少し足してから長さ1に直し、上を向きすぎていたら角度の上限までねかせる
        direction = (direction.normalized + Vector3.up * upwardBias).normalized;
        direction = LimitAngle(direction, character);

        target.Launch(direction * speed);
    }

    /// <summary>
    /// **飛ぶ向きが上を向きすぎていたら、<see cref="maxLaunchAngle"/> までねかせる。**
    ///
    /// 横の向きは、なるべく「爆弾から人へ」の横の向きを使う。
    /// 爆弾の真上に乗っていて横の向きが無いときは、**人が動いていた向き**、
    /// 止まっていたら**人の向いている向き**へ飛ばす（踏んだまま前に抜けていく）。
    /// </summary>
    private Vector3 LimitAngle(Vector3 direction, CharacterController character)
    {
        if (maxLaunchAngle >= 90f)
        {
            return direction;
        }

        // 地面からの角度（度）。direction は長さ1なので、y がそのまま「上向きの割合」＝ sin(角度) になる
        float angle = Mathf.Asin(Mathf.Clamp(direction.y, -1f, 1f)) * Mathf.Rad2Deg;

        if (angle <= maxLaunchAngle)
        {
            return direction;
        }

        // 横の向き（上下の成分を除いたもの）
        Vector3 flat = new Vector3(direction.x, 0f, direction.z);

        // ほぼ真上（約6度以内）のときは、横の向きがぶれやすいので使わない
        // （横の長さ 0.1 未満。direction は長さ1なので、真上から asin(0.1)＝約5.7度以内）
        if (flat.sqrMagnitude < 0.1f * 0.1f)
        {
            // 代わりに、人が動いていた向き（秒速0.1メートル未満なら「止まっている」とみなす）
            flat = new Vector3(character.velocity.x, 0f, character.velocity.z);

            if (flat.sqrMagnitude < 0.1f * 0.1f)
            {
                // 止まっていたら、人の向いている向き
                flat = new Vector3(character.transform.forward.x, 0f, character.transform.forward.z);
            }

            // 人の向きまで真上か真下を向いていて、それでも横の向きが決まらないとき
            if (flat.sqrMagnitude < 0.0001f)
            {
                flat = Vector3.forward;
            }
        }

        // 横の向きを保ったまま、ちょうど上限の角度になる向きを作る（長さ1）
        float radians = maxLaunchAngle * Mathf.Deg2Rad;

        return flat.normalized * Mathf.Cos(radians) + Vector3.up * Mathf.Sin(radians);
    }

    /// <summary>
    /// 爆発の見た目（広がって消える球、<see cref="BlastEffect"/>）を出す。
    /// マテリアルが入っていなければ、見た目は出さない（爆発そのものはする）。
    /// </summary>
    private void SpawnBlast(Vector3 center)
    {
        if (blastMaterial == null)
        {
            return;
        }

        GameObject blast = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        blast.name = name + "_Blast";

        // 見た目だけなので、当たり判定を外す。消えるのはフレームの終わりなので、先に切っておく
        Collider collider = blast.GetComponent<Collider>();
        collider.enabled = false;
        Destroy(collider);

        blast.transform.position = center;

        MeshRenderer blastRenderer = blast.GetComponent<MeshRenderer>();
        blastRenderer.sharedMaterial = blastMaterial;
        blastRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

        BlastEffect effect = blast.AddComponent<BlastEffect>();
        effect.Play(blastSize, blastSeconds);
    }

    // ------------------------------------------------------------
    // 見た目・戻る
    // ------------------------------------------------------------

    /// <summary>浮いていると分かるように、置いた位置を中心にゆっくり上下させる。</summary>
    private void Bob()
    {
        if (bobHeight <= 0f)
        {
            return;
        }

        // 2 は揺れの速さ（1秒あたりのラジアン）。約3.1秒で1往復する
        float offset = Mathf.Sin(Time.time * 2f + bobPhase) * bobHeight;
        transform.localPosition = restLocalPosition + Vector3.up * offset;
    }

    /// <summary>爆発したあと、毎フレーム呼ぶ。「元に戻る秒数」が決めてあれば、時間が来たら戻す。</summary>
    private void UpdateRespawn()
    {
        if (respawnSeconds <= 0f)
        {
            return;
        }

        respawnTimer -= Time.deltaTime;

        if (respawnTimer <= 0f)
        {
            Restore();
        }
    }

    /// <summary>
    /// **爆発する前の状態に戻す。**
    /// やり直しの合図（<see cref="StageReset"/>）でも呼ばれる。
    /// </summary>
    public void Restore()
    {
        HasExploded = false;
        transform.localPosition = restLocalPosition;
        SetVisible(true);
    }

    /// <summary>
    /// 見た目を出す・消す。**ゲームオブジェクトは止めずに、描くのだけをやめる**
    /// （止めてしまうと Update も止まり、元に戻る時間を数えられなくなるため）。
    /// </summary>
    private void SetVisible(bool visible)
    {
        for (int i = 0; i < visuals.Length; i++)
        {
            if (visuals[i] != null)
            {
                visuals[i].enabled = visible;
            }
        }
    }

    private void OnDrawGizmosSelected()
    {
        // 「触れた」とみなす範囲を、赤い球で示す
        Gizmos.color = new Color(1f, 0.3f, 0.2f, 0.8f);
        Gizmos.DrawWireSphere(transform.position, touchRadius);
    }
}
