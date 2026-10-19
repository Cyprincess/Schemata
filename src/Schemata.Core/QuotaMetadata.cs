using System.Threading.RateLimiting;

namespace Schemata.Core;

public static class QuotaMetadata
{
    public static readonly MetadataName<string> Subject = new("schemata.quota.subject");
}
