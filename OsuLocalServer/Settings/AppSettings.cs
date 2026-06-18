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
    };

    public string Urls { get; set; } = "http://localhost:5048";
    public bool OpenBrowserOnStartup { get; set; } = true;
    public bool BackupBeforeWrite { get; set; } = true;
    public UpdateSource UpdateSource { get; set; } = UpdateSource.Gitee;
    public LazerSettings Lazer { get; set; } = new();
    public StableSettings Stable { get; set; } = new();
    public ApiV2Credentials ApiV2 { get; set; } = new();
    public ManagementSettings Management { get; set; } = new();

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
