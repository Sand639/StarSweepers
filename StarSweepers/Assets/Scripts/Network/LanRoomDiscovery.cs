using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

/// <summary>
/// **同じネットワーク（LAN）の部屋を、IPアドレスを打たずに見つける仕組み。**（2026/10/13・タイトル画面の作り直し）
///
/// ・**部屋を作った側**（LAN のホスト）は、1秒ごとに「ここに部屋があります（名前・人数・番号）」を
///   同じネットワーク全体へ小さく知らせる（UDP のブロードキャスト）
/// ・**探す側**（タイトル画面の部屋の一覧）は、その知らせを聞いて一覧に足す。しばらく聞こえなくなった部屋は消す
///
/// 知らせに使う番号（ポート）は <see cref="DiscoveryPort"/>。ゲームの通信そのもの（UnityTransport のポート）とは別。
/// Windows のファイアウォールが「アクセスを許可しますか」と聞いてきたら、許可すること。
///
/// どのシーンにも置かなくてよい（使うときに自動で1つ作られ、シーンをまたいで残る）。
/// </summary>
public class LanRoomDiscovery : MonoBehaviour
{
    /// <summary>部屋の知らせに使うポート。</summary>
    public const int DiscoveryPort = 47777;

    /// <summary>知らせの先頭に付ける印（ほかのソフトの知らせと見分ける。中身の形を変えたら数字を上げる）。</summary>
    private const string Header = "STARSWEEPERS_LAN_ROOM_1";

    /// <summary>何秒ごとに知らせるか。</summary>
    private const float AdvertiseInterval = 1f;

    /// <summary>何秒聞こえなかったら、一覧から消すか。</summary>
    private const float RoomTimeout = 3.5f;

    /// <summary>知らせを送れなかったとき、何秒待ってからやり直すか。</summary>
    private const float SendRetryDelay = 5f;

    /// <summary>見つかった LAN の部屋1つぶん。</summary>
    public struct Room
    {
        /// <summary>ホストのIPアドレス（知らせの送り元から分かる）。</summary>
        public string Address;

        /// <summary>ゲームの通信に使うポート（UnityTransport のもの。<see cref="DiscoveryPort"/> ではない）。</summary>
        public ushort Port;

        /// <summary>部屋の名前。</summary>
        public string Name;

        /// <summary>いまの人数（ホストを含む）。</summary>
        public int Players;

        /// <summary>最大人数。</summary>
        public int MaxPlayers;

        /// <summary>最後に知らせが聞こえた時刻（`Time.unscaledTime`）。しばらく更新されなければ一覧から消す。</summary>
        public float LastSeen;
    }

    private static LanRoomDiscovery instance;

    /// <summary>見つかった部屋。鍵は「IPアドレス:ポート」（同じホストからの知らせを1つにまとめるため）。</summary>
    private readonly Dictionary<string, Room> rooms = new Dictionary<string, Room>();

    /// <summary><see cref="rooms"/> を名前の順に並べた写し。画面に出す側が毎回並べ直さなくて済むようにしている。</summary>
    private readonly List<Room> roomList = new List<Room>();

    /// <summary>知らせを送る口（部屋を作った側）。初めて送るときに開き、送れなかったら閉じて作り直す。</summary>
    private UdpClient sender;

    /// <summary>知らせを聞く口（探す側）。一覧を出している間だけ開いている。</summary>
    private UdpClient listener;

    /// <summary>いま知らせを送り続けているか。ホストをやめると自動で false になる。</summary>
    private bool advertising;

    /// <summary>知らせに載せる部屋の名前。</summary>
    private string advertisedName = string.Empty;

    /// <summary>知らせに載せる最大人数。</summary>
    private int advertisedMaxPlayers = 4;

    /// <summary>次に知らせを送るまでの残り秒数。</summary>
    private float advertiseTimer;

    /// <summary>
    /// 「聞きたい」と言っている画面の数。
    /// <see cref="StartListening"/> で増やし、<see cref="StopListening"/> で減らす。0 になったら聞くのをやめて一覧も消す
    /// （2つの画面が同時に聞いていても、片方が閉じただけで止まらないようにするため）。
    /// </summary>
    private int listenRequests;

    /// <summary>いま見つかっている LAN の部屋（名前の順）。</summary>
    public static IReadOnlyList<Room> Rooms => Instance.roomList;

    /// <summary>無ければ作る。シーンをまたいで残す（DontDestroyOnLoad）ので、ホストがロビーへ移っても知らせは続く。</summary>
    private static LanRoomDiscovery Instance
    {
        get
        {
            if (instance == null)
            {
                GameObject created = new GameObject("LanRoomDiscovery");
                DontDestroyOnLoad(created);
                instance = created.AddComponent<LanRoomDiscovery>();
            }
            return instance;
        }
    }

    /// <summary>
    /// 再生を始めるたびに、前回の再生の残骸を指さないようにする
    /// （エディタで「再生のたびに静的な値を消さない」設定にしていても安全なように）。
    /// </summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        instance = null;
    }

    // ------------------------------------------------------------
    // 部屋を作った側
    // ------------------------------------------------------------

    /// <summary>
    /// **この部屋を LAN に知らせ始める。** LAN のホストになったあとに呼ぶ。
    /// ホストをやめると（つながりが切れると）自動で止まる。
    /// </summary>
    public static void StartAdvertising(string roomName, int maxPlayers)
    {
        LanRoomDiscovery discovery = Instance;
        discovery.advertisedName = string.IsNullOrWhiteSpace(roomName) ? "LAN の部屋" : roomName.Replace('\n', ' ');
        discovery.advertisedMaxPlayers = Mathf.Max(1, maxPlayers);
        discovery.advertising = true;
        discovery.advertiseTimer = 0f;
        Debug.Log($"[LAN] 部屋「{discovery.advertisedName}」を同じネットワークに知らせます（ポート {DiscoveryPort}）。");
    }

    /// <summary>知らせるのをやめる（部屋を閉じたとき）。ホストをやめたときは、呼ばなくても自動で止まる。</summary>
    public static void StopAdvertising()
    {
        if (instance != null)
        {
            instance.advertising = false;
        }
    }

    // ------------------------------------------------------------
    // 探す側
    // ------------------------------------------------------------

    /// <summary>LAN の部屋の知らせを聞き始める（一覧を出している間だけ）。<see cref="StopListening"/> と対にして呼ぶ。</summary>
    public static void StartListening()
    {
        LanRoomDiscovery discovery = Instance;
        discovery.listenRequests++;
        discovery.OpenListener();
    }

    /// <summary>聞くのをやめる。ほかにも聞いている画面があれば、その画面が閉じるまでは聞き続ける。</summary>
    public static void StopListening()
    {
        if (instance == null)
        {
            return;
        }

        instance.listenRequests = Mathf.Max(0, instance.listenRequests - 1);
        if (instance.listenRequests == 0)
        {
            instance.CloseListener();
            instance.rooms.Clear();
            instance.roomList.Clear();
        }
    }

    // ------------------------------------------------------------
    // 毎フレーム
    // ------------------------------------------------------------

    private void Update()
    {
        UpdateAdvertising();
        UpdateListening();
    }

    private void OnDestroy()
    {
        CloseListener();
        sender?.Close();
        sender = null;
    }

    private void UpdateAdvertising()
    {
        if (!advertising)
        {
            return;
        }

        NetworkManager manager = NetworkManager.Singleton;
        if (manager == null || !manager.IsListening || !manager.IsServer)
        {
            // ホストをやめた
            advertising = false;
            return;
        }

        advertiseTimer -= Time.unscaledDeltaTime;
        if (advertiseTimer > 0f)
        {
            return;
        }
        advertiseTimer = AdvertiseInterval;

        // ゲームの通信に使っているポートを知らせる（参加する側はこの番号へつなぐ）。UnityTransport でなければ既定の 7777
        ushort port = manager.NetworkConfig.NetworkTransport is UnityTransport transport ? transport.ConnectionData.Port : (ushort)7777;

        // 知らせの形：改行区切りで「印・ポート・いまの人数・最大人数・部屋の名前」。
        // 受け取る側（ReadAnnouncement）も同じ順番で読む。**形を変えたら Header の数字を上げる**
        // （部屋の名前に改行が入ると区切りと混ざるので、StartAdvertising で取り除いてある）
        string message = string.Join("\n", Header, port.ToString(), manager.ConnectedClientsIds.Count.ToString(),
            advertisedMaxPlayers.ToString(), advertisedName);
        byte[] bytes = Encoding.UTF8.GetBytes(message);

        try
        {
            if (sender == null)
            {
                sender = new UdpClient { EnableBroadcast = true };
            }
            sender.Send(bytes, bytes.Length, new IPEndPoint(IPAddress.Broadcast, DiscoveryPort));
        }
        catch (System.Exception error)
        {
            Debug.LogWarning($"[LAN] 部屋の知らせを送れませんでした：{error.Message}");
            sender?.Close();
            sender = null;
            advertiseTimer = SendRetryDelay; // 少し待ってからやり直す
        }
    }

    private void UpdateListening()
    {
        if (listener == null)
        {
            return;
        }

        try
        {
            // このフレームまでに届いている知らせを全部読む（Available は、まだ読んでいないデータの量）
            while (listener.Available > 0)
            {
                IPEndPoint from = new IPEndPoint(IPAddress.Any, 0);
                byte[] data = listener.Receive(ref from);
                ReadAnnouncement(data, from);
            }
        }
        catch (System.Exception error)
        {
            Debug.LogWarning($"[LAN] 部屋の知らせを受け取れませんでした：{error.Message}");
        }

        // しばらく聞こえない部屋は消す
        bool changed = RemoveExpiredRooms();

        if (changed || roomList.Count != rooms.Count)
        {
            RebuildList();
        }
    }

    /// <summary>
    /// <see cref="RoomTimeout"/> 秒より長く聞こえていない部屋（ホストをやめた・落ちた・ネットワークから外れた）を消す。
    /// 1つでも消したら true。
    /// </summary>
    private bool RemoveExpiredRooms()
    {
        float now = Time.unscaledTime;
        List<string> expired = null;

        // Dictionary は foreach の途中で消せないので、いったん集めてから消す
        foreach (KeyValuePair<string, Room> pair in rooms)
        {
            if (now - pair.Value.LastSeen > RoomTimeout)
            {
                (expired ??= new List<string>()).Add(pair.Key);
            }
        }

        if (expired == null)
        {
            return false;
        }

        foreach (string key in expired)
        {
            rooms.Remove(key);
        }

        return true;
    }

    /// <summary>
    /// 届いた知らせを読んで、部屋の一覧に足す（すでにある部屋なら上書きして、最後に聞こえた時刻を更新する）。
    /// 形は <see cref="UpdateAdvertising"/> が送るものと同じ。印が違う・数字として読めないものは、ほかのソフトの知らせとみなして捨てる。
    /// </summary>
    private void ReadAnnouncement(byte[] data, IPEndPoint from)
    {
        string[] fields = Encoding.UTF8.GetString(data).Split('\n');
        if (fields.Length < 5 || fields[0] != Header ||
            !ushort.TryParse(fields[1], out ushort port) ||
            !int.TryParse(fields[2], out int players) ||
            !int.TryParse(fields[3], out int maxPlayers))
        {
            return;
        }

        string address = from.Address.ToString();
        string key = $"{address}:{port}";

        rooms[key] = new Room
        {
            Address = address,
            Port = port,
            Name = fields[4],
            Players = players,
            MaxPlayers = maxPlayers,
            LastSeen = Time.unscaledTime,
        };

        RebuildList();
    }

    /// <summary>画面に出す一覧を作り直す。名前（同じ名前なら IP）の文字の順で、並びが毎回変わらないようにする。</summary>
    private void RebuildList()
    {
        roomList.Clear();
        roomList.AddRange(rooms.Values);
        roomList.Sort((a, b) => string.CompareOrdinal(a.Name + a.Address, b.Name + b.Address));
    }

    private void OpenListener()
    {
        if (listener != null)
        {
            return;
        }

        try
        {
            // 同じPCでゲームを2つ起動しても両方で聞けるように、ポートを共有する
            listener = new UdpClient { ExclusiveAddressUse = false, EnableBroadcast = true };
            listener.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            listener.Client.Bind(new IPEndPoint(IPAddress.Any, DiscoveryPort));
        }
        catch (System.Exception error)
        {
            Debug.LogWarning($"[LAN] 部屋の知らせを聞く準備ができませんでした（ポート {DiscoveryPort}）：{error.Message}");
            CloseListener();
        }
    }

    private void CloseListener()
    {
        listener?.Close();
        listener = null;
    }
}
