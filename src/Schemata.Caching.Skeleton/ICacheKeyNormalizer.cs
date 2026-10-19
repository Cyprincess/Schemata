namespace Schemata.Caching.Skeleton;

/// <summary>Synchronous pure transformation applied to every logical cache key before backend dispatch.</summary>
public interface ICacheKeyNormalizer
{
    string Normalize(string key);
}

/// <summary>Default normalization returning the logical key unchanged.</summary>
public sealed class IdentityCacheKeyNormalizer : ICacheKeyNormalizer
{
    /// <summary>Shared identity normalizer instance.</summary>
    public static IdentityCacheKeyNormalizer Instance { get; } = new();

    private IdentityCacheKeyNormalizer() { }

    public string Normalize(string key) { return key; }
}
