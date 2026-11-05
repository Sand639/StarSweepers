using UnityEngine;

/// <summary>
/// **プレイヤー（本実装）の検証シーンだけで使う、画面左上の説明と結果の表示。**（2026/10/27）
///
/// 操作のしかたと、いまの段階・最後の投げの結果（後ろ／前／クリティカル）を出す。
/// クリティカルの受付時間はここにも出さない（仕様どおり、合図は床の光の輪だけ）。
/// 試合のシーンには置かない。
/// </summary>
public class SweeperPlayerTestHud : MonoBehaviour
{
    [SerializeField] private SweeperShooter shooter;
    [SerializeField] private SweeperReel reel;

    private GUIStyle style;

    private void OnGUI()
    {
        style ??= new GUIStyle(GUI.skin.box)
        {
            alignment = TextAnchor.UpperLeft,
            fontSize = Mathf.Max(14, Screen.height / 54),
            wordWrap = true
        };

        string state = shooter != null ? shooter.PhaseName : "-";
        string result = reel != null && reel.LastResult != "" ? reel.LastResult : "-";

        string text =
            "プレイヤー（本実装）の検証\n" +
            "左スティック／WASD：移動（移動方向を向く）\n" +
            "右スティック／マウス：狙う方向（半径1mの円の中のカーソル）\n" +
            "RT／左クリック 長押し：届く長さを溜める（矢印の帯）\n" +
            "離す：光線が伸びる → 物資に当たると巻き取る\n" +
            "巻き取り中に押す：後ろへ投げる（床が光った瞬間ならクリティカル）\n" +
            "押さない：前へ投げる\n" +
            $"段階：{state}　最後の投げ：{result}";

        float width = Mathf.Min(Screen.width - 20f, style.fontSize * 30f);
        float height = style.CalcHeight(new GUIContent(text), width);
        GUI.Box(new Rect(10f, 10f, width, height), text, style);
    }
}
