using System;

namespace Schemata.Abstractions.Entities;

public interface ITimestamp
{
    DateTime? CreateTime { get; set; }

    DateTime? UpdateTime { get; set; }
}
