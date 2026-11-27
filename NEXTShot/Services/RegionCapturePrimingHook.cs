using System.Drawing;
using System.Runtime.InteropServices;
using NEXTShot.Native;

namespace NEXTShot.Services;

/// <summary>
/// LL-хук: priming кадра области на keydown и диспетчер горячих клавиш, если WM_HOTKEY не доходит.
/// </summary>
public sealed class RegionCapturePrimingHook : IDisposable
{
    private sealed class HotkeyBinding
    {
        public required uint Vk;
        public required uint Modifiers;
        public required bool PrimeDesktop;
        public required Action? Handler;
    }

    private readonly object _lock = new();
    private readonly List<HotkeyBinding> _bindings = new();
    private Bitmap? _pending;
    private IntPtr _hook;
    private NativeMethods.LowLevelKeyboardProc? _proc;
    private Func<bool>? _captureCursor;

    public void Configure(
        AppSettings settings,
        Func<bool> captureCursor,
        Action regionCapture,
        Action windowCapture,
        Action fullCapture,
        Action videoToggle)
    {
        _captureCursor = captureCursor;
        _bindings.Clear();
        _bindings.Add(MakeBinding(settings.HotkeyRegionScreenshot, prime: true, regionCapture));
        _bindings.Add(MakeBinding(settings.HotkeyActiveWindowScreenshot, prime: false, windowCapture));
        _bindings.Add(MakeBinding(settings.HotkeyFullScreenshot, prime: false, fullCapture));
        _bindings.Add(MakeBinding(settings.HotkeyVideoRecording, prime: false, videoToggle));
        EnsureInstalled();
    }

    private static HotkeyBinding MakeBinding(string spec, bool prime, Action handler) => new()
    {
        Vk = NativeMethods.ParseHotkeyKey(spec),
        Modifiers = NativeMethods.ParseHotkeyModifiers(spec),
        PrimeDesktop = prime,
        Handler = handler
    };

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
            foreach (var binding in _bindings)
            {
                if (data.VkCode != binding.Vk || !KeyboardModifiersMatch(binding.Modifiers, data.Flags))
                    continue;

                if (binding.PrimeDesktop)
                    PrimeCaptureFrame();
                binding.Handler?.Invoke();
                break;
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

    private static bool KeyboardModifiersMatch(uint required, uint hookFlags)
    {
        static bool Down(int vk) => (NativeMethods.GetAsyncKeyState(vk) & 0x8000) != 0;
        var alt = (hookFlags & NativeMethods.LlkhfAltDown) != 0 || Down(NativeMethods.VkMenu);
        var ctrl = Down(NativeMethods.VkControl);
        var shift = Down(NativeMethods.VkShift);
        var wantAlt = (required & (uint)NativeMethods.HotkeyModifiers.Alt) != 0;
        var wantCtrl = (required & (uint)NativeMethods.HotkeyModifiers.Control) != 0;
        var wantShift = (required & (uint)NativeMethods.HotkeyModifiers.Shift) != 0;
        return alt == wantAlt && ctrl == wantCtrl && shift == wantShift;
    }
}