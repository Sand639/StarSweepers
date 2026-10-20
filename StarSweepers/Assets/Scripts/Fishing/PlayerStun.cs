using UnityEngine;

/// <summary>
/// **プレイヤーのスタン（動けない状態）を管理する。**
///
/// 爆発に巻き込まれると、ここに <see cref="Stun"/> が呼ばれて一定時間動けなくなる。
/// スタン中だと分かるように、体を赤く点滅させ、頭の上に印を出す。
///
/// 「動けない」の判定を持つだけで、移動を止めるのは <see cref="FishingPlayerController"/>、
/// フックを止めるのは <see cref="HookController"/> の側で <see cref="IsStunned"/> を見て行う。
/// </summary>
public class PlayerStun : MonoBehaviour
{
    [Header("見せ方")]
    [Tooltip("スタン中に赤く点滅させる見た目。プレイヤーの体の Renderer を入れる")]
    [SerializeField] private Renderer[] bodyRenderers;

    [Tooltip("点滅するときの色")]
    [SerializeField] private Color stunColor = new Color(1f, 0.2f, 0.15f);

    [Tooltip("1秒に何回点滅させるか")]
    [SerializeField] private float blinkPerSecond = 6f;

    [Tooltip("スタン中だけ出す、頭の上の印")]
    [SerializeField] private GameObject stunMarker;

    [Tooltip("頭の上の印を回す速さ（1秒あたりの度）")]
    [SerializeField] private float markerSpinSpeed = 240f;

    /// <summary>いま動けない状態か。</summary>
    public bool IsStunned => remaining > 0f;

    /// <summary>スタンが解けるまでの残り秒数。</summary>
    public float Remaining => Mathf.Max(0f, remaining);

    private float remaining;

    // 点滅が終わったら戻す色。
    // **スタンが始まる瞬間に、その時点の色を控える。**
    // Awake で控えると、あとからチームの色（FishingNetPlayer）が塗られても
    // プレハブの元の色（青）に戻ってしまうため
    private Color[] originalColors = new Color[0];

    private void Awake()
    {
        // 入れ忘れでも、点滅の for 文が落ちないよう空の配列にしておく
        if (bodyRenderers == null)
        {
            bodyRenderers = new Renderer[0];
        }

        if (stunMarker != null)
        {
            stunMarker.SetActive(false);
        }
    }

    /// <summary>いまの体の色を控える。<see cref="Stun"/> の最初の1回で呼ばれる（理由は <see cref="originalColors"/> のコメント）。</summary>
    private void CaptureColors()
    {
        originalColors = new Color[bodyRenderers.Length];
        for (int i = 0; i < bodyRenderers.Length; i++)
        {
            originalColors[i] = bodyRenderers[i] != null
                ? bodyRenderers[i].material.color
                : Color.white;
        }
    }

    /// <summary>
    /// この秒数だけ動けなくする。
    /// すでにスタン中なら、**残りが長いほうを採用**する（短い爆発で上書きしない）。
    /// </summary>
    public void Stun(float seconds)
    {
        // スタン中に重ねて食らったときは控え直さない（点滅中の赤を控えてしまうため）
        if (!IsStunned)
        {
            CaptureColors();
        }

        remaining = Mathf.Max(remaining, seconds);

        if (stunMarker != null)
        {
            stunMarker.SetActive(true);
        }
    }

    private void Update()
    {
        if (remaining <= 0f)
        {
            return;
        }

        remaining -= Time.deltaTime;

        // このフレームで解けた：色と印を元に戻して終わり
        if (remaining <= 0f)
        {
            remaining = 0f;
            RestoreColors();

            if (stunMarker != null)
            {
                stunMarker.SetActive(false);
            }
            return;
        }

        // まだスタン中：体を点滅させ、頭の上の印を回す
        Blink();

        if (stunMarker != null)
        {
            stunMarker.transform.Rotate(Vector3.up, markerSpinSpeed * Time.deltaTime, Space.World);
        }
    }

    /// <summary>体を赤と元の色で交互に塗る。</summary>
    private void Blink()
    {
        // 1回の点滅（1 ÷ blinkPerSecond 秒）のうち、前半を赤、後半を元の色にする
        bool on = Mathf.Repeat(Time.time * blinkPerSecond, 1f) < 0.5f;

        for (int i = 0; i < bodyRenderers.Length; i++)
        {
            if (bodyRenderers[i] == null)
            {
                continue;
            }
            bodyRenderers[i].material.color = on ? stunColor : originalColors[i];
        }
    }

    private void RestoreColors()
    {
        for (int i = 0; i < bodyRenderers.Length; i++)
        {
            if (bodyRenderers[i] == null)
            {
                continue;
            }
            bodyRenderers[i].material.color = originalColors[i];
        }
    }
}
