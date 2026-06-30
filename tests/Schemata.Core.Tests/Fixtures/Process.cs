using System.ComponentModel;
using Schemata.Abstractions.Entities;

namespace Schemata.Core.Tests.Fixtures;

[DisplayName("Process")]
[CanonicalName("processes/{process}")]
public class Process
{
    public string? Name { get; set; }
}
