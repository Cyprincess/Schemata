using System;

namespace Schemata.Abstractions.Entities;

public interface IConcurrency
{
    Guid? Timestamp { get; set; }
}
