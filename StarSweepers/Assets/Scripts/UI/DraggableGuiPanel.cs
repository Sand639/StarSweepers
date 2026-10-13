using UnityEngine;

/// <summary>
/// **`OnGUI` で描く枠を、ドラッグで動かせて・折りたためるようにする部品。**
///
/// 確認用の表示（接続画面・通信の様子・ロビーなど）は `GUILayout.BeginArea` で
/// 位置を決め打ちしていたため、**小さい窓で並べて起動すると枠同士が重なって読めなかった。**
/// そこで、枠を次の形にそろえる。
///
///   ・**上の帯（タイトル）をつかんでドラッグすると動く**
///   ・**帯の右の「－」で折りたためる**（「＋」で開く）
///   ・**窓が小さいと自動で縮む**（1920×1080 を基準に、窓の大きさに比例）
///   ・**高さは中身に合わせる**（空いた部分まで枠が伸びない）
///
/// 使い方（MonoBehaviour の OnGUI の中で）：
/// <code>
/// private readonly DraggableGuiPanel panel = new DraggableGuiPanel("通信の様子", 0f, 12f, 300f);
/// private void OnGUI() { panel.Draw(uiScale, DrawContents); }
/// </code>
///
/// MonoBehaviour ではないので、シーンに置く必要はない。
/// </summary>
public class DraggableGuiPanel
{
    /// <summary>窓ごとに違う番号が要る（IMGUI の決まり）。他と被らないよう大きい数から始める。</summary>
    private static int nextWindowId = 0x4D4930;

    /// <summary>これより小さくは縮めない（文字が読めなくなるため）。</summary>
    private const float MinScale = 0.5f;

    /// <summary>タイトルの帯の高さ。ここをつかんでドラッグする。</summary>
    private const float TitleBarHeight = 20f;

    private readonly int windowId;
    private readonly string title;
    private readonly float anchorX;
    private readonly float offsetX;
    private readonly float defaultY;
    private readonly float width;

    /// <summary>縮める前の座標での位置と大きさ。</summary>
    private Rect rect;

    private bool placed;
    private bool collapsed;
    private GUI.WindowFunction contents;

    /// <summary>
    /// 「＋」「－」が押されて、**次の並べ直しで切り替える予定**があるか。
    ///
    /// ## なぜその場で切り替えないのか（2026/9/22 修正）
    ///
    /// `OnGUI` は1回の描画の中で、まず**並べ方を決める**（Layout）、次に**描く・クリックを受け取る**、
    /// の順に何度も呼ばれる。ボタンが押されたと分かるのは後のほう。
    ///
    /// そこで開いてしまうと、**並べ方は「閉じた状態」で決まっているのに、中身を描こうとする**ことになり、
    /// Unity が「並べ方が決まっていない物を描こうとした」と失敗する
    /// （`Getting control 0's position in a group with only 0 controls`）。
    /// 見た目は**「＋」を押すと開こうとするが、開けない**になっていた（大槻さんの報告）。
    ///
    /// そのため、押されたら**印だけ付けておき、次に並べ方を決めるときに切り替える。**
    /// </summary>
    private bool toggleRequested;

    /// <summary>折りたたまれているか。</summary>
    public bool Collapsed
    {
        get => collapsed;
        set => collapsed = value;
    }

    /// <param name="title">上の帯に出す名前</param>
    /// <param name="anchorX">最初に出す横位置の基準。0＝左端、1＝右端、0.5＝中央</param>
    /// <param name="offsetX">基準からずらす量（右端基準なら、マイナスで内側へ）</param>
    /// <param name="y">最初に出す縦位置</param>
    /// <param name="width">枠の幅（縮める前の値）</param>
    public DraggableGuiPanel(string title, float anchorX, float offsetX, float y, float width)
    {
        windowId = nextWindowId++;
        this.title = title;
        this.anchorX = anchorX;
        this.offsetX = offsetX;
        defaultY = y;
        this.width = width;
    }

    /// <summary>
    /// 窓の大きさに合わせた縮小率を返す。
    /// **1920×1080 のときに <paramref name="uiScale"/> そのまま**、960×540 ならその半分になる。
    /// </summary>
    public static float FitScale(float uiScale)
    {
        float ratio = Mathf.Min(Screen.width / 1920f, Screen.height / 1080f);
        return Mathf.Max(MinScale, uiScale * Mathf.Min(1f, ratio));
    }

    /// <summary>
    /// 枠を描く。**OnGUI の中で毎回呼ぶ。**
    /// 中身は <paramref name="drawContents"/> の中で `GUILayout` を使って描く。
    /// </summary>
    public void Draw(float uiScale, GUI.WindowFunction drawContents)
    {
        float scale = FitScale(uiScale);
        float viewWidth = Screen.width / scale;
        float viewHeight = Screen.height / scale;

        if (!placed)
        {
            placed = true;
            rect = new Rect(
                anchorX * (viewWidth - width) + offsetX,
                defaultY,
                width,
                0f);
        }

        contents = drawContents;

        // **開く・閉じるは、並べ方を決める段階でだけ切り替える**（toggleRequested の説明を参照）
        if (toggleRequested && Event.current.type == EventType.Layout)
        {
            collapsed = !collapsed;
            toggleRequested = false;
        }

        Matrix4x4 saved = GUI.matrix;
        GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));

        // 高さを0に戻してから測り直すと、中身が減ったときに枠も縮む
        // （戻さないと、一度伸びた高さのまま残る）。
        // **測り直すのは Layout のときだけ**にする（描く側で0にすると枠がちらつくため）
        rect.width = width;
        if (Event.current.type == EventType.Layout)
        {
            rect.height = 0f;
        }
        // 閉じているときも、帯と「＋」が押せる高さは必ず残す
        rect = GUILayout.Window(windowId, rect, DrawWindow, title,
            GUILayout.Width(width), GUILayout.MinHeight(TitleBarHeight + 6f));

        // **帯が画面の外へ出てつかめなくならない**ところまで戻す
        // （中身は画面の外へはみ出してもよい。帯さえ見えていれば引き戻せる）
        rect.x = Mathf.Clamp(rect.x, 40f - rect.width, viewWidth - 40f);
        rect.y = Mathf.Clamp(rect.y, 0f, viewHeight - TitleBarHeight);

        // マウスが枠の上にあれば、画面のポインター（スコープレンズ）を出してもらう。
        // Windows のカーソルは隠してあるので、遊んでいる間でも枠のボタンを押せるように（2026/10/11）
        if (Event.current.type == EventType.Repaint && rect.Contains(Event.current.mousePosition))
        {
            UiPointer.ReportPanelHover();
        }

        GUI.matrix = saved;
    }

    private void DrawWindow(int id)
    {
        // 帯の右端に、折りたたみのボタン
        // ここでは切り替えない。印だけ付けて、次に並べ方を決めるときに切り替える
        if (GUI.Button(new Rect(width - 24f, 2f, 20f, TitleBarHeight - 4f), collapsed ? "+" : "-"))
        {
            toggleRequested = true;
        }

        if (!collapsed)
        {
            contents?.Invoke(id);
        }
        else
        {
            // **たたんでいるときも、幅を保つための空の場所を1つ置く。**
            //
            // GUILayout.Window は、中身が1つも無いと**幅の指定（GUILayout.Width）を無視して**
            // 枠の余白ぶんまで縮んでしまう。そうなると帯も「＋」も窓の外にはみ出して見えなくなり、
            // **二度と開けない小さな楕円**だけが残る（2026/9/22・大槻さんの報告）
            GUILayoutUtility.GetRect(width - 20f, 2f);
        }

        // 帯の部分だけをつかめるようにする（全体にすると、ボタンや入力欄が押しにくくなる）
        GUI.DragWindow(new Rect(0f, 0f, width - 28f, TitleBarHeight));
    }
}
