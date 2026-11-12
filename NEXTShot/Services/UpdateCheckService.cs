using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace NEXTShot.Services;

public enum UpdateCheckStatus
{
    UpToDate,
    NewerFound,
    NoReleases,
    Error
}

public sealed record UpdateCheckResult(
    UpdateCheckStatus Status,
    string? LatestVersionText = null,
    string? ReleasePageUrl = null,
    string? DownloadUrl = null,
    string? Message = null);

public static class UpdateCheckService
{
    public const string Owner = "MorfeiSYS";
    public const string Repo = "NEXT-Shot";

    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromSeconds(20)
    };

    static UpdateCheckService()
    {
        Http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "NEXT-Shot");
        Http.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "application/vnd.github+json");
    }

    public static async Task<UpdateCheckResult> CheckAsync(Version current, CancellationToken ct = default)
    {
        var url = $"https://api.github.com/repos/{Owner}/{Repo}/releases/latest";
        HttpResponseMessage response;
        try
        {
            response = await Http.GetAsync(url, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return new UpdateCheckResult(UpdateCheckStatus.Error, Message: ex.Message);
        }

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return new UpdateCheckResult(UpdateCheckStatus.NoReleases);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            return new UpdateCheckResult(UpdateCheckStatus.Error,
                Message: $"GitHub API: {(int)response.StatusCode} {response.ReasonPhrase}. {TrimBody(body)}");
        }

        GitHubRelease? release;
        try
        {
            release = await response.Content.ReadFromJsonAsync<GitHubRelease>(cancellationToken: ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return new UpdateCheckResult(UpdateCheckStatus.Error, Message: ex.Message);
        }

        if (release is null || string.IsNullOrWhiteSpace(release.TagName))
            return new UpdateCheckResult(UpdateCheckStatus.NoReleases);

        if (!TryParseReleaseVersion(release.TagName, out var latest))
            return new UpdateCheckResult(UpdateCheckStatus.Error, Message: $"Неизвестный тег релиза: {release.TagName}");

        var latestText = FormatVersion(latest);
        var pageUrl = release.HtmlUrl;
        var downloadUrl = PickInstallerUrl(release.Assets);

        if (latest > NormalizeForCompare(current))
        {
            return new UpdateCheckResult(UpdateCheckStatus.NewerFound, latestText, pageUrl, downloadUrl);
        }

        return new UpdateCheckResult(UpdateCheckStatus.UpToDate, latestText, pageUrl, downloadUrl);
    }

    private static string? PickInstallerUrl(GitHubAsset[]? assets)
    {
        if (assets is null || assets.Length == 0) return null;
        foreach (var a in assets)
        {
            if (a.Name is null) continue;
            if (a.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                && a.Name.Contains("Setup", StringComparison.OrdinalIgnoreCase))
                return a.BrowserDownloadUrl;
        }

        foreach (var a in assets)
        {
            if (a.Name?.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) == true)
                return a.BrowserDownloadUrl;
        }

        return assets[0].BrowserDownloadUrl;
    }

    public static Version NormalizeForCompare(Version v) => new(v.Major, v.Minor, Math.Max(v.Build, 0));

    public static bool TryParseReleaseVersion(string tag, out Version version)
    {
        version = new Version(0, 0);
        var s = tag.Trim();
        if (s.StartsWith("v", StringComparison.OrdinalIgnoreCase))
            s = s[1..];
        if (Version.TryParse(s, out var parsed))
        {
            version = NormalizeForCompare(parsed);
            return true;
        }

        return false;
    }

    public static string FormatVersion(Version v) => $"{v.Major}.{v.Minor}.{v.Build}";

    private static string TrimBody(string body) =>
        body.Length > 120 ? body[..120] + "…" : body;

    private sealed class GitHubRelease
    {
        [JsonPropertyName("tag_name")]
        public string? TagName { get; set; }

        [JsonPropertyName("html_url")]
        public string? HtmlUrl { get; set; }

        [JsonPropertyName("assets")]
        public GitHubAsset[]? Assets { get; set; }
    }

    private sealed class GitHubAsset
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("browser_download_url")]
        public string? BrowserDownloadUrl { get; set; }
    }
}
