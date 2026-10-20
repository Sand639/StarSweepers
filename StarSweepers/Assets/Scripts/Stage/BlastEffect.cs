using UnityEngine;

/// <summary>
/// **爆発の見た目。** 一瞬で大きく広がって、縮みながら消える球。
///
/// 爆弾（<see cref="Bomb"/>）が爆発したときに作られ、終わったら自分で消える。
/// 当たり判定は無い（見た目だけ）。
/// </summary>
public class BlastEffect : MonoBehaviour
{
    /// <summary>一番大きくなったときの直径（メートル）。</summary>
    private float size;

    /// <summary>出てから消えるまでの秒数。</summary>
    private float duration;

    /// <summary>出てからの経過秒数。</summary>
    private float elapsed;

    /// <summary>
    /// 広がり始める。<paramref name="diameter"/> は一番大きくなったときの直径。
    /// **付けたらすぐに呼ぶこと**（呼ばないと大きさ 0 のまま、すぐ消える）。
    /// </summary>
    public void Play(float diameter, float seconds)
    {
        size = diameter;

        // 短すぎると一瞬で消えて見えないので、0.05秒より短くはしない
        duration = Mathf.Max(0.05f, seconds);
        elapsed = 0f;

        transform.localScale = Vector3.zero;
    }

    private void Update()
    {
        // ポーズ中でも止めない（止めると画面に火の玉が残り続ける）
        elapsed += Time.unscaledDeltaTime;

        // 進み具合。出た瞬間が 0、消える瞬間が 1
        float t = Mathf.Clamp01(elapsed / duration);

        // 前半で一気に広がり、後半で縮んで消える
        // （最初の 35% でなめらかに最大まで広がり、残りの 65% でまっすぐ縮む）
        float scale = t < 0.35f
            ? Mathf.SmoothStep(0f, 1f, t / 0.35f)
            : Mathf.Lerp(1f, 0f, (t - 0.35f) / 0.65f);

        transform.localScale = Vector3.one * (size * scale);

        if (t >= 1f)
        {
            Destroy(gameObject);
        }
    }
}
