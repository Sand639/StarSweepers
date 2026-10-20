using UnityEngine;

/// <summary>
/// **フックを溜めている間、足元から狙う方向へ、地面に長方形を伸ばす。**（レティクル タイプB。2026/10/13・小野田さんの依頼）
///
/// 長さは「**いま離したら、フックがどこまで飛ぶか**」（<see cref="HookController.ReachAtCurrentCharge"/>）。
/// 溜めるほど伸びるので、どこまで届くかが地面の上で分かる。
/// オートエイム（近く／遠く）で狙っている物資があるときは、**その物資へ向けて、物資までの長さ**で出す
/// （そのときのフックは、溜めた量に関係なくその物資へ飛ぶため）。
///
/// 設定の「レティクル」が**タイプBのときだけ**出る（<see cref="GameSettings.AimReticle"/>）。
/// タイプAは今までどおりで、これは何も出さない。
///
/// **<see cref="HookController"/> が自分で付ける**ので、シーンやプレハブに置く必要はない。
/// 見た目は画像を使わず、白い四角を引き伸ばしている（9スライスなので、伸ばしても縁の太さは変わらない）。
///
/// ※ 地面は**足元の高さの平らな面**として描く。坂や段差の上では、地面に埋まったり浮いたりして見えることがある。
/// </summary>
public class HookChargeStrip : MonoBehaviour
{
    [Header("見た目")]
    [Tooltip("長方形の幅（m）")]
    [Min(0.05f)]
    [SerializeField] private float width = 0.8f;

    [Tooltip("色。中は薄く、縁は濃く出る")]
    [SerializeField] private Color color = new Color(0.35f, 0.95f, 1f, 0.9f);

    [Tooltip("地面からどれだけ浮かせるか（m）。地面とちらつくときは少し上げる")]
    [Min(0f)]
    [SerializeField] private float lift = 0.05f;

    [Header("足元の地面")]
    [Tooltip("足元の高さを調べるときに当てるレイヤー")]
    [SerializeField] private LayerMask groundMask = ~0;

    [Tooltip("足元の高さを、足の裏からどこまで下へ探すか（m）")]
    [Min(0.1f)]
    [SerializeField] private float groundSearchDistance = 5f;

    // 縁の絵の大きさ。絵は 32 ピクセル四方で、外側の 4 ピクセルを濃い縁にする
    private const int TextureSize = 32;
    private const int EdgePixels = 4;
    private const float PixelsPerMeter = 64f; // 縁の太さ ＝ 4 / 64 ＝ 約 6 cm
    private const float InsideAlpha = 0.35f; // 中の濃さ（0〜1。縁は 1 で、中はこれだけ薄い）

    // 白い四角の絵は全員で1枚を使い回す
    private static Sprite sharedSprite;

    private readonly RaycastHit[] groundHits = new RaycastHit[8];

    private HookController hook;
    private CharacterController body;

    // 地面に寝かせる長方形。プレイヤーの子にすると体と一緒に回ったり大きさが変わったりするので、外に置く
    private SpriteRenderer strip;

    private void Awake()
    {
        hook = GetComponent<HookController>();
        body = GetComponent<CharacterController>();
    }

    /// <summary>
    /// 毎フレーム、長方形の向き・長さ・位置を決め直す。
    /// 描画の直前（LateUpdate）にやるのは、**そのフレームの移動と溜めが終わったあとの値**で出すため
    /// （Update だと、プレイヤーが動く前の位置に1フレーム遅れて出ることがある）。
    /// </summary>
    private void LateUpdate()
    {
        if (!ShouldShow())
        {
            Hide();
            return;
        }

        Transform hand = hook.HandPoint != null ? hook.HandPoint : transform;
        Vector3 origin = hand.position;
        Vector3 direction = hook.CurrentAimDirection;
        float length = hook.ReachAtCurrentCharge;

        // オートエイムで狙っている物資があれば、フックはそこへ飛ぶので、そちらへ向けて物資までの長さにする
        HookAimAssist assist = hook.AimAssist;
        HookableObject target = assist != null && assist.Mode != HookAimAssist.AimMode.Manual
            ? assist.DisplayTarget
            : null;
        if (target != null)
        {
            Vector3 toTarget = HookAimAssist.AimPointOf(target) - origin;
            toTarget.y = 0f;
            // 物資が手元と同じ場所にあって向きが決まらないときは、元の向き・長さのままにする
            if (toTarget.sqrMagnitude > 0.0001f)
            {
                direction = toTarget.normalized;
                length = toTarget.magnitude;
            }
        }

        origin.y = FindGroundHeight(origin) + lift;

        if (strip == null)
        {
            strip = CreateStrip();
        }

        strip.enabled = true;
        strip.color = color;
        // 溜め始めで届く距離が短くても、最低でも幅と同じ長さ（正方形）は出して、消えたように見せない
        strip.size = new Vector2(width, Mathf.Max(width, length));

        // 地面に寝かせて、絵の上（長さの向き）を狙う方向へ向ける。絵の根もとは下の端なので、足元から前へ伸びる
        float yaw = Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg;
        strip.transform.SetPositionAndRotation(origin, Quaternion.Euler(90f, yaw, 0f));
    }

    /// <summary>いま長方形を出してよいか。溜めている間だけ、しかも設定がタイプBのときだけ。</summary>
    private bool ShouldShow()
    {
        return hook != null
            && hook.isActiveAndEnabled // オンラインの他の人のぶんは HookController が止まっているので出ない
            && hook.Phase == HookController.HookPhase.Charging
            && GameSettings.AimReticle == AimReticleType.TypeB
            && !GamePause.BlocksInput;
    }

    /// <summary>長方形を消す。部品そのものは残しておき、次に溜めたときにまた使う（毎回作り直さない）。</summary>
    private void Hide()
    {
        if (strip != null)
        {
            strip.enabled = false;
        }
    }

    private void OnDisable()
    {
        Hide();
    }

    private void OnDestroy()
    {
        if (strip != null)
        {
            Destroy(strip.gameObject);
        }
    }

    /// <summary>
    /// **手元の真下の地面の高さ。** 足の裏の少し上から下へ探す（自分の体の当たり判定は飛ばす）。
    /// 見つからなければ、足の裏の高さを使う。
    /// </summary>
    private float FindGroundHeight(Vector3 from)
    {
        float feet = body != null && body.enabled ? body.bounds.min.y : transform.position.y;

        // 探し始めは足の裏の 30 cm 上（足の裏ぴったりから始めると、少し沈んでいる地面を取りこぼすため）。
        // 上にずらしたぶん、探す長さにも同じ 30 cm を足して、設定どおりの深さまで探す
        Vector3 start = new Vector3(from.x, feet + 0.3f, from.z);

        int count = Physics.RaycastNonAlloc(start, Vector3.down, groundHits,
            groundSearchDistance + 0.3f, groundMask, QueryTriggerInteraction.Ignore);

        float best = float.NegativeInfinity;
        for (int i = 0; i < count; i++)
        {
            RaycastHit hit = groundHits[i];
            if (hit.collider.transform.IsChildOf(transform))
            {
                continue;
            }

            // いちばん上（足の裏にいちばん近い）面を地面とみなす
            if (hit.point.y > best)
            {
                best = hit.point.y;
            }
        }

        return float.IsNegativeInfinity(best) ? feet : best;
    }

    /// <summary>
    /// 長方形の部品を作る（最初に溜めたときに1回だけ）。
    /// 9スライス（Sliced）で伸ばすので、長さを変えても縁の太さは変わらない。地面の飾りなので影は付けない。
    /// </summary>
    private SpriteRenderer CreateStrip()
    {
        GameObject created = new GameObject($"{name} の溜めの長方形");
        SpriteRenderer renderer = created.AddComponent<SpriteRenderer>();
        renderer.sprite = GetSprite();
        renderer.drawMode = SpriteDrawMode.Sliced;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        return renderer;
    }

    /// <summary>縁だけ濃い、白い四角の絵。根もと（ピボット）は下の辺の真ん中。</summary>
    private static Sprite GetSprite()
    {
        if (sharedSprite != null)
        {
            return sharedSprite;
        }

        Texture2D texture = new Texture2D(TextureSize, TextureSize, TextureFormat.RGBA32, false)
        {
            name = "HookChargeStrip",
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
        };

        Color32[] pixels = new Color32[TextureSize * TextureSize];
        for (int y = 0; y < TextureSize; y++)
        {
            for (int x = 0; x < TextureSize; x++)
            {
                bool edge = x < EdgePixels || y < EdgePixels
                    || x >= TextureSize - EdgePixels || y >= TextureSize - EdgePixels;
                byte alpha = edge ? (byte)255 : (byte)(255 * InsideAlpha);
                pixels[y * TextureSize + x] = new Color32(255, 255, 255, alpha);
            }
        }

        texture.SetPixels32(pixels);
        texture.Apply();

        sharedSprite = Sprite.Create(
            texture,
            new Rect(0f, 0f, TextureSize, TextureSize),
            new Vector2(0.5f, 0f),
            PixelsPerMeter,
            0,
            SpriteMeshType.FullRect,
            new Vector4(EdgePixels, EdgePixels, EdgePixels, EdgePixels));
        sharedSprite.name = "HookChargeStrip";
        return sharedSprite;
    }
}
