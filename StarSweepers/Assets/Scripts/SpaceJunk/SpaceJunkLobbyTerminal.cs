using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

/// <summary>
/// **ロビーに置く「設定端末」。** 近づいて `E` を押すと、ホストの詳細設定が開く。
///
/// ## ホストだけが使える
///
/// 参加者が近づいても、**押す案内も出ないし開かない。**
/// 設定を変えられるのはホストだけなので、開けても意味が無いため。
///
/// ## なぜ端末にしたのか
///
/// 前は設定がロビーの画面にずっと出っぱなしで、**画面が設定で埋まっていた**。
/// 端末を置いて「開きたいときだけ開く」形にすると、
/// ロビーでは歩き回れて、設定は必要なときだけ前に出る（2026/9/17・大槻さん）。
///
/// ## 開いている間は動けない
///
/// 設定を触っている最中に歩いたりフックを撃ったりしないよう、
/// **開いている間は自分の移動とフックを止める。**
///
/// 画面を描くのは <see cref="SpaceJunkLobbyScreen"/>（プレハブ）。こちらは「開いているか」だけを持つ。
/// </summary>
public class SpaceJunkLobbyTerminal : MonoBehaviour
{
    /// <summary>ロビーに置かれている端末。画面を描くほうから探すために持っている。</summary>
    public static SpaceJunkLobbyTerminal Current { get; private set; }

    [Header("操作")]
    [Tooltip("この距離まで近づくと使える（メートル）")]
    [SerializeField] private float interactRange = 3f;

    [Tooltip("開く／閉じるキー")]
    [SerializeField] private Key interactKey = Key.E;

    [Tooltip("開く／閉じる、コントローラーのボタン（South＝Xbox の A）")]
    [SerializeField] private GamepadButton interactButton = GamepadButton.South;

    [Header("見た目")]
    [Tooltip("近づいたときに色を変える見た目。空でもよい")]
    [SerializeField] private Renderer highlightRenderer;

    [Tooltip("ホストが近づいているときの色")]
    [SerializeField] private Color nearColor = new Color(0.4f, 1f, 0.6f);

    /// <summary>詳細設定が開いているか。</summary>
    public bool IsOpen { get; private set; }

    /// <summary>ホストがこの端末のそばにいるか（案内を出すかどうかの判断に使う）。</summary>
    public bool IsHostNearby { get; private set; }

    /// <summary>押すキーとボタンの名前（案内の文言に使う。例：「E／A」）。</summary>
    public string InteractKeyName => $"{interactKey}／{GamepadInput.Label(interactButton)}";

    private Color originalColor = Color.white;

    private void Awake()
    {
        Current = this;

        // 色を変える前に元の色を控えておく（AIの申し送り参照）
        if (highlightRenderer != null)
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
        NetworkManager manager = NetworkManager.Singleton;
        bool isHost = manager != null && manager.IsListening && manager.IsServer;

        Transform player = FindLocalPlayer();

        IsHostNearby = isHost
                    && player != null
                    && Vector3.Distance(player.position, transform.position) <= interactRange;

        // **ポーズ画面が開いている間（と、閉じたそのフレーム）は入力を読まない。**
        // 読んでしまうと、ポーズを Escape で閉じた瞬間に、同じ Escape で
        // 詳細設定まで閉じてしまう（申し送り 2026/9/8・2026/9/16 と同じ取り合い）。
        // `BlocksInput` は「切り替わったフレーム」も含むので、これだけで防げる
        // 設定の入力欄に打ち込んでいる最中も、E を読まない（打った拍子に設定が閉じないように）
        bool inputBlocked = GamePause.BlocksInput || SpaceJunkLobbyScreen.IsTyping;

        // **開いている間は、コントローラーの A では閉じない。**（2026/10/6 から設定画面はボタンを A で押すため。
        // 閉じるのは B／Esc／「戻る」。<see cref="SpaceJunkLobbyScreen"/> が受け持つ）
        bool pressed = WasPressed(interactKey) || (!IsOpen && GamepadInput.WasPressed(interactButton));

        if (IsHostNearby && !inputBlocked && pressed)
        {
            IsOpen = !IsOpen;
        }

        // 離れた／ホストでなくなったら閉じる
        if (!IsHostNearby && IsOpen)
        {
            IsOpen = false;
        }

        // ⚠ **Escape はここでは見ない。**
        //
        // Escape は**ポーズ画面の持ち物**である（`PauseMenu`）。
        // ここでも見ると、ポーズ画面が先に動いて開いたあと、同じ Escape で
        // 詳細設定まで閉じる、という取り合いになる。
        // このプロジェクトでは同じ失敗を何度もしているので、
        // **「同じキーを2か所で見ない。持ち主を固定する」**という決まりに従う
        // （`AIの申し送り.md` 2026/9/8・2026/9/16）。
        //
        // 閉じ方は「もう一度 E」か、画面の「閉じる」ボタン。

        ApplyHighlight();
        SetLocalPlayerControlEnabled(!IsOpen);
    }

    /// <summary>詳細設定を閉じる。画面の「閉じる」ボタンから呼ばれる。</summary>
    public void Close()
    {
        IsOpen = false;
    }

    /// <summary>
    /// キーが押された瞬間か。
    ///
    /// **このプロジェクトは Input System（新）だけを使う設定**（Active Input Handling）なので、
    /// 古い `Input.GetKeyDown` は動かない。`Keyboard.current` から読むこと。
    /// （2026/9/20・E キーが効かなかった原因がこれだった）
    /// </summary>
    private static bool WasPressed(Key key)
    {
        Keyboard keyboard = Keyboard.current;
        return keyboard != null && keyboard[key].wasPressedThisFrame;
    }

    /// <summary>このPCで操作しているプレイヤーの位置。</summary>
    private static Transform FindLocalPlayer()
    {
        foreach (FishingNetPlayer player in FishingNetPlayer.All)
        {
            if (player != null && player.IsOwner)
            {
                return player.transform;
            }
        }

        return null;
    }

    private void ApplyHighlight()
    {
        if (highlightRenderer == null)
        {
            return;
        }

        highlightRenderer.material.color = IsHostNearby ? nearColor : originalColor;
    }

    /// <summary>設定を開いている間、自分の移動とフックを止める。</summary>
    private void SetLocalPlayerControlEnabled(bool enabledState)
    {
        foreach (FishingNetPlayer player in FishingNetPlayer.All)
        {
            if (player == null || !player.IsOwner)
            {
                continue;
            }

            FishingPlayerController move = player.GetComponent<FishingPlayerController>();
            if (move != null)
            {
                move.enabled = enabledState;
            }

            HookController hook = player.GetComponent<HookController>();
            if (hook != null)
            {
                hook.enabled = enabledState;
            }

            // **オートエイムも止める。** 狙い方の切り替えに数字の 1・2・3 を使っているので、
            // 止めないと、設定に「120」と打ち込んだだけで狙い方が切り替わってしまう
            HookAimAssist aimAssist = player.GetComponent<HookAimAssist>();
            if (aimAssist != null)
            {
                aimAssist.enabled = enabledState;
            }
        }
    }

    private void OnDrawGizmosSelected()
    {
        // 使える距離をシーン画面に出す
        Gizmos.color = new Color(0.4f, 1f, 0.6f, 0.6f);
        Gizmos.DrawWireSphere(transform.position, interactRange);
    }
}
