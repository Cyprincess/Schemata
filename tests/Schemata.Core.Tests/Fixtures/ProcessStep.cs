using Schemata.Abstractions.Entities;

namespace Schemata.Core.Tests.Fixtures;

[CanonicalName("processes/{process}/steps/{step}")]
public class ProcessStep : ICanonicalName
{
    public string? Process { get; set; }

    public string? Name { get; set; }

    public string? CanonicalName { get; set; }
}
