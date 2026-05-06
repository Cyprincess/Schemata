using Schemata.Abstractions.Entities;

namespace Schemata.Entity.Repository.Tests.Fixtures;

[CanonicalName("fixtures/{fixture}")]
public class PatternEntity : ICanonicalName
{
    #region ICanonicalName Members

    public string? Name { get; set; }

    public string? CanonicalName { get; set; }

    #endregion
}
