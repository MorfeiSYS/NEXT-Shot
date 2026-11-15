using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;
using NEXTShot.Native;
using NEXTShot.Windows;

namespace NEXTShot.Services;

public sealed class MouseClickHighlightService : IDisposable
{
    private IntPtr _hook;
    private NativeMethods.LowLevelMouseProc? _proc;
    private MouseClickOverlayWindow? _overlay;
    private NativeMethods.Rect _virtualScreen;
    private NativeMethods.Rect? _recordRegion;
    private Dispatcher? _dispatcher;

    public void Start(AppSettings settings, NativeMethods.Rect recordRegion)
    {
        Stop();
        if (!settings.HighlightMouseClicks) return;

        _virtualScreen = ScreenCaptureService.GetVirtualScreenRect();
        _recordRegion = recordRegion;
        _dispatcher = Application.Current?.Dispatcher;
        _overlay = new MouseClickOverlayWindow();
        _dispatcher?.Invoke(() => _overlay.ShowForVirtualScreen());

        _proc = HookProc;
        var hMod = NativeMethods.GetModuleHandle(null);
        _hook = NativeMethods.SetWindowsHookEx(NativeMethods.WhMouseLl, _proc, hMod, 0);
    }

    public void Stop()
    {
        if (_hook != IntPtr.Zero)
        {
            NativeMethods.UnhookWindowsHookEx(_hook);
            _hook = IntPtr.Zero;
        }

        _proc = null;
        var overlay = _overlay;
        _overlay = null;
        _dispatcher?.BeginInvoke(() =>
        {
            overlay?.Close();
        });
        _dispatcher = null;
        _recordRegion = null;
    }

    public void Dispose() => Stop();

    private IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            var msg = (int)wParam;
            MouseClickButton? btn = msg switch
            {
                NativeMethods.WmLButtonDown => MouseClickButton.Left,
                NativeMethods.WmRButtonDown => MouseClickButton.Right,
                NativeMethods.WmMButtonDown => MouseClickButton.Middle,
                _ => null
            };

            if (btn is { } button)
            {
                var data = Marshal.PtrToStructure<NativeMethods.MsLlHookStruct>(lParam);
                var x = data.Pt.X;
                var y = data.Pt.Y;
                if (IsInsideRecordRegion(x, y))
                    DispatchClick(button, x, y);
            }
        }

        return NativeMethods.CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    private bool IsInsideRecordRegion(int x, int y)
    {
        var r = _recordRegion ?? _virtualScreen;
        return x >= r.Left && x < r.Right && y >= r.Top && y < r.Bottom;
    }

    private void DispatchClick(MouseClickButton button, int screenX, int screenY)
    {
        var overlay = _overlay;
        var dispatcher = _dispatcher;
        if (overlay is null || dispatcher is null) return;

        dispatcher.BeginInvoke(() => overlay.PlayClick(button, screenX, screenY));
    }
}