using System;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

/// <summary>
/// **試合の残り時間を、円形ゲージと「分:秒」の文字で表示する。**
///
/// 残り時間の割合に応じて3つの段階（フェーズ）に分かれ、ゲージの色が変わる。
/// 段階が変わった瞬間は、イベント（<see cref="PhaseChanged"/> と On Phase Changed）で外に知らせる。
///
/// | 段階 | 番号 | 残りの割合 |
/// | 初期 | 0 | Middle Phase Threshold より多い |
/// | 中盤 | 1 | Final Phase Threshold より多い |
/// | 終盤 | 2 | それ以下 |
///
/// 検証シーンは `Assets/Scenes/Test/GameTimerUITest.unity`、プレハブは `Assets/Resources/GameTimerUI.prefab`
/// （どちらも <see cref="GameTimerUISetup"/> で作る）。
/// 今は 2 分の仮のカウントダウンで、試合の設定や通信とはまだつないでいない。
/// </summary>
public sealed class GameTimerUI : MonoBehaviour
{
    [Header("表示部品")]
    [Tooltip("ゲージの後ろに置く背景の画像")]
    [SerializeField] private Image backdropImage;

    [Tooltip("残り時間に合わせて欠けていく円形のゲージ（Filled の Image）")]
    [SerializeField] private Image gaugeImage;

    [Tooltip("「分:秒」を出す文字（TextMeshPro）")]
    [SerializeField] private TMP_Text timeText;

    [Header("タイマー")]
    [Tooltip("最初の残り時間（秒）")]
    [SerializeField, Min(0.01f)] private float maxTimeSeconds = 120f;

    [Tooltip("ON：再生したら自動で数え始める。OFF：Resume() を呼ぶまで止まっている")]
    [SerializeField] private bool runOnStart = true;

    [Header("残り時間の割合と色")]
    [Tooltip("残りの割合（1 = 満タン）がこれ以下になると「中盤」の色になる")]
    [SerializeField, Range(0f, 1f)] private float middlePhaseThreshold = 0.66f;

    [Tooltip("残りの割合がこれ以下になると「終盤」の色になる")]
    [SerializeField, Range(0f, 1f)] private float finalPhaseThreshold = 0.33f;

    [Tooltip("初期（残りが多いとき）のゲージの色")]
    [SerializeField] private Color initialPhaseColor = new Color(0.56f, 0.71f, 0.67f, 1f);

    [Tooltip("中盤のゲージの色")]
    [SerializeField] private Color middlePhaseColor = new Color(0.92f, 0.68f, 0.30f, 1f);

    [Tooltip("終盤（残りが少ないとき）のゲージの色")]
    [SerializeField] private Color finalPhaseColor = new Color(0.51f, 0.24f, 0.18f, 1f);

    [Tooltip("段階が変わったときに呼ぶ。渡る数は 初期=0、中盤=1、終盤=2")]
    [SerializeField] private UnityEvent<int> onPhaseChanged = new UnityEvent<int>();

    /// <summary>フェーズが変わったとき通知する。フェーズ番号は初期=0、中盤=1、終盤=2。</summary>
    public event Action<int> PhaseChanged;

    /// <summary>いまの残り時間（秒）。0 になったら止まる。</summary>
    public float RemainingSeconds { get; private set; }

    /// <summary>残り時間の割合（1 = 満タン、0 = 時間切れ）。</summary>
    public float Progress { get; private set; }

    /// <summary>いまの段階（初期=0、中盤=1、終盤=2）。</summary>
    public int CurrentPhase { get; private set; }

    /// <summary>
    /// ゲージ用の、この表示だけの材質。
    /// 元の材質をそのまま書き換えると、同じ材質を使う他の物まで一緒に変わってしまうので、複製して使う。
    /// </summary>
    private Material runtimeGaugeMaterial;

    // ゲージのシェーダーに渡す値の名前（シェーダー側にこの名前の項目があるときだけ渡す）
    private static readonly int ProgressProperty = Shader.PropertyToID("_Progress");
    private static readonly int TimerColorProperty = Shader.PropertyToID("_TimerColor");

    private void Awake()
    {
        if (gaugeImage != null && gaugeImage.material != null)
        {
            runtimeGaugeMaterial = new Material(gaugeImage.material);
            gaugeImage.material = runtimeGaugeMaterial;
        }

        ResetTimer();
    }

    private void Start()
    {
        if (runOnStart)
        {
            Resume();
        }
    }

    private void Update()
    {
        if (RemainingSeconds <= 0f)
        {
            return;
        }

        // Time.deltaTime なので、ポーズで世界の時間が止まっている間（timeScale = 0）はタイマーも止まる
        SetRemainingTime(RemainingSeconds - Time.deltaTime);
    }

    private void OnDestroy()
    {
        if (runtimeGaugeMaterial != null)
        {
            Destroy(runtimeGaugeMaterial);
        }
    }

    /// <summary>残り時間を最初（Max Time Seconds）に戻す。段階が変わっても、ここではイベントを出さない。</summary>
    public void ResetTimer()
    {
        RemainingSeconds = Mathf.Max(0f, maxTimeSeconds);
        Progress = 1f;
        CurrentPhase = GetPhase(Progress);
        UpdateVisuals(Progress);
    }

    /// <summary>数え始める（再開する）。部品の有効・無効で止めているだけなので、残り時間はそのまま。</summary>
    public void Resume() => enabled = true;

    /// <summary>数えるのを止める。表示は最後の値のまま残る。</summary>
    public void Pause() => enabled = false;

    /// <summary>
    /// 残り時間を決め直す（0 〜 Max Time Seconds に収める）。
    /// 段階が変わったらイベントを出し、表示も更新する。外から時間を同期したいときにも使える。
    /// </summary>
    public void SetRemainingTime(float seconds)
    {
        // Max(0.01f, …) は、最大時間が 0 のときに 0 で割らないための用心
        RemainingSeconds = Mathf.Clamp(seconds, 0f, Mathf.Max(0.01f, maxTimeSeconds));
        Progress = Mathf.Clamp01(RemainingSeconds / Mathf.Max(0.01f, maxTimeSeconds));

        int nextPhase = GetPhase(Progress);
        if (nextPhase != CurrentPhase)
        {
            CurrentPhase = nextPhase;
            onPhaseChanged?.Invoke(CurrentPhase);
            PhaseChanged?.Invoke(CurrentPhase);
        }

        UpdateVisuals(Progress);
    }

    /// <summary>ゲージ、文字、将来のシェーダー用値をまとめて更新する。</summary>
    public void UpdateVisuals(float progress)
    {
        progress = Mathf.Clamp01(progress);
        Color phaseColor = GetColor(GetPhase(progress));

        if (gaugeImage != null)
        {
            gaugeImage.type = Image.Type.Filled;
            gaugeImage.fillMethod = Image.FillMethod.Radial360;
            gaugeImage.fillOrigin = (int)Image.Origin360.Top;
            // 残り量を上から時計回りに描くため、減少時は空きが時計回りに広がる。
            gaugeImage.fillClockwise = false;
            gaugeImage.fillAmount = progress;
            gaugeImage.color = phaseColor;

            if (runtimeGaugeMaterial != null)
            {
                if (runtimeGaugeMaterial.HasProperty(ProgressProperty))
                    runtimeGaugeMaterial.SetFloat(ProgressProperty, progress);
                if (runtimeGaugeMaterial.HasProperty(TimerColorProperty))
                    runtimeGaugeMaterial.SetColor(TimerColorProperty, phaseColor);
            }
        }

        if (timeText != null)
        {
            // 切り上げにして、残り 0.3 秒でも「0:01」と出す（0:00 になるのは本当に時間切れのときだけ）
            int totalSeconds = Mathf.CeilToInt(RemainingSeconds);
            timeText.text = $"{totalSeconds / 60}:{totalSeconds % 60:00}";
        }
    }

    /// <summary>残りの割合から段階（初期=0、中盤=1、終盤=2）を決める。</summary>
    private int GetPhase(float progress)
    {
        // 中盤のしきい値が終盤より小さく設定されていても壊れないよう、大きいほうを中盤の境にする
        float middleThreshold = Mathf.Max(finalPhaseThreshold, middlePhaseThreshold);
        return progress > middleThreshold ? 0 : progress > finalPhaseThreshold ? 1 : 2;
    }

    /// <summary>段階に対応するゲージの色。</summary>
    private Color GetColor(int phase) => phase switch
    {
        0 => initialPhaseColor,
        1 => middlePhaseColor,
        _ => finalPhaseColor
    };
}
