using System;
using System.IO;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Moq;
using Schemata.Messaging.Skeleton;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Flow.Integration.Tests.Resource.Fixtures;
using Xunit;

namespace Schemata.Flow.Integration.Tests.Resource;

[Trait("Category", "Integration")]
public sealed class StreamHttpTransportShould : IClassFixture<WebAppFactory>
{
    private readonly WebAppFactory _factory;
    public StreamHttpTransportShould(WebAppFactory factory) { _factory = factory; }

    private static StringContent Body(string id, bool failBefore = false, bool failAfter = false) => new(
        $$"""{"id":"{{id}}","fail_before":{{failBefore.ToString().ToLowerInvariant()}},"fail_after":{{failAfter.ToString().ToLowerInvariant()}}}""",
        System.Text.Encoding.UTF8, "application/json");


    [Fact]
    public async Task Deliver_First_Item_Before_Producer_Completes_As_Single_Line_Frames() {
        var state = _factory.Services.GetRequiredService<StreamProbeState>();
        var id = Guid.NewGuid().ToString("n");
        using var response = await _factory.CreateClient().SendAsync(new(HttpMethod.Post, "/v1/streams:probe") {
            Content = Body(id),
        }, HttpCompletionOption.ResponseHeadersRead);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/x-ndjson", response.Content.Headers.ContentType?.MediaType);
        await using var stream = await response.Content.ReadAsStreamAsync();
        using var reader = new System.IO.StreamReader(stream);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var first = await reader.ReadLineAsync(cancellation.Token);
        var item = JsonSerializer.Deserialize<JsonElement>(first!);
        Assert.Equal(1, item.GetProperty("item").GetProperty("value").GetInt32());
        Assert.False(state.End(id).Task.IsCompleted);
        state.Gate(id).TrySetResult();
        var second = await reader.ReadLineAsync(cancellation.Token);
        Assert.Equal(2, JsonSerializer.Deserialize<JsonElement>(second!).GetProperty("item").GetProperty("value").GetInt32());
        var complete = await reader.ReadLineAsync(cancellation.Token);
        Assert.True(JsonSerializer.Deserialize<JsonElement>(complete!).TryGetProperty("complete", out var done) && done.GetBoolean());
        Assert.Null(await reader.ReadLineAsync(cancellation.Token));
        await state.ScopeEnd(id).Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(1, state.ScopeDisposals[id]);
    }

    [Fact]
    public async Task Reject_Before_Output_With_Structured_Error_Status() {
        using var client = _factory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(15);
        var id = Guid.NewGuid().ToString("n");
        var state = _factory.Services.GetRequiredService<StreamProbeState>();
        var response = await client.PostAsync("/v1/streams:probe", Body(id, failBefore: true));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.NotEqual("application/x-ndjson", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("INVALID_ARGUMENT", body.RootElement.GetProperty("error").GetProperty("status").GetString());
        Assert.False(body.RootElement.TryGetProperty("item", out _));
        await state.ScopeEnd(id).Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(1, state.ScopeDisposals[id]);
    }

    [Fact]
    public async Task Signal_Terminal_Error_After_Output_Started() {
        var state = _factory.Services.GetRequiredService<StreamProbeState>();
        var id = Guid.NewGuid().ToString("n");
        using var response = await _factory.CreateClient().SendAsync(new(HttpMethod.Post, "/v1/streams:probe") {
            Content = Body(id, failAfter: true),
        }, HttpCompletionOption.ResponseHeadersRead);
        await using var stream = await response.Content.ReadAsStreamAsync();
        using var reader = new System.IO.StreamReader(stream);
        var first = await reader.ReadLineAsync();
        state.Gate(id).TrySetResult();
        var terminal = await reader.ReadLineAsync();
        Assert.Equal(1, JsonSerializer.Deserialize<JsonElement>(first!).GetProperty("item").GetProperty("value").GetInt32());
        var error = JsonSerializer.Deserialize<JsonElement>(terminal!).GetProperty("error");
        Assert.Equal("INVALID_ARGUMENT", error.GetProperty("status").GetString());
        Assert.False(JsonSerializer.Deserialize<JsonElement>(terminal!).TryGetProperty("complete", out _));
        Assert.Null(await reader.ReadLineAsync());
        await state.ScopeEnd(id).Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(1, state.ScopeDisposals[id]);
    }

    [Fact]
    public async Task Dispose_Enumeration_When_Client_Disconnects() {
        var state = _factory.Services.GetRequiredService<StreamProbeState>();
        var id = Guid.NewGuid().ToString("n");
        using var client = _factory.CreateClient();
        using var response = await client.SendAsync(new(HttpMethod.Post, "/v1/streams:probe") {
            Content = Body(id),
        }, HttpCompletionOption.ResponseHeadersRead);
        await using var stream = await response.Content.ReadAsStreamAsync();
        using var reader = new System.IO.StreamReader(stream);
        Assert.NotNull(await reader.ReadLineAsync());
        response.Dispose();
        await state.End(id).Task.WaitAsync(TimeSpan.FromSeconds(10));
        await state.ScopeEnd(id).Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(1, state.ScopeDisposals[id]);
    }

    [Trait("Layer", "Integration")]
    [Fact]
    public async Task Dispose_Suspended_Execution_Scope_When_Response_Write_Is_Cancelled() {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton<StreamProbeState>();
        builder.Services.AddScoped<StreamScopeProbe>();
        builder.Services.AddScoped<IStreamRequestHandler<StreamProbeRequest, StreamProbeItem>, StreamProbeHandler>();
        builder.Services.AddSchemataStreams();
        using var app = builder.Build();
        var id = Guid.NewGuid().ToString("n");
        using var cancellation = new CancellationTokenSource();
        var output = new Mock<Stream>();
        output.SetupGet(stream => stream.CanWrite).Returns(true);
        output.Setup(stream => stream.WriteAsync(It.IsAny<ReadOnlyMemory<byte>>(), It.IsAny<CancellationToken>()))
            .Returns(() => {
                cancellation.Cancel();
                return ValueTask.FromCanceled(cancellation.Token);
            });
        var response = new Mock<IHttpResponseBodyFeature>();
        response.SetupGet(feature => feature.Stream).Returns(output.Object);
        response.Setup(feature => feature.StartAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        app.Use((context, next) => {
            context.RequestAborted = cancellation.Token;
            context.Features.Set(response.Object);
            return next(context);
        });
        app.MapSchemataStream<StreamProbeRequest, StreamProbeItem>("/v1/streams:probe");
        await app.StartAsync();
        using var client = app.GetTestClient();
        await Assert.ThrowsAnyAsync<Exception>(() => client.PostAsync("/v1/streams:probe", Body(id)));
        var state = app.Services.GetRequiredService<StreamProbeState>();
        await state.ScopeEnd(id).Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(1, state.ScopeDisposals[id]);
        Assert.False(state.Gate(id).Task.IsCompleted);
        output.Verify(stream => stream.WriteAsync(It.IsAny<ReadOnlyMemory<byte>>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Return_501_Before_Output_When_Stream_Dispatch_Is_Not_Installed() {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddRouting();
        using var app = builder.Build();
        app.MapSchemataStream<StreamProbeRequest, StreamProbeItem>("/v1/streams:probe");
        await app.StartAsync();
        var response = await app.GetTestClient().PostAsync("/v1/streams:probe", Body("unused"));
        Assert.Equal(HttpStatusCode.NotImplemented, response.StatusCode);
        Assert.Empty(await response.Content.ReadAsStringAsync());
    }
}
