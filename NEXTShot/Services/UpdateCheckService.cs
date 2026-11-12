using System.Net.Http;
using System.Net.Http.Headers;
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
    public const string IssuesUrl = "https://github.com/MorfeiSYS/NEXT-Shot/issues";

    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromSeconds(20)
    };

    static UpdateCheckService()
    {
        Http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "NEXT-Shot");
        Http.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "application/vnd.github+json");
        Http.DefaultRequestHeaders.TryAddWithoutValidation("X-GitHub-Api-Version", "2022-11-28");
    }

    public static async Task<UpdateCheckResult> CheckAsync(Version current, CancellationToken ct = default)
    {
        var (release, status, err) = await ResolveLatestReleaseAsync(ct).ConfigureAwait(false);
        if (status == HttpFetchStatus.NotFound && err is null)
        {
            return new UpdateCheckResult(UpdateCheckStatus.NoReleases,
                Message: DescribeRepoAccessHint());
        }

        if (err is not null)
            return new UpdateCheckResult(UpdateCheckStatus.Error, Message: err);

        if (release is null || string.IsNullOrWhiteSpace(release.TagName))
            return new UpdateCheckResult(UpdateCheckStatus.NoReleases, Message: DescribeRepoAccessHint());

        if (!TryParseReleaseVersion(release.TagName, out var latest))
            return new UpdateCheckResult(UpdateCheckStatus.Error, Message: $"Неизвестный тег релиза: {release.TagName}");

        var latestText = FormatVersion(latest);
        var pageUrl = release.HtmlUrl ?? ReleasePageForTag(release.TagName);
        var downloadUrl = PickInstallerUrl(release.Assets);

        if (latest > NormalizeForCompare(current))
            return new UpdateCheckResult(UpdateCheckStatus.NewerFound, latestText, pageUrl, downloadUrl);

        return new UpdateCheckResult(UpdateCheckStatus.UpToDate, latestText, pageUrl, downloadUrl);
    }

    private static async Task<(GitHubRelease? Release, HttpFetchStatus Status, string? Error)> ResolveLatestReleaseAsync(
        CancellationToken ct)
    {
        var latest = await FetchReleaseJsonAsync($"repos/{Owner}/{Repo}/releases/latest", ct).ConfigureAwait(false);
        if (latest.Release?.TagName is not null)
            return (latest.Release, latest.Status, null);

        if (latest.Status == HttpFetchStatus.Error)
            return (null, latest.Status, latest.Error);

        var list = await FetchReleasesListAsync(ct).ConfigureAwait(false);
        if (list.Error is not null)
            return (null, list.Status, list.Error);

        var pick = PickNewestRelease(list.Releases);
        if (pick is not null)
            return (pick, HttpFetchStatus.Ok, null);

        if (list.Status == HttpFetchStatus.NotFound)
            return (null, HttpFetchStatus.NotFound, null);

        var tag = await FetchNewestTagAsync(ct).ConfigureAwait(false);
        if (tag.Error is not null)
            return (null, tag.Status, tag.Error);

        if (tag.TagName is null)
            return (null, list.Status == HttpFetchStatus.NotFound ? HttpFetchStatus.NotFound : HttpFetchStatus.Ok, null);

        return (new GitHubRelease
        {
            TagName = tag.TagName,
            HtmlUrl = ReleasePageForTag(tag.TagName),
            Assets = null
        }, HttpFetchStatus.Ok, null);
    }

    private static async Task<(GitHubRelease? Release, HttpFetchStatus Status, string? Error)> FetchReleaseJsonAsync(
        string path, CancellationToken ct)
    {
        using var response = await SendAsync(path, ct).ConfigureAwait(false);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return (null, HttpFetchStatus.NotFound, null);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            return (null, HttpFetchStatus.Error, FormatApiError(response, body));
        }

        try
        {
            var release = await response.Content.ReadFromJsonAsync<GitHubRelease>(cancellationToken: ct).ConfigureAwait(false);
            return (release, HttpFetchStatus.Ok, null);
        }
        catch (Exception ex)
        {
            return (null, HttpFetchStatus.Error, ex.Message);
        }
    }

    private static async Task<(GitHubRelease[]? Releases, HttpFetchStatus Status, string? Error)> FetchReleasesListAsync(
        CancellationToken ct)
    {
        using var response = await SendAsync($"repos/{Owner}/{Repo}/releases?per_page=30", ct).ConfigureAwait(false);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return (null, HttpFetchStatus.NotFound, null);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            return (null, HttpFetchStatus.Error, FormatApiError(response, body));
        }

        try
        {
            var releases = await response.Content.ReadFromJsonAsync<GitHubRelease[]>(cancellationToken: ct).ConfigureAwait(false);
            return (releases ?? [], HttpFetchStatus.Ok, null);
        }
        catch (Exception ex)
        {
            return (null, HttpFetchStatus.Error, ex.Message);
        }
    }

    private static async Task<(string? TagName, HttpFetchStatus Status, string? Error)> FetchNewestTagAsync(CancellationToken ct)
    {
        using var response = await SendAsync($"repos/{Owner}/{Repo}/tags?per_page=30", ct).ConfigureAwait(false);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return (null, HttpFetchStatus.NotFound, null);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            return (null, HttpFetchStatus.Error, FormatApiError(response, body));
        }

        try
        {
            var tags = await response.Content.ReadFromJsonAsync<GitHubTag[]>(cancellationToken: ct).ConfigureAwait(false);
            if (tags is null || tags.Length == 0)
                return (null, HttpFetchStatus.Ok, null);

            string? bestName = null;
            Version bestVer = new(0, 0);
            foreach (var t in tags)
            {
                if (t.Name is null || !TryParseReleaseVersion(t.Name, out var v))
                    continue;
                if (bestName is null || v > bestVer)
                {
                    bestName = t.Name;
                    bestVer = v;
                }
            }

            return (bestName, HttpFetchStatus.Ok, null);
        }
        catch (Exception ex)
        {
            return (null, HttpFetchStatus.Error, ex.Message);
        }
    }

    private static GitHubRelease? PickNewestRelease(GitHubRelease[]? releases)
    {
        if (releases is null || releases.Length == 0) return null;

        GitHubRelease? best = null;
        Version bestVer = new(0, 0);
        foreach (var r in releases)
        {
            if (r.Draft) continue;
            if (r.TagName is null || !TryParseReleaseVersion(r.TagName, out var v)) continue;
            if (best is null || v > bestVer)
            {
                best = r;
                bestVer = v;
            }
        }

        return best;
    }

    private static Task<HttpResponseMessage> SendAsync(string path, CancellationToken ct)
    {
        var req = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/{path}");
        var token = Environment.GetEnvironmentVariable("NEXTSHOT_GITHUB_TOKEN");
        if (!string.IsNullOrWhiteSpace(token))
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Trim());
        return Http.SendAsync(req, ct);
    }

    private static string ReleasePageForTag(string tag) =>
        $"https://github.com/{Owner}/{Repo}/releases/tag/{Uri.EscapeDataString(tag)}";

    private static string DescribeRepoAccessHint() =>
        "GitHub вернул 404: нет опубликованных релизов или репозиторий приватный. " +
        "Сделайте репозиторий публичным либо задайте переменную окружения NEXTSHOT_GITHUB_TOKEN " +
        "(classic PAT с правом public_repo или доступом read к этому репозиторию).";

    private static string FormatApiError(HttpResponseMessage response, string body)
    {
        if (body.Contains("\"Not Found\"", StringComparison.Ordinal) && response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return DescribeRepoAccessHint();
        return $"GitHub API: {(int)response.StatusCode} {response.ReasonPhrase}. {TrimBody(body)}";
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

    private enum HttpFetchStatus { Ok, NotFound, Error }

    private sealed class GitHubRelease
    {
        [JsonPropertyName("tag_name")]
        public string? TagName { get; set; }

        [JsonPropertyName("html_url")]
        public string? HtmlUrl { get; set; }

        [JsonPropertyName("assets")]
        public GitHubAsset[]? Assets { get; set; }

        [JsonPropertyName("draft")]
        public bool Draft { get; set; }
    }

    private sealed class GitHubTag
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }
    }

    private sealed class GitHubAsset
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("browser_download_url")]
        public string? BrowserDownloadUrl { get; set; }
    }
}
