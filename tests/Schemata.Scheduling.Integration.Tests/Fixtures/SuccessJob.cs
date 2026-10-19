using System.Threading;
using System.Threading.Tasks;
using Schemata.Scheduling.Skeleton;

namespace Schemata.Scheduling.Integration.Tests.Fixtures;

public sealed class SuccessJob : IScheduledJob
{
    public const string Key    = "jobs.success";
    public const string Output = """{"status":"done"}""";

    public Task ExecuteAsync(JobContext context, CancellationToken ct) {
        context.Execution!.Output = Output;

        return Task.CompletedTask;
    }
}