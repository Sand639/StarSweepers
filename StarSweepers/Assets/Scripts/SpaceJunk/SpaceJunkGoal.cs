using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// **素材を投げ入れるゴール1つ分。** ステージの四方に4つ置く。
///
/// ## 誰の点になるか
///
/// **そのゴールを持つチームのもの。** 誰が投げ入れても持ち主のチームに入る。
/// つまり「自分たちの陣地へ運ぶ」形になり、**間違えて相手のゴールへ入れると相手を助けてしまう。**
///
/// 釣りにあった「どのチームでも入る共通ゴール」は無い。
/// **3チームのときは、余る1つのゴールを閉ざす**（入れても何も起きない）。
///
/// ## 判定するのはホストだけ
///
/// 参加者のPCでは何も数えない（それぞれが数えると、同じ素材で何度も数えてしまう）。
/// 数えるのは <see cref="SpaceJunkRound"/>。
///
/// フックで引っ張ったまま持ち込んだ素材は数えない（引きずり込みで稼げないようにするため）。
/// </summary>
[RequireComponent(typeof(Collider))]
[RequireComponent(typeof(NetworkObject))]
public class SpaceJunkGoal : NetworkBehaviour
{
    /// <summary>シーンにあるゴール全部。<see cref="SpaceJunkRound"/> が持ち主を配るために使う。</summary>
    public static readonly List<SpaceJunkGoal> All = new List<SpaceJunkGoal>();

    [Header("このゴールの場所")]
    [Tooltip("ゴールの並び順の番号（0=北 / 1=東 / 2=南 / 3=西）。持ち主の割り当てに使う")]
    [SerializeField] private int goalIndex = 0;

    [Header("見た目")]
    [Tooltip("チームの色に塗る床の Renderer")]
    [SerializeField] private Renderer padRenderer;

    [Tooltip("新しい種類が入ったときに一瞬光らせる色")]
    [SerializeField] private Color flashColor = Color.white;

    [Tooltip("光っている秒数")]
    [SerializeField] private float flashSeconds = 0.4f;

    /// <summary>ゴールの並び順の番号。</summary>
    public int GoalIndex => goalIndex;

    /// <summary>このゴールを持っているチーム。<see cref="SpaceJunkTeams.ClosedGoal"/> なら閉ざされている。</summary>
    public int OwnerTeam { get; private set; } = SpaceJunkTeams.ClosedGoal;

    /// <summary>閉ざされたゴールか（3チームのときに1つだけ出る）。</summary>
    public bool IsClosed => OwnerTeam == SpaceJunkTeams.ClosedGoal;

    private Color teamColor = Color.white;
    private float flashUntil = -999f;

    private void Awake()
    {
        GetComponent<Collider>().isTrigger = true;

        // **入れ忘れていたら、子どもの見た目を自動で使う。**
        // 入っていないとチームの色が出ず、**自分のゴールが見分けられない**
        if (padRenderer == null)
        {
            padRenderer = GetComponentInChildren<Renderer>();
        }

        if (padRenderer == null)
        {
            Debug.LogWarning(
                $"{name}: チームの色に塗る床（Pad Renderer）がありません。" +
                "ゴールが何色か分からなくなるので、**色を塗る板を子どもに置いてください。**", this);
        }
    }

    private void OnEnable()
    {
        if (!All.Contains(this))
        {
            All.Add(this);
        }

        // **ラウンドの進行役が先に生まれていたら、その時点の持ち主をもらう。**
        // 生まれる順番はシーンによって変わるので、両側から取りに行く形にしてある
        if (SpaceJunkRound.Current != null)
        {
            ApplyOwnerTeam(SpaceJunkRound.Current.OwnerTeamOfGoal(goalIndex));
        }
    }

    private void OnDisable()
    {
        All.Remove(this);
    }

    /// <summary>持ち主のチームを受け取り、床の色を変える。<see cref="SpaceJunkRound"/> から呼ばれる。</summary>
    public void ApplyOwnerTeam(int team)
    {
        OwnerTeam = team;
        teamColor = SpaceJunkTeams.TeamColor(team);

        if (padRenderer != null)
        {
            padRenderer.material.color = teamColor;
        }
    }

    /// <summary>光っている間（素材が入った直後）だけ Flash Color、それ以外はチームの色で床を塗る。</summary>
    private void Update()
    {
        if (padRenderer == null)
        {
            return;
        }

        bool lit = Time.time < flashUntil;
        padRenderer.material.color = lit ? flashColor : teamColor;
    }

    private void OnTriggerEnter(Collider other)
    {
        TryCollect(other);
    }

    /// <summary>
    /// **中に入ったまま、あとから数えられる状態になった物も拾う**（2026/9/29）。
    /// 重いデブリはフックで引きずられて（つかまれたまま）ゴールに入り、中で手を離されるので、
    /// 入った瞬間（OnTriggerEnter）だけ見ていると数えそこねる。
    /// </summary>
    private void OnTriggerStay(Collider other)
    {
        TryCollect(other);
    }

    private void TryCollect(Collider other)
    {
        // **数えるのはホストだけ。**
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer)
        {
            return;
        }

        // 閉ざされたゴールは何もしない（素材もそのまま残す）
        if (IsClosed)
        {
            return;
        }

        HookableObject item = other.GetComponentInParent<HookableObject>();

        if (item == null || item.IsHooked || item.IsVanished)
        {
            return;
        }

        SpaceJunkMaterial material = item.GetComponent<SpaceJunkMaterial>();

        // 素材以外（宇宙ごみの飾りなど）は数えない
        if (material == null)
        {
            return;
        }

        FishingNetSupply netSupply = item.GetComponent<FishingNetSupply>();

        // 引っ掛けたまま持ち込まれたものは数えない
        if (netSupply != null && netSupply.IsClaimed)
        {
            return;
        }

        // **もう持っている種類でも、素材は消える（何も起きない）。**
        bool counted = SpaceJunkRound.Current != null
                    && SpaceJunkRound.Current.ServerCollect(OwnerTeam, material.Kind, item.GetComponent<NetworkObject>());

        netSupply?.ClearLastThrower();

        if (counted)
        {
            FlashClientRpc();
        }

        item.Vanish();
    }

    /// <summary>全員の画面で床を一瞬光らせる。</summary>
    [ClientRpc]
    private void FlashClientRpc()
    {
        flashUntil = Time.time + flashSeconds;
    }
}
