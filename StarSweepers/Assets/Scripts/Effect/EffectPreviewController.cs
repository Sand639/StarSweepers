using System.Collections;
using UnityEngine;

/// <summary>
/// Animator / Animation / Particle System を使うエフェクトを再生する共通プレビュー。
/// 再生開始の遅れ・再生速度・表示時間はInspectorから調整できる。
///
/// **エフェクトの確認用シーン**（`fbxTest`、`EffectTest_〜`）に置いて使う部品。
/// シーンを再生すると自動で1回出し、画面左上の「再生」ボタンで何度でも出し直せる。
/// 出す物は「シーンに置いてある物（<see cref="targetEffect"/>）」か「プレハブから毎回作る物（<see cref="effectPrefab"/>）」のどちらか。
/// シーンを作るツール（<c>ExplosionEffectTestSceneBuilder</c> / <c>EffectPreviewSetupWindow</c>）が <see cref="Configure"/> で設定する。
/// </summary>
public sealed class EffectPreviewController : MonoBehaviour
{
    [Header("Effect")]
    [Tooltip("シーンに置いてあるエフェクト。出すたびに ON/OFF で出し直す")]
    [SerializeField] private GameObject targetEffect;

    [Tooltip("Target Effect が空のときに、出すたびに新しく作るプレハブ")]
    [SerializeField] private GameObject effectPrefab;

    [Tooltip("エフェクトの Animator に入れる再生用のコントローラー（任意）")]
    [SerializeField] private RuntimeAnimatorController animatorController;

    [Tooltip("「再生」を押してから出るまでの秒数")]
    [SerializeField, Min(0f)] private float startDelay;

    [Tooltip("再生速度（倍）。粒子・アニメーションの両方に効く")]
    [SerializeField, Min(0.01f)] private float playbackSpeed = 1f;

    [Tooltip("出している秒数。0 なら、粒子やアニメーションの長さから自動で決める")]
    [SerializeField, Min(0f)] private float visibleDuration;

    [Tooltip("シーンを再生したら、すぐ1回出す")]
    [SerializeField] private bool playOnStart = true;

    /// <summary>いま出しているエフェクト。targetEffect そのもの、またはプレハブから作った物。</summary>
    private GameObject currentEffect;

    /// <summary>再生中の流れ（<see cref="PlayAfterDelay"/>）。出し直すときに前のを止めるために持つ。</summary>
    private Coroutine playbackRoutine;

    /// <summary>シーンを作るツールが「設定済みか」を見るための読み取り口。</summary>
    public GameObject TargetEffect => targetEffect;

    /// <summary>シーンを作るツールが「設定済みか」を見るための読み取り口。</summary>
    public RuntimeAnimatorController AnimatorController => animatorController;

    /// <summary>シーンを作るツールから、出す物とコントローラーを設定する（シーンに置いてある物を使う形にする）。</summary>
    public void Configure(GameObject effect, RuntimeAnimatorController controller)
    {
        targetEffect = effect;
        effectPrefab = null;
        animatorController = controller;
        playOnStart = true;
    }

    /// <summary>画面の左上に、いまの設定と「再生」ボタンを出す（箱は 310×115 px）。</summary>
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
        if (!playOnStart)
        {
            return;
        }

        // シーンに置いてある物は、待ち時間の間は見えないように先に隠す
        if (targetEffect != null)
        {
            targetEffect.SetActive(false);
        }

        PlayEffect();
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

        // 前の再生が途中なら止めて、出ている物を片づけてから出し直す
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

    /// <summary>
    /// 再生の流れ：待つ → 出す → 粒子・Animator・古い Animation を最初から再生 → 表示時間が過ぎたら片づける。
    /// 表示時間が 0（自動）で長さが決められないときは、出しっぱなしにする。
    /// </summary>
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

        // 粒子：残っている粒を消してから、最初から出し直す
        ParticleSystem[] particleSystems = currentEffect.GetComponentsInChildren<ParticleSystem>(true);
        foreach (ParticleSystem particleSystem in particleSystems)
        {
            ParticleSystem.MainModule main = particleSystem.main;
            main.simulationSpeed = playbackSpeed;
            particleSystem.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            particleSystem.Play(true);
        }

        // Animator：Rebind と Update(0) で前回の再生の状態を捨て、最初のフレームに戻す。
        // クリップが1つも無いコントローラーで Play すると警告が出るので、あるときだけ再生する
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
            // Animator が無いのにコントローラーだけ指定されていたら、付けて再生する
            animator = currentEffect.AddComponent<Animator>();
            animator.runtimeAnimatorController = animatorController;
            animator.speed = playbackSpeed;
            animator.Rebind();
            animator.Update(0f);
            animator.Play(0, 0, 0f);
        }

        // 古い方式の Animation（Legacy）も、あれば最初から再生する
        Animation legacyAnimation = currentEffect.GetComponentInChildren<Animation>(true);
        if (legacyAnimation != null)
        {
            legacyAnimation.Rewind();
            legacyAnimation.Play();
        }

        float duration = visibleDuration > 0f ? visibleDuration : GetAutomaticDuration(
            particleSystems, animator, legacyAnimation, playbackSpeed);

        // 表示時間が過ぎたら、シーンの物は隠し、プレハブから作った物は消す
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

    /// <summary>
    /// 表示時間を自動で決める：粒子・Animator のクリップ・古い Animation のうち、いちばん長いもの（再生速度で割った秒数）。
    /// ループする粒子が1つでもあれば終わりが無いので 0（出しっぱなし）を返す。
    /// </summary>
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

            // 粒子は「出し続ける時間」＋「最後に出た粒が消えるまでの寿命」で見積もる。寿命がランダムなら最大値を使う
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
