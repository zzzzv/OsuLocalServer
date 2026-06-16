using System.Text.RegularExpressions;
using Microsoft.AspNetCore.SignalR;

namespace OsuLocalServer;

public sealed class VersionCheckService
{
    public const string LatestUrl = "https://github.com/zzzzv/OsuLocalServer/releases/latest";
    private const string UserAgent = "OsuLocalServer";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<VersionCheckService> _logger;
    private readonly IHubContext<ServerHub> _hubContext;

    private VersionCheckResult? _cachedResult;
    private DateTime _lastCheck;

    private static readonly Regex TagRegex = new(
        @"/releases/tag/v?(\d+\.\d+\.\d+)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public VersionCheckService(IHttpClientFactory httpClientFactory, ILogger<VersionCheckService> logger, IHubContext<ServerHub> hubContext)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        _hubContext = hubContext;
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
        try
        {
            using var http = _httpClientFactory.CreateClient(nameof(VersionCheckService));
            http.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);

            var html = await http.GetStringAsync(LatestUrl);
            var match = TagRegex.Match(html);

            if (!match.Success)
            {
                _logger.LogWarning("Could not find version tag in releases page");
                return;
            }

            var latestStr = match.Groups[1].Value;
            var latest = Version.TryParse(latestStr, out var v) ? v : null;
            var current = Version.TryParse(Utils.AppVersion, out var c) ? c : null;

            var isNewer = latest is not null && current is not null && latest > current;
            var isUpToDate = latest is not null && current is not null && latest <= current;

            _cachedResult = new VersionCheckResult(isNewer, isUpToDate, latest?.ToString(3));
            _lastCheck = DateTime.UtcNow;

            _logger.LogInformation(
                "Version check completed: local={Local}, latest={Latest}, updateAvailable={UpdateAvailable}",
                Utils.AppVersion, latest?.ToString(3) ?? "unknown", isNewer);

            _ = _hubContext.Clients.All.SendAsync("VersionCheckResult", _cachedResult);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Failed to check latest version from GitHub (network error)");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error while checking latest version");
        }
    }
}

public sealed record VersionCheckResult(bool? IsNewer, bool? IsUpToDate, string? LatestVersion);
