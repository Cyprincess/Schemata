using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Google.Protobuf;
using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using ProtoBuf.Grpc.Client;
using ProtoBuf.Grpc.Configuration;
using Schemata.Push.Grpc.Integration.Tests.Fixtures;
using Schemata.Push.Skeleton;
using Schemata.Transport.Grpc;
using Xunit;

namespace Schemata.Push.Grpc.Integration.Tests;

[Trait("Layer", "Integration")]
public sealed class PushWireShould
{
    private sealed class Factory : WebApplicationFactory<Program>
    {
        public List<GrpcChannel> Channels { get; } = new();
        protected override void ConfigureWebHost(IWebHostBuilder builder) => builder.UseEnvironment("Testing").UseContentRoot(AppContext.BaseDirectory);
        protected override void Dispose(bool disposing) {
            if (disposing) foreach (var channel in Channels) channel.Dispose();
            base.Dispose(disposing);
        }
    }

    private static HttpClient Http(Factory factory, string owner = "users/operator", string permissions = "push.send") {
        var http = factory.CreateClient();
        http.DefaultRequestHeaders.Add("X-Owner", owner);
        http.DefaultRequestHeaders.Add("X-Permissions", permissions);
        return http;
    }

    private static IPushControlService Grpc(Factory factory, HttpClient http) {
        var channel = GrpcChannel.ForAddress(http.BaseAddress!, new() { HttpClient = http });
        factory.Channels.Add(channel);
        return channel.CreateGrpcService<IPushControlService>(ClientFactory.Create(BinderConfiguration.Create([
            ProtoBufMarshallerFactory.Create(factory.Services.GetRequiredService<PushGrpcModel>().Model)])));
    }

    private static StringContent Json(string text) => new(text, Encoding.UTF8, "application/json");
    private static RecordingTransport Receiver(Factory factory) => factory.Services.GetServices<IPushTransport>().OfType<RecordingTransport>().Single(t => t.Name == "ok");

    [Theory]
    [InlineData("{\"title\":\"outer\",\"data\":{\"title\":\"inner\",\"nested\":[1,true,null,{\"ValueKey\":2.5}]}}")]
    [InlineData("[1,2.5,null,\"text\"]")]
    [InlineData("42.125")]
    [InlineData("null")]
    public async Task Preserve_Arbitrary_Json_Through_Both_Bindings(string payload) {
        using var factory = new Factory();
        using var http = Http(factory);
        var response = await http.PostAsync("/v1/push/subscriptions:send", Json("{\"message\":" + payload + "}"));
        response.EnsureSuccessStatusCode();
        var grpc = await Grpc(factory, http).SendAsync(new() { MessageJson = payload });
        var deliveries = Receiver(factory).Deliveries.ToArray();
        Assert.Equal(2, deliveries.Length);
        Assert.Equal(payload, Assert.IsType<JsonElement>(deliveries[0].Message).GetRawText());
        Assert.Equal(payload, Assert.IsType<JsonElement>(deliveries[1].Message).GetRawText());
        Assert.All(deliveries, d => Assert.IsType<BroadcastTarget>(d.Target));
        Assert.All(deliveries, d => Assert.Equal(PushPriority.Normal, d.Options.Priority));
        Assert.Equal(TransportStatus.Sent, grpc.Outcomes.Single(o => o.Transport == "ok").Status);
        Assert.Equal(TransportStatus.Failed, grpc.Outcomes.Single(o => o.Transport == "later").Status);
    }

    public static IEnumerable<object[]> Durations() {
        foreach (var ticks in new[] { 0L, 1L, -1L, 12345678L, -12345678L, long.MinValue, long.MaxValue }) yield return [ticks];
    }

    [Theory]
    [MemberData(nameof(Durations))]
    public async Task Preserve_Exact_Signed_Duration_And_Explicit_Low(long ticks) {
        using var factory = new Factory();
        using var http = Http(factory);
        var duration = TimeSpan.FromTicks(ticks);
        var response = await http.PostAsync("/v1/push/subscriptions:send", Json(JsonSerializer.Serialize(new {
            message = new { value = 1 }, options = new { priority = "low", time_to_live = duration, collapse_key = "collapse", dedup_id = "dedup" },
        })));
        response.EnsureSuccessStatusCode();
        var command = new SendPushCommand { MessageJson = "{\"value\":1}", Options = new() { Priority = PushPriority.Low, TimeToLive = duration, CollapseKey = "collapse", DedupId = "dedup" } };
        using var stream = new MemoryStream();
        var model = factory.Services.GetRequiredService<PushGrpcModel>().Model;
        model.Serialize(stream, command);
        stream.Position = 0;
        var roundTrip = model.Deserialize<SendPushCommand>(stream);
        Assert.Equal(duration, roundTrip.Options!.TimeToLive);
        Assert.Equal(PushPriority.Low, roundTrip.Options.Priority);
        await Grpc(factory, http).SendAsync(roundTrip);
        var deliveries = Receiver(factory).Deliveries.ToArray();
        Assert.Equal(2, deliveries.Length);
        Assert.All(deliveries, d => {
            Assert.Equal(duration, d.Options.TimeToLive);
            Assert.Equal(PushPriority.Low, d.Options.Priority);
            Assert.Equal("collapse", d.Options.CollapseKey);
            Assert.Equal("dedup", d.Options.DedupId);
        });
        var descriptor = factory.Services.GetServices<IGrpcServiceDescriptorContributor>()
            .SelectMany(contributor => contributor.GetServiceDescriptors(factory.Services))
            .Single(service => service.FullName == PushGrpcModel.ServiceName);
        var send = descriptor.Methods.Single(method => method.Name == "Send");
        var options = send.InputType.FindFieldByName("Options").MessageType;
        Assert.Equal("google.protobuf.Duration", options.FindFieldByName("TimeToLive").MessageType.FullName);
        Assert.True(options.FindFieldByName("Priority").HasPresence);
    }

    [Fact]
    public async Task Default_An_Empty_Options_Message_To_Normal() {
        using var factory = new Factory();
        using var http = Http(factory);
        (await http.PostAsync("/v1/push/subscriptions:send", Json("{\"message\":null,\"options\":{}}"))).EnsureSuccessStatusCode();
        await Grpc(factory, http).SendAsync(new() { MessageJson = "null", Options = new() });
        var inputs = Receiver(factory).Deliveries.ToArray();
        Assert.Equal(2, inputs.Length);
        Assert.All(inputs, d => Assert.Equal(PushPriority.Normal, d.Options.Priority));
    }

    [Fact]
    public async Task Reject_Undefined_Priority_Without_Sending() {
        using var factory = new Factory();
        using var http = Http(factory);
        Assert.Equal(HttpStatusCode.BadRequest, (await http.PostAsync("/v1/push/subscriptions:send", Json("{\"message\":null,\"options\":{\"priority\":999}}"))).StatusCode);
        var error = await Assert.ThrowsAsync<RpcException>(() => Grpc(factory, http).SendAsync(new() { MessageJson = "null", Options = new() { Priority = (PushPriority)999 } }).AsTask());
        Assert.Equal(StatusCode.InvalidArgument, error.StatusCode);
        Assert.Empty(Receiver(factory).Deliveries);
    }

    [Fact]
    public async Task Reject_Out_Of_Range_Http_Duration_Without_Sending() {
        using var factory = new Factory();
        using var http = Http(factory);
        Assert.Equal(HttpStatusCode.BadRequest, (await http.PostAsync("/v1/push/subscriptions:send", Json("{\"message\":null,\"options\":{\"time_to_live\":\"10675199.02:48:05.4775808\"}}"))).StatusCode);
        Assert.Empty(Receiver(factory).Deliveries);
    }

    [Theory]
    [InlineData("{\"kind\":\"broadcast\"}", PushTargetKind.Broadcast)]
    [InlineData("{\"kind\":\"topic\",\"topic\":\"news\"}", PushTargetKind.Topic)]
    [InlineData("{\"kind\":\"channel\",\"channel\":\"updates\"}", PushTargetKind.Channel)]
    [InlineData("{\"kind\":\"recipient\",\"subject\":\"users/alice\"}", PushTargetKind.Recipient)]
    [InlineData("{\"kind\":\"custom\",\"custom_kind\":\"device\",\"params\":{\"key\":\"value\"}}", PushTargetKind.Custom)]
    public async Task Preserve_Selected_Target_And_Metadata(string target, PushTargetKind kind) {
        using var factory = new Factory();
        using var http = Http(factory);
        (await http.PostAsync("/v1/push/subscriptions:send", Json("{\"message\":null,\"target\":" + target + ",\"metadata\":{\"key\":\"value\"}}"))).EnsureSuccessStatusCode();
        await Grpc(factory, http).SendAsync(new() {
            MessageJson = "null", TargetKind = kind, Topic = "news", Channel = "updates", Subject = "users/alice", CustomKind = "device",
            CustomParams = { ["key"] = "value" }, Metadata = { ["key"] = "value" },
        });
        var deliveries = Receiver(factory).Deliveries.ToArray();
        Assert.Equal(2, deliveries.Length);
        Assert.Equal(deliveries[0].Target.GetType(), deliveries[1].Target.GetType());
        if (kind == PushTargetKind.Custom) {
            var first = Assert.IsType<CustomTarget>(deliveries[0].Target);
            var second = Assert.IsType<CustomTarget>(deliveries[1].Target);
            Assert.Equal("device", first.CustomKind);
            Assert.Equal(first.CustomKind, second.CustomKind);
            Assert.Equal("value", first.Params["key"]);
            Assert.Equal(first.Params["key"], second.Params["key"]);
        } else {
            Assert.Equal(deliveries[0].Target, deliveries[1].Target);
        }
        Assert.All(deliveries, d => Assert.Equal("value", d.Metadata["key"]));
    }

    [Theory]
    [InlineData("{}", "", "")]
    [InlineData("{\"provider\":\"fcm\"}", "fcm", "")]
    [InlineData("{\"provider_key\":\"address\"}", "", "address")]
    public async Task Reject_Required_Subscription_Fields_Through_The_Shared_Control_Boundary(string payload, string provider, string key) {
        using var factory = new Factory();
        using var http = Http(factory, permissions: "push.subscriptions.create,push.subscriptions.get");
        Assert.Equal(HttpStatusCode.BadRequest, (await http.PostAsync("/v1/push/subscriptions", Json(payload))).StatusCode);
        var grpc = Grpc(factory, http);
        var error = await Assert.ThrowsAsync<RpcException>(() => grpc.CreateAsync(new() { Provider = provider, ProviderKey = key }).AsTask());
        Assert.Equal(StatusCode.InvalidArgument, error.StatusCode);
        Assert.Empty((await grpc.ListAsync(new())).Subscriptions);
    }

    [Fact]
    public async Task Deny_Send_Without_Action_Permission_In_Both_Transports() {
        using var factory = new Factory();
        using var http = Http(factory, permissions: "push.subscriptions.get");
        Assert.Equal(HttpStatusCode.Forbidden, (await http.PostAsync("/v1/push/subscriptions:send", Json("{\"message\":null}"))).StatusCode);
        var error = await Assert.ThrowsAsync<RpcException>(() => Grpc(factory, http).SendAsync(new() { MessageJson = "null" }).AsTask());
        Assert.Equal(StatusCode.PermissionDenied, error.StatusCode);
        Assert.Empty(Receiver(factory).Deliveries);
    }

    [Theory]
    [InlineData("{\"kind\":\"unknown\"}", 777)]
    [InlineData("{\"kind\":\"topic\",\"topic\":\"\"}", 2)]
    [InlineData("{\"kind\":\"channel\",\"channel\":\"\"}", 3)]
    [InlineData("{\"kind\":\"recipient\",\"subject\":\"\"}", 4)]
    [InlineData("{\"kind\":\"custom\",\"custom_kind\":\"\"}", 5)]
    public async Task Reject_Invalid_Targets_Without_Sending(string target, int kind) {
        using var factory = new Factory();
        using var http = Http(factory);
        var response = await http.PostAsync("/v1/push/subscriptions:send", Json("{\"message\":null,\"target\":" + target + "}"));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = await Assert.ThrowsAsync<RpcException>(() => Grpc(factory, http).SendAsync(new() { MessageJson = "null", TargetKind = (PushTargetKind)kind }).AsTask());
        Assert.Equal(StatusCode.InvalidArgument, error.StatusCode);
        Assert.Empty(Receiver(factory).Deliveries);
    }

    [Fact]
    public async Task Reject_Missing_And_Malformed_Messages_Without_Sending() {
        using var factory = new Factory();
        using var http = Http(factory);
        Assert.Equal(HttpStatusCode.BadRequest, (await http.PostAsync("/v1/push/subscriptions:send", Json("{}"))).StatusCode);
        var grpc = Grpc(factory, http);
        foreach (var payload in new string?[] { null, "{broken" }) {
            var error = await Assert.ThrowsAsync<RpcException>(() => grpc.SendAsync(new() { MessageJson = payload }).AsTask());
            Assert.Equal(StatusCode.InvalidArgument, error.StatusCode);
            Assert.Contains(error.Trailers, t => t.Key == "grpc-status-details-bin");
        }
        Assert.Empty(Receiver(factory).Deliveries);
    }

    [Theory]
    [InlineData(0L, 1)]
    [InlineData(0L, 1000000000)]
    [InlineData(1L, -100)]
    [InlineData(-1L, 100)]
    [InlineData(long.MaxValue, 0)]
    [InlineData(922337203685L, 477580800)]
    [InlineData(-922337203685L, -477580900)]
    public async Task Reject_Lossy_Or_Malformed_Duration_Before_Delivery(long seconds, int nanos) {
        using var factory = new Factory();
        using var http = Http(factory);
        using var channel = GrpcChannel.ForAddress(http.BaseAddress!, new() { HttpClient = http });
        var model = factory.Services.GetRequiredService<PushGrpcModel>();
        var method = new Method<byte[], SendPushResult>(MethodType.Unary, PushGrpcModel.ServiceName, "Send", Marshallers.Create<byte[]>(b => b, b => b), model.Marshaller<SendPushResult>());
        var error = await Assert.ThrowsAsync<RpcException>(() => channel.CreateCallInvoker().AsyncUnaryCall(method, null, new(), DurationBytes(seconds, nanos)).ResponseAsync);
        Assert.Equal(StatusCode.InvalidArgument, error.StatusCode);
        Assert.Contains(error.Trailers, t => t.Key == "grpc-status-details-bin");
        Assert.Empty(Receiver(factory).Deliveries);
    }

    [Theory]
    [InlineData("0A046E756C6C42081202080112021064", 10000001L)]
    [InlineData("0A046E756C6C420A12020801120408001064", 1L)]
    [InlineData("0A046E756C6C420C120508AC021001120310C801", 3000000002L)]
    [InlineData("0A046E756C6C42081202100112021064", 1L)]
    [InlineData("0A046E756C6C4206120408011064420412020800", 1L)]
    [InlineData("0A046E756C6C420412021001420412021064", 1L)]
    [InlineData("0A046E756C6C421312020801120D100008FFFFFFFFFFFFFFFFFF01", -10000000L)]
    [InlineData("0A046E756C6C42081204080110641200", 10000001L)]
    [InlineData("0A046E756C6C420412020801420412021064", 10000001L)]
    public async Task Merge_Repeated_Duration_Fields_Before_Delivery(string hex, long expectedTicks) {
        using var factory = new Factory();
        using var http = Http(factory);
        using var channel = GrpcChannel.ForAddress(http.BaseAddress!, new() { HttpClient = http });
        var model = factory.Services.GetRequiredService<PushGrpcModel>();
        var method = new Method<byte[], SendPushResult>(MethodType.Unary, PushGrpcModel.ServiceName, "Send", Marshallers.Create<byte[]>(b => b, b => b), model.Marshaller<SendPushResult>());
        var result = await channel.CreateCallInvoker().AsyncUnaryCall(method, null, new(), Convert.FromHexString(hex)).ResponseAsync;
        Assert.Equal(TransportStatus.Sent, result.Outcomes.Single(o => o.Transport == "ok").Status);
        var delivery = Assert.Single(Receiver(factory).Deliveries);
        Assert.Equal(expectedTicks, delivery.Options.TimeToLive!.Value.Ticks);
        Assert.Equal(JsonValueKind.Null, Assert.IsType<JsonElement>(delivery.Message).ValueKind);
    }

    [Fact]
    public async Task Reject_Invalid_Final_Merged_Duration_Without_Delivery() {
        using var factory = new Factory();
        using var http = Http(factory);
        using var channel = GrpcChannel.ForAddress(http.BaseAddress!, new() { HttpClient = http });
        var model = factory.Services.GetRequiredService<PushGrpcModel>();
        var method = new Method<byte[], SendPushResult>(MethodType.Unary, PushGrpcModel.ServiceName, "Send", Marshallers.Create<byte[]>(b => b, b => b), model.Marshaller<SendPushResult>());
        var error = await Assert.ThrowsAsync<RpcException>(() => channel.CreateCallInvoker().AsyncUnaryCall(method, null, new(), Convert.FromHexString("0A046E756C6C42081202080112021001")).ResponseAsync);
        Assert.Equal(StatusCode.InvalidArgument, error.StatusCode);
        Assert.Contains(error.Trailers, t => t.Key == "grpc-status-details-bin");
        Assert.Empty(Receiver(factory).Deliveries);
    }

    private static byte[] DurationBytes(long seconds, int nanos) {
        using var duration = new MemoryStream();
        using (var output = new CodedOutputStream(duration, true)) {
            output.WriteTag(1, WireFormat.WireType.Varint); output.WriteInt64(seconds);
            output.WriteTag(2, WireFormat.WireType.Varint); output.WriteInt32(nanos);
        }
        using var options = new MemoryStream();
        using (var output = new CodedOutputStream(options, true)) {
            output.WriteTag(2, WireFormat.WireType.LengthDelimited); output.WriteBytes(ByteString.CopyFrom(duration.ToArray()));
        }
        using var request = new MemoryStream();
        using (var output = new CodedOutputStream(request, true)) {
            output.WriteTag(1, WireFormat.WireType.LengthDelimited); output.WriteString("null");
            output.WriteTag(8, WireFormat.WireType.LengthDelimited); output.WriteBytes(ByteString.CopyFrom(options.ToArray()));
        }
        return request.ToArray();
    }

    [Fact]
    public async Task Share_Subscription_State_And_Owner_Isolation_Across_Transports() {
        using var factory = new Factory();
        const string permissions = "push.subscriptions.create,push.subscriptions.get,push.subscriptions.delete";
        using var alice = Http(factory, "users/alice", permissions);
        using var bob = Http(factory, "users/bob", permissions);
        var response = await alice.PostAsync("/v1/push/subscriptions", Json("{\"provider\":\"fcm\",\"provider_key\":\"secret\",\"metadata\":{\"credential\":\"hidden\"}}"));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var text = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("secret", text); Assert.DoesNotContain("hidden", text);
        using var created = JsonDocument.Parse(text);
        var aliceGrpc = Grpc(factory, alice);
        var row = Assert.Single((await aliceGrpc.ListAsync(new())).Subscriptions);
        Assert.Equal(created.RootElement.GetProperty("uid").GetString(), row.Uid);
        Assert.Equal(created.RootElement.GetProperty("canonical_name").GetString(), row.CanonicalName);
        var bobGrpc = Grpc(factory, bob);
        Assert.Empty((await bobGrpc.ListAsync(new())).Subscriptions);
        await bobGrpc.DeleteAsync(new() { Provider = "fcm", ProviderKey = "secret" });
        Assert.Single((await aliceGrpc.ListAsync(new())).Subscriptions);
        await aliceGrpc.DeleteAsync(new() { Provider = "fcm", ProviderKey = "secret" });
        using var list = JsonDocument.Parse(await (await alice.GetAsync("/v1/push/subscriptions")).Content.ReadAsStringAsync());
        Assert.Equal(0, list.RootElement.GetProperty("subscriptions").GetArrayLength());
        var grpcCreated = await aliceGrpc.CreateAsync(new() { Provider = "fcm", ProviderKey = "second-secret", Metadata = { ["credential"] = "second-hidden" } });
        var visible = await (await alice.GetAsync("/v1/push/subscriptions?provider=fcm")).Content.ReadAsStringAsync();
        Assert.DoesNotContain("second-secret", visible);
        Assert.DoesNotContain("second-hidden", visible);
        using var projection = JsonDocument.Parse(visible);
        Assert.Equal(grpcCreated.Uid, Assert.Single(projection.RootElement.GetProperty("subscriptions").EnumerateArray()).GetProperty("uid").GetString());
        Assert.Equal(HttpStatusCode.NoContent, (await alice.SendAsync(new(HttpMethod.Delete, "/v1/push/subscriptions") {
            Content = Json("{\"provider\":\"fcm\",\"provider_key\":\"second-secret\"}"),
        })).StatusCode);
        Assert.Empty((await aliceGrpc.ListAsync(new())).Subscriptions);
    }
}
