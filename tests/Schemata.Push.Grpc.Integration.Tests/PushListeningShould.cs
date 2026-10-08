using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Grpc.Net.Client;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ProtoBuf.Grpc.Client;
using ProtoBuf.Grpc.Configuration;
using Schemata.Entity.EntityFrameworkCore;
using Schemata.Entity.Repository.Advisors;
using Schemata.Push.Grpc.Integration.Tests.Fixtures;
using Schemata.Push.Skeleton;
using Schemata.Push.Skeleton.Control;
using Schemata.Push.Skeleton.Entities;
using Xunit;

namespace Schemata.Push.Grpc.Integration.Tests;

[Trait("Layer", "Integration")]
public sealed class PushListeningShould
{
    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public Task Reach_The_Controller_After_Repeated_And_Reordered_Installation(bool schemata, bool transportFirst)
        => SmokeAsync(schemata, transportFirst);

    private static async Task SmokeAsync(bool schemata, bool transportFirst) {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing", ContentRootPath = AppContext.BaseDirectory });
        ListenOptions? httpListener = null, grpcListener = null;
        builder.WebHost.ConfigureKestrel(options => {
            options.Listen(IPAddress.Loopback, 0, listen => { listen.Protocols = HttpProtocols.Http1; httpListener = listen; });
            options.Listen(IPAddress.Loopback, 0, listen => { listen.Protocols = HttpProtocols.Http2; grpcListener = listen; });
        });
        var connectionString = $"Data Source=push-listening-{Guid.NewGuid():n};Mode=Memory;Cache=Shared";
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();
        builder.Services.AddAuthentication("Test").AddScheme<AuthenticationSchemeOptions, PushGrpcAuthenticationHandler>("Test", _ => { });
        builder.Services.AddAuthorization(options => {
            foreach (var policy in new[] { PushPolicies.Create, PushPolicies.List, PushPolicies.Delete, PushPolicies.Send })
                options.AddPolicy(policy, p => p.RequireAuthenticatedUser().RequireClaim("permission", policy));
        });
        builder.Services.AddDbContextFactory<PushGrpcDbContext>(options => options.UseSqlite(connectionString).ReplaceService<IModelCustomizer, SchemataModelCustomizer>());
        builder.Services.AddRepository<SchemataPushSubscription, EfCoreRepository<PushGrpcDbContext, SchemataPushSubscription>>();
        if (transportFirst) builder.Services.AddSchemataPushHttp();
        if (schemata) {
            builder.UseSchemata(schema => {
                schema.UseAuthentication((AuthenticationBuilder _) => { });
                schema.UsePush().MapHttp().MapHttp();
            });
        } else {
            builder.Services.AddSchemataPush();
            builder.Services.AddControllers();
        }
        builder.Services.AddSchemataPushHttp();
        builder.Services.AddSchemataPushGrpc();
        builder.Services.TryAddEnumerable(ServiceDescriptor.Scoped<IRepositoryAddAdvisor<SchemataPushSubscription>, AdviceAddSubscriptionName>());
        var receiver = new RecordingTransport("ok");
        builder.Services.AddSingleton<IPushTransport>(receiver);
        builder.Services.AddSingleton<IPushTransport>(new RecordingTransport("later"));
        await using var app = builder.Build();
        using (var scope = app.Services.CreateScope()) await scope.ServiceProvider.GetRequiredService<PushGrpcDbContext>().Database.EnsureCreatedAsync();
        if (!schemata) {
            app.UseSchemataExceptionHandler();
            app.UseAuthentication();
            app.UseAuthorization();
            app.MapControllers();
        }
        app.MapSchemataPushGrpc();
        await app.StartAsync();
        try {
            var httpAddress = new Uri($"http://{httpListener!.IPEndPoint}");
            var grpcAddress = new Uri($"http://{grpcListener!.IPEndPoint}");
            var addresses = new[] { httpAddress.AbsoluteUri, grpcAddress.AbsoluteUri };
            using var http = new HttpClient(new SocketsHttpHandler { UseProxy = false }) { BaseAddress = httpAddress };
            http.DefaultRequestHeaders.Add("X-Owner", "users/alice");
            http.DefaultRequestHeaders.Add("X-Permissions", string.Join(",", PushPolicies.Create, PushPolicies.List, PushPolicies.Delete, PushPolicies.Send));
            var create = await http.PostAsync("/v1/push/subscriptions", Json("{\"provider\":\"fcm\",\"provider_key\":\"local-secret\"}"));
            Assert.Equal(HttpStatusCode.Created, create.StatusCode);
            Assert.DoesNotContain("local-secret", await create.Content.ReadAsStringAsync());
            using var grpcHttp = new HttpClient(new SocketsHttpHandler { UseProxy = false });
            grpcHttp.DefaultRequestHeaders.Add("X-Owner", "users/alice");
            grpcHttp.DefaultRequestHeaders.Add("X-Permissions", string.Join(",", PushPolicies.List, PushPolicies.Send));
            using var channel = GrpcChannel.ForAddress(grpcAddress, new() { HttpClient = grpcHttp });
            var model = app.Services.GetRequiredService<PushGrpcModel>();
            var grpc = channel.CreateGrpcService<IPushControlService>(ClientFactory.Create(BinderConfiguration.Create([ProtoBufMarshallerFactory.Create(model.Model)])));
            Assert.Equal("fcm", Assert.Single((await grpc.ListAsync(new())).Subscriptions).Provider);
            const string payload = "{\"title\":\"outer\",\"data\":{\"title\":\"inner\",\"items\":[1,2.5,null]}}";
            const string target = "\"target\":{\"kind\":\"topic\",\"topic\":\"news\"}";
            var sent = await http.PostAsync("/v1/push/subscriptions:send", Json("{\"message\":" + payload + "," + target + ",\"options\":{\"priority\":\"low\",\"time_to_live\":\"-00:00:01.2345678\"}}"));
            sent.EnsureSuccessStatusCode();
            var result = await grpc.SendAsync(new() { MessageJson = payload, TargetKind = PushTargetKind.Topic, Topic = "news", Options = new() { Priority = PushPriority.Low, TimeToLive = TimeSpan.FromTicks(-12345678) } });
            var inputs = receiver.Deliveries.ToArray();
            Assert.Equal(2, inputs.Length);
            foreach (var input in inputs) {
                Assert.Equal(payload, Assert.IsType<JsonElement>(input.Message).GetRawText());
                Assert.Equal("news", Assert.IsType<TopicTarget>(input.Target).Topic);
                Assert.Equal(PushPriority.Low, input.Options.Priority);
                Assert.Equal(TimeSpan.FromTicks(-12345678), input.Options.TimeToLive);
            }
            Assert.Equal(TransportStatus.Failed, result.Outcomes.Single(o => o.Transport == "later").Status);
            Console.WriteLine(JsonSerializer.Serialize(new { smoke = "push-listening", schemata, transportFirst, addresses, payload = Assert.IsType<JsonElement>(inputs[1].Message), priority = inputs[1].Options.Priority.ToString(), ttl_ticks = inputs[1].Options.TimeToLive!.Value.Ticks, deliveries = inputs.Length, outcomes = result.Outcomes }));
        } finally {
            await app.StopAsync();
        }
    }

    private static StringContent Json(string text) => new(text, Encoding.UTF8, "application/json");
}
