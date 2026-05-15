using System;

namespace Schemata.Scheduling.Skeleton;

public interface IScheduleDefinition
{
    bool      IsRecurring { get; }
    DateTime? GetNextRunTime(DateTime from);
}
