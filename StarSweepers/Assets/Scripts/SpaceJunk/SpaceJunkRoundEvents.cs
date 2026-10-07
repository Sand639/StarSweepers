using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// **ラウンドの途中で起きるイベント。** <see cref="SpaceJunkRound"/> の続き（同じクラスをファイルだけ分けている）。
///
/// ## 流れ（2026/9/29・大槻さん）
///
/// 1. ラウンドが始まってから <see cref="SpaceJunkEventSettings.startSeconds"/> 秒（初期値20秒）たったら
/// 2. <see cref="SpaceJunkEventSettings.pool"/>（候補の一覧）から、選ばれやすさの比で1つを選んで始める
/// 3. 候補ごとの「続く秒数」が過ぎたら終わる（0 ならラウンドの終わりまで続く）
///
/// 設定は <see cref="SpaceJunkSession"/>（プレハブ）の「イベント」の欄。**得点制（Score）のときだけ起きる。**
/// 画面に出す名前と説明もそこで変えられ、**ホストの値が全員へ配られる**。
///
/// ## デブリセット（DebrisSet）
///
/// - チームごとに、デブリを4〜6個ランダムに選んだ「お題」が出る（同じ種類が2個以上入ることもある）。
///   **個数はイベントの始まりに1回だけ決め、全チーム同じ。中身はチームごとにばらばら**（2026/9/29 修正）
/// - **お題のうち3個**（<see cref="SpaceJunkEventSettings.debrisSetGoal"/>）をゴールに入れると **10点**。
///   **そのあと新しいお題を引き直し、0から集め直す**
///   - 順不同（初期値。<see cref="SpaceJunkEventSettings.debrisSetOrdered"/> が OFF）…
///     お題に入っていて、まだ足りていない種類なら進む
///   - 順番どおり（ON）… **お題の次の1個と同じ種類のときだけ進む**（先頭から3個）
/// - お題に無い・もう足りているデブリを入れても、ふつうの1点は入る
///
/// ## 期間限定高価値デブリ（HighValueDebris。2026/9/29 修正）
///
/// - 始まると、**デブリの種類が1つランダムに選ばれる**（例：回路基板＝紫）
/// - **続く秒数のうちは、その種類を入れると +2点**（1点と合わせて3点）
/// - 画面の上に「紫（回路基板）のデブリが +2点」のように出る
///
/// ## 特殊デブリ（HeavyDebris）
///
/// - 始まると、**重さを決めた大きなデブリ**が出る（初期値は3個。重さは1個ずつ 1〜3 の間からランダム。水色に光る）
/// - **投げられない。** 右クリックのフックは引っ掛からず、左で引っ掛けて地面を引きずることしかできない
///   （ゲージが往復しない・重いほど速い・重いほど短くしか引きずれない。操作は <see cref="ThrowController"/>）
/// - ゴールに入れると、1点の代わりに高い得点（初期値20点。重さには関係ない）
///
/// ## 判定するのはホストだけ
///
/// イベントを選ぶのも、お題や高価値の種類・重いデブリを決めるのも、得点を数えるのもホスト。
/// 参加者のPCは下の一覧を受け取って出すだけ（重いデブリの見た目は、一覧を見て各PCが付ける）。
/// </summary>
public partial class SpaceJunkRound
{
    // ------------------------------------------------------------
    // イベント全体
    // ------------------------------------------------------------

    /// <summary>いま起きている（起きた）イベント。</summary>
    private readonly NetworkVariable<SpaceJunkEventKind> activeEvent =
        new NetworkVariable<SpaceJunkEventKind>(SpaceJunkEventKind.None);

    /// <summary>イベントが始まる（始まった）時刻。0 なら、このラウンドではイベントは起きない。</summary>
    private readonly NetworkVariable<double> eventServerTime = new NetworkVariable<double>(0d);

    /// <summary>イベントが終わる時刻。0 ならラウンドの終わりまで続く。</summary>
    private readonly NetworkVariable<double> eventEndServerTime = new NetworkVariable<double>(0d);

    /// <summary>画面に出すイベントの名前（ホストの設定）。</summary>
    private readonly NetworkVariable<FixedString64Bytes> eventName =
        new NetworkVariable<FixedString64Bytes>(default);

    /// <summary>画面に出すイベントの説明（ホストの設定）。</summary>
    private readonly NetworkVariable<FixedString512Bytes> eventDescription =
        new NetworkVariable<FixedString512Bytes>(default);

    /// <summary>終わりの片付けをもう済ませたか（ホストだけが使う）。</summary>
    private bool serverEventFinished;

    /// <summary>いま起きている（起きた）イベント。起きていなければ None。</summary>
    public SpaceJunkEventKind ActiveEvent => activeEvent.Value;

    /// <summary>画面に出すイベントの名前。</summary>
    public string EventName => eventName.Value.ToString();

    /// <summary>画面に出すイベントの説明。</summary>
    public string EventDescription => eventDescription.Value.ToString();

    /// <summary>イベントがいま続いているか（始まっていて、終わっていない）。</summary>
    public bool IsEventRunning
    {
        get
        {
            if (NetworkManager == null || activeEvent.Value == SpaceJunkEventKind.None)
            {
                return false;
            }

            return eventEndServerTime.Value <= 0d || NetworkManager.ServerTime.Time < eventEndServerTime.Value;
        }
    }

    /// <summary>イベントの残り秒数。**時間の決まっていないイベント、または起きていなければ -1。**</summary>
    public float EventRemainingSeconds
    {
        get
        {
            if (NetworkManager == null || activeEvent.Value == SpaceJunkEventKind.None || eventEndServerTime.Value <= 0d)
            {
                return -1f;
            }

            return Mathf.Max(0f, (float)(eventEndServerTime.Value - NetworkManager.ServerTime.Time));
        }
    }

    /// <summary>
    /// イベントが始まるまでの秒数。**このラウンドでイベントが起きない、またはもう始まっていれば -1。**
    /// </summary>
    public float SecondsUntilEvent
    {
        get
        {
            if (NetworkManager == null || eventServerTime.Value <= 0d || activeEvent.Value != SpaceJunkEventKind.None)
            {
                return -1f;
            }

            // 始まりの演出中は止めておく（START から減り始める。残り時間と同じ。2026/10/7）
            if (IsIntro)
            {
                return Mathf.Max(0f, (float)(eventServerTime.Value - PlayStartServerTime));
            }

            return Mathf.Max(0f, (float)(eventServerTime.Value - NetworkManager.ServerTime.Time));
        }
    }

    /// <summary>イベントが始まってからの秒数。始まっていなければ -1（始まった合図の表示に使う）。</summary>
    public float SecondsSinceEventStarted
    {
        get
        {
            if (NetworkManager == null || activeEvent.Value == SpaceJunkEventKind.None)
            {
                return -1f;
            }

            return Mathf.Max(0f, (float)(NetworkManager.ServerTime.Time - eventServerTime.Value));
        }
    }

    /// <summary>ホストの設定。係がいなければ初期値。</summary>
    private static SpaceJunkEventSettings EventSettings =>
        SpaceJunkSession.Current != null ? SpaceJunkSession.Current.Events : new SpaceJunkEventSettings();

    // ------------------------------------------------------------
    // デブリセットの状態
    // ------------------------------------------------------------

    /// <summary>お題のデブリのいちばん多い数。**設定でこれより大きくしても、ここで切り詰める。**</summary>
    public const int MaxSetSize = 8;

    /// <summary>
    /// お題の「必要な数」。**チーム × デブリの種類**で並べてある
    /// （<c>team * SpaceJunkMaterials.Count + 種類</c>）。
    /// </summary>
    private readonly NetworkList<int> setRequired = new NetworkList<int>();

    /// <summary>お題の「もう入れた数」。並びは <see cref="setRequired"/> と同じ。</summary>
    private readonly NetworkList<int> setProgress = new NetworkList<int>();

    /// <summary>チームごとの、いまのお題をそろえたときのボーナス。</summary>
    private readonly NetworkList<int> setBonus = new NetworkList<int>();

    /// <summary>チームごとの、お題をそろえた回数。</summary>
    private readonly NetworkList<int> setsCompleted = new NetworkList<int>();

    /// <summary>
    /// お題の中身を**並び順のまま**持つ。チームごとに <see cref="MaxSetSize"/> 個ぶんの枠
    /// （<c>team * MaxSetSize + 何番目</c>）。空いている枠は -1。
    /// </summary>
    private readonly NetworkList<int> setSequence = new NetworkList<int>();

    /// <summary>順番どおりのとき、チームごとに「何個目まで入れたか」。</summary>
    private readonly NetworkList<int> setStep = new NetworkList<int>();

    /// <summary>お題を順番どおりに入れる必要があるか。**ホストの設定を全員へ配る。**</summary>
    private readonly NetworkVariable<bool> setOrdered = new NetworkVariable<bool>(false);

    /// <summary>お題のうち、何個入れたらそろったことになるか。**ホストの設定を全員へ配る。**</summary>
    private readonly NetworkVariable<int> setGoal = new NetworkVariable<int>(3);

    /// <summary>このイベントで使うお題の個数（全チーム同じ。ホストだけが使う）。</summary>
    private int serverSetSize;

    /// <summary>お題を順番どおりに入れる必要があるか。</summary>
    public bool SetOrdered => setOrdered.Value;

    /// <summary>そのチームが、いまのお題で何個入れたらそろうか（お題の個数より多くはならない）。</summary>
    public int SetGoalOf(int team)
    {
        return Mathf.Min(setGoal.Value, SetSizeOf(team));
    }

    /// <summary>そのチームが、いまのお題でもう何個入れたか。</summary>
    public int SetDoneOf(int team)
    {
        if (setOrdered.Value)
        {
            return SetStepOf(team);
        }

        int done = 0;
        for (int k = 0; k < SpaceJunkMaterials.Count; k++)
        {
            done += SetProgressOf(team, SpaceJunkMaterials.FromIndex(k));
        }
        return done;
    }

    /// <summary>そのチームのお題の個数。</summary>
    public int SetSizeOf(int team)
    {
        int size = 0;
        while (size < MaxSetSize && SetItemAt(team, size) >= 0)
        {
            size++;
        }
        return size;
    }

    /// <summary>そのチームのお題の n 番目の種類（番号）。無ければ -1。</summary>
    public int SetItemAt(int team, int n)
    {
        if (team < 0 || team >= SpaceJunkTeams.MaxTeams || n < 0 || n >= MaxSetSize)
        {
            return -1;
        }

        int i = team * MaxSetSize + n;
        return i < setSequence.Count ? setSequence[i] : -1;
    }

    /// <summary>順番どおりのとき、そのチームが何個目まで入れたか。</summary>
    public int SetStepOf(int team)
    {
        return team >= 0 && team < setStep.Count ? setStep[team] : 0;
    }

    /// <summary>お題で、そのチームがその種類をいくつ入れる必要があるか。</summary>
    public int SetRequiredOf(int team, SpaceJunkMaterialKind kind)
    {
        int i = SetIndex(team, kind);
        return i >= 0 && i < setRequired.Count ? setRequired[i] : 0;
    }

    /// <summary>お題で、そのチームがその種類をもういくつ入れたか。</summary>
    public int SetProgressOf(int team, SpaceJunkMaterialKind kind)
    {
        int i = SetIndex(team, kind);
        return i >= 0 && i < setProgress.Count ? setProgress[i] : 0;
    }

    /// <summary>そのチームの、いまのお題をそろえたときのボーナス。</summary>
    public int SetBonusOf(int team)
    {
        return team >= 0 && team < setBonus.Count ? setBonus[team] : 0;
    }

    /// <summary>そのチームが、このラウンドでお題をそろえた回数。</summary>
    public int SetsCompletedOf(int team)
    {
        return team >= 0 && team < setsCompleted.Count ? setsCompleted[team] : 0;
    }

    private static int SetIndex(int team, SpaceJunkMaterialKind kind)
    {
        if (team < 0 || team >= SpaceJunkTeams.MaxTeams)
        {
            return -1;
        }

        return team * SpaceJunkMaterials.Count + (int)kind;
    }

    // ------------------------------------------------------------
    // 期間限定高価値デブリの状態
    // ------------------------------------------------------------

    /// <summary>高価値に選ばれたデブリの種類（番号）。-1 ならまだ選ばれていない。</summary>
    private readonly NetworkVariable<int> highValueKind = new NetworkVariable<int>(-1);

    /// <summary>高価値の種類を1個入れたときに、1点とは別に入る得点（ホストの設定を全員へ配る）。</summary>
    private readonly NetworkVariable<int> highValueBonus = new NetworkVariable<int>(2);

    /// <summary>高価値に選ばれたデブリの種類。まだ選ばれていなければ false。</summary>
    public bool TryGetHighValueKind(out SpaceJunkMaterialKind kind)
    {
        kind = SpaceJunkMaterials.FromIndex(Mathf.Max(0, highValueKind.Value));
        return highValueKind.Value >= 0;
    }

    /// <summary>高価値の種類を1個入れたときに、1点とは別に入る得点。</summary>
    public int HighValueBonus => highValueBonus.Value;

    // ------------------------------------------------------------
    // 特殊デブリ（重いデブリ）の状態
    // ------------------------------------------------------------

    /// <summary>重いデブリの、通信の番号（NetworkObjectId）の一覧。</summary>
    private readonly NetworkList<ulong> heavyIds = new NetworkList<ulong>();

    /// <summary>重いデブリの重さ。並びは <see cref="heavyIds"/> と同じ。</summary>
    private readonly NetworkList<float> heavyWeights = new NetworkList<float>();

    /// <summary>重いデブリを1個入れたときの得点（ホストの設定を全員へ配る。画面の表示用）。</summary>
    private readonly NetworkVariable<int> heavyPoints = new NetworkVariable<int>(20);

    /// <summary>このPCで、いま重い物にしているデブリ。</summary>
    private readonly HashSet<ulong> appliedHeavyIds = new HashSet<ulong>();

    /// <summary>まだ残っている重いデブリの数。</summary>
    public int HeavyRemaining => heavyIds.Count;

    /// <summary>
    /// 重さが1増えるごとに足す得点（ホストの設定を全員へ配る。画面の表示用）。**重さボーナスが OFF なら 0。**
    /// </summary>
    private readonly NetworkVariable<int> heavyBonusPerWeight = new NetworkVariable<int>(0);

    /// <summary>重いデブリを1個入れたときの得点（重さ1のとき）。</summary>
    public int HeavyPoints => heavyPoints.Value;

    /// <summary>重さが1増えるごとに足す得点。重さボーナスが OFF なら 0。</summary>
    public int HeavyBonusPerWeight => heavyBonusPerWeight.Value;

    // ------------------------------------------------------------
    // ホストだけが動かす
    // ------------------------------------------------------------

    /// <summary>ラウンドの始まりに、イベントの時刻を決める。<see cref="OnNetworkSpawn"/> から呼ぶ。</summary>
    private void ServerInitEvents(float roundSeconds)
    {
        setRequired.Clear();
        setProgress.Clear();
        setBonus.Clear();
        setsCompleted.Clear();
        setSequence.Clear();
        setStep.Clear();
        heavyIds.Clear();
        heavyWeights.Clear();

        for (int i = 0; i < SpaceJunkTeams.MaxTeams * SpaceJunkMaterials.Count; i++)
        {
            setRequired.Add(0);
            setProgress.Add(0);
        }

        for (int i = 0; i < SpaceJunkTeams.MaxTeams * MaxSetSize; i++)
        {
            setSequence.Add(-1);
        }

        for (int team = 0; team < SpaceJunkTeams.MaxTeams; team++)
        {
            setBonus.Add(0);
            setsCompleted.Add(0);
            setStep.Add(0);
        }

        activeEvent.Value = SpaceJunkEventKind.None;
        eventServerTime.Value = 0d;
        eventEndServerTime.Value = 0d;
        eventName.Value = default;
        eventDescription.Value = default;
        serverEventFinished = false;

        SpaceJunkEventSettings settings = EventSettings;
        setOrdered.Value = settings.debrisSetOrdered;
        setGoal.Value = Mathf.Max(1, settings.debrisSetGoal);
        highValueKind.Value = -1;
        highValueBonus.Value = Mathf.Max(0, settings.highValueBonus);
        heavyPoints.Value = Mathf.Max(1, settings.heavyPoints);
        heavyBonusPerWeight.Value = settings.heavyWeightBonus ? Mathf.Max(0, settings.heavyBonusPerWeight) : 0;

        // **得点制のときだけ。** 3種類ルールはその場で決着がつくので、ボーナスの意味が無い
        bool hasCandidate = (settings.pool != null && settings.pool.Count > 0) ||
                            settings.debugForceEvent != SpaceJunkEventKind.None;

        if (rule.Value != SpaceJunkWinRule.Score || !hasCandidate)
        {
            return;
        }

        // ラウンドが終わってから始まっても意味が無い
        if (settings.startSeconds >= roundSeconds)
        {
            Debug.Log($"[JUNK] イベントの開始（{settings.startSeconds} 秒後）がラウンドの時間（{roundSeconds} 秒）より遅いので、" +
                      "このラウンドではイベントは起きません。");
            return;
        }

        // 動き出してから数える（始まりの演出があれば、その終わりから。2026/10/7）
        double playStart = PlayStartServerTime > 0d ? PlayStartServerTime : NetworkManager.ServerTime.Time;
        eventServerTime.Value = playStart + settings.startSeconds;
    }

    /// <summary>時間が来たらイベントを始め、続く秒数が過ぎたら終わらせる。<see cref="Update"/> から呼ぶ。</summary>
    private void ServerUpdateEvents()
    {
        if (activeEvent.Value != SpaceJunkEventKind.None)
        {
            if (!serverEventFinished && !IsEventRunning)
            {
                ServerFinishEvent();
            }
            else if (activeEvent.Value == SpaceJunkEventKind.HeavyDebris)
            {
                ServerPruneLost(heavyIds, heavyWeights);
            }
            return;
        }

        if (eventServerTime.Value <= 0d || NetworkManager.ServerTime.Time < eventServerTime.Value)
        {
            return;
        }

        SpaceJunkEventEntry picked = EventSettings.PickRandom();

        if (picked == null || picked.kind == SpaceJunkEventKind.None)
        {
            // 候補に None が入っていた（または選べる候補が無い）。**「今回は何も起きない」**として、もう選び直さない
            eventServerTime.Value = 0d;
            Debug.Log("[JUNK] イベントの抽選：今回は何も起きません。");
            return;
        }

        ServerStartEvent(picked);
    }

    private void ServerStartEvent(SpaceJunkEventEntry entry)
    {
        SpaceJunkEventSettings settings = EventSettings;

        switch (entry.kind)
        {
            case SpaceJunkEventKind.DebrisSet:
                // **お題の個数は、ここで1回だけ決めて全チームそろえる**（中身はチームごとにばらばら）
                int min = Mathf.Max(1, settings.debrisSetMinSize);
                int max = Mathf.Max(min, settings.debrisSetMaxSize);
                serverSetSize = Mathf.Min(Random.Range(min, max + 1), MaxSetSize);

                for (int team = 0; team < SpaceJunkTeams.MaxTeams; team++)
                {
                    ServerDrawDebrisSet(team);
                }
                break;

            case SpaceJunkEventKind.HighValueDebris:
                highValueKind.Value = SpaceJunkMaterials.RandomUsedIndex();
                Debug.Log($"[JUNK] 高価値の種類：{SpaceJunkMaterials.Name(SpaceJunkMaterials.FromIndex(highValueKind.Value))}" +
                          $"（+{highValueBonus.Value} 点）");
                break;

            case SpaceJunkEventKind.HeavyDebris:
                ServerSpawnHeavyDebris(settings);
                break;
        }

        FixedString64Bytes nameText = default;
        nameText.CopyFromTruncated(entry.NameOrDefault);
        FixedString512Bytes descriptionText = default;
        descriptionText.CopyFromTruncated(entry.DescriptionOrDefault(settings));

        // 始まった時刻は「予定の時刻」ではなく「実際に始まった時刻」にそろえる（始まった合図の表示に使う）
        double now = NetworkManager.ServerTime.Time;
        eventServerTime.Value = now;
        eventEndServerTime.Value = entry.durationSeconds > 0f ? now + entry.durationSeconds : 0d;
        eventName.Value = nameText;
        eventDescription.Value = descriptionText;
        activeEvent.Value = entry.kind;

        Debug.Log($"[JUNK] イベント開始：{entry.NameOrDefault}" +
                  (entry.durationSeconds > 0f ? $"（{entry.durationSeconds} 秒間）" : "（ラウンドの終わりまで）"));
    }

    /// <summary>続く秒数が過ぎたときの片付け。</summary>
    private void ServerFinishEvent()
    {
        serverEventFinished = true;

        if (activeEvent.Value == SpaceJunkEventKind.HeavyDebris)
        {
            // 時間の決まった特殊デブリなら、残りはふつうのデブリに戻す（消さない）
            heavyIds.Clear();
            heavyWeights.Clear();
        }

        Debug.Log($"[JUNK] イベント終了：{EventName}");
    }

    /// <summary>
    /// **デブリがゴールに入ったときの、イベントのぶんの得点。** ふつうの1点とは別に足される。
    /// <see cref="ServerAddScore"/> から呼ぶ。
    /// </summary>
    private int ServerEventBonusOnCollect(int team, SpaceJunkMaterialKind kind, NetworkObject item)
    {
        if (!IsEventRunning)
        {
            return 0;
        }

        switch (activeEvent.Value)
        {
            case SpaceJunkEventKind.DebrisSet:
                return ServerDebrisSetCollect(team, kind);
            case SpaceJunkEventKind.HighValueDebris:
                return ServerHighValueCollect(team, kind);
            case SpaceJunkEventKind.HeavyDebris:
                return ServerHeavyCollect(team, item);
            default:
                return 0;
        }
    }

    // ------------------------------------------------------------
    // デブリセット
    // ------------------------------------------------------------

    /// <summary>
    /// そのチームのお題を、新しく引き直す（進み具合は0に戻る）。
    /// **個数はイベントの始まりに決めた数（全チーム同じ）**、中身はチームごとにばらばら。
    /// </summary>
    private void ServerDrawDebrisSet(int team)
    {
        SpaceJunkEventSettings settings = EventSettings;
        int size = Mathf.Clamp(serverSetSize, 1, MaxSetSize);

        for (int k = 0; k < SpaceJunkMaterials.Count; k++)
        {
            int i = SetIndex(team, SpaceJunkMaterials.FromIndex(k));
            setRequired[i] = 0;
            setProgress[i] = 0;
        }

        for (int n = 0; n < MaxSetSize; n++)
        {
            int kindIndex = n < size ? SpaceJunkMaterials.RandomUsedIndex() : -1;
            setSequence[team * MaxSetSize + n] = kindIndex;

            if (kindIndex >= 0)
            {
                int i = SetIndex(team, SpaceJunkMaterials.FromIndex(kindIndex));
                setRequired[i] = setRequired[i] + 1;
            }
        }

        setStep[team] = 0;
        setBonus[team] = Mathf.Max(0, settings.debrisSetBonus);
    }

    /// <summary>
    /// お題のデブリなら進める。**お題のうち <see cref="SetGoalOf"/> 個（初期値3個）入れたら**ボーナスを返して、お題を引き直す。
    /// 順番どおりのときは、**お題の次の1個と同じ種類のときだけ**進める。
    /// </summary>
    private int ServerDebrisSetCollect(int team, SpaceJunkMaterialKind kind)
    {
        int i = SetIndex(team, kind);

        if (i < 0)
        {
            return 0;
        }

        if (setOrdered.Value)
        {
            int step = setStep[team];

            if (SetItemAt(team, step) != (int)kind)
            {
                // 次の1個ではない。**進まないし、戻りもしない**
                return 0;
            }

            setStep[team] = step + 1;
            setProgress[i] = setProgress[i] + 1;
        }
        else
        {
            if (setProgress[i] >= setRequired[i])
            {
                // お題に無い、またはもう足りているデブリ
                return 0;
            }

            setProgress[i] = setProgress[i] + 1;
        }

        if (SetDoneOf(team) < SetGoalOf(team))
        {
            return 0;
        }

        // そろった
        int bonus = setBonus[team];
        setsCompleted[team] = setsCompleted[team] + 1;

        Debug.Log($"[JUNK] {SpaceJunkTeams.TeamName(team)} がデブリセットをそろえました（+{bonus} 点）。新しいお題を引きます。");

        ServerDrawDebrisSet(team);
        return bonus;
    }

    // ------------------------------------------------------------
    // 期間限定高価値デブリ
    // ------------------------------------------------------------

    /// <summary>
    /// **高価値に選ばれた種類なら、1点とは別に +2点**（初期値）を返す。
    /// </summary>
    private int ServerHighValueCollect(int team, SpaceJunkMaterialKind kind)
    {
        if (highValueKind.Value != (int)kind)
        {
            return 0;
        }

        Debug.Log($"[JUNK] {SpaceJunkTeams.TeamName(team)} が高価値の {SpaceJunkMaterials.Name(kind)} を入れました（+{highValueBonus.Value} 点）。");
        return highValueBonus.Value;
    }

    /// <summary>
    /// **マップの外へ落ちて消えた重いデブリを、一覧から外す**。
    /// 外さないと「あと ○個」が減らないまま残る。<paramref name="parallel"/> は同じ並びの一覧（重さ）。無ければ null。
    /// </summary>
    private void ServerPruneLost(NetworkList<ulong> ids, NetworkList<float> parallel)
    {
        if (ids.Count == 0 || NetworkManager == null || NetworkManager.SpawnManager == null)
        {
            return;
        }

        for (int i = ids.Count - 1; i >= 0; i--)
        {
            bool alive = NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(ids[i], out NetworkObject networkObject)
                         && networkObject != null
                         && networkObject.TryGetComponent(out HookableObject item)
                         && !item.IsVanished;

            if (!alive)
            {
                ids.RemoveAt(i);
                if (parallel != null && i < parallel.Count)
                {
                    parallel.RemoveAt(i);
                }
            }
        }
    }

    // ------------------------------------------------------------
    // 特殊デブリ（重いデブリ）
    // ------------------------------------------------------------

    /// <summary>
    /// 重いデブリを出す。**設定の数だけ**、スポナーに新しく出させる。
    /// 重さは1個ずつ、設定の X〜Y の間からランダムに決める（<see cref="SpaceJunkEventSettings.PickHeavyWeight"/>）。
    /// 大きさ・重さは、一覧を見て各PCが付ける（<see cref="UpdateHeavyMarks"/>）。
    /// </summary>
    private void ServerSpawnHeavyDebris(SpaceJunkEventSettings settings)
    {
        SpaceJunkSpawner spawner = FindFirstObjectByType<SpaceJunkSpawner>();

        if (spawner == null)
        {
            Debug.LogWarning("[JUNK] 重いデブリを出せませんでした（スポナーが無い）。");
            return;
        }

        for (int n = 0; n < Mathf.Max(1, settings.heavyInitialCount); n++)
        {
            float weight = settings.PickHeavyWeight();

            GameObject spawned = spawner.ServerSpawnExtra();
            NetworkObject networkObject = spawned != null ? spawned.GetComponent<NetworkObject>() : null;

            if (networkObject == null || !networkObject.IsSpawned)
            {
                Debug.LogWarning($"[JUNK] 重さ {weight} の重いデブリを置く場所が見つからず、出せませんでした。");
                continue;
            }

            heavyIds.Add(networkObject.NetworkObjectId);
            heavyWeights.Add(Mathf.Max(0.1f, weight));
        }
    }

    /// <summary>重いデブリなら、1点との差のぶんを返す（合計が特殊デブリの得点になる）。</summary>
    private int ServerHeavyCollect(int team, NetworkObject item)
    {
        if (item == null)
        {
            return 0;
        }

        int index = heavyIds.IndexOf(item.NetworkObjectId);
        if (index < 0)
        {
            return 0;
        }

        float weight = index < heavyWeights.Count ? heavyWeights[index] : 1f;
        heavyIds.RemoveAt(index);
        if (index < heavyWeights.Count)
        {
            heavyWeights.RemoveAt(index);
        }

        // 重さボーナスが ON なら、重いほど高い（設定は HeavyPointsFor の1か所）
        int points = EventSettings.HeavyPointsFor(weight);

        Debug.Log($"[JUNK] {SpaceJunkTeams.TeamName(team)} が重さ {weight:0.#} の特殊デブリを入れました（{points} 点）。" +
                  $"残り {heavyIds.Count} 個。");

        return Mathf.Max(0, points - PointPerItem);
    }

    // ------------------------------------------------------------
    // 見た目（**全員のPCで動かす**）
    // ------------------------------------------------------------

    /// <summary>
    /// ホストが配っている「重いデブリの一覧」に合わせて、このPCで重い物にしたり戻したりする
    /// （大きさ・質量・色。投げられなくなるのは <see cref="HeavyHookable"/> を見て操作の側が判断する）。
    /// </summary>
    private void UpdateHeavyMarks()
    {
        if (!IsSpawned || NetworkManager == null || NetworkManager.SpawnManager == null)
        {
            return;
        }

        var spawned = NetworkManager.SpawnManager.SpawnedObjects;
        SpaceJunkEventSettings settings = EventSettings;

        for (int i = 0; i < heavyIds.Count; i++)
        {
            ulong id = heavyIds[i];

            if (appliedHeavyIds.Contains(id) || !spawned.TryGetValue(id, out NetworkObject networkObject) || networkObject == null)
            {
                continue;
            }

            float weight = i < heavyWeights.Count ? heavyWeights[i] : 1f;

            if (!networkObject.TryGetComponent(out HeavyHookable heavy))
            {
                heavy = networkObject.gameObject.AddComponent<HeavyHookable>();
            }
            heavy.Apply(weight, settings.HeavyScale(weight));

            if (!networkObject.TryGetComponent(out SpaceJunkHighValueMark mark))
            {
                mark = networkObject.gameObject.AddComponent<SpaceJunkHighValueMark>();
            }
            mark.Show(settings.heavyColor);

            appliedHeavyIds.Add(id);
        }

        if (appliedHeavyIds.Count == 0)
        {
            return;
        }

        List<ulong> removed = null;

        foreach (ulong id in appliedHeavyIds)
        {
            if (heavyIds.Contains(id))
            {
                continue;
            }

            if (spawned.TryGetValue(id, out NetworkObject networkObject) && networkObject != null)
            {
                if (networkObject.TryGetComponent(out HeavyHookable heavy))
                {
                    heavy.Remove();
                }

                if (networkObject.TryGetComponent(out SpaceJunkHighValueMark mark))
                {
                    mark.Hide();
                }
            }

            (removed ??= new List<ulong>()).Add(id);
        }

        if (removed != null)
        {
            foreach (ulong id in removed)
            {
                appliedHeavyIds.Remove(id);
            }
        }
    }
}
