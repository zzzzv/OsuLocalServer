using System.Collections;
using System.Reflection;
using Realms;
using osu.Game.Beatmaps;
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

    public static List<object> Query(string clientRealmPath, string rql, int depth, Func<Realm, IEnumerable> queryFunc, HashSet<string>? noExpandFields = null)
    {
        if (string.IsNullOrWhiteSpace(rql))
            throw new ArgumentException("RQL 查询字符串不能为空。", nameof(rql));

        using var realm = OpenRealm(clientRealmPath);

        var items = queryFunc(realm).Cast<object>().ToList();
        return RealmConverter.ToList(items, depth, noExpandFields);
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
            ?? throw new InvalidOperationException("找不到 osu.Game.Database.RealmAccess 类型，无法解析 lazer schema 版本。");

        var schemaVersionField = realmAccessType.GetField("schema_version", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("找不到 osu.Game.Database.RealmAccess.schema_version 字段，无法解析 lazer schema 版本。");

        var value = schemaVersionField.IsLiteral
            ? schemaVersionField.GetRawConstantValue()
            : schemaVersionField.GetValue(null);

        if (value is null)
            throw new InvalidOperationException("osu.Game.Database.RealmAccess.schema_version 值为 null。");

        return Convert.ToUInt64(value);
    }

    public static int WriteStarRatings(string clientRealmPath, Dictionary<string, double> starRatings)
    {
        using var realm = OpenRealm(clientRealmPath, false);

        int updated = 0;
        realm.Write(() =>
        {
            foreach (var bm in realm.All<BeatmapInfo>())
            {
                if (starRatings.TryGetValue(bm.MD5Hash, out var sr))
                {
                    bm.StarRating = sr;
                    updated++;
                }
            }
        });

        return updated;
    }
}
