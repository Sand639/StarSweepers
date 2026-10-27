using UnityEngine;

/// <summary>
/// **狙っている間に、リングの縁から狙う方向へ伸ばす矢印の帯（AOE）。**（2026/10/27・小野田さんの仕様）
///
/// 帯の長さ＝「いま離したら光線がどこまで伸びるか」。<see cref="SweeperShooter"/> が毎フレーム <see cref="Show"/> を呼ぶ。
///
/// 見た目は白い矢印の画像（`Art/Sprites/AimLengthBody.png` を繰り返し、先に `AimLengthTip.png`）にチームの色を掛けたもの。
/// **繰り返しの回数は帯の長さで変わる**（長さ 10 枚ぶんなら UV × 10）。1枚の長さは、画像の縦横の比と帯の幅で決まる。
/// 矢印の画像は**右（+X）向き**に描かれている前提で、右を帯の先へ向ける。
/// </summary>
public class SweeperAimArea : MonoBehaviour
{
    [Header("画像")]
    [Tooltip("繰り返す部分の画像（Art/Sprites/AimLengthBody.png）。インポートの Wrap Mode は Repeat にしておく")]
    [SerializeField] private Texture2D bodyTexture;

    [Tooltip("帯の先の画像（Art/Sprites/AimLengthTip.png）。空なら先は付けない")]
    [SerializeField] private Texture2D tipTexture;

    [Tooltip("Art/Materials/SweeperAimStrip.mat を入れる。空なら再生したときに作る")]
    [SerializeField] private Material material;

    [Header("形")]
    [Tooltip("帯の幅（m）")]
    [Min(0.05f)]
    [SerializeField] private float width = 1f;

    [Tooltip("矢印1枚の長さの倍率。1 で画像の縦横の比のまま。大きくすると1枚が長くなる（繰り返しの回数が減る）")]
    [Min(0.05f)]
    [SerializeField] private float tileScale = 1f;

    [Tooltip("矢印が前へ流れる速さ（1秒に何枚ぶん）。0 で止まる")]
    [SerializeField] private float scrollSpeed = 1f;

    [Tooltip("床からどれだけ浮かせるか（m）。リング（0.02）より少し上にする")]
    [Min(0f)]
    [SerializeField] private float lift = 0.025f;

    [Header("色")]
    [Tooltip("色。ふつうは SweeperPlayer がチームの色を入れる")]
    [SerializeField] private Color color = Color.white;

    [Tooltip("濃さ（色の不透明度に掛ける）")]
    [Range(0f, 1f)]
    [SerializeField] private float alpha = 0.85f;

    private static readonly int MainTexId = Shader.PropertyToID("_MainTex");
    private static readonly int ColorId = Shader.PropertyToID("_Color");
    private static readonly int ScrollId = Shader.PropertyToID("_ScrollSpeed");

    private Transform root;
    private Strip body;
    private Strip tip;
    private MaterialPropertyBlock block;

    /// <summary>帯の部品1つぶん（体か先）。</summary>
    private class Strip
    {
        public GameObject Object;
        public Mesh Mesh;
        public MeshRenderer Renderer;
        public readonly Vector3[] Vertices = new Vector3[4];
        public readonly Vector2[] Uvs = new Vector2[4];
        public bool HasTriangles;
    }

    private void Awake()
    {
        root = new GameObject("AimAreaVisual").transform;
        root.SetParent(transform, false);

        body = CreateStrip("Body");
        tip = CreateStrip("Tip");
        ApplyColor();
        Hide();
    }

    /// <summary>色を変える（<see cref="SweeperPlayer"/> がチームの色を入れる）。</summary>
    public void SetColor(Color newColor)
    {
        color = newColor;
        ApplyColor();
    }

    /// <summary>
    /// 帯を出す。<paramref name="feet"/> は足元の位置、<paramref name="direction"/> は狙う向き。
    /// 帯は中心から <paramref name="startDistance"/>（リングの縁）から始まり、中心から <paramref name="endDistance"/> で終わる。
    /// </summary>
    public void Show(Vector3 feet, Vector3 direction, float startDistance, float endDistance)
    {
        direction.y = 0f;
        if (direction.sqrMagnitude < 0.0001f || endDistance <= startDistance)
        {
            Hide();
            return;
        }

        root.position = feet + Vector3.up * lift;
        root.rotation = Quaternion.LookRotation(direction.normalized, Vector3.up);

        float total = endDistance - startDistance;
        float tipLength = tipTexture != null ? width * tipTexture.width / Mathf.Max(1f, tipTexture.height) : 0f;
        tipLength = Mathf.Min(tipLength, total);
        float bodyLength = total - tipLength;

        float tileLength = width * tileScale;
        if (bodyTexture != null)
        {
            tileLength *= (float)bodyTexture.width / Mathf.Max(1, bodyTexture.height);
        }

        // 体：長さに合わせて UV を繰り返す（例：10枚ぶんなら u が 0〜10）
        SetStrip(body, startDistance, bodyLength, bodyLength / Mathf.Max(0.01f, tileLength), bodyLength > 0.001f);
        // 先：画像1枚をそのまま
        SetStrip(tip, startDistance + bodyLength, tipLength, 1f, tipLength > 0.001f);
    }

    /// <summary>帯を消す。</summary>
    public void Hide()
    {
        body.Object.SetActive(false);
        tip.Object.SetActive(false);
    }

    private Strip CreateStrip(string objectName)
    {
        Strip strip = new Strip();
        strip.Object = new GameObject(objectName);
        strip.Object.transform.SetParent(root, false);

        strip.Mesh = new Mesh { name = "SweeperAimArea" + objectName };
        strip.Mesh.MarkDynamic();
        strip.Object.AddComponent<MeshFilter>().sharedMesh = strip.Mesh;

        strip.Renderer = strip.Object.AddComponent<MeshRenderer>();
        strip.Renderer.sharedMaterial = material != null ? material : SweeperGroundMesh.FallbackMaterial("StarSweepers/SweeperAimStrip");
        strip.Renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        strip.Renderer.receiveShadows = false;
        return strip;
    }

    /// <summary>帯の部品を、前へ <paramref name="from"/> から <paramref name="length"/> だけの長方形にする。U は長さの向き（0〜repeat）。</summary>
    private void SetStrip(Strip strip, float from, float length, float repeat, bool visible)
    {
        strip.Object.SetActive(visible);
        if (!visible)
        {
            return;
        }

        float half = width * 0.5f;
        float to = from + length;

        strip.Vertices[0] = new Vector3(-half, 0f, from);
        strip.Vertices[1] = new Vector3(half, 0f, from);
        strip.Vertices[2] = new Vector3(-half, 0f, to);
        strip.Vertices[3] = new Vector3(half, 0f, to);

        // 画像の右（U の +）を帯の先へ、画像の上（V の +）を帯の左へ向ける
        strip.Uvs[0] = new Vector2(0f, 1f);
        strip.Uvs[1] = new Vector2(0f, 0f);
        strip.Uvs[2] = new Vector2(repeat, 1f);
        strip.Uvs[3] = new Vector2(repeat, 0f);

        strip.Mesh.vertices = strip.Vertices;
        strip.Mesh.uv = strip.Uvs;
        if (!strip.HasTriangles)
        {
            strip.Mesh.triangles = new[] { 0, 2, 1, 2, 3, 1 };
            strip.Mesh.normals = new[] { Vector3.up, Vector3.up, Vector3.up, Vector3.up };
            strip.HasTriangles = true;
        }
        strip.Mesh.RecalculateBounds();
    }

    private void ApplyColor()
    {
        if (body == null)
        {
            return;
        }

        block ??= new MaterialPropertyBlock();
        Color tinted = new Color(color.r, color.g, color.b, color.a * alpha);

        ApplyTo(body.Renderer, bodyTexture, tinted, scrollSpeed);
        // 先は流さない（1枚だけなので、流すと途切れる）
        ApplyTo(tip.Renderer, tipTexture, tinted, 0f);
    }

    private void ApplyTo(MeshRenderer target, Texture2D texture, Color tinted, float scroll)
    {
        target.GetPropertyBlock(block);
        if (texture != null)
        {
            block.SetTexture(MainTexId, texture);
        }
        block.SetColor(ColorId, tinted);
        block.SetFloat(ScrollId, scroll);
        target.SetPropertyBlock(block);
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (Application.isPlaying)
        {
            ApplyColor();
        }
    }
#endif
}
