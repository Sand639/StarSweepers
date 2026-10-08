using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// **オンラインの釣りプレイヤー1人分。**
///
/// 役割は3つ。
///   ① **参加番号とチームを決める**（ホストが決めて全員に配る）
///   ② **自分の1人だけが操作できるようにする**（他の人のぶんは操作スクリプトを止める）
///   ③ **フックと糸を他の人にも見せる**（自分のフックの位置を送り、他の人はそれを再現する）
///
/// 移動そのものは既存の <see cref="FishingPlayerController"/> がそのまま動かし、
/// 位置は `NetworkTransform` が配る（`動きは本人` の形）。
/// このスクリプトは通信のための上乗せだけを持つ。
/// </summary>
[RequireComponent(typeof(NetworkObject))]
public class FishingNetPlayer : NetworkBehaviour
{
    /// <summary>いまシーンにいるオンラインプレイヤー全員。人数とチーム分けに使う。</summary>
    public static readonly List<FishingNetPlayer> All = new List<FishingNetPlayer>();

    [Header("自分だけが動かすもの")]
    [Tooltip("自分のぶんだけ有効にするスクリプト。他の人のぶんは止める")]
    [SerializeField] private MonoBehaviour[] ownerOnlyScripts;

    [Tooltip("他の人のぶんでは切る CharacterController（切らないと送られてきた位置に移せない）")]
    [SerializeField] private CharacterController characterController;

    [Header("見た目")]
    [Tooltip("チームの色に塗る見た目")]
    [SerializeField] private Renderer[] teamRenderers;

    [Header("フックと糸")]
    [Tooltip("フックの先端。他の人のぶんは、送られてきた位置に置くだけ")]
    [SerializeField] private Transform hookVisual;

    [Tooltip("糸の見た目")]
    [SerializeField] private HookLine line;

    [Tooltip("糸の起点（手元）")]
    [SerializeField] private Transform handPoint;

    [Header("出てくる場所")]
    [Tooltip("中心からどれだけ離れた場所に出てくるか（メートル）")]
    [SerializeField] private float spawnRadius = 6f;

    // ---- ホストが決めて全員に配るもの ----

    /// <summary>参加番号（0から）。**ホストが決める。**</summary>
    private readonly NetworkVariable<int> playerIndex = new NetworkVariable<int>(-1);

    /// <summary>
    /// **そのPCの何人目か（0から）。ホストが出すときに決める。**
    /// ふつうは 0（1台に1人）。1台で複数人のとき、2人目以降はホストが追加で出し、1・2… が入る（<see cref="LocalMultiplayer"/>）。
    /// </summary>
    private readonly NetworkVariable<int> localSeat = new NetworkVariable<int>(0);

    // ---- 本人が送るもの（他の人が見るため） ----

    /// <summary>フックの先端の位置。**本人が送る。**</summary>
    private readonly NetworkVariable<Vector3> hookPosition = new NetworkVariable<Vector3>(
        Vector3.zero, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

    /// <summary>糸が出ているか。**本人が送る。**</summary>
    private readonly NetworkVariable<bool> lineVisible = new NetworkVariable<bool>(
        false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

    /// <summary>「狙っている物資が無い」を表す値。</summary>
    private const ulong NoAimTarget = ulong.MaxValue;

    /// <summary>
    /// オートエイムで狙っている物資（の通信上の番号）。**本人が送る。**
    /// 他の人の画面でも、その物資の上に目印を出すために使う（<see cref="HookAimAssist"/>）。
    /// </summary>
    private readonly NetworkVariable<ulong> aimTargetId = new NetworkVariable<ulong>(
        NoAimTarget, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

    // ---- ロビーで選んでいるマップ（ホストのプレイヤーの値だけを使う） ----

    /// <summary>
    /// ホストがロビーで選んでいるマップのシーン名。**ホストが書き、全員が読む。**
    ///
    /// ロビーの画面（<see cref="FishingLobbyUI"/>）は通信の部品ではないので、
    /// 参加者へ配る値を持てない。そこで**ロビーの時点ですでに全員の画面にいる
    /// ホストのプレイヤー**に持たせている。あとから入った人にも自動で届く。
    /// </summary>
    private readonly NetworkVariable<FixedString64Bytes> lobbyMapName =
        new NetworkVariable<FixedString64Bytes>(default);

    /// <summary>
    /// ホストがロビーで選んでいるマップのシーン名。まだ分からなければ空文字。
    /// </summary>
    public static string HostSelectedMap
    {
        get
        {
            foreach (FishingNetPlayer player in All)
            {
                if (player != null && player.OwnerClientId == NetworkManager.ServerClientId && player.LocalSeat == 0)
                {
                    return player.lobbyMapName.Value.ToString();
                }
            }

            return string.Empty;
        }
    }

    /// <summary>
    /// ホストが選んだマップを全員へ配る。**ホストのPCからだけ呼ぶこと。**
    /// </summary>
    public static void ServerSetSelectedMap(string sceneName)
    {
        foreach (FishingNetPlayer player in All)
        {
            if (player != null && player.IsServer && player.OwnerClientId == NetworkManager.ServerClientId && player.LocalSeat == 0)
            {
                FixedString64Bytes value = new FixedString64Bytes(sceneName ?? string.Empty);
                if (player.lobbyMapName.Value != value)
                {
                    player.lobbyMapName.Value = value;
                }
                return;
            }
        }
    }

    /// <summary>参加番号（0から）。まだ決まっていなければ -1。</summary>
    public int PlayerIndex => playerIndex.Value;

    /// <summary>そのPCの何人目か（0から）。1台に1人なら常に 0。</summary>
    public int LocalSeat => localSeat.Value;

    /// <summary>
    /// **人ごとの番号。** チーム分け（SpaceJunkSession の席）はこれで見分ける。
    /// 1人目は接続番号（OwnerClientId）そのままなので、1台に1人なら今までと同じ番号。
    /// </summary>
    public ulong PlayerKey => LocalMultiplayer.MakeKey(OwnerClientId, localSeat.Value);

    /// <summary>そのPCの何人目かを決める。**ホストが、出す（Spawn）前に呼ぶ。**</summary>
    public void ServerInitLocalSeat(int seat)
    {
        localSeat.Value = Mathf.Max(0, seat);
    }

    /// <summary>所属チーム。</summary>
    public int TeamIndex => FishingTeams.TeamOf(playerIndex.Value);

    /// <summary>画面に出す名前。</summary>
    public string DisplayName =>
        playerIndex.Value < 0 ? "参加中…" : $"プレイヤー{playerIndex.Value + 1}";

    private HookController hookController;
    private PlayerInputSource input;

    private void Awake()
    {
        hookController = GetComponent<HookController>();
        input = PlayerInputSource.Get(this);

        if (characterController == null)
        {
            characterController = GetComponent<CharacterController>();
        }
    }

    public override void OnNetworkSpawn()
    {
        // ホストが参加番号を決める。**空いている一番小さい番号を使う**
        if (IsServer)
        {
            playerIndex.Value = FirstFreeIndex();
        }

        All.Add(this);

        playerIndex.OnValueChanged += OnPlayerIndexChanged;
        ApplyTeamColor();

        Debug.Log($"[FISH] プレイヤー {OwnerClientId} が出てきました" +
                  $"（参加番号 {playerIndex.Value} ／ 自分が操作する：{IsOwner}）");

        if (IsOwner)
        {
            input.SeatIndex = LocalMultiplayer.IsActive ? localSeat.Value : -1;
            MoveToSpawnPoint();
            FollowWithCamera();
        }
        else
        {
            // 他の人のぶん。**このPCでは操作しない。**
            SetOwnerScriptsEnabled(false);

            if (characterController != null)
            {
                characterController.enabled = false;
            }
        }
    }

    public override void OnNetworkDespawn()
    {
        playerIndex.OnValueChanged -= OnPlayerIndexChanged;
        All.Remove(this);
    }

    private void OnPlayerIndexChanged(int before, int after)
    {
        ApplyTeamColor();
    }

    /// <summary>
    /// **まだ誰も使っていない、一番小さい参加番号を返す**（ホストだけが呼ぶ）。
    ///
    /// 以前は「いまいる人数」をそのまま番号にしていたが、
    /// **途中で抜けた人がいると番号が空き、次に入ってきた人が誰かと同じ番号になっていた**
    /// （例：0〜4 の5人 → 2番が抜ける → 残りは 0・1・3・4 で人数は4 → 次の人も4番。2026/9/24・大槻さん）。
    ///
    /// 番号は画面の名前（プレイヤー1…）だけでなく、**物資を誰が引っ掛けているかの判断**にも使うので、
    /// かぶると別の人の物資を投げられてしまう。
    /// </summary>
    private static int FirstFreeIndex()
    {
        for (int index = 0; index < FishingTeams.MaxPlayers; index++)
        {
            bool used = false;

            foreach (FishingNetPlayer player in All)
            {
                if (player != null && player.playerIndex.Value == index)
                {
                    used = true;
                    break;
                }
            }

            if (!used)
            {
                return index;
            }
        }

        // 満員のときの保険。ふだんは人数で先に断られるので、ここには来ない
        Debug.LogWarning("[FISH] 参加番号がすべて使われています。一番大きい番号を使います。");
        return FishingTeams.MaxPlayers - 1;
    }

    /// <summary>釣り会場に入って、配置とカメラを合わせ終わったか。</summary>
    private bool placedInMatch;

    private void Update()
    {
        if (IsOwner)
        {
            // 1台で複数人なら、そのPCの何人目かの席（P1・P2…）の機器だけで動かす。1人なら今までどおり
            input.SeatIndex = LocalMultiplayer.IsActive ? localSeat.Value : -1;

            // **プレイヤーはシーンをまたいで生き続ける**（ロビーで生まれた本体がそのまま来る）ので、
            // 釣り会場に着いたことに気づいたら、そこで出てくる場所とカメラを合わせ直す。
            // ホストからの合図を待つ形にすると、読み込みの速さで順番が変わって取りこぼすため、
            // **試合のまとめ役がこのPCに現れたかどうか**で判断している
            bool inMatch = FishingMatch.Current != null;

            if (inMatch && !placedInMatch)
            {
                placedInMatch = true;
                MoveToSpawnPoint();
                FollowWithCamera();
            }
            else if (!inMatch && placedInMatch)
            {
                // ロビーへ戻ったとき（次にまた会場へ入れるようにしておく）
                placedInMatch = false;
            }

            SendHookState();
        }
        else
        {
            ShowRemoteHookState();
        }
    }

    // ------------------------------------------------------------
    // フックと糸の共有
    // ------------------------------------------------------------

    /// <summary>自分のフックの位置と、糸が出ているかを送る。</summary>
    private void SendHookState()
    {
        if (hookController == null || hookVisual == null)
        {
            return;
        }

        hookPosition.Value = hookVisual.position;
        lineVisible.Value = hookController.Phase != HookController.HookPhase.Idle
                         && hookController.Phase != HookController.HookPhase.Charging;
    }

    /// <summary>他の人のフックと糸を、送られてきた値で再現する。</summary>
    private void ShowRemoteHookState()
    {
        if (hookVisual != null)
        {
            hookVisual.position = hookPosition.Value;
        }

        if (line != null && handPoint != null)
        {
            line.Show(lineVisible.Value);
            if (lineVisible.Value)
            {
                line.SetEnds(handPoint.position, hookPosition.Value);
            }
        }
    }

    // ------------------------------------------------------------
    // オートエイムの目印の共有
    // ------------------------------------------------------------

    /// <summary>
    /// 狙っている物資を他の人へ配る。**本人のPCからだけ呼ぶ**（<see cref="HookAimAssist"/>）。
    /// 値が変わったときだけ送られる。
    /// </summary>
    public void SetAimTarget(HookableObject target)
    {
        if (!IsSpawned || !IsOwner)
        {
            return;
        }

        ulong id = NoAimTarget;
        if (target != null)
        {
            NetworkObject networkObject = target.GetComponent<NetworkObject>();
            if (networkObject != null && networkObject.IsSpawned)
            {
                id = networkObject.NetworkObjectId;
            }
        }

        if (aimTargetId.Value != id)
        {
            aimTargetId.Value = id;
        }
    }

    /// <summary>この人がオートエイムで狙っている物資。狙っていなければ null。</summary>
    public HookableObject AimTarget
    {
        get
        {
            if (!IsSpawned || aimTargetId.Value == NoAimTarget || NetworkManager == null)
            {
                return null;
            }

            if (NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(aimTargetId.Value, out NetworkObject networkObject)
                && networkObject != null)
            {
                return networkObject.GetComponent<HookableObject>();
            }

            return null;
        }
    }

    // ------------------------------------------------------------
    // 見た目と出てくる場所
    // ------------------------------------------------------------

    /// <summary>
    /// チームの色に塗る。
    /// **参加番号から計算で決めているので、色を通信で送る必要がない**
    /// （どのPCで見ても同じ人が同じ色になる）。
    /// </summary>
    private void ApplyTeamColor()
    {
        if (teamRenderers == null || playerIndex.Value < 0)
        {
            return;
        }

        Color color = FishingTeams.TeamColor(TeamIndex);

        foreach (Renderer renderer in teamRenderers)
        {
            if (renderer != null)
            {
                renderer.material.color = color;
            }
        }
    }

    /// <summary>自分のぶんだけ操作スクリプトを有効／無効にする。</summary>
    private void SetOwnerScriptsEnabled(bool enabledState)
    {
        if (ownerOnlyScripts == null)
        {
            return;
        }

        foreach (MonoBehaviour script in ownerOnlyScripts)
        {
            if (script != null)
            {
                script.enabled = enabledState;
            }
        }
    }

    /// <summary>参加番号ごとに、円周上の違う場所へ移す。全員が重なって出てくるのを防ぐため。</summary>
    private void MoveToSpawnPoint()
    {
        int index = Mathf.Max(0, playerIndex.Value);
        float angle = index * (360f / FishingTeams.MaxPlayers);
        Vector3 position = Quaternion.Euler(0f, angle, 0f) * (Vector3.forward * spawnRadius);

        if (characterController != null)
        {
            characterController.enabled = false;
            transform.SetPositionAndRotation(position, Quaternion.Euler(0f, angle + 180f, 0f));
            characterController.enabled = true;
        }
        else
        {
            transform.SetPositionAndRotation(position, Quaternion.Euler(0f, angle + 180f, 0f));
        }
    }

    /// <summary>
    /// **シーンに置いてあるもの（カメラ・UI）と自分を結びつける。**
    ///
    /// プレイヤーはプレハブから生まれるので、**シーンの中身をあらかじめ入れておけない。**
    /// 自分のぶんが生まれた時点で、ここで探して渡す。
    /// </summary>
    private void FollowWithCamera()
    {
        // 狙いの計算に使うカメラ（1台で複数人のときも、全員が同じ画面のカメラを使う）
        PlayerAimController aim = GetComponent<PlayerAimController>();
        if (aim != null && Camera.main != null)
        {
            aim.SetCamera(Camera.main);
        }

        // チャージ量とスキルチェックのゲージは、人ごとに頭の上に出す（1台で複数人のときも全員に出る）
        if (hookController != null)
        {
            HookChargeUI ui = HookChargeUI.CreateOverhead(transform);
            if (ui != null && hookController.UI != ui)
            {
                hookController.SetUI(ui);
            }
        }

        // カメラは画面に1つしか無いので、そのPCの1人目にだけ結びつける
        if (localSeat.Value != 0)
        {
            return;
        }

        TopDownCameraFollow follow = FindFirstObjectByType<TopDownCameraFollow>();

        if (follow != null)
        {
            follow.SetTarget(transform);
        }
    }
}
