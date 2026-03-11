using Schemata.Abstractions.Entities;

namespace Schemata.Abstractions.Resource;

public class GetRequest : ICanonicalName
{
    #region ICanonicalName Members

    public string? Name { get; set; }

    public string? CanonicalName { get; set; }

    #endregion
}
