using System.Collections;
using UnityEngine;

/// <summary>
/// Animator / Animation / Particle System を使うエフェクトを再生する共通プレビュー。
/// 再生開始の遅れ・再生速度・表示時間はInspectorから調整できる。
/// </summary>
public sealed class EffectPreviewController : MonoBehaviour
{
    [Header("Effect")]
    [SerializeField] private GameObject targetEffect;
    [SerializeField] private GameObject effectPrefab;
    [SerializeField] private RuntimeAnimatorController animatorController;
    [SerializeField, Min(0f)] private float startDelay;
    [SerializeField, Min(0.01f)] private float playbackSpeed = 1f;
    [SerializeField, Min(0f)] private float visibleDuration;
    [SerializeField] private bool playOnStart = true;

    private GameObject currentEffect;
    private Coroutine playbackRoutine;

    public GameObject TargetEffect => targetEffect;
    public RuntimeAnimatorController AnimatorController => animatorController;

    public void Configure(GameObject effect, RuntimeAnimatorController controller)
    {
        targetEffect = effect;
        effectPrefab = null;
        animatorController = controller;
        playOnStart = true;
    }

    private void OnGUI()
    {
        const int left = 20;
        const int top = 20;
        GUI.Box(new Rect(left, top, 310, 115), "エフェクト確認");
        GUI.Label(new Rect(left + 12, top + 30, 286, 22),
            $"待ち時間: {startDelay:0.00}秒   再生速度: {playbackSpeed:0.00}倍");
        GUI.Label(new Rect(left + 12, top + 53, 286, 22),
            $"表示時間: {(visibleDuration <= 0f ? "自動" : $"{visibleDuration:0.00}秒")}");
        if (GUI.Button(new Rect(left + 12, top + 78, 286, 26), "再生 / もう一度再生"))
        {
            PlayEffect();
        }
    }

    private void Start()
    {
        if (playOnStart)
        {
            if (targetEffect != null)
            {
                targetEffect.SetActive(false);
            }

            PlayEffect();
        }
    }

    /// <summary>Inspectorのコンテキストメニュー、またはUIからエフェクトを再生する。</summary>
    [ContextMenu("Play Effect")]
    public void PlayEffect()
    {
        if (targetEffect == null && effectPrefab == null)
        {
            Debug.LogError("Target Effect または Effect Prefab を設定してください。", this);
            return;
        }

        if (playbackRoutine != null)
        {
            StopCoroutine(playbackRoutine);
        }

        if (targetEffect != null)
        {
            targetEffect.SetActive(false);
        }
        else if (currentEffect != null)
        {
            Destroy(currentEffect);
        }

        playbackRoutine = StartCoroutine(PlayAfterDelay());
    }

    private IEnumerator PlayAfterDelay()
    {
        if (startDelay > 0f)
        {
            yield return new WaitForSeconds(startDelay);
        }

        currentEffect = targetEffect != null
            ? targetEffect
            : Instantiate(effectPrefab, transform.position, transform.rotation);
        currentEffect.SetActive(true);

        ParticleSystem[] particleSystems = currentEffect.GetComponentsInChildren<ParticleSystem>(true);
        foreach (ParticleSystem particleSystem in particleSystems)
        {
            ParticleSystem.MainModule main = particleSystem.main;
            main.simulationSpeed = playbackSpeed;
            particleSystem.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            particleSystem.Play(true);
        }

        Animator animator = currentEffect.GetComponentInChildren<Animator>(true);
        if (animator != null)
        {
            if (animatorController != null)
            {
                animator.runtimeAnimatorController = animatorController;
            }

            animator.speed = playbackSpeed;
            animator.Rebind();
            animator.Update(0f);
            if (animator.runtimeAnimatorController != null &&
                animator.runtimeAnimatorController.animationClips.Length > 0)
            {
                animator.Play(0, 0, 0f);
            }
        }
        else if (animatorController != null)
        {
            animator = currentEffect.AddComponent<Animator>();
            animator.runtimeAnimatorController = animatorController;
            animator.speed = playbackSpeed;
            animator.Rebind();
            animator.Update(0f);
            animator.Play(0, 0, 0f);
        }

        Animation legacyAnimation = currentEffect.GetComponentInChildren<Animation>(true);
        if (legacyAnimation != null)
        {
            legacyAnimation.Rewind();
            legacyAnimation.Play();
        }

        float duration = visibleDuration > 0f ? visibleDuration : GetAutomaticDuration(
            particleSystems, animator, legacyAnimation, playbackSpeed);

        if (duration > 0f)
        {
            yield return new WaitForSeconds(duration);
            if (currentEffect != null && targetEffect != null)
            {
                currentEffect.SetActive(false);
            }
            else if (currentEffect != null)
            {
                Destroy(currentEffect);
                currentEffect = null;
            }
        }

        playbackRoutine = null;
    }

    private static float GetAutomaticDuration(ParticleSystem[] systems, Animator animator,
        Animation legacyAnimation, float speed)
    {
        float duration = 0f;
        float safeSpeed = Mathf.Max(0.01f, Mathf.Abs(speed));

        foreach (ParticleSystem system in systems)
        {
            ParticleSystem.MainModule main = system.main;
            if (main.loop)
            {
                return 0f;
            }

            float particleLife = main.startLifetime.mode == ParticleSystemCurveMode.Constant
                ? main.startLifetime.constant
                : main.startLifetime.constantMax;
            duration = Mathf.Max(duration, (main.duration + particleLife) / safeSpeed);
        }

        if (animator != null && animator.runtimeAnimatorController != null)
        {
            foreach (AnimationClip clip in animator.runtimeAnimatorController.animationClips)
            {
                duration = Mathf.Max(duration, clip.length / safeSpeed);
            }
        }

        if (legacyAnimation != null)
        {
            foreach (AnimationState state in legacyAnimation)
            {
                duration = Mathf.Max(duration, state.length / safeSpeed);
            }
        }

        return duration;
    }
}
