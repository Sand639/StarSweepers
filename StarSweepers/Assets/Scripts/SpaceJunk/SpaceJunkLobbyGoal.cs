using Unity.Netcode;
using UnityEngine;

/// <summary>
/// **ロビー（試合前）の練習エリアに置く簡易ゴール。**
/// チーム分けや得点計算は一切せず、素材が入ってきたら「消える（Vanish）」挙動だけを再現する。
/// </summary>
[RequireComponent(typeof(Collider))]
[RequireComponent(typeof(NetworkObject))]
public class SpaceJunkLobbyGoal : NetworkBehaviour
{
    [Header("見た目")]
    [Tooltip("床の Renderer（色を固定したり光らせたりする用）")]
    [SerializeField] private Renderer padRenderer;

    [Tooltip("ロビーでのゴールの基本色（練習用なので緑や黄色などお好みで）")]
    [SerializeField] private Color defaultColor = new Color(0.2f, 0.6f, 1f); // さわやかな水色

    [Tooltip("素材が入ったときに一瞬光らせる色")]
    [SerializeField] private Color flashColor = Color.white;

    [Tooltip("光っている秒数")]
    [SerializeField] private float flashSeconds = 0.4f;

    private float flashUntil = -999f;

    private void Awake()
    {
        // トリガーに強制設定
        GetComponent<Collider>().isTrigger = true;

        if (padRenderer == null)
        {
            padRenderer = GetComponentInChildren<Renderer>();
        }
    }

    private void Start()
    {
        // 初期色を設定
        if (padRenderer != null)
        {
            padRenderer.material.color = defaultColor;
        }
    }

    private void Update()
    {
        if (padRenderer == null) return;

        // ゴールした時のピカッと光る演出の更新
        bool lit = Time.time < flashUntil;
        padRenderer.material.color = lit ? flashColor : defaultColor;
    }

    private void OnTriggerEnter(Collider other)
    {
        TryCollect(other);
    }

    private void OnTriggerStay(Collider other)
    {
        TryCollect(other);
    }

    private void TryCollect(Collider other)
    {
        // 判定・消去はホスト（サーバー）だけが責任を持って行う
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer)
        {
            return;
        }

        // 親オブジェクトから HookableObject を探す
        HookableObject item = other.GetComponentInParent<HookableObject>();

        // アイテムが無い、あるいは既に消滅処理中なら何もしない
        if (item == null || item.IsVanished)
        {
            return;
        }

        // ⚠ 試合中とは違い、練習用なので「フックで引っ張ったまま持ち込んだ場合」でも
        // 手を離した瞬間に綺麗に消えるように、item.IsHooked のガードはあえて外しています。

        // 全員の画面でゴールをピカッと光らせる
        FlashClientRpc();

        // アイテムを消去（同期して消える）
        Debug.Log($"[LOBBY_GOAL] {item.name} がロビーゴールに入ったため、消去します。");
        item.Vanish();
    }

    /// <summary>
    /// 全員の画面で床を一瞬光らせる演出の同期
    /// </summary>
    [ClientRpc]
    private void FlashClientRpc()
    {
        flashUntil = Time.time + flashSeconds;
    }
}
