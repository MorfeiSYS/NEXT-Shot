using System.IO;
using Microsoft.Win32;

namespace NEXTShot.Services;

public static class AutostartService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string CanonicalValueName = "NEXTShot";

    private static readonly string[] LegacyValueNames = ["Skrinshoter_NEXTShot", "Skrinshoter", "NEXTShot"];

    public static bool IsEnabled()
    {
        var exe = NormalizePath(Environment.ProcessPath);
        if (string.IsNullOrEmpty(exe)) return false;

        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, false);
        if (key is null) return false;

        foreach (var name in LegacyValueNames)
        {
            if (key.GetValue(name) is string raw && PathsEqual(raw, exe))
                return true;
        }

        return false;
    }

    public static void Apply(bool enable)
    {
        var exe = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exe)) return;

        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, true);
        if (key is null) return;

        if (enable)
        {
            var value = $"\"{exe}\"";
            var existing = key.GetValue(CanonicalValueName) as string;
            if (existing is not null && PathsEqual(existing, exe))
            {
                CleanupDuplicates(key, exe, keepName: CanonicalValueName);
                return;
            }

            key.SetValue(CanonicalValueName, value);
            CleanupDuplicates(key, exe, keepName: CanonicalValueName);
        }
        else
        {
            foreach (var name in LegacyValueNames)
                key.DeleteValue(name, false);
        }
    }

    public static void CleanupDuplicates()
    {
        var exe = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exe)) return;

        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, true);
        if (key is null) return;
        CleanupDuplicates(key, exe, keepName: CanonicalValueName);
    }

    private static void CleanupDuplicates(RegistryKey key, string exePath, string keepName)
    {
        var exe = NormalizePath(exePath);
        foreach (var name in key.GetValueNames())
        {
            if (string.Equals(name, keepName, StringComparison.OrdinalIgnoreCase)) continue;
            if (key.GetValue(name) is not string raw) continue;
            if (PathsEqual(raw, exe))
                key.DeleteValue(name, false);
        }
    }

    private static bool PathsEqual(string registryValue, string exePath)
    {
        return string.Equals(NormalizePath(Unquote(registryValue)), NormalizePath(exePath), StringComparison.OrdinalIgnoreCase);
    }

    private static string Unquote(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return "";
        s = s.Trim();
        if (s.Length >= 2 && s[0] == '"' && s[^1] == '"')
            return s[1..^1];
        return s;
    }

    private static string NormalizePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return "";
        try { return Path.GetFullPath(path.Trim()); }
        catch { return path.Trim(); }
    }
}
