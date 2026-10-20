using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

/// <summary>
/// **起動時の引数を見て、自動でホストまたは参加を始める。**
///
/// 展示当日、4台のPCで毎回ボタンを押して回るのは手間なので、
/// ショートカットに引数を書いておけば、起動しただけでつながるようにしてある。
///
/// 使い方（ショートカットの「リンク先」の末尾に足す）：
///
///   ゲーム.exe -host                     … このPCがホストになる（LAN）
///   ゲーム.exe -client 192.168.0.10      … そのIPのホストへ参加する（LAN）
///   ゲーム.exe -client 192.168.0.10 -port 7777
///
/// インターネット越しの場合：
///
///   ゲーム.exe -nethost                  … 部屋を作る（合言葉が画面に出る）
///   ゲーム.exe -netjoin ABCDEF           … その合言葉の部屋に入る
///
/// 引数が無ければ何もしない（画面のボタンで操作する）。
/// </summary>
[RequireComponent(typeof(NetworkManager))]
public class LanAutoStart : MonoBehaviour
{
    [Tooltip("引数が無いときでも、エディタでの再生時は自動でホストになる")]
    [SerializeField] private bool autoHostInEditor = false;

    /// <summary>引数で自動的に始めたか。画面表示の切り替えに使える。</summary>
    public bool StartedFromCommandLine { get; private set; }

    /// <summary>
    /// 起動時の引数を読んで、LAN かインターネットかを決めて始める。
    /// `-nethost` / `-netjoin` は見つけた時点で始めて終わる（LAN の引数と混ざっていても、インターネット側を優先）。
    /// LAN の引数（`-host` `-client` `-port`）は全部読んでから始める（`-port` が後ろに書かれていてもよいように）。
    /// </summary>
    private void Start()
    {
        string[] args = System.Environment.GetCommandLineArgs();

        string address = null;
        ushort port = 0; // 0 ＝ 指定なし（Inspector の値を使う）
        bool wantHost = false;
        bool wantClient = false;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "-host":
                    wantHost = true;
                    break;

                case "-client":
                    wantClient = true;
                    // 次の引数がホストのIP（無ければ Inspector の値）
                    if (i + 1 < args.Length)
                    {
                        address = args[i + 1];
                    }
                    break;

                case "-port":
                    if (i + 1 < args.Length && ushort.TryParse(args[i + 1], out ushort parsed))
                    {
                        port = parsed;
                    }
                    break;

                case "-nethost":
                    StartInternet(null);
                    return;

                case "-netjoin":
                    StartInternet(i + 1 < args.Length ? args[i + 1] : null);
                    return;
            }
        }

        if (!wantHost && !wantClient)
        {
            if (autoHostInEditor && Application.isEditor)
            {
                StartAs(true, null, 0);
            }

            return;
        }

        // -host と -client の両方が書かれていたら、ホストにする
        StartAs(wantHost, address, port);
    }

    /// <summary>
    /// インターネット越しでつなぐ。
    /// `code` が null なら部屋を作る側、入っていればその合言葉で参加する側。
    /// </summary>
    private void StartInternet(string code)
    {
        InternetConnection internet = GetComponent<InternetConnection>();

        if (internet == null)
        {
            Debug.LogError("InternetConnection が見つかりません。自動接続を中止します。");
            return;
        }

        StartedFromCommandLine = true;

        if (string.IsNullOrEmpty(code))
        {
            Debug.Log("[NET] 起動時の引数で 部屋を作る を開始しました");
            internet.HostGame();
        }
        else
        {
            Debug.Log($"[NET] 起動時の引数で 参加 を開始しました（合言葉：{code}）");
            internet.JoinGame(code);
        }
    }

    /// <summary>
    /// LAN でホストまたは参加を始める。
    /// IP・ポートが指定されていなければ（null・0）、Inspector の UnityTransport に入っている値をそのまま使う。
    /// </summary>
    private void StartAs(bool asHost, string address, ushort port)
    {
        NetworkManager manager = GetComponent<NetworkManager>();
        var transport = manager.NetworkConfig.NetworkTransport as UnityTransport;

        if (transport == null)
        {
            Debug.LogError("UnityTransport が見つかりません。自動接続を中止します。");
            return;
        }

        string useAddress = string.IsNullOrEmpty(address)
            ? transport.ConnectionData.Address
            : address;

        ushort usePort = port == 0 ? transport.ConnectionData.Port : port;

        // ホストは "0.0.0.0" で待ち受ける（どのネットワークからの参加も受け付ける）。参加者は待ち受けないので null でよい
        transport.SetConnectionData(useAddress, usePort, asHost ? "0.0.0.0" : null);

        bool started = asHost ? manager.StartHost() : manager.StartClient();
        StartedFromCommandLine = true;

        Debug.Log(started
            ? $"[LAN] 起動時の引数で {(asHost ? "ホスト" : "参加")} を開始しました（{useAddress}:{usePort}）"
            : $"[LAN] 開始に失敗しました（{useAddress}:{usePort}）");
    }
}
