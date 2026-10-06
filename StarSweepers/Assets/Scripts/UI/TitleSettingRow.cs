using UnityEngine;
using UnityEngine.UI;

/// <summary>タイトル画面の設定の項目。数字は保存に使うので、足すときは空いている番号を使う。</summary>
public enum TitleSettingKind
{
    Volume = 0,
    MouseSensitivity = 1,
    ControllerType = 2,
    Resolution = 3,
    ScreenMode = 4,
    FrameRateLimit = 5,
    VSync = 6,
    Quality = 7,
}

/// <summary>
/// **タイトル画面の設定の1行に「何の設定か」を付ける印。**
///
/// ・つまみ（Slider）を入れた行 … 音量・マウス感度・フレームレート上限
/// ・＜ ＞ のボタンを入れた行 … 操作タイプ・解像度・スクリーンモード・垂直同期・画質
///
/// 値の読み書きは <see cref="TitleScreen"/> がする。Value Text に、いまの値（「80 %」など）が入る。
/// </summary>
public class TitleSettingRow : MonoBehaviour
{
    [Tooltip("何の設定か")]
    [SerializeField] private TitleSettingKind kind;

    [Tooltip("いまの値を出す文字")]
    [SerializeField] private Text valueText;

    [Tooltip("つまみ（音量など）。＜ ＞ で変える項目では空")]
    [SerializeField] private Slider slider;

    [Tooltip("「＜」のボタン。つまみの項目では空")]
    [SerializeField] private Button previous;

    [Tooltip("「＞」のボタン。つまみの項目では空")]
    [SerializeField] private Button next;

    public TitleSettingKind Kind => kind;
    public Text ValueText => valueText;
    public Slider Slider => slider;
    public Button Previous => previous;
    public Button Next => next;

#if UNITY_EDITOR
    /// <summary>プレハブを作るツールが中身を決める。</summary>
    public void EditorSetup(TitleSettingKind value, Text text, Slider useSlider, Button left, Button right)
    {
        kind = value;
        valueText = text;
        slider = useSlider;
        previous = left;
        next = right;
    }
#endif
}
