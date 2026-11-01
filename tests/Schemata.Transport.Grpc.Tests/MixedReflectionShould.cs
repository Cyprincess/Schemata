using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Google.Protobuf;
using Google.Protobuf.Reflection;
using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Resource;
using Schemata.Entity.EntityFrameworkCore;
using Schemata.Expressions.Cel;
using Schemata.Insight.Foundation.Drivers;
using Schemata.Insight.Skeleton;
using Schemata.Messaging.Skeleton;
using Schemata.Push.Skeleton;
using Schemata.Push.Skeleton.Control;
using Schemata.Push.Skeleton.Entities;
using Schemata.Transport.Grpc.Interceptors;
using Xunit;

namespace Schemata.Transport.Grpc.Tests;

[Trait("Category", "Integration")]
[Trait("Layer", "Integration")]
public sealed class MixedReflectionShould
{
    [Fact]
    public async Task Discover_And_Invoke_All_Transports_Using_Reflected_Protobuf_Shapes() {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Reflection", ContentRootPath = AppContext.BaseDirectory });
        builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, 0, listen => listen.Protocols = HttpProtocols.Http2));
        var connectionString = $"Data Source=reflection-{Guid.NewGuid():n};Mode=Memory;Cache=Shared";
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();
        var amounts = new Dictionary<string, decimal?> { { "exact", 7922816251426433759354395033.5m } };
        builder.UseSchemata(schema => {
            schema.UseMapster().Map<Entry, Entry>();
            schema.UseResource().MapGrpc().Use<Entry, Entry, Entry, Entry>(null, resource => resource.Operations = [Operations.Get]);
            schema.UseInsight(insight => insight.UseCel().AddRepositorySource<Entry, ValueRow>("values", entry => new ValueRow {
                Unsigned = ulong.MaxValue, Precise = 7922816251426433759354395033.5m,
                Empty = Array.Empty<int>(), Map = amounts,
            }).AddSourceDriver<RepositoryDriver>(RepositoryDriver.DriverName)).MapGrpc();
            schema.Services.AddDbContextFactory<ReflectionDb>(options => options.UseSqlite(connectionString).ReplaceService<IModelCustomizer, SchemataModelCustomizer>());
            schema.Services.AddRepository<Entry, EfCoreRepository<ReflectionDb, Entry>>();
            schema.Services.AddSchemataGrpcStream<StreamRequest, StreamItem>("schemata.reflection.Stream", "First");
            schema.Services.AddSchemataGrpcStream<OtherRequest, StreamItem>("schemata.reflection.Stream", "Second");
            schema.Services.AddScoped<IStreamRequestHandler<StreamRequest, StreamItem>, FirstHandler>();
            schema.Services.AddScoped<IStreamRequestHandler<OtherRequest, StreamItem>, SecondHandler>();
            schema.Services.AddSchemataStreams();
        });
        builder.Services.AddSchemataPush();
        builder.Services.AddSchemataPushGrpc();
        builder.Services.AddSchemataGrpcTransport();
        builder.Services.AddAuthorization(options => {
            foreach (var policy in new[] { PushPolicies.Create, PushPolicies.List, PushPolicies.Delete, PushPolicies.Send })
                options.AddPolicy(policy, rule => rule.RequireAuthenticatedUser());
        });
        var receiver = new Receiver();
        builder.Services.AddSingleton<IPushTransport>(receiver);
        await using var app = builder.Build();
        app.Use(async (context, next) => {
            context.User = new(new ClaimsIdentity([new Claim("sub", "users/reflection")], "Reflection"));
            await next(context);
        });
        app.UseAuthorization();
        app.MapSchemataGrpcStream<StreamRequest, StreamItem>();
        app.MapSchemataGrpcStream<OtherRequest, StreamItem>();
        app.MapSchemataPushGrpc();
        using (var scope = app.Services.CreateScope()) {
            var db = scope.ServiceProvider.GetRequiredService<ReflectionDb>();
            await db.Database.EnsureCreatedAsync();
            db.Entries.Add(new() { Uid = Guid.NewGuid(), Name = "one", CanonicalName = "entries/one", Value = 37 });
            await db.SaveChangesAsync();
        }
        await app.StartAsync();
        try {
            var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
            using var channel = GrpcChannel.ForAddress(address, new() { HttpHandler = new SocketsHttpHandler { UseProxy = false } });
            var invoker = channel.CreateCallInvoker();
            var resource = await ReflectionClient.Discover(invoker, typeof(Entry).Namespace + ".EntryService");
            var get = resource.Methods.Single(method => method.Name == "GetEntry");
            var response = await Unary(invoker, get, new() { ["name"] = "entries/one" });
            Assert.Equal(37L, ReadInteger(get.OutputType, response, "value"));
            var stream = await ReflectionClient.Discover(invoker, "schemata.reflection.Stream");
            foreach (var method in stream.Methods) {
                using var call = invoker.AsyncServerStreamingCall(ReflectionClient.Method(method), null, new(),
                    ReflectionClient.Encode(method.InputType, new Dictionary<string, object?> { ["value"] = 43 }));
                Assert.True(await call.ResponseStream.MoveNext(CancellationToken.None));
                Assert.Equal(43L, ReadInteger(method.OutputType, call.ResponseStream.Current, "value"));
                Assert.False(await call.ResponseStream.MoveNext(CancellationToken.None));
                Assert.Equal(StatusCode.OK, call.GetStatus().StatusCode);
            }
            Assert.Equal(new[] { "First", "Second" }, stream.Methods.Select(method => method.Name).OrderBy(name => name));
            var insight = await ReflectionClient.Discover(invoker, "schemata.insight.v1.InsightService");
            var query = insight.Methods.Single(method => method.Name == "Query");
            var queryBytes = await Unary(invoker, query, new() { ["Sources"] = new object?[] {
                new Dictionary<string, object?> { ["Alias"] = "v", ["Name"] = "values" },
            } });
            Assert.NotEmpty(queryBytes);
            var rowType = query.OutputType.FindFieldByName("Rows").MessageType;
            Assert.True(rowType.FindFieldByName("Fields").IsMap);
            var dynamicType = rowType.FindFieldByName("Fields").MessageType.FindFieldByName("value").MessageType;
            Assert.Equal(FieldType.Message, dynamicType.FindFieldByName("ListValue").FieldType);
            var label = dynamicType.FindFieldByName("TypeLabel");
            Assert.True(label.HasPresence);
            Assert.Equal(FieldType.Enum, label.FieldType);
            var dynamicBytes = await Unary(invoker, query, new() {
                ["Sources"] = new object?[] { new Dictionary<string, object?> { ["Alias"] = "v", ["Name"] = "values" } },
                ["Selections"] = new object?[] { new Dictionary<string, object?> {
                    ["Alias"] = "computed", ["Expression"] = new Dictionary<string, object?> { ["Source"] = "1", ["Language"] = "cel" },
                } },
            });
            var dynamicResult = ReflectionClient.Decode(query.OutputType, dynamicBytes);
            var dynamicRow = (Dictionary<string, List<object>>)Assert.Single(dynamicResult["Rows"]);
            var dynamicLeaf = (Dictionary<string, List<object>>)Assert.Single(
                ((Dictionary<string, List<object>>)Assert.Single(dynamicRow["Fields"]))["value"]);
            Assert.Equal(1L, Assert.Single(dynamicLeaf["IntValue"]));
            Assert.Equal(label.EnumType.Values.Single(value => value.Name.EndsWith("_Int64", StringComparison.Ordinal)).Number,
                Assert.Single(dynamicLeaf[label.Name]));
            var push = await ReflectionClient.Discover(invoker, "schemata.push.v1.PushControl");
            var send = push.Methods.Single(method => method.Name == "Send");
            var options = send.InputType.FindFieldByName("Options").MessageType;
            Assert.Equal("google.protobuf.Duration", options.FindFieldByName("TimeToLive").MessageType.FullName);
            var queryResult = ReflectionClient.Decode(query.OutputType, queryBytes);
            var row = (Dictionary<string, List<object>>)Assert.Single(queryResult["Rows"]);
            var fields = row["Fields"].Cast<Dictionary<string, List<object>>>().ToDictionary(
                pair => (string)Assert.Single(pair["key"]), pair => (Dictionary<string, List<object>>)Assert.Single(pair["value"]));
            Assert.Equal("18446744073709551615", Assert.Single(fields["unsigned"]["StringValue"]));
            Assert.Equal("7922816251426433759354395033.5", Assert.Single(fields["precise"]["StringValue"]));
            Assert.Empty((Dictionary<string, List<object>>)Assert.Single(fields["empty"]["ListValue"]));
            Assert.Equal(0L, Assert.Single(fields["zero"]["IntValue"]));
            Assert.Equal(false, Assert.Single(fields["false"]["BoolValue"]));
            Assert.True(dynamicType.FindFieldByName("IntValue").HasPresence);
            Assert.True(dynamicType.FindFieldByName("BoolValue").HasPresence);
            Assert.True(options.FindFieldByName("Priority").HasPresence);
            var priority = options.FindFieldByName("Priority").EnumType.Values.Single(value => value.Name.EndsWith("_Low", StringComparison.Ordinal)).Number;
            var sent = await Unary(invoker, send, new() {
                ["MessageJson"] = "{\"items\":[1,null,{\"nested\":true}]}",
                ["Options"] = new Dictionary<string, object?> {
                    ["Priority"] = priority,
                    ["TimeToLive"] = new Dictionary<string, object?> { ["seconds"] = -1L, ["nanos"] = -234567800 },
                },
            });
            Assert.Equal(-12345678L, receiver.Last!.Options.TimeToLive!.Value.Ticks);
            Assert.Equal(PushPriority.Low, receiver.Last.Options.Priority);
            Assert.NotEmpty(sent);
            Assert.Equal("{\"items\":[1,null,{\"nested\":true}]}", ((System.Text.Json.JsonElement)receiver.Last.Message).GetRawText());
            var sentResult = ReflectionClient.Decode(send.OutputType, sent);
            var outcome = (Dictionary<string, List<object>>)Assert.Single(sentResult["Outcomes"]);
            Assert.Equal("local", Assert.Single(outcome["Transport"]));
            Assert.Equal((int)TransportStatus.Sent, outcome.TryGetValue("Status", out var statuses) ? Assert.Single(statuses) : 0);
            using (var invalid = invoker.AsyncUnaryCall(ReflectionClient.Method(send), null, new(),
                ReflectionClient.Encode(send.InputType, new Dictionary<string, object?> { ["MessageJson"] = "null", ["TargetKind"] = 777 }))) {
                var failure = await Assert.ThrowsAsync<RpcException>(async () => await invalid.ResponseAsync);
                Assert.Equal(StatusCode.InvalidArgument, failure.StatusCode);
            }
            Assert.Equal(1, receiver.Count);
            var proto = await ReflectionClient.Discover(invoker, "grpc.reflection.v1alpha.ServerReflection");
            var reflected = proto.Methods.Single(method => method.Name == "ServerReflectionInfo");
            using var reflection = invoker.AsyncDuplexStreamingCall(ReflectionClient.Method(reflected), null, new());
            await reflection.RequestStream.WriteAsync(ReflectionClient.Encode(reflected.InputType, new Dictionary<string, object?> { ["list_services"] = "" }));
            await reflection.RequestStream.CompleteAsync();
            Assert.True(await reflection.ResponseStream.MoveNext(CancellationToken.None));
            Assert.NotEmpty(reflection.ResponseStream.Current);
            var reflectedResult = ReflectionClient.Decode(reflected.OutputType, reflection.ResponseStream.Current);
            var serviceList = (Dictionary<string, List<object>>)Assert.Single(reflectedResult["list_services_response"]);
            Assert.Contains(serviceList["service"].Cast<Dictionary<string, List<object>>>(), service =>
                (string)Assert.Single(service["name"]) == push.FullName);
            Assert.Equal("grpc.reflection.v1alpha.ServerReflectionRequest", reflected.InputType.FullName);
            var configured = app.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<global::Grpc.AspNetCore.Server.GrpcServiceOptions>>().Value;
            Assert.Single(configured.Interceptors, interceptor => interceptor.Type == typeof(ExceptionMappingInterceptor));
        } finally { await app.StopAsync(); }
    }

    private static async Task<byte[]> Unary(CallInvoker invoker, MethodDescriptor method, Dictionary<string, object?> values) {
        using var call = invoker.AsyncUnaryCall(ReflectionClient.Method(method), null, new(), ReflectionClient.Encode(method.InputType, values));
        return await call.ResponseAsync;
    }

    private static long ReadInteger(MessageDescriptor descriptor, byte[] bytes, string name) {
        var number = descriptor.FindFieldByName(name).FieldNumber;
        var reader = new CodedInputStream(bytes);
        uint tag;
        while ((tag = reader.ReadTag()) != 0) {
            if (WireFormat.GetTagFieldNumber(tag) == number) return reader.ReadInt64();
            reader.SkipLastField();
        }
        throw new InvalidOperationException("The reflected response field was absent.");
    }

    [CanonicalName("entries/{entry}")]
    [Schemata.Abstractions.Entities.PrimaryKey(nameof(Uid))]
    public sealed class Entry : ICanonicalName, IIdentifier {
        public Guid Uid { get; set; }
        public string? Name { get; set; }
        public string? CanonicalName { get; set; }
        public int Value { get; set; }
    }
    public sealed class ReflectionDb(DbContextOptions<ReflectionDb> options) : DbContext(options) {
        public DbSet<Entry> Entries { get; set; } = null!;
    }
    public sealed class ValueRow {
        public ulong Unsigned { get; set; }
        public decimal Precise { get; set; }
        public int[] Empty { get; set; } = [];
        public Dictionary<string, decimal?> Map { get; set; } = [];
        public int Zero { get; set; }
        public bool False { get; set; }
    }
    public sealed class StreamRequest : IStreamRequest<StreamItem> { public int Value { get; set; } }
    public sealed class OtherRequest : IStreamRequest<StreamItem> { public int Value { get; set; } }
    public sealed class StreamItem { public int Value { get; set; } }
    public sealed class FirstHandler : IStreamRequestHandler<StreamRequest, StreamItem> {
        public async IAsyncEnumerable<StreamItem> HandleAsync(StreamRequest request, StreamExecutionContext context, [EnumeratorCancellation] CancellationToken ct = default) {
            yield return new() { Value = request.Value };
            await Task.CompletedTask;
        }
    }
    public sealed class SecondHandler : IStreamRequestHandler<OtherRequest, StreamItem> {
        public async IAsyncEnumerable<StreamItem> HandleAsync(OtherRequest request, StreamExecutionContext context, [EnumeratorCancellation] CancellationToken ct = default) {
            yield return new() { Value = request.Value };
            await Task.CompletedTask;
        }
    }
    public sealed class Receiver : IPushTransport {
        public string Name => "local";
        public PushContext? Last { get; private set; }
        public int Count { get; private set; }
        public ValueTask<TransportResult> TrySendAsync(PushContext context, CancellationToken ct = default) {
            Last = context;
            Count++;
            return ValueTask.FromResult(TransportResult.Sent(Name));
        }
    }
}
