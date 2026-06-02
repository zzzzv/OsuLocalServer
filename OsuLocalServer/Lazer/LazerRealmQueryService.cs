using System.Collections;
using System.Reflection;
using Realms;
using osu.Game.Beatmaps;
using osu.Game.Collections;
using osu.Game.Scoring;

using OsuLocalServer.Settings;

namespace OsuLocalServer.Lazer;

internal sealed class LazerRealmQueryService
{
    private static readonly Lazy<ulong> lazerSchemaVersion = new(ResolveLazerSchemaVersion);

    private readonly ILogger<LazerRealmQueryService> logger;
    private readonly SettingService _settings;

    public LazerRealmQueryService(SettingService settings, ILogger<LazerRealmQueryService> logger)
    {
        this.logger = logger;
        _settings = settings;
    }

    public List<object> QueryScores(string rql, int depth = 0) =>
        Query(rql, depth, realm => realm.All<ScoreInfo>().Filter(rql));

    public List<object> QueryBeatmaps(string rql, int depth = 0) =>
        Query(rql, depth, realm => realm.All<BeatmapInfo>().Filter(rql));

    public List<object> QueryBeatmapSets(string rql, int depth = 0) =>
        Query(rql, depth, realm => realm.All<BeatmapSetInfo>().Filter(rql));

    public List<object> QueryCollections(string rql, int depth = 0) =>
        Query(rql, depth, realm => realm.All<BeatmapCollection>().Filter(rql));

    // Avoid generic constraint where T : RealmObject to prevent JIT from resolving
    // the Realm assembly before OsuLazerAssemblyResolver.Register() runs.
    public List<object> Query(string rql, int depth, Func<Realm, IEnumerable> queryFunc)
    {
        if (string.IsNullOrWhiteSpace(rql))
            throw new ArgumentException("RQL query string must not be empty.", nameof(rql));

        var path = _settings.Settings.Lazer.ClientRealmPath;
        var tempDir = LazerPaths.GetDefaultTempDirectory();
        Directory.CreateDirectory(tempDir);

        var config = new RealmConfiguration(path)
        {
            IsReadOnly = true,
            SchemaVersion = lazerSchemaVersion.Value,
            FallbackPipePath = tempDir,
        };

        using var realm = Realm.GetInstance(config);

        var items = queryFunc(realm).Cast<object>().ToList();
        var dictList = RealmConverter.ToList(items, depth);
        var typeName = items.Count > 0 ? items[0].GetType().Name : "?";

        logger.LogInformation("RQL query completed [{Type}]: {RQL}, found {Count} records.", typeName, rql, dictList.Count);

        return dictList;
    }

    private static ulong ResolveLazerSchemaVersion()
    {
        var realmAccessType = typeof(ScoreInfo).Assembly.GetType("osu.Game.Database.RealmAccess")
            ?? throw new InvalidOperationException("Could not find osu.Game.Database.RealmAccess type to resolve lazer schema version.");

        var schemaVersionField = realmAccessType.GetField("schema_version", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("Could not find osu.Game.Database.RealmAccess.schema_version field to resolve lazer schema version.");

        var value = schemaVersionField.IsLiteral
            ? schemaVersionField.GetRawConstantValue()
            : schemaVersionField.GetValue(null);

        if (value is null)
            throw new InvalidOperationException("osu.Game.Database.RealmAccess.schema_version value is null.");

        return Convert.ToUInt64(value);
    }
}
