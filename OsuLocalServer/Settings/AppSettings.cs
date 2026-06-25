using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using OsuLocalServer.Lazer;
using OsuLocalServer.Stable;

namespace OsuLocalServer.Settings;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum UpdateSource
{
    GitHub,
    Gitee,
}

public sealed class AppSettings
{
    internal static readonly string StorageDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        Assembly.GetEntryAssembly()?.GetName()?.Name ?? "OsuLocalServer");

    private const string FileName = "settings.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        ReadCommentHandling = JsonCommentHandling.Skip,
        IgnoreReadOnlyProperties = true,
    };

    public string Urls { get; set; } = "http://localhost:5048";
    public bool OpenBrowserOnStartup { get; set; } = true;
    public UpdateSource UpdateSource { get; set; } = UpdateSource.Gitee;
    public LazerSettings Lazer { get; set; } = new();
    public StableSettings Stable { get; set; } = new();
    public ApiV2Credentials ApiV2 { get; set; } = new();
    public ManagementSettings Management { get; set; } = new();
    public ManiaLabSettings ManiaLab { get; set; } = new();

    public static AppSettings Load()
    {
        var path = Path.Combine(StorageDir, FileName);

        if (!File.Exists(path))
        {
            var defaults = new AppSettings();
            defaults.Save();
            return defaults;
        }

        try
        {
            var json = File.ReadAllText(path);
            var settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
            settings.Lazer ??= new LazerSettings();
            settings.Stable ??= new StableSettings();
            settings.ApiV2 ??= new ApiV2Credentials();
            settings.Management ??= new ManagementSettings();
            settings.ManiaLab ??= new ManiaLabSettings();
            return settings;
        }
        catch
        {
            var defaults = new AppSettings();
            defaults.Save();
            return defaults;
        }
    }

    public void Save()
    {
        Directory.CreateDirectory(StorageDir);
        var path = Path.Combine(StorageDir, FileName);
        var json = JsonSerializer.Serialize(this, JsonOptions);
        File.WriteAllText(path, json);
    }
}

public sealed class LazerSettings
{
    public string ClientRealmPath { get; set; } = Path.Combine(LazerPaths.GetDefaultDataDirectory(), "client.realm");

    [JsonIgnore]
    public bool Disable { get; set; }

    public bool IsAvailable => !Disable && LazerPaths.IsAvailable();
}

public sealed class StableSettings
{
    public bool BackupBeforeWrite { get; set; } = true;
    public string OsuRootPath { get; set; } = StablePathResolver.TryFindOsuRootPath() ?? "";

    [JsonIgnore]
    public bool Disable { get; set; }

    public bool IsAvailable => !Disable && StablePathResolver.IsValidOsuRoot(OsuRootPath);
}

public sealed class ApiV2Credentials
{
    public string ClientId { get; set; } = "";
    public string ClientSecret { get; set; } = "";
    public string? AccessToken { get; set; }
    public DateTimeOffset TokenExpiresAt { get; set; } = DateTimeOffset.MinValue;
}

public sealed class ManagementSettings
{
    public string ManiaSRPackPath { get; set; } = Path.Combine(AppSettings.StorageDir, "mania_sr.msgpack");
}

public sealed class ManiaLabSettings
{
    /// <summary>null 或 "latest" 表示自动使用最新已安装版本。</summary>
    public string? SelectedVersion { get; set; }

    public string VersionsDir => Path.Combine(AppSettings.StorageDir, "mania-lab");
    public string VersionDir(string version) => Path.Combine(VersionsDir, version);

    public string? SelectedVersionEffective
    {
        get
        {
            var installed = InstalledVersions;
            if (SelectedVersion is null or "latest")
                return installed.FirstOrDefault();
            return installed.Contains(SelectedVersion) ? SelectedVersion : installed.FirstOrDefault();
        }
    }

    public string? CurrentDir
    {
        get
        {
            var v = SelectedVersionEffective;
            return v is not null ? VersionDir(v) : null;
        }
    }

    public List<string> InstalledVersions
    {
        get
        {
            if (!Directory.Exists(VersionsDir))
                return new List<string>();

            return Directory.GetDirectories(VersionsDir)
                .Select(Path.GetFileName)
                .Where(v => v is not null && File.Exists(Path.Combine(VersionsDir, v, "index.html")))
                .OrderByDescending(v => v)
                .ToList()!;
        }
    }

    public bool IsAvailable => SelectedVersionEffective is not null
        && Directory.Exists(CurrentDir)
        && File.Exists(Path.Combine(CurrentDir!, "index.html"));

    public static string? ExtractVersionFromZipName(string fileName)
    {
        const string prefix = "mania-lab-";
        const string suffix = ".zip";
        if (fileName.StartsWith(prefix) && fileName.EndsWith(suffix))
            return fileName.Substring(prefix.Length, fileName.Length - prefix.Length - suffix.Length);
        return null;
    }
}
