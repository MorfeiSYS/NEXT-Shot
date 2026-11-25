using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media.Imaging;

namespace NEXTShot.Helpers;

public static class ClipboardHelper
{
    public const uint HrClipbrdCantOpen = 0x800401D0;

    public const string ClipboardBusyMessage =
        "Не удалось скопировать в буфер обмена. Закройте синхронизацию буфера в RustDesk или другой программе удалённого доступа и повторите попытку.";

    private const int MaxAttempts = 10;

    public static bool TrySetImage(BitmapSource image)
    {
        for (var attempt = 0; attempt < MaxAttempts; attempt++)
        {
            try
            {
                Clipboard.SetImage(image);
                return true;
            }
            catch (COMException ex) when ((uint)ex.HResult == HrClipbrdCantOpen)
            {
                DelayBeforeRetry(attempt);
            }
            catch (ExternalException ex) when ((uint)ex.HResult == HrClipbrdCantOpen)
            {
                DelayBeforeRetry(attempt);
            }
        }

        return false;
    }

    public static bool TrySetText(string text)
    {
        for (var attempt = 0; attempt < MaxAttempts; attempt++)
        {
            try
            {
                Clipboard.SetText(text);
                return true;
            }
            catch (COMException ex) when ((uint)ex.HResult == HrClipbrdCantOpen)
            {
                DelayBeforeRetry(attempt);
            }
            catch (ExternalException ex) when ((uint)ex.HResult == HrClipbrdCantOpen)
            {
                DelayBeforeRetry(attempt);
            }
        }

        return false;
    }

    private static void DelayBeforeRetry(int attempt)
    {
        try { Clipboard.Clear(); } catch { }
        Thread.Sleep(40 + attempt * 25);
    }
}