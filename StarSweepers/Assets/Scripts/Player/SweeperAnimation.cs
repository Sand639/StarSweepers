using UnityEngine;

/// <summary>
/// **プレイヤー（本実装）のアニメーションの切り替え口。**（2026/10/27・小野田さんの仕様）
///
/// ・狙い始めたら <c>A_AIM_01</c>
/// ・光線を撃った瞬間に <c>A_SHOOT</c> を流し、**終わったら <c>A_AIM_01</c> に戻る**
/// ・投げ終わったら Idle State（空なら何もしない。Animator の流れに任せる）
///
/// **キャラクターのモデルとアニメーションはまだ無い**（いまはカプセル）。
/// Animator が無い・その名前の状態が無いときは、何もしない（エラーにもしない）。
/// モデルが入ったら、体の子にモデルを置いて Animator を付け、状態の名前をここの欄に合わせればよい。
/// </summary>
public class SweeperAnimation : MonoBehaviour
{
    [Tooltip("動かす Animator。空なら体の子から探す")]
    [SerializeField] private Animator animator;

    [Header("状態の名前（Animator の中の名前）")]
    [Tooltip("撃った瞬間に流す状態")]
    [SerializeField] private string shootState = "A_SHOOT";

    [Tooltip("狙っている間の状態。撃ったあとも、ここへ戻る")]
    [SerializeField] private string aimState = "A_AIM_01";

    [Tooltip("投げ終わったときに戻す状態。空なら何もしない")]
    [SerializeField] private string idleState = "";

    [Tooltip("歩く速さを渡す float のパラメーター名。空・無ければ渡さない")]
    [SerializeField] private string speedParameter = "Speed";

    [Tooltip("状態を切り替えるときのつなぎの秒数")]
    [Min(0f)]
    [SerializeField] private float crossFade = 0.05f;

    private SweeperMover mover;

    /// <summary>A_SHOOT を流していて、終わったら A_AIM_01 に戻す待ちか。</summary>
    private bool waitingShootEnd;

    private int shootHash;
    private int speedHash;
    private bool hasSpeed;

    private void Awake()
    {
        if (animator == null)
        {
            animator = GetComponentInChildren<Animator>();
        }

        mover = GetComponent<SweeperMover>();
        shootHash = Animator.StringToHash(shootState);

        if (animator != null && !string.IsNullOrEmpty(speedParameter))
        {
            speedHash = Animator.StringToHash(speedParameter);
            foreach (AnimatorControllerParameter parameter in animator.parameters)
            {
                if (parameter.nameHash == speedHash && parameter.type == AnimatorControllerParameterType.Float)
                {
                    hasSpeed = true;
                }
            }
        }
    }

    /// <summary>狙い始めた（A_AIM_01）。</summary>
    public void PlayAim()
    {
        waitingShootEnd = false;
        Play(aimState);
    }

    /// <summary>撃った（A_SHOOT を流し、終わったら A_AIM_01 に戻る）。</summary>
    public void PlayShoot()
    {
        waitingShootEnd = Play(shootState);
    }

    /// <summary>投げ終わった・やめた。</summary>
    public void PlayIdle()
    {
        waitingShootEnd = false;
        Play(idleState);
    }

    private void Update()
    {
        if (animator == null || !animator.isActiveAndEnabled)
        {
            return;
        }

        if (hasSpeed && mover != null)
        {
            animator.SetFloat(speedHash, mover.CurrentSpeed);
        }

        if (!waitingShootEnd || animator.IsInTransition(0))
        {
            return;
        }

        AnimatorStateInfo state = animator.GetCurrentAnimatorStateInfo(0);
        if (state.shortNameHash == shootHash && state.normalizedTime >= 1f)
        {
            waitingShootEnd = false;
            Play(aimState);
        }
    }

    /// <summary>その名前の状態があれば切り替える。切り替えたら true。</summary>
    private bool Play(string stateName)
    {
        if (animator == null || !animator.isActiveAndEnabled || string.IsNullOrEmpty(stateName))
        {
            return false;
        }

        int hash = Animator.StringToHash(stateName);
        if (!animator.HasState(0, hash))
        {
            return false;
        }

        animator.CrossFadeInFixedTime(hash, crossFade, 0, 0f);
        return true;
    }
}
