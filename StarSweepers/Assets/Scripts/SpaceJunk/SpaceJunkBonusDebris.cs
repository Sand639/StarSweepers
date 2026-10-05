using UnityEngine;

/// <summary>
/// **得点の高い特殊デブリの目印。** ゴールに入れると、ふつうの1点に Bonus Points が足される。
///
/// STAGE_02 の**真ん中に置く特殊デブリ**で使う（2026/10/6・大槻さん）。
/// 真ん中に出し、消えたらまた真ん中に出すのは <see cref="SpaceJunkBombPoints"/>（Respawn When Moved を OFF）。
///
/// 見た目は、始まったときに <see cref="SpaceJunkHighValueMark"/> を付けて**金色に光らせる**（レーダーでも大きな点で出る）。
/// 素材のスポナーの数（Max Objects）には入らない。
///
/// 使い方：素材のプレハブ（SpaceJunkMaterial 付き）にこれを付ける。
/// ラウンドイベントの「特殊デブリ（重いデブリ）」とは**別物**。
/// </summary>
public class SpaceJunkBonusDebris : MonoBehaviour
{
    [Tooltip("ゴールに入れたとき、ふつうの1点に足す点数（初期値9 → 合計10点）")]
    [SerializeField] private int bonusPoints = 9;

    [Tooltip("光らせる色")]
    [SerializeField] private Color glowColor = new Color(1f, 0.82f, 0.15f);

    /// <summary>ふつうの1点に足す点数。</summary>
    public int BonusPoints => Mathf.Max(0, bonusPoints);

    private void Start()
    {
        if (!TryGetComponent(out SpaceJunkHighValueMark mark))
        {
            mark = gameObject.AddComponent<SpaceJunkHighValueMark>();
        }
        mark.Show(glowColor);
    }
}
