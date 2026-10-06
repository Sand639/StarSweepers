using UnityEngine;

namespace ProjectEL4S.InputControl
{
    /// <summary>
    /// <see cref="DualDeviceInput"/> の動作確認用 HUD（IMGUI のみ。UI のセットアップ不要）。
    /// DualDeviceInput と同じ GameObject に付けて Play するだけ。確認が終わったら外す。
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(DualDeviceInput))]
    public sealed class DualDeviceInputTestHud : MonoBehaviour
    {
        [SerializeField] private bool _showHud = true;

        [Tooltip("左手側 / 右手側 のカーソルを画面に描く")]
        [SerializeField] private bool _drawCursors = true;

        [Tooltip("Down / Up を光らせる時間（秒）")]
        [SerializeField] private float _flashDuration = 0.15f;

        [SerializeField] private Color _leftColor = new Color(0.30f, 0.75f, 1f);
        [SerializeField] private Color _rightColor = new Color(1f, 0.55f, 0.25f);

        private DualDeviceInput _input;
        private Texture2D _pixel;
        private GUIStyle _label;
        private GUIStyle _header;

        // Down / Up はそのフレームしか立たないので、光らせる用に時刻を保持する
        private readonly float[] _triggerDownAt = { -99f, -99f };
        private readonly float[] _triggerUpAt = { -99f, -99f };
        private readonly float[] _bumperDownAt = { -99f, -99f };
        private readonly float[] _bumperUpAt = { -99f, -99f };

        private void Awake()
        {
            _input = GetComponent<DualDeviceInput>();

            _pixel = new Texture2D(1, 1);
            _pixel.SetPixel(0, 0, Color.white);
            _pixel.Apply();
        }

        private void OnDestroy()
        {
            if (_pixel != null) Destroy(_pixel);
        }

        private void Update()
        {
            LatchEdges(0, _input.Left);
            LatchEdges(1, _input.Right);
        }

        private void LatchEdges(int i, DualDeviceInput.SideState s)
        {
            if (s.TriggerDown) _triggerDownAt[i] = Time.unscaledTime;
            if (s.TriggerUp) _triggerUpAt[i] = Time.unscaledTime;
            if (s.BumperDown) _bumperDownAt[i] = Time.unscaledTime;
            if (s.BumperUp) _bumperUpAt[i] = Time.unscaledTime;
        }

        private void OnGUI()
        {
            if (!_showHud) return;
            EnsureStyles();

            if (_drawCursors)
            {
                DrawCursor(_input.Left, _leftColor, "L");
                DrawCursor(_input.Right, _rightColor, "R");
            }

            const float panelW = 330f;
            const float panelH = 300f;

            Fill(new Rect(8f, 8f, panelW, panelH), new Color(0f, 0f, 0f, 0.72f));
            GUILayout.BeginArea(new Rect(16f, 14f, panelW - 16f, panelH - 12f));

            GUILayout.Label($"ActiveScheme : {_input.ActiveScheme}", _header);
            GUILayout.Space(2f);

            DrawSide("LEFT  (マウス0番 / 左スティック)", 0, _input.Left, _leftColor,
                "Trigger = 左クリック / LT", "Bumper = 右クリック / LB");
            GUILayout.Space(6f);
            DrawSide("RIGHT (マウス1番 / 右スティック)", 1, _input.Right, _rightColor,
                "Trigger = 右クリック / RT", "Bumper = 左クリック / RB");

            GUILayout.EndArea();
        }

        private void DrawSide(string title, int index, DualDeviceInput.SideState s, Color color,
            string triggerCaption, string bumperCaption)
        {
            var prev = GUI.color;
            GUI.color = color;
            GUILayout.Label(title, _header);
            GUI.color = prev;

            GUILayout.Label(s.IsActive ? "  IsActive : YES" : "  IsActive : no  (未割り当て/未接続)", _label);
            GUILayout.Label($"  Stick  {s.Stick.x,6:0.00},{s.Stick.y,6:0.00}   Delta {s.Delta.x,6:0.0},{s.Delta.y,6:0.0}", _label);
            GUILayout.Label($"  Cursor {s.CursorPosition.x,6:0},{s.CursorPosition.y,6:0}", _label);

            // トリガー（0..1 のバー）
            GUILayout.Label($"  {triggerCaption}", _label);
            Rect bar = GUILayoutUtility.GetRect(1f, 12f, GUILayout.ExpandWidth(true));
            bar.x += 12f;
            bar.width -= 12f;
            Fill(bar, new Color(1f, 1f, 1f, 0.15f));
            if (s.Trigger > 0f)
            {
                Fill(new Rect(bar.x, bar.y, bar.width * Mathf.Clamp01(s.Trigger), bar.height), color);
            }
            GUI.Label(new Rect(bar.x + 4f, bar.y - 2f, 120f, 16f),
                $"{s.Trigger:0.00} {EdgeTag(_triggerDownAt[index], _triggerUpAt[index])}", _label);

            // バンパー（ON/OFF）
            Rect row = GUILayoutUtility.GetRect(1f, 16f, GUILayout.ExpandWidth(true));
            Rect lamp = new Rect(row.x + 12f, row.y + 2f, 12f, 12f);
            Fill(lamp, s.BumperPressed ? color : new Color(1f, 1f, 1f, 0.15f));
            GUI.Label(new Rect(lamp.xMax + 6f, row.y, row.width, 18f),
                $"{bumperCaption}  {EdgeTag(_bumperDownAt[index], _bumperUpAt[index])}", _label);
        }

        private string EdgeTag(float downAt, float upAt)
        {
            bool down = Time.unscaledTime - downAt <= _flashDuration;
            bool up = Time.unscaledTime - upAt <= _flashDuration;
            if (down) return "[DOWN]";
            if (up) return "[UP]";
            return string.Empty;
        }

        private void DrawCursor(DualDeviceInput.SideState s, Color color, string tag)
        {
            if (!s.IsActive) return;

            // ScreenPosition は左下原点、GUI は左上原点なので Y を反転する
            float x = s.CursorPosition.x;
            float y = Screen.height - s.CursorPosition.y;

            float size = s.TriggerPressed ? 22f : 14f;
            Color c = s.BumperPressed ? Color.white : color;

            Fill(new Rect(x - size, y - 1f, size * 2f, 2f), c);
            Fill(new Rect(x - 1f, y - size, 2f, size * 2f), c);

            var prev = GUI.color;
            GUI.color = c;
            GUI.Label(new Rect(x + 8f, y + 6f, 40f, 18f), tag, _label);
            GUI.color = prev;
        }

        private void Fill(Rect rect, Color color)
        {
            var prev = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, _pixel);
            GUI.color = prev;
        }

        private void EnsureStyles()
        {
            if (_label != null) return;

            _label = new GUIStyle(GUI.skin.label)
            {
                fontSize = 12,
                richText = false,
            };
            _label.normal.textColor = Color.white;

            _header = new GUIStyle(_label)
            {
                fontStyle = FontStyle.Bold,
                fontSize = 13,
            };
        }
    }
}
