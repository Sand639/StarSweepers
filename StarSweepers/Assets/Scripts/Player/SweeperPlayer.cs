using UnityEngine;

/// <summary>
/// **プレイヤー（本実装）のまとめ役。**（2026/10/27・小野田さんの仕様）
///
/// 名前とチームを持ち、同じ体に付いている見た目の部品（リング・名前・狙いの帯・光線・カーソル）に
/// **チームの色**を配る。ロビーから名前とチームを受け取るときは <see cref="SetProfile"/> を呼ぶ。
///
/// 体の部品の分担：
/// | 部品 | 役割 |
/// | --- | --- |
/// | <see cref="SweeperMover"/> | 移動と向き |
/// | <see cref="SweeperAim"/> | 狙う向き（半径1mの円の中のカーソル） |
/// | <see cref="SweeperRing"/> | 足元のリング（狙いのカーソルも小さなリング） |
/// | <see cref="SweeperNameTag"/> | 頭の上の名前 |
/// | <see cref="SweeperAimArea"/> | 狙っている間の矢印の帯 |
/// | <see cref="SweeperShooter"/> | 溜める・光線を伸ばす・当たったら巻き取りへ |
/// | <see cref="SweeperReel"/> | 巻き取りと、前後への投げ・クリティカル |
/// | <see cref="SweeperAnimation"/> | アニメーションの切り替え |
///
/// 詳しくは `Documents/機能ドキュメント/プレイヤーの本実装.md`。
/// </summary>
[DisallowMultipleComponent]
public class SweeperPlayer : MonoBehaviour
{
    [Header("名前とチーム（ロビーから受け取る。いまは手で入れる）")]
    [Tooltip("頭の上に出す名前")]
    [SerializeField] private string playerName = "Player";

    [Tooltip("チームの番号（0 = 青、1 = 赤、2 = 緑、3 = 黄）。色は SpaceJunkTeams と同じ")]
    [Min(0)]
    [SerializeField] private int teamIndex;

    [Tooltip("ON：チームの番号の色を使う。OFF：下の Custom Color を使う")]
    [SerializeField] private bool useTeamColor = true;

    [Tooltip("Use Team Color が OFF のときの色")]
    [SerializeField] private Color customColor = Color.white;

    /// <summary>名前。</summary>
    public string PlayerName => playerName;

    /// <summary>チームの番号。</summary>
    public int TeamIndex => teamIndex;

    /// <summary>このプレイヤーの色（チームの色）。</summary>
    public Color TeamColor => useTeamColor ? SpaceJunkTeams.TeamColor(teamIndex) : customColor;

    private void Start()
    {
        Apply();
    }

    /// <summary>
    /// **ロビーから名前とチームを受け取る入口。** 呼ぶと、すぐ見た目に反映される。
    /// </summary>
    public void SetProfile(string newName, int newTeamIndex)
    {
        playerName = newName;
        teamIndex = Mathf.Max(0, newTeamIndex);
        useTeamColor = true;
        Apply();
    }

    /// <summary>名前と色を、同じ体の部品に配り直す。</summary>
    public void Apply()
    {
        Color color = TeamColor;

        foreach (SweeperRing ring in GetComponentsInChildren<SweeperRing>(true))
        {
            ring.SetColor(color);
        }

        foreach (SweeperNameTag nameTag in GetComponentsInChildren<SweeperNameTag>(true))
        {
            nameTag.SetName(playerName);
            nameTag.SetColor(color);
        }

        foreach (SweeperAimArea area in GetComponentsInChildren<SweeperAimArea>(true))
        {
            area.SetColor(color);
        }

        foreach (SweeperShooter shooter in GetComponentsInChildren<SweeperShooter>(true))
        {
            shooter.SetColor(color);
        }
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        // 再生中にインスペクターで名前やチームを変えたら、その場で見た目に出す
        if (Application.isPlaying)
        {
            Apply();
        }
    }
#endif
}
