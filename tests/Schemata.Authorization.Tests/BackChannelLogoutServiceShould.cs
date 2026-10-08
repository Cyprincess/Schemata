using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using Schemata.Authorization.Foundation.Services;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Authorization.Skeleton.Managers;
using Schemata.Scheduling.Skeleton;
using Schemata.Scheduling.Skeleton.Entities;
using Schemata.Security.Skeleton.Entities;
using Schemata.Security.Skeleton.Services;
using Xunit;
using static Schemata.Authorization.Skeleton.AuthorizationConstants;

namespace Schemata.Authorization.Tests;

/// <summary>
///     Behavioral coverage for <see cref="BackChannelLogoutService{TApp}" />: one unsigned
///     recipient-fact message per notified relying party, dispatched as scheduler job variables.
/// </summary>
public class BackChannelLogoutServiceShould
{
    [Fact]
    public async Task Carry_Recipient_Facts_Without_Signing() {
        var service = Create(App());

        var snapshot = await service.PrepareAsync("user-1", "sid-1");

        var message = Assert.Single(snapshot.BackChannelMessages);
        Assert.Equal("https://rp.example/logout", message.Uri);
        Assert.Equal("client-1", message.Audience);
        Assert.Equal("user-1", message.Subject);
        Assert.Equal("sid-1", message.SessionId);
        Assert.Null(message.SigningAlgorithm);
    }

    [Fact]
    public async Task Carry_The_Registered_Signing_Algorithm_In_Recipient_Facts() {
        var app = App();
        app.IdTokenSignedResponseAlg = SigningAlgorithms.EcdsaSha384;
        var service = Create(app);

        var snapshot = await service.PrepareAsync("user-1", "sid-1");

        var message = Assert.Single(snapshot.BackChannelMessages);
        Assert.Equal(SigningAlgorithms.EcdsaSha384, message.SigningAlgorithm);
    }

    [Fact]
    public async Task Carry_The_Registered_Signing_Algorithm_Into_The_Job_Variables() {
        JobContext? triggered = null;
        var scheduler = new Mock<IScheduler>();
        scheduler.Setup(s => s.TriggerAsync<BackChannelLogoutJob>(
                     It.IsAny<JobContext>(), It.IsAny<CancellationToken>()))
                 .Callback<JobContext, CancellationToken>((context, _) => triggered = context)
                 .ReturnsAsync(new SchemataJobExecution());
        var services = new Mock<IServiceProvider>();
        services.Setup(s => s.GetService(typeof(IScheduler))).Returns(scheduler.Object);
        var app = App();
        app.IdTokenSignedResponseAlg = SigningAlgorithms.EcdsaSha384;
        var service = Create(services.Object, app);

        var snapshot = await service.PrepareAsync("user-1", "sid-1");
        await service.DispatchAsync(snapshot, CancellationToken.None);

        Assert.NotNull(triggered);
        Assert.Equal(SigningAlgorithms.EcdsaSha384,
            triggered.Variables[BackChannelLogoutJob.VariableKeys.SigningAlgorithm]);
    }

    [Fact]
    public async Task Prepare_One_Message_Per_Notified_Relying_Party() {
        var service = Create(App("client-1"), App("client-2"));

        var snapshot = await service.PrepareAsync("user-1", "sid-1");

        Assert.Equal(2, snapshot.BackChannelMessages.Count);
        var audiences = snapshot.BackChannelMessages
                                .Select(message => Assert.IsType<string>(message.Audience))
                                .ToArray();
        Assert.Equal(["client-1", "client-2"], audiences);
    }

    [Fact]
    public async Task Dispatch_The_Recipient_Facts_As_Job_Variables() {
        JobContext? triggered = null;
        var scheduler = new Mock<IScheduler>();
        scheduler.Setup(s => s.TriggerAsync<BackChannelLogoutJob>(
                     It.IsAny<JobContext>(), It.IsAny<CancellationToken>()))
                 .Callback<JobContext, CancellationToken>((context, _) => triggered = context)
                 .ReturnsAsync(new SchemataJobExecution());
        var services = new Mock<IServiceProvider>();
        services.Setup(s => s.GetService(typeof(IScheduler))).Returns(scheduler.Object);
        var service = Create(services.Object, App());

        var snapshot = await service.PrepareAsync("user-1", "sid-1");
        await service.DispatchAsync(snapshot, CancellationToken.None);

        Assert.NotNull(triggered);
        Assert.Equal("https://rp.example/logout", triggered.Variables[BackChannelLogoutJob.VariableKeys.Uri]);
        Assert.Equal("client-1", triggered.Variables[BackChannelLogoutJob.VariableKeys.Audience]);
        Assert.Equal("user-1", triggered.Variables[BackChannelLogoutJob.VariableKeys.Subject]);
        Assert.Equal("sid-1", triggered.Variables[BackChannelLogoutJob.VariableKeys.SessionId]);
        Assert.Null(triggered.Variables[BackChannelLogoutJob.VariableKeys.SigningAlgorithm]);
    }

    private static BackChannelLogoutService<SchemataApplication> Create(
        params SchemataApplication[] applications
    ) {
        return Create(null, applications);
    }

    private static BackChannelLogoutService<SchemataApplication> Create(
        IServiceProvider? services,
        params SchemataApplication[] applications
    ) {
        var tokens = new Mock<ITokenStore<SchemataToken>>();
        // Each notified RP holds an explicit participation fact for the session.
        var participants = applications.Select(app => app.CanonicalName!).ToArray();
        tokens.Setup(m => m.ListParticipantsAsync(It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(participants);
        var apps = new Mock<IApplicationManager<SchemataApplication>>();
        apps.SetupTypedMetadata();
        apps.Setup(m => m.ListAsync(It.IsAny<Func<IQueryable<SchemataApplication>, IQueryable<SchemataApplication>>>(),
                                    It.IsAny<CancellationToken>()))
            .Returns(Enumerate(applications));
        return new(apps.Object, tokens.Object, services ?? new Mock<IServiceProvider>().Object);
    }

    private static SchemataApplication App(string clientId = "client-1") {
        return new() {
            Uid                  = Guid.NewGuid(),
            ClientId             = clientId,
            CanonicalName        = $"applications/{clientId}",
            BackChannelLogoutUri = "https://rp.example/logout",
        };
    }

    private static async IAsyncEnumerable<SchemataApplication> Enumerate(SchemataApplication[] applications) {
        foreach (var app in applications) {
            await Task.CompletedTask;
            yield return app;
        }
    }
}
