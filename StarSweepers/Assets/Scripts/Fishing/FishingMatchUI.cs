using Unity.Netcode;
using UnityEngine;

/// <summary>
/// **試合の残り時間と、決着したときの結果を出す画面。**
///
/// 対戦中 … 画面上に**残り時間**（目標点で戦う設定なら「◯点先取」）
/// 決着後 … 画面中央に**結果**（チームごとの最終得点と、勝ったチーム）と、
///          **ホストだけに「ロビーへ戻る」**
///
/// 数える／勝敗を決めるのは <see cref="FishingMatch"/>（ホスト）。
/// こちらはその値を出すだけで、判定は一切していない。
///
/// フォントの素材を用意しなくても出せるように `OnGUI` で描いている
/// （既存の `NetworkStatusHud.cs` と同じ作り）。**本番の画面はあとで作り直す前提。**
/// </summary>
public class FishingMatchUI : MonoBehaviour
{
    [Header("戻り先のシーン")]
    [Tooltip("「ロビーへ戻る」で全員が移動するシーンの名前。**Build Settings に入っていること**")]
    [SerializeField] private string lobbySceneName = "FishingLobby";

    [Header("表示")]
    [Tooltip("OFFにすると何も表示しない")]
    [SerializeField] private bool showUi = true;

    [Tooltip("文字の大きさ。**窓が小さいときは自動で縮む**ので、これは上限として使われる")]
    [Range(1f, 4f)]
    [SerializeField] private float uiScale = 1.6f;

    [Tooltip("残り時間がこの秒数を切ったら赤くする")]
    [SerializeField] private float warnSeconds = 30f;

    private GUIStyle timerStyle;
    private GUIStyle resultStyle;
    private GUIStyle lineStyle;

    /// <summary>今フレーム、実際に使っている縮小率（窓の大きさに合わせて変わる）。</summary>
    private float activeScale = 1.6f;

    private void OnGUI()
    {
        if (!showUi || FishingMatch.Current == null)
        {
            return;
        }

        PrepareStyles();

        // **窓が小さいときは自動で縮める。**
        // 4つ並べて起動すると1つの窓が小さくなり、
        // そのままだと結果画面の「ロビーへ戻る」が画面の外へ出てしまう
        // （他の表示と同じ割合で縮むよう、DraggableGuiPanel.FitScale にそろえている）
        //
        // 520×330 は、結果画面（勝敗＋チーム4行＋ボタン）が等倍で収まる窓の大きさの目安（ピクセル）。
        // 窓がそれより小さいぶんだけ縮め、ただし 0.6 倍より小さくはしない（読めなくなるため）
        float scale = Mathf.Min(DraggableGuiPanel.FitScale(uiScale), Screen.width / 520f, Screen.height / 330f);
        scale = Mathf.Max(0.6f, scale);

        // GUI 全体を scale 倍にして描く。描き終わったら元の行列に戻す（他の OnGUI に影響させない）
        Matrix4x4 savedMatrix = GUI.matrix;
        GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));

        activeScale = scale;

        // 縮めたぶん、描ける幅は広くなる（scale 倍した座標系での画面の幅）
        float width = Screen.width / scale;
        FishingMatch match = FishingMatch.Current;

        if (match.IsPlaying)
        {
            DrawTimer(match, width);
        }
        else
        {
            DrawResult(match, width);
        }

        GUI.matrix = savedMatrix;
    }

    /// <summary>
    /// 文字の見た目を最初の1回だけ作る。
    /// `GUI.skin` は OnGUI の中でしか触れないので、Awake ではなくここで作っている。
    /// </summary>
    private void PrepareStyles()
    {
        if (timerStyle == null)
        {
            timerStyle = MakeCenteredLabel(26, bold: true);
        }

        if (resultStyle == null)
        {
            resultStyle = MakeCenteredLabel(30, bold: true);
        }

        if (lineStyle == null)
        {
            lineStyle = MakeCenteredLabel(16, bold: false);
        }
    }

    /// <summary>中央ぞろえのラベルの見た目を作る。</summary>
    private static GUIStyle MakeCenteredLabel(int fontSize, bool bold)
    {
        GUIStyle style = new GUIStyle(GUI.skin.label)
        {
            fontSize = fontSize,
            alignment = TextAnchor.MiddleCenter
        };

        if (bold)
        {
            style.fontStyle = FontStyle.Bold;
        }

        return style;
    }

    // ------------------------------------------------------------
    // 対戦中
    // ------------------------------------------------------------

    /// <summary>画面の上端中央に、残り時間（または「◯点先取」）を出す。</summary>
    private void DrawTimer(FishingMatch match, float width)
    {
        // 上端から 4px、高さ 32px の帯に、画面の幅いっぱいで中央ぞろえ
        if (match.EndRule == MatchEndRule.TargetScore)
        {
            GUI.color = Color.white;
            GUI.Label(new Rect(0f, 4f, width, 32f), $"{match.TargetScore} 点先取", timerStyle);
            return;
        }

        float remaining = match.RemainingSeconds;
        int minutes = Mathf.FloorToInt(remaining / 60f);
        int seconds = Mathf.FloorToInt(remaining % 60f);

        // 残りわずかは赤
        GUI.color = remaining <= warnSeconds
            ? new Color(1f, 0.3f, 0.25f)
            : Color.white;

        GUI.Label(new Rect(0f, 4f, width, 32f), $"{minutes}:{seconds:00}", timerStyle);

        // GUI.color は次の描画にも残るので、必ず白に戻す
        GUI.color = Color.white;
    }

    // ------------------------------------------------------------
    // 決着後
    // ------------------------------------------------------------

    /// <summary>画面の中央あたりに、勝敗・チームごとの得点・「ロビーへ戻る」を上から順に並べる。</summary>
    private void DrawResult(FishingMatch match, float width)
    {
        // y は「次に描く行の上端」。描くたびに行の高さぶん下へずらしていく
        float height = Screen.height / activeScale;
        float y = height * 0.20f; // 画面の上から 2 割の位置から始める

        // 勝ったチーム（引き分けもある）
        if (match.WinnerTeam < 0)
        {
            GUI.color = Color.white;
            GUI.Label(new Rect(0f, y, width, 40f), "引き分け", resultStyle);
        }
        else
        {
            GUI.color = FishingTeams.TeamColor(match.WinnerTeam);
            GUI.Label(new Rect(0f, y, width, 40f),
                $"{FishingTeams.TeamName(match.WinnerTeam)} の勝ち！", resultStyle);
        }

        y += 46f; // 勝敗の行（高さ 40）＋すき間

        // チームごとの最終得点（1チーム1行、行の高さ 22）
        for (int team = 0; team < match.TeamCount; team++)
        {
            GUI.color = FishingTeams.TeamColor(team);
            GUI.Label(new Rect(0f, y, width, 22f),
                $"{FishingTeams.TeamName(team)}　{match.ScoreOf(team)} 点", lineStyle);
            y += 22f;
        }

        GUI.color = Color.white;
        y += 14f; // 得点とボタンの間のすき間

        DrawReturnButton(width, y);
    }

    /// <summary>ホストだけに「ロビーへ戻る」を出す。</summary>
    private void DrawReturnButton(float width, float y)
    {
        NetworkManager manager = NetworkManager.Singleton;

        if (manager == null || !manager.IsServer)
        {
            GUI.Label(new Rect(0f, y, width, 22f), "ホストがロビーへ戻すのを待っています…", lineStyle);
            return;
        }

        float buttonWidth = 260f;
        Rect rect = new Rect((width - buttonWidth) * 0.5f, y, buttonWidth, 30f);

        if (GUI.Button(rect, "ロビーへ戻る（全員で移動）"))
        {
            ReturnToLobby(manager);
        }
    }

    /// <summary>
    /// 全員のシーンをロビーへ戻す。**ホストだけが呼べる。**
    /// 戻ると点数はリセットされ、もう一度「ゲーム開始」で遊べる。
    /// </summary>
    private void ReturnToLobby(NetworkManager manager)
    {
        if (string.IsNullOrEmpty(lobbySceneName))
        {
            Debug.LogError("戻り先のシーン名が空です。", this);
            return;
        }

        var status = manager.SceneManager.LoadScene(
            lobbySceneName, UnityEngine.SceneManagement.LoadSceneMode.Single);

        if (status != SceneEventProgressStatus.Started)
        {
            Debug.LogError(
                $"[FISH] シーン「{lobbySceneName}」を読み込めませんでした（{status}）。\n" +
                "**File > Build Settings のシーン一覧に、ロビーと会場の両方が入っているか**確認してください。");
        }
    }
}
