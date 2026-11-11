using System.Collections;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Resources;

static class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        var listOnly = args.Any(a => a == "--list");
        var filtered = args.Where(a => a != "--list").ToArray();
        if (filtered.Length < 1)
        {
            Console.Error.WriteLine("Usage: ExtractUi <Skrinshoter.exe> [output-dir]");
            return 1;
        }

        var exe = filtered[0];
        var outDir = filtered.Length > 1
            ? filtered[1]
            : Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "NEXTShot", "NEXTShot", "Resources", "Toolbar");
        outDir = Path.GetFullPath(outDir);

        if (!File.Exists(exe))
        {
            Console.Error.WriteLine($"Not found: {exe}");
            return 1;
        }

        Directory.CreateDirectory(outDir);
        var asm = Assembly.LoadFrom(exe);
        var saved = 0;

        foreach (var resName in asm.GetManifestResourceNames())
        {
            if (resName.EndsWith(".ico", StringComparison.OrdinalIgnoreCase))
            {
                using var s = asm.GetManifestResourceStream(resName);
                if (s is null) continue;
                using var ms = new MemoryStream();
                s.CopyTo(ms);
                var path = Path.Combine(outDir, Sanitize(Path.GetFileName(resName).Replace(".ico", ".png")));
                using var icon = new Icon(new MemoryStream(ms.ToArray()));
                using var bmp = icon.ToBitmap();
                bmp.Save(path, ImageFormat.Png);
                Console.WriteLine($"ico -> {path}");
                saved++;
                continue;
            }

            if (!resName.Equals("Skrinshoter.g.resources", StringComparison.OrdinalIgnoreCase))
                continue;

            using var stream = asm.GetManifestResourceStream(resName);
            if (stream is null) continue;
            using var reader = new ResourceReader(stream);
            foreach (DictionaryEntry e in reader)
            {
                var key = e.Key?.ToString() ?? "unknown";
                if (listOnly)
                {
                    Console.WriteLine(key + " => " + (e.Value?.GetType().Name ?? "null"));
                    continue;
                }
                try
                {
                    switch (e.Value)
                    {
                        case byte[] bytes when LooksLikePng(bytes):
                            var path = Path.Combine(outDir, $"{Sanitize(key)}.png");
                            File.WriteAllBytes(path, bytes);
                            saved++;
                            break;
                        case UnmanagedMemoryStream ums when key.EndsWith(".baml", StringComparison.OrdinalIgnoreCase)
                            && (key.Contains("image", StringComparison.OrdinalIgnoreCase)
                                || key.Contains("screenshotediting", StringComparison.OrdinalIgnoreCase)):
                            var carved = CarvePngs(ums, Sanitize(key), outDir);
                            saved += carved;
                            Console.WriteLine($"carved {carved} from {key}");
                            break;
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"entry {key}: {ex.Message}");
                }
            }
        }

        if (listOnly)
            return 0;

        saved += ExtractUi.WpfBamlLoad.ExtractFromBamlStreams(asm, outDir);

        CopyMappedToolbarIcons(outDir);
        Console.WriteLine($"Total saved/carved: {saved} -> {outDir}");
        return 0;
    }

    static int CarvePngs(Stream stream, string prefix, string outDir)
    {
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        var bytes = ms.ToArray();
        var count = 0;
        for (var i = 0; i < bytes.Length - 8; i++)
        {
            if (LooksLikePngAt(bytes, i))
            {
                var end = FindPngEnd(bytes, i);
                if (end < 0) continue;
                var len = end - i + 8;
                WriteCarved(outDir, $"{prefix}_{count:D2}.png", bytes, i, len);
                count++;
                i = end + 7;
                continue;
            }

            if (LooksLikeJpegAt(bytes, i))
            {
                var end = FindJpegEnd(bytes, i);
                if (end < 0) continue;
                var len = end - i + 2;
                WriteCarved(outDir, $"{prefix}_{count:D2}.jpg", bytes, i, len);
                count++;
                i = end + 1;
            }
        }

        return count;
    }

    static void WriteCarved(string outDir, string name, byte[] bytes, int offset, int len)
    {
        var path = Path.Combine(outDir, name);
        File.WriteAllBytes(path, bytes.AsSpan(offset, len).ToArray());
    }

    static int FindPngEnd(byte[] bytes, int start)
    {
        const string iend = "IEND";
        for (var j = start + 8; j < bytes.Length - 12; j++)
        {
            if (bytes[j + 4] == iend[0] && bytes[j + 5] == iend[1] && bytes[j + 6] == iend[2] && bytes[j + 7] == iend[3])
                return j + 4;
        }

        return -1;
    }

    static void CopyMappedToolbarIcons(string dir)
    {
        var map = new (string pattern, string dest)[]
        {
            ("DrawingWindowArrowNormalImage", "arrow.png"),
            ("DrawingWindowArrowCheckedImage", "arrow.checked.png"),
            ("DrawingWindowLineNormalImage", "line.png"),
            ("DrawingWindowLineCheckedImage", "line.checked.png"),
            ("DrawingWindowPensilNormalImage", "pencil.png"),
            ("DrawingWindowPensilCheckedImage", "pencil.checked.png"),
            ("DrawingWindowMarkerNormalImage", "marker.png"),
            ("DrawingWindowMarkerCheckedImage", "marker.checked.png"),
            ("DrawingWindowRectangleNormalImage", "rectangle.png"),
            ("DrawingWindowRectangleCheckedImage", "rectangle.checked.png"),
            ("DrawingWindowOvalNormalImage", "oval.png"),
            ("DrawingWindowOvalCheckedImage", "oval.checked.png"),
            ("DrawingWindowTextNormalImage", "text.png"),
            ("DrawingWindowTextCheckedImage", "text.checked.png"),
            ("DrawingWindowNumberNormalImage", "step.png"),
            ("DrawingWindowNumberCheckedImage", "step.checked.png"),
            ("DrawingWindowBlurNormalImage", "blur.png"),
            ("DrawingWindowBlurCheckedImage", "blur.checked.png"),
            ("DrawingWindowUndoNormalImage", "undo.png"),
            ("DrawingWindowLineThicknessImage", "thickness.png"),
            ("ActionWindowUploadToCloudImage", "cloud.png"),
            ("ActionWindowSaveToFileImage", "save.png"),
            ("ActionWindowCopyToClipboardImage", "copy.png"),
            ("ActionWindowPrintImage", "print.png"),
            ("ActionWindowCloseImage", "close.png"),
        };

        if (!Directory.Exists(dir)) return;
        var files = Directory.GetFiles(dir, "*.png");
        foreach (var (pattern, dest) in map)
        {
            var destPath = Path.Combine(dir, dest);
            var match = files.FirstOrDefault(f => MatchPattern(Path.GetFileName(f), pattern));
            if (match is null) continue;
            File.Copy(match, destPath, true);
            Console.WriteLine($"map {Path.GetFileName(match)} -> {dest}");
        }
    }

    static bool MatchPattern(string name, string pattern)
    {
        if (pattern.StartsWith('*'))
            return name.Contains(pattern.Trim('*'), StringComparison.OrdinalIgnoreCase);
        return name.Contains(pattern, StringComparison.OrdinalIgnoreCase);
    }

    static string Sanitize(string name)
    {
        foreach (var c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');
        return name.Replace('/', '_').Replace('\\', '_').Replace('.', '_');
    }

    static int FindJpegEnd(byte[] bytes, int start)
    {
        for (var j = start + 2; j < bytes.Length - 1; j++)
        {
            if (bytes[j] == 0xFF && bytes[j + 1] == 0xD9)
                return j + 1;
        }

        return -1;
    }

    static bool LooksLikePng(byte[] b) => b.Length > 8 && LooksLikePngAt(b, 0);
    static bool LooksLikePngAt(byte[] b, int i) =>
        b.Length > i + 8 && b[i] == 0x89 && b[i + 1] == 0x50 && b[i + 2] == 0x4E && b[i + 3] == 0x47;
    static bool LooksLikeJpegAt(byte[] b, int i) =>
        b.Length > i + 2 && b[i] == 0xFF && b[i + 1] == 0xD8;
}
