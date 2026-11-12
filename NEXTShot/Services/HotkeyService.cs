using System.Runtime.InteropServices;
using System.Windows.Interop;
using NEXTShot.Native;

namespace NEXTShot.Services;

public sealed class HotkeyService : IDisposable
{
    public event Action? RegionCaptureRequested;
    public event Action? WindowCaptureRequested;
    public event Action? FullScreenCaptureRequested;
    public event Action? VideoToggleRequested;

    private readonly HwndSource _source;
    private readonly Dictionary<int, Action> _handlers = new();
    private int _nextId = 1;

    public bool RegionHotkeyRegistered { get; private set; }

    public HotkeyService()
    {
        var parameters = new HwndSourceParameters("NEXTShotHotkeys")
        {
            Width = 0,
            Height = 0,
            PositionX = 0,
            PositionY = 0,
            WindowStyle = 0
        };
        _source = new HwndSource(parameters);
        _source.AddHook(WndProc);
    }

    public void RegisterFromSettings(AppSettings settings)
    {
        UnregisterAll();
        RegionHotkeyRegistered = RegisterHotkey(NativeMethods.ParseHotkeyModifiers(settings.HotkeyRegionScreenshot),
            NativeMethods.ParseHotkeyKey(settings.HotkeyRegionScreenshot), () => RegionCaptureRequested?.Invoke());
        RegisterHotkey(NativeMethods.ParseHotkeyModifiers(settings.HotkeyActiveWindowScreenshot),
            NativeMethods.ParseHotkeyKey(settings.HotkeyActiveWindowScreenshot), () => WindowCaptureRequested?.Invoke());
        RegisterHotkey(NativeMethods.ParseHotkeyModifiers(settings.HotkeyFullScreenshot),
            NativeMethods.ParseHotkeyKey(settings.HotkeyFullScreenshot), () => FullScreenCaptureRequested?.Invoke());
        RegisterHotkey(NativeMethods.ParseHotkeyModifiers(settings.HotkeyVideoRecording),
            NativeMethods.ParseHotkeyKey(settings.HotkeyVideoRecording), () => VideoToggleRequested?.Invoke());
    }

    private bool RegisterHotkey(uint modifiers, uint vk, Action handler)
    {
        var id = _nextId++;
        if (!NativeMethods.RegisterHotKey(_source.Handle, id, modifiers, vk))
            return false;

        _handlers[id] = handler;
        return true;
    }

    private void UnregisterAll()
    {
        foreach (var id in _handlers.Keys)
            NativeMethods.UnregisterHotKey(_source.Handle, id);
        _handlers.Clear();
        _nextId = 1;
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == NativeMethods.WmHotkey && _handlers.TryGetValue(wParam.ToInt32(), out var action))
        {
            action();
            handled = true;
        }

        return IntPtr.Zero;
    }

    public void Dispose()
    {
        UnregisterAll();
        _source.Dispose();
    }
}
