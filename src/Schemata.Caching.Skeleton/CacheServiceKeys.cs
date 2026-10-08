namespace Schemata.Caching.Skeleton;

/// <summary>Framework-reserved keyed registration slots for the cache provider pipeline.</summary>
public static class CacheServiceKeys
{
    /// <summary>Keyed slot holding the backend selected by the last explicit provider registration.</summary>
    public const string Backend = "Schemata.Caching.Backend";

    /// <summary>Keyed slot holding the executable public selection resolved by the canonical outlet.</summary>
    public const string Selected = "Schemata.Caching.Selected";
}
