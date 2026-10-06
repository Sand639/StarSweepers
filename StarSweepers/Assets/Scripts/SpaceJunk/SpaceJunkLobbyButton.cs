using UnityEngine;
using UnityEngine.UI;

/// <summary>ロビーの画面のボタンを押したときにすること。数字は保存に使うので、足すときは空いている番号を使う。</summary>
public enum SpaceJunkLobbyAction
{
    None = 0,
    Close = 1,
    StartMatch = 2,
    RandomAssign = 3,
    SelectRandomMap = 4,
    Step = 5,
    SelectMap = 6,
    ToggleMapCheck = 7,
    TeamPrevious = 8,
    TeamNext = 9,
    ChangePassword = 10,
}

/// <summary>
/// **ロビーの画面のボタン1つに「押したら何をするか」を付ける印。**
///
/// ・Action を選ぶだけで働きが決まる。複製して Action を変えれば、別の場所に同じ働きのボタンを置ける
/// ・Step（数を増やす・減らす）は、**親にある <see cref="SpaceJunkLobbyValueRow"/> の値を Amount だけ変える**（+10 なら 10、-1 なら -1）
/// ・マップの行・チーム分けの行のボタン（SelectMap など）は、行を作るときにコードがつなぐ
/// </summary>
[RequireComponent(typeof(Button))]
public class SpaceJunkLobbyButton : MonoBehaviour
{
    [Tooltip("押したときにすること")]
    [SerializeField] private SpaceJunkLobbyAction action;

    [Tooltip("Step のときに、値をいくつ変えるか（-1・+10 など）")]
    [SerializeField] private int amount;

    public SpaceJunkLobbyAction Action => action;
    public int Amount => amount;
    public Button Button => GetComponent<Button>();

#if UNITY_EDITOR
    /// <summary>プレハブを作るツールが中身を決める。</summary>
    public void EditorSetup(SpaceJunkLobbyAction value, int stepAmount)
    {
        action = value;
        amount = stepAmount;
    }
#endif
}
