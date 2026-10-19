using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Schemata.Abstractions.Entities;
using Schemata.Scheduling.Skeleton;
using Schemata.Scheduling.Skeleton.Attributes;
using Schemata.Scheduling.Skeleton.Entities;

namespace Schemata.Scheduling.Integration.Tests.Fixtures;

[Table("CustomJobs")]
[CanonicalName("jobs/{job}")]
public sealed class MyJob : SchemataJob;

[Table("CustomJobExecutions")]
[CanonicalName("operations/{operation}")]
public sealed class MyExecution : SchemataJobExecution;

[ScheduledJob(JobKey)]
public sealed class CustomProbeJob : IScheduledJob
{
    public const string JobKey = "custom.probe";

    public List<Guid> Runs { get; } = [];

    public Task ExecuteAsync(JobContext context, CancellationToken ct) {
        Runs.Add(context.ExecutionUid.GetValueOrDefault());
        return Task.CompletedTask;
    }
}

public sealed class CustomSchedulingDbContext(DbContextOptions<CustomSchedulingDbContext> options) : DbContext(options)
{
    public DbSet<MyJob>       MyJobs       { get; set; } = null!;
    public DbSet<MyExecution> MyExecutions { get; set; } = null!;
}
