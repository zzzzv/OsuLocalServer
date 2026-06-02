using System.Collections;
using System.Reflection;
using Realms;
using osu.Game.Collections;
using osu.Game.Scoring;

namespace OsuLocalServer.Lazer;

internal static class LazerRealm
{
    private static readonly Lazy<ulong> lazerSchemaVersion = new(ResolveLazerSchemaVersion);

    public static Realm OpenRealm(string clientRealmPath, bool isReadOnly = true)
    {
        var tempDir = LazerPaths.GetDefaultTempDirectory();
        Directory.CreateDirectory(tempDir);
        var config = new RealmConfiguration(clientRealmPath)
        {
            IsReadOnly = isReadOnly,
            SchemaVersion = lazerSchemaVersion.Value,
            FallbackPipePath = tempDir,
        };
        return Realm.GetInstance(config);
    }

    public static List<object> Query(string clientRealmPath, string rql, int depth, Func<Realm, IEnumerable> queryFunc)
    {
        if (string.IsNullOrWhiteSpace(rql))
            throw new ArgumentException("RQL query string must not be empty.", nameof(rql));

        using var realm = OpenRealm(clientRealmPath);

        var items = queryFunc(realm).Cast<object>().ToList();
        return RealmConverter.ToList(items, depth);
    }

    public static CollectionOpResult AddToCollection(string clientRealmPath, string name, string[] beatmapMd5Hashes)
    {
        using var realm = OpenRealm(clientRealmPath, false);

        bool wasNew = false;
        realm.Write(() =>
        {
            var existing = realm.All<BeatmapCollection>()
                .FirstOrDefault(c => c.Name == name);

            if (existing is not null)
            {
                wasNew = false;
                foreach (var hash in beatmapMd5Hashes)
                {
                    if (!existing.BeatmapMD5Hashes.Contains(hash))
                        existing.BeatmapMD5Hashes.Add(hash);
                }
                existing.LastModified = DateTimeOffset.UtcNow;
            }
            else
            {
                wasNew = true;
                realm.Add(new BeatmapCollection(name, beatmapMd5Hashes.ToList()));
            }
        });

        var created = realm.All<BeatmapCollection>().First(c => c.Name == name);
        return new CollectionOpResult(name, created.BeatmapMD5Hashes.Count, wasNew);
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
