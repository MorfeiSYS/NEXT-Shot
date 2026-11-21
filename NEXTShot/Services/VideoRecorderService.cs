using System.Diagnostics;
using System.IO;
using System.Text;
using NEXTShot.Native;

namespace NEXTShot.Services;

public sealed class VideoRecorderService : IDisposable
{
    private Process? _process;
    private string? _finalOutputPath;
    private string? _currentSegmentPath;
    private NativeMethods.Rect? _activeRegion;
    private AppSettings? _sessionSettings;
    private StringBuilder? _stderr;
    private readonly List<string> _completedSegments = new();
    private readonly Stopwatch _activeSegmentWatch = new();
    private TimeSpan _accumulatedDuration;
    private bool _sessionActive;

    public bool IsRecording => _process is { HasExited: false };
    public bool IsSessionActive => _sessionActive;
    public bool IsPaused => _sessionActive && !IsRecording;
    public NativeMethods.Rect? ActiveRegion => _activeRegion;

    public TimeSpan RecordedDuration
    {
        get
        {
            var total = _accumulatedDuration;
            if (IsRecording && _activeSegmentWatch.IsRunning)
                total += _activeSegmentWatch.Elapsed;
            return total;
        }
    }

    public string? Toggle(AppSettings settings, NativeMethods.Rect region)
    {
        if (IsSessionActive)
        {
            var path = _finalOutputPath;
            Stop();
            return path;
        }

        return Start(settings, region);
    }

    public string? Start(AppSettings settings, NativeMethods.Rect region)
    {
        if (IsSessionActive) return _finalOutputPath;

        var ffmpeg = FindFfmpeg();
        if (ffmpeg is null) return null;

        var savePath = string.IsNullOrWhiteSpace(settings.VideoSavePath) ? settings.ScreenshotsPath : settings.VideoSavePath;
        Directory.CreateDirectory(savePath);
        _finalOutputPath = Path.Combine(savePath, $"video_{DateTime.Now:yyyyMMdd_HHmmss}.mp4");
        _sessionSettings = settings;
        _activeRegion = region;
        _completedSegments.Clear();
        _accumulatedDuration = TimeSpan.Zero;
        _sessionActive = true;

        if (!StartNextSegment())
        {
            ResetSession();
            return null;
        }

        return _finalOutputPath;
    }

    public bool Pause()
    {
        if (!IsRecording) return false;
        FinalizeCurrentSegment();
        return IsPaused;
    }

    public bool Resume()
    {
        if (!IsPaused || _sessionSettings is null || _activeRegion is not { } region) return false;
        return StartNextSegment();
    }

    public string? Stop()
    {
        if (!_sessionActive)
            return null;

        var output = _finalOutputPath;
        if (IsRecording)
            FinalizeCurrentSegment();

        var path = FinalizeOutputFile();
        ResetSession();
        return path ?? output;
    }

    private bool StartNextSegment()
    {
        if (_sessionSettings is null || _activeRegion is not { } region) return false;

        var ffmpeg = FindFfmpeg();
        if (ffmpeg is null) return false;

        var segDir = GetSegmentDirectory();
        _currentSegmentPath = Path.Combine(segDir, $"seg_{DateTime.Now:yyyyMMdd_HHmmss_fff}.mp4");

        var w = Math.Max(2, region.Width & ~1);
        var h = Math.Max(2, region.Height & ~1);
        var fps = _sessionSettings.VideoQuality switch
        {
            1 => 60,
            2 => 30,
            _ => 30
        };

        var drawMouse = _sessionSettings.CaptureCursor ? 1 : 0;
        var fullScreen = IsFullVirtualScreen(region);
        var vf = fullScreen ? BuildFullScreenVideoFilter(_sessionSettings) : string.Empty;
        var argLine = BuildGdigrabArgs(fps, region, w, h, drawMouse, vf, _currentSegmentPath);

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
                FailSegmentStart();
                return false;
            }

            _process.BeginErrorReadLine();
            Thread.Sleep(200);
            if (_process.HasExited)
            {
                FailSegmentStart();
                return false;
            }

            _activeSegmentWatch.Restart();
            return true;
        }
        catch
        {
            FailSegmentStart();
            return false;
        }
    }

    private void FailSegmentStart()
    {
        try { _process?.Kill(entireProcessTree: true); } catch { }
        _process?.Dispose();
        _process = null;
        if (_currentSegmentPath is not null && File.Exists(_currentSegmentPath))
        {
            try
            {
                if (new FileInfo(_currentSegmentPath).Length == 0)
                    File.Delete(_currentSegmentPath);
            }
            catch { }
        }

        _currentSegmentPath = null;
    }

    private void FinalizeCurrentSegment()
    {
        _activeSegmentWatch.Stop();
        if (_process is not null && !_process.HasExited)
        {
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
        }

        _process?.Dispose();
        _process = null;

        if (_currentSegmentPath is not null && File.Exists(_currentSegmentPath))
        {
            var fi = new FileInfo(_currentSegmentPath);
            if (fi.Length > 0)
            {
                _completedSegments.Add(_currentSegmentPath);
                var segDur = ProbeDuration(_currentSegmentPath);
                if (segDur > TimeSpan.Zero)
                    _accumulatedDuration += segDur;
                else
                    _accumulatedDuration += _activeSegmentWatch.Elapsed;
            }
            else
            {
                try { File.Delete(_currentSegmentPath); } catch { }
            }
        }

        _currentSegmentPath = null;
        _activeSegmentWatch.Reset();
    }

    private string? FinalizeOutputFile()
    {
        if (_finalOutputPath is null) return null;

        if (_completedSegments.Count == 0)
        {
            if (File.Exists(_finalOutputPath))
            {
                try { File.Delete(_finalOutputPath); } catch { }
            }

            return null;
        }

        try
        {
            if (_completedSegments.Count == 1)
            {
                var single = _completedSegments[0];
                if (File.Exists(_finalOutputPath))
                    File.Delete(_finalOutputPath);
                File.Move(single, _finalOutputPath, overwrite: true);
                return _finalOutputPath;
            }

            var ffmpeg = FindFfmpeg();
            if (ffmpeg is null) return _completedSegments[^1];

            var listPath = Path.Combine(GetSegmentDirectory(), $"concat_{Guid.NewGuid():N}.txt");
            var lines = _completedSegments.Select(s =>
            {
                var escaped = s.Replace("'", "'\\''");
                return $"file '{escaped}'";
            });
            File.WriteAllLines(listPath, lines);

            if (File.Exists(_finalOutputPath))
                File.Delete(_finalOutputPath);

            var argLine = $"-hide_banner -loglevel error -y -f concat -safe 0 -i \"{listPath}\" -c copy \"{_finalOutputPath}\"";
            using var proc = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = ffmpeg,
                    Arguments = argLine,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };
            proc.Start();
            proc.WaitForExit(60000);

            try { File.Delete(listPath); } catch { }
            foreach (var seg in _completedSegments)
            {
                try { if (File.Exists(seg)) File.Delete(seg); } catch { }
            }

            if (File.Exists(_finalOutputPath) && new FileInfo(_finalOutputPath).Length > 0)
                return _finalOutputPath;

            return null;
        }
        catch
        {
            return null;
        }
    }

    private void ResetSession()
    {
        try { _process?.Kill(entireProcessTree: true); } catch { }
        _process?.Dispose();
        _process = null;
        _activeRegion = null;
        _sessionSettings = null;
        _finalOutputPath = null;
        _currentSegmentPath = null;
        _stderr = null;
        _completedSegments.Clear();
        _accumulatedDuration = TimeSpan.Zero;
        _activeSegmentWatch.Reset();
        _sessionActive = false;
    }

    private static TimeSpan ProbeDuration(string path)
    {
        var ffmpeg = FindFfmpeg();
        if (ffmpeg is null) return TimeSpan.Zero;

        try
        {
            using var proc = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = ffmpeg,
                    Arguments = $"-hide_banner -i \"{path}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardError = true
                }
            };
            proc.Start();
            var err = proc.StandardError.ReadToEnd();
            proc.WaitForExit(5000);
            var idx = err.IndexOf("Duration:", StringComparison.Ordinal);
            if (idx < 0) return TimeSpan.Zero;
            var slice = err.AsSpan(idx + 9);
            var end = slice.IndexOf(',');
            if (end < 0) return TimeSpan.Zero;
            var durStr = slice[..end].Trim().ToString();
            return TimeSpan.TryParse(durStr, out var ts) ? ts : TimeSpan.Zero;
        }
        catch
        {
            return TimeSpan.Zero;
        }
    }

    private static string GetSegmentDirectory()
    {
        var dir = Path.Combine(Path.GetTempPath(), "NEXTShot", "segments");
        Directory.CreateDirectory(dir);
        return dir;
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