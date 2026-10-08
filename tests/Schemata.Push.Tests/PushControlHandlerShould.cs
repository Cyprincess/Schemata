using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Security.Claims;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Schemata.Abstractions.Exceptions;
using Schemata.Messaging.Skeleton;
using Schemata.Push.Foundation.Handlers;
using Schemata.Push.Skeleton;
using Schemata.Push.Skeleton.Control;
using Xunit;

namespace Schemata.Push.Tests;

[Trait("Layer", "Unit")]
public class PushControlHandlerShould
{
    [Theory]
    [InlineData(PushPolicies.Create)]
    [InlineData(PushPolicies.List)]
    [InlineData(PushPolicies.Delete)]
    [InlineData(PushPolicies.Send)]
    public async Task Denied_Policy_Precedes_Owner_And_Invalid_Input(string policy) {
        var principal = Principal();
        var authorization = new Mock<IAuthorizationService>();
        authorization.Setup(service => service.AuthorizeAsync(principal, null, policy))
                     .ReturnsAsync(AuthorizationResult.Failed());
        var owners = new Mock<IPushOwnerResolver>(MockBehavior.Strict);
        var dispatcher = new Mock<IRequestDispatcher>(MockBehavior.Strict);
        var handler = new PushControlHandler(authorization.Object, owners.Object, dispatcher.Object);

        await Assert.ThrowsAsync<PermissionDeniedException>(() => Invoke(handler, policy, principal));

        authorization.Verify(service => service.AuthorizeAsync(principal, null, policy), Times.Once);
        owners.VerifyNoOtherCalls();
        dispatcher.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(PushPolicies.Create)]
    [InlineData(PushPolicies.List)]
    [InlineData(PushPolicies.Delete)]
    public async Task Authorized_Subscription_Action_Requires_A_Resolved_Owner(string policy) {
        var principal = Principal();
        var authorization = new Mock<IAuthorizationService>();
        authorization.Setup(service => service.AuthorizeAsync(principal, null, policy))
                     .ReturnsAsync(AuthorizationResult.Success());
        var owners = new Mock<IPushOwnerResolver>();
        owners.Setup(resolver => resolver.Resolve(principal)).Returns((string?)null);
        var dispatcher = new Mock<IRequestDispatcher>(MockBehavior.Strict);
        var handler = new PushControlHandler(authorization.Object, owners.Object, dispatcher.Object);

        await Assert.ThrowsAsync<UnauthenticatedException>(() => Invoke(handler, policy, principal));

        dispatcher.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(PushPolicies.Create, "", "key")]
    [InlineData(PushPolicies.Create, "provider", " ")]
    [InlineData(PushPolicies.Delete, "", "key")]
    [InlineData(PushPolicies.Delete, "provider", " ")]
    public async Task Authorized_Mutation_Rejects_Empty_Address_Before_Dispatch(string policy, string provider, string key) {
        var principal = Principal();
        var authorization = new Mock<IAuthorizationService>();
        authorization.Setup(service => service.AuthorizeAsync(principal, null, policy))
                     .ReturnsAsync(AuthorizationResult.Success());
        var owners = new Mock<IPushOwnerResolver>();
        owners.Setup(resolver => resolver.Resolve(principal)).Returns("owners/one");
        var dispatcher = new Mock<IRequestDispatcher>(MockBehavior.Strict);
        var handler = new PushControlHandler(authorization.Object, owners.Object, dispatcher.Object);

        await Assert.ThrowsAsync<InvalidArgumentException>(() => policy == PushPolicies.Create
            ? (Task)handler.HandleAsync(new CreatePushControlRequest(principal, provider, key))
            : handler.HandleAsync(new DeletePushControlRequest(principal, provider, key)));

        dispatcher.VerifyNoOtherCalls();
    }

    private static Task Invoke(PushControlHandler handler, string policy, ClaimsPrincipal principal) => policy switch {
        PushPolicies.Create => handler.HandleAsync(new CreatePushControlRequest(principal, "", "")),
        PushPolicies.List => handler.HandleAsync(new ListPushControlRequest(principal)),
        PushPolicies.Delete => handler.HandleAsync(new DeletePushControlRequest(principal, "", "")),
        PushPolicies.Send => handler.HandleAsync(new SendPushControlRequest(principal, default, new("unknown"))),
        _ => throw new ArgumentOutOfRangeException(nameof(policy)),
    };

    internal static ClaimsPrincipal Principal() => new(new ClaimsIdentity([new Claim("sub", "owners/one")], "Test"));
}

[Trait("Layer", "Integration")]
public class PushControlDispatchShould
{
    [Theory]
    [InlineData("{\"title\":\"outer\",\"body\":\"body\",\"data\":{\"title\":\"inner\",\"nested\":[1,null,{\"flag\":true}]}}")]
    [InlineData("[1,{\"key\":\"value\"},null]")]
    [InlineData("12.345")]
    [InlineData("null")]
    public async Task Local_Control_Preserves_Json_And_Defaults_In_Actual_Fanout(string json) {
        PushContext? delivered = null;
        var transport = new Mock<IPushTransport>();
        transport.SetupGet(value => value.Name).Returns("receiver");
        transport.Setup(value => value.TrySendAsync(It.IsAny<PushContext>(), It.IsAny<CancellationToken>()))
                 .Returns((PushContext context, CancellationToken _) => {
                     delivered = context;
                     return ValueTask.FromResult(TransportResult.Sent("receiver"));
                 });
        using var provider = Services(transport.Object);
        using var scope = provider.CreateScope();
        using var document = JsonDocument.Parse(json);

        var outcomes = await scope.ServiceProvider.GetRequiredService<IRequestDispatcher>()
                                  .SendAsync<SendPushControlRequest, ImmutableArray<TransportResult>>(
                                      new(PushControlHandlerShould.Principal(), document.RootElement), CancellationToken.None);

        Assert.Equal(json, Assert.IsType<JsonElement>(delivered!.Message).GetRawText());
        Assert.IsType<BroadcastTarget>(delivered.Target);
        Assert.Equal(PushPriority.Normal, delivered.Options.Priority);
        Assert.Equal(TransportStatus.Sent, Assert.Single(outcomes).Status);
        transport.Verify(value => value.TrySendAsync(It.IsAny<PushContext>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData("unknown", null)]
    [InlineData("topic", "")]
    [InlineData("channel", " ")]
    [InlineData("recipient", null)]
    [InlineData("custom", "")]
    public async Task Invalid_Target_Produces_Zero_Deliveries(string kind, string? field) {
        var transport = new Mock<IPushTransport>(MockBehavior.Strict);
        using var provider = Services(transport.Object);
        using var scope = provider.CreateScope();
        using var document = JsonDocument.Parse("null");
        var target = new PushControlTarget(kind, field, field, field, field);

        await Assert.ThrowsAsync<InvalidArgumentException>(() => scope.ServiceProvider.GetRequiredService<IRequestDispatcher>()
            .SendAsync<SendPushControlRequest, ImmutableArray<TransportResult>>(
                new(PushControlHandlerShould.Principal(), document.RootElement, target)));

        transport.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Missing_Message_Produces_Zero_Deliveries() {
        var transport = new Mock<IPushTransport>(MockBehavior.Strict);
        using var provider = Services(transport.Object);
        using var scope = provider.CreateScope();

        await Assert.ThrowsAsync<InvalidArgumentException>(() => scope.ServiceProvider.GetRequiredService<IRequestDispatcher>()
            .SendAsync<SendPushControlRequest, ImmutableArray<TransportResult>>(new(PushControlHandlerShould.Principal(), default)));

        transport.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Unknown_Priority_Produces_Zero_Deliveries() {
        var transport = new Mock<IPushTransport>(MockBehavior.Strict);
        using var provider = Services(transport.Object);
        using var scope = provider.CreateScope();
        using var document = JsonDocument.Parse("null");

        await Assert.ThrowsAsync<InvalidArgumentException>(() => scope.ServiceProvider.GetRequiredService<IRequestDispatcher>()
            .SendAsync<SendPushControlRequest, ImmutableArray<TransportResult>>(new(
                PushControlHandlerShould.Principal(), document.RootElement, Options: new((PushPriority)999))));

        transport.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(0L)]
    [InlineData(1L)]
    [InlineData(-123456789L)]
    [InlineData(long.MinValue)]
    [InlineData(long.MaxValue)]
    public async Task Explicit_Low_And_Tick_Range_Reach_Receiver_Unchanged(long ticks) {
        PushOptions? observed = null;
        var transport = new Mock<IPushTransport>();
        transport.SetupGet(value => value.Name).Returns("receiver");
        transport.Setup(value => value.TrySendAsync(It.IsAny<PushContext>(), It.IsAny<CancellationToken>()))
                 .Returns((PushContext context, CancellationToken _) => {
                     observed = context.Options;
                     return ValueTask.FromResult(TransportResult.Sent("receiver"));
                 });
        using var provider = Services(transport.Object);
        using var scope = provider.CreateScope();
        using var document = JsonDocument.Parse("null");

        await scope.ServiceProvider.GetRequiredService<IRequestDispatcher>()
                   .SendAsync<SendPushControlRequest, ImmutableArray<TransportResult>>(new(
                       PushControlHandlerShould.Principal(), document.RootElement,
                       Options: new(PushPriority.Low, TimeSpan.FromTicks(ticks), "collapse", "dedup")));

        Assert.Equal(PushPriority.Low, observed!.Priority);
        Assert.Equal(ticks, observed.TimeToLive!.Value.Ticks);
        Assert.Equal("collapse", observed.CollapseKey);
        Assert.Equal("dedup", observed.DedupId);
    }

    [Fact]
    public async Task Present_Options_With_Missing_Priority_Use_Normal_And_Preserve_Lifetime() {
        PushOptions? observed = null;
        var transport = new Mock<IPushTransport>();
        transport.SetupGet(value => value.Name).Returns("receiver");
        transport.Setup(value => value.TrySendAsync(It.IsAny<PushContext>(), It.IsAny<CancellationToken>()))
                 .Returns((PushContext context, CancellationToken _) => {
                     observed = context.Options;
                     return ValueTask.FromResult(TransportResult.Sent("receiver"));
                 });
        using var provider = Services(transport.Object);
        using var scope = provider.CreateScope();
        using var document = JsonDocument.Parse("null");

        await scope.ServiceProvider.GetRequiredService<IRequestDispatcher>()
                   .SendAsync<SendPushControlRequest, ImmutableArray<TransportResult>>(new(
                       PushControlHandlerShould.Principal(), document.RootElement,
                       Options: new(TimeToLive: TimeSpan.FromTicks(123))));

        Assert.Equal(PushPriority.Normal, observed!.Priority);
        Assert.Equal(123, observed.TimeToLive!.Value.Ticks);
    }

    [Fact]
    public async Task Control_Send_Reports_All_Outcomes_After_A_Transport_Fails() {
        var success = new Mock<IPushTransport>();
        success.SetupGet(value => value.Name).Returns("receiver");
        success.Setup(value => value.TrySendAsync(It.IsAny<PushContext>(), It.IsAny<CancellationToken>()))
               .Returns((PushContext context, CancellationToken _) => ValueTask.FromResult(
                   TransportResult.Sent("receiver", provider: context.Metadata["reference"])));
        var failure = new Mock<IPushTransport>();
        failure.SetupGet(value => value.Name).Returns("failed");
        failure.Setup(value => value.TrySendAsync(It.IsAny<PushContext>(), It.IsAny<CancellationToken>()))
               .Throws(new InvalidOperationException("delivery failed"));
        var skipped = new Mock<IPushTransport>();
        skipped.SetupGet(value => value.Name).Returns("skipped");
        skipped.Setup(value => value.TrySendAsync(It.IsAny<PushContext>(), It.IsAny<CancellationToken>()))
               .Returns(ValueTask.FromResult(TransportResult.Skipped("skipped")));
        using var provider = Services(success.Object, failure.Object, skipped.Object);
        using var scope = provider.CreateScope();
        using var document = JsonDocument.Parse("null");

        var outcomes = await scope.ServiceProvider.GetRequiredService<IRequestDispatcher>()
                                  .SendAsync<SendPushControlRequest, ImmutableArray<TransportResult>>(new(
                                      PushControlHandlerShould.Principal(), document.RootElement,
                                      Metadata: new Dictionary<string, string?> { ["reference"] = "computed-reference" }));

        Assert.Equal(3, outcomes.Length);
        Assert.Contains(outcomes, result => result.Transport == "receiver" && result.Status == TransportStatus.Sent
                                            && result.Provider == "computed-reference");
        Assert.Contains(outcomes, result => result.Transport == "failed" && result.Status == TransportStatus.Failed
                                            && result.Error == "delivery failed");
        Assert.Contains(outcomes, result => result.Transport == "skipped" && result.Status == TransportStatus.Skipped);
        success.Verify(value => value.TrySendAsync(It.IsAny<PushContext>(), It.IsAny<CancellationToken>()), Times.Once);
        failure.Verify(value => value.TrySendAsync(It.IsAny<PushContext>(), It.IsAny<CancellationToken>()), Times.Once);
        skipped.Verify(value => value.TrySendAsync(It.IsAny<PushContext>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    private static ServiceProvider Services(params IPushTransport[] transports) {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAuthorization(options => options.AddPolicy(PushPolicies.Send, builder => builder.RequireAuthenticatedUser()));
        foreach (var transport in transports) services.AddSingleton(transport);
        services.AddSchemataPush();
        return services.BuildServiceProvider();
    }
}
