using ProjectEL4S.InputControl;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

/// <summary>
/// **プレイヤー1人ぶんの操作の読み口（ラッパ）。**
///
/// プレイヤーの操作スクリプト（移動・狙い・フック・投げる）は、`Mouse.current` や `Gamepad.current` を
/// 直接読まずに、ここを通して読む。こうしておくと、**同じスクリプトのまま 2 通りの遊び方ができる。**
///
/// | Seat Index | 遊び方 | 読むもの |
/// | --- | --- | --- |
/// | -1（初期値） | **単体操作**（今までどおり） | Input Actions・`Mouse.current`・`Gamepad.current`（どの機器でも動く） |
/// | 0 以上 | **1台のPCで複数人** | `InputSeatManager` のその席（0 = P1）のキーボード・マウス・コントローラ**だけ** |
///
/// 付いていなくても、操作スクリプトが <see cref="Get"/> で自動で付ける（初期値は単体操作なので、
/// **プレハブやシーンを直さなくても今までと同じ動き**になる）。
/// 複数人で遊ぶときは、プレイヤーごとに <see cref="SeatIndex"/> を 0, 1, … にし、シーンに `InputSeatManager` を置く。
/// </summary>
[DisallowMultipleComponent]
public class PlayerInputSource : MonoBehaviour
{
    [Tooltip("-1 = 単体操作（今までどおり、どの機器でも動く）。\n" +
             "0 以上 = その番号の席（0 = P1、1 = P2）の機器だけで動く。シーンに InputSeatManager が要る")]
    [SerializeField] private int seatIndex = -1;

    private bool warnedNoSeat;

    /// <summary>席の番号。-1 なら単体操作。遊んでいる途中で変えてもよい。</summary>
    public int SeatIndex
    {
        get => seatIndex;
        set
        {
            if (seatIndex != value)
            {
                seatIndex = value;
                warnedNoSeat = false;
            }
        }
    }

    /// <summary>席で読むか（1台のPCで複数人）。false なら単体操作。</summary>
    public bool UsesSeat => seatIndex >= 0;

    /// <summary>
    /// 読んでいる席。単体操作のとき、または席がまだ無い（InputSeatManager が無い・番号が大きすぎる）ときは null。
    /// **席で読む設定なのに null のときは、何も入力しない**（全員が同じ機器で動いてしまわないように）。
    /// </summary>
    public InputSeat Seat
    {
        get
        {
            if (!UsesSeat)
            {
                return null;
            }

            InputSeatManager manager = InputSeatManager.Instance;
            InputSeat seat = manager != null ? manager.GetSeat(seatIndex) : null;
            if (seat == null && !warnedNoSeat)
            {
                warnedNoSeat = true;
                Debug.LogWarning(
                    $"{name}: 席 {seatIndex} が見つかりません。シーンに InputSeatManager を置き、Seat Count を足りる数にしてください。" +
                    "それまでこのプレイヤーは動きません。", this);
            }
            return seat;
        }
    }

    /// <summary>
    /// その体の読み口を返す。親にあればそれを使い、無ければ体（CharacterController の付いた物）に付ける。
    /// 付けたものは単体操作で始まる。
    /// </summary>
    public static PlayerInputSource Get(Component owner)
    {
        PlayerInputSource source = owner.GetComponentInParent<PlayerInputSource>();
        if (source != null)
        {
            return source;
        }

        // 同じ体のスクリプトが別々に付けてしまわないよう、付ける場所を体の根元にそろえる
        CharacterController body = owner.GetComponentInParent<CharacterController>();
        GameObject target = body != null ? body.gameObject : owner.gameObject;
        return target.AddComponent<PlayerInputSource>();
    }

    // ---- 移動・狙い ----

    /// <summary>移動（-1..1）。単体操作では <paramref name="singleAction"/>（Input Actions の Move）を読む。</summary>
    public Vector2 ReadMove(InputAction singleAction)
    {
        if (!UsesSeat)
        {
            return singleAction != null ? singleAction.ReadValue<Vector2>() : Vector2.zero;
        }

        InputSeat seat = Seat;
        return seat != null ? seat.Move : Vector2.zero;
    }

    /// <summary>このプレイヤーのコントローラ。単体操作では最後に触ったもの（<c>Gamepad.current</c>）。無ければ null。</summary>
    public Gamepad Gamepad
    {
        get
        {
            if (!UsesSeat)
            {
                return Gamepad.current;
            }

            InputSeat seat = Seat;
            return seat != null && seat.HasGamepad ? seat.Gamepad : null;
        }
    }

    /// <summary>
    /// 狙いに使う画面上の位置（Screen 座標）。単体操作ではマウスの位置。
    /// 席では、その席のマウスか右スティックで動かすカーソル（両方つながっていれば 1 つにまとまる）。
    /// </summary>
    public bool TryReadPointer(out Vector2 screenPoint)
    {
        if (!UsesSeat)
        {
            Mouse mouse = Mouse.current;
            screenPoint = mouse != null ? mouse.position.ReadValue() : Vector2.zero;
            return mouse != null;
        }

        InputSeat seat = Seat;
        if (seat == null || !(seat.HasMouse || seat.HasGamepad))
        {
            screenPoint = Vector2.zero;
            return false;
        }

        screenPoint = seat.Cursor;
        return true;
    }

    // ---- ボタン ----

    /// <summary>Attack が押された瞬間か。単体操作では <paramref name="singleAction"/>（Input Actions の Attack）を読む。</summary>
    public bool AttackPressed(InputAction singleAction)
    {
        if (!UsesSeat)
        {
            return singleAction != null && singleAction.WasPressedThisFrame();
        }

        InputSeat seat = Seat;
        return seat != null && seat.WasPressedThisFrame(InputSeatButton.Attack);
    }

    /// <summary>Attack が離された瞬間か。</summary>
    public bool AttackReleased(InputAction singleAction)
    {
        if (!UsesSeat)
        {
            return singleAction != null && singleAction.WasReleasedThisFrame();
        }

        InputSeat seat = Seat;
        return seat != null && seat.WasReleasedThisFrame(InputSeatButton.Attack);
    }

    /// <summary>「引っ張る」ボタン（左クリック／LT）が押された瞬間か。席では Attack ボタン。</summary>
    public bool PullPressed => UsesSeat
        ? SeatPressed(InputSeatButton.Attack)
        : (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
          || (Gamepad.current != null && Gamepad.current.leftTrigger.wasPressedThisFrame);

    /// <summary>「引っ張る」ボタンが離された瞬間か。</summary>
    public bool PullReleased => UsesSeat
        ? SeatReleased(InputSeatButton.Attack)
        : (Mouse.current != null && Mouse.current.leftButton.wasReleasedThisFrame)
          || (Gamepad.current != null && Gamepad.current.leftTrigger.wasReleasedThisFrame);

    /// <summary>「投げる」ボタン（右クリック／RT）が押された瞬間か。席では Aim ボタン。</summary>
    public bool ThrowPressed => UsesSeat
        ? SeatPressed(InputSeatButton.Aim)
        : (Mouse.current != null && Mouse.current.rightButton.wasPressedThisFrame)
          || (Gamepad.current != null && Gamepad.current.rightTrigger.wasPressedThisFrame);

    /// <summary>「投げる」ボタンが離された瞬間か。</summary>
    public bool ThrowReleased => UsesSeat
        ? SeatReleased(InputSeatButton.Aim)
        : (Mouse.current != null && Mouse.current.rightButton.wasReleasedThisFrame)
          || (Gamepad.current != null && Gamepad.current.rightTrigger.wasReleasedThisFrame);

    /// <summary>
    /// キーかコントローラのボタンが押された瞬間か（「戻る」など、Input Actions を使っていない操作用）。
    /// 単体操作では `Keyboard.current` と `Gamepad.current`、席ではその席のキーボードとコントローラ。
    /// </summary>
    public bool WasPressed(Key key, GamepadButton button)
    {
        if (!UsesSeat)
        {
            Keyboard keyboard = Keyboard.current;
            return (keyboard != null && keyboard[key].wasPressedThisFrame) || GamepadInput.WasPressed(button);
        }

        // コントローラは、席に入った瞬間のボタンを数えない（InputSeat と同じ決まり）
        InputSeat seat = Seat;
        if (seat == null)
        {
            return false;
        }

        return (seat.HasKeyboard && seat.Keyboard.WasPressedThisFrame(key))
            || (seat.HasGamepad && seat.GamepadArmed && seat.Gamepad[button].wasPressedThisFrame);
    }

    private bool SeatPressed(InputSeatButton button)
    {
        InputSeat seat = Seat;
        return seat != null && seat.WasPressedThisFrame(button);
    }

    private bool SeatReleased(InputSeatButton button)
    {
        InputSeat seat = Seat;
        return seat != null && seat.WasReleasedThisFrame(button);
    }
}
