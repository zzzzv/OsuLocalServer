using MessagePack;

namespace OsuLocalServer;

/// <summary>mania SR 算法。</summary>
public enum ManiaSRAlgorithm
{
    PPY = 0,
    XXY = 1,
    SPM = 2,
}

/// <summary>单个算法槽位的状态，每个槽位占状态位图中的 2 bit。</summary>
public enum SRState : byte
{
    /// <summary>未计算，下次生成任务会补算。</summary>
    Pending = 0,

    /// <summary>有值。</summary>
    Ok = 1,

    /// <summary>计算失败。终态，不再重试（需要重试时用「重算失败项」）。</summary>
    Failed = 2,

    /// <summary>算法不适用（如 SPM 只支持 4K/6K/7K）。终态，不再重试。</summary>
    Unsupported = 3,
}

/// <summary>
/// 星级三元组（NM / HT / DT）。msgpack 用定长数组 <c>[NM, HT, DT]</c> 存储，字段名不入包。
/// </summary>
[MessagePackObject]
public record class StarRating(
    [property: Key(0)] double NM,
    [property: Key(1)] double HT,
    [property: Key(2)] double DT)
{
    public static readonly StarRating Zero = new(0, 0, 0);

    /// <summary>SR 恒大于 0，全 0 视为没有值。</summary>
    [IgnoreMember]
    public bool IsValid => NM > 0 || HT > 0 || DT > 0;
}

/// <summary>
/// 一个谱面的 SR 数据。msgpack 用定长数组 <c>[状态位, PPY, XXY, SPM]</c> 存储，
/// 尾部槽位缺省时读出为 null。
/// </summary>
[MessagePackObject]
public sealed record class ManiaSRData
{
    /// <summary>状态位图：bit0-1 = PPY，bit2-3 = XXY，bit4-5 = SPM。</summary>
    [Key(0)]
    public byte St { get; init; }

    [Key(1)]
    public StarRating? PPY { get; init; }

    [Key(2)]
    public StarRating? XXY { get; init; }

    [Key(3)]
    public StarRating? SPM { get; init; }

    [IgnoreMember]
    public static ManiaSRData Empty => new();

    public SRState StateOf(ManiaSRAlgorithm algorithm) => (SRState)((St >> ShiftOf(algorithm)) & 0b11);

    public StarRating? RatingOf(ManiaSRAlgorithm algorithm) => algorithm switch
    {
        ManiaSRAlgorithm.PPY => PPY,
        ManiaSRAlgorithm.XXY => XXY,
        _ => SPM,
    };

    /// <summary>写入某个算法的状态与值。传 null 表示该槽位没有值。</summary>
    public ManiaSRData With(ManiaSRAlgorithm algorithm, SRState state, StarRating? rating = null)
    {
        int shift = ShiftOf(algorithm);
        byte st = (byte)((St & ~(0b11 << shift)) | (((byte)state & 0b11) << shift));
        return algorithm switch
        {
            ManiaSRAlgorithm.PPY => this with { St = st, PPY = rating },
            ManiaSRAlgorithm.XXY => this with { St = st, XXY = rating },
            _ => this with { St = st, SPM = rating },
        };
    }

    /// <summary>把某个算法重置为未计算。</summary>
    public ManiaSRData Pending(ManiaSRAlgorithm algorithm) => With(algorithm, SRState.Pending);

    private static int ShiftOf(ManiaSRAlgorithm algorithm) => (int)algorithm * 2;
}
