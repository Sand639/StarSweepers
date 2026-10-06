using System;
using System.IO;
using System.Threading.Tasks;
using Unity.Netcode;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Multiplayer;
using UnityEngine;

/// <summary>
/// **インターネット越しに、離れた人とつなぐ。**
///
/// 家のPCはルーターの内側にいるため、外から直接つなぐことができない。
/// そこで **Unityの中継サーバー（Relay）を1つ挟んで、両方から外向きにつなぐ。**
///
/// つなぎ方は**合言葉（6文字くらいの英数字）**を使う。
/// ホストが合言葉を作り、それを相手に伝えると、相手はそれを入れて入ってこられる。
///
/// ## LANとの違いは「つなぐ前の準備」だけ
///
/// ゲームの中身（位置・得点・物理の同期）は**LANのときとまったく同じもの**が動く。
/// 変わるのは、どの道を通ってつながるかだけ。
///
/// | | LAN | インターネット |
/// | --- | --- | --- |
/// | つなぎ方 | 相手のIPを直接指定 | 合言葉で中継サーバー経由 |
/// | ゲームの中身 | 同じ | 同じ |
/// | 遅れ | ほぼ無し | 数十ミリ秒 |
///
/// ## 使う前に必要な準備（人の作業）
///
/// **Unity Cloud にこのプロジェクトを登録しておく必要がある。**
/// 済んでいない場合は、画面にその旨が出るようにしてある。
/// 手順は `Documents/機能ドキュメント/インターネットでの複数人プレイ.md` を読むこと。
/// </summary>
[RequireComponent(typeof(NetworkManager))]
public class InternetConnection : MonoBehaviour
{
    /// <summary>いま何をしている最中か。画面表示に使う。</summary>
    public enum Phase
    {
        /// <summary>まだ何もしていない</summary>
        Idle,

        /// <summary>つなぐ準備をしている（初回だけ数秒かかる）</summary>
        Preparing,

        /// <summary>部屋を作っている（ホスト側）</summary>
        Creating,

        /// <summary>部屋に入ろうとしている（参加側）</summary>
        Joining,

        /// <summary>つながっている</summary>
        Connected,

        /// <summary>失敗した。理由は Message に入る</summary>
        Failed,
    }

    [Header("部屋の設定")]
    [Tooltip("同時に遊べる人数。ホストを含む")]
    [Range(2, 8)]
    [SerializeField] private int maxPlayers = 4;

    [Tooltip("部屋の名前。一覧に出すわけではないので、分かりやすければよい")]
    [SerializeField] private string sessionName = "StarSweepers";

    [Tooltip("ONにすると、部屋の一覧に出さない（合言葉を知っている人だけが入れる）")]
    [SerializeField] private bool isPrivate = true;

    [Header("中継サーバー")]
    [Tooltip("使う地域。空欄にすると、一番速い地域を自動で選ぶ（ふつうは空欄でよい）。" +
             "例：asia-northeast1（東京）／asia-southeast1（シンガポール）")]
    [SerializeField] private string region = string.Empty;

    /// <summary>いまの状態。</summary>
    public Phase State { get; private set; } = Phase.Idle;

    /// <summary>ホストが作った合言葉。相手に伝える。</summary>
    public string JoinCode { get; private set; } = string.Empty;

    /// <summary>画面に出す説明。失敗したときは理由が入る。</summary>
    public string Message { get; private set; } = string.Empty;

    /// <summary>いま作業中か。ボタンを押せなくするのに使う。</summary>
    public bool IsBusy =>
        State == Phase.Preparing || State == Phase.Creating || State == Phase.Joining;

    private ISession session;

    /// <summary>
    /// 「このPCで何番目に起動したか」を押さえておくための鍵。
    /// 掴んだままにしておくと、他のインスタンスは同じ番号を取れない。
    /// **プロセスが終われば自動で解放される。**
    /// </summary>
    private static FileStream instanceLock;

    /// <summary>自動で決まったプロファイル名。1番目は null（初期のまま）。</summary>
    private static string autoProfile;

    private static bool profileResolved;

    private void Awake()
    {
        // 起動した順に番号を取る。**つなぐ前に決めておく**のが大事
        ResolveInstanceSlot();
    }

    // ------------------------------------------------------------
    // 外から呼ぶ入口
    // ------------------------------------------------------------

    /// <summary>ホストになって部屋を作る。できたら合言葉が <see cref="JoinCode"/> に入る。</summary>
    public async void HostGame()
    {
        if (IsBusy)
        {
            return;
        }

        if (!await PrepareAsync())
        {
            return;
        }

        // 前の部屋から抜け損ねていたら、先に抜ける（残っていると「すでに参加しています」で入れない）
        await LeaveStaleSessionsAsync();

        State = Phase.Creating;
        Message = "部屋を作っています…";

        try
        {
            // 地域を空欄にすると、Unityが一番速い地域を測って選んでくれる
            SessionOptions options = new SessionOptions
            {
                Name = sessionName,
                MaxPlayers = maxPlayers,
                IsPrivate = isPrivate,
            }.WithRelayNetwork(string.IsNullOrWhiteSpace(region) ? null : region.Trim());

            // 部屋ができると、通信の開始（ホストとしての待ち受け）まで自動で行われる
            session = await MultiplayerService.Instance.CreateSessionAsync(options);

            JoinCode = session.Code;
            State = Phase.Connected;
            Message = "部屋ができました。合言葉を相手に伝えてください。";

            Debug.Log($"[NET] 部屋を作りました。合言葉：{JoinCode}");
        }
        catch (Exception error)
        {
            Fail("部屋を作れませんでした", error);
        }
    }

    /// <summary>合言葉を使って、他の人の部屋に入る。</summary>
    public async void JoinGame(string code)
    {
        if (IsBusy)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(code))
        {
            State = Phase.Failed;
            Message = "合言葉が入力されていません。";
            return;
        }

        if (!await PrepareAsync())
        {
            return;
        }

        // 前の部屋から抜け損ねていたら、先に抜ける（残っていると「すでに参加しています」で入れない）
        await LeaveStaleSessionsAsync();

        State = Phase.Joining;
        Message = "部屋に入ろうとしています…";

        try
        {
            session = await MultiplayerService.Instance.JoinSessionByCodeAsync(code.Trim());

            JoinCode = session.Code;
            State = Phase.Connected;
            Message = "つながりました。";

            Debug.Log($"[NET] 部屋に入りました。合言葉：{JoinCode}");
        }
        catch (Exception error)
        {
            Fail("部屋に入れませんでした。合言葉が違うか、部屋が閉じられています", error);
        }
    }

    /// <summary>部屋から出る。</summary>
    public async void LeaveGame()
    {
        await LeaveGameAsync();
    }

    /// <summary>
    /// **部屋から出て、抜け終わるまで待つ。** 抜けたあとで通信も止める。
    /// 先に通信を止めると抜ける処理が失敗し、中継サーバーに「まだ参加している」記録が残って、
    /// 次に同じ合言葉で入れなくなる（SessionConflict。2026/10/6）。
    /// </summary>
    public async Task LeaveGameAsync()
    {
        try
        {
            if (session != null)
            {
                await session.LeaveAsync();
            }
        }
        catch (Exception error)
        {
            Debug.LogWarning($"[NET] 退出でエラー：{error.Message}");
        }
        finally
        {
            session = null;
            JoinCode = string.Empty;
            State = Phase.Idle;
            Message = string.Empty;

            // 念のため、通信も止めておく
            if (NetworkManager.Singleton != null &&
                (NetworkManager.Singleton.IsClient || NetworkManager.Singleton.IsServer))
            {
                NetworkManager.Singleton.Shutdown();
            }
        }
    }

    /// <summary>
    /// **抜け損ねて残っている部屋があれば、抜ける。** 部屋を作る・入る直前に呼ぶ。
    ///
    /// 前の部屋から正しく抜けられなかったとき（通信を先に止めた、ゲームが固まった など）、
    /// 中継サーバーには「まだ参加している」記録が残り、同じ合言葉で入ろうとすると
    /// SessionConflict（すでに参加しています）で断られる。ここで先に抜けておく。
    /// </summary>
    private async Task LeaveStaleSessionsAsync()
    {
        if (MultiplayerService.Instance == null || MultiplayerService.Instance.Sessions == null)
        {
            return;
        }

        // 抜けると一覧から消えるので、先に写しておく
        var stale = new System.Collections.Generic.List<ISession>(MultiplayerService.Instance.Sessions.Values);

        foreach (ISession leftover in stale)
        {
            try
            {
                Debug.Log($"[NET] 抜け損ねていた部屋から抜けます（合言葉：{leftover.Code}）。");
                await leftover.LeaveAsync();
            }
            catch (Exception error)
            {
                Debug.LogWarning($"[NET] 残っていた部屋から抜けられませんでした：{error.Message}");
            }
        }

        session = null;
    }

    // ------------------------------------------------------------
    // 準備（初回だけ）
    // ------------------------------------------------------------

    /// <summary>
    /// 中継サーバーを使うための下準備。
    ///
    /// **初回だけ数秒かかる。** 2回目以降はすぐ終わる。
    /// 準備できたら true を返す。
    /// </summary>
    private async Task<bool> PrepareAsync()
    {
        // ★ここが一番よくある詰まりどころ。
        //   Unity Cloud への登録が済んでいないと、その先へ進めない
        if (string.IsNullOrEmpty(Application.cloudProjectId))
        {
            State = Phase.Failed;
            Message =
                "Unity Cloud への登録が済んでいません。\n" +
                "Unityの Project Settings → Services から\n" +
                "プロジェクトを登録し、Relay を有効にしてください。\n" +
                "（手順：Documents/機能ドキュメント/インターネットでの複数人プレイ.md）";

            Debug.LogError("[NET] Unity Cloud への登録が済んでいません。");
            return false;
        }

        State = Phase.Preparing;
        Message = "つなぐ準備をしています…";

        try
        {
            if (UnityServices.State != ServicesInitializationState.Initialized)
            {
                await UnityServices.InitializeAsync();
            }

            // ★1台のPCで2つ起動して試すときに必要。下の説明を読むこと
            ApplyProfile();

            // 名前やパスワードは要らない。この場かぎりの身分証を発行してもらうだけ
            if (!AuthenticationService.Instance.IsSignedIn)
            {
                await AuthenticationService.Instance.SignInAnonymouslyAsync();
            }

            return true;
        }
        catch (Exception error)
        {
            Fail("つなぐ準備に失敗しました。インターネットにつながっているか確認してください", error);
            return false;
        }
    }

    /// <summary>
    /// **1台のPCで2つ起動して試すときのための仕組み。**
    ///
    /// 匿名サインインの身分証は、**1台のPCにつき1つ**しか保存されない。
    /// そのため同じPCで2つ起動すると、**両方が「同じ人」として扱われ**、
    /// 参加しようとしたときに「すでにこの部屋の一員です」と断られる。
    ///
    /// Unityには**プロファイル**という仕組みがあり、
    /// 名前を分けると**別人として扱われる**ので、それを使う。
    ///
    /// **起動した順に自動で決まる**ので、ふつうは何もしなくてよい。
    /// 1番目はそのまま、2番目は `slot1`、3番目は `slot2` …となる。
    ///
    /// 手で決めたいときだけ、起動時の引数を使う：
    ///
    ///   ゲーム.exe -nethost -profile A
    ///
    /// **別々のPCで遊ぶ本番では、そもそもぶつからない。**
    /// PCが違えば身分証も別々になるため。
    /// </summary>
    private static void ResolveInstanceSlot()
    {
        if (profileResolved)
        {
            return;
        }

        profileResolved = true;

        // 手で指定されていれば、それを優先する
        autoProfile = ReadProfileArgument();

        if (!string.IsNullOrWhiteSpace(autoProfile))
        {
            Debug.Log($"[NET] プロファイルは引数の「{autoProfile}」を使います");
            return;
        }

        // 指定が無ければ、空いている番号を先着順で取る。
        // 鍵ファイルを掴んだままにするので、他のインスタンスは同じ番号を取れない
        for (int slot = 0; slot < 8; slot++)
        {
            string path = Path.Combine(Application.persistentDataPath, $"instance{slot}.lock");

            try
            {
                instanceLock = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);

                // 1番目はそのまま（本番はここしか通らない）
                autoProfile = slot == 0 ? null : $"slot{slot}";

                Debug.Log(autoProfile == null
                    ? "[NET] このPCで1番目の起動です（プロファイルはそのまま）"
                    : $"[NET] このPCで{slot + 1}番目の起動です。プロファイルを「{autoProfile}」にします");

                return;
            }
            catch (IOException)
            {
                // その番号は他のインスタンスが使っている。次を試す
            }
        }

        Debug.LogWarning("[NET] 空いているプロファイル番号がありません（8つまで）");
    }

    /// <summary>起動時の引数から `-profile ＜名前＞` を読む。無ければ null。</summary>
    private static string ReadProfileArgument()
    {
        string[] args = Environment.GetCommandLineArgs();

        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == "-profile" && !string.IsNullOrWhiteSpace(args[i + 1]))
            {
                return args[i + 1];
            }
        }

        return null;
    }

    /// <summary>決まったプロファイルを、サインインの前に反映する。</summary>
    private static void ApplyProfile()
    {
        if (string.IsNullOrWhiteSpace(autoProfile))
        {
            return;
        }

        if (AuthenticationService.Instance.IsSignedIn)
        {
            return;
        }

        AuthenticationService.Instance.SwitchProfile(autoProfile);
        Debug.Log($"[NET] プロファイルを「{autoProfile}」に切り替えました（別人として扱われます）");
    }

    /// <summary>失敗したときの後始末。理由を画面とログの両方に残す。</summary>
    private void Fail(string reason, Exception error)
    {
        State = Phase.Failed;
        Message = $"{reason}\n（{error.GetType().Name}）";

        Debug.LogError($"[NET] {reason}：{error}");
    }
}
