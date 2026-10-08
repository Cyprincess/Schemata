using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Microsoft.IdentityModel.JsonWebTokens;
using Schemata.Authorization.Foundation.Authentication;
using Schemata.Authorization.Foundation.Services;
using Schemata.Scheduling.Skeleton;
using Schemata.Scheduling.Skeleton.Entities;
using Schemata.Security.Skeleton;
using Xunit;
using static Schemata.Abstractions.SchemataConstants;
using static Schemata.Authorization.Skeleton.AuthorizationConstants;

namespace Schemata.Authorization.Tests;

/// <summary>
///     Behavioral coverage for <see cref="BackChannelLogoutJob" />: the logout token is signed at
///     execution time from the recipient facts carried as job variables, and every delivery
///     failure propagates so Scheduling records the execution failed.
/// </summary>
public class BackChannelLogoutJobShould
{
    private const string Issuer = "https://as.example";

    [Fact]
    public async Task Sign_At_Execution_Time_And_Post_The_Logout_Token_Form() {
        var clock   = new FakeTimeProvider(new(2026, 9, 24, 12, 0, 0, TimeSpan.Zero));
        var handler = new StubHandler(_ => new(HttpStatusCode.OK));
        var job     = CreateJob(handler, clock);
        var context = Context(new());

        clock.Advance(TimeSpan.FromMinutes(1));
        await job.ExecuteAsync(context, CancellationToken.None);

        Assert.NotNull(handler.Request);
        Assert.Equal(HttpMethod.Post, handler.Request.Method);
        Assert.Equal("https://rp.example/logout", handler.Request.RequestUri?.ToString());
        var jwt = new JsonWebTokenHandler().ReadJsonWebToken(FormValue(handler.Body, Parameters.LogoutToken));
        Assert.Equal(TokenMediaTypes.Logout, jwt.Typ);
        Assert.Equal(Issuer, jwt.Issuer);
        Assert.Equal("client-1", jwt.Audiences.Single());
        Assert.Equal("user-1", jwt.GetClaim(IdentityClaims.Subject).Value);
        Assert.Equal("sid-1", jwt.GetClaim(Claims.SessionId).Value);
        Assert.Contains("backchannel-logout", jwt.GetClaim(Claims.Events).Value);
        Assert.False(string.IsNullOrWhiteSpace(jwt.GetClaim(Claims.JwtId).Value));
        Assert.Equal(clock.GetUtcNow().UtcDateTime + TimeSpan.FromMinutes(2), jwt.ValidTo);
        Assert.Equal("https://rp.example/logout", context.Execution?.Output);
        Assert.Equal(clock.GetUtcNow().UtcDateTime, jwt.IssuedAt);
    }

    [Fact]
    public async Task Sign_The_Logout_Token_With_The_Registered_Algorithm_When_The_Issuer_Serves_It() {
        var clock   = new FakeTimeProvider(new(2026, 9, 24, 12, 0, 0, TimeSpan.Zero));
        var handler = new StubHandler(_ => new(HttpStatusCode.OK));
        // The rsa row is the primary key (RS256); the requested algorithm must select the p-256
        // row instead, so a dropped or ignored variable would surface as RS256.
        var job     = CreateJob(handler, clock, SecurityConstants.Algorithms.Rsa, SecurityConstants.Algorithms.P256);
        var context = Context();
        context.Variables = context.Variables.ToDictionary(
            pair => pair.Key,
            pair => pair.Key == BackChannelLogoutJob.VariableKeys.SigningAlgorithm
                ? SigningAlgorithms.EcdsaSha256
                : pair.Value);

        await job.ExecuteAsync(context, CancellationToken.None);

        Assert.NotNull(handler.Request);
        var jwt = new JsonWebTokenHandler().ReadJsonWebToken(FormValue(handler.Body, Parameters.LogoutToken));
        Assert.Equal(SigningAlgorithms.EcdsaSha256, jwt.Alg);
    }

    [Fact]
    public async Task Throw_Before_Any_Http_When_The_Registered_Algorithm_Is_Not_Served() {
        var handler = new StubHandler(_ => new(HttpStatusCode.OK));
        var job     = CreateJob(handler, new(),
            SecurityConstants.Algorithms.Rsa, SecurityConstants.Algorithms.P256);
        var context = Context();
        context.Variables = context.Variables.ToDictionary(
            pair => pair.Key,
            pair => pair.Key == BackChannelLogoutJob.VariableKeys.SigningAlgorithm
                ? SigningAlgorithms.EcdsaSha512
                : pair.Value);

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(
            () => job.ExecuteAsync(context, CancellationToken.None));

        Assert.Contains(SigningAlgorithms.EcdsaSha512, failure.Message);
        Assert.Null(handler.Request);
    }

    [Fact]
    public async Task Throw_When_The_Uri_Variable_Is_Missing() {
        var handler = new StubHandler(_ => new(HttpStatusCode.OK));
        var job     = CreateJob(handler, new());
        var context = Context();
        context.Variables = context.Variables
                                   .Where(pair => pair.Key != BackChannelLogoutJob.VariableKeys.Uri)
                                   .ToDictionary(pair => pair.Key, pair => pair.Value);

        await Assert.ThrowsAsync<InvalidOperationException>(() => job.ExecuteAsync(context, CancellationToken.None));
        Assert.Null(handler.Request);
    }

    [Fact]
    public async Task Throw_When_The_Audience_Variable_Is_Missing() {
        var handler = new StubHandler(_ => new(HttpStatusCode.OK));
        var job     = CreateJob(handler, new());
        var context = Context();
        context.Variables = context.Variables
                                   .Where(pair => pair.Key != BackChannelLogoutJob.VariableKeys.Audience)
                                   .ToDictionary(pair => pair.Key, pair => pair.Value);

        await Assert.ThrowsAsync<InvalidOperationException>(() => job.ExecuteAsync(context, CancellationToken.None));
        Assert.Null(handler.Request);
    }

    [Fact]
    public async Task Throw_On_A_Non_Success_Response() {
        var handler = new StubHandler(_ => new(HttpStatusCode.BadGateway));
        var job     = CreateJob(handler, new());

        await Assert.ThrowsAsync<HttpRequestException>(() => job.ExecuteAsync(Context(), CancellationToken.None));
    }

    [Fact]
    public async Task Propagate_A_Transport_Failure() {
        var handler = new StubHandler(_ => throw new HttpRequestException("connection refused"));
        var job     = CreateJob(handler, new());

        var failure = await Assert.ThrowsAsync<HttpRequestException>(
            () => job.ExecuteAsync(Context(), CancellationToken.None));
        Assert.Equal("connection refused", failure.Message);
    }

    [Fact]
    public async Task Propagate_Cancellation() {
        var handler = new StubHandler(_ => new(HttpStatusCode.OK));
        var job     = CreateJob(handler, new());

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => job.ExecuteAsync(Context(), new(canceled: true)));
    }

    [Fact]
    public async Task Throw_When_Both_Subject_And_Sid_Are_Missing() {
        var handler = new StubHandler(_ => new(HttpStatusCode.OK));
        var job     = CreateJob(handler, new());
        var context = Context();
        context.Variables = context.Variables
                                   .Where(pair => pair.Key is not BackChannelLogoutJob.VariableKeys.Subject
                                                            and not BackChannelLogoutJob.VariableKeys.SessionId)
                                   .ToDictionary(pair => pair.Key, pair => pair.Value);

        await Assert.ThrowsAsync<InvalidOperationException>(() => job.ExecuteAsync(context, CancellationToken.None));
        Assert.Null(handler.Request);
    }

    [Fact]
    public async Task Throw_When_Both_Subject_And_Sid_Are_Blank() {
        var handler = new StubHandler(_ => new(HttpStatusCode.OK));
        var job     = CreateJob(handler, new());
        var context = Context();
        context.Variables = context.Variables.ToDictionary(
            pair => pair.Key,
            pair => pair.Key is BackChannelLogoutJob.VariableKeys.Subject
                              or BackChannelLogoutJob.VariableKeys.SessionId
                ? "   "
                : pair.Value);

        await Assert.ThrowsAsync<InvalidOperationException>(() => job.ExecuteAsync(context, CancellationToken.None));
        Assert.Null(handler.Request);
    }

    [Fact]
    public async Task Succeed_When_Subject_Is_Supplied_And_Sid_Is_Missing() {
        var handler = new StubHandler(_ => new(HttpStatusCode.OK));
        var job     = CreateJob(handler, new());
        var context = Context();
        context.Variables = context.Variables
                                   .Where(pair => pair.Key != BackChannelLogoutJob.VariableKeys.SessionId)
                                   .ToDictionary(pair => pair.Key, pair => pair.Value);

        await job.ExecuteAsync(context, CancellationToken.None);

        Assert.NotNull(handler.Request);
        var jwt = new JsonWebTokenHandler().ReadJsonWebToken(FormValue(handler.Body, Parameters.LogoutToken));
        Assert.Equal("user-1", jwt.GetClaim(IdentityClaims.Subject).Value);
        Assert.False(jwt.TryGetPayloadValue<string>(Claims.SessionId, out _));
    }

    [Fact]
    public async Task Succeed_When_Sid_Is_Supplied_And_Subject_Is_Missing() {
        var handler = new StubHandler(_ => new(HttpStatusCode.OK));
        var job     = CreateJob(handler, new());
        var context = Context();
        context.Variables = context.Variables
                                   .Where(pair => pair.Key != BackChannelLogoutJob.VariableKeys.Subject)
                                   .ToDictionary(pair => pair.Key, pair => pair.Value);

        await job.ExecuteAsync(context, CancellationToken.None);

        Assert.NotNull(handler.Request);
        var jwt = new JsonWebTokenHandler().ReadJsonWebToken(FormValue(handler.Body, Parameters.LogoutToken));
        Assert.Equal("sid-1", jwt.GetClaim(Claims.SessionId).Value);
        Assert.False(jwt.TryGetPayloadValue<string>(IdentityClaims.Subject, out _));
    }

    private static BackChannelLogoutJob CreateJob(HttpMessageHandler handler, FakeTimeProvider clock) {
        var options = new SchemataAuthorizationOptions { Issuer = Issuer };
        return new(
            new StubFactory(handler),
            TestSecurityKeys.CreateTokenService(options, time: clock),
            Options.Create(options),
            clock);
    }

    private static BackChannelLogoutJob CreateJob(HttpMessageHandler handler, FakeTimeProvider clock, params string[] algorithms) {
        var options = new SchemataAuthorizationOptions { Issuer = Issuer };
        var store   = new TestSecurityStore();
        var created = new DateTime(2026, 9, 24, 0, 0, 0, DateTimeKind.Utc);
        var offset  = 0;
        foreach (var algorithm in algorithms) {
            TestSecurityKeys.AddSigningRow(store, options.Issuer, algorithm)
                            .CreateTime = created.AddMinutes(algorithms.Length - offset++);
        }

        return new(
            new StubFactory(handler),
            TestSecurityKeys.CreateTokenService(options, store, time: clock, seed: false),
            Options.Create(options),
            clock);
    }

    private static JobContext Context(SchemataJobExecution? execution = null) {
        return new() {
            Execution = execution,
            Variables = new Dictionary<string, string?> {
                [BackChannelLogoutJob.VariableKeys.Uri]              = "https://rp.example/logout",
                [BackChannelLogoutJob.VariableKeys.Audience]         = "client-1",
                [BackChannelLogoutJob.VariableKeys.Subject]          = "user-1",
                [BackChannelLogoutJob.VariableKeys.SessionId]        = "sid-1",
                [BackChannelLogoutJob.VariableKeys.SigningAlgorithm] = null,
            },
        };
    }

    private static string FormValue(string? body, string key) {
        Assert.NotNull(body);
        var pair = Assert.Single(body.Split('&'));
        var parts = pair.Split('=', 2);
        Assert.Equal(key, parts[0]);
        return parts[1];
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }
        public string?             Body    { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken ct) {
            Request = request;
            Body    = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
            return respond(request);
        }
    }

    private sealed class StubFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) {
            return new(handler);
        }
    }
}
