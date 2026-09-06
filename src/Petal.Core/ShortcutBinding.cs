namespace Petal.Core;

public record ShortcutBinding(string Label, uint Modifiers, uint Key)
{
    static readonly Dictionary<uint, string> Names = new()
    {
        [0x08] = "Backspace", [0x09] = "Tab", [0x0D] = "Enter", [0x20] = "Space",
        [0x21] = "Page Up", [0x22] = "Page Down", [0x23] = "End", [0x24] = "Home",
        [0x25] = "Left", [0x26] = "Up", [0x27] = "Right", [0x28] = "Down",
        [0x2D] = "Insert", [0x2E] = "Delete", [0xAD] = "Volume Mute", [0xAE] = "Volume Down",
        [0xAF] = "Volume Up", [0xB0] = "Media Next", [0xB1] = "Media Previous",
        [0xB2] = "Media Stop", [0xB3] = "Media Play Pause"
    };
    public static ShortcutBinding FromKey(uint key, uint modifiers)
    {
        if (key < 8 || key > 254 || key is 0x10 or 0x11 or 0x12 or 0x1B or 0x5B or 0x5C or 0x7B or >= 0xA0 and <= 0xA5 || modifiers > 15)
            throw new ArgumentException("Choose a key other than Escape, F12, or a modifier by itself.");
        string name = Names.GetValueOrDefault(key) ?? (key >= 0x70 && key <= 0x87 ? "F" + (key - 0x6F) : key >= 0x30 && key <= 0x5A ? ((char)key).ToString() : $"Key 0x{key:X2}");
        var parts = new List<string>();
        if ((modifiers & 2) != 0) parts.Add("Ctrl");
        if ((modifiers & 1) != 0) parts.Add("Alt");
        if ((modifiers & 4) != 0) parts.Add("Shift");
        if ((modifiers & 8) != 0) parts.Add("Win");
        parts.Add(name); return new(string.Join(" + ", parts), modifiers, key);
    }
    public static ShortcutBinding Get(string label)
    {
        var parts = label.Split(" + "); uint modifiers = 0;
        foreach (var part in parts.SkipLast(1)) modifiers |= part switch { "Ctrl" => 2u, "Alt" => 1u, "Shift" => 4u, "Win" => 8u, _ => throw new ArgumentException("Invalid shortcut modifier.") };
        string name = parts[^1]; uint key;
        var named = Names.FirstOrDefault(x => x.Value == name);
        if (named.Key != 0) key = named.Key;
        else if (name.StartsWith('F') && uint.TryParse(name[1..], out var f) && f is >= 1 and <= 24) key = 0x6F + f;
        else if (name.Length == 1 && char.IsAsciiLetterOrDigit(name[0])) key = char.ToUpperInvariant(name[0]);
        else if (name.StartsWith("Key 0x") && uint.TryParse(name[6..], System.Globalization.NumberStyles.HexNumber, null, out var vk)) key = vk;
        else throw new ArgumentException("Choose a supported recording shortcut.");
        return FromKey(key, modifiers);
    }
}
