using System.Diagnostics;
using System.IO;
using System.Text;
using NEXTShot.Native;

namespace NEXTShot.Services;

public sealed class VideoRecorderService : IDisposable
{
    private Process? _process;
    private string? _outputPath;
    private NativeMethods.Rect? _activeRegion;
    private StringBuilder? _stderr;

    public bool IsRecording => _process is { HasExited: false };
    public NativeMethods.Rect? ActiveRegion => _activeRegion;

    public string? Toggle(AppSettings settings, NativeMethods.Rect region)
    {
        if (IsRecording)
        {
            var path = _outputPath;
            Stop();
            return path;
        }

        return Start(settings, region);
    }

    public string? Start(AppSettings settings, NativeMethods.Rect region)
    {
        if (IsRecording) return _outputPath;

        var ffmpeg = FindFfmpeg();
        if (ffmpeg is null) return null;

        var savePath = string.IsNullOrWhiteSpace(settings.VideoSavePath) ? settings.ScreenshotsPath : settings.VideoSavePath;
        Directory.CreateDirectory(savePath);
        _outputPath = Path.Combine(savePath, $"video_{DateTime.Now:yyyyMMdd_HHmmss}.mp4");
        _activeRegion = region;

        var w = Math.Max(2, region.Width & ~1);
        var h = Math.Max(2, region.Height & ~1);
        var fps = settings.VideoQuality switch
        {
            1 => 60,
            2 => 30,
            _ => 30
        };

        var drawMouse = settings.CaptureCursor ? 1 : 0;
        var fullScreen = IsFullVirtualScreen(region);
        var vf = fullScreen ? BuildFullScreenVideoFilter(settings) : string.Empty;
        var argLine = BuildGdigrabArgs(fps, region, w, h, drawMouse, vf, _outputPath);

        _stderr = new StringBuilder();
        try
        {
            _process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = ffmpeg,
                    Arguments = argLine,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardInput = true,
                    RedirectStandardError = true
                },
                EnableRaisingEvents = true
            };
            _process.ErrorDataReceived += (_, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                    _stderr.AppendLine(e.Data);
            };
            if (!_process.Start())
            {
                FailStart();
                return null;
            }

            _process.BeginErrorReadLine();

            Thread.Sleep(200);
            if (_process.HasExited)
            {
                FailStart();
                return null;
            }

            return _outputPath;
        }
        catch
        {
            FailStart();
            return null;
        }
    }

    private void FailStart()
    {
        try { _process?.Kill(entireProcessTree: true); } catch { }
        _process?.Dispose();
        _process = null;
        _activeRegion = null;
        if (_outputPath is not null && File.Exists(_outputPath))
        {
            try
            {
                if (new FileInfo(_outputPath).Length == 0)
                    File.Delete(_outputPath);
            }
            catch { }
        }

        _outputPath = null;
    }

    public string? Stop()
    {
        var path = _outputPath;
        if (_process is null || _process.HasExited)
        {
            _process = null;
            _activeRegion = null;
            return path;
        }

        try
        {
            _process.StandardInput.Write('q');
            _process.StandardInput.WriteLine();
            _process.StandardInput.Flush();
            _process.StandardInput.Close();
        }
        catch
        {
            try { _process.Kill(entireProcessTree: true); } catch { }
        }

        _process.WaitForExit(15000);
        _process.Dispose();
        _process = null;
        _activeRegion = null;

        if (path is not null && File.Exists(path) && new FileInfo(path).Length == 0)
        {
            try { File.Delete(path); } catch { }
            return null;
        }

        return path;
    }

    private static string BuildGdigrabArgs(int fps, NativeMethods.Rect region, int w, int h, int drawMouse, string vf, string outputPath)
    {
        var args = new StringBuilder();
        args.Append("-hide_banner -loglevel error -y ");
        args.Append($"-f gdigrab -framerate {fps} ");
        args.Append($"-offset_x {region.Left} -offset_y {region.Top} -video_size {w}x{h} ");
        args.Append($"-draw_mouse {drawMouse} -i desktop ");
        if (!string.IsNullOrEmpty(vf))
            args.Append(vf).Append(' ');
        args.Append("-c:v libx264 -preset ultrafast -pix_fmt yuv420p ");
        args.Append('"').Append(outputPath).Append('"');
        return args.ToString();
    }

    private static bool IsFullVirtualScreen(NativeMethods.Rect region)
    {
        var vs = ScreenCaptureService.GetVirtualScreenRect();
        return region.Left <= vs.Left + 2
               && region.Top <= vs.Top + 2
               && Math.Abs(region.Width - vs.Width) <= 4
               && Math.Abs(region.Height - vs.Height) <= 4;
    }

    private static string BuildFullScreenVideoFilter(AppSettings settings) => settings.VideoQuality switch
    {
        1 => "-vf scale=1920:1080",
        2 => "-vf scale=3840:2160",
        _ => "-vf scale=1920:1080"
    };

    private static string? FindFfmpeg()
    {
        var paths = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "ffmpeg.exe"),
            "ffmpeg"
        };

        foreach (var p in paths)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = p,
                    Arguments = "-version",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                using var proc = Process.Start(psi);
                proc?.WaitForExit(3000);
                if (proc is { ExitCode: 0 })
                    return p;
            }
            catch { }
        }

        return null;
    }

    public void Dispose() => Stop();
}
