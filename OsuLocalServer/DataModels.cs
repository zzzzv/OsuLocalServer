using MessagePack;

namespace OsuLocalServer;

[MessagePackObject(keyAsPropertyName: true)]
public record class StarRating(double NM, double HT, double DT);

[MessagePackObject(keyAsPropertyName: true)]
public record class ManiaSRData(
    StarRating PPY,
    StarRating XXY
);
