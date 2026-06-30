using System.ComponentModel;
using Schemata.Abstractions.Entities;

namespace Schemata.Core.Tests.Fixtures;

[DisplayName("Step")]
[CanonicalName("processes/{process}/steps/{step}")]
public class ProcessStep
{
    public string? Process { get; set; }

    public string? Name { get; set; }
}
