namespace Schemata.Caching.Redis;

/// <summary>Constants used by the Redis cache provider.</summary>
public static class RedisConstants
{
    /// <summary>Fixed namespace prefix framing the value (or set) key of every logical cache key.</summary>
    public const string DataPrefix = "__data__:";

    /// <summary>
    ///     Fixed namespace prefix framing the expiration-metadata key of every logical cache key.
    /// </summary>
    /// <remarks>
    ///     <see cref="DataPrefix" /> and <see cref="MetaPrefix" /> are fixed and neither is a prefix
    ///     of the other, so a data key and a metadata key can never collide, whatever the logical key
    ///     contains. Both embed the logical key unchanged, so any hash tag inside it still maps both
    ///     framed keys to the same Redis Cluster slot.
    /// </remarks>
    public const string MetaPrefix = "__meta__:";
}
