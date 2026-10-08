using System;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

/// <summary>試合の残り時間を円形ゲージと文字で表示する。</summary>
public sealed class GameTimerUI : MonoBehaviour
{
    [Header("表示部品")]
    [SerializeField] private Image backdropImage;
    [SerializeField] private Image gaugeImage;
    [SerializeField] private Text timeText;

    [Header("タイマー")]
    [SerializeField, Min(0.01f)] private float maxTimeSeconds = 120f;
    [SerializeField] private bool runOnStart = true;

    [Header("残り時間の割合と色")]
    [SerializeField, Range(0f, 1f)] private float middlePhaseThreshold = 0.66f;
    [SerializeField, Range(0f, 1f)] private float finalPhaseThreshold = 0.33f;
    [SerializeField] private Color initialPhaseColor = new Color(0.16f, 0.82f, 0.86f, 1f);
    [SerializeField] private Color middlePhaseColor = new Color(1f, 0.61f, 0.16f, 1f);
    [SerializeField] private Color finalPhaseColor = new Color(0.86f, 0.19f, 0.16f, 1f);
    [SerializeField] private UnityEvent<int> onPhaseChanged = new UnityEvent<int>();

    /// <summary>フェーズが変わったとき通知する。フェーズ番号は初期=0、中盤=1、終盤=2。</summary>
    public event Action<int> PhaseChanged;

    public float RemainingSeconds { get; private set; }
    public float Progress { get; private set; }
    public int CurrentPhase { get; private set; }

    private Material runtimeGaugeMaterial;
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

        SetRemainingTime(RemainingSeconds - Time.deltaTime);
    }

    private void OnDestroy()
    {
        if (runtimeGaugeMaterial != null)
        {
            Destroy(runtimeGaugeMaterial);
        }
    }

    public void ResetTimer()
    {
        RemainingSeconds = Mathf.Max(0f, maxTimeSeconds);
        Progress = 1f;
        CurrentPhase = GetPhase(Progress);
        UpdateVisuals(Progress);
    }

    public void Resume() => enabled = true;
    public void Pause() => enabled = false;

    public void SetRemainingTime(float seconds)
    {
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
            gaugeImage.fillClockwise = true;
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
            int totalSeconds = Mathf.CeilToInt(RemainingSeconds);
            timeText.text = $"{totalSeconds / 60}:{totalSeconds % 60:00}";
        }
    }

    private int GetPhase(float progress)
    {
        float middleThreshold = Mathf.Max(finalPhaseThreshold, middlePhaseThreshold);
        return progress > middleThreshold ? 0 : progress > finalPhaseThreshold ? 1 : 2;
    }

    private Color GetColor(int phase) => phase switch
    {
        0 => initialPhaseColor,
        1 => middlePhaseColor,
        _ => finalPhaseColor
    };
}
