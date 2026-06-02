using OsuParsers.Decoders;

namespace OsuLocalServer.Stable;

internal static class StableDatabase
{
    public static CollectionOpResult AddToCollection(string dbPath, string name, string[] beatmapMd5Hashes)
    {
        var db = DatabaseDecoder.DecodeCollection(dbPath);

        var existing = db.Collections.FirstOrDefault(c => c.Name == name);
        bool created;

        if (existing is not null)
        {
            created = false;
            var existingHashes = new HashSet<string>(existing.MD5Hashes);
            existingHashes.UnionWith(beatmapMd5Hashes);
            if (existingHashes.Count != existing.MD5Hashes.Count)
            {
                existing.MD5Hashes.Clear();
                existing.MD5Hashes.AddRange(existingHashes);
                existing.Count = existing.MD5Hashes.Count;
            }
        }
        else
        {
            created = true;
            var c = new OsuParsers.Database.Objects.Collection { Name = name };
            c.MD5Hashes.AddRange(beatmapMd5Hashes);
            c.Count = c.MD5Hashes.Count;
            db.Collections.Add(c);
        }

        db.CollectionCount = db.Collections.Count;
        db.Save(dbPath);

        var count = db.Collections.First(c => c.Name == name).MD5Hashes.Count;
        return new CollectionOpResult(name, count, created);
    }
}
