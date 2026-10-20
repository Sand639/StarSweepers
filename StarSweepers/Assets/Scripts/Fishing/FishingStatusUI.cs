using UnityEngine;

/// <summary>
/// **確認用の文字表示。** 点数・爆発までの残り秒数・スタン中の表示を画面に出す。
///
/// フォントの素材を用意しなくても出せるように、`OnGUI` で描いている
/// （既存の `NetworkStatusHud.cs` と同じ作り）。
/// **プロトタイプ用の表示なので、本番の画面には出さない**（Show Hud を切る）。
///
/// チャージ量とスキルチェックのゲージは <see cref="HookChargeUI"/> の担当で、こちらには含まない。
/// </summary>
public class FishingStatusUI : MonoBehaviour
{
    [Header("表示するか")]
    [Tooltip("OFFにすると何も表示しない（本番では切る）")]
    [SerializeField] private bool showHud = true;

    [Tooltip("文字の大きさ")]
    [Range(1f, 4f)]
    [SerializeField] private float uiScale = 1.6f;

    [Header("参照")]
    [Tooltip("1人用の点数を持っている ScoreBoard（オンラインのシーンでは空でよい）")]
    [SerializeField] private ScoreBoard scoreBoard;

    [Tooltip("プレイヤーの PlayerStun。オンラインでは自分のプレイヤーから自動で探す")]
    [SerializeField] private PlayerStun playerStun;

    [Header("表示の設定")]
    [Tooltip("「◯◯に投入！」を出しておく秒数")]
    [SerializeField] private float eventMessageSeconds = 2f;

    [Tooltip("操作の説明を出す")]
    [SerializeField] private bool showControls = true;

    /// <summary>「◯◯に投入！」の文字の色（薄い緑）。</summary>
    private static readonly Color EventColor = new Color(0.4f, 1f, 0.5f);

    /// <summary>点数の行と行の間（px。縮める前の大きさ）。</summary>
    private const float LineHeight = 18f;

    private GUIStyle labelStyle;
    private GUIStyle bigStyle;

    /// <summary>今フレーム、実際に使っている縮小率（窓の大きさに合わせて変わる）。</summary>
    private float activeScale = 1.6f;

    /// <summary>
    /// 毎フレームの描画。全体を <see cref="activeScale"/> で拡大してから、
    /// 左上の点数 → 上の爆発カウントダウン → 中央のスタン表示 → 一番下の操作説明、の順に描く。
    /// </summary>
    private void OnGUI()
    {
        if (!showHud)
        {
            return;
        }

        PrepareStyles();

        // **窓が小さいときは自動で縮める**（小さい窓で並べると、他の表示と重なるため）
        activeScale = DraggableGuiPanel.FitScale(uiScale);

        Matrix4x4 saved = GUI.matrix;
        GUI.matrix = Matrix4x4.Scale(new Vector3(activeScale, activeScale, 1f));

        float width = Screen.width / activeScale;
        DrawScore();
        DrawBombTimer(width);
        DrawStun(width);
        DrawControls(width);

        GUI.matrix = saved;
    }

    /// <summary>文字のスタイルを初回だけ作る（`GUI.skin` は OnGUI の中でしか触れないため、Awake では作れない）。</summary>
    private void PrepareStyles()
    {
        if (labelStyle == null)
        {
            labelStyle = new GUIStyle(GUI.skin.label) { fontSize = 13 };
        }
        if (bigStyle == null)
        {
            bigStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 22,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter
            };
        }
    }

    /// <summary>
    /// 左上に点数を出す。
    /// **オンラインの試合があればチームごとの点数**、無ければ1人用の点数を出す。
    /// </summary>
    private void DrawScore()
    {
        if (FishingMatch.Current != null)
        {
            DrawTeamScore(FishingMatch.Current);
            return;
        }

        if (scoreBoard == null)
        {
            return;
        }

        float y = 8f;

        for (int i = 0; i < scoreBoard.PlayerCount; i++)
        {
            GUI.color = Color.white;
            GUI.Label(new Rect(10f, y, 320f, 20f),
                $"{scoreBoard.NameOf(i)}　スコア {scoreBoard.ScoreOf(i)}", labelStyle);
            y += LineHeight;
        }

        DrawEventMessage(y, 380f, scoreBoard.LastEvent, scoreBoard.LastEventTime);
    }

    /// <summary>チームごとの点数を出す。自分のチームには印を付ける。</summary>
    private void DrawTeamScore(FishingMatch match)
    {
        int myTeam = FindMyTeam();
        float y = 8f;

        for (int team = 0; team < match.TeamCount; team++)
        {
            GUI.color = FishingTeams.TeamColor(team);

            string mark = team == myTeam ? "▶ " : "　 ";
            GUI.Label(new Rect(10f, y, 340f, 20f),
                $"{mark}{FishingTeams.TeamName(team)}　{match.ScoreOf(team)} 点", labelStyle);

            y += LineHeight;
        }

        GUI.color = Color.white;

        DrawEventMessage(y, 420f, match.LastEvent, match.LastEventTime);
    }

    /// <summary>
    /// 点数の下に、直前のできごと（「◯◯に投入！」など）を出す。
    /// 起きてから <see cref="eventMessageSeconds"/> 秒の間だけ出し、そのあとは消える。
    /// </summary>
    private void DrawEventMessage(float y, float width, string message, float happenedAt)
    {
        if (string.IsNullOrEmpty(message) || Time.time - happenedAt >= eventMessageSeconds)
        {
            return;
        }

        GUI.color = EventColor;
        GUI.Label(new Rect(10f, y, width, 20f), message, labelStyle);
        GUI.color = Color.white;
    }

    /// <summary>このPCで自分が操作しているオンラインのプレイヤー。オンラインでない・まだいないときは null。</summary>
    private static FishingNetPlayer FindMyPlayer()
    {
        foreach (FishingNetPlayer player in FishingNetPlayer.All)
        {
            if (player != null && player.IsOwner)
            {
                return player;
            }
        }
        return null;
    }

    /// <summary>自分が操作しているプレイヤーのチーム。見つからなければ -1。</summary>
    private int FindMyTeam()
    {
        FishingNetPlayer me = FindMyPlayer();
        return me != null ? me.TeamIndex : -1;
    }

    /// <summary>オンラインでは、自分のプレイヤーから PlayerStun を拾う。</summary>
    private PlayerStun ResolveStun()
    {
        if (playerStun != null)
        {
            return playerStun;
        }

        FishingNetPlayer me = FindMyPlayer();
        return me != null ? me.GetComponent<PlayerStun>() : null;
    }

    /// <summary>火のついた爆発物があれば、画面上部に残り秒数を大きく出す。</summary>
    private void DrawBombTimer(float width)
    {
        ExplosiveObject mostUrgent = FindMostUrgentBomb();
        if (mostUrgent == null)
        {
            return;
        }

        float remaining = Mathf.Max(0f, mostUrgent.FuseRemaining);

        // 残りが少ないほど、黄色から赤へ近づける（0.01 は 0 で割らないための下限）
        float danger = 1f - Mathf.Clamp01(remaining / Mathf.Max(0.01f, mostUrgent.FuseSeconds));
        GUI.color = Color.Lerp(new Color(1f, 0.85f, 0.2f), new Color(1f, 0.2f, 0.15f), danger);

        // 一番上は残り時間（FishingMatchUI）が使っているので、その下に出す
        GUI.Label(new Rect(0f, 40f, width, 30f),
            $"爆発まで {remaining:0.0} 秒！", bigStyle);

        GUI.color = Color.white;
    }

    /// <summary>スタン中は、画面中央に大きく出す。</summary>
    private void DrawStun(float width)
    {
        PlayerStun stun = ResolveStun();

        if (stun == null || !stun.IsStunned)
        {
            return;
        }

        float height = Screen.height / activeScale;

        // 画面の真ん中より少し上（高さの 42% の位置）に、赤い大きな文字で出す
        GUI.color = new Color(1f, 0.3f, 0.25f);
        GUI.Label(new Rect(0f, height * 0.42f, width, 30f),
            $"スタン中！　動けない（あと {stun.Remaining:0.0} 秒）", bigStyle);
        GUI.color = Color.white;
    }

    /// <summary>画面の一番下に、操作の説明を薄い白で1行出す（<see cref="HookAimAssist"/> の表示はこの行の上に乗る）。</summary>
    private void DrawControls(float width)
    {
        if (!showControls)
        {
            return;
        }

        float height = Screen.height / activeScale;

        GUI.color = new Color(1f, 1f, 1f, 0.65f);
        GUI.Label(new Rect(10f, height - 26f, width - 20f, 20f),
            "WASD 移動／マウス 向き／左クリック 長押しでチャージ・離して発射／" +
            "引き寄せ中は枠の中で左クリック（前半＝後ろへ・後半＝前へ）", labelStyle);
        GUI.color = Color.white;
    }

    /// <summary>火のついた爆発物のうち、一番残り時間が短いものを返す。</summary>
    private ExplosiveObject FindMostUrgentBomb()
    {
        ExplosiveObject best = null;

        foreach (ExplosiveObject bomb in ExplosiveObject.LitBombs)
        {
            if (bomb == null || !bomb.FuseLit)
            {
                continue;
            }
            if (best == null || bomb.FuseRemaining < best.FuseRemaining)
            {
                best = bomb;
            }
        }

        return best;
    }
}
