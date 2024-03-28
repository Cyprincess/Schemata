using System;

namespace Schemata.Abstractions.Entity;

public interface IConcurrency
{
    Guid? Timestamp { get; set; }
}
