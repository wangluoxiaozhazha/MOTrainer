namespace MOTrainer.WinForms;

[Flags]
internal enum HotkeyModifiers : uint
{
    None = 0x0000,
    Alt = 0x0001,
    Control = 0x0002,
    Shift = 0x0004,
    Windows = 0x0008,
}

internal readonly record struct HotkeyGesture(
    Keys Key,
    HotkeyModifiers Modifiers)
{
    public string DisplayText
    {
        get
        {
            List<string> parts = [];
            if (Modifiers.HasFlag(HotkeyModifiers.Control))
            {
                parts.Add("Ctrl");
            }

            if (Modifiers.HasFlag(HotkeyModifiers.Alt))
            {
                parts.Add("Alt");
            }

            if (Modifiers.HasFlag(HotkeyModifiers.Shift))
            {
                parts.Add("Shift");
            }

            if (Modifiers.HasFlag(HotkeyModifiers.Windows))
            {
                parts.Add("Win");
            }

            parts.Add(KeyDisplayText(Key));
            return string.Join("+", parts);
        }
    }

    public static HotkeyGesture Ctrl(Keys key) =>
        new(key, HotkeyModifiers.Control);

    public static HotkeyGesture Alt(Keys key) =>
        new(key, HotkeyModifiers.Alt);

    public static bool TryParse(
        string? text,
        out HotkeyGesture gesture)
    {
        gesture = default;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        string[] parts = text!.Split(
            new[] { '+' },
            StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            return false;
        }

        HotkeyModifiers modifiers = HotkeyModifiers.None;
        for (int index = 0; index < parts.Length - 1; index++)
        {
            HotkeyModifiers modifier =
                parts[index].Trim().ToUpperInvariant() switch
                {
                    "CTRL" or "CONTROL" => HotkeyModifiers.Control,
                    "ALT" => HotkeyModifiers.Alt,
                    "SHIFT" => HotkeyModifiers.Shift,
                    "WIN" or "WINDOWS" => HotkeyModifiers.Windows,
                    _ => HotkeyModifiers.None,
                };
            if (modifier == HotkeyModifiers.None ||
                modifiers.HasFlag(modifier))
            {
                return false;
            }

            modifiers |= modifier;
        }

        string keyText = parts[parts.Length - 1].Trim();
        Keys key;
        if (keyText.Length == 1 &&
            keyText[0] is >= '0' and <= '9')
        {
            key = (Keys)((int)Keys.D0 + (keyText[0] - '0'));
        }
        else if (!Enum.TryParse(keyText, true, out key))
        {
            return false;
        }

        key &= Keys.KeyCode;
        if (key is Keys.None or
            Keys.ControlKey or Keys.LControlKey or Keys.RControlKey or
            Keys.Menu or Keys.LMenu or Keys.RMenu or
            Keys.ShiftKey or Keys.LShiftKey or Keys.RShiftKey or
            Keys.LWin or Keys.RWin)
        {
            return false;
        }

        gesture = new HotkeyGesture(key, modifiers);
        return true;
    }

    private static string KeyDisplayText(Keys key) =>
        key switch
        {
            Keys.Space => "Space",
            >= Keys.D0 and <= Keys.D9 =>
                ((int)key - (int)Keys.D0).ToString(),
            _ => key.ToString(),
        };
}
