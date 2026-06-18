using OsuParsers.Decoders;
using OsuParsers.Database.Objects;
using OsuParsers.Enums;

namespace OsuLocalServer.Stable;

internal static class StableDatabase
{
    public static string GetBeatmapPath(this DbBeatmap beatmap, string osuRoot) =>
        Path.Combine(osuRoot, "Songs", beatmap.FolderName, beatmap.FileName);

    public static CollectionOpResult AddToCollection(string osuRoot, string name, string[] beatmapMd5Hashes, bool backup = false)
    {
        if (Utils.IsOsuProcessRunning(osuRoot))
            throw new InvalidOperationException("osu!stable 正在运行，无法写入 collection.db");

        var dbPath = Path.Combine(osuRoot, "collection.db");
        var db = DatabaseDecoder.DecodeCollection(dbPath);

        var existing = db.Collections.FirstOrDefault(c => c.Name == name);
        bool created;
        bool changed = false;

        if (existing is not null)
        {
            created = false;
            var existingHashes = new HashSet<string>(existing.MD5Hashes);
            existingHashes.UnionWith(beatmapMd5Hashes);
            if (existingHashes.Count != existing.MD5Hashes.Count)
            {
                changed = true;
                existing.MD5Hashes.Clear();
                existing.MD5Hashes.AddRange(existingHashes);
                existing.Count = existing.MD5Hashes.Count;
            }
        }
        else
        {
            created = true;
            changed = true;
            var c = new Collection { Name = name };
            c.MD5Hashes.AddRange(beatmapMd5Hashes);
            c.Count = c.MD5Hashes.Count;
            db.Collections.Add(c);
        }

        if (changed && backup)
            Utils.BackupFile(dbPath);

        db.CollectionCount = db.Collections.Count;
        db.Save(dbPath);

        var count = db.Collections.First(c => c.Name == name).MD5Hashes.Count;
        return new CollectionOpResult(name, count, created);
    }

    public static int WriteManiaStarRatings(string osuDbPath, Dictionary<string, StarRating> starRatings, bool backup = false)
    {
        var osuRoot = Path.GetDirectoryName(osuDbPath)!;
        if (Utils.IsOsuProcessRunning(osuRoot))
            throw new InvalidOperationException("osu!stable 正在运行，无法写入 osu!.db");

        var db = DatabaseDecoder.DecodeOsu(osuDbPath);
        int updated = 0;

        foreach (var bm in db.Beatmaps)
        {
            if (bm.MD5Hash is null) continue;
            if (starRatings.TryGetValue(bm.MD5Hash, out var sr))
            {
                bm.ManiaStarRating[Mods.None] = sr.NM;
                bm.ManiaStarRating[Mods.HalfTime] = sr.HT;
                bm.ManiaStarRating[Mods.DoubleTime] = sr.DT;
                updated++;
            }
        }

        if (updated > 0 && backup)
            Utils.BackupFile(osuDbPath);

        db.Save(osuDbPath);
        return updated;
    }
}
