using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
public class NameChangeTerminal : MonoBehaviour
{
    /// <summary>ロビーに置かれている端末。画面を描くほうから探すために持っている。</summary>
    public static NameChangeTerminal Current { get; private set; }

    [Header("操作")]
    [Tooltip("この距離まで近づくと使える（メートル）")]
    [SerializeField] private float interactRange = 3f;

    [Tooltip("名前をランダムに変えるキー")]
    [SerializeField] private Key interactKey = Key.E;

    [Tooltip("名前をランダムに変えるボタン（South＝Xbox の A）")]
    [SerializeField] private GamepadButton interactButton = GamepadButton.South;

    [Header("見た目")]
    [Tooltip("近づいたときに色を変える見た目。空でもよい")]
    [SerializeField] private Renderer highlightRenderer;

    [Tooltip("自分のプレイヤーが近づいているときの色")]
    [SerializeField] private Color nearColor = new Color(0.4f, 1f, 0.6f);


    /// <summary>このPCで操作しているプレイヤーが近くにいるか。</summary>
    public bool IsPlayerNearby { get; private set; }

    // 既存の表示側との互換用。参加者も端末を使える。
    public bool IsHostNearby => IsPlayerNearby;

    /// <summary>押すキーとボタンの名前（案内の文言に使う。例：「E／A」）。</summary>
    public string InteractKeyName => $"{interactKey}／{GamepadInput.Label(interactButton)}";

    /// <summary>近づいていないときに戻す、見た目の元の色。</summary>
    private Color originalColor = Color.white;

    private void Awake()
    {
        Current = this;

        if (highlightRenderer != null && highlightRenderer.sharedMaterial != null)
        {
            originalColor = highlightRenderer.sharedMaterial.color;
        }
    }

    private void OnDestroy()
    {
        if (Current == this)
        {
            Current = null;
        }
    }



    private void Update()
    {
        IsPlayerNearby = false;
        bool blocked = GamePause.BlocksInput || SpaceJunkLobbyScreen.IsTyping
            || (SpaceJunkLobbyTerminal.Current != null && SpaceJunkLobbyTerminal.Current.IsOpen);

        foreach (FishingNetPlayer player in FishingNetPlayer.All)
        {
            if (!CanUse(player)) continue;
            IsPlayerNearby = true;

            // 1台で複数人のときも、その人の機器で押した入力だけを使う。
            if (!blocked && PlayerInputSource.Get(player).WasPressed(interactKey, interactButton))
            {
                TryRandomName(player);
            }
        }

        if (highlightRenderer != null && highlightRenderer.sharedMaterial != null)
        {
            highlightRenderer.material.color = IsPlayerNearby ? nearColor : originalColor;
        }
    }

    /// <summary>近くにいる本人だけの名前を抽選し、既存のPlayer経由で全員へ同期する。</summary>
    public bool TryRandomName(FishingNetPlayer player)
    {
        if (!CanUse(player) || GamePause.BlocksInput || SpaceJunkLobbyScreen.IsTyping
            || (SpaceJunkLobbyTerminal.Current != null && SpaceJunkLobbyTerminal.Current.IsOpen)) return false;

        NameDataManager database = player.NetworkManager.GetComponent<NameDataManager>();
        if (database == null)
        {
            database = player.NetworkManager.gameObject.AddComponent<NameDataManager>();
        }

        return database.RandomName(out int prefix, out int connector, out int suffix)
            && player.SetName(prefix, connector, suffix);
    }

    private bool CanUse(FishingNetPlayer player)
    {
        return isActiveAndEnabled && player != null && player.IsSpawned && player.IsOwner
            && player.NetworkManager != null && player.NetworkManager.IsListening
            && (player.transform.position - transform.position).sqrMagnitude
                <= Mathf.Max(0f, interactRange) * Mathf.Max(0f, interactRange);
    }

    private void OnDisable()
    {
        IsPlayerNearby = false;
        if (highlightRenderer != null && highlightRenderer.sharedMaterial != null)
        {
            highlightRenderer.material.color = originalColor;
        }
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = nearColor;
        Gizmos.DrawWireSphere(transform.position, Mathf.Max(0f, interactRange));
    }
}
