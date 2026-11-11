using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using NEXTShot.Native;

namespace NEXTShot.Services;

public sealed class VideoRecorderService : IDisposable
{
    private Process? _process;
    private string? _outputPath;
    private NativeMethods.Rect? _activeRegion;
    private StringBuilder? _stderr;
    private Thread? _captureThread;
    private volatile bool _stopCapture;
    private bool _pipedCapture;

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
        _pipedCapture = !fullScreen;

        var captureRegion = new NativeMethods.Rect
        {
            Left = region.Left,
            Top = region.Top,
            Right = region.Left + w,
            Bottom = region.Top + h
        };

        var argLine = fullScreen
            ? BuildGdigrabArgs(fps, region, w, h, drawMouse, vf, _outputPath)
            : BuildPipedArgs(fps, w, h, _outputPath);

        _stderr = new StringBuilder();
        _stopCapture = false;
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

            if (_pipedCapture)
            {
                _captureThread = new Thread(() => PipedCaptureLoop(captureRegion, fps, w, h, settings.CaptureCursor))
                {
                    IsBackground = true,
                    Name = "NEXTShot.VideoPipedCapture"
                };
                _captureThread.Start();
            }

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

    private void PipedCaptureLoop(NativeMethods.Rect region, int fps, int w, int h, bool includeCursor)
    {
        var stream = _process?.StandardInput?.BaseStream;
        if (stream is null) return;

        var frameDelay = 1000.0 / fps;
        var sw = Stopwatch.StartNew();
        var scheduleMs = 0.0;
        var row = new byte[w * 4];

        while (!_stopCapture && _process is { HasExited: false })
        {
            try
            {
                using var bmp = ScreenCaptureService.CaptureRect(region, includeCursor);
                if (bmp.Width != w || bmp.Height != h)
                {
                    using var resized = new Bitmap(w, h, PixelFormat.Format32bppArgb);
                    using (var g = Graphics.FromImage(resized))
                        g.DrawImage(bmp, 0, 0, w, h);
                    WriteBgraFrame(stream, resized, row);
                }
                else
                    WriteBgraFrame(stream, bmp, row);
            }
            catch
            {
                break;
            }

            scheduleMs += frameDelay;
            var sleep = (int)(scheduleMs - sw.Elapsed.TotalMilliseconds);
            if (sleep > 0)
                Thread.Sleep(sleep);
            else if (sleep < -frameDelay * 3)
                scheduleMs = sw.Elapsed.TotalMilliseconds;
        }
    }

    private static void WriteBgraFrame(Stream stream, Bitmap bmp, byte[] row)
    {
        var rect = new Rectangle(0, 0, bmp.Width, bmp.Height);
        var data = bmp.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            var rowBytes = bmp.Width * 4;
            for (var y = 0; y < bmp.Height; y++)
            {
                Marshal.Copy(data.Scan0 + y * data.Stride, row, 0, rowBytes);
                stream.Write(row, 0, rowBytes);
            }

            stream.Flush();
        }
        finally
        {
            bmp.UnlockBits(data);
        }
    }

    private void FailStart()
    {
        _stopCapture = true;
        try { _captureThread?.Join(2000); } catch { }
        _captureThread = null;
        try { _process?.Kill(entireProcessTree: true); } catch { }
        _process?.Dispose();
        _process = null;
        _activeRegion = null;
        _pipedCapture = false;
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
            _pipedCapture = false;
            return path;
        }

        if (_pipedCapture)
        {
            _stopCapture = true;
            try { _captureThread?.Join(8000); } catch { }
            _captureThread = null;
            try { _process.StandardInput.Close(); } catch { try { _process.Kill(entireProcessTree: true); } catch { } }
        }
        else
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
        }

        _process.WaitForExit(15000);
        _process.Dispose();
        _process = null;
        _activeRegion = null;
        _pipedCapture = false;

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

    private static string BuildPipedArgs(int fps, int w, int h, string outputPath)
    {
        var args = new StringBuilder();
        args.Append("-hide_banner -loglevel error -y ");
        args.Append($"-f rawvideo -pixel_format bgra -video_size {w}x{h} -framerate {fps} ");
        args.Append("-i pipe:0 ");
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
