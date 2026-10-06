using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;

namespace ProjectEL4S.MultiKeyboard
{
    /// <summary>
    /// MultiKeyboardManager の動作確認 HUD。IMGUI だけで完結しているので UI のセットアップは不要。
    /// キーボードごとに色付きの四角を WASD / 矢印キーで動かし、押しているキーを表示する。
    /// </summary>
    [RequireComponent(typeof(MultiKeyboardManager))]
    public sealed class MultiKeyboardTestHud : MonoBehaviour
    {
        [SerializeField] private float _boxSize = 60f;
        [Tooltip("四角の移動速度（px/秒）")]
        [SerializeField] private float _moveSpeed = 400f;
        [SerializeField] private MultiKeyboardMoveKeys _moveKeys = MultiKeyboardMoveKeys.WasdAndArrows;

        private MultiKeyboardManager _manager;
        private Texture2D _pixel;
        private Vector2[] _boxPositions;   // GUI 座標（左上原点）の中心
        private int[] _keyDownCounts;
        private GUIStyle _labelStyle;
        private readonly StringBuilder _sb = new StringBuilder();

        private void Awake()
        {
            _manager = GetComponent<MultiKeyboardManager>();

            _pixel = new Texture2D(1, 1);
            _pixel.SetPixel(0, 0, Color.white);
            _pixel.Apply();
        }

        private void Start()
        {
            // 台数は MultiKeyboardManager.Awake で決まるので、ここで確保する
            int count = _manager.Keyboards.Count;
            _boxPositions = new Vector2[count];
            _keyDownCounts = new int[count];
            ResetBoxes();
        }

        private void OnDestroy()
        {
            if (_pixel != null) Destroy(_pixel);
        }

        private void Update()
        {
            // F5 でリセット。Raw Input 側のどのキーボードで押しても効くようにする
            bool reset = Keyboard.current != null && Keyboard.current.f5Key.wasPressedThisFrame;
            for (int i = 0; i < _manager.Keyboards.Count; i++)
            {
                reset |= _manager.Keyboards[i].WasPressedThisFrame(Key.F5);
            }
            if (reset)
            {
                _manager.ClearAssignments();
                ResetBoxes();
                for (int i = 0; i < _keyDownCounts.Length; i++) _keyDownCounts[i] = 0;
                return;
            }

            for (int i = 0; i < _manager.Keyboards.Count; i++)
            {
                var kb = _manager.Keyboards[i];
                if (!kb.IsAssigned) continue;

                if (kb.AnyKeyDown) _keyDownCounts[i]++;

                Vector2 move = kb.ReadMove(_moveKeys);
                // GUI 座標は下が正なので Y を反転する
                Vector2 p = _boxPositions[i] + new Vector2(move.x, -move.y) * (_moveSpeed * Time.deltaTime);
                p.x = Mathf.Clamp(p.x, _boxSize * 0.5f, Screen.width - _boxSize * 0.5f);
                p.y = Mathf.Clamp(p.y, _boxSize * 0.5f, Screen.height - _boxSize * 0.5f);
                _boxPositions[i] = p;
            }
        }

        private void OnGUI()
        {
            if (_labelStyle == null)
            {
                _labelStyle = new GUIStyle(GUI.skin.label) { fontSize = 16 };
            }

            DrawStatus();
            DrawBoxes();
        }

        private void DrawStatus()
        {
            _sb.Clear();
            _sb.AppendLine("=== Multi Keyboard Test ===");
            _sb.AppendLine(_manager.RawInputAvailable
                ? $"Raw Input: OK  /  受信イベント数: {_manager.RawEventCount}"
                : $"Raw Input: 使用不可 → {_manager.StatusMessage}");
            _sb.AppendLine($"Unity に取られた登録を取り戻した回数: {_manager.RegistrationRestoreCount}"
                           + $"   /   Unity の Keyboard.current（参考）: {(Keyboard.current != null && Keyboard.current.anyKey.isPressed ? "押されている" : "反応なし")}");
            _sb.AppendLine($"割り当て済みキーボード: {_manager.AssignedKeyboardCount} / {_manager.Keyboards.Count}"
                           + "   （キーを押すと順番に割り当て）");
            _sb.AppendLine($"移動: {_moveKeys}   /   F5 キー: 割り当てリセット");
            _sb.AppendLine();

            for (int i = 0; i < _manager.Keyboards.Count; i++)
            {
                var kb = _manager.Keyboards[i];
                if (!kb.IsAssigned)
                {
                    _sb.AppendLine($"[{i}] 未割り当て");
                    continue;
                }
                Vector2 move = kb.ReadMove(_moveKeys);
                _sb.Append($"[{i}] move=({move.x:F2}, {move.y:F2}) keyDowns={_keyDownCounts[i]} pressed=");
                for (int k = 0; k < kb.PressedKeys.Count; k++)
                {
                    if (k > 0) _sb.Append(' ');
                    _sb.Append(kb.PressedKeys[k]);
                }
                _sb.AppendLine();
                _sb.AppendLine($"     {Shorten(kb.DeviceName)}");
            }

            GUI.color = Color.black;
            GUI.Label(new Rect(13f, 11f, 1000f, 260f), _sb.ToString(), _labelStyle);
            GUI.color = Color.white;
            GUI.Label(new Rect(12f, 10f, 1000f, 260f), _sb.ToString(), _labelStyle);
        }

        private void DrawBoxes()
        {
            for (int i = 0; i < _manager.Keyboards.Count; i++)
            {
                var kb = _manager.Keyboards[i];
                if (!kb.IsAssigned) continue;

                Vector2 c = _boxPositions[i];
                var rect = new Rect(c.x - _boxSize * 0.5f, c.y - _boxSize * 0.5f, _boxSize, _boxSize);
                GUI.color = kb.Color;
                GUI.DrawTexture(rect, _pixel);
                GUI.color = Color.black;
                GUI.Label(new Rect(rect.x + 6f, rect.y + 4f, rect.width, 24f), $"K{i}", _labelStyle);
            }
            GUI.color = Color.white;
        }

        private void ResetBoxes()
        {
            for (int i = 0; i < _boxPositions.Length; i++)
            {
                float step = Screen.width * 0.15f;
                float x = Screen.width * 0.5f + (i - (_boxPositions.Length - 1) * 0.5f) * step;
                _boxPositions[i] = new Vector2(x, Screen.height * 0.6f);
            }
        }

        private static string Shorten(string deviceName)
        {
            if (string.IsNullOrEmpty(deviceName)) return "(unknown)";
            return deviceName.Length <= 70 ? deviceName : deviceName.Substring(0, 70) + "...";
        }
    }
}
