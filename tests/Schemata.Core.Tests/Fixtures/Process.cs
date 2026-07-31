using Schemata.Abstractions.Entities;

namespace Schemata.Core.Tests.Fixtures;

[CanonicalName("processes/{process}")]
public class Process : ICanonicalName
{
    public string? Name { get; set; }

    public string? CanonicalName { get; set; }
}
