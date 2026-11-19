using Flip.Core;

namespace Flip;
// Single non-modifier key: no Windows copy/move/link modifiers are required.
internal readonly record struct WheelShortcut(string Name, ushort VirtualKey)
{
    internal const string Default = "DOWN";
    internal string Id => VirtualKey == 0xE2 ? "OEM102" : "VK:" + VirtualKey.ToString("X2");
    internal string DisplayName => FormatDisplayName(VirtualKey);
    internal static WheelShortcut Parse(string? value) => (value ?? "").Trim().ToUpperInvariant().Replace(" ", "") switch
    {
        "OEM102" or "OEM_102" or "VK_OEM_102" or "<>" or "<" or ">" => new("<>", 0xE2),
        "DOWN" => FromVirtualKey(0x28),
        "UP" => FromVirtualKey(0x26),
        "LEFT" => FromVirtualKey(0x25),
        "RIGHT" => FromVirtualKey(0x27),
        "SPACE" => FromVirtualKey(0x20),
        var s when s.StartsWith("VK:", StringComparison.Ordinal) && ushort.TryParse(s[3..], System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out ushort key) => FromVirtualKey(key),
        var s when s.Length == 1 && char.IsAsciiLetterOrDigit(s[0]) => FromVirtualKey(s[0]),
        var s when s.StartsWith("F", StringComparison.Ordinal) && int.TryParse(s[1..], out int f) && f is >= 1 and <= 11 => FromVirtualKey((ushort)(0x6F + f)),
        _ => throw new ArgumentException(Loc.T(
            "Scegli un tasto singolo: una freccia, Spazio, una lettera o un tasto funzione. Ctrl, Alt, Shift e Fn non sono utilizzabili da soli.",
            "Choose a single key: an arrow key, Space, a letter, or a function key. Ctrl, Alt, Shift, and Fn cannot be used alone."))
    };
    internal static WheelShortcut FromVirtualKey(ushort key) => new(FormatDisplayName(key), key);
    internal static string FormatDisplayName(ushort key) => key switch
    {
        0x20 => Loc.T("Spazio", "Space"),
        0x25 => Loc.T("Freccia sinistra", "Left Arrow"),
        0x26 => Loc.T("Freccia su", "Up Arrow"),
        0x27 => Loc.T("Freccia destra", "Right Arrow"),
        0x28 => Loc.T("Freccia giù", "Down Arrow"),
        >= 0x30 and <= 0x39 or >= 0x41 and <= 0x5A => ((char)key).ToString(),
        >= 0x70 and <= 0x7A => "F" + (key - 0x6F),
        0xE2 => "<>",
        >= 0xBA and <= 0xC0 or >= 0xDB and <= 0xDF => ((System.Windows.Input.Key)System.Windows.Input.KeyInterop.KeyFromVirtualKey(key)).ToString(),
        _ => throw new ArgumentException(Loc.T(
            "Questo tasto è riservato. Scegli una freccia, Spazio, una lettera o F1–F11, senza modificatori.",
            "This key is reserved. Choose an arrow key, Space, a letter, or F1–F11 without modifiers."))
    };
    internal bool Matches(ushort key, ushort flags, ushort scanCode = 0) =>
        VirtualKey == 0xE2 ? (flags & 6) == 0 && (scanCode != 0 ? scanCode == 0x56 : key == VirtualKey) : key == VirtualKey;
}
