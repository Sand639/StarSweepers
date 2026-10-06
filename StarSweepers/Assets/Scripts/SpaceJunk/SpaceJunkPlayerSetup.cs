using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

/// <summary>
/// **宇宙ごみ用に、プレイヤーへ上乗せする部品。**
///
/// フックの操作そのものは釣りの部品（<c>FishingNetPlayer</c> / <c>HookController</c> など）を
/// **そのまま共用している。** ただし、そのままでは次の2つが合わない。
///
/// ### ① マップに着いても、位置とカメラが合わない
///
/// <c>FishingNetPlayer</c> は「釣り会場に着いたか」を **<c>FishingMatch</c> がいるかどうか**で判断している。
/// 宇宙ごみのマップには <c>FishingMatch</c> を置かない（釣りの時間制ルールが動いてしまうため）ので、
/// **着いたことに気づかず、ロビーの位置のまま・カメラも追ってこない。**
/// → ここで <see cref="SpaceJunkRound"/> が現れたかどうかで同じことをやり直す。
///
/// ### ② チームの色が食い違う
///
/// <c>FishingNetPlayer</c> は**参加した順に2人ずつ**でチームの色を塗る。
/// 宇宙ごみは**ホストが自由にチームを決める**ので、そのままでは色が合わない。
/// → ここで <see cref="SpaceJunkSession"/> が持っているチームの色に塗り直す。
///
/// ### 出てくる場所
///
/// **自分のチームのゴールの近く**に出る。見つからなければ、接続番号ごとに円周上へ散らす。
///
/// 使い方：宇宙ごみ用のプレイヤーのプレハブに、釣りの部品と一緒に付ける。
/// **釣りのプレハブには付けないこと**（釣りの動きが変わってしまう）。
/// </summary>
public class SpaceJunkPlayerSetup : MonoBehaviour
{
    [Header("見た目")]
    [Tooltip("チームの色に塗る見た目。**釣りのプレハブと同じものを入れる**")]
    [SerializeField] private Renderer[] teamRenderers;

    [Header("出てくる場所")]
    [Tooltip("自分のゴールから、ステージの中心へ向かって何m離れた場所に出るか")]
    [SerializeField] private float spawnInset = 4f;

    [Tooltip("重ならないように、どれだけばらけさせるか（メートル）")]
    [SerializeField] private float spawnScatter = 1.5f;

    [Tooltip("ゴールが見つからないときに使う、中心からの距離")]
    [SerializeField] private float fallbackRadius = 6f;

    [Header("落ちたときの立て直し")]
    [Tooltip("押すと、その場から**出てくる場所へ戻る**キー。落ちて戻れなくなったとき用")]
    [SerializeField] private Key resetKey = Key.R;

    [Tooltip("同じことをする、コントローラーのボタン（North＝Xbox の Y）")]
    [SerializeField] private GamepadButton resetButton = GamepadButton.North;

    [Tooltip("この高さより下まで落ちたら、**自動で出てくる場所へ戻す**（メートル）")]
    [SerializeField] private float fallResetHeight = -8f;

    [Tooltip("**ラウンド中に**戻ったとき（キー・落下）、動けない秒数（復活時間）。0 で止めない。ロビーでは止めない")]
    [Min(0f)]
    [SerializeField] private float respawnLockSeconds = 3f;

    [Header("レーダー（自分のぶんにだけ付く）")]
    [Tooltip("レーダーの外枠の画像（Assets/Art/Sprites/Radar）")]
    [SerializeField] private Sprite radarFrame;

    [Tooltip("レーダーの方角（N/E/S/W）の輪の画像。自分の向きに合わせて回る")]
    [SerializeField] private Sprite radarCompass;

    [Tooltip("レーダーの真ん中に出す自分の印の画像")]
    [SerializeField] private Sprite radarSelf;

    /// <summary>
    /// このPCで操作している人の「戻る」キーの名前。画面の案内に使う。
    /// 自分のぶんが動き出すまでは空。
    /// </summary>
    public static string LocalResetKeyName { get; private set; } = string.Empty;

    /// <summary>
    /// このPCで操作している人の、**復活までの残り秒数**。0 なら動ける。画面の案内に使う。
    /// </summary>
    public static float LocalRespawnRemaining { get; private set; }

    private FishingNetPlayer netPlayer;
    private CharacterController characterController;
    private FishingPlayerController mover;
    private HookController hookController;

    /// <summary>復活までの残り秒数（自分のぶんだけ使う）。</summary>
    private float respawnRemaining;

    /// <summary>マップに着いて、場所とカメラを合わせ終わったか。</summary>
    private bool placedInRound;

    /// <summary>名前のタグのプレハブの名前（Assets/Resources の中）。</summary>
    private const string NameplatePrefabName = "PlayerNameplate";

    /// <summary>頭の上の名前のタグ。</summary>
    private SpaceJunkNameplate nameplate;

    /// <summary>直前に塗ったチーム。変わったときだけ塗り直すために持つ。</summary>
    private int lastAppliedTeam = -999;

    private void Awake()
    {
        netPlayer = GetComponent<FishingNetPlayer>();
        characterController = GetComponent<CharacterController>();
        mover = GetComponent<FishingPlayerController>();
        hookController = GetComponent<HookController>();
    }

    private void Update()
    {
        // チームの色は、**自分のぶんも他の人のぶんも**塗る（誰が味方か分かるように）
        ApplyTeamColor();

        // 頭の上の名前のタグも、**全員のぶん**に付ける
        EnsureNameplate();

        if (netPlayer == null || !netPlayer.IsOwner)
        {
            return;
        }

        LocalResetKeyName = $"{resetKey}／{GamepadInput.Label(resetButton)}";

        EnsureRadar();
        EnsureLocalStateCheck();

        TickPlacement();
        TickRespawnLock();

        bool inRound = SpaceJunkRound.Current != null;

        if (inRound && !placedInRound)
        {
            placedInRound = true;

            // 新しいラウンドの始まり。前のラウンドの復活待ちが残っていても、ここで解く
            respawnRemaining = 0f;
            LocalRespawnRemaining = 0f;

            // 置き直しは、**自分のチームとゴールが届くまで待ってから**（TickPlacement）
            BeginPlacement();
        }
        else if (!inRound && placedInRound)
        {
            // ロビーへ戻った。**ここでは置き直さない。**
            // この瞬間はまだシーンの入れ替わりの途中で、前のマップの物が残っている。
            // 置き直すのは、ロビーのシーンが読み込み終わってから（OnSceneLoaded → TickPlacement）
            placedInRound = false;
        }

        // 置き直しを待っている間は、落下の判定もしない（止めて待っているため）
        if (!placementPending)
        {
            CheckReset();
        }
    }

    // ------------------------------------------------------------
    // シーンが切り替わったときの置き直し（2026/10/6 に作り直した）
    // ------------------------------------------------------------
    //
    // 前は「シーンを読み込んでから2フレーム待って置き直す」決め打ちだった。
    // 通信が遅いと、2フレームの時点ではまだラウンドの係や自分のチーム・ゴールの持ち主が届いておらず、
    // **ゴールが見つからないとき用の場所（中心のまわり）に置かれ、床の無いマップでは落ちていた。**
    //
    // 今は、**届くまでその場で止めて待つ**（最大 MaxPlacementWaitSeconds 秒）。
    // 届かなければ**床がある所**を探して置き、あとからゴールが届いたら置き直す。

    /// <summary>置き直しを待っているか（待っている間は、動けず落ちない）。</summary>
    private bool placementPending;

    /// <summary>待ち始めてからの秒数とフレーム数。</summary>
    private float placementWaitTime;
    private int placementFramesWaited;

    /// <summary>ゴールが見つからず、とりあえず床のある所に置いたか（あとでゴールが届いたら置き直す）。</summary>
    private bool placedByFallback;
    private float fallbackRetryTimer;

    /// <summary>最低でも待つフレーム数（読み込んだ直後は、前のシーンの物が残っているため）。</summary>
    private const int MinPlacementFrames = 2;

    /// <summary>マップで、自分のゴールが届くのを待つ最大の秒数。</summary>
    private const float MaxPlacementWaitSeconds = 4f;

    /// <summary>とりあえず置いたあと、ゴールが届いたら置き直す期間（秒）。</summary>
    private const float FallbackRetrySeconds = 6f;

    /// <summary>置き直しを待っているか（待っている間は動けない）。</summary>
    public bool IsWaitingForPlacement => placementPending;

    /// <summary>
    /// **いま、このプレイヤーは動けるべきか。** 答え合わせ（<see cref="SpaceJunkLocalStateCheck"/>）が、
    /// 実際の状態と比べるのに使う。
    /// </summary>
    public bool ControlShouldBeEnabled
    {
        get
        {
            if (placementPending || respawnRemaining > 0f)
            {
                return false;
            }

            if (SpaceJunkRound.Current != null)
            {
                return SpaceJunkRound.Current.IsPlaying; // 結果発表中は止まる
            }

            // ロビー：ホストが設定端末を開いている間は止まる
            SpaceJunkLobbyTerminal terminal = FindFirstObjectByType<SpaceJunkLobbyTerminal>();
            return terminal == null || !terminal.IsOpen;
        }
    }

    private void OnEnable()
    {
        UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private void OnDisable()
    {
        UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    /// <summary>
    /// **シーンが読み込まれたら、置き直しを待つ。**
    ///
    /// プレイヤーはシーンをまたいで生き続けるので、**移った直後は
    /// 「前のシーンで立っていた場所」のまま**になる。そのままだと新しいシーンの地面の外で落ちる
    /// （2026/9/20・大槻さんの報告）。
    /// </summary>
    private void OnSceneLoaded(UnityEngine.SceneManagement.Scene scene,
                               UnityEngine.SceneManagement.LoadSceneMode mode)
    {
        if (netPlayer == null || !netPlayer.IsOwner)
        {
            return;
        }

        BeginPlacement();
    }

    /// <summary>置き直しを待ち始める。待っている間は動けない（落ちない）。</summary>
    private void BeginPlacement()
    {
        placementPending = true;
        placementWaitTime = 0f;
        placementFramesWaited = 0;
        SetControlEnabled(false);
    }

    /// <summary>
    /// **置き直しの準備ができたら置く。**
    /// ・ロビー … 少し待ってから、ロビー用の場所へ
    /// ・マップ … ラウンドの係と、**自分のチームのゴール**が届くまで待ってから、ゴールの近くへ。
    ///   待ちきれなければ、床のある所へ置き、あとでゴールが届いたら置き直す
    /// </summary>
    private void TickPlacement()
    {
        if (placementPending)
        {
            placementFramesWaited++;
            placementWaitTime += Time.unscaledDeltaTime;

            // ラウンドの係が操作を戻しても、置き終わるまでは止めておく（落ちないように）
            SetControlEnabled(false);

            if (placementFramesWaited < MinPlacementFrames)
            {
                return;
            }

            if (!IsInLobbyScene())
            {
                bool ready = SpaceJunkRound.Current != null &&
                             SpaceJunkRound.Current.gameObject.scene ==
                             UnityEngine.SceneManagement.SceneManager.GetActiveScene() &&
                             TryFindOwnGoal(out _);

                if (!ready && placementWaitTime < MaxPlacementWaitSeconds)
                {
                    return;
                }

                if (!ready)
                {
                    Debug.LogWarning($"[NET][答え合わせ] 自分のゴールが {MaxPlacementWaitSeconds:0} 秒待っても届かないので、" +
                                     "とりあえず床のある所に置きます（届いたら置き直します）。");
                }
            }

            placementPending = false;
            MoveToSpawnPoint();
            FollowWithCamera();
            SetControlEnabled(ControlShouldBeEnabled);
            return;
        }

        // とりあえず置いたあとに、自分のゴールが届いたら置き直す
        if (placedByFallback && SpaceJunkRound.Current != null)
        {
            fallbackRetryTimer += Time.unscaledDeltaTime;
            if (fallbackRetryTimer > FallbackRetrySeconds)
            {
                placedByFallback = false;
                return;
            }

            if (TryFindOwnGoal(out _))
            {
                Debug.LogWarning("[NET][答え合わせ] 自分のゴールが遅れて届いたので、ゴールの近くへ置き直しました。");
                MoveToSpawnPoint();
                FollowWithCamera();
            }
        }
    }

    /// <summary>いまのシーンがロビーか。</summary>
    private static bool IsInLobbyScene()
    {
        string lobby = SpaceJunkSession.Current != null ? SpaceJunkSession.Current.LobbySceneName : "SpaceJunkLobby";
        return UnityEngine.SceneManagement.SceneManager.GetActiveScene().name == lobby;
    }

    /// <summary>
    /// **このシーンにある、自分のチームのゴール**を探す。自分のチームがまだ届いていなければ false。
    /// （シーンの入れ替わりの途中に残っている「前のマップのゴール」はつかまない）
    /// </summary>
    private bool TryFindOwnGoal(out SpaceJunkGoal found)
    {
        found = null;

        if (SpaceJunkSession.Current == null || netPlayer == null)
        {
            return false;
        }

        int team = MyTeam();
        UnityEngine.SceneManagement.Scene active = UnityEngine.SceneManagement.SceneManager.GetActiveScene();

        foreach (SpaceJunkGoal goal in SpaceJunkGoal.All)
        {
            if (goal != null && goal.gameObject.scene == active && goal.OwnerTeam == team)
            {
                found = goal;
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 頭の上の名前のタグ（<see cref="SpaceJunkNameplate"/>）を付ける。
    /// 見た目は `Assets/Resources/PlayerNameplate.prefab` から作る（無ければ初期値で作る）。
    /// プレイヤーの子にするので、シーンが変わっても一緒に残る。
    /// </summary>
    private void EnsureNameplate()
    {
        if (nameplate != null || netPlayer == null)
        {
            return;
        }

        SpaceJunkNameplate prefab = Resources.Load<SpaceJunkNameplate>(NameplatePrefabName);

        if (prefab != null)
        {
            nameplate = Instantiate(prefab, transform);
        }
        else
        {
            GameObject plate = new GameObject(NameplatePrefabName);
            plate.transform.SetParent(transform, false);
            nameplate = plate.AddComponent<SpaceJunkNameplate>();
        }

        nameplate.Attach(netPlayer);
    }

    /// <summary>自分のぶんにだけ、答え合わせの部品を付ける。</summary>
    private void EnsureLocalStateCheck()
    {
        if (GetComponent<SpaceJunkLocalStateCheck>() == null)
        {
            gameObject.AddComponent<SpaceJunkLocalStateCheck>();
        }
    }

    /// <summary>
    /// **落ちて戻れなくなったときの立て直し。**
    ///
    /// ・自分で押して戻る（<see cref="resetKey"/>）
    /// ・下まで落ちたら自動で戻す（<see cref="fallResetHeight"/>）
    ///
    /// 穴に落ちても待っていれば戻るが、**引っかかって落ちきらないこともある**ので、
    /// 自分で戻せるキーも用意してある。
    /// </summary>
    private void CheckReset()
    {
        // 下まで落ちたら、押されなくても戻す
        if (transform.position.y < fallResetHeight)
        {
            MoveToSpawnPoint();
            FollowWithCamera();
            StartRespawnLock();
            return;
        }

        // ポーズ中（と閉じたフレーム）と、ロビーの設定画面を開いている間は入力を読まない
        if (GamePause.BlocksInput || SpaceJunkLobbyScreen.IsSettingsOpen)
        {
            return;
        }

        Keyboard keyboard = Keyboard.current;

        // 復活待ちの間は、もう一度押しても何もしない（押し続けて待ち時間を延ばせないように）
        if (respawnRemaining > 0f)
        {
            return;
        }

        if ((keyboard != null && keyboard[resetKey].wasPressedThisFrame) || GamepadInput.WasPressed(resetButton))
        {
            MoveToSpawnPoint();
            FollowWithCamera();
            StartRespawnLock();
        }
    }

    // ------------------------------------------------------------
    // 復活時間（ラウンド中に戻ったとき、しばらく動けない）
    // ------------------------------------------------------------

    /// <summary>
    /// **ラウンド中なら、復活時間のあいだ動けなくする。**（2026/9/22・大槻さん）
    /// R キーで戻るのを「逃げ」に使えないようにするため。ロビーでは止めない。
    /// </summary>
    private void StartRespawnLock()
    {
        if (SpaceJunkRound.Current == null || respawnLockSeconds <= 0f)
        {
            return;
        }

        respawnRemaining = respawnLockSeconds;
        LocalRespawnRemaining = respawnRemaining;
        SetControlEnabled(false);
    }

    private void TickRespawnLock()
    {
        if (respawnRemaining <= 0f)
        {
            return;
        }

        respawnRemaining -= Time.deltaTime;
        LocalRespawnRemaining = Mathf.Max(0f, respawnRemaining);

        if (respawnRemaining <= 0f)
        {
            respawnRemaining = 0f;

            // **ラウンドが終わって結果を出している最中なら、戻さない**（ラウンドの係が止めているため）
            SetControlEnabled(SpaceJunkRound.PlayAllowed);
        }
    }

    /// <summary>移動とフックを止める／戻す。</summary>
    private void SetControlEnabled(bool enabledState)
    {
        if (mover != null)
        {
            mover.enabled = enabledState;
        }

        if (hookController != null)
        {
            hookController.enabled = enabledState;
        }
    }

    /// <summary>
    /// **自分のぶんにだけ、レーダーを付ける。**（2026/9/22）
    /// 他の人のぶんに付けると、その人を中心にしたレーダーがもう1枚出てしまう。
    /// レーダーはマップにいる間だけ出る（<see cref="SpaceJunkRadar"/>）。
    /// </summary>
    private void EnsureRadar()
    {
        if (GetComponent<SpaceJunkRadar>() != null)
        {
            return;
        }

        SpaceJunkRadar radar = gameObject.AddComponent<SpaceJunkRadar>();
        radar.Setup(radarFrame, radarCompass, radarSelf);
    }

    /// <summary>このプレイヤーのチーム。係がいなければ 0。</summary>
    private int MyTeam()
    {
        if (SpaceJunkSession.Current == null)
        {
            return 0;
        }

        return SpaceJunkSession.Current.TeamOf(netPlayer != null ? netPlayer.OwnerClientId : 0UL);
    }

    /// <summary>ホストが決めたチームの色に塗り直す。</summary>
    private void ApplyTeamColor()
    {
        if (teamRenderers == null || SpaceJunkSession.Current == null)
        {
            return;
        }

        int team = MyTeam();

        if (team == lastAppliedTeam)
        {
            return;
        }

        lastAppliedTeam = team;
        Color color = SpaceJunkTeams.TeamColor(team);

        foreach (Renderer renderer in teamRenderers)
        {
            if (renderer != null)
            {
                renderer.material.color = color;
            }
        }
    }

    /// <summary>
    /// **自分のチームのゴールの近く**へ移す。見つからなければ円周上へ散らす。
    ///
    /// **移す前に、フックを手元に戻して、つかんでいる物資も離す**（2026/9/22）。
    /// R キー・落下・ラウンドの始まり・ロビーへ戻ったとき、のどれでもここを通るので、
    /// 物資をつかんだまま戻ったり、伸ばしたフックが次のラウンドに残ったりしない。
    /// </summary>
    private void MoveToSpawnPoint()
    {
        if (hookController != null)
        {
            hookController.ResetHook();
        }

        Vector3 position = FindSpawnPosition();
        Quaternion rotation = Quaternion.LookRotation(
            new Vector3(-position.x, 0f, -position.z).sqrMagnitude > 0.01f
                ? new Vector3(-position.x, 0f, -position.z)
                : Vector3.forward);

        // CharacterController が付いたまま動かすと位置が戻されるので、いったん切る
        if (characterController != null)
        {
            characterController.enabled = false;
            transform.SetPositionAndRotation(position, rotation);
            characterController.enabled = true;
        }
        else
        {
            transform.SetPositionAndRotation(position, rotation);
        }
    }

    private Vector3 FindSpawnPosition()
    {
        Vector2 scatter = Random.insideUnitCircle * spawnScatter;

        // **ゴールを当てにするのは、ラウンドが動いているときだけ。**
        //
        // ロビーにはゴールが無い。それなのに `SpaceJunkGoal.All` を見に行くと、
        // **シーンの入れ替わりの途中でまだ消えていない「前のマップのゴール」**を
        // つかんでしまい、ロビーの地面のはるか外（中心から約21m）に置かれる。
        // ロビーの地面は20m四方（中心から10m）なので、そのまま落ちる
        // （2026/9/20・大槻さんの報告）
        if (SpaceJunkRound.Current == null)
        {
            placedByFallback = false;
            return SafeFallbackPosition(scatter);
        }

        // **このシーンにある、自分のチームのゴール**（前のマップのゴールはつかまない。2026/10/6）
        if (TryFindOwnGoal(out SpaceJunkGoal goal))
        {
            placedByFallback = false;

            // ゴールから、ステージの中心へ向かって少し内側
            Vector3 goalPosition = goal.transform.position;
            Vector3 toCenter = new Vector3(-goalPosition.x, 0f, -goalPosition.z);

            if (toCenter.sqrMagnitude < 0.01f)
            {
                toCenter = Vector3.forward;
            }

            Vector3 spot = goalPosition + toCenter.normalized * spawnInset;
            return new Vector3(spot.x + scatter.x, goalPosition.y, spot.z + scatter.y);
        }

        // ゴールが見つからないとき（まだ持ち主が配られていないなど）。**あとで届いたら置き直す**
        placedByFallback = true;
        fallbackRetryTimer = 0f;
        return SafeFallbackPosition(scatter);
    }

    /// <summary>
    /// **ゴールを当てにしないときの、床がある出てくる場所。**（2026/10/6）
    /// まず中心のまわりの決まった場所（<see cref="FallbackSpawnPosition"/>）を見て、
    /// そこに床が無ければ（ドーナツの穴・床の無い帯など）、まわりを順に探して床のある所を使う。
    /// </summary>
    private Vector3 SafeFallbackPosition(Vector2 scatter)
    {
        Vector3 first = FallbackSpawnPosition(scatter);
        if (TryGroundAt(first, out Vector3 grounded))
        {
            return grounded;
        }

        float[] radii = { fallbackRadius, 3f, 0f, 9f, 12f, 15f };
        for (int r = 0; r < radii.Length; r++)
        {
            for (int a = 0; a < 8; a++)
            {
                Vector3 candidate = Quaternion.Euler(0f, a * 45f, 0f) * (Vector3.forward * radii[r]);
                if (TryGroundAt(candidate, out grounded))
                {
                    return grounded;
                }
            }
        }

        return first;
    }

    /// <summary>その場所の真下に床があれば、床の上の位置を返す。</summary>
    private bool TryGroundAt(Vector3 spot, out Vector3 grounded)
    {
        grounded = spot;
        Vector3 from = new Vector3(spot.x, spot.y + 6f, spot.z);

        foreach (RaycastHit hit in Physics.RaycastAll(from, Vector3.down, 20f, ~0, QueryTriggerInteraction.Ignore))
        {
            // 自分の体や、物資・プレイヤーは床とみなさない
            if (hit.collider.transform.IsChildOf(transform) ||
                hit.collider.GetComponentInParent<HookableObject>() != null ||
                hit.collider.GetComponentInParent<CharacterController>() != null)
            {
                continue;
            }

            grounded = new Vector3(spot.x, hit.point.y, spot.z);
            return true;
        }

        return false;
    }

    /// <summary>
    /// **ゴールを当てにしないときの出てくる場所。** ロビーでもここを使う。
    ///
    /// 中心のまわりに、接続番号ごとに角度をずらして並べる。
    /// 全員が同じ場所に重なって出てこないようにするため。
    /// </summary>
    private Vector3 FallbackSpawnPosition(Vector2 scatter)
    {
        ulong id = netPlayer != null ? netPlayer.OwnerClientId : 0UL;
        float angle = (id % (ulong)SpaceJunkTeams.MaxPlayers) * (360f / SpaceJunkTeams.MaxPlayers);
        Vector3 spot = Quaternion.Euler(0f, angle, 0f) * (Vector3.forward * fallbackRadius);

        return new Vector3(spot.x + scatter.x, 0f, spot.z + scatter.y);
    }

    /// <summary>
    /// **シーンに置いてあるもの（カメラ・ゲージ）と自分を結びつける。**
    /// プレイヤーはプレハブから生まれるので、シーンの中身をあらかじめ入れておけない。
    /// </summary>
    private void FollowWithCamera()
    {
        TopDownCameraFollow follow = FindFirstObjectByType<TopDownCameraFollow>();
        if (follow != null)
        {
            follow.SetTarget(transform);
        }

        // 自陣が手前に来るように回るカメラ（SpaceJunkMap01TeamCam など）
        SpaceJunkTeamFollowCamera teamCamera = FindFirstObjectByType<SpaceJunkTeamFollowCamera>();
        if (teamCamera != null)
        {
            teamCamera.SetTarget(transform);
        }

        PlayerAimController aim = GetComponent<PlayerAimController>();
        if (aim != null && Camera.main != null)
        {
            aim.SetCamera(Camera.main);
        }

        HookController hook = GetComponent<HookController>();
        if (hook != null)
        {
            HookChargeUI ui = FindFirstObjectByType<HookChargeUI>();
            if (ui != null)
            {
                hook.SetUI(ui);
            }
        }
    }
}
