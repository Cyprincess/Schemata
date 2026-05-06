using Schemata.Abstractions.Entities;

namespace Schemata.Entity.Repository.Tests.Fixtures;

public class NoPatternEntity : ICanonicalName
{
    #region ICanonicalName Members

    public string? Name { get; set; }

    public string? CanonicalName { get; set; }

    #endregion
}
