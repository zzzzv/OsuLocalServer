namespace OsuLocalServer;

public sealed record CreateCollectionRequest(
    string Name,
    string[] BeatmapMd5Hashes
);

public sealed record CollectionOpResult(string Name, int BeatmapCount, bool Created);

