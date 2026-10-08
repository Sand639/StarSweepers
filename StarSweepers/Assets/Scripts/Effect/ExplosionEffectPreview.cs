using System.Collections;
using UnityEngine;

/// <summary>
/// 爆発FBXの表示と、再生タイミングを確認するためのシンプルなプレビュー。
/// 再生開始の遅れ・再生速度・表示時間はInspectorから調整できる。
/// </summary>
public sealed class ExplosionEffectPreview : MonoBehaviour
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

    private void OnGUI()
    {
        const int left = 20;
        const int top = 20;
        GUI.Box(new Rect(left, top, 310, 115), "Explosion FBX Preview");
        GUI.Label(new Rect(left + 12, top + 30, 286, 22),
            $"Delay: {startDelay:0.00}s   Speed: {playbackSpeed:0.00}x");
        GUI.Label(new Rect(left + 12, top + 53, 286, 22),
            $"Visible: {(visibleDuration <= 0f ? "Animation length" : $"{visibleDuration:0.00}s")}");
        if (GUI.Button(new Rect(left + 12, top + 78, 286, 26), "Play / Replay"))
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
    [ContextMenu("Play Explosion")]
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
        Animator animator = currentEffect.GetComponentInChildren<Animator>();
        if (animator != null)
        {
            if (animatorController != null)
            {
                animator.runtimeAnimatorController = animatorController;
            }

            animator.speed = playbackSpeed;
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
            animator.Update(0f);
            animator.Play(0, 0, 0f);
        }

        float duration = visibleDuration;
        if (duration <= 0f && animator != null && animator.runtimeAnimatorController != null &&
            animator.runtimeAnimatorController.animationClips.Length > 0)
        {
            duration = animator.runtimeAnimatorController.animationClips[0].length /
                       Mathf.Max(0.01f, Mathf.Abs(playbackSpeed));
        }

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
}
