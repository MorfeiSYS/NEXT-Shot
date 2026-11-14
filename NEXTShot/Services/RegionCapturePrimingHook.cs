using System.Drawing;
using System.Runtime.InteropServices;
using NEXTShot.Native;

namespace NEXTShot.Services;

/// <summary>
/// Захват экрана на WM_KEYDOWN до закрытия всплывающих подсказок и до показа оверлея.
/// </summary>
public sealed class RegionCapturePrimingHook : IDisposable
{
    private readonly object _lock = new();
    private Bitmap? _pending;
    private IntPtr _hook;
    private NativeMethods.LowLevelKeyboardProc? _proc;
    private uint _vk = NativeMethods.VkSnapshot;
    private uint _modifiers;
    private bool _regionHotkeyRegistered = true;
    private Func<bool>? _captureCursor;
    private Action? _fallbackRegionCapture;
    private long _lastFallbackInvokeTick;

    public void Configure(AppSettings settings, bool regionHotkeyRegistered, Func<bool> captureCursor, Action fallbackRegionCapture)
    {
        _vk = NativeMethods.ParseHotkeyKey(settings.HotkeyRegionScreenshot);
        _modifiers = NativeMethods.ParseHotkeyModifiers(settings.HotkeyRegionScreenshot);
        _regionHotkeyRegistered = regionHotkeyRegistered;
        _captureCursor = captureCursor;
        _fallbackRegionCapture = fallbackRegionCapture;
        EnsureInstalled();
    }

    public Bitmap? TakePendingFrame()
    {
        lock (_lock)
        {
            var frame = _pending;
            _pending = null;
            return frame;
        }
    }

    public void Dispose()
    {
        if (_hook != IntPtr.Zero)
        {
            NativeMethods.UnhookWindowsHookEx(_hook);
            _hook = IntPtr.Zero;
        }

        lock (_lock)
        {
            _pending?.Dispose();
            _pending = null;
        }
    }

    private void EnsureInstalled()
    {
        if (_hook != IntPtr.Zero) return;
        _proc = HookProc;
        var hMod = NativeMethods.GetModuleHandle(null);
        _hook = NativeMethods.SetWindowsHookEx(NativeMethods.WhKeyboardLl, _proc, hMod, 0);
    }

    private IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && (wParam == (IntPtr)NativeMethods.WmKeyDown || wParam == (IntPtr)NativeMethods.WmSysKeyDown))
        {
            var data = Marshal.PtrToStructure<NativeMethods.KbdLlHookStruct>(lParam);
            if (data.VkCode == _vk && KeyboardModifiersMatch(_modifiers))
            {
                PrimeCaptureFrame();
                if (!_regionHotkeyRegistered)
                    InvokeFallbackRegionCapture();
            }
        }

        return NativeMethods.CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    private void PrimeCaptureFrame()
    {
        try
        {
            var includeCursor = _captureCursor?.Invoke() ?? true;
            using var bmp = ScreenCaptureService.CaptureVirtualScreen(includeCursor);
            var clone = (Bitmap)bmp.Clone();
            lock (_lock)
            {
                _pending?.Dispose();
                _pending = clone;
            }
        }
        catch
        {
            // WM_HOTKEY / меню — повторный захват в CaptureRegion.
        }
    }

    private void InvokeFallbackRegionCapture()
    {
        var now = Environment.TickCount64;
        if (now - _lastFallbackInvokeTick < 400) return;
        _lastFallbackInvokeTick = now;
        _fallbackRegionCapture?.Invoke();
    }

    private static bool KeyboardModifiersMatch(uint required)
    {
        static bool Down(int vk) => (NativeMethods.GetAsyncKeyState(vk) & 0x8000) != 0;
        var alt = Down(NativeMethods.VkMenu);
        var ctrl = Down(NativeMethods.VkControl);
        var shift = Down(NativeMethods.VkShift);
        var wantAlt = (required & (uint)NativeMethods.HotkeyModifiers.Alt) != 0;
        var wantCtrl = (required & (uint)NativeMethods.HotkeyModifiers.Control) != 0;
        var wantShift = (required & (uint)NativeMethods.HotkeyModifiers.Shift) != 0;
        return alt == wantAlt && ctrl == wantCtrl && shift == wantShift;
    }
}