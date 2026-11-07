using System;
using System.Windows.Forms;

namespace WallScape
{
    internal static class Hotkey
    {
        public static bool TryParse(string text, out uint modifiers, out uint virtualKey)
        {
            modifiers = Native.MOD_NOREPEAT;
            virtualKey = 0;

            if (string.IsNullOrEmpty(text))
                return false;

            string[] parts = text.Split('+');
            foreach (string raw in parts)
            {
                string part = raw.Trim();
                if (part.Length == 0)
                    continue;

                string lower = part.ToLowerInvariant();
                if (lower == "alt")
                    modifiers |= Native.MOD_ALT;
                else if (lower == "ctrl" || lower == "control")
                    modifiers |= Native.MOD_CONTROL;
                else if (lower == "shift")
                    modifiers |= Native.MOD_SHIFT;
                else if (lower == "win" || lower == "windows")
                    modifiers |= Native.MOD_WIN;
                else
                {
                    if (part.Length == 1)
                    {
                        char c = char.ToUpperInvariant(part[0]);
                        if ((c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9'))
                            virtualKey = c;
                        else
                            return false;
                    }
                    else
                    {
                        Keys key;
                        if (!Enum.TryParse<Keys>(part, true, out key))
                            return false;
                        virtualKey = (uint)key;
                    }
                }
            }

            uint required = Native.MOD_ALT | Native.MOD_CONTROL | Native.MOD_SHIFT | Native.MOD_WIN;
            return virtualKey != 0 && (modifiers & required) != 0;
        }

        public static string ToDisplay(string text)
        {
            if (string.IsNullOrEmpty(text))
                return text;
            return text.Replace("+", " + ");
        }
    }
}
