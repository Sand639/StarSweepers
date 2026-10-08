using System;
using System.Collections.Generic;
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

    /// <summary>
    /// **ホストが部屋に付けたパスワード。** 空ならパスワードなし（2026/10/6・大槻さん）。
    /// ホストのPCでだけ分かる（中継サーバーからは読めないため、作ったときに控えておく）。
    /// </summary>
    public string CurrentPassword { get; private set; } = string.Empty;

    /// <summary>いまの部屋にパスワードが付いているか（ホストのPCで使う）。</summary>
    public bool HasPassword => !string.IsNullOrEmpty(CurrentPassword);

    /// <summary>いまの部屋の名前・人数・最大人数（Steam のフレンドに見せるのに使う。部屋に入っていなければ空・0）。</summary>
    public string RoomName => session != null ? session.Name : string.Empty;
    public int RoomPlayers => session != null ? session.PlayerCount : 0;
    public int RoomMaxPlayers => session != null ? session.MaxPlayers : 0;

    /// <summary>
    /// **パスワードの文字数**（遊ぶ人が入れる文字数。2026/10/6・大槻さんの決まり：1〜32文字）。
    /// 中継サーバーは 8〜64 文字しか受け付けないので、8文字に足りないときは<see cref="ToSessionPassword"/> で後ろに a を足して渡す。
    /// </summary>
    public const int PasswordMinLength = 1;
    public const int PasswordMaxLength = 32;

    /// <summary>入力欄に打てる文字数。32文字を超えたことを知らせられるよう、少し多めに打てるようにしておく。</summary>
    public const int PasswordInputLimit = 64;

    /// <summary>中継サーバーが受け付ける、いちばん短いパスワード。</summary>
    private const int SessionPasswordMinLength = 8;

    /// <summary>32文字を超えたときに出す文。</summary>
    public const string PasswordTooLongMessage = "パスワードは32文字までです";

    /// <summary>次に作る部屋のパスワード（空ならなし）。</summary>
    private string password = string.Empty;

    /// <summary>
    /// パスワードとして使えない理由。使えるなら null。**空は「パスワードなし」なので使える。**
    /// </summary>
    public static string PasswordProblem(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return null;
        }

        return value.Length > PasswordMaxLength ? PasswordTooLongMessage : null;
    }

    /// <summary>
    /// **中継サーバーに渡すパスワードにする。** 8文字に足りなければ、足りない数だけ後ろに「a」を足す
    /// （例：「B」→「Baaaaaaa」）。作る側も入る側も同じ足し方をするので、遊ぶ人は「B」と入れるだけでよい。空なら null（パスワードなし）。
    /// </summary>
    private static string ToSessionPassword(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return null;
        }

        return value.Length < SessionPasswordMinLength
            ? value + new string('a', SessionPasswordMinLength - value.Length)
            : value;
    }

    /// <summary>入る側のパスワードの設定（空なら付けない）。</summary>
    private static JoinSessionOptions JoinOptions(string joinPassword)
    {
        return string.IsNullOrEmpty(joinPassword) ? null : new JoinSessionOptions { Password = ToSessionPassword(joinPassword) };
    }

    /// <summary>
    /// **部屋のパスワードを変える**（ホストだけ。ロビーのゲーム設定から呼ぶ）。
    /// うまくいけば null、だめなら理由を返す。パスワードなしで作った部屋には付けられない（付け外しは部屋を作るときに決める）。
    /// </summary>
    public async Task<string> ChangePasswordAsync(string newPassword)
    {
        if (session == null || !session.IsHost || State != Phase.Connected)
        {
            return "インターネットの部屋のホストのときだけ変えられます。";
        }

        if (!HasPassword)
        {
            return "パスワードなしで作った部屋なので、パスワードは付けられません。";
        }

        if (string.IsNullOrEmpty(newPassword))
        {
            return "新しいパスワードを入れてください。";
        }

        string problem = PasswordProblem(newPassword);
        if (problem != null)
        {
            return problem;
        }

        try
        {
            IHostSession host = session.AsHost();
            host.Password = ToSessionPassword(newPassword);
            await host.SavePropertiesAsync();
            CurrentPassword = newPassword;
            Debug.Log("[NET] 部屋のパスワードを変えました。");
            return null;
        }
        catch (Exception error)
        {
            Debug.LogWarning($"[NET] パスワードを変えられませんでした：{error.Message}");
            return "パスワードを変えられませんでした（通信を確かめてください）。";
        }
    }

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

        // パスワードは 32 文字まで（空ならなし）。だめなら作る前に止める
        string passwordProblem = PasswordProblem(password);
        if (passwordProblem != null)
        {
            State = Phase.Failed;
            Message = passwordProblem;
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
                Password = ToSessionPassword(password),
            }.WithRelayNetwork(string.IsNullOrWhiteSpace(region) ? null : region.Trim());

            // 部屋ができると、通信の開始（ホストとしての待ち受け）まで自動で行われる
            session = await MultiplayerService.Instance.CreateSessionAsync(options);

            CurrentPassword = password;
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

    /// <summary>合言葉を使って、他の人の部屋に入る。パスワードの付いた部屋なら <paramref name="joinPassword"/> も要る。</summary>
    public async void JoinGame(string code, string joinPassword = "")
    {
        if (IsBusy)
        {
            return;
        }

        if (PasswordProblem(joinPassword) != null)
        {
            State = Phase.Failed;
            Message = PasswordProblem(joinPassword);
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
            session = await MultiplayerService.Instance.JoinSessionByCodeAsync(code.Trim(), JoinOptions(joinPassword));

            CurrentPassword = string.Empty;
            JoinCode = session.Code;
            State = Phase.Connected;
            Message = "つながりました。";

            Debug.Log($"[NET] 部屋に入りました。合言葉：{JoinCode}");
        }
        catch (Exception error)
        {
            Fail("部屋に入れませんでした。合言葉かパスワードが違うか、部屋が閉じられています", error);
        }
    }

    // ------------------------------------------------------------
    // タイトル画面から使う入口（2026/10/6）
    // ------------------------------------------------------------

    /// <summary>
    /// **部屋の名前・最大人数・非公開かを決めて、部屋を作る。** タイトル画面の「サーバーを作成」から呼ぶ。
    /// 非公開にすると、パブリックサーバーの一覧に出ない（合言葉を知っている人だけが入れる）。
    /// </summary>
    public void HostGame(string roomName, int players, bool privateRoom, string roomPassword = "")
    {
        if (IsBusy)
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(roomName))
        {
            sessionName = roomName.Trim();
        }

        maxPlayers = Mathf.Clamp(players, 2, 8);
        isPrivate = privateRoom;
        password = roomPassword ?? string.Empty;
        HostGame();
    }

    /// <summary>
    /// **公開されている部屋の一覧を取る。** タイトル画面の「パブリックサーバーを探す」から呼ぶ。
    /// 失敗したときは null（理由は <see cref="Message"/>）。
    /// </summary>
    public async Task<List<ISessionInfo>> QueryPublicSessionsAsync()
    {
        if (IsBusy)
        {
            return null;
        }

        if (!await PrepareAsync())
        {
            return null;
        }

        Message = "サーバーを探しています…";

        try
        {
            QuerySessionsResults results = await MultiplayerService.Instance.QuerySessionsAsync(new QuerySessionsOptions());
            State = Phase.Idle;
            Message = string.Empty;
            return results != null && results.Sessions != null
                ? new List<ISessionInfo>(results.Sessions)
                : new List<ISessionInfo>();
        }
        catch (Exception error)
        {
            Fail("サーバーの一覧を取れませんでした", error);
            return null;
        }
    }

    /// <summary>**一覧で選んだ部屋に入る。** タイトル画面のパブリックサーバーの一覧から呼ぶ。</summary>
    public async void JoinGameById(string sessionId, string joinPassword = "")
    {
        if (IsBusy || string.IsNullOrEmpty(sessionId))
        {
            return;
        }

        if (PasswordProblem(joinPassword) != null)
        {
            State = Phase.Failed;
            Message = PasswordProblem(joinPassword);
            return;
        }

        if (!await PrepareAsync())
        {
            return;
        }

        // 前の部屋から抜け損ねていたら、先に抜ける
        await LeaveStaleSessionsAsync();

        State = Phase.Joining;
        Message = "部屋に入ろうとしています…";

        try
        {
            session = await MultiplayerService.Instance.JoinSessionByIdAsync(sessionId, JoinOptions(joinPassword));

            CurrentPassword = string.Empty;
            JoinCode = session.Code;
            State = Phase.Connected;
            Message = "つながりました。";

            Debug.Log($"[NET] 一覧から部屋に入りました。合言葉：{JoinCode}");
        }
        catch (Exception error)
        {
            Fail("部屋に入れませんでした。パスワードが違うか、満員か、部屋が閉じられています", error);
        }
    }

    /// <summary>失敗の表示を消して、何もしていない状態に戻す（タイトル画面で「戻る」を押したとき）。</summary>
    public void ClearFailure()
    {
        if (State == Phase.Failed)
        {
            State = Phase.Idle;
            Message = string.Empty;
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
            CurrentPassword = string.Empty;
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
