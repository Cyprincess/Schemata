using System;
using Schemata.Scheduling.Skeleton;
using Schemata.Scheduling.Skeleton.Entities;
using Xunit;

namespace Schemata.Scheduling.Tests;

public class SchemataOperationShould
{
    [Fact]
    public void MapSucceededExecution_ToDoneOperationWithResponse() {
        var execution = new SchemataJobExecution {
            Name          = "abc",
            CanonicalName = "operations/abc",
            Job           = "jobs/report",
            State         = ExecutionState.Succeeded,
            StartTime     = DateTime.UtcNow.AddSeconds(-5),
            EndTime       = DateTime.UtcNow,
            Output        = "{\"ok\":true}",
        };

        var operation = SchemataOperation.FromExecution(execution);

        Assert.Equal("abc", operation.Name);
        Assert.Equal("operations/abc", operation.CanonicalName);
        Assert.True(operation.Done);
        Assert.Null(operation.Error);
        Assert.Equal("{\"ok\":true}", operation.Response?.Output);
        Assert.Equal("jobs/report", operation.Metadata?.Job);
        Assert.Equal(execution.StartTime, operation.Metadata?.StartTime);
        Assert.Equal(execution.EndTime, operation.Metadata?.EndTime);
    }

    [Fact]
    public void MapFailedExecution_ToDoneOperationWithError() {
        var execution = new SchemataJobExecution {
            Name          = "def",
            CanonicalName = "operations/def",
            Job           = "jobs/report",
            State         = ExecutionState.Failed,
            StartTime     = DateTime.UtcNow.AddSeconds(-5),
            EndTime       = DateTime.UtcNow,
            RecentError   = "Boom",
        };

        var operation = SchemataOperation.FromExecution(execution);

        Assert.True(operation.Done);
        Assert.Null(operation.Response);
        Assert.NotNull(operation.Error);
        Assert.Equal("UNKNOWN", operation.Error!.Code);
        Assert.Equal("Boom", operation.Error.Message);
    }

    [Fact]
    public void MapPendingExecution_ToUndoneOperationWithoutResult() {
        var execution = new SchemataJobExecution {
            Name          = "ghi",
            CanonicalName = "operations/ghi",
            Job           = "jobs/report",
            State         = ExecutionState.Pending,
            StartTime     = DateTime.UtcNow,
        };

        var operation = SchemataOperation.FromExecution(execution);

        Assert.False(operation.Done);
        Assert.Null(operation.Error);
        Assert.Null(operation.Response);
    }
}
