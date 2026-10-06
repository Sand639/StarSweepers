using UnityEngine.InputSystem;

namespace ProjectEL4S.MultiKeyboard
{
    /// <summary>
    /// Raw Input のスキャンコード → Input System の <see cref="Key"/> 変換表。
    /// Key は「US 配列でのキーの物理的な位置」で決まっているので、スキャンコード（物理位置）から引けば
    /// 日本語配列でも W/A/S/D などの位置はずれない。
    /// 変換できないキー（変換・無変換・かな・￥・ろ など Key に無いもの）は Key.None を返す。
    /// </summary>
    public static class RawKeyMap
    {
        private const ushort VK_PAUSE = 0x13;

        // E0 なしのスキャンコード（0x00..0x7F）
        private static readonly Key[] s_normal = new Key[0x80];
        // E0 付きのスキャンコード（0x00..0x7F）
        private static readonly Key[] s_extended = new Key[0x80];

        static RawKeyMap()
        {
            Set(0x01, Key.Escape);
            Set(0x02, Key.Digit1); Set(0x03, Key.Digit2); Set(0x04, Key.Digit3); Set(0x05, Key.Digit4);
            Set(0x06, Key.Digit5); Set(0x07, Key.Digit6); Set(0x08, Key.Digit7); Set(0x09, Key.Digit8);
            Set(0x0A, Key.Digit9); Set(0x0B, Key.Digit0);
            Set(0x0C, Key.Minus); Set(0x0D, Key.Equals); Set(0x0E, Key.Backspace); Set(0x0F, Key.Tab);
            Set(0x10, Key.Q); Set(0x11, Key.W); Set(0x12, Key.E); Set(0x13, Key.R); Set(0x14, Key.T);
            Set(0x15, Key.Y); Set(0x16, Key.U); Set(0x17, Key.I); Set(0x18, Key.O); Set(0x19, Key.P);
            Set(0x1A, Key.LeftBracket); Set(0x1B, Key.RightBracket);
            Set(0x1C, Key.Enter, Key.NumpadEnter);
            Set(0x1D, Key.LeftCtrl, Key.RightCtrl);
            Set(0x1E, Key.A); Set(0x1F, Key.S); Set(0x20, Key.D); Set(0x21, Key.F); Set(0x22, Key.G);
            Set(0x23, Key.H); Set(0x24, Key.J); Set(0x25, Key.K); Set(0x26, Key.L);
            Set(0x27, Key.Semicolon); Set(0x28, Key.Quote); Set(0x29, Key.Backquote);
            Set(0x2A, Key.LeftShift); Set(0x2B, Key.Backslash);
            Set(0x2C, Key.Z); Set(0x2D, Key.X); Set(0x2E, Key.C); Set(0x2F, Key.V); Set(0x30, Key.B);
            Set(0x31, Key.N); Set(0x32, Key.M);
            Set(0x33, Key.Comma); Set(0x34, Key.Period);
            Set(0x35, Key.Slash, Key.NumpadDivide);
            Set(0x36, Key.RightShift);
            Set(0x37, Key.NumpadMultiply, Key.PrintScreen);
            Set(0x38, Key.LeftAlt, Key.RightAlt);
            Set(0x39, Key.Space); Set(0x3A, Key.CapsLock);
            Set(0x3B, Key.F1); Set(0x3C, Key.F2); Set(0x3D, Key.F3); Set(0x3E, Key.F4); Set(0x3F, Key.F5);
            Set(0x40, Key.F6); Set(0x41, Key.F7); Set(0x42, Key.F8); Set(0x43, Key.F9); Set(0x44, Key.F10);
            Set(0x45, Key.NumLock); Set(0x46, Key.ScrollLock);
            // テンキーと、同じスキャンコードを E0 付きで使うナビゲーションキー
            Set(0x47, Key.Numpad7, Key.Home);
            Set(0x48, Key.Numpad8, Key.UpArrow);
            Set(0x49, Key.Numpad9, Key.PageUp);
            Set(0x4A, Key.NumpadMinus);
            Set(0x4B, Key.Numpad4, Key.LeftArrow);
            Set(0x4C, Key.Numpad5);
            Set(0x4D, Key.Numpad6, Key.RightArrow);
            Set(0x4E, Key.NumpadPlus);
            Set(0x4F, Key.Numpad1, Key.End);
            Set(0x50, Key.Numpad2, Key.DownArrow);
            Set(0x51, Key.Numpad3, Key.PageDown);
            Set(0x52, Key.Numpad0, Key.Insert);
            Set(0x53, Key.NumpadPeriod, Key.Delete);
            Set(0x57, Key.F11); Set(0x58, Key.F12);
            s_extended[0x5B] = Key.LeftMeta;
            s_extended[0x5C] = Key.RightMeta;
            s_extended[0x5D] = Key.ContextMenu;
        }

        private static void Set(int scanCode, Key normal, Key extended = Key.None)
        {
            s_normal[scanCode] = normal;
            s_extended[scanCode] = extended;
        }

        public static Key ToKey(in RawKeyEvent e)
        {
            // Pause は E1 付きの特殊な並びで届くので、仮想キーで判定する
            if (e.VirtualKey == VK_PAUSE) return Key.Pause;
            if (e.MakeCode >= 0x80) return Key.None;
            return e.Extended ? s_extended[e.MakeCode] : s_normal[e.MakeCode];
        }
    }
}
