using System;
using System.Text.Json;
using Schemata.Abstractions.Resource;
using Schemata.Scheduling.Skeleton;
using Schemata.Scheduling.Skeleton.Entities;
using Xunit;

namespace Schemata.Scheduling.Tests;

public class OperationEnvelopeShould
{
    [Fact]
    public void MapSucceededExecution_ToDoneOperationWithResponse() {
        var execution = new SchemataJobExecution {
            Name          = "abc",
            CanonicalName = "operations/abc",
            Job           = "jobs/report",
            Method        = "run",
            State         = ExecutionState.Succeeded,
            StartTime     = DateTime.UtcNow.AddSeconds(-5),
            EndTime       = DateTime.UtcNow,
            Output        = "{\"ok\":true}",
        };

        var operation = OperationMapper.FromExecution(execution);

        Assert.Equal("abc", operation.Name);
        Assert.Equal("operations/abc", operation.CanonicalName);
        Assert.True(operation.Done);
        Assert.Null(operation.Error);
        Assert.Equal("{\"ok\":true}", operation.Response?.Output);
        Assert.Equal("run", operation.Metadata?.Method);
        Assert.Equal("jobs/report", operation.Metadata?.Job);
        Assert.Equal(execution.StartTime, operation.Metadata?.StartTime);
        Assert.Equal(execution.EndTime, operation.Metadata?.EndTime);
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

        var operation = OperationMapper.FromExecution(execution);

        Assert.False(operation.Done);
        Assert.Null(operation.Error);
        Assert.Null(operation.Response);
    }

    [Fact]
    public void FailedExecution_IntegerStatusCode() {
        var execution = new SchemataJobExecution {
            Name          = "def",
            CanonicalName = "operations/def",
            Job           = "jobs/report",
            State         = ExecutionState.Failed,
            StartTime     = DateTime.UtcNow.AddSeconds(-5),
            EndTime       = DateTime.UtcNow,
            RecentError   = "Boom",
        };

        var operation = OperationMapper.FromExecution(execution);

        Assert.True(operation.Done);
        Assert.Null(operation.Response);
        Assert.NotNull(operation.Error);
        Assert.Equal(2, operation.Error!.Code);
        Assert.Equal("Boom", operation.Error.Message);
    }

    [Fact]
    public void Response_NotDoubleEncoded() {
        var operation = new Operation {
            Name          = "abc",
            CanonicalName = "operations/abc",
            Done          = true,
            Response      = new() { Output = "{\"ok\":true}" },
        };

        var json = JsonSerializer.Serialize(operation);

        using var document = JsonDocument.Parse(json);
        var output = document.RootElement.GetProperty(nameof(Operation.Response))
                             .GetProperty(nameof(OperationResponse.Output));

        // A double-encoded result would surface Output as a JSON string literal.
        Assert.Equal(JsonValueKind.Object, output.ValueKind);
        Assert.True(output.GetProperty("ok").GetBoolean());
    }

}
