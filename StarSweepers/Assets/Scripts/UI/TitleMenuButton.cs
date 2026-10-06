using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// **タイトル画面のボタン1つの見た目を切り替える部品。**
/// マウスが重なったとき・コントローラーで選ばれたときに、画像・色・文字の色・ふちを変える。
/// 使う画像と色は <see cref="TitleScreenTheme"/> から受け取る（<see cref="TitleScreen"/> が付ける）。
///
/// 「選ばれている」状態（設定のタブ・通信のしかたの切り替えなど）も、同じ見た目で出せる（<see cref="SetSelectedLook"/>）。
/// </summary>
public class TitleMenuButton : MonoBehaviour,
    IPointerEnterHandler, IPointerExitHandler, ISelectHandler, IDeselectHandler
{
    private Image background;
    private Text label;
    private Outline outline;
    private TitleScreenTheme theme;

    private bool pointerOver;
    private bool selectedByNavigation;
    private bool selectedLook;

    /// <summary>見た目の材料を受け取る。</summary>
    public void Setup(Image backgroundImage, Text labelText, TitleScreenTheme useTheme)
    {
        background = backgroundImage;
        label = labelText;
        theme = useTheme;

        outline = background.GetComponent<Outline>();
        if (outline == null)
        {
            outline = background.gameObject.AddComponent<Outline>();
        }
        outline.effectDistance = new Vector2(3f, -3f);

        Refresh();
    }

    /// <summary>「選ばれている」見た目にする（タブ・切り替えボタンで、いまの選択を示す）。</summary>
    public void SetSelectedLook(bool selected)
    {
        selectedLook = selected;
        Refresh();
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        pointerOver = true;
        Refresh();
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        pointerOver = false;
        Refresh();
    }

    public void OnSelect(BaseEventData eventData)
    {
        selectedByNavigation = true;
        Refresh();
    }

    public void OnDeselect(BaseEventData eventData)
    {
        selectedByNavigation = false;
        Refresh();
    }

    private void OnDisable()
    {
        // 画面を切り替えたときに「重なっている」見た目が残らないように
        pointerOver = false;
        selectedByNavigation = false;
        Refresh();
    }

    private void Refresh()
    {
        if (background == null || theme == null)
        {
            return;
        }

        bool hot = pointerOver || selectedByNavigation;
        bool lit = hot || selectedLook;

        Sprite sprite = hot && theme.buttonHover != null ? theme.buttonHover : theme.button;
        background.sprite = sprite;
        background.type = sprite != null && sprite.border != Vector4.zero ? Image.Type.Sliced : Image.Type.Simple;
        background.color = lit ? theme.buttonHoverColor : theme.buttonColor;

        if (label != null)
        {
            label.color = lit ? theme.buttonHoverTextColor : theme.buttonTextColor;
        }

        if (outline != null)
        {
            outline.effectColor = lit ? theme.buttonOutlineColor : new Color(theme.buttonOutlineColor.r,
                theme.buttonOutlineColor.g, theme.buttonOutlineColor.b, 0.35f);
        }
    }
}
