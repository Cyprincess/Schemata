using System;
using System.Collections.Generic;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Schemata.Scheduling.Skeleton;

namespace Schemata.Scheduling.Foundation.Builders;

public sealed class SchedulingBuilder
{
    internal readonly List<JobRegistration> Jobs = new();
    internal readonly IServiceCollection    Services;

    public SchedulingBuilder(IServiceCollection services) { Services = services; }

    public SchedulingBuilder WithJob<T>(IScheduleDefinition schedule)
        where T : class, IScheduledJob {
        Services.TryAddTransient(typeof(T));
        Jobs.Add(new(typeof(T), schedule));
        return this;
    }

    public SchedulingBuilder WithJob<T>(string cronExpression)
        where T : class, IScheduledJob {
        return WithJob<T>(new CronSchedule(cronExpression));
    }

    public SchedulingBuilder WithJob<T>(TimeSpan delay)
        where T : class, IScheduledJob {
        return WithJob<T>(new OneTimeSchedule(DateTime.UtcNow + delay));
    }

    public SchedulingBuilder WithJob<T>(DateTime runTime)
        where T : class, IScheduledJob {
        return WithJob<T>(new OneTimeSchedule(runTime));
    }
}
