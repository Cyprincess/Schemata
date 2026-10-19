using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Schemata.Abstractions.Advisors;
using Schemata.Insight.Http.Integration.Tests.Fixtures;
using Schemata.Insight.Skeleton.Advisors;
using Schemata.Insight.Skeleton.Catalog;
using Schemata.Insight.Skeleton.Models;
using Xunit;

namespace Schemata.Insight.Http.Integration.Tests;

[Trait("Layer", "Integration")]
public sealed class CallerContinuationShould
{
    [Theory]
    [InlineData("bob")]
    [InlineData("anonymous")]
    [InlineData("anonymous-name")]
    [InlineData("other-scheme")]
    [InlineData("unidentified")]
    [InlineData("default-anonymous")]
    public async Task Rejects_Name_Only_Caller_Changes_Before_Repository_Reads(string caller) {
        var keys = Directory.CreateTempSubdirectory("insight-caller-");
        try {
            var reads = new ReadProbe();
            using var factory = Factory(keys, reads);
            using var client = factory.CreateClient();
            SetCaller(client, "alice");
            var first = await Page(client);
            Assert.Equal("Bob", Assert.Single(first.GetProperty("rows").EnumerateArray()).GetProperty("full_name").GetString());
            Assert.Equal(1, reads.Count);
            var token = first.GetProperty("next_page_token").GetString()!;
            SetCaller(client, caller);
            var before = reads.Count;
            using var rejected = await client.PostAsJsonAsync("/v1/insight:query", Request(token));
            Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
            var error = await rejected.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal("INVALID_ARGUMENT", error.GetProperty("error").GetProperty("status").GetString());
            Assert.Equal("INVALID_ARGUMENT", Assert.Single(error.GetProperty("error").GetProperty("details").EnumerateArray(),
                detail => detail.TryGetProperty("reason", out _)).GetProperty("reason").GetString());
            Assert.Equal(before, reads.Count);
        } finally { keys.Delete(true); }
    }

    [Fact]
    public async Task Continues_Name_Only_Alice_With_Exact_Repository_Rows_And_Ignores_Unauthenticated_Claims() {
        var keys = Directory.CreateTempSubdirectory("insight-caller-");
        try {
            using var factory = Factory(keys, new());
            using var client = factory.CreateClient();
            var names = new List<string?>();
            string? token = null;
            do {
                SetCaller(client, token is null ? "alice" : "alice-shadow");
                var page = await Page(client, token);
                foreach (var row in page.GetProperty("rows").EnumerateArray()) names.Add(row.GetProperty("full_name").GetString());
                token = page.TryGetProperty("next_page_token", out var next) ? next.GetString() : null;
            } while (token is not null);
            Assert.Equal(new[] { "Bob", "Cleo", "Ada" }, names);
        } finally { keys.Delete(true); }
    }

    [Fact]
    public async Task Allows_Unidentified_Authenticated_Terminal_Queries_But_Rejects_Continuation_Creation() {
        var keys = Directory.CreateTempSubdirectory("insight-caller-");
        try {
            using var factory = Factory(keys, new());
            using var client = factory.CreateClient();
            SetCaller(client, "unidentified");
            using var terminal = await client.PostAsJsonAsync("/v1/insight:query", Request(null, 10));
            Assert.Equal(HttpStatusCode.OK, terminal.StatusCode);
            var page = await terminal.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(new[] { "Bob", "Cleo", "Ada" }, page.GetProperty("rows").EnumerateArray().Select(row => row.GetProperty("full_name").GetString()));
            using var paged = await client.PostAsJsonAsync("/v1/insight:query", Request());
            Assert.Equal(HttpStatusCode.BadRequest, paged.StatusCode);
            var error = await paged.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal("INVALID_ARGUMENT", error.GetProperty("error").GetProperty("status").GetString());
        } finally { keys.Delete(true); }
    }

    private static WebAppFactory Factory(DirectoryInfo keys, ReadProbe reads) => new WebAppFactory().WithServices(services => {
        services.AddDataProtection().PersistKeysToFileSystem(keys).SetApplicationName("InsightCallerContinuations");
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IInsightSourceAdvisor>(reads));
        services.Insert(0, ServiceDescriptor.Singleton<IStartupFilter>(new CallerFilter()));
    });

    private static object Request(string? token = null, int size = 1) => new {
        sources = new[] { new { alias = "s", name = "students" } },
        transformations = new[] { new { order_by = new { order_by = "age" } } },
        page_size = size, page_token = token,
    };

    private static async Task<JsonElement> Page(HttpClient client, string? token = null) {
        using var response = await client.PostAsJsonAsync("/v1/insight:query", Request(token));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static void SetCaller(HttpClient client, string caller) {
        client.DefaultRequestHeaders.Remove("X-Test-Caller");
        client.DefaultRequestHeaders.Add("X-Test-Caller", caller);
    }

    private sealed class CallerFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app => {
            app.Use(async (context, continuation) => {
                var caller = context.Request.Headers["X-Test-Caller"].ToString();
                var identity = new ClaimsIdentity(caller is "unidentified" or "anonymous" ? [] : [new Claim(ClaimTypes.Name, caller == "bob" ? "Bob" : "Alice")],
                    caller is "anonymous" or "anonymous-name" or "default-anonymous" ? null : caller == "other-scheme" ? "other" : "test");
                context.User = new(identity);
                if (caller is "alice-shadow" or "default-anonymous") {
                    context.User.AddIdentity(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "Bob")], caller == "default-anonymous" ? "test" : null));
                }
                await continuation();
            });
            next(app);
        };
    }

    private sealed class ReadProbe : IInsightSourceAdvisor
    {
        public int Order => 0;
        internal int Count { get; private set; }

        public Task<AdviseResult> AdviseAsync(AdviceContext context, SourceBinding binding, SourceConfig config,
            ClaimsPrincipal? principal, CancellationToken ct = default) {
            Count++;
            return Task.FromResult(AdviseResult.Continue);
        }
    }
}
