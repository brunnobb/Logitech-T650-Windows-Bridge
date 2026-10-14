using System.Runtime.InteropServices;

namespace T650Bridge.Gesture;

/// <summary>
/// Parses human-readable keyboard shortcut strings (e.g. "Win+Tab", "Ctrl+Win+Right", "Alt+Shift+Tab")
/// and synthesizes key press / release sequences via Windows keybd_event.
/// </summary>
public static class ShortcutExecutor
{
    private const uint KEYEVENTF_KEYDOWN = 0x0000;
    private const uint KEYEVENTF_KEYUP = 0x0002;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

    /// <summary>
    /// Parses a string into an ordered array of Windows Virtual Key (VK) codes.
    /// Returns null if the string is empty or invalid.
    /// </summary>
    public static byte[]? ParseShortcut(string? shortcut)
    {
        if (string.IsNullOrWhiteSpace(shortcut))
            return null;

        var parts = shortcut.Split(new[] { '+', '-', ' ' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
            return null;

        var vks = new List<byte>();
        foreach (var rawPart in parts)
        {
            var part = rawPart.ToUpperInvariant();
            byte vk = part switch
            {
                // Modifiers
                "WIN" or "WINDOWS" or "LWIN" or "RWIN" or "SUPER" or "META" => 0x5B, // VK_LWIN
                "CTRL" or "CONTROL" or "LCTRL" or "RCTRL" => 0x11,                   // VK_CONTROL
                "ALT" or "MENU" or "LALT" or "RALT" => 0x12,                         // VK_MENU
                "SHIFT" or "LSHIFT" or "RSHIFT" => 0x10,                             // VK_SHIFT

                // Navigation & Editing
                "TAB" => 0x09,
                "SPACE" or "SPACEBAR" => 0x20,
                "ENTER" or "RETURN" => 0x0D,
                "ESC" or "ESCAPE" => 0x1B,
                "BACK" or "BACKSPACE" => 0x08,
                "DEL" or "DELETE" => 0x2E,
                "INS" or "INSERT" => 0x2D,
                "HOME" => 0x24,
                "END" => 0x23,
                "PAGEUP" or "PGUP" => 0x21,
                "PAGEDOWN" or "PGDN" => 0x22,

                // Arrow keys
                "LEFT" or "LEFTARROW" => 0x25,
                "UP" or "UPARROW" => 0x26,
                "RIGHT" or "RIGHTARROW" => 0x27,
                "DOWN" or "DOWNARROW" => 0x28,

                // Multimedia & System
                "PRNTSCRN" or "PRINTSCREEN" or "SNAPSHOT" => 0x2C,
                "VOLUMEUP" or "VOLUP" => 0xAF,
                "VOLUMEDOWN" or "VOLDOWN" => 0xAE,
                "MUTE" or "VOLUMEMUTE" => 0xAD,
                "MEDIANEXT" or "NEXTTRACK" => 0xB0,
                "MEDIAPREV" or "PREVTRACK" => 0xB1,
                "PLAYPAUSE" or "MEDIAPLAY" => 0xB3,

                // Function keys
                "F1" => 0x70,
                "F2" => 0x71,
                "F3" => 0x72,
                "F4" => 0x73,
                "F5" => 0x74,
                "F6" => 0x75,
                "F7" => 0x76,
                "F8" => 0x77,
                "F9" => 0x78,
                "F10" => 0x79,
                "F11" => 0x7A,
                "F12" => 0x7B,

                // Letters (A-Z) & Numbers (0-9)
                _ when part.Length == 1 && part[0] >= 'A' && part[0] <= 'Z' => (byte)part[0],
                _ when part.Length == 1 && part[0] >= '0' && part[0] <= '9' => (byte)part[0],

                _ => 0
            };

            if (vk != 0 && !vks.Contains(vk))
            {
                vks.Add(vk);
            }
        }

        return vks.Count > 0 ? vks.ToArray() : null;
    }

    /// <summary>
    /// Executes the keyboard shortcut by pressing all keys in order and releasing them in reverse order.
    /// Returns true if successfully triggered, false if empty or invalid.
    /// </summary>
    public static bool ExecuteShortcut(string? shortcut)
    {
        var vks = ParseShortcut(shortcut);
        if (vks == null || vks.Length == 0)
            return false;

        // Press down keys in order
        for (int i = 0; i < vks.Length; i++)
        {
            keybd_event(vks[i], 0, KEYEVENTF_KEYDOWN, UIntPtr.Zero);
        }

        // Release keys in reverse order
        for (int i = vks.Length - 1; i >= 0; i--)
        {
            keybd_event(vks[i], 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
        }

        return true;
    }
}
