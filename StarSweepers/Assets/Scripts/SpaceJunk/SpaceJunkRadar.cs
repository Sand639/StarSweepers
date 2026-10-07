using UnityEngine;

/// <summary>
/// **自分を中心に、物資とほかのプレイヤーの位置を映すレーダー。** 画面の右下に出る。
///
/// 2026/9/22・大槻さんの依頼。
///
/// | 映るもの | 見た目 |
/// | --- | --- |
/// | 自分 | 真ん中の矢印（素材の色のまま）。**常に上を向く** |
/// | ほかのプレイヤー（味方・敵） | **チームの色**の点。範囲の外にいるときは、縁に小さく出る |
/// | 物資（宇宙ごみの素材） | **白い**点。範囲の外は出さない |
///
/// ## 向き（自分の向きが常に上）
///
/// レーダーの上＝**プレイヤーが向いている方向**。向きを変えると、点と方角（N/E/S/W）の輪が回る。
/// 方角の輪を見れば、いま自分がどの方角を向いているかが分かる。
///
/// ## 置き方
///
/// シーンには置かない。**自分のプレイヤーが生まれたときに、<see cref="SpaceJunkPlayerSetup"/> が付ける。**
/// 画像（外枠・方角・自分）も、そこから渡される（`SpaceJunkPlayer.prefab` の Radar の欄）。
/// ラウンド中（マップにいる間）だけ出る。ロビーでは出さない。
///
/// フォントや UI の部品を用意しなくても出せるように `OnGUI` で描いている（ほかの宇宙ごみの画面と同じ）。
/// </summary>
public class SpaceJunkRadar : MonoBehaviour
{
    [Header("画像")]
    [Tooltip("レーダーの外枠（背景）")]
    [SerializeField] private Sprite frameSprite;

    [Tooltip("方角（N/E/S/W）の輪。自分の向きに合わせて回る")]
    [SerializeField] private Sprite compassSprite;

    [Tooltip("真ん中に出す自分の印。上向きの絵にしておく")]
    [SerializeField] private Sprite selfSprite;

    [Header("大きさ・範囲")]
    [Tooltip("レーダーの直径（画面の高さ 1080 のときの大きさ。窓の大きさに合わせて伸び縮みする）")]
    [SerializeField] private float size = 260f;

    [Tooltip("画面の端からの余白（1080 のとき）")]
    [SerializeField] private float margin = 24f;

    [Tooltip("レーダーの縁までが、何メートル先にあたるか")]
    [Min(1f)]
    [SerializeField] private float range = 40f;

    [Tooltip("点を置ける範囲（半径に対する割合）。方角の輪に点が重ならないよう、少し内側にしてある")]
    [Range(0.3f, 1f)]
    [SerializeField] private float innerRatio = 0.78f;

    [Header("点")]
    [Tooltip("プレイヤーの点の大きさ（1080 のとき）")]
    [SerializeField] private float playerDotSize = 16f;

    [Tooltip("物資の点の大きさ（1080 のとき）")]
    [SerializeField] private float supplyDotSize = 9f;

    [Tooltip("自分の印の大きさ（1080 のとき）")]
    [SerializeField] private float selfSize = 26f;

    [Tooltip("物資の点の色")]
    [SerializeField] private Color supplyColor = Color.white;

    // 点に使う丸い画像（素材が無くても描けるよう、ここで作る）
    private static Texture2D dotTexture;

    /// <summary>画像を渡す。<see cref="SpaceJunkPlayerSetup"/> が付けたときに呼ぶ。</summary>
    public void Setup(Sprite frame, Sprite compass, Sprite self)
    {
        frameSprite = frame;
        compassSprite = compass;
        selfSprite = self;
    }

    private void OnGUI()
    {
        // マップにいる間だけ。ポーズ中は出さない（ポーズ画面の邪魔をしないように）
        if (SpaceJunkRound.Current == null || GamePause.IsPaused || Event.current.type != EventType.Repaint)
        {
            return;
        }

        float scale = Mathf.Clamp(Screen.height / 1080f, 0.4f, 3f);
        float diameter = size * scale;
        float radius = diameter * 0.5f;

        Vector2 center = new Vector2(
            Screen.width - margin * scale - radius,
            Screen.height - margin * scale - radius);

        Rect area = new Rect(center.x - radius, center.y - radius, diameter, diameter);

        // レーダーの上＝自分が向いている方向
        float heading = transform.eulerAngles.y;

        DrawSprite(frameSprite, area, Color.white);
        if (frameSprite == null)
        {
            DrawDot(center, diameter, new Color(0.1f, 0.2f, 0.12f, 0.6f));
        }

        // 方角の輪。北が「自分から見た北の方向」へ来るように回す
        DrawRotated(compassSprite, area, -heading, center);

        float dotRadius = radius * innerRatio;

        DrawSupplies(center, dotRadius, heading, scale);
        DrawPlayers(center, dotRadius, heading, scale);

        // 真ん中の自分。常に上向き
        float self = selfSize * scale;
        Rect selfRect = new Rect(center.x - self * 0.5f, center.y - self * 0.5f, self, self);
        DrawSprite(selfSprite, selfRect, Color.white);
    }

    /// <summary>物資（宇宙ごみの素材）を白い点で描く。範囲の外は描かない。</summary>
    private void DrawSupplies(Vector2 center, float dotRadius, float heading, float scale)
    {
        float dot = supplyDotSize * scale;

        foreach (HookableObject item in HookableObject.All)
        {
            if (item == null || !item.isActiveAndEnabled || !item.TryGetComponent(out SpaceJunkMaterial _))
            {
                continue;
            }

            // 重い（特殊）デブリは、その色の大きめの点で出す
            bool highValue = item.TryGetComponent(out SpaceJunkHighValueMark mark) && mark.enabled;

            if (ToRadar(item.transform.position, heading, dotRadius, false, out Vector2 offset))
            {
                DrawDot(center + offset,
                        highValue ? dot * 1.6f : dot,
                        highValue ? mark.MarkColor : supplyColor);
            }
        }
    }

    /// <summary>ほかのプレイヤーをチームの色の点で描く。範囲の外にいる人は縁に小さく出す。</summary>
    private void DrawPlayers(Vector2 center, float dotRadius, float heading, float scale)
    {
        SpaceJunkSession session = SpaceJunkSession.Current;
        float dot = playerDotSize * scale;

        foreach (FishingNetPlayer player in FishingNetPlayer.All)
        {
            if (player == null || player.transform == transform || !player.gameObject.activeInHierarchy)
            {
                continue;
            }

            int team = session != null ? session.TeamOf(player.PlayerKey) : 0;
            Color color = SpaceJunkTeams.TeamColor(team);

            bool inside = ToRadar(player.transform.position, heading, dotRadius, true, out Vector2 offset);

            // 範囲の外の人は、縁に少し小さく・薄く出す（どっちの方向にいるかだけ分かるように）
            if (!inside)
            {
                color.a = 0.6f;
            }

            DrawDot(center + offset, inside ? dot : dot * 0.75f, color);
        }
    }

    /// <summary>
    /// **世界の位置を、レーダーの上の位置（中心からのずれ）に直す。**
    /// 範囲の外なら false。<paramref name="clampToEdge"/> が true のときは、縁に寄せた位置を返す。
    /// </summary>
    private bool ToRadar(Vector3 world, float heading, float dotRadius, bool clampToEdge, out Vector2 offset)
    {
        Vector3 relative = world - transform.position;
        relative.y = 0f;

        // 自分の向きが上に来るよう、自分の向きのぶんだけ逆に回す
        Vector3 local = Quaternion.Euler(0f, -heading, 0f) * relative;

        // 画面は下向きが +y なので、奥（+z）は上（-y）
        Vector2 radar = new Vector2(local.x, -local.z) / range;

        bool inside = radar.sqrMagnitude <= 1f;

        if (!inside && clampToEdge)
        {
            radar = radar.normalized;
        }

        offset = radar * dotRadius;
        return inside;
    }

    private static void DrawSprite(Sprite sprite, Rect rect, Color color)
    {
        if (sprite == null)
        {
            return;
        }

        Color saved = GUI.color;
        GUI.color = color;
        GUI.DrawTextureWithTexCoords(rect, sprite.texture, TexCoords(sprite));
        GUI.color = saved;
    }

    /// <summary>画像を、中心のまわりに回して描く（時計回りが正）。</summary>
    private static void DrawRotated(Sprite sprite, Rect rect, float degrees, Vector2 pivot)
    {
        if (sprite == null)
        {
            return;
        }

        Matrix4x4 saved = GUI.matrix;
        GUIUtility.RotateAroundPivot(degrees, pivot);
        DrawSprite(sprite, rect, Color.white);
        GUI.matrix = saved;
    }

    /// <summary>画像の中で、その絵が入っている範囲（0〜1）。</summary>
    private static Rect TexCoords(Sprite sprite)
    {
        Rect r = sprite.textureRect;
        Texture t = sprite.texture;
        return new Rect(r.x / t.width, r.y / t.height, r.width / t.width, r.height / t.height);
    }

    private static void DrawDot(Vector2 at, float diameter, Color color)
    {
        Color saved = GUI.color;
        GUI.color = color;
        GUI.DrawTexture(new Rect(at.x - diameter * 0.5f, at.y - diameter * 0.5f, diameter, diameter), DotTexture());
        GUI.color = saved;
    }

    /// <summary>白い丸の画像。最初に1回だけ作る。</summary>
    private static Texture2D DotTexture()
    {
        if (dotTexture != null)
        {
            return dotTexture;
        }

        const int size = 32;
        dotTexture = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            name = "RadarDot",
            filterMode = FilterMode.Bilinear,
            hideFlags = HideFlags.HideAndDontSave
        };

        float half = (size - 1) * 0.5f;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float distance = Vector2.Distance(new Vector2(x, y), new Vector2(half, half));
                float alpha = Mathf.Clamp01(half - distance + 0.5f);
                dotTexture.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
            }
        }

        dotTexture.Apply();
        return dotTexture;
    }
}
