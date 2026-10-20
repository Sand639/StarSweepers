using System.Collections.Generic;
using System.IO;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// **ロビーの画面。** 誰が入っているか・どのチームかを並べて、ホストが試合を始める。
///
/// つなぐ操作そのものは既存の `LanConnectionUi`（LAN／インターネット／1人）が担当する。
/// このスクリプトは**つながったあと**の「待合室」だけを受け持つ。
///
/// **マップを選べるのもホストだけ。** 候補は、ビルドの一覧に入っている `FishingOnline` と
/// `FishingMap〜` で始まるシーン（`Tools > StarSweepers > 釣りの新しいマップを作る` で増やせる）。
/// 選んだマップの名前は参加者の画面にも出る。
///
/// **ゲーム開始を押せるのはホストだけ。**
/// 押すと、`NetworkManager` のシーン管理で**全員のシーンがまとめて切り替わる**。
/// 参加者が自分でシーンを読み込む必要はない。
///
/// これは通信の中身を確かめるための仮の表示で、
/// **本番のロビー画面はあとで作り直す**前提のもの（`ネットワークの制作工程.md` の工程3）。
/// </summary>
public class FishingLobbyUI : MonoBehaviour
{
    [Header("つなぎ先のシーン")]
    [Tooltip("最初に選ばれているマップのシーン名。**Build Settings に入っていないと読み込めない**")]
    [SerializeField] private string gameSceneName = "FishingOnline";

    [Tooltip("名前がこれで始まるシーンを、**マップの候補として自動で並べる**（ビルドの一覧に入っているものだけ）。" +
             "`Tools > StarSweepers > 釣りの新しいマップを作る` で作ったマップは FishingMap01 のような名前になる")]
    [SerializeField] private string mapScenePrefix = "FishingMap";

    /// <summary>ホストが選んでいるマップ。**ロビーへ戻っても覚えている**（NetworkManager ごと残るため）。</summary>
    private string selectedMap = string.Empty;

    [Header("表示")]
    [Tooltip("OFFにすると何も表示しない")]
    [SerializeField] private bool showUi = true;

    [Tooltip("文字の大きさ。**窓が小さいときは自動で縮む**ので、これは上限として使われる")]
    [Range(1f, 4f)]
    [SerializeField] private float uiScale = 1.5f;

    [Tooltip("最初に出す、画面の左からの位置（LanConnectionUi と重ならないようにずらす）。出たあとは帯をつかんで動かせる")]
    [SerializeField] private float panelX = 366f;

    /// <summary>パネルの幅（縮める前の基準）。</summary>
    private const float PanelWidth = 320f;

    /// <summary>うまくいかない理由を出すときの文字の色（薄い赤）。</summary>
    private static readonly Color WarningColor = new Color(1f, 0.5f, 0.4f);

    private GUIStyle labelStyle;

    /// <summary>「ゲーム開始」を押した結果。うまくいかなかった理由を画面に出すために持つ。</summary>
    private string startMessage = string.Empty;

    /// <summary>
    /// **帯をつかんで動かせる／「－」で折りたためる／窓が小さいと縮む**枠。
    /// 位置を決め打ちしていたころは、小さい窓で接続画面や通信の様子と重なって読めなかった。
    /// </summary>
    private DraggableGuiPanel panel;

    /// <summary>つなぐ画面。試合中だけ隠すために持っておく。</summary>
    private LanConnectionUi connectionUi;

    private void Awake()
    {
        connectionUi = GetComponent<LanConnectionUi>();
    }

    /// <summary>毎フレーム、つなぐ画面の出し入れと、（ホストなら）選んでいるマップの配り直しをする。</summary>
    private void Update()
    {
        // **試合が始まったら、つなぐ画面も隠す。**
        // NetworkManager はシーンをまたいで生き残るため、
        // 隠さないとプレイ画面に「ホストとして動作中／切断する」が出っぱなしになる。
        // 部品ごと止める（enabled = false）ので、OnGUI が呼ばれなくなる
        if (connectionUi != null)
        {
            connectionUi.enabled = FishingMatch.Current == null;
        }

        // ホストはロビーにいる間、選んでいるマップを全員へ配り続ける
        // （あとから入った人にも届くよう、値はホストのプレイヤーに持たせている）
        NetworkManager manager = NetworkManager.Singleton;
        if (manager != null && manager.IsServer && FishingMatch.Current == null)
        {
            RefreshSelectedMap();
        }
    }

    private void OnDisable()
    {
        // ロビーへ戻ったときに、つなぐ画面が消えたままにならないよう戻しておく
        if (connectionUi != null)
        {
            connectionUi.enabled = true;
        }
    }

    /// <summary>
    /// ロビーの枠を描く。**つながっていて、まだ釣り会場へ移っていないときだけ**出す。
    /// 中身は上から「ゲーム開始 → マップ → 入っている人 → ゴールの割り当て」。
    /// </summary>
    private void OnGUI()
    {
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

        // **釣り会場へ移ったあとは、この画面を出さない。**
        // NetworkManager はシーンをまたいで生き残るので、
        // 何もしないとゲーム中もロビーの画面が重なって出てしまう
        if (FishingMatch.Current != null)
        {
            return;
        }

        PrepareStyle();

        if (panel == null)
        {
            panel = new DraggableGuiPanel("ロビー", 0f, panelX, 12f, PanelWidth);
        }

        panel.Draw(uiScale, windowId =>
        {
            // **「ゲーム開始」を一番上に置く。**
            // 下に置くと、中身が増えたときに画面外へ押し出されて押せなくなる
            DrawStartButton(manager);

            GUILayout.Space(6f);

            DrawMapChoice(manager);

            GUILayout.Space(6f);

            DrawRoster();
            DrawTeamRule();
        });
    }

    /// <summary>文字のスタイルを初回だけ作る（`GUI.skin` は OnGUI の中でしか触れないため、Awake では作れない）。</summary>
    private void PrepareStyle()
    {
        if (labelStyle == null)
        {
            labelStyle = new GUIStyle(GUI.skin.label) { fontSize = 12, wordWrap = true };
        }
    }

    /// <summary>
    /// マップを選ぶ。**ホストはボタンで選べて、参加者には選ばれたマップの名前だけが出る。**
    /// 選んだ結果はホストのプレイヤーを通して全員に配られる（<see cref="FishingNetPlayer.HostSelectedMap"/>）。
    /// </summary>
    private void DrawMapChoice(NetworkManager manager)
    {
        GUILayout.Label("■ マップ", labelStyle);

        if (!manager.IsServer)
        {
            string hostMap = FishingNetPlayer.HostSelectedMap;
            GUILayout.Label(
                string.IsNullOrEmpty(hostMap) ? "　（ホストが選んでいます…）" : $"　{hostMap}（ホストが選びます）",
                labelStyle);
            return;
        }

        List<string> maps = GetAvailableMaps();

        if (maps.Count == 0)
        {
            GUILayout.Label("　遊べるマップがありません（ビルドの一覧を確認してください）", labelStyle);
            return;
        }

        foreach (string map in maps)
        {
            bool isSelected = map == selectedMap;

            // 選んでいるマップは押せない形にして、印を付ける
            GUI.enabled = !isSelected;
            if (GUILayout.Button(isSelected ? $"● {map}（選択中）" : map))
            {
                selectedMap = map;
                startMessage = string.Empty;
            }
            GUI.enabled = true;
        }
    }

    /// <summary>
    /// 選んでいるマップを正しい状態にし、全員へ配る。**ホストだけが呼ぶ。**
    /// 候補から消えたマップが選ばれたままにならないよう、毎回確かめている。
    /// </summary>
    private void RefreshSelectedMap()
    {
        List<string> maps = GetAvailableMaps();

        // 選んでいたマップが候補から消えていたら選び直す。
        // 優先順位：最初の会場 → 候補の先頭 → （候補が1つも無ければ）最初の会場の名前のまま
        if (!maps.Contains(selectedMap))
        {
            selectedMap = maps.Contains(gameSceneName) ? gameSceneName
                        : maps.Count > 0 ? maps[0]
                        : gameSceneName;
        }

        FishingNetPlayer.ServerSetSelectedMap(selectedMap);
    }

    /// <summary>
    /// 選べるマップの一覧。**ビルドの一覧に入っているシーンのうち、
    /// 最初の会場（<see cref="gameSceneName"/>）と、名前が <see cref="mapScenePrefix"/> で始まるもの。**
    /// ビルドの一覧に無いシーンは Netcode で読み込めないので、はじめから候補に出さない。
    /// </summary>
    private List<string> GetAvailableMaps()
    {
        List<string> maps = new List<string>();

        foreach (string name in SceneNamesInBuildList())
        {
            bool isMap = name == gameSceneName
                      || (!string.IsNullOrEmpty(mapScenePrefix) && name.StartsWith(mapScenePrefix));

            if (isMap && !maps.Contains(name))
            {
                maps.Add(name);
            }
        }

        return maps;
    }

    /// <summary>
    /// ビルドのシーン一覧に入っているシーンの名前（拡張子無し）を、並んでいる順に返す。
    /// エディタでは `File > Build Settings` の一覧がそのまま使われる。
    /// </summary>
    private static IEnumerable<string> SceneNamesInBuildList()
    {
        int count = UnityEngine.SceneManagement.SceneManager.sceneCountInBuildSettings;

        for (int i = 0; i < count; i++)
        {
            yield return Path.GetFileNameWithoutExtension(
                UnityEngine.SceneManagement.SceneUtility.GetScenePathByBuildIndex(i));
        }
    }

    /// <summary>入っている人を並べる。参加者のPCでも見えるように、生まれた本体の一覧を使う。</summary>
    private void DrawRoster()
    {
        int playerCount = FishingNetPlayer.All.Count;
        int teamCount = FishingTeams.TeamCountFor(playerCount);

        GUILayout.Label($"■ ロビー（{playerCount} / {FishingTeams.MaxPlayers} 人）", labelStyle);
        GUILayout.Space(4f);

        foreach (FishingNetPlayer player in FishingNetPlayer.All)
        {
            if (player == null)
            {
                continue;
            }

            string mark = player.IsOwner ? "▶ " : "　 ";
            Color saved = GUI.color;
            GUI.color = FishingTeams.TeamColor(player.TeamIndex);

            GUILayout.Label(
                $"{mark}{player.DisplayName}　（{FishingTeams.TeamName(player.TeamIndex)}）",
                labelStyle);

            GUI.color = saved;
        }

        GUILayout.Space(6f);
        GUILayout.Label($"いまの人数だと **{teamCount} チーム** に分かれます", labelStyle);
    }

    /// <summary>いまの人数でゴールがどう割り当てられるかを出す。</summary>
    private void DrawTeamRule()
    {
        int teamCount = FishingTeams.TeamCountFor(FishingNetPlayer.All.Count);
        int[] owners = FishingTeams.PocketOwners(teamCount);

        GUILayout.Space(6f);
        GUILayout.Label("■ ゴールの割り当て", labelStyle);

        for (int i = 0; i < owners.Length; i++)
        {
            Color saved = GUI.color;
            GUI.color = FishingTeams.TeamColor(owners[i]);

            GUILayout.Label(
                $"　{FishingTeams.PocketPlaceName(i)} … {FishingTeams.TeamName(owners[i])}",
                labelStyle);

            GUI.color = saved;
        }
    }

    /// <summary>
    /// ホストだけに「ゲーム開始」を出す。
    /// **パネルの一番上に置いている**（下だと中身が増えたときに押せなくなるため）。
    /// </summary>
    private void DrawStartButton(NetworkManager manager)
    {
        if (!manager.IsServer)
        {
            GUILayout.Label("ホストが始めるのを待っています…", labelStyle);
            return;
        }

        if (!manager.NetworkConfig.EnableSceneManagement)
        {
            GUILayout.Label(
                "NetworkManager の Enable Scene Management が OFF です。" +
                "ONにしないとシーンを切り替えられません。", labelStyle);
            return;
        }

        // **一番多い原因を先に出す。**
        // ビルドの一覧に入っていないシーンは、Netcodeでも読み込めない
        if (!IsSceneInBuildList(selectedMap))
        {
            GUI.color = WarningColor;
            GUILayout.Label(
                $"シーン「{selectedMap}」がビルドの一覧に入っていません。\n" +
                "File > Build Profiles を開いて、\n" +
                "FishingLobby と遊びたいマップのシーンを入れてください\n" +
                "（`釣りのマップをビルドの一覧に登録し直す` を実行すると自動で入ります）。",
                labelStyle);
            GUI.color = Color.white;
            return;
        }

        if (GUILayout.Button($"ゲーム開始（{selectedMap} へ全員で移動）"))
        {
            StartGame(manager);
        }

        // 押したあと、うまくいかなかった場合はその理由を画面に出す
        // （ビルドで動かしているときはConsoleが見られないため）
        if (!string.IsNullOrEmpty(startMessage))
        {
            GUI.color = WarningColor;
            GUILayout.Space(4f);
            GUILayout.Label(startMessage, labelStyle);
            GUI.color = Color.white;
        }
    }

    /// <summary>
    /// そのシーンが**ビルドのシーン一覧に入っているか**を調べる。
    ///
    /// 入っていないと、Netcode でシーンを切り替えられない
    /// （エディタでは `File > Build Settings` の一覧がそのまま使われる）。
    /// **移行できない原因のほとんどがこれ**なので、先に確かめて画面に出している。
    /// </summary>
    private static bool IsSceneInBuildList(string sceneName)
    {
        if (string.IsNullOrEmpty(sceneName))
        {
            return false;
        }

        foreach (string name in SceneNamesInBuildList())
        {
            if (name == sceneName)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 全員のシーンをまとめて切り替える。**ホストだけが呼べる。**
    /// 参加者のシーンは `NetworkManager` が自動で合わせてくれる。
    /// </summary>
    private void StartGame(NetworkManager manager)
    {
        if (string.IsNullOrEmpty(selectedMap))
        {
            Debug.LogError("移動先のシーン名が空です。", this);
            return;
        }

        if (manager.SceneManager == null)
        {
            startMessage = "SceneManager がまだ使えません（接続が完了していない可能性があります）。";
            Debug.LogError("[FISH] " + startMessage);
            return;
        }

        var status = manager.SceneManager.LoadScene(
            selectedMap, UnityEngine.SceneManagement.LoadSceneMode.Single);

        if (status == SceneEventProgressStatus.Started)
        {
            startMessage = string.Empty;
            Debug.Log($"[FISH] シーン「{selectedMap}」へ全員で移動します。");
            return;
        }

        startMessage = $"シーンを読み込めませんでした（{status}）。";

        Debug.LogError(
            $"[FISH] シーン「{selectedMap}」を読み込めませんでした（{status}）。\n" +
            "よくある原因：\n" +
            "・**File > Build Profiles のシーン一覧に、選んだマップが入っていない**\n" +
            "・NetworkManager の Enable Scene Management が OFF\n" +
            "・別のシーン切り替えがまだ終わっていない（SceneEventInProgress）");
    }
}
