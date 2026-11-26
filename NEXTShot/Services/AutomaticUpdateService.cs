using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Threading;

namespace NEXTShot.Services;

public sealed class AutomaticUpdateService : IDisposable
{
    private readonly Func<AppSettings> _settings;
    private readonly DispatcherTimer _timer;
    private int _checkInFlight;

    public AutomaticUpdateService(Func<AppSettings> settings)
    {
        _settings = settings;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromHours(6) };
        _timer.Tick += (_, _) => _ = RunDueCheckAsync(manual: false);
    }

    public void Start()
    {
        _timer.Start();
        _ = RunDueCheckAsync(manual: false, delay: TimeSpan.FromSeconds(12));
    }

    public void OnSettingsChanged() => _ = RunDueCheckAsync(manual: false);

    public async Task RunDueCheckAsync(bool manual, TimeSpan? delay = null)
    {
        if (delay is { } d && d > TimeSpan.Zero)
            await Task.Delay(d).ConfigureAwait(false);

        var settings = _settings();
        if (!manual && settings.UpdateCheckKind == UpdateCheckIntervalKind.None)
            return;
        if (!manual && !settings.IsAutomaticUpdateCheckDueUtc(DateTime.UtcNow))
            return;

        if (Interlocked.CompareExchange(ref _checkInFlight, 1, 0) != 0)
            return;

        try
        {
            if (!manual)
            {
                settings.LastUpdateCheckUtc = DateTime.UtcNow;
                settings.Save();
            }

            var current = GetCurrentVersion();
            var result = await UpdateCheckService.CheckAsync(current).ConfigureAwait(false);
            if (result.Status != UpdateCheckStatus.NewerFound
                || string.IsNullOrWhiteSpace(result.DownloadUrl))
                return;

            var path = await UpdateCheckService.DownloadInstallerAsync(result.DownloadUrl).ConfigureAwait(false);
            if (path is null) return;

            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                UpdateCheckService.StartSilentInstallAndExit(path);
            });
        }
        catch
        {
            /* автообновление без всплывающих окон */
        }
        finally
        {
            Interlocked.Exchange(ref _checkInFlight, 0);
        }
    }

    private static Version GetCurrentVersion()
    {
        var v = Assembly.GetExecutingAssembly().GetName().Version;
        return v is null ? new Version(1, 0, 0) : UpdateCheckService.NormalizeForCompare(v);
    }

    public void Dispose() => _timer.Stop();
}