using System.Collections.Generic;
using Schemata.Scheduling.Foundation.Builders;

namespace Schemata.Scheduling.Foundation;

public class SchemataSchedulingOptions
{
    public List<JobRegistration> Jobs { get; } = new();
}
