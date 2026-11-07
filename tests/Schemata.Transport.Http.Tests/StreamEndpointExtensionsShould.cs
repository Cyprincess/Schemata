using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Messaging.Skeleton;
using Xunit;

namespace Schemata.Transport.Http.Tests;

[Trait("Category", "Integration")]
[Trait("Layer", "Integration")]
public class StreamEndpointExtensionsShould
{
    [Fact]
    public async Task Cache_Serializer_Options_Per_Mapped_Endpoint() {
        var first = new ConcurrentQueue<object>();
        var second = new ConcurrentQueue<object>();
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddSingleton<IStreamDispatcher, Dispatcher>();
        await using var app = builder.Build();
        app.MapSchemataStream<Request, Item>("/first").AddEndpointFilter(async (invocation, next) => {
            var result = await next(invocation);
            if (result is not null) first.Enqueue(result);
            return result;
        });
        app.MapSchemataStream<Request, Item>("/second").AddEndpointFilter(async (invocation, next) => {
            var result = await next(invocation);
            if (result is not null) second.Enqueue(result);
            return result;
        });
        app.Urls.Add("http://127.0.0.1:0");
        await app.StartAsync();
        using var client = new HttpClient { BaseAddress = new(app.Urls.Single()) };

        try {
            await Task.WhenAll(Send(client, "/first"), Send(client, "/first"));
            await Send(client, "/second");
        } finally {
            await app.StopAsync();
        }

        Assert.Equal(2, first.Count);
        var secondResult = Assert.Single(second);
        var firstOptions = first.Select(Options).ToArray();
        Assert.Same(firstOptions[0], firstOptions[1]);
        Assert.NotSame(firstOptions[0], Options(secondResult));
    }

    private static JsonSerializerOptions Options(object result) {
        var field = result.GetType().GetFields(BindingFlags.Instance | BindingFlags.NonPublic)
                    .Single(candidate => candidate.FieldType == typeof(JsonSerializerOptions));
        return (JsonSerializerOptions)field.GetValue(result)!;
    }

    private static async Task Send(HttpClient client, string path) {
        using var content = new StringContent("{}", Encoding.UTF8, "application/json");
        using var response = await client.PostAsync(path, content);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/x-ndjson", response.Content.Headers.ContentType?.MediaType);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("\"complete\":true", body, StringComparison.Ordinal);
    }

    private sealed class Request : IStreamRequest<Item>;

    private sealed record Item(string Value);

    private sealed class Dispatcher : IStreamDispatcher
    {
        public async IAsyncEnumerable<TItem> Stream<TRequest, TItem>(
            TRequest request,
            ClaimsPrincipal? principal = null,
            [EnumeratorCancellation] CancellationToken ct = default
        ) where TRequest : IStreamRequest<TItem> {
            await Task.Yield();
            yield return (TItem)(object)new Item("one");
        }
    }
}
