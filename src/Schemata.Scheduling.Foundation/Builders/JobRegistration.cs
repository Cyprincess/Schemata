using System;
using Schemata.Scheduling.Skeleton;

namespace Schemata.Scheduling.Foundation.Builders;

public sealed class JobRegistration
{
    public JobRegistration(Type jobType, IScheduleDefinition schedule) {
        JobType  = jobType;
        Schedule = schedule;
    }

    public Type JobType { get; }

    public IScheduleDefinition Schedule { get; }
}
