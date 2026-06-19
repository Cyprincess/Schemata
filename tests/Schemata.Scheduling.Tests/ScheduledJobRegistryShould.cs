using System.Threading;
using System.Threading.Tasks;
using Schemata.Scheduling.Foundation.Internal;
using Schemata.Scheduling.Skeleton;
using Schemata.Scheduling.Skeleton.Attributes;
using Xunit;

namespace Schemata.Scheduling.Tests;

public class ScheduledJobRegistryShould
{
    [Fact]
    public void Resolve_RegisteredJobByDefaultKey() {
        IScheduledJobRegistry registry = new DefaultScheduledJobRegistry();

        registry.Register<DefaultKeyJob>();

        Assert.Equal(typeof(DefaultKeyJob), registry.Resolve(typeof(DefaultKeyJob).FullName!));
        Assert.Equal(typeof(DefaultKeyJob).FullName, registry.ResolveKey(typeof(DefaultKeyJob)));
    }

    [Fact]
    public void Resolve_RegisteredJobByAttributeKey() {
        IScheduledJobRegistry registry = new DefaultScheduledJobRegistry();

        registry.RegisterAll([typeof(AttributeKeyJob)]);

        Assert.Equal(typeof(AttributeKeyJob), registry.Resolve("jobs:attribute"));
        Assert.Equal("jobs:attribute", registry.ResolveKey(typeof(AttributeKeyJob)));
    }

    [Fact]
    public void RegisterAll_IgnoresNonJobTypes() {
        IScheduledJobRegistry registry = new DefaultScheduledJobRegistry();

        registry.RegisterAll([typeof(AttributeKeyJob), typeof(NonJob)]);

        Assert.Null(registry.ResolveKey(typeof(NonJob)));
    }

    #region Nested type: AttributeKeyJob

    [ScheduledJob("jobs:attribute")]
    private sealed class AttributeKeyJob : IScheduledJob
    {
        #region IScheduledJob Members

        public Task ExecuteAsync(JobContext context, CancellationToken ct) { return Task.CompletedTask; }

        #endregion
    }

    #endregion

    #region Nested type: DefaultKeyJob

    private sealed class DefaultKeyJob : IScheduledJob
    {
        #region IScheduledJob Members

        public Task ExecuteAsync(JobContext context, CancellationToken ct) { return Task.CompletedTask; }

        #endregion
    }

    #endregion

    #region Nested type: NonJob

    private sealed class NonJob;

    #endregion
}
