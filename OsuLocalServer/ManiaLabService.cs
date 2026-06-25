using System.IO.Compression;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.SignalR;
using OsuLocalServer.Settings;

namespace OsuLocalServer;

public sealed class ManiaLabService
{
    private const string Owner = "zzzzv";
    private const string Repo = "mania-lab";
    private const string GiteeToolsOwner = "zzzzv";
    private const string GiteeToolsRepo = "osu-tools";
    private const string GiteeProjectFolder = "mania-lab";
    private const string UserAgent = "OsuLocalServer";

    private static readonly Regex GitHubTagRegex = new(
        @"/releases/tag/(\d{8}-\d{6})",
        RegexOptions.Compiled);

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<ManiaLabService> _logger;
    private readonly IHubContext<ServerHub> _hubContext;
    private readonly SettingService _settingService;

    private string? _cachedLatestVersion;
    private string? _cachedDownloadUrl;
    private DateTime _lastCheck;
    private bool _isDownloading;

    public ManiaLabService(
        IHttpClientFactory httpClientFactory,
        ILogger<ManiaLabService> logger,
        IHubContext<ServerHub> hubContext,
        SettingService settingService)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        _hubContext = hubContext;
        _settingService = settingService;
    }

    /// <summary>获取当前状态（同步，用于 SSR）。</summary>
    public ManiaLabState GetState()
    {
        var settings = _settingService.Settings.ManiaLab;
        return new ManiaLabState(
            InstalledVersion: settings.SelectedVersionEffective,
            LatestVersion: _cachedLatestVersion,
            IsDownloading: _isDownloading,
            IsAvailable: settings.IsAvailable,
            InstalledVersions: settings.InstalledVersions);
    }

    /// <summary>检查最新版本，缓存 1 小时。</summary>
    public async Task<ManiaLabVersionResult> CheckForUpdateAsync()
    {
        if (_cachedLatestVersion is not null && DateTime.UtcNow - _lastCheck < TimeSpan.FromHours(1))
            return new ManiaLabVersionResult(_cachedLatestVersion, _cachedDownloadUrl, null);

        var source = _settingService.Settings.UpdateSource;
        var sourceLabel = source.ToString();

        try
        {
            using var http = _httpClientFactory.CreateClient(nameof(ManiaLabService));
            http.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);

            string? latestVersion;
            string? downloadUrl;

            if (source == UpdateSource.Gitee)
            {
                (latestVersion, downloadUrl) = await CheckGiteeAsync(http);
            }
            else
            {
                (latestVersion, downloadUrl) = await CheckGitHubAsync(http);
            }

            if (latestVersion is null)
            {
                var failMsg = "无法解析最新版本";
                _ = _hubContext.Clients.All.SendAsync("ManiaLabVersionFailed", sourceLabel, failMsg);
                return new ManiaLabVersionResult(null, null, failMsg);
            }

            _cachedLatestVersion = latestVersion;
            _cachedDownloadUrl = downloadUrl;
            _lastCheck = DateTime.UtcNow;

            _logger.LogInformation(
                "ManiaLab version check ({Source}): latest={Latest}, downloadUrl={DownloadUrl}",
                sourceLabel, latestVersion, downloadUrl);

            var result = new ManiaLabVersionResult(latestVersion, downloadUrl, null);
            _ = _hubContext.Clients.All.SendAsync("ManiaLabVersionResult", result);
            return result;
        }
        catch (HttpRequestException ex)
        {
            var reason = ex.StatusCode is not null ? $"HTTP {(int)ex.StatusCode}" : "连接失败";
            _logger.LogWarning(ex, "Failed to check mania-lab version from {Source} ({Reason})", sourceLabel, reason);
            _ = _hubContext.Clients.All.SendAsync("ManiaLabVersionFailed", sourceLabel, reason);
            return new ManiaLabVersionResult(null, null, reason);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error checking mania-lab version from {Source}", sourceLabel);
            _ = _hubContext.Clients.All.SendAsync("ManiaLabVersionFailed", sourceLabel, ex.Message);
            return new ManiaLabVersionResult(null, null, ex.Message);
        }
    }

    /// <summary>下载最新版本并解压。</summary>
    public async Task DownloadLatestAsync()
    {
        if (_isDownloading)
        {
            _logger.LogWarning("Download already in progress");
            return;
        }

        var checkResult = await CheckForUpdateAsync();
        if (checkResult.LatestVersion is null || checkResult.DownloadUrl is null)
        {
            _logger.LogWarning("Cannot download: no version info available");
            return;
        }

        var maniaLab = _settingService.Settings.ManiaLab;
        var version = checkResult.LatestVersion;
        var targetDir = maniaLab.VersionDir(version);

        // 已安装则跳过
        if (Directory.Exists(targetDir) && File.Exists(Path.Combine(targetDir, "index.html")))
        {
            _logger.LogInformation("ManiaLab version {Version} already installed, skipping", version);
            return;
        }

        _isDownloading = true;
        try
        {
            _ = _hubContext.Clients.All.SendAsync("ManiaLabDownloadProgress", "正在下载...");

            using var http = _httpClientFactory.CreateClient(nameof(ManiaLabService));
            http.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);

            var zipPath = Path.Combine(Path.GetTempPath(), $"mania-lab-{version}.zip");

            _logger.LogInformation("Downloading mania-lab {Version} from {Url}", version, checkResult.DownloadUrl);

            using (var response = await http.GetAsync(checkResult.DownloadUrl, HttpCompletionOption.ResponseHeadersRead))
            {
                response.EnsureSuccessStatusCode();
                await using var fs = new FileStream(zipPath, FileMode.Create, FileAccess.Write, FileShare.None);
                await response.Content.CopyToAsync(fs);
            }

            _ = _hubContext.Clients.All.SendAsync("ManiaLabDownloadProgress", "正在解压...");

            // 确保目标目录存在且为空
            if (Directory.Exists(targetDir))
                Directory.Delete(targetDir, recursive: true);
            Directory.CreateDirectory(targetDir);

            ZipFile.ExtractToDirectory(zipPath, targetDir);

            // 清理临时文件
            try { File.Delete(zipPath); } catch { }

            // 自动选择最新版本
            _settingService.Settings.ManiaLab.SelectedVersion = "latest";
            _settingService.Settings.Save();

            _logger.LogInformation("ManiaLab version {Version} installed successfully", version);

            _ = _hubContext.Clients.All.SendAsync("ManiaLabDownloadComplete", version);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to download mania-lab version {Version}", version);
            _ = _hubContext.Clients.All.SendAsync("ManiaLabDownloadFailed", version, ex.Message);
        }
        finally
        {
            _isDownloading = false;
        }
    }

    /// <summary>切换当前激活的版本。</summary>
    public void SelectVersion(string? version)
    {
        var settings = _settingService.Settings;
        settings.ManiaLab.SelectedVersion = version;
        settings.Save();
        _logger.LogInformation("ManiaLab version switched to {Version}", version ?? "latest");
    }

    private static async Task<(string? version, string? downloadUrl)> CheckGitHubAsync(HttpClient http)
    {
        var url = $"https://github.com/{Owner}/{Repo}/releases/latest";
        var html = await http.GetStringAsync(url);
        var match = GitHubTagRegex.Match(html);
        if (!match.Success)
            return (null, null);

        var tag = match.Groups[1].Value;
        var downloadUrl = $"https://github.com/{Owner}/{Repo}/releases/download/{tag}/mania-lab-{tag}.zip";
        return (tag, downloadUrl);
    }

    private static async Task<(string? version, string? downloadUrl)> CheckGiteeAsync(HttpClient http)
    {
        var url = $"https://gitee.com/{GiteeToolsOwner}/{GiteeToolsRepo}/raw/master/{GiteeProjectFolder}/version.txt";
        var text = await http.GetStringAsync(url);
        var version = text.Trim();
        if (string.IsNullOrEmpty(version))
            return (null, null);

        var downloadUrl = $"https://gitee.com/{GiteeToolsOwner}/{GiteeToolsRepo}/raw/master/{GiteeProjectFolder}/mania-lab-{version}.zip";
        return (version, downloadUrl);
    }
}

public sealed record ManiaLabState(
    string? InstalledVersion,
    string? LatestVersion,
    bool IsDownloading,
    bool IsAvailable,
    List<string> InstalledVersions);

public sealed record ManiaLabVersionResult(
    string? LatestVersion,
    string? DownloadUrl,
    string? Error);
