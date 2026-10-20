using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// **合図を受けて、位置・回転・大きさが変わる物。**
///
/// 「閉じた状態」と「開いた状態」の**2つを行き来する**だけの部品。
/// 扉・壁・足場・仕掛けなど、**動いてほしい物なら何にでも付けられる。**
///
/// - <see cref="PressButton"/> の「踏まれたときにすること」に <see cref="Toggle"/> を繋ぐ
/// - <see cref="LockedDoor"/> に入れておけば、鍵で開く扉になる
///
/// **置いた場所・向き・大きさが「閉じた状態」**になる。
/// そこからどれだけ変わるかを、下の3つで決める。
///
/// | 決めるもの | 例 |
/// | --- | --- |
/// | 位置 | (0, -3, 0) で床に沈む／(3, 0, 0) で横にスライド |
/// | 回転 | (0, 90, 0) で90度開く扉になる |
/// | 大きさ | (0, -2, 0) で縮んで消える壁になる |
///
/// **3つは同時に使える。** 回りながら縮む、なども作れる。
///
/// ## 入力のしかたが2通りある
///
/// それぞれに「**差分**」と「**変更後の値**」の2つの欄がある。
/// **どちらかを入れると、もう一方が自動で計算される。**
///
/// - 位置Xが10のとき、**差分に -2** を入れると → 変更後は **8**
/// - 位置Xが10のとき、**変更後に -2** を入れると → 差分は **-12**
///
/// 好きなほうで入力すればよい。
/// **実際に動くときに使っているのは「差分」のほう**なので、
/// 物を置き直しても、**同じだけ動く**（プレハブを別の場所に置いても同じ動きになる）。
///
/// ## 気をつけること
///
/// **上に乗っている物は、一緒に運ばれない。**
/// ロボットは `CharacterController` で動いているので、床が動いても付いてこない。
/// 「動く床に乗って運ばれる」遊びをしたい場合は、別の作りが要る。
/// </summary>
public class MoveObject : MonoBehaviour
{
    [Header("動き")]
    [Tooltip("動き終わるまでの時間（秒）")]
    [Range(0.05f, 10f)]
    [SerializeField] private float moveTime = 1.2f;

    [Tooltip("ONにすると、最初から開いた状態で始まる")]
    [SerializeField] private bool startOpen = false;

    [Header("位置")]
    [Tooltip("**開いたときに、どれだけ動くか**（いまの位置からのずれ）")]
    [FormerlySerializedAs("moveOffset")]
    [SerializeField] private Vector3 positionOffset = new Vector3(0f, -3.2f, 0f);

    [Tooltip("**開いたときの位置そのもの。** ここに入れると、上の差分が自動で計算される")]
    [SerializeField] private Vector3 positionResult;

    [Header("回転")]
    [Tooltip("**開いたときに、どれだけ回るか**（度）。(0, 90, 0) で横に90度開く")]
    [SerializeField] private Vector3 rotationOffset = Vector3.zero;

    [Tooltip("**開いたときの角度そのもの。** ここに入れると、上の差分が自動で計算される")]
    [SerializeField] private Vector3 rotationResult;

    [Header("大きさ")]
    [Tooltip("**開いたときに、どれだけ大きさが変わるか**（足し算）。(0, -2, 0) で縦に2縮む")]
    [SerializeField] private Vector3 scaleOffset = Vector3.zero;

    [Tooltip("**開いたときの大きさそのもの。** ここに入れると、上の差分が自動で計算される")]
    [SerializeField] private Vector3 scaleResult;

    // 前回の値。**どちらの欄が書き換えられたか**を見分けるために覚えておく
    [HideInInspector] [SerializeField] private Vector3 lastPositionOffset;
    [HideInInspector] [SerializeField] private Vector3 lastPositionResult;
    [HideInInspector] [SerializeField] private Vector3 lastRotationOffset;
    [HideInInspector] [SerializeField] private Vector3 lastRotationResult;
    [HideInInspector] [SerializeField] private Vector3 lastScaleOffset;
    [HideInInspector] [SerializeField] private Vector3 lastScaleResult;

    /// <summary>いま開こうとしているか（動いている途中も含む）。</summary>
    public bool IsOpen { get; private set; }

    /// <summary>動いている最中か。</summary>
    public bool IsMoving => !Mathf.Approximately(amount, IsOpen ? 1f : 0f);

    /// <summary>閉じた状態。置いたときの位置・向き・大きさ。</summary>
    private Vector3 closedPosition;

    /// <summary>閉じた状態の向き（度。X・Y・Z の回転角）。</summary>
    private Vector3 closedRotation;

    /// <summary>閉じた状態の大きさ。</summary>
    private Vector3 closedScale;

    /// <summary>開き具合。0で閉じた状態、1で開いた状態。</summary>
    private float amount;

    private void Awake()
    {
        closedPosition = transform.localPosition;
        closedRotation = transform.localEulerAngles;
        closedScale = transform.localScale;

        IsOpen = startOpen;
        amount = startOpen ? 1f : 0f;

        Apply();
    }

    private void Update()
    {
        float target = IsOpen ? 1f : 0f;

        if (Mathf.Approximately(amount, target))
        {
            return;
        }

        // 1秒あたりに進む開き具合。moveTime 秒で 0 → 1 を進みきる。
        // 0秒が入っていたときは、0.01秒で動ききる速さにする（0 で割らないため）
        float speed = moveTime > 0f ? 1f / moveTime : 100f;

        amount = Mathf.MoveTowards(amount, target, speed * Time.deltaTime);

        Apply();
    }

    /// <summary>
    /// **開いていれば閉じ、閉じていれば開く。**
    /// ボタンや扉から繋ぐのは普通これ。
    /// </summary>
    public void Toggle()
    {
        SetOpen(!IsOpen);
    }

    /// <summary>開ける（差分のぶんだけ動かす）。</summary>
    public void Open()
    {
        SetOpen(true);
    }

    /// <summary>閉じる（**置いた場所へ戻す**）。</summary>
    public void Close()
    {
        SetOpen(false);
    }

    /// <summary>開くか閉じるかを指定する。動いている途中でも受け付ける。</summary>
    public void SetOpen(bool open)
    {
        if (IsOpen == open)
        {
            return;
        }

        IsOpen = open;

        Debug.Log($"[STAGE] {name} が{(open ? "開き" : "閉じ")}ます");
    }

    /// <summary>いまの開き具合を、位置・回転・大きさに反映する。</summary>
    private void Apply()
    {
        transform.localPosition = Vector3.Lerp(closedPosition, closedPosition + positionOffset, amount);

        // ※ Slerp は**近いほうの回り方**で回る。180度を超える回転（例：270度）を入れると、
        //    逆向きに 90度回って見える（行き着く角度は同じ）
        transform.localRotation = Quaternion.Slerp(
            Quaternion.Euler(closedRotation),
            Quaternion.Euler(closedRotation + rotationOffset),
            amount);

        transform.localScale = Vector3.Lerp(closedScale, closedScale + scaleOffset, amount);
    }

    // ------------------------------------------------------------
    // インスペクターでの入力
    // ------------------------------------------------------------

    /// <summary>
    /// **「差分」と「変更後の値」を、いつも合った状態に保つ。**
    ///
    /// どちらかを書き換えたら、もう一方を計算し直す。
    /// **物を動かしたときは差分のほうを残す**（同じだけ動く、が保たれる）。
    /// </summary>
    private void OnValidate()
    {
        // 再生中は、動いている途中の値で上書きしてしまうので触らない
        if (Application.isPlaying)
        {
            return;
        }

        SyncPair(transform.localPosition,
            ref positionOffset, ref positionResult, ref lastPositionOffset, ref lastPositionResult);

        // 回転は、Unity が 0〜360度に直した角度をもとに計算する。
        // そのため、-10度に置いた物の「変更後」は 350 のような数で出ることがある（向きとしては同じ）
        SyncPair(transform.localEulerAngles,
            ref rotationOffset, ref rotationResult, ref lastRotationOffset, ref lastRotationResult);

        SyncPair(transform.localScale,
            ref scaleOffset, ref scaleResult, ref lastScaleOffset, ref lastScaleResult);
    }

    /// <summary>
    /// 差分と変更後の値の、書き換えられたほうから、もう一方を求める。
    /// <paramref name="baseValue"/> は閉じた状態（いま置かれている値）。
    /// last〜 は前回の値で、**前回と違うほう＝いま書き換えられたほう**と判断する。
    /// </summary>
    private static void SyncPair(Vector3 baseValue,
        ref Vector3 offset, ref Vector3 result, ref Vector3 lastOffset, ref Vector3 lastResult)
    {
        if (offset != lastOffset)
        {
            result = baseValue + offset;
        }
        else if (result != lastResult)
        {
            offset = result - baseValue;
        }
        else
        {
            // どちらも触られていない＝物のほうが動いた。差分はそのままにして、結果を合わせ直す
            result = baseValue + offset;
        }

        lastOffset = offset;
        lastResult = result;
    }

    /// <summary>
    /// シーンビューに、**開いたときの姿**を描く。置くときに動く先が分かるように。
    ///
    /// **位置だけでなく、回転と大きさも反映している。**
    /// 90度回して開く扉などは、線と箱を見れば動く先が分かる。
    ///
    /// 描き方は `Gizmos.matrix` に「開いたときの姿」を入れて、
    /// **その中で原点に箱を描く**形にしている。
    /// 位置・回転・大きさを別々に計算するより確実なため。
    /// </summary>
    private void OnDrawGizmosSelected()
    {
        // 閉じた状態。再生中は控えてある値、止まっているときは置かれたままの値
        Vector3 basePosition = Application.isPlaying ? closedPosition : transform.localPosition;
        Vector3 baseRotation = Application.isPlaying ? closedRotation : transform.localEulerAngles;
        Vector3 baseScale = Application.isPlaying ? closedScale : transform.localScale;

        Matrix4x4 parent = transform.parent != null
            ? transform.parent.localToWorldMatrix
            : Matrix4x4.identity;

        Matrix4x4 closed = parent * Matrix4x4.TRS(
            basePosition, Quaternion.Euler(baseRotation), baseScale);

        Matrix4x4 open = parent * Matrix4x4.TRS(
            basePosition + positionOffset,
            Quaternion.Euler(baseRotation + rotationOffset),
            baseScale + scaleOffset);

        Gizmos.color = new Color(0.4f, 0.8f, 1f);

        // 「いまの場所」から「開いたときの場所」へ線を引く
        Gizmos.DrawLine(closed.MultiplyPoint3x4(Vector3.zero), open.MultiplyPoint3x4(Vector3.zero));

        Bounds bounds = LocalBounds();

        Gizmos.matrix = open;
        Gizmos.DrawWireCube(bounds.center, bounds.size);

        // 触ったあとは必ず戻す。戻さないと、あとに描かれる線まで傾いてしまう
        Gizmos.matrix = Matrix4x4.identity;
    }

    /// <summary>
    /// この物の**大きさを入れる前の形**。見た目が入っていればその形、無ければ1メートルの箱。
    /// </summary>
    private Bounds LocalBounds()
    {
        MeshFilter filter = GetComponent<MeshFilter>();

        if (filter != null && filter.sharedMesh != null)
        {
            return filter.sharedMesh.bounds;
        }

        return new Bounds(Vector3.zero, Vector3.one);
    }
}
