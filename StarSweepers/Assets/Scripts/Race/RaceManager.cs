using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// **レースの進行役。**
///
/// 「用意 → カウントダウン → 走行 → ゴール」を進め、
/// **誰が何周したか**を数える。
///
/// | 段階 | 何が起きるか |
/// | --- | --- |
/// | 用意（Waiting） | 少し待つ。カートは動かせない |
/// | カウントダウン（Countdown） | 3・2・1。カートは動かせない |
/// | 走行（Running） | 走れる。時間を測る |
/// | 終了（Finished） | 全員がゴールした |
///
/// ## 通信（マルチプレイ）にする時のために
///
/// **周回を数えるのは、この1か所だけ**にしてある。
/// 通信にするときは、**この部品をホストの上でだけ動かし**、
/// 段階と周回数を配れば済む形を狙っている。
/// カート側（<see cref="KartController"/>）には順位も周回も持たせていない。
/// </summary>
// 走る側（カート）が動いたあとに調べたいので、最後に回す
[DefaultExecutionOrder(100)]
public class RaceManager : MonoBehaviour
{
    /// <summary>レースの進み具合。</summary>
    public enum Phase
    {
        /// <summary>用意（少し待つ）</summary>
        Waiting,

        /// <summary>カウントダウン中</summary>
        Countdown,

        /// <summary>走行中</summary>
        Running,

        /// <summary>全員ゴール</summary>
        Finished,
    }

    /// <summary>いま動いているレース。HUDなどが見に来る。</summary>
    public static RaceManager Current { get; private set; }

    [Header("ルール")]
    [Tooltip("**何周でゴールか。** まずは1周")]
    [Min(1)]
    [SerializeField] private int lapsToFinish = 1;

    [Tooltip("再生してから、カウントダウンが始まるまでの秒数")]
    [SerializeField] private float readySeconds = 1.5f;

    [Tooltip("カウントダウンの秒数（3なら 3・2・1）")]
    [Min(1)]
    [SerializeField] private int countdownSeconds = 3;

    [Tooltip("再生したら、ひとりでにレースを始めるか")]
    [SerializeField] private bool startAutomatically = true;

    [Header("コース")]
    [Tooltip("**通過点を、通る順番に入れる。** 0番目がスタート／ゴールの線。" +
             "空のままなら、シーンの中から番号順に集める")]
    [SerializeField] private List<RaceCheckpoint> checkpoints = new List<RaceCheckpoint>();

    [Tooltip("スタート位置。**上から順に、参加している人へ割り当てる**（通信で人数が増えたとき用）")]
    [SerializeField] private List<Transform> startGrid = new List<Transform>();

    [Header("落ちたとき")]
    [Tooltip("**この高さより下に落ちたら、最後に通った通過点へ戻す**（メートル）")]
    [SerializeField] private float fallHeight = -10f;

    [Tooltip("**通過点のどれだけ手前に戻すか**（メートル）。マイナスにすると、通過点の先に出る")]
    [SerializeField] private float respawnBackDistance = 2f;

    [Tooltip("戻す場所の、地面からの高さ（メートル）。**地面が見つからなければ、通過点の高さのまま**")]
    [Min(0f)]
    [SerializeField] private float respawnAboveGround = 0.3f;

    [Tooltip("戻したときにコンソールへ書き出す")]
    [SerializeField] private bool logRespawns = true;

    [Header("操作")]
    [Tooltip("**やり直しのキー。** 押すとスタート地点に戻る")]
    [SerializeField] private Key restartKey = Key.R;

    /// <summary>いまの段階。</summary>
    public Phase State { get; private set; } = Phase.Waiting;

    /// <summary>カウントダウンの残り秒数。</summary>
    public float CountdownRemaining { get; private set; }

    /// <summary>スタートしてからの秒数。</summary>
    public float ElapsedTime { get; private set; }

    /// <summary>何周でゴールか。</summary>
    public int LapsToFinish => lapsToFinish;

    /// <summary>通過点の数。</summary>
    public int CheckpointCount => checkpoints.Count;

    /// <summary>「スタート！」を出しておく残り秒数。0より大きい間だけ出す。</summary>
    public float StartSignalRemaining { get; private set; }

    /// <summary>走り出したときに「スタート！」を画面に出しておく秒数。</summary>
    private const float StartSignalSeconds = 1.2f;

    /// <summary>
    /// 落ちた人を戻すときに、真下の地面を探す線の長さ（メートル）。
    /// 通過点の <see cref="GroundSearchStartAbove"/> メートル上から、この長さだけ下へ調べる。
    /// </summary>
    private const float GroundSearchLength = 60f;

    /// <summary>地面を探す線を、通過点の何メートル上から出すか。通過点が地面より低くても拾えるように。</summary>
    private const float GroundSearchStartAbove = 3f;

    /// <summary>ゴールした人数。次にゴールした人の順位になる。</summary>
    private int finishedCount;

    /// <summary>地面を探すときの当たりを受け取る入れ物。毎回作らずに使い回す。</summary>
    private readonly RaycastHit[] groundHits = new RaycastHit[16];

    private void Awake()
    {
        Current = this;

        CollectCheckpointsIfEmpty();
    }

    private void OnDestroy()
    {
        if (Current == this)
        {
            Current = null;
        }
    }

    private void Start()
    {
        ResetRace();
    }

    /// <summary>
    /// 毎フレームの流れ。**やり直し → 段階を進める → 落ちた人を戻す → 通過点を調べる → 位置を控える**の順。
    /// カート側が動いたあと（LateUpdate、かつ実行順 100）に回すので、このフレームの移動がすべて済んでいる。
    /// </summary>
    private void LateUpdate()
    {
        if (GamePause.IsPaused)
        {
            return;
        }

        Keyboard keyboard = Keyboard.current;

        if (keyboard != null && keyboard[restartKey].wasPressedThisFrame)
        {
            ResetRace();
            return;
        }

        AdvancePhase();

        // **周回を調べるより先に**戻す（戻した場所は、周回の判定に使わない）
        CheckFalls();

        if (State == Phase.Running || State == Phase.Finished)
        {
            CheckCheckpoints();
        }

        // 次のフレームのために、今の位置を控えておく
        for (int i = 0; i < RaceRacer.All.Count; i++)
        {
            RaceRacer.All[i].CommitPosition();
        }
    }

    // ------------------------------------------------------------
    // 進行
    // ------------------------------------------------------------

    /// <summary>用意の状態に戻し、全員をスタート地点へ並べる。</summary>
    public void ResetRace()
    {
        State = Phase.Waiting;

        // 用意の時間もこの1つの数字に含めて減らしていく。
        // 残りが countdownSeconds を切ったところで「カウントダウン」の段階に移る（AdvancePhase）
        CountdownRemaining = readySeconds + countdownSeconds;
        ElapsedTime = 0f;
        StartSignalRemaining = 0f;
        finishedCount = 0;

        // 割れた床など、コースの仕掛けも元に戻す（聞いている物だけが戻る）
        StageReset.Request();

        LineUpRacers();

        for (int i = 0; i < RaceRacer.All.Count; i++)
        {
            RaceRacer.All[i].ResetToStart(FirstCheckpoint());
        }

        if (checkpoints.Count == 0)
        {
            Debug.LogWarning($"{name}: 通過点が1つもありません。周回を数えられません。", this);
        }
    }

    /// <summary>すぐに走り出せる状態にする（用意とカウントダウンを飛ばす）。</summary>
    public void StartRace()
    {
        State = Phase.Running;
        CountdownRemaining = 0f;
        ElapsedTime = 0f;
        StartSignalRemaining = StartSignalSeconds;

        SetControlEnabled(true);
    }

    /// <summary>
    /// 時間を進めて、段階を「用意 → カウントダウン → 走行」と移す。
    /// 「終了」は <see cref="PassCheckpoint"/> で全員がゴールしたときに入るので、ここでは触らない。
    /// </summary>
    private void AdvancePhase()
    {
        if (StartSignalRemaining > 0f)
        {
            StartSignalRemaining -= Time.deltaTime;
        }

        switch (State)
        {
            case Phase.Waiting:
                if (!startAutomatically)
                {
                    return;
                }

                CountdownRemaining -= Time.deltaTime;

                if (CountdownRemaining <= countdownSeconds)
                {
                    State = Phase.Countdown;
                }

                return;

            case Phase.Countdown:
                CountdownRemaining -= Time.deltaTime;

                if (CountdownRemaining <= 0f)
                {
                    StartRace();
                }

                return;

            case Phase.Running:
                ElapsedTime += Time.deltaTime;
                return;
        }
    }

    /// <summary>全員の操作を受け付ける・受け付けないを切り替える。</summary>
    private void SetControlEnabled(bool canControl)
    {
        for (int i = 0; i < RaceRacer.All.Count; i++)
        {
            RaceRacer racer = RaceRacer.All[i];

            // ゴールした人は、そのまま止めておく
            racer.ControlEnabled = canControl && !racer.Finished;
        }
    }

    // ------------------------------------------------------------
    // 周回を数える
    // ------------------------------------------------------------

    /// <summary>
    /// 全員について、**次に狙う通過点**をこのフレームで横切ったかを調べる。
    /// 「次の通過点」だけを見るので、順番を飛ばした近道は数えられない。
    /// </summary>
    private void CheckCheckpoints()
    {
        if (checkpoints.Count == 0)
        {
            return;
        }

        for (int i = 0; i < RaceRacer.All.Count; i++)
        {
            RaceRacer racer = RaceRacer.All[i];

            if (racer.Finished)
            {
                continue;
            }

            RaceCheckpoint next = checkpoints[racer.NextCheckpoint % checkpoints.Count];

            if (next == null || !next.WasCrossed(racer.PreviousPosition, racer.TrackedPosition))
            {
                continue;
            }

            PassCheckpoint(racer, next);
        }
    }

    /// <summary>
    /// 通過点を1つ通ったときの記録。
    /// 次の通過点へ進め、スタート／ゴールの線なら周回を増やし、規定の周回に達したらゴール扱いにする。
    /// </summary>
    private void PassCheckpoint(RaceRacer racer, RaceCheckpoint passed)
    {
        // 落ちたときに戻す場所として覚えておく
        racer.LastPassedCheckpoint = racer.NextCheckpoint % checkpoints.Count;

        racer.NextCheckpoint = (racer.NextCheckpoint + 1) % checkpoints.Count;

        // **スタート／ゴールの線を通ったら1周ぶん。**
        // 他の通過点を全部通ってからでないと、ここには来られない
        if (!passed.IsStartLine)
        {
            return;
        }

        racer.Lap++;

        if (racer.Lap < lapsToFinish)
        {
            Debug.Log($"[RACE] {racer.RacerName}：{racer.Lap}周目を終えた（{FormatTime(ElapsedTime)}）");
            return;
        }

        finishedCount++;

        racer.Finished = true;
        racer.FinishTime = ElapsedTime;
        racer.Rank = finishedCount;
        racer.ControlEnabled = false;

        Debug.Log($"[RACE] {racer.RacerName}：ゴール！ {racer.Rank}位 タイム {FormatTime(racer.FinishTime)}");

        if (finishedCount >= RaceRacer.All.Count)
        {
            State = Phase.Finished;
        }
    }

    // ------------------------------------------------------------
    // 落ちたら戻す
    // ------------------------------------------------------------

    /// <summary>落ちた判定の高さより下にいる人を、コースに戻す。</summary>
    private void CheckFalls()
    {
        for (int i = 0; i < RaceRacer.All.Count; i++)
        {
            RaceRacer racer = RaceRacer.All[i];

            if (racer.LowestHeight < fallHeight)
            {
                Respawn(racer);
            }
        }
    }

    /// <summary>
    /// **最後に通った通過点の少し手前に戻す。** 周回の記録はそのまま。
    /// まだ1つも通っていなければ、スタート地点に戻す。
    /// </summary>
    public void Respawn(RaceRacer racer)
    {
        int index = racer.LastPassedCheckpoint;
        RaceCheckpoint checkpoint = index >= 0 && index < checkpoints.Count ? checkpoints[index] : null;

        if (checkpoint == null)
        {
            racer.TeleportTo(racer.StartPosition, racer.StartRotation);
            LogRespawn(racer, "スタート地点");

            return;
        }

        GetRespawnPoint(checkpoint, out Vector3 position, out Quaternion rotation);
        racer.TeleportTo(position, rotation);

        LogRespawn(racer, $"通過点{index}（{checkpoint.name}）の手前");
    }

    /// <summary>
    /// 通過点から、戻す場所と向きを求める。
    /// **進む向きに対して手前**に下がり、真下の地面の少し上に置く。
    /// </summary>
    private void GetRespawnPoint(RaceCheckpoint checkpoint, out Vector3 position, out Quaternion rotation)
    {
        // 通過点が傾いていても、向きは水平にそろえる（斜めを向いて出てこないように）
        Vector3 forward = checkpoint.Forward;
        forward.y = 0f;

        if (forward.sqrMagnitude < 0.0001f)
        {
            forward = Vector3.forward;
        }

        forward.Normalize();

        Vector3 point = checkpoint.transform.position - forward * respawnBackDistance;

        position = SnapToGround(point);
        rotation = Quaternion.LookRotation(forward);
    }

    /// <summary>
    /// **真下の地面を探して、その少し上の位置を返す。**
    /// 見つからなければ（下が穴なら）、渡された位置のまま返す。
    /// </summary>
    private Vector3 SnapToGround(Vector3 point)
    {
        // 少し上から下へ線を飛ばす。通過点そのものが地面より低い位置にあっても拾えるように
        int count = Physics.RaycastNonAlloc(
            point + Vector3.up * GroundSearchStartAbove, Vector3.down, groundHits,
            GroundSearchLength, ~0, QueryTriggerInteraction.Ignore);

        // 当たった中で一番近い（＝一番上にある）ものを地面とみなす
        float nearest = float.MaxValue;
        Vector3 ground = point;

        for (int i = 0; i < count; i++)
        {
            RaycastHit hit = groundHits[i];

            // 人の体を地面と間違えない
            if (hit.collider is CharacterController)
            {
                continue;
            }

            if (hit.distance < nearest)
            {
                nearest = hit.distance;
                ground = hit.point;
            }
        }

        return nearest < float.MaxValue ? ground + Vector3.up * respawnAboveGround : point;
    }

    /// <summary>戻した記録をコンソールに書く（インスペクターで切れる）。</summary>
    private void LogRespawn(RaceRacer racer, string where)
    {
        if (logRespawns)
        {
            Debug.Log($"[RACE] {racer.RacerName}：高さ {fallHeight:0.#} より下に落ちたので、{where}に戻した", racer);
        }
    }

    /// <summary>
    /// **走り始めるときに、次に狙う通過点。**
    ///
    /// スタート地点は0番の線の**手前**にあるので、
    /// スタート直後に0番を通ってしまう。**それを1周と数えないため**に1番から始める。
    /// </summary>
    private int FirstCheckpoint()
    {
        return checkpoints.Count > 1 ? 1 : 0;
    }

    // ------------------------------------------------------------
    // 並べる・集める
    // ------------------------------------------------------------

    /// <summary>
    /// スタート位置を、参加している人に上から順に割り当てる。
    /// 割り当てられなかった人（スタート位置が足りない）は、置かれた場所がそのままスタート地点になる。
    /// </summary>
    private void LineUpRacers()
    {
        if (startGrid.Count == 0)
        {
            return;
        }

        for (int i = 0; i < RaceRacer.All.Count && i < startGrid.Count; i++)
        {
            Transform slot = startGrid[i];

            if (slot != null)
            {
                RaceRacer.All[i].SetStartPoint(slot.position, slot.rotation);
            }
        }
    }

    /// <summary>通過点が入っていなければ、シーンから集めて番号順に並べる。</summary>
    private void CollectCheckpointsIfEmpty()
    {
        checkpoints.RemoveAll(point => point == null);

        if (checkpoints.Count > 0)
        {
            return;
        }

        checkpoints.AddRange(FindObjectsByType<RaceCheckpoint>(FindObjectsSortMode.None));
        checkpoints.Sort((a, b) => a.Order.CompareTo(b.Order));
    }

    // ------------------------------------------------------------
    // 表示用
    // ------------------------------------------------------------

    /// <summary>
    /// 選んだときに、**落ちたら戻る場所**を黄緑の球で、
    /// **落ちたと判定する高さ**を赤い枠で見せる。
    /// </summary>
    private void OnDrawGizmosSelected()
    {
        Vector3 center = Vector3.zero;
        int count = 0;

        for (int i = 0; i < checkpoints.Count; i++)
        {
            RaceCheckpoint checkpoint = checkpoints[i];

            if (checkpoint == null)
            {
                continue;
            }

            GetRespawnPoint(checkpoint, out Vector3 position, out _);

            Gizmos.color = new Color(0.6f, 1f, 0.3f, 0.9f);
            Gizmos.DrawSphere(position, 0.3f);
            Gizmos.DrawLine(position, checkpoint.transform.position);

            center += checkpoint.transform.position;
            count++;
        }

        if (count == 0)
        {
            return;
        }

        // 落ちたと判定する高さ。通過点の真ん中あたりに、広めの枠を描く
        center /= count;
        center.y = fallHeight;

        // 枠の大きさ（150m 四方）は目安。コース全体が収まる程度の広さにしてある
        Gizmos.color = new Color(1f, 0.25f, 0.2f, 0.8f);
        Gizmos.DrawWireCube(center, new Vector3(150f, 0f, 150f));
    }

    /// <summary>秒数を「1:23.45」の形にする。</summary>
    public static string FormatTime(float seconds)
    {
        if (seconds < 0f)
        {
            seconds = 0f;
        }

        int minutes = (int)(seconds / 60f);
        float rest = seconds - minutes * 60f;

        return $"{minutes}:{rest:00.00}";
    }

    /// <summary>いま画面の中央に出すべき大きな文字。無ければ空文字。</summary>
    public string BigMessage()
    {
        if (State == Phase.Countdown)
        {
            return Mathf.CeilToInt(CountdownRemaining).ToString();
        }

        if (StartSignalRemaining > 0f)
        {
            return "スタート！";
        }

        return string.Empty;
    }
}
