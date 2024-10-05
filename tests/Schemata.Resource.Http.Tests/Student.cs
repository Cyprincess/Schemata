using System;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Resource;

namespace Schemata.Resource.Http.Tests;

public class Student : IIdentifier, IConcurrency, IFreshness
{
    public long Id { get; set; }

    public string? Name { get; set; }

    public int Age { get; set; }

    public int Grade { get; set; }

    public Guid? Timestamp { get; set; }

    public string? EntityTag { get; set; }
}
