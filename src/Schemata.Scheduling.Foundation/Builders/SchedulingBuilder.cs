using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Schemata.Scheduling.Skeleton;

namespace Schemata.Scheduling.Foundation.Builders;

/// <summary>Fluent builder for registering scheduled jobs with the Scheduling feature.</summary>
public sealed class SchedulingBuilder
{
    private readonly TimeProvider _time;

    /// <summary>Initializes a new <see cref="SchedulingBuilder" /> bound to the given service collection.</summary>
    /// <param name="services">The service collection used to register jobs and options.</param>
    /// <param name="timeProvider">Clock used to anchor relative one-time delays; defaults to the system clock.</param>
    public SchedulingBuilder(IServiceCollection services, TimeProvider? timeProvider = null) {
        Services = services;
        _time    = timeProvider ?? TimeProvider.System;
    }

    /// <summary>The underlying service collection used to register jobs and configure options.</summary>
    public IServiceCollection Services { get; }

    /// <summary>Registers <typeparamref name="T" /> against the supplied <see cref="IScheduleDefinition" />.</summary>
    public SchedulingBuilder WithJob<T>(IScheduleDefinition schedule)
        where T : class, IScheduledJob {
        Services.TryAddTransient<T>();
        Services.Configure<SchemataSchedulingOptions>(options => { options.Jobs.Add(new(typeof(T), schedule)); });
        return this;
    }

    /// <summary>Registers <typeparamref name="T" /> on a <see cref="CronSchedule" /> from the given expression.</summary>
    public SchedulingBuilder WithJob<T>(string cronExpression)
        where T : class, IScheduledJob {
        return WithJob<T>(new CronSchedule(cronExpression));
    }

    /// <summary>Registers <typeparamref name="T" /> for a one-time fire at <c>UtcNow + delay</c>.</summary>
    public SchedulingBuilder WithJob<T>(TimeSpan delay)
        where T : class, IScheduledJob {
        return WithJob<T>(new OneTimeSchedule(_time.GetUtcNow().UtcDateTime + delay));
    }

    /// <summary>Registers <typeparamref name="T" /> for a one-time fire at the given UTC <paramref name="runTime" />.</summary>
    public SchedulingBuilder WithJob<T>(DateTime runTime)
        where T : class, IScheduledJob {
        return WithJob<T>(new OneTimeSchedule(runTime));
    }
}
