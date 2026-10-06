using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// **乗っている人と物を、決まった向きに運ぶベルトコンベア。** 一定時間ごとに向きが逆になる（2026/10/6・大槻さん）。
///
/// ・**置いた場所が、ベルトの上面の真ん中。** ベルトは置いた物の奥行き（青い矢印 Z）の向きに流れる
/// ・Reverse Interval Seconds（初期値8秒）ごとに**向きが逆になる**。0 なら逆にならない
/// ・逆になる Warning Seconds（初期値1.5秒）前から、**ベルトの帯が黄色く点滅して予告する**
/// ・帯（横の筋）が流れて見えるので、どちらへ流れているか一目で分かる
///
/// 大きさ（Belt Size）を変えると、当たり判定・見た目・帯の数が自動で合う。
/// ベルトの端から先に床が無ければ、運ばれた物は落ちる（場外へ捨てるベルトにできる）。
///
/// ## オンラインのとき
///
/// ベルトそのものは通信しない。**全員で同期された時計（ServerTime）から、いまの向きを各PCが計算する**
/// （列車 <see cref="SpaceJunkTrain"/>・回転する床 <see cref="SpaceJunkRotatingFloor"/> と同じ）。
/// ・プレイヤー … **自分のPCが、自分の体を運ぶ**
/// ・物資 … **物理を動かしているPC（ふつうはホスト）が運ぶ**。ほかのPCでは物理が止まっているので何もしない
/// </summary>
[RequireComponent(typeof(BoxCollider))]
public class SpaceJunkConveyor : MonoBehaviour
{
    [Header("流れ方")]
    [Tooltip("流れる速さ（1秒あたりのメートル）。マイナスにすると最初の向きが逆になる")]
    [SerializeField] private float speed = 4f;

    [Tooltip("何秒ごとに向きが逆になるか。0 なら逆にならない")]
    [SerializeField] private float reverseIntervalSeconds = 8f;

    [Tooltip("向きが逆になる何秒前から予告する（帯が黄色く点滅する）か")]
    [SerializeField] private float warningSeconds = 1.5f;

    [Tooltip("逆になるタイミングをずらす秒数（ベルトを何本か置いて、ばらばらに逆にしたいとき）")]
    [SerializeField] private float timeOffsetSeconds = 0f;

    [Header("運ぶ強さ")]
    [Tooltip("物資をベルトの速さに合わせる強さ。大きいほどすぐベルトと同じ速さになる")]
    [SerializeField] private float itemGrip = 6f;

    [Tooltip("ベルトの上面から、どの高さまでの人と物を「乗っている」とみなすか（m）")]
    [SerializeField] private float carryHeight = 1.2f;

    [Header("大きさと見た目")]
    [Tooltip("ベルトの大きさ（X＝幅、Y＝厚み、Z＝長さ）")]
    [SerializeField] private Vector3 beltSize = new Vector3(4f, 0.5f, 20f);

    [Tooltip("ベルトの土台の見た目（子の Base）")]
    [SerializeField] private Transform baseVisual;

    [Tooltip("帯（横の筋）の間隔（m）")]
    [SerializeField] private float slatSpacing = 1.5f;

    [Tooltip("帯の色")]
    [SerializeField] private Color slatColor = new Color(0.55f, 0.6f, 0.68f);

    [Tooltip("向きが逆になる前の、予告の色")]
    [SerializeField] private Color warningColor = new Color(1f, 0.82f, 0.1f);

    /// <summary>いま流れている向き（ワールド。長さ1）。止まっていれば 0。</summary>
    public Vector3 FlowDirection { get; private set; }

    private BoxCollider box;
    private readonly List<Transform> slats = new List<Transform>();
    private Material slatMaterial;
    private float slatScroll;
    private readonly Collider[] overlapBuffer = new Collider[64];
    private readonly HashSet<Rigidbody> pushedThisStep = new HashSet<Rigidbody>();

    private void Awake()
    {
        box = GetComponent<BoxCollider>();
        ApplySize();
        BuildSlats();
    }

    private void OnValidate()
    {
        ApplySize();
    }

    /// <summary>当たり判定と土台の見た目を、Belt Size に合わせる。上面が置いた場所の高さになる。</summary>
    private void ApplySize()
    {
        if (box == null)
        {
            box = GetComponent<BoxCollider>();
        }

        if (box != null)
        {
            box.size = beltSize;
            box.center = new Vector3(0f, -beltSize.y * 0.5f, 0f);
        }

        if (baseVisual != null)
        {
            baseVisual.localScale = beltSize;
            baseVisual.localPosition = new Vector3(0f, -beltSize.y * 0.5f, 0f);
        }
    }

    // ------------------------------------------------------------
    // 向き
    // ------------------------------------------------------------

    private void Update()
    {
        float sign = CurrentSign(out bool warning);
        FlowDirection = transform.forward * sign;

        UpdateSlats(sign, warning);
        CarryPlayers();
    }

    /// <summary>
    /// いまの向き（+1＝奥へ、-1＝手前へ）。<paramref name="warning"/> は、もうすぐ逆になるか。
    /// </summary>
    private float CurrentSign(out bool warning)
    {
        warning = false;
        float sign = speed >= 0f ? 1f : -1f;

        if (reverseIntervalSeconds <= 0f)
        {
            return sign;
        }

        double clock = SharedClock() - timeOffsetSeconds;
        double interval = reverseIntervalSeconds;
        long index = (long)System.Math.Floor(clock / interval);
        double untilFlip = (index + 1) * interval - clock;

        warning = untilFlip <= warningSeconds;
        return (index & 1L) == 0L ? sign : -sign;
    }

    /// <summary>
    /// 全員で同じ時計。通信中は ServerTime、1人用のときはシーンが始まってからの秒数。
    /// </summary>
    private static double SharedClock()
    {
        NetworkManager network = NetworkManager.Singleton;
        if (network != null && network.IsListening)
        {
            return network.ServerTime.Time;
        }
        return Time.timeSinceLevelLoad;
    }

    // ------------------------------------------------------------
    // 運ぶ
    // ------------------------------------------------------------

    /// <summary>ベルトの上に立っている（自分のPCで動かしている）プレイヤーを運ぶ。</summary>
    private void CarryPlayers()
    {
        int count = OverlapAbove();
        float distance = Mathf.Abs(speed) * Time.deltaTime;

        for (int i = 0; i < count; i++)
        {
            FishingPlayerController player = overlapBuffer[i].GetComponentInParent<FishingPlayerController>();
            if (player == null || !player.isActiveAndEnabled)
            {
                continue;
            }

            CharacterController controller = player.GetComponent<CharacterController>();
            if (controller == null || !controller.enabled || controller != overlapBuffer[i])
            {
                continue;
            }

            controller.Move(FlowDirection * distance);
        }
    }

    private void FixedUpdate()
    {
        // 物資は物理で運ぶ。物理を動かしているPCでだけ効く（ほかのPCでは Is Kinematic になっている）
        int count = OverlapAbove();
        pushedThisStep.Clear();

        float target = Mathf.Abs(speed);
        float grip = Mathf.Clamp01(itemGrip * Time.fixedDeltaTime);

        for (int i = 0; i < count; i++)
        {
            Rigidbody body = overlapBuffer[i].attachedRigidbody;
            if (body == null || body.isKinematic || !pushedThisStep.Add(body))
            {
                continue;
            }

            // 引っ掛けられている物は、フックの動きに任せる
            HookableObject item = body.GetComponent<HookableObject>();
            if (item != null && (item.IsHooked || item.IsVanished))
            {
                continue;
            }

            float along = Vector3.Dot(body.linearVelocity, FlowDirection);
            body.AddForce(FlowDirection * ((target - along) * grip), ForceMode.VelocityChange);
        }
    }

    /// <summary>ベルトの上の箱に入っている当たり判定を集める。数を返す。</summary>
    private int OverlapAbove()
    {
        Vector3 halfExtents = new Vector3(beltSize.x * 0.5f, carryHeight * 0.5f, beltSize.z * 0.5f);
        Vector3 center = transform.TransformPoint(new Vector3(0f, carryHeight * 0.5f, 0f));

        return Physics.OverlapBoxNonAlloc(center, halfExtents, overlapBuffer, transform.rotation,
            ~0, QueryTriggerInteraction.Ignore);
    }

    // ------------------------------------------------------------
    // 見た目（流れる帯）
    // ------------------------------------------------------------

    /// <summary>帯（横の筋）を、ベルトの長さに合わせて並べる。当たり判定は付けない。</summary>
    private void BuildSlats()
    {
        float spacing = Mathf.Max(0.3f, slatSpacing);
        int count = Mathf.Max(1, Mathf.FloorToInt(beltSize.z / spacing));

        Renderer baseRenderer = baseVisual != null ? baseVisual.GetComponent<Renderer>() : null;
        slatMaterial = baseRenderer != null ? new Material(baseRenderer.sharedMaterial) : null;

        for (int i = 0; i < count; i++)
        {
            GameObject slat = GameObject.CreatePrimitive(PrimitiveType.Cube);
            slat.name = $"Slat_{i + 1}";
            Destroy(slat.GetComponent<Collider>());
            slat.transform.SetParent(transform, false);
            slat.transform.localScale = new Vector3(beltSize.x * 0.92f, 0.04f, spacing * 0.25f);

            if (slatMaterial != null)
            {
                slat.GetComponent<Renderer>().sharedMaterial = slatMaterial;
            }

            slats.Add(slat.transform);
        }
    }

    private void UpdateSlats(float sign, bool warning)
    {
        if (slats.Count == 0)
        {
            return;
        }

        float spacing = beltSize.z / slats.Count;
        slatScroll = Mathf.Repeat(slatScroll + sign * Mathf.Abs(speed) * Time.deltaTime, spacing);

        // 帯は等間隔に並び、流れる向きへ少しずつずれる。端まで行ったら反対の端へ回り込む
        for (int i = 0; i < slats.Count; i++)
        {
            float z = -beltSize.z * 0.5f + Mathf.Repeat(i * spacing + slatScroll + spacing * 0.5f, beltSize.z);
            slats[i].localPosition = new Vector3(0f, 0.02f, z);
        }

        if (slatMaterial != null)
        {
            // 予告中は 1秒に4回点滅する
            bool blinkOn = warning && Mathf.Repeat(Time.time * 4f, 1f) < 0.5f;
            Color color = blinkOn ? warningColor : slatColor;
            slatMaterial.color = color;
            if (slatMaterial.HasProperty("_BaseColor"))
            {
                slatMaterial.SetColor("_BaseColor", color);
            }
        }
    }

    private void OnDestroy()
    {
        if (slatMaterial != null)
        {
            Destroy(slatMaterial);
        }
    }

    private void OnDrawGizmos()
    {
        // 最初に流れる向きを、ベルトの上に矢印（線）で出す
        Gizmos.color = new Color(1f, 0.85f, 0.2f, 0.9f);
        Vector3 forward = transform.forward * (speed >= 0f ? 1f : -1f);
        Vector3 from = transform.position + Vector3.up * 0.1f - forward * beltSize.z * 0.4f;
        Vector3 to = transform.position + Vector3.up * 0.1f + forward * beltSize.z * 0.4f;
        Gizmos.DrawLine(from, to);
        Vector3 side = Vector3.Cross(Vector3.up, forward) * (beltSize.x * 0.3f);
        Gizmos.DrawLine(to, to - forward * 1.5f + side);
        Gizmos.DrawLine(to, to - forward * 1.5f - side);
    }
}
