#if !DISABLESTEAMWORKS && (UNITY_STANDALONE_WIN || UNITY_STANDALONE_LINUX || UNITY_STANDALONE_OSX || STEAMWORKS_WIN || STEAMWORKS_LIN_OSX)
#define STARSWEEPERS_STEAM
#endif

using System.Collections.Generic;
using System.IO;
using Unity.Netcode;
using UnityEngine;
#if STARSWEEPERS_STEAM
using Steamworks;
#endif

/// <summary>Steam のフレンドがいる部屋1つぶん。</summary>
public class SteamFriendRoom
{
    /// <summary>フレンドの名前（Steam の表示名）。</summary>
    public string FriendName;

    /// <summary>部屋の名前。</summary>
    public string RoomName;

    /// <summary>参加コード（これで部屋に入る）。</summary>
    public string JoinCode;

    /// <summary>「2/8」など。</summary>
    public string Players;

    /// <summary>パスワードが付いているか。</summary>
    public bool HasPassword;
}

/// <summary>
/// **Steam のフレンドの部屋を見つける係。**（2026/10/6・大槻さん「スプラみたいに、フレンドのサーバーを出したい」）
///
/// 通信そのものは今までどおり Unity の部屋（<see cref="InternetConnection"/>）を使い、**Steam はフレンドの一覧と、
/// フレンドがどの部屋にいるかを知るためだけ**に使う。
///
/// ・部屋に入っている間、Steam の「リッチプレゼンス」（フレンドに見える、いまの様子）に**参加コード**などを書いておく
/// ・タイトルの「フレンドのサーバーを探す」で、このゲームで部屋にいるフレンドを並べる（<see cref="FindFriendRooms"/>）
/// ・Steam のフレンド一覧（Shift+Tab の画面）の「ゲームに参加」からも入れる（タイトル画面にいるとき）
///
/// **Steam を起動していない・Steam で動かせない環境では何もしない**（<see cref="IsAvailable"/> が false。ほかは今までどおり動く）。
/// 展示会場（LAN）でも邪魔にならない。
///
/// ## App ID について
///
/// まだこのゲーム専用の App ID が無いので、Valve のテスト用の **480（Spacewar）** で動かしている（`StarSweepers/steam_appid.txt`）。
/// このため、Steam のフレンド一覧には「Spacewar をプレイ中」と出る。**Steam で配ることが決まって App ID をもらったら、
/// `steam_appid.txt` の数字を変える**（Unity を開き直す）。
///
/// シーンに置かなくても、再生したときに自分で1つ作る。
/// </summary>
public class SteamFriendsService : MonoBehaviour
{
    /// <summary>リッチプレゼンスの鍵（フレンドのPCで読む）。</summary>
    private const string CodeKey = "ss_code";
    private const string RoomKey = "ss_room";
    private const string PlayersKey = "ss_players";
    private const string PasswordKey = "ss_password";

    /// <summary>Steam が「ゲームに参加」で渡してくる文字（"+join 参加コード"）。</summary>
    private const string ConnectKey = "connect";
    private const string JoinPrefix = "+join ";

    /// <summary>リッチプレゼンスを書き直す間隔（秒）。</summary>
    private const float PresenceInterval = 2f;

    /// <summary>Steam が使える状態か（Steam が起動していて、初期化できた）。</summary>
    public static bool IsAvailable { get; private set; }

    /// <summary>Steam が使えないときの理由（画面に出す）。使えるなら空。</summary>
    public static string StatusMessage { get; private set; } =
        "このゲームは Steam の機能に対応していない環境で動いているので、フレンドのサーバーは出せません。";

    /// <summary>
    /// まだこのゲーム専用の App ID が無いので使っている、Valve のテスト用の App ID（Spacewar）。
    /// App ID をもらったら、ここと `steam_appid.txt` を変える。
    /// </summary>
    public const uint DevelopmentAppId = 480;

    /// <summary>
    /// Steam から「このコードの部屋に入りたい」と言われたときの参加コード（タイトル画面が受け取って入る）。
    /// 受け取ったら <see cref="ConsumePendingJoinCode"/> で消す。
    /// </summary>
    public static string PendingJoinCode { get; private set; } = string.Empty;

    private float presenceTimer;
    private string lastPresence = string.Empty;

#if STARSWEEPERS_STEAM
    private Callback<GameRichPresenceJoinRequested_t> joinRequested;
#endif

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AutoCreate()
    {
        if (FindFirstObjectByType<SteamFriendsService>() != null)
        {
            return;
        }

        GameObject created = new GameObject("SteamFriendsService (自動)");
        created.AddComponent<SteamFriendsService>();
        DontDestroyOnLoad(created);
    }

    /// <summary>Steam から渡された参加コードを受け取って消す。無ければ空。</summary>
    public static string ConsumePendingJoinCode()
    {
        string code = PendingJoinCode;
        PendingJoinCode = string.Empty;
        return code;
    }

#if STARSWEEPERS_STEAM
    private void Awake()
    {
        try
        {
            if (!Packsize.Test() || !DllCheck.Test())
            {
                StatusMessage = "Steam の部品が正しく入っていないので、Steam の機能は使えません。";
                Debug.LogWarning("[STEAM] " + StatusMessage);
                return;
            }

            // **steam_appid.txt が exe の隣に無くても、つながるようにする**（2026/10/7）。
            // Steam の外（exe をダブルクリック）で起動したときは、steam_appid.txt で「どのゲームか」を Steam に伝える。
            // ところが Unity のふつうのビルド（Build Profiles）は、このファイルをビルドに入れてくれない。
            // そこで、ファイルが無ければ、同じことを環境変数（SteamAppId）で伝える
            if (!File.Exists("steam_appid.txt") && string.IsNullOrEmpty(System.Environment.GetEnvironmentVariable("SteamAppId")))
            {
                System.Environment.SetEnvironmentVariable("SteamAppId", DevelopmentAppId.ToString());
                System.Environment.SetEnvironmentVariable("SteamGameId", DevelopmentAppId.ToString());
                Debug.Log($"[STEAM] steam_appid.txt が無いので、App ID {DevelopmentAppId} として Steam につなぎます。");
            }

            ESteamAPIInitResult result = SteamAPI.InitEx(out string error);
            IsAvailable = result == ESteamAPIInitResult.k_ESteamAPIInitResult_OK;

            if (!IsAvailable)
            {
                switch (result)
                {
                    case ESteamAPIInitResult.k_ESteamAPIInitResult_NoSteamClient:
                        StatusMessage = "Steam が起動していないので、フレンドのサーバーは出せません。\nSteam を起動してから、ゲームを立ち上げ直してください。";
                        break;
                    case ESteamAPIInitResult.k_ESteamAPIInitResult_VersionMismatch:
                        StatusMessage = "Steam が古いので、フレンドのサーバーは出せません。Steam を更新してください。";
                        break;
                    default:
                        StatusMessage = $"Steam につながりませんでした（{error}）。\nSteam にログインしているか確かめて、ゲームを立ち上げ直してください。";
                        break;
                }

                Debug.Log($"[STEAM] Steam につながりませんでした（{result}：{error}）。フレンドの機能は使いません。ほかは今までどおり動きます。");
                return;
            }
        }
        catch (System.DllNotFoundException error)
        {
            StatusMessage = "steam_api64.dll が見つからないので、Steam の機能は使えません。";
            Debug.LogWarning($"[STEAM] {StatusMessage}（{error.Message}）");
            IsAvailable = false;
            return;
        }

        StatusMessage = string.Empty;
        Debug.Log($"[STEAM] Steam につながりました（{SteamFriends.GetPersonaName()}・App ID {SteamUtils.GetAppID()}）。");
        joinRequested = Callback<GameRichPresenceJoinRequested_t>.Create(OnJoinRequested);

        // Steam の「ゲームに参加」から起動されたときは、コマンドラインに "+join 参加コード" が付いてくる
        string[] args = System.Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == "+join")
            {
                PendingJoinCode = args[i + 1];
            }
        }
    }

    private void Update()
    {
        if (!IsAvailable)
        {
            return;
        }

        SteamAPI.RunCallbacks();

        presenceTimer -= Time.unscaledDeltaTime;
        if (presenceTimer <= 0f)
        {
            presenceTimer = PresenceInterval;
            UpdatePresence();
        }
    }

    private void OnDestroy()
    {
        if (IsAvailable)
        {
            SteamFriends.ClearRichPresence();
            SteamAPI.Shutdown();
            IsAvailable = false;
        }
    }

    /// <summary>
    /// 部屋に入っている間は、参加コードなどをフレンドに見えるようにしておく。入っていなければ消す。
    /// </summary>
    private void UpdatePresence()
    {
        InternetConnection internet = Internet();
        bool inRoom = internet != null && internet.State == InternetConnection.Phase.Connected &&
                      !string.IsNullOrEmpty(internet.JoinCode);

        string presence = inRoom
            ? $"{internet.JoinCode}|{internet.RoomName}|{internet.RoomPlayers}/{internet.RoomMaxPlayers}|{internet.HasPassword}"
            : string.Empty;

        if (presence == lastPresence)
        {
            return;
        }

        lastPresence = presence;

        if (!inRoom)
        {
            SteamFriends.ClearRichPresence();
            return;
        }

        SteamFriends.SetRichPresence(CodeKey, internet.JoinCode);
        SteamFriends.SetRichPresence(RoomKey, internet.RoomName);
        SteamFriends.SetRichPresence(PlayersKey, $"{internet.RoomPlayers}/{internet.RoomMaxPlayers}");
        SteamFriends.SetRichPresence(PasswordKey, internet.HasPassword ? "1" : "0");
        SteamFriends.SetRichPresence(ConnectKey, JoinPrefix + internet.JoinCode);
    }

    /// <summary>Steam のフレンド一覧の「ゲームに参加」が押されたとき。</summary>
    private void OnJoinRequested(GameRichPresenceJoinRequested_t request)
    {
        string connect = request.m_rgchConnect ?? string.Empty;
        if (connect.StartsWith(JoinPrefix))
        {
            PendingJoinCode = connect.Substring(JoinPrefix.Length).Trim();
            Debug.Log($"[STEAM] フレンドの部屋（{PendingJoinCode}）に入るよう頼まれました。");
        }
    }

    /// <summary>
    /// フレンドの部屋の情報を取り寄せる（<see cref="FindFriendRooms"/> の前に呼ぶ。届くまで少しかかる）。
    /// </summary>
    public static void RequestFriendRooms()
    {
        if (!IsAvailable)
        {
            return;
        }

        foreach (CSteamID friend in FriendsPlayingThisGame())
        {
            SteamFriends.RequestFriendRichPresence(friend);
        }
    }

    /// <summary>このゲームで部屋に入っているフレンドの一覧。Steam が使えなければ空。</summary>
    public static List<SteamFriendRoom> FindFriendRooms()
    {
        List<SteamFriendRoom> rooms = new List<SteamFriendRoom>();
        if (!IsAvailable)
        {
            return rooms;
        }

        foreach (CSteamID friend in FriendsPlayingThisGame())
        {
            string code = SteamFriends.GetFriendRichPresence(friend, CodeKey);
            if (string.IsNullOrEmpty(code))
            {
                continue;
            }

            rooms.Add(new SteamFriendRoom
            {
                FriendName = SteamFriends.GetFriendPersonaName(friend),
                RoomName = SteamFriends.GetFriendRichPresence(friend, RoomKey),
                JoinCode = code,
                Players = SteamFriends.GetFriendRichPresence(friend, PlayersKey),
                HasPassword = SteamFriends.GetFriendRichPresence(friend, PasswordKey) == "1",
            });
        }

        return rooms;
    }

    /// <summary>
    /// **うまく見つからないときの手がかり**（2026/10/7）。Steam のフレンドの数と、このゲームを遊んでいる人の数。
    /// フレンドのサーバーの画面に出して、どこで止まっているかを分かるようにする。
    /// </summary>
    public static string Diagnose()
    {
        if (!IsAvailable)
        {
            return StatusMessage;
        }

        int friends = SteamFriends.GetFriendCount(EFriendFlags.k_EFriendFlagImmediate);
        int playing = FriendsPlayingThisGame().Count;
        return $"Steam：{SteamFriends.GetPersonaName()} でつながっています（App ID {SteamUtils.GetAppID()}）。\n" +
               $"フレンド {friends} 人のうち、このゲームを遊んでいる人 {playing} 人";
    }

    /// <summary>このゲーム（同じ App ID）を遊んでいるフレンドの数。</summary>
    public static int CountFriendsPlayingThisGame()
    {
        return IsAvailable ? FriendsPlayingThisGame().Count : 0;
    }

    /// <summary>いまこのゲーム（同じ App ID）を遊んでいるフレンド。</summary>
    private static List<CSteamID> FriendsPlayingThisGame()
    {
        List<CSteamID> friends = new List<CSteamID>();
        AppId_t thisGame = SteamUtils.GetAppID();

        int count = SteamFriends.GetFriendCount(EFriendFlags.k_EFriendFlagImmediate);
        for (int i = 0; i < count; i++)
        {
            CSteamID friend = SteamFriends.GetFriendByIndex(i, EFriendFlags.k_EFriendFlagImmediate);
            if (SteamFriends.GetFriendGamePlayed(friend, out FriendGameInfo_t game) && game.m_gameID.AppID() == thisGame)
            {
                friends.Add(friend);
            }
        }

        return friends;
    }
#else
    /// <summary>Steam が使えない環境：何もしない。</summary>
    public static void RequestFriendRooms()
    {
    }

    /// <summary>Steam が使えない環境：理由だけ返す。</summary>
    public static string Diagnose()
    {
        return StatusMessage;
    }

    /// <summary>Steam が使えない環境：いつも 0。</summary>
    public static int CountFriendsPlayingThisGame()
    {
        return 0;
    }

    /// <summary>Steam が使えない環境：いつも空。</summary>
    public static List<SteamFriendRoom> FindFriendRooms()
    {
        return new List<SteamFriendRoom>();
    }
#endif

    private static InternetConnection Internet()
    {
        NetworkManager manager = NetworkManager.Singleton;
        return manager != null ? manager.GetComponent<InternetConnection>() : null;
    }
}
