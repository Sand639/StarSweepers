using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;

namespace ProjectEL4S.MultiMouse
{
    /// <summary>
    /// MouseTestScene 用の動作確認 HUD。IMGUI だけで完結しているので UI のセットアップは不要。
    /// マウスごとのカーソル描画・ボタン状態表示・同時ドラッグの確認ができる。
    /// </summary>
    [RequireComponent(typeof(MultiMouseManager))]
    public sealed class MultiMouseTestHud : MonoBehaviour
    {
        private sealed class Draggable
        {
            public Rect Rect;        // GUI 座標（左上原点）
            public int Owner = -1;   // 掴んでいるポインタ index
            public int GrabCount;
        }

        [SerializeField] private float _cursorSize = 22f;
        [SerializeField] private int _draggableCount = 4;

        private MultiMouseManager _manager;
        private Texture2D _pixel;
        private Draggable[] _draggables;
        private int[] _clickCounts;
        private GUIStyle _labelStyle;
        private readonly StringBuilder _sb = new StringBuilder();

        private void Awake()
        {
            _manager = GetComponent<MultiMouseManager>();

            _pixel = new Texture2D(1, 1);
            _pixel.SetPixel(0, 0, Color.white);
            _pixel.Apply();

            _draggableCount = Mathf.Max(1, _draggableCount);
            _draggables = new Draggable[_draggableCount];
            for (int i = 0; i < _draggableCount; i++)
            {
                _draggables[i] = new Draggable
                {
                    Rect = new Rect(140f + i * 150f, 260f, 110f, 110f),
                };
            }
            _clickCounts = new int[8];
        }

        private void OnDestroy()
        {
            if (_pixel != null) Destroy(_pixel);
        }

        private void Update()
        {
            if (Keyboard.current != null && Keyboard.current.rKey.wasPressedThisFrame)
            {
                _manager.ClearAssignments();
                for (int i = 0; i < _draggables.Length; i++) _draggables[i].Owner = -1;
                for (int i = 0; i < _clickCounts.Length; i++) _clickCounts[i] = 0;
            }

            for (int p = 0; p < _manager.Pointers.Count; p++)
            {
                var pointer = _manager.Pointers[p];
                if (!pointer.IsAssigned) continue;

                Vector2 gui = ToGui(pointer.ScreenPosition);

                if (pointer.LeftDown)
                {
                    if (p < _clickCounts.Length) _clickCounts[p]++;

                    // 手前（配列の後ろ）から判定。まだ誰も掴んでいない箱だけ掴める
                    for (int i = _draggables.Length - 1; i >= 0; i--)
                    {
                        var d = _draggables[i];
                        if (d.Owner >= 0 || !d.Rect.Contains(gui)) continue;
                        d.Owner = p;
                        d.GrabCount++;
                        break;
                    }
                }

                if (pointer.LeftUp)
                {
                    for (int i = 0; i < _draggables.Length; i++)
                    {
                        if (_draggables[i].Owner == p) _draggables[i].Owner = -1;
                    }
                }

                if (pointer.LeftPressed && pointer.Delta != Vector2.zero)
                {
                    for (int i = 0; i < _draggables.Length; i++)
                    {
                        var d = _draggables[i];
                        if (d.Owner != p) continue;
                        d.Rect.x = Mathf.Clamp(d.Rect.x + pointer.Delta.x, 0f, Screen.width - d.Rect.width);
                        d.Rect.y = Mathf.Clamp(d.Rect.y - pointer.Delta.y, 0f, Screen.height - d.Rect.height);
                    }
                }
            }
        }

        private void OnGUI()
        {
            if (_labelStyle == null)
            {
                _labelStyle = new GUIStyle(GUI.skin.label) { fontSize = 16 };
            }

            DrawStatus();
            DrawDraggables();
            DrawCursors();
        }

        private void DrawStatus()
        {
            _sb.Clear();
            _sb.AppendLine("=== Multi Mouse Test ===");
            _sb.AppendLine(_manager.RawInputAvailable
                ? $"Raw Input: OK  /  受信イベント数: {_manager.RawEventCount}"
                : $"Raw Input: 使用不可 → {_manager.StatusMessage}");
            Vector2 unityDelta = Mouse.current != null ? Mouse.current.delta.ReadValue() : Vector2.zero;
            _sb.AppendLine($"Unity に取られた登録を取り戻した回数: {_manager.RegistrationRestoreCount}"
                           + $"   /   Unity の Mouse.current.delta（参考）: ({unityDelta.x:F0}, {unityDelta.y:F0})");
            _sb.AppendLine($"割り当て済みポインタ: {_manager.AssignedPointerCount} / {_manager.Pointers.Count}"
                           + "   （マウスを動かすと順番に割り当て）");
            _sb.AppendLine("R キー: 割り当てリセット");
            _sb.AppendLine();

            for (int i = 0; i < _manager.Pointers.Count; i++)
            {
                var p = _manager.Pointers[i];
                if (!p.IsAssigned)
                {
                    _sb.AppendLine($"[{i}] 未割り当て");
                    continue;
                }
                string buttons = (p.LeftPressed ? "L" : "-") + (p.RightPressed ? "R" : "-") + (p.IsPressed(MultiMouseButton.Middle) ? "M" : "-");
                _sb.AppendLine($"[{i}] pos=({p.ScreenPosition.x:F0}, {p.ScreenPosition.y:F0}) delta=({p.Delta.x:F0}, {p.Delta.y:F0}) raw=({p.RawDelta.x:F0}, {p.RawDelta.y:F0}) btn={buttons} clicks={(i < _clickCounts.Length ? _clickCounts[i] : 0)}");
                _sb.AppendLine($"     {Shorten(p.DeviceName)}");
            }

            GUI.color = Color.black;
            GUI.Label(new Rect(13f, 11f, 900f, 240f), _sb.ToString(), _labelStyle);
            GUI.color = Color.white;
            GUI.Label(new Rect(12f, 10f, 900f, 240f), _sb.ToString(), _labelStyle);
        }

        private void DrawDraggables()
        {
            for (int i = 0; i < _draggables.Length; i++)
            {
                var d = _draggables[i];
                Color color = d.Owner >= 0 ? _manager.Pointers[d.Owner].Color : new Color(0.35f, 0.35f, 0.4f);

                GUI.color = color;
                GUI.DrawTexture(d.Rect, _pixel);
                GUI.color = Color.white;
                GUI.Label(new Rect(d.Rect.x + 8f, d.Rect.y + 8f, d.Rect.width, 60f),
                    d.Owner >= 0 ? $"掴み中\nP{d.Owner}\n({d.GrabCount})" : $"box {i}\n({d.GrabCount})", _labelStyle);
            }
            GUI.color = Color.white;
        }

        private void DrawCursors()
        {
            for (int i = 0; i < _manager.Pointers.Count; i++)
            {
                var p = _manager.Pointers[i];
                if (!p.IsAssigned) continue;

                Vector2 gui = ToGui(p.ScreenPosition);
                float s = p.LeftPressed ? _cursorSize * 0.7f : _cursorSize;

                GUI.color = p.Color;
                // 十字カーソル
                GUI.DrawTexture(new Rect(gui.x - s, gui.y - 1.5f, s * 2f, 3f), _pixel);
                GUI.DrawTexture(new Rect(gui.x - 1.5f, gui.y - s, 3f, s * 2f), _pixel);
                GUI.DrawTexture(new Rect(gui.x - 4f, gui.y - 4f, 8f, 8f), _pixel);
                GUI.color = Color.white;
                GUI.Label(new Rect(gui.x + 10f, gui.y + 6f, 40f, 24f), i.ToString(), _labelStyle);
            }
            GUI.color = Color.white;
        }

        private static Vector2 ToGui(Vector2 screenPosition)
        {
            return new Vector2(screenPosition.x, Screen.height - screenPosition.y);
        }

        private static string Shorten(string deviceName)
        {
            if (string.IsNullOrEmpty(deviceName)) return "(unknown)";
            return deviceName.Length <= 70 ? deviceName : deviceName.Substring(0, 70) + "...";
        }
    }
}
