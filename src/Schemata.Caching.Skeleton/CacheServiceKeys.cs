namespace Schemata.Caching.Skeleton;

/// <summary>Framework-reserved keyed registration slot for the cache backend pipeline.</summary>
public static class CacheServiceKeys
{
    /// <summary>Keyed slot holding the backend selected by the last explicit registration.</summary>
    public const string Backend = "Schemata.Caching.Backend";
}
