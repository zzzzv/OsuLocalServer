using System.Text.RegularExpressions;
using Microsoft.AspNetCore.SignalR;
using OsuLocalServer.Settings;

namespace OsuLocalServer;

public sealed class VersionCheckService
{
    private const string Owner = "zzzzv";
    private const string Repo = "OsuLocalServer";
    private const string GiteeToolsOwner = "zzzzv";
    private const string GiteeToolsRepo = "osu-tools";
    private const string GiteeProjectFolder = "OsuLocalServer";
    private const string UserAgent = "OsuLocalServer";

    private static readonly Regex TagRegex = new(
        @"/releases/tag/v?(\d+\.\d+\.\d+)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public string GetLatestUrl() => _settingService.Settings.UpdateSource switch
    {
        UpdateSource.Gitee => $"https://gitee.com/{GiteeToolsOwner}/{GiteeToolsRepo}/tree/master/{GiteeProjectFolder}",
        _ => $"https://github.com/{Owner}/{Repo}/releases/latest",
    };

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<VersionCheckService> _logger;
    private readonly IHubContext<ServerHub> _hubContext;
    private readonly SettingService _settingService;

    private VersionCheckResult? _cachedResult;
    private DateTime _lastCheck;

    public void ClearCache()
    {
        _cachedResult = null;
        _lastCheck = default;
    }

    public VersionCheckService(
        IHttpClientFactory httpClientFactory,
        ILogger<VersionCheckService> logger,
        IHubContext<ServerHub> hubContext,
        SettingService settingService)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        _hubContext = hubContext;
        _settingService = settingService;
    }

    public VersionCheckResult Check()
    {
        if (_cachedResult is not null && DateTime.UtcNow - _lastCheck < TimeSpan.FromHours(1))
            return _cachedResult;

        _ = RefreshAsync();
        return _cachedResult ?? new VersionCheckResult(null, null, null);
    }

    private async Task RefreshAsync()
    {
        var source = _settingService.Settings.UpdateSource;
        var sourceLabel = source.ToString();

        try
        {
            using var http = _httpClientFactory.CreateClient(nameof(VersionCheckService));
            http.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);

            string? latestStr = source switch
            {
                UpdateSource.Gitee => await CheckGiteeAsync(http),
                _ => await CheckGitHubAsync(http),
            };

            if (latestStr is null)
                return;

            var latest = Version.TryParse(latestStr, out var v) ? v : null;
            var current = Version.TryParse(Utils.AppVersion, out var c) ? c : null;

            var isNewer = latest is not null && current is not null && latest > current;
            var isUpToDate = latest is not null && current is not null && latest <= current;

            _cachedResult = new VersionCheckResult(isNewer, isUpToDate, latest?.ToString(3));
            _lastCheck = DateTime.UtcNow;

            _logger.LogInformation(
                "Version check ({Source}) completed: local={Local}, latest={Latest}, updateAvailable={UpdateAvailable}",
                sourceLabel, Utils.AppVersion, latest?.ToString(3) ?? "unknown", isNewer);

            _ = _hubContext.Clients.All.SendAsync("VersionCheckResult", _cachedResult);
        }
        catch (HttpRequestException ex)
        {
            var reason = ex.StatusCode is not null
                ? $"HTTP {(int)ex.StatusCode}"
                : "连接失败";
            _logger.LogWarning(ex, "Failed to check latest version from {Source} ({Reason})", sourceLabel, reason);
            _ = _hubContext.Clients.All.SendAsync("VersionCheckFailed", sourceLabel, reason);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error while checking latest version from {Source}", sourceLabel);
            _ = _hubContext.Clients.All.SendAsync("VersionCheckFailed", sourceLabel, ex.Message);
        }
    }

    private static async Task<string?> CheckGitHubAsync(HttpClient http)
    {
        var url = $"https://github.com/{Owner}/{Repo}/releases/latest";
        var html = await http.GetStringAsync(url);
        var match = TagRegex.Match(html);
        return match.Success ? match.Groups[1].Value : null;
    }

    private static async Task<string?> CheckGiteeAsync(HttpClient http)
    {
        var url = $"https://gitee.com/{GiteeToolsOwner}/{GiteeToolsRepo}/raw/master/{GiteeProjectFolder}/version.txt";
        var text = await http.GetStringAsync(url);
        return text.Trim().TrimStart('v');
    }
}

public sealed record VersionCheckResult(bool? IsNewer, bool? IsUpToDate, string? LatestVersion);
