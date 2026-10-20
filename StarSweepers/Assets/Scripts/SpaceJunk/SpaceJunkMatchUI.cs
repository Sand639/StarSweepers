using Unity.Netcode;
using UnityEngine;

/// <summary>
/// **ラウンド中の画面。** マップ（ステージ）のシーンに1つ置く。
///
/// 出しているもの：
///
///   ・**いま何ラウンド目か**と、何本先取か
///   ・**残り時間**（残り10秒を切ると赤くなる）
///   ・得点制 … **チームごとの得点**と、**イベントの進み具合**（デブリセットなら、お題のデブリの印）
///   ・**イベントの始まる前のカウントダウンと、始まった合図**
///   ・3種類ルール … **チームごとに、どの素材をそろえたか**（3種類ぶんの印）
///   ・**チームごとのラウンドの勝ち数**
///   ・決着後 … **このラウンドを取ったチーム**（引き分けならその旨）
///   ・試合が終わったら … **試合に勝ったチーム**
///
/// 数える／勝敗を決めるのは <see cref="SpaceJunkRound"/> と <see cref="SpaceJunkSession"/>（ホスト）。
/// こちらはその値を出すだけで、判定は一切していない。
///
/// フォントの素材を用意しなくても出せるように `OnGUI` で描いている。
/// **本番の画面はあとで作り直す前提。**
/// </summary>
public class SpaceJunkMatchUI : MonoBehaviour
{
    [Header("表示")]
    [Tooltip("OFFにすると何も表示しない")]
    [SerializeField] private bool showUi = true;

    [Tooltip("文字の大きさ。**窓が小さいときは自動で縮む**ので、これは上限として使われる")]
    [Range(1f, 4f)]
    [SerializeField] private float uiScale = 1.6f;

    [Tooltip("残り時間がこの秒数を切ったら赤くする")]
    [SerializeField] private float warnSeconds = 10f;

    /// <summary>描く基準の幅（この幅に収まるように縮める）。</summary>
    private const float BaseWidth = 520f;

    // 文字の見た目（GUI.skin は OnGUI の中でしか使えないので、最初の OnGUI で作る。PrepareStyles）

    /// <summary>ラウンド数・イベントの一行など、上の見出し。</summary>
    private GUIStyle titleStyle;

    /// <summary>残り時間の大きな数字。</summary>
    private GUIStyle timerStyle;

    /// <summary>左のチームごとの行・案内。</summary>
    private GUIStyle lineStyle;

    /// <summary>真ん中に出す大きな文字（決着・復活まで・イベント発生）。</summary>
    private GUIStyle resultStyle;

    private void OnGUI()
    {
        SpaceJunkRound round = SpaceJunkRound.Current;

        if (!showUi || round == null)
        {
            return;
        }

        PrepareStyles();

        // 拡大率：設定の倍率（1920×1080 基準）を上限に、窓が小さいときは「幅 520・高さ 360」が収まるまで縮める。
        // ただし 0.6 倍より小さくはしない（文字が読めなくなるため）
        float scale = Mathf.Min(DraggableGuiPanel.FitScale(uiScale), Screen.width / BaseWidth, Screen.height / 360f);
        scale = Mathf.Max(0.6f, scale);

        // ここから下は、拡大率で割った「縮める前の大きさ」で位置を書く（GUI.matrix がまとめて拡大する）
        Matrix4x4 saved = GUI.matrix;
        GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));

        float viewWidth = Screen.width / scale;

        DrawHeader(round, viewWidth);
        DrawTeamProgress(round, viewWidth);

        if (round.Phase == SpaceJunkRoundPhase.Playing)
        {
            DrawEventNotice(round, viewWidth, Screen.height / scale);
        }

        if (round.Phase == SpaceJunkRoundPhase.Result)
        {
            DrawResult(round, viewWidth, Screen.height / scale);
        }
        else if (SpaceJunkPlayerSetup.LocalRespawnRemaining > 0f)
        {
            // 戻った直後の、動けない時間（復活時間）
            float viewHeight = Screen.height / scale;
            int seconds = Mathf.CeilToInt(SpaceJunkPlayerSetup.LocalRespawnRemaining);
            GUI.Label(new Rect(0f, viewHeight * 0.5f - 60f, viewWidth, 60f), $"復活まで {seconds}", resultStyle);
        }

        GUI.matrix = saved;
    }

    private void PrepareStyles()
    {
        if (titleStyle == null)
        {
            titleStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 16,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.UpperCenter
            };
        }

        if (timerStyle == null)
        {
            timerStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 30,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.UpperCenter
            };
        }

        if (lineStyle == null)
        {
            lineStyle = new GUIStyle(GUI.skin.label) { fontSize = 13 };
        }

        if (resultStyle == null)
        {
            resultStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 26,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                wordWrap = true
            };
        }
    }

    /// <summary>画面の上に、ラウンド数と残り時間を出す。</summary>
    private void DrawHeader(SpaceJunkRound round, float viewWidth)
    {
        SpaceJunkSession session = SpaceJunkSession.Current;

        string title = session != null
            ? $"第 {session.CurrentRound} ラウンド　（{session.RoundsToWin} 本先取）"
            : "ラウンド中";

        GUI.Label(new Rect(0f, 8f, viewWidth, 24f), title, titleStyle);

        float remaining = round.RemainingSeconds;
        int minutes = Mathf.FloorToInt(remaining / 60f);
        int seconds = Mathf.FloorToInt(remaining % 60f);

        Color saved = GUI.color;
        GUI.color = remaining <= warnSeconds ? new Color(1f, 0.45f, 0.4f) : Color.white;

        GUI.Label(new Rect(0f, 30f, viewWidth, 40f), $"{minutes}:{seconds:00}", timerStyle);

        GUI.color = saved;
    }

    /// <summary>
    /// 画面の左に、チームごとの進み具合を出す。
    /// **そろえた種類には色がつき、まだのものは暗いまま。**
    /// </summary>
    private void DrawTeamProgress(SpaceJunkRound round, float viewWidth)
    {
        SpaceJunkSession session = SpaceJunkSession.Current;
        int teamCount = session != null ? session.TeamCount : 1;
        int myTeam = MyTeam();

        // 上の見出し（ラウンド数 8〜32・残り時間 30〜70・イベントの一行 70〜94）の下から並べる
        float y = 96f;

        // 左から 12、幅 260・高さ 240 の枠の中に、上から順に並べる
        GUILayout.BeginArea(new Rect(12f, y, 260f, 240f));

        for (int team = 0; team < teamCount; team++)
        {
            Color saved = GUI.color;
            GUI.color = SpaceJunkTeams.TeamColor(team);

            // 自分のチームの行にだけ ▶ を付ける（ほかの行は同じ幅の空白で頭をそろえる）
            string mark = team == myTeam ? "▶ " : "　 ";
            int wins = session != null ? session.RoundWinsOf(team) : 0;

            // 得点制 … 得点とイベントの進み具合。3種類ルール（下）… そろえた素材の印
            if (round.Rule == SpaceJunkWinRule.Score)
            {
                GUILayout.Label($"{mark}{SpaceJunkTeams.TeamName(team)}　{round.ScoreOf(team)} 点　（{wins} 本）", lineStyle);
                GUI.color = saved;

                DrawEventProgress(round, team);
                GUILayout.Space(4f);
                continue;
            }

            GUILayout.Label($"{mark}{SpaceJunkTeams.TeamName(team)}　（{wins} 本）", lineStyle);

            GUI.color = saved;

            // 素材の種類ぶんの印（そのマップで出る種類だけ）
            GUILayout.BeginHorizontal();
            GUILayout.Space(16f);

            for (int i = 0; i < SpaceJunkMaterials.Count; i++)
            {
                SpaceJunkMaterialKind kind = SpaceJunkMaterials.FromIndex(i);

                // そのマップで出ない種類は出さない
                if (!SpaceJunkMaterials.IsUsed(kind))
                {
                    continue;
                }

                bool has = round.HasKind(team, kind);

                // そろえた種類は素材の色、まだの種類は暗い灰色
                Color kindSaved = GUI.color;
                GUI.color = has
                    ? SpaceJunkMaterials.Color(kind)
                    : new Color(0.35f, 0.35f, 0.38f);

                GUILayout.Label(has ? $"■ {SpaceJunkMaterials.Name(kind)}" : $"□ {SpaceJunkMaterials.Name(kind)}",
                                lineStyle);

                GUI.color = kindSaved;
            }

            GUILayout.EndHorizontal();
            GUILayout.Space(4f);
        }

        // **落ちて戻れなくなったときの案内。** マップの外へ落ちることがあるので出しておく
        if (!string.IsNullOrEmpty(SpaceJunkPlayerSetup.LocalResetKeyName))
        {
            GUILayout.Space(4f);
            GUILayout.Label($"落ちたら［{SpaceJunkPlayerSetup.LocalResetKeyName}］で戻れます", lineStyle);
        }

        // **カメラの切り替えの案内。** 切り替えられるカメラ（STAGE_01〜05）のときだけ出す
        if (!string.IsNullOrEmpty(SpaceJunkCameraModeSwitch.LocalToggleKeyName))
        {
            GUILayout.Label($"［{SpaceJunkCameraModeSwitch.LocalToggleKeyName}］でカメラ切り替え　{SpaceJunkCameraModeSwitch.LocalModeSummary}", lineStyle);
        }

        GUILayout.EndArea();
    }

    /// <summary>
    /// 得点制のとき、**イベントの進み具合**を出す。
    /// デブリセットなら「お題 1/3 → +10　■■□ ■ □」のように、入れた数と、素材の色の印を並べる
    /// （■＝もう入れた、□＝まだ）。順不同のときは、種類ごとにまとめて並べている。
    /// </summary>
    private void DrawEventProgress(SpaceJunkRound round, int team)
    {
        if (round.ActiveEvent != SpaceJunkEventKind.DebrisSet || !round.IsEventRunning)
        {
            return;
        }

        GUILayout.BeginHorizontal();
        GUILayout.Space(16f);

        Color saved = GUI.color;
        GUI.color = new Color(0.85f, 0.85f, 0.9f);
        GUILayout.Label($"お題 {round.SetDoneOf(team)}/{round.SetGoalOf(team)} → +{round.SetBonusOf(team)}", lineStyle, GUILayout.ExpandWidth(false));

        if (round.SetOrdered)
        {
            // 順番どおり … お題を左から順に並べる。次に入れる1個は ▶ を付けて目立たせる
            int size = round.SetSizeOf(team);
            int step = round.SetStepOf(team);

            for (int n = 0; n < size; n++)
            {
                SpaceJunkMaterialKind kind = SpaceJunkMaterials.FromIndex(round.SetItemAt(team, n));
                Color color = SpaceJunkMaterials.Color(kind);

                // 次の1個より先のものは、色を 7 割の明るさに落として控えめにする
                if (n > step)
                {
                    color = new Color(color.r * 0.7f, color.g * 0.7f, color.b * 0.7f, 1f);
                }

                GUI.color = color;

                // step … 次に入れる番目（0 から）。それより前＝入れた（■）、ちょうど＝次（▶□）、後ろ＝まだ（□）
                string mark = n < step ? "■" : n == step ? "▶□" : "□";
                GUILayout.Label(mark, lineStyle, GUILayout.ExpandWidth(false));
            }

            GUI.color = saved;
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            return;
        }

        for (int k = 0; k < SpaceJunkMaterials.Count; k++)
        {
            SpaceJunkMaterialKind kind = SpaceJunkMaterials.FromIndex(k);
            int required = round.SetRequiredOf(team, kind);
            int done = round.SetProgressOf(team, kind);

            if (required <= 0)
            {
                continue;
            }

            // 入れた数だけ ■、残りを □（入れすぎても ■ は必要な数まで、□ がマイナスにならないよう 0 で止める）
            Color color = SpaceJunkMaterials.Color(kind);
            string marks = new string('■', Mathf.Min(done, required)) + new string('□', Mathf.Max(0, required - done));

            GUI.color = color;
            GUILayout.Label(marks, lineStyle, GUILayout.ExpandWidth(false));
        }

        GUI.color = saved;
        GUILayout.FlexibleSpace();
        GUILayout.EndHorizontal();
    }

    /// <summary>
    /// イベントの**始まる前のカウントダウン**（残り5秒から）と、**始まった合図**（4秒間）を出す。
    /// </summary>
    private void DrawEventNotice(SpaceJunkRound round, float viewWidth, float viewHeight)
    {
        // 一行の表示は、どれも残り時間のすぐ下（y = 70）に出す。上から順に、当てはまった1つだけを出して抜ける

        // 始まる前の 5 秒間だけ、カウントダウン
        float until = round.SecondsUntilEvent;

        if (until > 0f && until <= 5f)
        {
            GUI.Label(new Rect(0f, 70f, viewWidth, 24f), $"イベントまで {Mathf.CeilToInt(until)}", titleStyle);
            return;
        }

        // 始まってからの秒数（マイナスなら、まだ始まっていない）
        float since = round.SecondsSinceEventStarted;

        if (since < 0f)
        {
            return;
        }

        // 始まって 4 秒間は、画面の上から 3 割の高さに大きく合図を出す（高さ 110 の枠の真ん中をそこに合わせる）
        if (since < 4f)
        {
            Color saved = GUI.color;
            GUI.color = new Color(1f, 0.9f, 0.4f);
            GUI.Label(new Rect(0f, viewHeight * 0.3f - 55f, viewWidth, 110f),
                      $"イベント発生！　{round.EventName}\n" +
                      $"<size=16>{round.EventDescription}{HighValueText(round, "\n")}</size>",
                      resultStyle);
            GUI.color = saved;
            return;
        }

        if (!round.IsEventRunning)
        {
            GUI.Label(new Rect(0f, 70f, viewWidth, 24f), $"イベント終了：{round.EventName}", titleStyle);
            return;
        }

        // 続いている間 … 名前と、時間の決まったイベントなら残り秒数
        string line = $"イベント：{round.EventName}";

        float remaining = round.EventRemainingSeconds;
        if (remaining >= 0f)
        {
            line += $"　残り {Mathf.CeilToInt(remaining)} 秒";
        }

        if (round.ActiveEvent == SpaceJunkEventKind.HighValueDebris)
        {
            line += HighValueText(round, "　");
        }
        else if (round.ActiveEvent == SpaceJunkEventKind.HeavyDebris)
        {
            string points = round.HeavyBonusPerWeight > 0
                ? $"重さ1で {round.HeavyPoints} 点・重さが1増えるごとに +{round.HeavyBonusPerWeight}"
                : $"1個 {round.HeavyPoints} 点";
            line += $"　特殊デブリ あと {round.HeavyRemaining} 個（{points}・引きずるだけ）";
        }

        GUI.Label(new Rect(0f, 70f, viewWidth, 24f), line, titleStyle);
    }

    /// <summary>
    /// 期間限定高価値デブリのとき、「紫（回路基板）のデブリが +2点」の一文（種類の色つき）。
    /// ほかのイベント、またはまだ選ばれていなければ空。
    /// </summary>
    private static string HighValueText(SpaceJunkRound round, string prefix)
    {
        if (round.ActiveEvent != SpaceJunkEventKind.HighValueDebris || !round.TryGetHighValueKind(out SpaceJunkMaterialKind kind))
        {
            return string.Empty;
        }

        string hex = ColorUtility.ToHtmlStringRGB(SpaceJunkMaterials.Color(kind));
        return $"{prefix}<color=#{hex}>{SpaceJunkMaterials.ColorName(kind)}（{SpaceJunkMaterials.Name(kind)}）</color>のデブリが +{round.HighValueBonus}点";
    }

    /// <summary>決着後の表示。</summary>
    private void DrawResult(SpaceJunkRound round, float viewWidth, float viewHeight)
    {
        SpaceJunkSession session = SpaceJunkSession.Current;

        string text;
        Color color = Color.white;

        if (session != null && session.State == SpaceJunkMatchState.MatchOver)
        {
            int winner = session.MatchWinner;
            text = winner >= 0
                ? $"{SpaceJunkTeams.TeamName(winner)} の勝ち！\n（{session.RoundWinsOf(winner)} 本先取）\n\nまもなくロビーへ戻ります"
                : "試合終了";

            if (winner >= 0)
            {
                color = SpaceJunkTeams.TeamColor(winner);
            }
        }
        else
        {
            int winner = round.RoundWinner;
            text = winner >= 0
                ? $"{SpaceJunkTeams.TeamName(winner)} がラウンドを取りました\n\nまもなく次のマップへ"
                : "引き分け\nこのラウンドは誰も取りません\n\nまもなく次のマップへ";

            if (winner >= 0)
            {
                color = SpaceJunkTeams.TeamColor(winner);
            }

            // 得点制なら、各チームの得点も出す（何点差だったかが分かるように）
            // 「まもなく次のマップへ」の前の空行に、得点の一行を差し込んでいる
            if (round.Rule == SpaceJunkWinRule.Score)
            {
                text = text.Replace("\n\nまもなく", $"\n{ScoreSummary(round)}\n\nまもなく");
            }
        }

        // 画面の真ん中に、幅 440・高さ 230 の枠（左上の位置は、真ん中から幅・高さの半分ずつ戻した所）
        Rect box = new Rect(viewWidth * 0.5f - 220f, viewHeight * 0.5f - 115f, 440f, 230f);

        GUI.Box(box, GUIContent.none);

        Color saved = GUI.color;
        GUI.color = color;
        GUI.Label(box, text, resultStyle);
        GUI.color = saved;
    }

    /// <summary>「青 12点／赤 8点」のような、全チームの得点の一行。</summary>
    private static string ScoreSummary(SpaceJunkRound round)
    {
        int teamCount = SpaceJunkSession.Current != null ? SpaceJunkSession.Current.TeamCount : 1;
        var parts = new System.Collections.Generic.List<string>();

        for (int team = 0; team < teamCount; team++)
        {
            parts.Add($"{SpaceJunkTeams.TeamName(team).Replace("チーム", string.Empty)} {round.ScoreOf(team)}点");
        }

        return string.Join("／", parts);
    }

    /// <summary>
    /// このPCで操作している人のチーム。分からなければ -1。
    /// 接続番号で引くので、1台で複数人のときは**そのPCの1人目**のチームになる。
    /// </summary>
    private static int MyTeam()
    {
        NetworkManager manager = NetworkManager.Singleton;

        if (manager == null || SpaceJunkSession.Current == null)
        {
            return -1;
        }

        return SpaceJunkSession.Current.TeamOf(manager.LocalClientId);
    }
}
