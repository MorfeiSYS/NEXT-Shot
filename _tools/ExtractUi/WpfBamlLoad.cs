using System.IO;
using System.Reflection;
using System.Resources;
using System.Windows;
using System.Windows.Baml2006;
using System.Windows.Markup;
using System.Xaml;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ExtractUi;

static class WpfBamlLoad
{
    public static int ExtractFromBamlStreams(Assembly asm, string outDir)
    {
        _ = Application.Current ?? new Application();
        TrySetResourceAssembly(asm);
        var count = 0;
        using var g = asm.GetManifestResourceStream("Skrinshoter.g.resources");
        if (g is null) return 0;
        using var reader = new ResourceReader(g);
        var bamls = new Dictionary<string, UnmanagedMemoryStream>();
        foreach (System.Collections.DictionaryEntry e in reader)
        {
            if (e.Key is not string key || e.Value is not UnmanagedMemoryStream ums) continue;
            if (!key.EndsWith(".baml", StringComparison.OrdinalIgnoreCase)) continue;
            bamls[key] = ums;
        }

        if (bamls.TryGetValue("app.baml", out var appBaml))
            TryMergeDictionary(asm, "app.baml", appBaml);

        var boot = bamls.Keys
            .Where(k => k.StartsWith("wpf/resources/", StringComparison.OrdinalIgnoreCase)
                && !k.Contains("screenshotediting", StringComparison.OrdinalIgnoreCase))
            .OrderBy(k => k, StringComparer.OrdinalIgnoreCase)
            .ToList();
        foreach (var key in boot)
        {
            if (!bamls.TryGetValue(key, out var ums)) continue;
            if (TryMergeDictionary(asm, key, ums) is ResourceDictionary rd)
            {
                Console.WriteLine($"merged {key} ({rd.Count} keys)");
                if (key.EndsWith("images.baml", StringComparison.OrdinalIgnoreCase))
                    count += SaveDictionary(rd, "images", outDir);
            }
        }

        var targets = new[]
        {
            "wpf/resources/screenshoteditingdrawingwindowimages.baml",
            "wpf/resources/screenshoteditingactionwindowimages.baml"
        };
        foreach (var key in targets)
        {
            if (!bamls.TryGetValue(key, out var ums)) continue;
            try
            {
                var root = LoadBamlRoot(asm, key, ums);
                if (root is ResourceDictionary rd)
                    count += SaveDictionary(rd, Sanitize(key), outDir);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"wpf load {key}: {ex.Message}");
            }
        }

        return count;
    }

    static ResourceDictionary? TryMergeDictionary(Assembly asm, string key, Stream ums)
    {
        try
        {
            var root = LoadBamlRoot(asm, key, ums);
            if (root is not ResourceDictionary rd) return null;
            foreach (var k in rd.Keys)
                Application.Current.Resources[k] = rd[k]!;
            return rd;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"merge {key}: {ex.Message}");
            return null;
        }
    }

    static object LoadBamlRoot(Assembly asm, string key, Stream ums)
    {
        ums.Position = 0;
        var settings = new XamlReaderSettings
        {
            LocalAssembly = asm,
            BaseUri = new Uri($"pack://application:,,,/{asm.GetName().Name};component/{key}", UriKind.Absolute)
        };
        using var baml = new Baml2006Reader(ums, settings);
        return System.Windows.Markup.XamlReader.Load(baml);
    }

    static int SaveDictionary(ResourceDictionary rd, string prefix, string outDir)
    {
        var count = 0;
        foreach (var key in rd.Keys)
        {
            var val = rd[key];
            if (val is ResourceDictionary nested)
                count += SaveDictionary(nested, prefix, outDir);
            else if (val is DrawingImage di)
            {
                if (SaveDrawingImage(di, Path.Combine(outDir, $"{prefix}__{Sanitize(key.ToString()!)}.png")))
                {
                    Console.WriteLine($"drawing -> {prefix}__{key}");
                    count++;
                }
            }
            else if (val is ImageSource src)
            {
                if (SaveBitmapSource(src, Path.Combine(outDir, $"{prefix}__{Sanitize(key.ToString()!)}.png")))
                {
                    Console.WriteLine($"image -> {prefix}__{key}");
                    count++;
                }
            }
        }

        return count;
    }

    static bool SaveBitmapSource(ImageSource src, string path)
    {
        if (src is not BitmapSource bs) return false;
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(bs));
        using var fs = File.Create(path);
        enc.Save(fs);
        return true;
    }

    static bool SaveDrawingImage(DrawingImage di, string path)
    {
        var w = (int)Math.Max(24, di.Width);
        var h = (int)Math.Max(24, di.Height);
        var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
            dc.DrawImage(di, new Rect(0, 0, w, h));
        rtb.Render(dv);
        return SaveBitmapSource(rtb, path);
    }

    static void TrySetResourceAssembly(Assembly asm)
    {
        var mi = typeof(Application).GetMethod("SetResourceAssembly",
            BindingFlags.NonPublic | BindingFlags.Static);
        mi?.Invoke(null, [asm]);
    }

    static string Sanitize(string name)
    {
        foreach (var c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');
        return name.Replace('/', '_').Replace('\\', '_');
    }
}
