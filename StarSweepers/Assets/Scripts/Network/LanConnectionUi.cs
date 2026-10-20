using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

/// <summary>
/// LANでつなぐための、**仮の操作画面**。
///
/// 画面左上に「ホストで開始」「参加」のボタンと、つながっている人数を出す。
/// **このPCのIPアドレスも出す**ので、他のPCの人はそれを見て入力すればよい。
///
/// これは通信の土台を確かめるための簡易表示で、
/// **本番のタイトル画面はあとで作り直す**前提のもの。
/// （見た目を作り込むより、確実に動いて状態が見えることを優先している）
/// </summary>
[RequireComponent(typeof(NetworkManager))]
public class LanConnectionUi : MonoBehaviour
{
    [Tooltip("参加するときの、つなぎ先のIPアドレス。同じPCで試すなら 127.0.0.1")]
    [SerializeField] private string address = "127.0.0.1";

    [Tooltip("使うポート番号。4台とも同じ値にすること")]
    [SerializeField] private ushort port = 7777;

    [Tooltip("画面表示の大きさ。展示用の大きい画面では上げる")]
    [Range(1f, 4f)]
    [SerializeField] private float uiScale = 1.5f;

    [Tooltip("OFFにすると画面に何も出さなくなる（本番の画面ができたら切る）")]
    [SerializeField] private bool showUi = true;

    /// <summary>通信のまとめ役。同じ GameObject に付いている（RequireComponent）。</summary>
    private NetworkManager manager;

    /// <summary>このPCのIPアドレス（複数あれば改行区切り）。起動時に1回だけ調べて、画面に出し続ける。</summary>
    private string myAddresses = "調べています…";

    private void Awake()
    {
        manager = GetComponent<NetworkManager>();

        // 2台目以降のウィンドウが裏に回っても通信を続けるために必要
        Application.runInBackground = true;

        myAddresses = CollectLocalAddresses();
    }

    private void OnGUI()
    {
        if (!showUi)
        {
            return;
        }

        // **帯をつかんで動かせる／「－」で折りたためる／窓が小さいと縮む**枠で描く。
        // 位置を決め打ちしていたころは、小さい窓で他の表示と重なって読めなかった
        panel.Draw(uiScale, DrawContents);
    }

    /// <summary>枠（左上に出す）。引数は「帯の名前・横の基準（0＝左端）・横のずらし・縦の位置・幅」。</summary>
    private readonly DraggableGuiPanel panel = new DraggableGuiPanel("接続", 0f, 12f, 12f, 340f);

    /// <summary>枠の中身。つながっているかどうかで、出す画面を切り替える。<see cref="DraggableGuiPanel"/> から呼ばれる。</summary>
    private void DrawContents(int windowId)
    {
        if (manager == null)
        {
            GUILayout.Label("NetworkManager が見つかりません");
        }
        else if (!manager.IsClient && !manager.IsServer)
        {
            DrawDisconnected();
        }
        else
        {
            DrawConnected();
        }
    }

    /// <summary>つながっていないときに、いまどの画面を出しているか。</summary>
    private enum UiPage
    {
        /// <summary>遊び方を選ぶ画面</summary>
        ModeSelect,

        /// <summary>LANでつなぐ画面</summary>
        Lan,

        /// <summary>インターネットでつなぐ画面</summary>
        Internet,
    }

    /// <summary>つながっていないときに出している画面。切断すると遊び方を選ぶ画面に戻す。</summary>
    private UiPage page = UiPage.ModeSelect;

    /// <summary>参加するときに入力する合言葉。</summary>
    private string joinCode = string.Empty;

    /// <summary>まだつながっていないときの表示。</summary>
    private void DrawDisconnected()
    {
        switch (page)
        {
            case UiPage.ModeSelect:
                DrawModeSelect();
                break;

            case UiPage.Internet:
                DrawInternet();
                break;

            default:
                DrawLan();
                break;
        }
    }

    /// <summary>
    /// 遊び方を選ぶ画面。
    ///
    /// **本番のタイトル画面は、ここと同じ選択肢になる予定。**
    /// いまは中身を確かめるための仮の見た目。
    /// </summary>
    private void DrawModeSelect()
    {
        GUILayout.Label("■ 遊び方を選ぶ");
        GUILayout.Space(6f);

        if (GUILayout.Button("LANで遊ぶ（同じ場所のPC同士）"))
        {
            page = UiPage.Lan;
        }

        GUILayout.Space(4f);

        if (GUILayout.Button("インターネットで遊ぶ（離れた人と）"))
        {
            page = UiPage.Internet;
        }

        GUILayout.Space(8f);

        // 「通信を使わない」と言いつつ、中身は1人だけのホスト。
        // プレイヤーの生成や得点など、通信前提で作ってある部品をそのまま動かすため
        if (GUILayout.Button("1人で練習する（通信を使わない）"))
        {
            Apply("0.0.0.0");
            manager.StartHost();
        }
    }

    /// <summary>
    /// インターネットでつなぐ画面。
    ///
    /// **合言葉でつなぐ。** ホストが作った合言葉を相手に伝えると、
    /// 相手はそれを入れて入ってこられる。
    /// </summary>
    private void DrawInternet()
    {
        GUILayout.Label("■ インターネットでつなぐ");

        InternetConnection internet = GetComponent<InternetConnection>();

        if (internet == null)
        {
            GUILayout.Label("InternetConnection が付いていません");

            if (GUILayout.Button("戻る"))
            {
                page = UiPage.ModeSelect;
            }

            return;
        }

        // 作業中はボタンを押せなくする（二重に押されると事故のもと）
        GUI.enabled = !internet.IsBusy;

        GUILayout.Space(4f);

        if (GUILayout.Button("部屋を作る（合言葉が出ます）"))
        {
            internet.HostGame();
        }

        GUILayout.Space(8f);
        GUILayout.Label("合言葉を入れて参加する");

        joinCode = GUILayout.TextField(joinCode);

        if (GUILayout.Button("参加する"))
        {
            internet.JoinGame(joinCode);
        }

        GUI.enabled = true;

        // 合言葉ができていたら、大きめに出す（読み上げて伝えるため）
        if (!string.IsNullOrEmpty(internet.JoinCode))
        {
            GUILayout.Space(8f);
            GUILayout.Label($"あなたの合言葉：{internet.JoinCode}");
        }

        if (!string.IsNullOrEmpty(internet.Message))
        {
            GUILayout.Space(4f);
            GUILayout.Label(internet.Message);
        }

        GUILayout.Space(8f);

        if (GUILayout.Button("戻る"))
        {
            page = UiPage.ModeSelect;
        }
    }

    /// <summary>
    /// インターネットでつないでいる場合に、**合言葉を出す。**
    ///
    /// 文字を選んでコピーできるように、ただの文字ではなく入力欄の形にしてある。
    /// Discordなどに貼り付けて相手に伝えるため。
    /// </summary>
    private void DrawJoinCodeIfAny()
    {
        InternetConnection internet = GetComponent<InternetConnection>();

        if (internet == null || string.IsNullOrEmpty(internet.JoinCode))
        {
            return;
        }

        GUILayout.Space(4f);
        GUILayout.Label("■ 合言葉（相手に伝える）");

        // 戻り値は使わない。選んでコピーできるようにするためだけの入力欄
        GUILayout.TextField(internet.JoinCode);

        GUILayout.Space(4f);
    }

    /// <summary>LANでつなぐ画面。</summary>
    private void DrawLan()
    {
        GUILayout.Label("■ LANでつなぐ");
        GUILayout.Space(4f);

        GUILayout.Label($"このPCのIP：\n{myAddresses}");
        GUILayout.Space(4f);

        if (GUILayout.Button("ホストで開始（この1台が親）"))
        {
            Apply("0.0.0.0");
            manager.StartHost();
        }

        GUILayout.Space(8f);
        GUILayout.Label("参加する（親のIPを入れる）");

        address = GUILayout.TextField(address);

        GUILayout.BeginHorizontal();
        GUILayout.Label("ポート", GUILayout.Width(50f));
        string portText = GUILayout.TextField(port.ToString());

        // 数字として読めるときだけ反映する（消している途中や文字を打ったときは、前の値のまま）
        if (ushort.TryParse(portText, out ushort parsed))
        {
            port = parsed;
        }
        GUILayout.EndHorizontal();

        if (GUILayout.Button("参加する"))
        {
            Apply(null);
            manager.StartClient();
        }

        GUILayout.Space(8f);

        if (GUILayout.Button("戻る"))
        {
            page = UiPage.ModeSelect;
        }
    }

    /// <summary>つながっているときの表示。</summary>
    private void DrawConnected()
    {
        // ホスト＝サーバーでもあり参加者でもある1台。「サーバー」だけになるのは専用サーバーのときで、このゲームでは使っていない
        string role = manager.IsHost ? "ホスト（親）" : manager.IsServer ? "サーバー" : "参加者";

        GUILayout.Label($"■ {role} として動作中");

        // ★つながったあとも合言葉を出し続ける。
        //   ここに出していないと、部屋を作った本人が自分の合言葉を見られない
        //   （部屋ができた瞬間にこの画面へ切り替わるため）
        DrawJoinCodeIfAny();

        GUILayout.Label($"自分の番号：{manager.LocalClientId}");

        if (manager.IsServer)
        {
            GUILayout.Label($"つながっている人数：{manager.ConnectedClientsIds.Count} 人");

            StringBuilder builder = new StringBuilder("番号：");
            foreach (ulong id in manager.ConnectedClientsIds)
            {
                builder.Append(id).Append(' ');
            }
            GUILayout.Label(builder.ToString());
        }
        else
        {
            GUILayout.Label(manager.IsConnectedClient ? "接続できています" : "接続中…");
        }

        GUILayout.Space(8f);

        if (GUILayout.Button("切断する"))
        {
            InternetConnection internet = GetComponent<InternetConnection>();

            // インターネットでつないでいた場合は、部屋からも抜ける必要がある
            if (internet != null && internet.State == InternetConnection.Phase.Connected)
            {
                internet.LeaveGame();
            }
            else
            {
                manager.Shutdown();
            }

            page = UiPage.ModeSelect;
        }
    }

    /// <summary>
    /// 入力されたIPとポートを通信部品に反映する。
    ///
    /// <paramref name="listenAddress"/> は「待ち受けるアドレス」。
    /// ホストは "0.0.0.0"（どのネットワークからの参加も受け付ける）、
    /// 参加者は null（つなぎ先の <see cref="address"/> がそのまま使われる。参加者は待ち受けないので何でもよい）。
    /// </summary>
    private void Apply(string listenAddress)
    {
        if (manager.NetworkConfig.NetworkTransport is UnityTransport transport)
        {
            transport.SetConnectionData(address, port, listenAddress);
        }
    }

    /// <summary>
    /// このPCが持っているIPアドレスを集める。
    /// 他のPCの人が、ここに出た数字を入力して参加する。
    /// </summary>
    private static string CollectLocalAddresses()
    {
        List<string> found = new List<string>();

        try
        {
            foreach (IPAddress ip in Dns.GetHostEntry(Dns.GetHostName()).AddressList)
            {
                // IPv4（192.168.x.x のような形）だけ。IPv6 は長くて打ち込めないので出さない
                if (ip.AddressFamily == AddressFamily.InterNetwork)
                {
                    found.Add(ip.ToString());
                }
            }
        }
        catch (System.Exception error)
        {
            return $"取得できませんでした（{error.GetType().Name}）";
        }

        return found.Count > 0 ? string.Join("\n", found) : "見つかりませんでした";
    }
}
