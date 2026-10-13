using System;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;

namespace ProjectEL4S.InputControl
{
    /// <summary>
    /// InputSeatManager の動作確認 HUD。IMGUI だけで完結しているので UI のセットアップは不要。
    /// 席ごとに色付きの四角を Move で動かし、カーソル（マウス or 右スティック）と押しているボタンを表示する。
    ///
    ///   F5 … 割り当てをやり直す（固定登録はそのまま）
    ///   F6 … 固定登録を始める
    ///   F7 … 登録をやめる
    ///   F8 … 保存した登録を消す（押した順に戻す）
    /// </summary>
    [RequireComponent(typeof(InputSeatManager))]
    public sealed class InputSeatTestHud : MonoBehaviour
    {
        [SerializeField] private float _boxSize = 60f;
        [Tooltip("四角の移動速度（px/秒）")]
        [SerializeField] private float _moveSpeed = 400f;

        private static readonly InputSeatButton[] s_buttons = (InputSeatButton[])Enum.GetValues(typeof(InputSeatButton));

        private InputSeatManager _manager;
        private Texture2D _pixel;
        private Vector2[] _boxPositions;   // GUI 座標（左上原点）の中心
        private int[] _buttonDownCounts;
        private GUIStyle _labelStyle;
        private GUIStyle _promptStyle;
        private readonly StringBuilder _sb = new StringBuilder();

        private void Awake()
        {
            _manager = GetComponent<InputSeatManager>();

            _pixel = new Texture2D(1, 1);
            _pixel.SetPixel(0, 0, Color.white);
            _pixel.Apply();
        }

        private void Start()
        {
            // 席の数は InputSeatManager.Awake で決まるので、ここで確保する
            _boxPositions = new Vector2[_manager.SeatCount];
            _buttonDownCounts = new int[_manager.SeatCount];
            ResetBoxes();
        }

        private void OnDestroy()
        {
            if (_pixel != null) Destroy(_pixel);
        }

        private void Update()
        {
            if (WasPressed(Key.F5))
            {
                _manager.ResetAssignments();
                ResetBoxes();
                Array.Clear(_buttonDownCounts, 0, _buttonDownCounts.Length);
                return;
            }
            if (WasPressed(Key.F6)) _manager.BeginRegistration();
            if (WasPressed(Key.F7)) _manager.CancelRegistration();
            if (WasPressed(Key.F8)) _manager.ForgetRegistration();

            for (int i = 0; i < _manager.SeatCount; i++)
            {
                var seat = _manager.GetSeat(i);
                for (int b = 0; b < s_buttons.Length; b++)
                {
                    if (seat.WasPressedThisFrame(s_buttons[b])) _buttonDownCounts[i]++;
                }

                // GUI 座標は下が正なので Y を反転する
                Vector2 p = _boxPositions[i] + new Vector2(seat.Move.x, -seat.Move.y) * (_moveSpeed * Time.deltaTime);
                p.x = Mathf.Clamp(p.x, _boxSize * 0.5f, Screen.width - _boxSize * 0.5f);
                p.y = Mathf.Clamp(p.y, _boxSize * 0.5f, Screen.height - _boxSize * 0.5f);
                _boxPositions[i] = p;
            }
        }

        /// <summary>Unity の Keyboard.current と、分けて読んだ全部のキーボードのどれで押しても効くようにする。</summary>
        private bool WasPressed(Key key)
        {
            if (Keyboard.current != null && Keyboard.current[key].wasPressedThisFrame) return true;
            var keyboards = _manager.KeyboardManager;
            if (keyboards == null) return false;
            for (int i = 0; i < keyboards.Keyboards.Count; i++)
            {
                if (keyboards.Keyboards[i].WasPressedThisFrame(key)) return true;
            }
            return false;
        }

        private void OnGUI()
        {
            if (_labelStyle == null)
            {
                _labelStyle = new GUIStyle(GUI.skin.label) { fontSize = 16 };
                _promptStyle = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 26,
                    alignment = TextAnchor.MiddleCenter,
                    wordWrap = true,
                };
            }

            DrawStatus();
            DrawBoxes();
            DrawCursors();
            if (_manager.IsRegistering) DrawRegistration();
        }

        private void DrawStatus()
        {
            var keyboards = _manager.KeyboardManager;
            var mice = _manager.MouseManager;

            _sb.Clear();
            _sb.AppendLine("=== Input Seat Test ===");
            _sb.AppendLine($"Raw Input  キーボード: {(keyboards.RawInputAvailable ? "OK" : "使用不可 → " + keyboards.StatusMessage)}"
                           + $"   /   マウス: {(mice.RawInputAvailable ? "OK" : "使用不可 → " + mice.StatusMessage)}");
            var saved = _manager.SavedRegistration;
            _sb.AppendLine(saved != null
                ? $"固定登録: あり（{saved.savedAt}）"
                : "固定登録: なし（押した順に割り当て）");
            _sb.AppendLine($"保存先: {InputSeatStore.FilePath}");
            _sb.AppendLine("F5: 割り当てやり直し   F6: 登録を始める   F7: 登録をやめる   F8: 登録を消す");
            _sb.AppendLine();

            for (int i = 0; i < _manager.SeatCount; i++)
            {
                var seat = _manager.GetSeat(i);
                _sb.AppendLine($"P{i + 1}  [{seat.LastUsedScheme}]  move=({seat.Move.x:F2}, {seat.Move.y:F2})"
                               + $"  aim=({seat.AimStick.x:F2}, {seat.AimStick.y:F2})"
                               + $"  cursor=({seat.Cursor.x:F0}, {seat.Cursor.y:F0})  押した回数={_buttonDownCounts[i]}");
                _sb.Append("     押している: ");
                bool any = false;
                for (int b = 0; b < s_buttons.Length; b++)
                {
                    if (!seat.IsPressed(s_buttons[b])) continue;
                    if (any) _sb.Append(' ');
                    _sb.Append(s_buttons[b]);
                    any = true;
                }
                if (!any) _sb.Append('-');
                _sb.AppendLine();
                _sb.AppendLine($"     キーボード: {DeviceLabel(seat.HasKeyboard, seat.Keyboard?.DeviceName, keyboards.HasFixedDevice(i))}");
                _sb.AppendLine($"     マウス　　: {DeviceLabel(seat.HasMouse, seat.Mouse?.DeviceName, mice.HasFixedDevice(i))}");
                _sb.AppendLine($"     コントローラ: {(seat.HasGamepad ? seat.Gamepad.displayName : "なし（ボタンを押すと入る）")}");
            }

            const float width = 1100f, height = 520f;
            GUI.color = Color.black;
            GUI.Label(new Rect(13f, 11f, width, height), _sb.ToString(), _labelStyle);
            GUI.color = Color.white;
            GUI.Label(new Rect(12f, 10f, width, height), _sb.ToString(), _labelStyle);
        }

        private static string DeviceLabel(bool assigned, string deviceName, bool isFixed)
        {
            if (assigned) return (isFixed ? "[固定] " : "[押した順] ") + Shorten(deviceName);
            return isFixed ? "登録したものが見つからない（USB の挿し場所が変わった？）" : "なし";
        }

        private void DrawBoxes()
        {
            for (int i = 0; i < _manager.SeatCount; i++)
            {
                var seat = _manager.GetSeat(i);
                if (!seat.HasAnyDevice) continue;

                Vector2 c = _boxPositions[i];
                var rect = new Rect(c.x - _boxSize * 0.5f, c.y - _boxSize * 0.5f, _boxSize, _boxSize);
                GUI.color = seat.Color;
                GUI.DrawTexture(rect, _pixel);
                GUI.color = Color.black;
                GUI.Label(new Rect(rect.x + 6f, rect.y + 4f, rect.width, 24f), $"P{i + 1}", _labelStyle);
            }
            GUI.color = Color.white;
        }

        private void DrawCursors()
        {
            for (int i = 0; i < _manager.SeatCount; i++)
            {
                var seat = _manager.GetSeat(i);
                if (!seat.HasMouse && !seat.HasGamepad) continue;

                // Screen 座標（左下原点）→ GUI 座標（左上原点）
                var c = new Vector2(seat.Cursor.x, Screen.height - seat.Cursor.y);
                bool pressing = seat.IsPressed(InputSeatButton.Attack) || seat.IsPressed(InputSeatButton.Aim);
                float size = pressing ? 22f : 16f;
                GUI.color = seat.Color;
                GUI.DrawTexture(new Rect(c.x - size * 0.5f, c.y - 1.5f, size, 3f), _pixel);
                GUI.DrawTexture(new Rect(c.x - 1.5f, c.y - size * 0.5f, 3f, size), _pixel);
                GUI.Label(new Rect(c.x + 8f, c.y + 4f, 60f, 24f), $"P{i + 1}", _labelStyle);
            }
            GUI.color = Color.white;
        }

        private void DrawRegistration()
        {
            float w = Mathf.Min(900f, Screen.width - 40f);
            float h = 200f;
            var rect = new Rect((Screen.width - w) * 0.5f, (Screen.height - h) * 0.5f, w, h);

            GUI.color = new Color(0f, 0f, 0f, 0.8f);
            GUI.DrawTexture(rect, _pixel);
            int seat = _manager.RegistrationSeatIndex;
            GUI.color = seat >= 0 && seat < _manager.SeatCount ? _manager.GetSeat(seat).Color : Color.white;
            GUI.Label(rect, _manager.RegistrationPrompt + "\n\nF7 でやめる", _promptStyle);
            GUI.color = Color.white;
        }

        private void ResetBoxes()
        {
            for (int i = 0; i < _boxPositions.Length; i++)
            {
                float step = Screen.width * 0.15f;
                float x = Screen.width * 0.5f + (i - (_boxPositions.Length - 1) * 0.5f) * step;
                _boxPositions[i] = new Vector2(x, Screen.height * 0.7f);
            }
        }

        private static string Shorten(string deviceName)
        {
            if (string.IsNullOrEmpty(deviceName)) return "(unknown)";
            return deviceName.Length <= 70 ? deviceName : deviceName.Substring(0, 70) + "...";
        }
    }
}
