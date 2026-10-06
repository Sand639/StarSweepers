using System.Collections.Generic;
using System.IO;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// **ロビーの画面。** 誰が入っているかを並べ、ホストが端末を開いたら詳細設定を出す。
///
/// つなぐ操作そのものは既存の `LanConnectionUi`（LAN／インターネット／1人）が担当する。
/// このスクリプトは**つながったあと**の「待合室」だけを受け持つ。
///
/// ## ふだん出ているもの
///
/// 参加者の一覧と、いまの設定の要約だけ。**設定そのものは出っぱなしにしない。**
///
/// ## 詳細設定（ホストだけ）
///
/// ロビーに置いた端末（<see cref="SpaceJunkLobbyTerminal"/>）に近づいて `E` を押すと開く。
/// 中でできること：
///
///   ・**チーム数を決める**（1〜4）
///   ・**誰をどのチームに入れるか**を、チームの枠のボタンで移す
///   ・**チーム数を指定してランダムに割り振る**
///   ・**何本先取か**（1〜5。初期値 2先）
///   ・**1ラウンドの最大時間**（初期値 60秒）
///   ・**この試合で使うマップ**をチェックで選ぶ（ラウンドごとにこの中からランダム）
///   ・**ゲーム開始**
///
/// 決めた中身は <see cref="SpaceJunkSession"/> が持ち、全員へ配られる。
///
/// フォントの素材を用意しなくても出せるように `OnGUI` で描いている
/// （既存の `NetworkStatusHud.cs` と同じ作り）。**本番の画面はあとで作り直す前提。**
/// </summary>
public class SpaceJunkLobbyUI : MonoBehaviour
{
    [Header("マップの候補")]
    [Tooltip("マップの一覧（SpaceJunkMapList）。**ここに入っていて「Show In Lobby」が付いたマップだけが候補に出る。**" +
             "`Tools > StarSweepers > 宇宙ごみのマップの一覧を開く` で自動で入る")]
    [SerializeField] private SpaceJunkMapList mapList;

    [Tooltip("マップの一覧が入っていないときだけ使う、昔の見分け方。" +
             "名前がこれで始まるシーンを候補に並べる")]
    [SerializeField] private string mapScenePrefix = "SpaceJunkMap";

    [Header("表示")]
    [Tooltip("OFFにすると何も表示しない")]
    [SerializeField] private bool showUi = true;

    [Tooltip("文字の大きさ。**窓が小さいときは自動で縮む**ので、これは上限として使われる")]
    [Range(1f, 4f)]
    [SerializeField] private float uiScale = 1.5f;

    [Tooltip("最初に出す、画面の左からの位置（LanConnectionUi と重ならないようにずらす）")]
    [SerializeField] private float panelX = 366f;

    /// <summary>設定の枠の幅（縮める前の基準）。</summary>
    private const float SettingsWidth = 620f;

    /// <summary>一覧の枠の幅。</summary>
    private const float RosterWidth = 320f;

    /// <summary>「この人を移す」で選んでいる人。<see cref="NoSelection"/> なら誰も選んでいない。</summary>
    private ulong selectedClientId = NoSelection;

    private const ulong NoSelection = ulong.MaxValue;

    /// <summary>詳細設定の巻物の位置。中身が画面に収まらないときに使う。</summary>
    private Vector2 settingsScroll;

    // ---- 1ラウンドの最大時間の入力欄 ----

    /// <summary>入力欄の名前。「いまここに打ち込んでいるか」を調べるのに使う。</summary>
    private const string RoundSecondsControl = "SpaceJunkRoundSeconds";

    private const int MinRoundSeconds = SpaceJunkSession.MinRoundSeconds;
    private const int MaxRoundSeconds = SpaceJunkSession.MaxRoundSeconds;

    /// <summary>入力欄に出している文字。打ち込んでいる最中は、決定するまでこちらを使う。</summary>
    private string roundSecondsText = string.Empty;

    /// <summary>入力が範囲の外だった、などのお知らせ。</summary>
    private string roundSecondsMessage = string.Empty;

    /// <summary>
    /// **いま入力欄に文字を打ち込んでいるか。**
    ///
    /// 打ち込み中に `E`（設定を閉じる）や `R`（位置を戻す）が効くと、
    /// 数字を入れようとしただけで設定が閉じたり、体が飛んだりする。
    /// そこで、キーを読む側（<see cref="SpaceJunkLobbyTerminal"/>・<see cref="SpaceJunkPlayerSetup"/>）が
    /// これを見て手を引く。
    /// </summary>
    public static bool IsEditingText { get; private set; }

    private GUIStyle labelStyle;
    private GUIStyle headerStyle;

    private DraggableGuiPanel panel;

    /// <summary>つなぐ画面。試合が始まったら隠すために持っておく。</summary>
    private LanConnectionUi connectionUi;

    /// <summary>「ゲーム開始」を押した結果。うまくいかなかった理由を画面に出すために持つ。</summary>
    private string startMessage = string.Empty;

    private void Awake()
    {
        connectionUi = GetComponent<LanConnectionUi>();
    }

    private void Update()
    {
        // **ラウンドが始まったら、つなぐ画面も隠す。**
        // NetworkManager はシーンをまたいで生き残るため、
        // 隠さないとプレイ画面に「ホストとして動作中／切断する」が出っぱなしになる
        if (connectionUi != null)
        {
            // タイトル画面が出ている間も隠す（タイトル画面からつなぐため。2026/10/6）
            connectionUi.enabled = SpaceJunkRound.Current == null && !TitleScreen.IsVisible;
        }
    }

    private void OnGUI()
    {
        // 「打ち込み中か」は毎回まず下ろし、入力欄を描いたときだけ立て直す。
        // **途中で return しても、古い「打ち込み中」が残らない**ようにするため
        // （残ると E や R がずっと効かなくなる）
        IsEditingText = false;

        if (!showUi)
        {
            return;
        }

        NetworkManager manager = NetworkManager.Singleton;

        // つながっていないときは、つなぐ画面（LanConnectionUi）に任せる
        if (manager == null || (!manager.IsClient && !manager.IsServer))
        {
            return;
        }

        // **マップへ移ったあとは、この画面を出さない。**
        if (SpaceJunkRound.Current != null)
        {
            return;
        }

        PrepareStyles();

        if (panel == null)
        {
            panel = new DraggableGuiPanel("ロビー", 0f, panelX, 12f, RosterWidth);
        }

        panel.Draw(uiScale, windowId =>
        {
            DrawRoster();
            GUILayout.Space(6f);
            DrawSummary();
            GUILayout.Space(6f);
            DrawTerminalHint(manager);
        });

        DrawSettingsWindow(manager);
    }

    private void PrepareStyles()
    {
        if (labelStyle == null)
        {
            labelStyle = new GUIStyle(GUI.skin.label) { fontSize = 12, wordWrap = true };
        }

        if (headerStyle == null)
        {
            headerStyle = new GUIStyle(GUI.skin.label) { fontSize = 13, fontStyle = FontStyle.Bold };
        }
    }

    // ------------------------------------------------------------
    // ふだん出ているもの
    // ------------------------------------------------------------

    /// <summary>入っている人を、チームの色つきで並べる。</summary>
    private void DrawRoster()
    {
        SpaceJunkSession session = SpaceJunkSession.Current;

        if (session == null)
        {
            GUILayout.Label("試合の係を待っています…", labelStyle);
            return;
        }

        IReadOnlyList<SpaceJunkPlayerSlot> slots = session.Slots;

        GUILayout.Label($"■ ロビー（{slots.Count} / {SpaceJunkTeams.MaxPlayers} 人）", headerStyle);
        GUILayout.Space(4f);

        foreach (SpaceJunkPlayerSlot slot in slots)
        {
            Color saved = GUI.color;
            GUI.color = SpaceJunkTeams.TeamColor(slot.Team);

            string mark = IsLocalPlayer(slot.ClientId) ? "▶ " : "　 ";
            GUILayout.Label($"{mark}{NameOf(slot.ClientId)}　（{SpaceJunkTeams.TeamName(slot.Team)}）", labelStyle);

            GUI.color = saved;
        }
    }

    /// <summary>いまの設定の要約。参加者にも見える。</summary>
    private void DrawSummary()
    {
        SpaceJunkSession session = SpaceJunkSession.Current;

        if (session == null)
        {
            return;
        }

        GUILayout.Label("■ いまの設定", headerStyle);
        GUILayout.Label($"　チーム数 … {session.TeamCount}", labelStyle);
        GUILayout.Label($"　{session.RoundsToWin} 本先取", labelStyle);
        GUILayout.Label($"　1ラウンド 最大 {Mathf.RoundToInt(session.RoundSeconds)} 秒", labelStyle);
        GUILayout.Label($"　使うマップ … {session.SelectedMapCount} 個", labelStyle);

        // **落ちて戻れなくなったときの案内。** 知らないと詰まるので常に出す
        if (!string.IsNullOrEmpty(SpaceJunkPlayerSetup.LocalResetKeyName))
        {
            GUILayout.Space(4f);
            GUILayout.Label($"落ちたら［{SpaceJunkPlayerSetup.LocalResetKeyName}］で最初の位置に戻れます", labelStyle);
        }
    }

    /// <summary>端末の案内。**ホストが端末のそばにいるときだけ出す。**</summary>
    private void DrawTerminalHint(NetworkManager manager)
    {
        if (!manager.IsServer)
        {
            GUILayout.Label("ホストが始めるのを待っています…", labelStyle);
            return;
        }

        SpaceJunkLobbyTerminal terminal = SpaceJunkLobbyTerminal.Current;

        if (terminal == null)
        {
            GUILayout.Label("設定端末がこのシーンにありません。", labelStyle);
            return;
        }

        if (terminal.IsOpen)
        {
            GUILayout.Label($"設定を開いています（もう一度［{terminal.InteractKeyName}］か「閉じる」で閉じます）", labelStyle);
            return;
        }

        GUILayout.Label(terminal.IsHostNearby
            ? $"［{terminal.InteractKeyName}］で詳細設定を開く"
            : "設定端末に近づくと、詳細設定を開けます", labelStyle);
    }

    // ------------------------------------------------------------
    // 詳細設定（ホストだけ）
    // ------------------------------------------------------------

    private void DrawSettingsWindow(NetworkManager manager)
    {
        SpaceJunkLobbyTerminal terminal = SpaceJunkLobbyTerminal.Current;
        SpaceJunkSession session = SpaceJunkSession.Current;

        if (terminal == null || !terminal.IsOpen || session == null || !manager.IsServer)
        {
            // **入力欄に打ち込んだまま設定が閉じられた場合に、入力欄から外しておく。**
            // 外さないと、次に開いたときに古い文字が残ったり、
            // 見えない入力欄がキーを受け取り続けたりする
            if (GUI.GetNameOfFocusedControl() == RoundSecondsControl)
            {
                GUIUtility.keyboardControl = 0;
            }

            roundSecondsMessage = string.Empty;
            return;
        }

        float scale = Mathf.Min(DraggableGuiPanel.FitScale(uiScale), Screen.width / (SettingsWidth + 40f));
        scale = Mathf.Max(0.5f, scale);

        Matrix4x4 saved = GUI.matrix;
        GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));

        float viewWidth = Screen.width / scale;
        float viewHeight = Screen.height / scale;
        Rect area = new Rect((viewWidth - SettingsWidth) * 0.5f, 24f, SettingsWidth, viewHeight - 48f);

        GUILayout.BeginArea(area, GUI.skin.window);

        GUILayout.BeginHorizontal();
        GUILayout.Label("■ 詳細設定（ホストだけが変えられます）", headerStyle);

        // **閉じるボタンも置いておく。** キーだけだと、
        // 閉じ方が分からなくなった人がその場から動けなくなる
        if (GUILayout.Button("閉じる", GUILayout.Width(80f)))
        {
            terminal.Close();
        }
        GUILayout.EndHorizontal();

        GUILayout.Space(6f);

        // **中身が増えても下のほうが押せるように、巻物（スクロール）にする。**
        // 人数が多い・マップが多いと、そのままでは「使うマップ」が画面の外へ出て選べなくなる
        // （申し送り 2026/9/14 の「小さい窓でボタンが押せなくなる」と同じ罠）
        settingsScroll = GUILayout.BeginScrollView(settingsScroll);

        DrawStartButton(session);
        GUILayout.Space(8f);

        DrawTeamCount(session);
        GUILayout.Space(6f);

        DrawTeamBoxes(session);
        GUILayout.Space(6f);

        DrawRoundsToWin(session);
        GUILayout.Space(4f);

        DrawRoundSeconds(session);
        GUILayout.Space(6f);

        DrawMapChoice(session);

        GUILayout.EndScrollView();
        GUILayout.EndArea();

        GUI.matrix = saved;
    }

    /// <summary>チーム数を決める。</summary>
    private void DrawTeamCount(SpaceJunkSession session)
    {
        GUILayout.Label("■ チーム数", headerStyle);

        GUILayout.BeginHorizontal();

        for (int i = 1; i <= SpaceJunkTeams.MaxTeams; i++)
        {
            bool isSelected = session.TeamCount == i;

            if (GUILayout.Button(isSelected ? $"● {i}" : $"{i}"))
            {
                session.ServerSetTeamCount(i);
            }
        }

        if (GUILayout.Button("この数でランダムに割り振る"))
        {
            session.ServerRandomAssign();
        }

        GUILayout.EndHorizontal();
    }

    /// <summary>
    /// **チームごとの枠。** 人を選んでから、入れたいチームの「ここへ入れる」を押す。
    /// </summary>
    private void DrawTeamBoxes(SpaceJunkSession session)
    {
        GUILayout.Label("■ チーム分け（人を選んでから、入れたいチームのボタンを押す）", headerStyle);

        // 誰を動かすかを先に選ぶ
        GUILayout.BeginHorizontal();
        foreach (SpaceJunkPlayerSlot slot in session.Slots)
        {
            bool isSelected = selectedClientId == slot.ClientId;

            Color saved = GUI.color;
            GUI.color = SpaceJunkTeams.TeamColor(slot.Team);

            if (GUILayout.Button(isSelected ? $"● {NameOf(slot.ClientId)}" : NameOf(slot.ClientId)))
            {
                selectedClientId = isSelected ? NoSelection : slot.ClientId;
            }

            GUI.color = saved;
        }
        GUILayout.EndHorizontal();

        GUILayout.Space(4f);

        // チームの枠を横に並べる
        GUILayout.BeginHorizontal();

        for (int team = 0; team < session.TeamCount; team++)
        {
            GUILayout.BeginVertical(GUI.skin.box);

            int members = session.PlayerCountOf(team);

            Color saved = GUI.color;
            GUI.color = SpaceJunkTeams.TeamColor(team);
            GUILayout.Label($"{SpaceJunkTeams.TeamName(team)}（{members}人）", headerStyle);
            GUI.color = saved;

            foreach (SpaceJunkPlayerSlot slot in session.Slots)
            {
                if (slot.Team == team)
                {
                    GUILayout.Label($"　{NameOf(slot.ClientId)}", labelStyle);
                }
            }

            if (members == 0)
            {
                // **無人のチームのゴールは、素材を入れても数えない。**
                // 知らないと「入れたのに増えない」と混乱するので、ここで伝える
                GUI.color = new Color(1f, 0.6f, 0.4f);
                GUILayout.Label("　（いません）\n　このチームのゴールは\n　数えません", labelStyle);
                GUI.color = saved;
            }

            GUI.enabled = selectedClientId != NoSelection;
            if (GUILayout.Button("ここへ入れる"))
            {
                session.ServerSetTeamOf(selectedClientId, team);
                selectedClientId = NoSelection;
            }
            GUI.enabled = true;

            GUILayout.EndVertical();
        }

        GUILayout.EndHorizontal();

        GUILayout.Label("　※ 人数の偏り（3人対1人など）も許しています。ホストの判断で決めてください。", labelStyle);
    }

    /// <summary>何本先取かを決める。</summary>
    private void DrawRoundsToWin(SpaceJunkSession session)
    {
        GUILayout.Label("■ 何本先取か", headerStyle);

        GUILayout.BeginHorizontal();

        for (int i = 1; i <= 5; i++)
        {
            bool isSelected = session.RoundsToWin == i;

            if (GUILayout.Button(isSelected ? $"● {i} 本先取" : $"{i} 本先取"))
            {
                session.ServerSetRoundsToWin(i);
            }
        }

        GUILayout.EndHorizontal();
    }

    /// <summary>1ラウンドの最大時間を決める。</summary>
    /// <summary>
    /// 1ラウンドの最大時間を**数字で打ち込んで**決める（2026/9/22・大槻さんの依頼で、選ぶ形から変更）。
    ///
    /// 打ち込んだだけでは変わらない。**「決定」か Enter で反映する。**
    /// 1文字打つたびに反映すると、「120」と打つ途中の「1」「12」が一瞬入ってしまうため。
    /// </summary>
    private void DrawRoundSeconds(SpaceJunkSession session)
    {
        GUILayout.Label($"■ 1ラウンドの最大時間（{MinRoundSeconds}〜{MaxRoundSeconds} 秒）", headerStyle);

        int current = Mathf.RoundToInt(session.RoundSeconds);
        bool focused = GUI.GetNameOfFocusedControl() == RoundSecondsControl;

        // キーを読む側に「いま打ち込み中」と知らせる
        IsEditingText = focused;

        // 打ち込んでいる最中は、手元の文字を上書きしない
        if (!focused)
        {
            roundSecondsText = current.ToString();
        }

        // **Enter で決定。** 入力欄が先に Enter を受け取る前に見ておく
        Event e = Event.current;
        bool enterPressed = focused
                         && e.type == EventType.KeyDown
                         && (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter);

        if (enterPressed)
        {
            e.Use();
        }

        GUILayout.BeginHorizontal();

        GUI.SetNextControlName(RoundSecondsControl);
        string edited = GUILayout.TextField(roundSecondsText, 3, GUILayout.Width(80f));

        // **数字以外は受け付けない**
        roundSecondsText = KeepDigits(edited);

        GUILayout.Label("秒", labelStyle, GUILayout.Width(24f));

        if (GUILayout.Button("決定", GUILayout.Width(70f)) || enterPressed)
        {
            ApplyRoundSeconds(session);
        }

        GUILayout.Label($"いま：{current} 秒", labelStyle);

        GUILayout.EndHorizontal();

        if (!string.IsNullOrEmpty(roundSecondsMessage))
        {
            GUI.color = new Color(1f, 0.6f, 0.4f);
            GUILayout.Label("　" + roundSecondsMessage, labelStyle);
            GUI.color = Color.white;
        }

        GUILayout.Label("　※ 打ち込んだら「決定」か Enter で反映します。", labelStyle);
        GUILayout.Label(session.WinRule == SpaceJunkWinRule.Score
            ? $"　※ 時間いっぱい遊び、得点の高いチームがラウンドを取ります（1個 {SpaceJunkRound.PointPerItem} 点。{session.Events.startSeconds:0} 秒たつとイベントが起きます）。"
            : "　※ 3種類そろえたチームが出たら、その時点でラウンドは終わります。", labelStyle);
    }

    /// <summary>打ち込んだ秒数を確かめて、ホストの設定に入れる。</summary>
    private void ApplyRoundSeconds(SpaceJunkSession session)
    {
        if (!int.TryParse(roundSecondsText, out int seconds))
        {
            roundSecondsMessage = "数字を入れてください。";
            return;
        }

        int clamped = Mathf.Clamp(seconds, MinRoundSeconds, MaxRoundSeconds);

        // 範囲の外だったら、直した値を入れたうえで知らせる（黙って変えると気づけない）
        roundSecondsMessage = clamped != seconds
            ? $"{MinRoundSeconds}〜{MaxRoundSeconds} 秒の間にしてください。{clamped} 秒にしました。"
            : string.Empty;

        session.ServerSetRoundSeconds(clamped);

        roundSecondsText = clamped.ToString();

        // 入力欄から外す（外さないと、E や R が打ち込み扱いのまま効かない）
        GUIUtility.keyboardControl = 0;
    }

    /// <summary>数字だけを残す。</summary>
    private static string KeepDigits(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        System.Text.StringBuilder digits = new System.Text.StringBuilder(text.Length);

        foreach (char c in text)
        {
            if (c >= '0' && c <= '9')
            {
                digits.Append(c);
            }
        }

        return digits.ToString();
    }

    /// <summary>この試合で使うマップを選ぶ。**ラウンドごとに、この中からランダムに選ばれる。**</summary>
    private void DrawMapChoice(SpaceJunkSession session)
    {
        GUILayout.Label("■ 使うマップ（この中からラウンドごとにランダム）", headerStyle);

        List<KeyValuePair<string, string>> maps = GetAvailableMaps(session.TeamCount, out int hiddenForTeams);

        // **いまのチーム数で遊べないマップは、選んであっても外す**（チーム数をあとから増やしたとき用。2026/9/30）。
        // 残しておくと、ゴールを持てないチームが出るマップでラウンドが始まってしまう
        PruneUnplayableSelection(session, maps);

        if (hiddenForTeams > 0)
        {
            GUI.color = new Color(0.8f, 0.8f, 0.85f);
            GUILayout.Label($"　※ {session.TeamCount} チームでは遊べないマップ（ゴールが足りない）を {hiddenForTeams} 個隠しています。", labelStyle);
            GUI.color = Color.white;
        }

        if (maps.Count == 0)
        {
            GUI.color = new Color(1f, 0.5f, 0.4f);
            GUILayout.Label(
                mapList != null
                    ? "マップの一覧に、ロビーに出すマップが1つもありません。\n" +
                      "Unity の `Tools > StarSweepers > 宇宙ごみのマップの一覧を開く` で、マップを足すか「Show In Lobby」を付けてください。"
                    : $"「{mapScenePrefix}〜」という名前のシーンが、ビルドの一覧に入っていません。\n" +
                      "Unity の `Tools > StarSweepers > 宇宙ごみのマップの一覧を開く` を実行してください。",
                labelStyle);
            GUI.color = Color.white;
            return;
        }

        // Key＝シーンの名前（切り替えに使う）、Value＝ロビーに出す名前
        foreach (KeyValuePair<string, string> map in maps)
        {
            bool isSelected = session.IsMapSelected(map.Key);

            if (GUILayout.Button(isSelected ? $"☑ {map.Value}" : $"☐ {map.Value}"))
            {
                session.ServerToggleMap(map.Key);
            }
        }

        if (session.SelectedMapCount > 1)
        {
            GUILayout.Label("　※ 直前と同じマップは選ばれません。", labelStyle);
        }
    }

    /// <summary>
    /// 選んであるマップのうち、**いまの候補（チーム数で遊べるもの）に無いもの**を外す。
    /// 一覧が無いとき（昔の拾い方）は、ゴールの控えが無いので何もしない。
    /// </summary>
    private void PruneUnplayableSelection(SpaceJunkSession session, List<KeyValuePair<string, string>> available)
    {
        if (mapList == null || session.SelectedMapCount == 0)
        {
            return;
        }

        HashSet<string> playable = new HashSet<string>();
        foreach (KeyValuePair<string, string> map in available)
        {
            playable.Add(map.Key);
        }

        // SelectedMaps は使い回しの入れ物なので、先に写してから外す
        List<string> selected = new List<string>(session.SelectedMaps);

        foreach (string map in selected)
        {
            SpaceJunkMapList.Entry entry = mapList.Find(map);

            // 一覧に無い・ロビーに出さないマップは、ここでは触らない（遊べるかどうかだけを見る）
            if (entry != null && !entry.SupportsTeamCount(session.TeamCount) && !playable.Contains(map))
            {
                session.ServerToggleMap(map);
                Debug.Log($"[JUNK] {map} は {session.TeamCount} チームでは遊べない（ゴールが足りない）ので、使うマップから外しました。");
            }
        }
    }

    /// <summary>ゲーム開始。**押せるのはホストだけ。**</summary>
    private void DrawStartButton(SpaceJunkSession session)
    {
        NetworkManager manager = NetworkManager.Singleton;

        if (!manager.NetworkConfig.EnableSceneManagement)
        {
            GUI.color = new Color(1f, 0.5f, 0.4f);
            GUILayout.Label(
                "NetworkManager の Enable Scene Management が OFF です。" +
                "ONにしないとシーンを切り替えられません。", labelStyle);
            GUI.color = Color.white;
            return;
        }

        if (session.SelectedMapCount == 0)
        {
            GUI.color = new Color(1f, 0.5f, 0.4f);
            GUILayout.Label("使うマップを1つ以上選んでください。", labelStyle);
            GUI.color = Color.white;
            return;
        }

        // **一番多い原因を先に出す。** ビルドの一覧に入っていないシーンは読み込めない
        foreach (string map in session.SelectedMaps)
        {
            if (!IsSceneInBuildList(map))
            {
                GUI.color = new Color(1f, 0.5f, 0.4f);
                GUILayout.Label(
                    $"シーン「{map}」がビルドの一覧に入っていません。\n" +
                    "File > Build Profiles を開いて入れてください。", labelStyle);
                GUI.color = Color.white;
                return;
            }
        }

        if (GUILayout.Button($"ゲーム開始（{session.RoundsToWin} 本先取）"))
        {
            session.ServerStartMatch();
            startMessage = string.Empty;
        }

        if (!string.IsNullOrEmpty(startMessage))
        {
            GUI.color = new Color(1f, 0.5f, 0.4f);
            GUILayout.Label(startMessage, labelStyle);
            GUI.color = Color.white;
        }
    }

    // ------------------------------------------------------------
    // 細かい道具
    // ------------------------------------------------------------

    /// <summary>その接続番号の人の、画面に出す名前。</summary>
    private static string NameOf(ulong clientId)
    {
        // タイトル画面で入れた名前があればそれ（2026/10/6）。無ければ「プレイヤー1」など
        if (SpaceJunkSession.Current != null)
        {
            return SpaceJunkSession.Current.NameOf(clientId);
        }

        foreach (FishingNetPlayer player in FishingNetPlayer.All)
        {
            if (player != null && player.OwnerClientId == clientId)
            {
                return player.DisplayName;
            }
        }

        return $"参加者 {clientId}";
    }

    /// <summary>その人が、このPCで操作している人か。</summary>
    private static bool IsLocalPlayer(ulong clientId)
    {
        return NetworkManager.Singleton != null && NetworkManager.Singleton.LocalClientId == clientId;
    }

    /// <summary>
    /// **ロビーに出すマップの候補を拾う。** Key＝シーンの名前、Value＝ロビーに出す名前。
    ///
    /// マップの一覧（<see cref="SpaceJunkMapList"/>）があれば、**そこに入っていて
    /// 「ロビーに出す」が付いたもの**だけを、一覧の順に並べる。
    /// ビルドの一覧に入っていないシーンは、選んでも切り替えられないので出さない。
    ///
    /// 一覧が無いときだけ、昔どおり「名前が `SpaceJunkMap` で始まるシーン」を拾う。
    /// </summary>
    private List<KeyValuePair<string, string>> GetAvailableMaps(int teamCount, out int hiddenForTeams)
    {
        List<KeyValuePair<string, string>> maps = new List<KeyValuePair<string, string>>();
        hiddenForTeams = 0;

        if (mapList != null)
        {
            foreach (SpaceJunkMapList.Entry entry in mapList.Maps)
            {
                if (entry == null || !entry.IsValid || !entry.showInLobby || !IsSceneInBuildList(entry.SceneName))
                {
                    continue;
                }

                // **いまのチーム数で、全チームが自分のゴールを持てないマップは出さない**（2026/9/30・大槻さん）
                if (!entry.SupportsTeamCount(teamCount))
                {
                    hiddenForTeams++;
                    continue;
                }

                // 4チームまで遊べないマップは、何チームまでかを名前の横に出す
                string label = entry.MaxTeams < SpaceJunkTeams.MaxTeams
                    ? $"{entry.Label}（{entry.MaxTeams}チームまで）"
                    : entry.Label;

                maps.Add(new KeyValuePair<string, string>(entry.SceneName, label));
            }

            return maps;
        }

        int count = UnityEngine.SceneManagement.SceneManager.sceneCountInBuildSettings;

        for (int i = 0; i < count; i++)
        {
            string path = UnityEngine.SceneManagement.SceneUtility.GetScenePathByBuildIndex(i);
            string name = Path.GetFileNameWithoutExtension(path);

            if (!string.IsNullOrEmpty(mapScenePrefix) && name.StartsWith(mapScenePrefix))
            {
                maps.Add(new KeyValuePair<string, string>(name, name));
            }
        }

        return maps;
    }

    /// <summary>そのシーンが**ビルドのシーン一覧に入っているか**。入っていないと切り替えられない。</summary>
    private static bool IsSceneInBuildList(string sceneName)
    {
        if (string.IsNullOrEmpty(sceneName))
        {
            return false;
        }

        int count = UnityEngine.SceneManagement.SceneManager.sceneCountInBuildSettings;

        for (int i = 0; i < count; i++)
        {
            string path = UnityEngine.SceneManagement.SceneUtility.GetScenePathByBuildIndex(i);

            if (Path.GetFileNameWithoutExtension(path) == sceneName)
            {
                return true;
            }
        }

        return false;
    }
}
