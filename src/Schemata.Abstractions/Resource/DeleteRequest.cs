using Schemata.Abstractions.Entities;

namespace Schemata.Abstractions.Resource;

public class DeleteRequest : ICanonicalName
{
    public string? Etag { get; set; }

    public bool Force { get; set; }

    #region ICanonicalName Members

    public string? Name { get; set; }

    public string? CanonicalName { get; set; }

    #endregion
}
