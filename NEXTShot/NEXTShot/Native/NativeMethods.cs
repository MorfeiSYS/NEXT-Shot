using System.Runtime.InteropServices;

namespace NEXTShot.Native;

public static class NativeMethods
{
    public const int WmHotkey = 0x0312;
    public const int VkSnapshot = 0x2C;

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    [DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    public static extern bool GetWindowRect(IntPtr hWnd, out Rect lpRect);

    [DllImport("user32.dll")]
    public static extern int GetSystemMetrics(int nIndex);

    [DllImport("user32.dll")]
    public static extern bool SetWindowDisplayAffinity(IntPtr hWnd, uint dwAffinity);

    public const uint WdaNone = 0;
    public const uint WdaExcludeFromCapture = 0x00000011;

    public const int SmXVirtualScreen = 76;
    public const int SmYVirtualScreen = 77;
    public const int SmCxVirtualScreen = 78;
    public const int SmCyVirtualScreen = 79;

    [StructLayout(LayoutKind.Sequential)]
    public struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
        public int Width => Right - Left;
        public int Height => Bottom - Top;
    }

    [Flags]
    public enum HotkeyModifiers : uint
    {
        None = 0,
        Alt = 1,
        Control = 2,
        Shift = 4,
        Win = 8
    }

    public static uint ParseHotkeyModifiers(string spec)
    {
        uint m = 0;
        foreach (var part in spec.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            m |= part switch
            {
                "17" => (uint)HotkeyModifiers.Control,
                "16" => (uint)HotkeyModifiers.Shift,
                "18" => (uint)HotkeyModifiers.Alt,
                _ => 0
            };
        }
        return m;
    }

    public static uint ParseHotkeyKey(string spec)
    {
        var parts = spec.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Length == 0 ? VkSnapshot : uint.Parse(parts[^1]);
    }
}