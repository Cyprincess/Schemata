using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Schemata.Common;
using Schemata.Messaging.Skeleton;
using Schemata.Push.Foundation.Commands;
using Schemata.Push.Scheduling.Runtime;
using Schemata.Push.Skeleton;
using Schemata.Scheduling.Skeleton;
using Schemata.Scheduling.Skeleton.Entities;
using Xunit;

namespace Schemata.Push.Tests;

public class PushDispatchJobShould
{
    [Fact]
    public async Task Reconstructs_PushContext_From_Valid_ArgsJson() {
        var expected = new PushContext("hello", new RecipientTarget("user-1")) {
            Options  = new() { Priority = PushPriority.High, CollapseKey = "collapse" },
            Metadata = new Dictionary<string, string?> { ["locale"] = "en" },
        };
        PushContext? sent = null;
        var dispatcher = new Mock<IRequestDispatcher>(MockBehavior.Strict);
        dispatcher.Setup(value => value.SendAsync<SendPushRequest, ImmutableArray<TransportResult>>(
                             It.IsAny<SendPushRequest>(), It.IsAny<CancellationToken>()))
                  .Callback((SendPushRequest request, CancellationToken _) => sent = request.Context)
                  .ReturnsAsync(ImmutableArray<TransportResult>.Empty);
        using var provider = Provider(dispatcher.Object);
        var context = new JobContext {
            ArgsJson  = JsonSerializer.Serialize(expected, SchemataJson.Default),
            Execution = new(),
        };

        await new PushDispatchJob(provider).ExecuteAsync(context, CancellationToken.None);

        Assert.NotNull(sent);
        Assert.Equal("hello", Assert.IsType<JsonElement>(sent.Message).GetString());
        Assert.Equal("user-1", Assert.IsType<RecipientTarget>(sent.Target).Subject);
        Assert.Equal(PushPriority.High, sent.Options.Priority);
        Assert.Equal("collapse", sent.Options.CollapseKey);
        Assert.Equal("en", sent.Metadata["locale"]);
    }

    [Fact]
    public async Task Forwards_CancellationToken_To_Dispatcher() {
        using var cts = new CancellationTokenSource();
        var dispatcher = new Mock<IRequestDispatcher>(MockBehavior.Strict);
        dispatcher.Setup(value => value.SendAsync<SendPushRequest, ImmutableArray<TransportResult>>(
                             It.IsAny<SendPushRequest>(), cts.Token))
                  .ReturnsAsync(ImmutableArray<TransportResult>.Empty);
        using var provider = Provider(dispatcher.Object);
        var context = Context(new("ping", new RecipientTarget("user-2")));

        await new PushDispatchJob(provider).ExecuteAsync(context, cts.Token);

        dispatcher.VerifyAll();
    }

    [Fact]
    public async Task Preserves_Result_Order_In_Execution_Output() {
        var expected = ImmutableArray.Create(
            TransportResult.Sent("transport-a", "addr-a"),
            TransportResult.Sent("transport-b", "addr-b"),
            TransportResult.Skipped("transport-c"));
        var dispatcher = new Mock<IRequestDispatcher>(MockBehavior.Strict);
        dispatcher.Setup(value => value.SendAsync<SendPushRequest, ImmutableArray<TransportResult>>(
                             It.IsAny<SendPushRequest>(), It.IsAny<CancellationToken>()))
                  .ReturnsAsync(expected);
        using var provider = Provider(dispatcher.Object);
        var execution = new SchemataJobExecution();
        var context = Context(new("ordered", new RecipientTarget("user-3")), execution);

        await new PushDispatchJob(provider).ExecuteAsync(context, CancellationToken.None);

        var output = JsonSerializer.Deserialize<List<TransportResult>>(execution.Output!, SchemataJson.Default);
        Assert.NotNull(output);
        Assert.Equal(expected, output);
    }

    [Fact]
    public async Task Missing_ArgsJson_Throws_Before_Dispatch() {
        var dispatcher = new Mock<IRequestDispatcher>(MockBehavior.Strict);
        using var provider = Provider(dispatcher.Object);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new PushDispatchJob(provider).ExecuteAsync(new(), CancellationToken.None));

        Assert.Contains("missing its dispatch context", exception.Message);
        dispatcher.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Empty_ArgsJson_Throws_JsonException_Before_Dispatch() {
        var dispatcher = new Mock<IRequestDispatcher>(MockBehavior.Strict);
        using var provider = Provider(dispatcher.Object);

        await Assert.ThrowsAsync<JsonException>(() =>
            new PushDispatchJob(provider).ExecuteAsync(
                new() { ArgsJson = string.Empty }, CancellationToken.None));

        dispatcher.VerifyNoOtherCalls();
    }

    private static JobContext Context(
        PushContext                context,
        SchemataJobExecution? execution = null
    ) {
        return new() {
            ArgsJson  = JsonSerializer.Serialize(context, SchemataJson.Default),
            Execution = execution ?? new SchemataJobExecution(),
        };
    }

    private static ServiceProvider Provider(IRequestDispatcher dispatcher) {
        return new ServiceCollection().AddSingleton(dispatcher).BuildServiceProvider();
    }
}
