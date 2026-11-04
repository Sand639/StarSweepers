using System.Collections.Generic;
using UnityEngine;



/// <summary>
/// **重力・踏んだ回数・乗っている累計時間で割れるガラスの床。**
/// CounterBreak はプレイヤーごとの乗り直しで別々の起点にヒビを作り、合計回数で割れる。
/// StayTime は乗っている人数ぶん時間を足し、各プレイヤーの足元からヒビを伸ばす。
/// 降りても累計時間とヒビは残り、Restore でまとめてリセットする。
/// 以下は StepBreak（従来の重力モード）の説明。
///
/// | 段階 | 何が起きるか |
/// | --- | --- |
/// | ふつう | 乗っても何も起きない |
/// | **ヒビ** | **重力が決めた値以上のときに踏むと、ヒビが入る。** ヒビは広がっていき、ふちが赤く点滅する |
/// | **割れる** | ヒビが入ってから決めた秒数で割れる。**破片に当たり判定は無い**ので、乗っていた人は落ちる |
///
/// **一度ヒビが入ったら止まらない。** 途中で重力が軽くなっても割れる。
///
/// ## 使い方
///
/// **ガラスのマテリアルを付けた Cube に、これを付けるだけ。**
/// 大きさは Cube の Scale で決める（ヒビも破片も、大きさに合わせて作られる）。
/// メニューの `Tools > StarSweepers > アーカイブ > 月面運動会 > 重力で割れるガラスの床を置く` でも置ける。
///
/// ## 「踏んだ」の見分け方
///
/// **床の上面のすぐ上に、人（CharacterController）がいるか**を毎フレーム調べている。
/// 当たった瞬間を拾う方式（OnCollision）は、CharacterController では呼ばれないため使っていない。
/// </summary>
[RequireComponent(typeof(BoxCollider))]
public class GravityGlassFloor : MonoBehaviour
{

    /// <summary>床の状態。</summary>
    public enum FloorState
    {
        /// <summary>割れていない</summary>
        Intact,
        /// <summary>ヒビが入った（もうすぐ割れる）</summary>
        Cracked,
        /// <summary>割れた</summary>
        Broken,
    }

    public enum GlassType
    {
        StepBreak,
        CounterBreak,
        StayTime,
    }


    [Header("ガラスのタイプ")]
    [SerializeField] private GlassType glasstype;

    [Header("割れる条件")]
    [Tooltip("**ヒビが入ってから割れるまでの秒数**")]
    [Min(0f)]
    [SerializeField] private float secondsUntilBreak = 1.5f;

    [Header("割れる条件")]
    [Tooltip("乗り直した回数。プレイヤーごとに数え、合計がこの回数に達すると割れる")]
    [Min(1)]
    [SerializeField] private int breakCount = 3;

    [Tooltip("爆発で割れるまでの回数。0なら爆発の影響を受けない")]
    [Min(0)]
    [SerializeField] private int blastHitsToBreak;

    [Header("割れる条件")]
    [Min(0f)]
    [SerializeField] private float stayTime = 3f;




    [Tooltip("**この重力以上のときに踏むと、ヒビが入る。** 正の数で入れる。地球は9.81")]
    [Min(0f)]
    [SerializeField] private float breakGravity = 12f;

    [Tooltip("ONなら**人（CharacterController）だけ**を数える。OFFにすると、置いた箱などでも割れる")]
    [SerializeField] private bool onlyCharacters = true;

    [Tooltip("上面からどれだけ上までを「乗っている」とみなすか（メートル）")]
    [Min(0.05f)]
    [SerializeField] private float detectHeight = 0.3f;

    [Header("ヒビの見た目")]
    [Tooltip("ヒビの線に使うマテリアル。**白い不透明なもの**が見えやすい")]
    [SerializeField] private Material crackMaterial;

    [Tooltip("ヒビの線の本数（踏んだ場所から放射状に伸びる）")]
    [Range(3, 16)]
    [SerializeField] private int crackLines = 7;

    [Tooltip("ヒビの線の太さ（メートル）")]
    [Min(0.005f)]
    [SerializeField] private float crackWidth = 0.035f;

    [Tooltip("ヒビが入ったときの、ふちの色。**割れる直前ほど速く点滅する**")]
    [SerializeField] private Color crackEdgeColor = new Color(1f, 0.35f, 0.25f, 1f);

    [Header("割れたときの見た目")]
    [Tooltip("破片の数（縦×横）。3なら 3×3＝9枚")]
    [Range(1, 6)]
    [SerializeField] private int piecesPerSide = 3;

    [Tooltip("破断直前にヒビをふちまで伸ばす秒数。0なら従来の破片を使う")]
    [Min(0f)]
    [SerializeField] private float fractureExtensionSeconds;

    [Tooltip("破片が消えるまでの秒数")]
    [Min(0.1f)]
    [SerializeField] private float pieceLifeSeconds = 3f;

    [Tooltip("破片が散らばる勢い")]
    [Min(0f)]
    [SerializeField] private float scatterSpeed = 1.2f;

    [Header("元に戻る")]
    [Tooltip("**割れてから元に戻るまでの秒数。** 0なら戻らない（やり直しのときだけ戻る）")]
    [Min(0f)]
    [SerializeField] private float respawnSeconds;

    [Tooltip("割れるたびにコンソールへ書き出す")]
    [SerializeField] private bool logChanges = true;

    /// <summary>いまの状態。</summary>
    public FloorState State { get; private set; } = FloorState.Intact;

    /// <summary>割れるまでの残り秒数。StayTime は残りの延べ滞在秒数。CounterBreak では使わない。</summary>
    public float BreakRemaining { get; private set; }

    /// <summary>
    /// 「乗っている人」を探すときの入れ物。**毎フレーム作り直さないよう、全部の床で使い回している。**
    /// 一度に拾えるのは16個まで（床の上にそれ以上の物が重なることは想定していない）。
    /// </summary>
    private static readonly Collider[] Hits = new Collider[16];

    /// <summary>ガラスのシェーダーの「ふちの色」の欄。名前で毎回探すと遅いので、番号にしておく。</summary>
    private static readonly int EdgeColorId = Shader.PropertyToID("_EdgeColor");

    private BoxCollider box;
    private MeshRenderer meshRenderer;

    /// <summary>
    /// ふちの色を、**この床だけ**変えるための入れ物。
    /// マテリアルを直接書き換えると、同じマテリアルを使う他の床まで赤くなってしまうため。
    /// </summary>
    private MaterialPropertyBlock propertyBlock;

    /// <summary>マテリアルにもともと入っていた、ふちの色。点滅はこの色と赤の間を行き来する。</summary>
    private Color originalEdgeColor = Color.white;

    /// <summary>マテリアルに「ふちの色」の欄があるか。無ければ点滅させない（ガラス以外のマテリアルでも動くように）。</summary>
    private bool hasEdgeColor;

    /// <summary>ヒビの線をまとめて入れておく親。消すときはこれごと消す。</summary>
    private Transform crackRoot;

    /// <summary>ヒビの線の一覧。**踏んだ場所に近い順**に並べてあり、前から順に見せていく。</summary>
    private readonly List<GameObject> crackSegments = new List<GameObject>();

    /// <summary>割れてから元に戻るまでの残り秒数。</summary>
    private float respawnTimer;

    private int stepCount;
    private int blastHits;
    private float fractureTime;
    private CrackPattern finalCracks;
    private readonly List<(Vector2 Point, Vector2 Direction)> crackTips = new();
    private float accumulatedStayTime;
    private readonly Dictionary<int, Vector3> steppers = new Dictionary<int, Vector3>();
    private readonly HashSet<int> previousSteppers = new HashSet<int>();
    private readonly Dictionary<int, CrackPattern> standingCracks = new Dictionary<int, CrackPattern>();
    private readonly List<CrackPattern> crackPatterns = new List<CrackPattern>();
    private readonly List<int> departedSteppers = new List<int>();

    // 各起点の線を保存し、線の根元を固定したまま先端を滑らかに伸ばす。
    private class CrackPattern
    {
        public readonly List<CrackSegment> Segments = new List<CrackSegment>();
        public float Progress;
    }

    private struct CrackSegment
    {
        public Transform Transform;
        public Vector3 Start;
        public Vector3 End;
        public float Length;
        public float StartProgress;
        public float EndProgress;
    }

    private void Awake()
    {
        box = GetComponent<BoxCollider>();
        meshRenderer = GetComponent<MeshRenderer>();
        propertyBlock = new MaterialPropertyBlock();

        // ふちの色を持つマテリアル（ガラスのシェーダー）のときだけ、点滅させる
        if (meshRenderer != null && meshRenderer.sharedMaterial != null &&
            meshRenderer.sharedMaterial.HasProperty(EdgeColorId))
        {
            hasEdgeColor = true;
            originalEdgeColor = meshRenderer.sharedMaterial.GetColor(EdgeColorId);
        }
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

        if (finalCracks != null)
        {
            fractureTime += Time.deltaTime;
            finalCracks.Progress = Mathf.Clamp01(fractureTime / fractureExtensionSeconds);
            RevealPattern(finalCracks);
            if (finalCracks.Progress >= 1f)
            {
                FinishBreak();
            }
            return;
        }

        if (State == FloorState.Broken)
        {
            UpdateBroken();
            return;
        }

        switch (glasstype)
        {
            case GlassType.CounterBreak:
                UpdateCounterBreak();
                return;
            case GlassType.StayTime:
                UpdateStayTime(Time.deltaTime);
                return;
        }

        // StepBreak は従来どおり、重力でヒビが入り、その後は時間で割れる。
        switch (State)
        {
            case FloorState.Intact:
                CheckStep();
                break;

            case FloorState.Cracked:
                UpdateCracked();
                break;

        }
    }

    private void UpdateCounterBreak()
    {
        FindSteppers();
        foreach (KeyValuePair<int, Vector3> stepper in steppers)
        {
            if (previousSteppers.Contains(stepper.Key))
            {
                continue;
            }

            stepCount++;
            State = FloorState.Cracked;
            CrackPattern pattern = BuildCracks(stepper.Value, true);
            if (stepCount >= Mathf.Max(1, breakCount))
            {
                Break();
                return;
            }
        }

        previousSteppers.Clear();
        previousSteppers.UnionWith(steppers.Keys);
        foreach (CrackPattern pattern in crackPatterns)
        {
            pattern.Progress = Mathf.MoveTowards(pattern.Progress, 1f, Time.deltaTime * 3f);
            RevealPattern(pattern);
        }
        if (State == FloorState.Cracked)
        {
            BlinkEdge((float)stepCount / Mathf.Max(1, breakCount));
        }
    }

    private void UpdateStayTime(float deltaTime)
    {
        FindSteppers();

        // 降りた起点は残すが、成長は止める。再び乗ると新しい足元に起点を作る。
        departedSteppers.Clear();
        foreach (int id in standingCracks.Keys)
        {
            if (!steppers.ContainsKey(id)) departedSteppers.Add(id);
        }
        foreach (int id in departedSteppers) standingCracks.Remove(id);

        float duration = Mathf.Max(0f, stayTime);
        float addedTime = Mathf.Min(deltaTime * steppers.Count, Mathf.Max(0f, duration - accumulatedStayTime));
        accumulatedStayTime += addedTime;
        BreakRemaining = Mathf.Max(0f, duration - accumulatedStayTime);

        foreach (KeyValuePair<int, Vector3> stepper in steppers)
        {
            State = FloorState.Cracked;
            if (!standingCracks.TryGetValue(stepper.Key, out CrackPattern pattern))
            {
                pattern = BuildCracks(stepper.Value, true);
                standingCracks.Add(stepper.Key, pattern);
            }

            // 人数が増えると、すべての在席者のヒビも同じ速度で速く伸びる。
            pattern.Progress = duration > 0f ? Mathf.Clamp01(pattern.Progress + addedTime / duration) : 1f;
            RevealPattern(pattern);
        }

        if (State == FloorState.Cracked)
        {
            BlinkEdge(duration > 0f ? accumulatedStayTime / duration : 1f);
        }
        if (steppers.Count > 0 && accumulatedStayTime >= duration)
        {
            Break();
        }
    }

    // ------------------------------------------------------------
    // 踏まれたか
    // ------------------------------------------------------------

    /// <summary>
    /// 割れていない間、毎フレーム呼ぶ。**重力が重いときに誰かが乗っていたら、ヒビを入れる。**
    /// </summary>
    private void CheckStep()
    {
        // 重力が軽いうちは、乗っても平気
        // （WorldGravity.Value は下向きでマイナスなので、大きさで比べる）
        if (Mathf.Abs(WorldGravity.Value) < breakGravity)
        {
            return;
        }

        if (TryFindStepper(out Vector3 footPosition))
        {
            Crack(footPosition);
        }
    }

    /// <summary>
    /// **上面のすぐ上に、誰か乗っているか。**
    /// 乗っていれば、その足もとの位置を返す（ヒビをそこから広げるため）。
    /// </summary>
    private bool TryFindStepper(out Vector3 footPosition)
    {
        FindSteppers();
        foreach (Vector3 position in steppers.Values)
        {
            footPosition = position;
            return true;
        }
        footPosition = transform.position;
        return false;
    }

    private void FindSteppers()
    {
        steppers.Clear();

        Vector3 size = Vector3.Scale(box.size, transform.lossyScale);
        Vector3 up = transform.up;

        // 上面の少し内側から、上へ detectHeight ぶんの薄い箱
        // （横の広さは床の 96%＝半分の 0.48倍。ふちよりわずかに内側）
        Vector3 top = transform.TransformPoint(box.center) + up * (size.y * 0.5f);
        Vector3 center = top + up * (detectHeight * 0.5f);
        Vector3 halfExtents = new Vector3(size.x * 0.48f, detectHeight * 0.5f, size.z * 0.48f);

        // ~0 は「すべてのレイヤー」。すり抜ける当たり判定（トリガー）は数えない
        int count = Physics.OverlapBoxNonAlloc(
            center, halfExtents, Hits, transform.rotation, ~0, QueryTriggerInteraction.Ignore);

        for (int i = 0; i < count; i++)
        {
            Collider hit = Hits[i];

            // 床そのものと、床の子（ヒビの線など）は数えない
            if (hit == null || hit == box || hit.transform.IsChildOf(transform))
            {
                continue;
            }

            CharacterController character = hit as CharacterController ?? hit.GetComponentInParent<CharacterController>();
            if (onlyCharacters && character == null)
            {
                continue;
            }

            // 同じプレイヤーの複数の Collider を二重に数えない。
            Component owner = character != null ? (Component)character : hit.attachedRigidbody != null ? (Component)hit.attachedRigidbody : hit;
            Vector3 foot = hit.bounds.center - up * hit.bounds.extents.y;
            if (character != null)
            {
                foot = character.transform.TransformPoint(character.center) - character.transform.up *
                    (character.height * Mathf.Abs(character.transform.lossyScale.y) * 0.5f);
            }
            float height = Vector3.Dot(foot - top, up);
            // 横から触れた人や、他の人の上に乗っている人は床の上の人数に含めない。
            if (height < -0.1f || height > detectHeight) continue;
            steppers[owner.GetInstanceID()] = foot;
        }
    }

    // ------------------------------------------------------------
    // ヒビ
    // ------------------------------------------------------------

    public void ApplyBlast(Vector3 blastCenter)
    {
        if (blastHitsToBreak <= 0 || State == FloorState.Broken || finalCracks != null)
        {
            return;
        }

        CrackPattern pattern = BuildCracks(box.ClosestPoint(blastCenter), append: true, startAtEdge: true);
        pattern.Progress = 1f;
        RevealPattern(pattern);

        blastHits++;
        if (blastHits >= blastHitsToBreak)
        {
            Break();
        }
    }

    /// <summary>
    /// **ヒビを入れる。** 外から割れかけにしたいときにも呼べる。
    /// <paramref name="from"/> は、ヒビが広がり始める場所（だいたいでよい）。
    /// </summary>
    public void Crack(Vector3 from)
    {
        if (State != FloorState.Intact)
        {
            return;
        }

        State = FloorState.Cracked;
        BreakRemaining = secondsUntilBreak;

        BuildCracks(from);

        if (logChanges)
        {
            Debug.Log($"[GLASS] {name}：ヒビが入った（重力 {Mathf.Abs(WorldGravity.Value):0.0}）。" +
                      $"{secondsUntilBreak:0.0}秒後に割れる", this);
        }

        if (secondsUntilBreak <= 0f)
        {
            Break();
        }
    }

    /// <summary>
    /// ヒビが入っている間、毎フレーム呼ぶ。ヒビを広げ、ふちを点滅させ、時間が来たら割る。
    /// **重力はもう見ない**（一度ヒビが入ったら、軽くなっても止まらない）。
    /// </summary>
    private void UpdateCracked()
    {
        BreakRemaining -= Time.deltaTime;

        // 割れるまでの進み具合。ヒビが入った瞬間が 0、割れる瞬間が 1
        float progress = secondsUntilBreak > 0f ? 1f - BreakRemaining / secondsUntilBreak : 1f;

        RevealCracks(progress);
        BlinkEdge(progress);

        if (BreakRemaining <= 0f)
        {
            Break();
        }
    }

    /// <summary>
    /// ヒビの線を作る。**踏んだ場所から放射状に、ギザギザに**伸ばす。
    ///
    /// 線は最初は隠しておき、割れる時間に近づくほど**踏んだ場所に近いほうから順に見せていく**
    /// （ヒビが広がっていくように見える）。
    /// </summary>
    private CrackPattern BuildCracks(Vector3 from, bool append = false, bool startAtEdge = false)
    {
        CrackPattern pattern = new CrackPattern();
        crackPatterns.Add(pattern);

        if (crackMaterial == null)
        {
            Debug.LogWarning($"{name}: ヒビのマテリアルが入っていません。ヒビは見えませんが、割れる動きはします。", this);
            return pattern;
        }

        // Cube の Scale がゆがんでいても線が斜めにゆがまないよう、
        // 入れ物の大きさを打ち消して「1倍」に戻しておく
        Vector3 scale = transform.lossyScale;

        if (crackRoot == null)
        {
            crackRoot = new GameObject("Cracks").transform;
            crackRoot.SetParent(transform, false);
            crackRoot.localScale = new Vector3(
                SafeInverse(scale.x), SafeInverse(scale.y), SafeInverse(scale.z));
        }

        // ここから下は、crackRoot の中での座標（大きさを打ち消してあるので、単位はそのままメートル）。
        // 線は上面の上に平らに並べるので、2次元の (x, y) を床の (x, z) として扱う
        Vector3 size = Vector3.Scale(box.size, scale);
        float halfX = size.x * 0.5f;
        float halfZ = size.z * 0.5f;

        // 上面の高さ。0.004（4ミリ）だけ浮かせて、ガラスの面とちらついて重ならないようにする
        float topY = box.center.y * scale.y + size.y * 0.5f + 0.004f;

        // 踏んだ場所を、上面の上の位置に直す（ふちに寄りすぎないように少し内側へ）
        // 爆発の場合は、ガラスのふちも起点にできる。
        Vector3 local = transform.InverseTransformPoint(from);
        float originLimit = startAtEdge ? 1f : 0.96f;
        Vector2 origin = new Vector2(
            Mathf.Clamp((local.x - box.center.x) * scale.x, -halfX * originLimit, halfX * originLimit),
            Mathf.Clamp((local.z - box.center.z) * scale.z, -halfZ * originLimit, halfZ * originLimit));

        // 破断前に伸ばす設定では、最初のヒビを短くして先端が伸びる余地を残す。
        float reach = Mathf.Max(halfX, halfZ) * (fractureExtensionSeconds > 0f ? 0.85f : 1.5f);

        // 1本目の向き（度）。毎回変えて、同じ形のヒビにならないようにする
        float startAngle = Random.Range(0f, 360f);

        for (int line = 0; line < crackLines; line++)
        {
            // 線を等間隔の向きに並べ、1本ごとに ±15度だけずらす（きれいに並びすぎないように）
            float angle = (startAngle + 360f * line / crackLines + Random.Range(-15f, 15f)) * Mathf.Deg2Rad;

            GrowCrackLine(origin, angle, reach, halfX, halfZ, topY, pattern);
        }

        // 踏んだ場所に近い順に並べる（名前の先頭が「何本目の区切りか」）。見せるときにこの順で出す
        // ※ 名前を文字として比べているので、区切りが10個以上になると「10」が「2」より前に来てしまう。
        //    いまは1本あたり最大4区切り（0〜3）なので問題ない
        if (!append)
        {
            foreach (CrackSegment segment in pattern.Segments)
            {
                crackSegments.Add(segment.Transform.gameObject);
            }
            crackSegments.Sort((a, b) => a.name.CompareTo(b.name));
        }

        if (append) RevealPattern(pattern);
        else RevealCracks(0f);
        return pattern;
    }

    /// <summary>
    /// ヒビの線を**1本**伸ばす。<paramref name="origin"/> から <paramref name="angle"/>（ラジアン）の向きへ、
    /// 3〜4個の区切りに分けて、**少しずつ曲げながら**進む。床のふちに着いたら、そこで止める。
    ///
    /// 座標は crackRoot の中での (x, z)。<paramref name="topY"/> は線を並べる高さ。
    /// </summary>
    private void GrowCrackLine(Vector2 origin, float angle, float reach, float halfX, float halfZ, float topY, CrackPattern pattern)
    {
        Vector2 point = origin;
        Vector2 direction = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));

        // Random.Range(int, int) は2つ目の数を含まないので、区切りは3個か4個
        int segments = Random.Range(3, 5);
        float step = reach / segments;

        for (int s = 0; s < segments; s++)
        {
            // 少しずつ向きをずらして、ギザギザにする
            // （±0.45ラジアン＝約±26度。1区切りの長さも 0.7〜1.1倍でばらつかせる）
            float bend = angle + Random.Range(-0.45f, 0.45f);
            Vector2 next = point + new Vector2(Mathf.Cos(bend), Mathf.Sin(bend)) * step * Random.Range(0.7f, 1.1f);

            // 床の外にはみ出したら、ふちで止める
            bool outside = Mathf.Abs(next.x) > halfX || Mathf.Abs(next.y) > halfZ;
            next.x = Mathf.Clamp(next.x, -halfX, halfX);
            next.y = Mathf.Clamp(next.y, -halfZ, halfZ);

            // s は「踏んだ場所から何個目の区切りか」。見せる順番に使う
            CreateSegment(point, next, topY, s, segments, pattern);

            if ((next - point).sqrMagnitude > 0.0001f)
            {
                direction = (next - point).normalized;
            }
            point = next;

            if (outside)
            {
                break;
            }
        }
        if (fractureExtensionSeconds > 0f)
        {
            crackTips.Add((point, direction));
        }
    }

    /// <summary>
    /// ヒビの線の1区切りを作る。**細長い Cube を、<paramref name="from"/> から <paramref name="to"/> へ渡す**形。
    /// <paramref name="depth"/> は踏んだ場所から何個目の区切りか（見せる順番に使う）。
    /// </summary>
    private void CreateSegment(Vector2 from, Vector2 to, float y, int depth, int segments, CrackPattern pattern)
    {
        Vector2 delta = to - from;
        float length = delta.magnitude;

        // 1センチ未満は見えないうえ、向きが決まらないので作らない（ふちのすぐ近くで止められたときなどに起きる）
        if (length < (fractureExtensionSeconds > 0f ? 0.0001f : 0.01f))
        {
            return;
        }

        GameObject segment = GameObject.CreatePrimitive(PrimitiveType.Cube);

        // 名前の先頭を「どれだけ奥か」にしておき、並べ替えに使う
        segment.name = $"{depth}_Crack";

        RemoveCollider(segment);

        // 2点のまん中に置き、Z（奥行き）を線の向きに合わせて、Z方向に長さぶん伸ばす。
        // 厚みは 0.008（8ミリ）。長さに線の太さを足しておくと、区切りのつなぎ目にすき間ができない
        segment.transform.SetParent(crackRoot, false);
        Vector3 centerOffset = new Vector3(box.center.x * transform.lossyScale.x, 0f, box.center.z * transform.lossyScale.z);
        Vector3 start = new Vector3(from.x, y, from.y) + centerOffset;
        Vector3 end = new Vector3(to.x, y, to.y) + centerOffset;
        segment.transform.localPosition = (start + end) * 0.5f;
        segment.transform.localRotation = Quaternion.LookRotation(new Vector3(delta.x, 0f, delta.y));
        segment.transform.localScale = new Vector3(crackWidth, 0.008f, length + crackWidth);

        MeshRenderer segmentRenderer = segment.GetComponent<MeshRenderer>();
        segmentRenderer.sharedMaterial = crackMaterial;
        segmentRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

        pattern.Segments.Add(new CrackSegment
        {
            Transform = segment.transform, Start = start, End = end, Length = length,
            StartProgress = (float)depth / segments, EndProgress = (float)(depth + 1) / segments,
        });
    }

    private void RevealPattern(CrackPattern pattern)
    {
        foreach (CrackSegment segment in pattern.Segments)
        {
            float amount = Mathf.InverseLerp(segment.StartProgress, segment.EndProgress, pattern.Progress);
            segment.Transform.gameObject.SetActive(amount > 0f);
            segment.Transform.localPosition = Vector3.Lerp(segment.Start, segment.End, amount * 0.5f);
            segment.Transform.localScale = new Vector3(crackWidth, 0.008f, (segment.Length + crackWidth) * amount);
        }
    }

    /// <summary>ヒビを、進み具合に合わせて見せていく。最初から4割は見えている。</summary>
    private void RevealCracks(float progress)
    {
        // 見せる割合は 4割 → 10割。progress を 1.4倍しているので、
        // 割れる時間の約7割が過ぎたところで全部見えきる（最後の3割は全部出たまま点滅だけが速くなる）
        int visible = Mathf.CeilToInt(crackSegments.Count * Mathf.Lerp(0.4f, 1f, Mathf.Clamp01(progress * 1.4f)));

        for (int i = 0; i < crackSegments.Count; i++)
        {
            bool show = i < visible;

            if (crackSegments[i].activeSelf != show)
            {
                crackSegments[i].SetActive(show);
            }
        }
    }

    /// <summary>ふちを赤く点滅させる。**割れる直前ほど速く**する。</summary>
    private void BlinkEdge(float progress)
    {
        if (!hasEdgeColor)
        {
            return;
        }

        // 点滅の速さ（1秒あたりのラジアン）。4 は約1.6秒に1回、18 は約0.35秒に1回の点滅
        float speed = Mathf.Lerp(4f, 18f, progress);

        // sin の -1〜1 を 0〜1 に直したもの
        float blink = (Mathf.Sin(Time.time * speed) + 1f) * 0.5f;

        // 一番薄いときでも 4割は赤を混ぜる（点滅の谷で、ヒビが入っていることを見失わないように）
        SetEdgeColor(Color.Lerp(originalEdgeColor, crackEdgeColor, Mathf.Lerp(0.4f, 1f, blink)));
    }

    /// <summary>この床のふちの色だけを変える（<see cref="propertyBlock"/> を通すので、マテリアルは書き換えない）。</summary>
    private void SetEdgeColor(Color color)
    {
        meshRenderer.GetPropertyBlock(propertyBlock);
        propertyBlock.SetColor(EdgeColorId, color);
        meshRenderer.SetPropertyBlock(propertyBlock);
    }

    /// <summary>ヒビの線を全部消す。線は crackRoot の子なので、親ごと消せば済む。</summary>
    private void ClearCracks()
    {
        crackSegments.Clear();
        crackPatterns.Clear();
        crackTips.Clear();
        finalCracks = null;
        fractureTime = 0f;
        standingCracks.Clear();

        if (crackRoot != null)
        {
            crackRoot.gameObject.SetActive(false);
            if (Application.isPlaying) Destroy(crackRoot.gameObject);
            else DestroyImmediate(crackRoot.gameObject);
            crackRoot = null;
        }
    }

    // ------------------------------------------------------------
    // 割れる・戻る
    // ------------------------------------------------------------

    /// <summary>**割る。** 外からすぐ割りたいときにも呼べる。</summary>
    public void Break()
    {
        if (State == FloorState.Broken || finalCracks != null)
        {
            return;
        }

        if (fractureExtensionSeconds <= 0f)
        {
            FinishBreak();
            return;
        }

        if (crackPatterns.Count == 0)
        {
            BuildCracks(transform.TransformPoint(box.center), append: true);
        }

        foreach (CrackPattern pattern in crackPatterns)
        {
            pattern.Progress = 1f;
            RevealPattern(pattern);
        }

        Vector3 scale = transform.lossyScale;
        Vector3 size = Vector3.Scale(box.size, scale);
        float halfX = size.x * 0.5f;
        float halfZ = size.z * 0.5f;
        float topY = box.center.y * scale.y + size.y * 0.5f + 0.004f;
        finalCracks = new CrackPattern();
        crackPatterns.Add(finalCracks);

        foreach (var tip in crackTips)
        {
            Vector2 end = GlassFractureGeometry.ExtendToEdge(tip.Point, tip.Direction, halfX, halfZ);
            CreateSegment(tip.Point, end, topY, 0, 1, finalCracks);
        }
        RevealPattern(finalCracks);
    }

    private void FinishBreak()
    {
        State = FloorState.Broken;
        BreakRemaining = 0f;
        respawnTimer = respawnSeconds;

        if (finalCracks == null)
        {
            SpawnPieces();
        }
        else
        {
            SpawnFracturePieces();
        }
        ClearCracks();

        // **当たり判定を消す。** 乗っていた人はそのまま落ちる
        box.enabled = false;

        if (meshRenderer != null)
        {
            meshRenderer.enabled = false;
        }

        if (logChanges)
        {
            Debug.Log($"[GLASS] {name}：割れた", this);
        }
    }

    /// <summary>割れている間、毎フレーム呼ぶ。「元に戻る秒数」が決めてあれば、時間が来たら戻す。</summary>
    private void UpdateBroken()
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
    /// **元の割れていない床に戻す。**
    /// やり直しの合図（<see cref="StageReset"/>）でも呼ばれる。
    /// </summary>
    public void Restore()
    {
        ClearCracks();

        State = FloorState.Intact;
        BreakRemaining = 0f;
        stepCount = 0;
        blastHits = 0;
        accumulatedStayTime = 0f;
        previousSteppers.Clear();
        steppers.Clear();
        departedSteppers.Clear();

        box.enabled = true;

        if (meshRenderer != null)
        {
            meshRenderer.enabled = true;

            // 点滅で変えた色を消して、マテリアルそのままの見た目に戻す
            meshRenderer.SetPropertyBlock(null);
        }
    }

    private void SpawnFracturePieces()
    {
        Vector3 scale = transform.lossyScale;
        Vector3 size = Vector3.Scale(box.size, scale);
        Vector3 center = Vector3.Scale(box.center, scale);
        var cuts = new List<(Vector2 Start, Vector2 End)>();
        foreach (CrackPattern pattern in crackPatterns)
        {
            foreach (CrackSegment segment in pattern.Segments)
            {
                cuts.Add((new Vector2(segment.Start.x - center.x, segment.Start.z - center.z),
                    new Vector2(segment.End.x - center.x, segment.End.z - center.z)));
            }
        }

        foreach (List<Vector2> polygon in GlassFractureGeometry.GetFaces(cuts, size.x * 0.5f, size.z * 0.5f))
        {
            var triangles = GlassFractureGeometry.Triangulate(polygon);
            if (triangles.Count == 0)
            {
                continue;
            }

            Vector2 pivot = Vector2.zero;
            foreach (Vector2 point in polygon)
            {
                pivot += point;
            }
            pivot /= polygon.Count;

            Mesh mesh = GlassFractureGeometry.Extrude(polygon, triangles, pivot, size.y);
            var piece = new GameObject(name + "_Piece");
            piece.AddComponent<MeshFilter>().sharedMesh = mesh;
            piece.AddComponent<MeshRenderer>();
            SetUpPiece(piece, new Vector3(pivot.x, 0f, pivot.y), meshRenderer.sharedMaterial);
            Destroy(mesh, pieceLifeSeconds);
        }
    }

    /// <summary>
    /// 割れた破片を出す。**当たり判定は付けない**ので、床も人も素通りして落ちていく。
    /// 落ちる速さはいまの重力に従う（重いほど速く落ちる）。
    /// </summary>
    private void SpawnPieces()
    {
        if (meshRenderer == null)
        {
            return;
        }

        Material material = meshRenderer.sharedMaterial;
        Vector3 size = Vector3.Scale(box.size, transform.lossyScale);
        int count = Mathf.Max(1, piecesPerSide);

        // 1枚の大きさは、マス目の 92%。すき間が空いて「割れた」と分かりやすい
        Vector3 pieceSize = new Vector3(size.x / count * 0.92f, size.y, size.z / count * 0.92f);

        for (int x = 0; x < count; x++)
        {
            for (int z = 0; z < count; z++)
            {
                // 床を count×count のマス目に分けたときの、マスの中心
                Vector3 offset = new Vector3(
                    ((x + 0.5f) / count - 0.5f) * size.x,
                    0f,
                    ((z + 0.5f) / count - 0.5f) * size.z);

                SpawnPiece(offset, pieceSize, material);
            }
        }
    }

    /// <summary>
    /// 破片を1枚出す。<paramref name="offset"/> は床の中心から見たマスの中心（床の向きで、メートル）。
    /// 決めた秒数で自分から消える。
    /// </summary>
    private void SpawnPiece(Vector3 offset, Vector3 pieceSize, Material material)
    {
        GameObject piece = GameObject.CreatePrimitive(PrimitiveType.Cube);
        piece.name = name + "_Piece";

        RemoveCollider(piece);

        piece.transform.localScale = pieceSize;
        SetUpPiece(piece, offset, material);
    }

    private void SetUpPiece(GameObject piece, Vector3 offset, Material material)
    {
        piece.transform.SetPositionAndRotation(
            transform.TransformPoint(box.center) + transform.rotation * offset,
            transform.rotation);

        MeshRenderer pieceRenderer = piece.GetComponent<MeshRenderer>();
        pieceRenderer.sharedMaterial = material;
        pieceRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

        // Rigidbody は「重力で落とす・散らばらせる」ために付けている。
        // 当たり判定が無く何にもぶつからないので、重さ（0.2kg）は動きにほぼ効かない
        Rigidbody body = piece.AddComponent<Rigidbody>();
        body.mass = 0.2f;

        // 真ん中から外へ、少しだけ散らばる
        // （まん中のマスは外向きが決まらないので、ランダムな向きにする。
        //   向きに ±0.5 のばらつきを足し、回転は1秒あたり最大4ラジアンでランダムに回す）
        Vector3 outward = offset.sqrMagnitude > 0.0001f ? offset.normalized : Random.onUnitSphere;
        body.linearVelocity = (outward + Random.insideUnitSphere * 0.5f) * scatterSpeed;
        body.angularVelocity = Random.insideUnitSphere * 4f;

        Destroy(piece, pieceLifeSeconds);
    }

    /// <summary>
    /// 当たり判定を外す。**消えるのはフレームの終わり**なので、先に切っておく
    /// （切らないと、そのフレームだけ人や他の破片を押してしまう）。
    /// </summary>
    private static void RemoveCollider(GameObject target)
    {
        Collider collider = target.GetComponent<Collider>();

        if (collider != null)
        {
            collider.enabled = false;
            if (Application.isPlaying) Destroy(collider);
            else DestroyImmediate(collider);
        }
    }

    /// <summary>1 ÷ value。value がほぼ 0 のときは、0 で割らないよう 1 を返す（大きさ 0 の床でも止まらないように）。</summary>
    private static float SafeInverse(float value)
    {
        return Mathf.Abs(value) > 0.0001f ? 1f / value : 1f;
    }

    private void OnDrawGizmosSelected()
    {
        BoxCollider collider = GetComponent<BoxCollider>();

        if (collider == null)
        {
            return;
        }

        // 「乗っている」とみなす範囲を、水色の箱で示す
        // （TryFindStepper で調べている箱と同じ。横は床の 96%、高さは detectHeight）
        Vector3 size = Vector3.Scale(collider.size, transform.lossyScale);

        Gizmos.matrix = Matrix4x4.TRS(
            transform.TransformPoint(collider.center) + transform.up * (size.y * 0.5f + detectHeight * 0.5f),
            transform.rotation, Vector3.one);

        Gizmos.color = new Color(0.3f, 0.8f, 1f, 0.6f);
        Gizmos.DrawWireCube(Vector3.zero, new Vector3(size.x * 0.96f, detectHeight, size.z * 0.96f));
        Gizmos.matrix = Matrix4x4.identity;
    }
}

internal static class GlassFractureGeometry
{
    private const float Epsilon = 0.0001f;

    private static float Cross(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;

    public static Vector2 ExtendToEdge(Vector2 point, Vector2 direction, float halfX, float halfZ)
    {
        float xTime = Mathf.Abs(direction.x) > Epsilon
            ? ((direction.x > 0f ? halfX : -halfX) - point.x) / direction.x
            : float.PositiveInfinity;
        float zTime = Mathf.Abs(direction.y) > Epsilon
            ? ((direction.y > 0f ? halfZ : -halfZ) - point.y) / direction.y
            : float.PositiveInfinity;
        return point + direction * Mathf.Max(0f, Mathf.Min(xTime, zTime));
    }

    public static List<List<Vector2>> GetFaces(
        List<(Vector2 Start, Vector2 End)> cuts, float halfX, float halfZ)
    {
        var corners = new[]
        {
            new Vector2(-halfX, -halfZ), new Vector2(halfX, -halfZ),
            new Vector2(halfX, halfZ), new Vector2(-halfX, halfZ)
        };
        for (int i = 0; i < corners.Length; i++)
        {
            cuts.Add((corners[i], corners[(i + 1) % corners.Length]));
        }

        var points = new List<Vector2>();
        var neighbors = new List<List<int>>();
        foreach (var cut in cuts)
        {
            Vector2 direction = cut.End - cut.Start;
            var stops = new List<float> { 0f, 1f };
            foreach (var other in cuts)
            {
                AddIntersections(stops, cut.Start, direction, other.Start, other.End);
            }
            stops.Sort();

            int previous = AddVertex(points, neighbors, cut.Start);
            foreach (float stop in stops)
            {
                int next = AddVertex(points, neighbors, cut.Start + direction * stop);
                if (next != previous && !neighbors[previous].Contains(next))
                {
                    neighbors[previous].Add(next);
                    neighbors[next].Add(previous);
                }
                previous = next;
            }
        }

        // Walking clockwise from each incoming edge traces the face on its left.
        for (int i = 0; i < points.Count; i++)
        {
            Vector2 pivot = points[i];
            neighbors[i].Sort((a, b) => Angle(points[a] - pivot).CompareTo(Angle(points[b] - pivot)));
        }

        var visited = new HashSet<(int, int)>();
        var faces = new List<List<Vector2>>();
        for (int start = 0; start < points.Count; start++)
        {
            foreach (int neighbor in neighbors[start])
            {
                var face = new List<Vector2>();
                int from = start;
                int to = neighbor;
                while (visited.Add((from, to)))
                {
                    face.Add(points[from]);
                    List<int> nextEdges = neighbors[to];
                    int incoming = nextEdges.IndexOf(from);
                    from = to;
                    to = nextEdges[(incoming + nextEdges.Count - 1) % nextEdges.Count];
                }

                float area = 0f;
                for (int i = 0; i < face.Count; i++)
                {
                    area += Cross(face[i], face[(i + 1) % face.Count]);
                }
                if (area > Epsilon)
                {
                    faces.Add(face);
                }
            }
        }
        return faces;
    }

    private static float Angle(Vector2 direction) => Mathf.Atan2(direction.y, direction.x);

    private static int AddVertex(List<Vector2> points, List<List<int>> neighbors, Vector2 point)
    {
        for (int i = 0; i < points.Count; i++)
        {
            if ((points[i] - point).sqrMagnitude <= Epsilon * Epsilon)
            {
                return i;
            }
        }
        points.Add(point);
        neighbors.Add(new List<int>());
        return points.Count - 1;
    }

    private static void AddIntersections(
        List<float> stops, Vector2 start, Vector2 direction, Vector2 otherStart, Vector2 otherEnd)
    {
        Vector2 otherDirection = otherEnd - otherStart;
        Vector2 offset = otherStart - start;
        float determinant = Cross(direction, otherDirection);
        if (Mathf.Abs(determinant) > Epsilon * direction.magnitude * otherDirection.magnitude)
        {
            float t = Cross(offset, otherDirection) / determinant;
            float u = Cross(offset, direction) / determinant;
            if (t >= -Epsilon && t <= 1f + Epsilon && u >= -Epsilon && u <= 1f + Epsilon)
            {
                stops.Add(Mathf.Clamp01(t));
            }
            return;
        }

        // Collinear cuts can overlap along a pane edge. Split both at the shared endpoints.
        float length = direction.magnitude;
        if (length <= Epsilon || Mathf.Abs(Cross(offset, direction)) > Epsilon * length)
        {
            return;
        }
        float first = Vector2.Dot(offset, direction) / direction.sqrMagnitude;
        float last = Vector2.Dot(otherEnd - start, direction) / direction.sqrMagnitude;
        stops.Add(Mathf.Clamp01(first));
        stops.Add(Mathf.Clamp01(last));
    }

    public static List<int> Triangulate(List<Vector2> polygon)
    {
        var remaining = new List<int>();
        var triangles = new List<int>();
        for (int i = 0; i < polygon.Count; i++)
        {
            remaining.Add(i);
        }

        while (remaining.Count >= 3)
        {
            bool clipped = false;
            for (int i = 0; i < remaining.Count; i++)
            {
                int a = remaining[(i + remaining.Count - 1) % remaining.Count];
                int b = remaining[i];
                int c = remaining[(i + 1) % remaining.Count];
                Vector2 ab = polygon[b] - polygon[a];
                Vector2 bc = polygon[c] - polygon[b];
                float turn = Cross(ab, bc);
                if (Mathf.Abs(turn) <= Epsilon * Epsilon && Vector2.Dot(ab, bc) >= 0f)
                {
                    remaining.RemoveAt(i);
                    clipped = true;
                    break;
                }
                if (turn <= 0f || ContainsOtherVertex(polygon, remaining, a, b, c))
                {
                    continue;
                }

                triangles.Add(a);
                triangles.Add(b);
                triangles.Add(c);
                remaining.RemoveAt(i);
                clipped = true;
                break;
            }
            if (!clipped)
            {
                triangles.Clear();
                break;
            }
        }
        return triangles;
    }

    private static bool ContainsOtherVertex(List<Vector2> polygon, List<int> remaining, int a, int b, int c)
    {
        foreach (int index in remaining)
        {
            if (index == a || index == b || index == c)
            {
                continue;
            }
            Vector2 point = polygon[index];
            if (Cross(polygon[b] - polygon[a], point - polygon[a]) >= 0f
                && Cross(polygon[c] - polygon[b], point - polygon[b]) >= 0f
                && Cross(polygon[a] - polygon[c], point - polygon[c]) >= 0f)
            {
                return true;
            }
        }
        return false;
    }

    public static Mesh Extrude(List<Vector2> polygon, List<int> faces, Vector2 pivot, float thickness)
    {
        var vertices = new List<Vector3>();
        var triangles = new List<int>();
        int count = polygon.Count;
        float top = thickness * 0.5f;

        foreach (Vector2 point in polygon)
        {
            vertices.Add(Position(point, pivot, top));
        }
        foreach (Vector2 point in polygon)
        {
            vertices.Add(Position(point, pivot, -top));
        }
        for (int i = 0; i < faces.Count; i += 3)
        {
            triangles.Add(faces[i]);
            triangles.Add(faces[i + 2]);
            triangles.Add(faces[i + 1]);
            triangles.Add(faces[i] + count);
            triangles.Add(faces[i + 1] + count);
            triangles.Add(faces[i + 2] + count);
        }

        for (int i = 0; i < count; i++)
        {
            int next = (i + 1) % count;
            int start = vertices.Count;
            vertices.Add(vertices[i]);
            vertices.Add(vertices[next]);
            vertices.Add(vertices[next + count]);
            vertices.Add(vertices[i + count]);
            triangles.Add(start);
            triangles.Add(start + 1);
            triangles.Add(start + 2);
            triangles.Add(start);
            triangles.Add(start + 2);
            triangles.Add(start + 3);
        }

        var mesh = new Mesh { name = "Glass shard" };
        mesh.SetVertices(vertices);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateNormals();
        return mesh;
    }

    private static Vector3 Position(Vector2 point, Vector2 pivot, float height)
    {
        return new Vector3(point.x - pivot.x, height, point.y - pivot.y);
    }
}
