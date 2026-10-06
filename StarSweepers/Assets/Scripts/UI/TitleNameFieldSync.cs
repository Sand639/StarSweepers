using UnityEngine;
using UnityEngine.UI;

/// <summary>名前の入力欄が開くたびに、保存されている名前を入れ直す（別の画面で変えた名前に合わせる）。</summary>
public class TitleNameFieldSync : MonoBehaviour
{
    private InputField field;

    public void Setup(InputField target)
    {
        field = target;
    }

    private void OnEnable()
    {
        if (field != null && field.text != GameSettings.PlayerName)
        {
            field.text = GameSettings.PlayerName;
        }
    }
}
