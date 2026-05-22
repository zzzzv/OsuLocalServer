using System.Collections;
using System.Reflection;
using Realms;
using osu.Game.Beatmaps;
using osu.Game.Collections;
using osu.Game.Scoring;

internal sealed class LazerScoreQueryService
{
    private static readonly Lazy<ulong> lazerSchemaVersion = new(ResolveLazerSchemaVersion);

    private readonly ILogger<LazerScoreQueryService> logger;
    private readonly string clientRealmPath;
    private readonly string tempDirectory;
    private readonly RealmConfiguration configuration;

    public LazerScoreQueryService(IConfiguration configuration, ILogger<LazerScoreQueryService> logger)
    {
        this.logger = logger;

        var dataDirectory = ServerConfig.GetDataDirectory(configuration);
        clientRealmPath = Path.Combine(dataDirectory, "client.realm");
        tempDirectory = ServerConfig.GetTempDirectory(configuration);

        Directory.CreateDirectory(tempDirectory);

        this.configuration = new RealmConfiguration(clientRealmPath)
        {
            IsReadOnly = true,
            SchemaVersion = lazerSchemaVersion.Value,
            FallbackPipePath = tempDirectory
        };
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

        using var realm = Realm.GetInstance(configuration);

        var items = queryFunc(realm).Cast<object>().ToList();
        var dictList = RealmConverter.ToList(items, depth);
        var typeName = items.Count > 0 ? items[0].GetType().Name : "?";

        logger.LogInformation("RQL query completed [{Type}]: {RQL}, found {Count} records.", typeName, rql, dictList.Count);

        return dictList;
    }

    private static ulong ResolveLazerSchemaVersion()
    {
        var realmAccessType = typeof(ScoreInfo).Assembly.GetType("osu.Game.Database.RealmAccess")
            ?? throw new InvalidOperationException("未找到 osu.Game.Database.RealmAccess, 无法获取 lazer 数据库 schema version。");

        var schemaVersionField = realmAccessType.GetField("schema_version", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("未找到 osu.Game.Database.RealmAccess.schema_version, 无法获取 lazer 数据库 schema version。");

        var value = schemaVersionField.IsLiteral
            ? schemaVersionField.GetRawConstantValue()
            : schemaVersionField.GetValue(null);

        if (value is null)
        {
            throw new InvalidOperationException("osu.Game.Database.RealmAccess.schema_version 的值为空。");
        }

        return Convert.ToUInt64(value);
    }
}
