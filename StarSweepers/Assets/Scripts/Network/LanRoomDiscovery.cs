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

    /// <summary>見つかった LAN の部屋1つぶん。</summary>
    public struct Room
    {
        public string Address;
        public ushort Port;
        public string Name;
        public int Players;
        public int MaxPlayers;
        public float LastSeen;
    }

    private static LanRoomDiscovery instance;

    private readonly Dictionary<string, Room> rooms = new Dictionary<string, Room>();
    private readonly List<Room> roomList = new List<Room>();

    private UdpClient sender;
    private UdpClient listener;
    private bool advertising;
    private string advertisedName = string.Empty;
    private int advertisedMaxPlayers = 4;
    private float advertiseTimer;
    private int listenRequests;

    /// <summary>いま見つかっている LAN の部屋（名前の順）。</summary>
    public static IReadOnlyList<Room> Rooms => Instance.roomList;

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

        ushort port = manager.NetworkConfig.NetworkTransport is UnityTransport transport ? transport.ConnectionData.Port : (ushort)7777;
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
            advertiseTimer = 5f; // 少し待ってからやり直す
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
            while (listener.Available > 0)
            {
                IPEndPoint from = new IPEndPoint(IPAddress.Any, 0);
                byte[] data = listener.Receive(ref from);
                Read(data, from);
            }
        }
        catch (System.Exception error)
        {
            Debug.LogWarning($"[LAN] 部屋の知らせを受け取れませんでした：{error.Message}");
        }

        // しばらく聞こえない部屋は消す
        float now = Time.unscaledTime;
        bool changed = false;
        List<string> expired = null;
        foreach (KeyValuePair<string, Room> pair in rooms)
        {
            if (now - pair.Value.LastSeen > RoomTimeout)
            {
                (expired ??= new List<string>()).Add(pair.Key);
            }
        }

        if (expired != null)
        {
            foreach (string key in expired)
            {
                rooms.Remove(key);
            }
            changed = true;
        }

        if (changed || roomList.Count != rooms.Count)
        {
            RebuildList();
        }
    }

    private void Read(byte[] data, IPEndPoint from)
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
