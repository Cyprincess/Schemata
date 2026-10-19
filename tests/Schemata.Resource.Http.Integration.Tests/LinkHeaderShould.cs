using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Abstractions.Resource;
using Schemata.Core.Building;
using Schemata.Resource.Http.Integration.Tests.Fixtures;
using Schemata.Transport.Http.Linking;
using Xunit;

namespace Schemata.Resource.Http.Integration.Tests;

[Trait("Category", "Integration")]
public class LinkHeaderShould : IClassFixture<WebAppFactory>
{
    private readonly WebAppFactory _factory;

    public LinkHeaderShould(WebAppFactory factory) { _factory = factory; }

    [Fact]
    public async Task List_Emits_Self_Next_And_Second_Page_Navigation() {
        var client = _factory.CreateClient();
        var name   = $"Links{Guid.NewGuid():n}";
        for (var i = 0; i < 2; i++) {
            var created = await client.PostAsync("/v1/students",
                new StringContent(JsonSerializer.Serialize(new { full_name = name }), Encoding.UTF8, "application/json"));
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        }

        var query    = "/v1/students?filter=" + Uri.EscapeDataString($"full_name=\"{name}\"") + "&page_size=1";
        var response = await client.GetAsync(query);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var first = Links(response);
        var self  = Assert.Single(first, link => link.Rel == "self");
        Assert.Contains(name, self.Target);
        Assert.Contains("page_size=1", self.Target);

        var next = Assert.Single(first, link => link.Rel == "next");
        Assert.Contains("page_token=", next.Target);

        var last = Assert.Single(first, link => link.Rel == "last");
        Assert.Contains("page_token=", last.Target);

        var secondResponse = await client.GetAsync(next.Target);
        Assert.Equal(HttpStatusCode.OK, secondResponse.StatusCode);
        var second = Links(secondResponse);

        var firstLink = Assert.Single(second, link => link.Rel == "first");
        Assert.DoesNotContain("page_token=", firstLink.Target);
        Assert.Contains("page_size=1", firstLink.Target);

        var previous = Assert.Single(second, link => link.Rel == "prev");
        Assert.Contains("page_token=", previous.Target);
        Assert.DoesNotContain(second, link => link.Rel == "next");
        Assert.DoesNotContain(second, link => link.Rel == "last");
    }

    [Fact]
    public async Task Get_Emits_Self_Canonical_And_Child_Collections() {
        var client   = _factory.CreateClient();
        var created  = await client.PostAsync("/v1/students",
            new StringContent("""{"full_name":"LinkTarget"}""", Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var body     = await created.Content.ReadFromJsonAsync<JsonElement>();
        var name     = Assert.IsType<string>(body.GetProperty("name").GetString());

        var response = await client.GetAsync($"/v1/{name}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var links = Links(response);
        var self  = Assert.Single(links, link => link.Rel == "self");
        Assert.EndsWith($"/v1/{name}", self.Target);

        var canonical = Assert.Single(links, link => link.Rel == "canonical");
        Assert.EndsWith($"/v1/{name}", canonical.Target);

        var notes = Assert.Single(links, link => link.Rel == "related" && link.Title == "Notes");
        Assert.EndsWith($"/v1/{name}/notes", notes.Target);
    }

    [Fact]
    public async Task Unusual_Names_Produce_Parsable_Links() {
        var raw = "课程 note 100%";
        using (var scope = _factory.Services.CreateScope()) {
            var database = scope.ServiceProvider.GetRequiredService<TestDbContext>();
            database.Students.Add(new Student {
                Uid = Guid.NewGuid(), Name = raw, CanonicalName = $"students/{raw}", FullName = "Encoded",
            });
            await database.SaveChangesAsync();
        }

        var response = await _factory.CreateClient().GetAsync("/v1/students/" + Uri.EscapeDataString(raw));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var canonical = Assert.Single(Links(response), link => link.Rel == "canonical");
        Assert.Contains("%20", canonical.Target);
        Assert.Contains("%25", canonical.Target);
    }

    [Fact]
    public async Task Create_Self_Identifies_The_Written_Resource() {
        var created = await _factory.CreateClient().PostAsync("/v1/students",
            new StringContent("""{"full_name":"CreateSelf"}""", Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        var links     = Links(created);
        var self      = Assert.Single(links, link => link.Rel == "self");
        var canonical = Assert.Single(links, link => link.Rel == "canonical");
        Assert.Equal(canonical.Target, self.Target);
        Assert.DoesNotContain("/v1/students?", self.Target);
    }

    [Fact]
    public async Task Per_Resource_Estimated_Total_Suppresses_Last_Link() {
        using var factory = _factory.WithWebHostBuilder(builder => builder.UseSetting("CountEstimator", "true"));
        var       suffix  = Guid.NewGuid().ToString("n");
        using (var scope = factory.Services.CreateScope()) {
            var database = scope.ServiceProvider.GetRequiredService<TestDbContext>();
            database.EstimatedThings.AddRange(Thing($"estimated-{suffix}-a"), Thing($"estimated-{suffix}-b"));
            await database.SaveChangesAsync();
        }

        var response = await factory.CreateClient().GetAsync("/v1/estimatedThings?page_size=1");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(2, body.GetProperty("total_size").GetInt32());

        var links = Links(response);
        Assert.Single(links, link => link.Rel == "next");
        Assert.DoesNotContain(links, link => link.Rel == "last");
    }

    [Fact]
    public async Task Global_Estimated_Total_Suppresses_Last_Link() {
        using var factory = _factory.WithWebHostBuilder(builder => {
            builder.UseSetting("CountEstimator", "true");
            builder.ConfigureServices(services => services.PostConfigure<SchemataResourceOptions>(
                options => options.TotalSize = TotalSizeMode.Estimated));
        });
        var name = $"GlobalEstimated{Guid.NewGuid():n}";
        using (var scope = factory.Services.CreateScope()) {
            var database = scope.ServiceProvider.GetRequiredService<TestDbContext>();
            for (var i = 0; i < 2; i++) {
                var key = $"{name}-{i}";
                database.Students.Add(new Student {
                    Uid = Guid.NewGuid(), Name = key, CanonicalName = $"students/{key}", FullName = name,
                });
            }

            await database.SaveChangesAsync();
        }

        var query = "/v1/students?filter=" + Uri.EscapeDataString($"full_name=\"{name}\"") + "&page_size=1";
        var response = await factory.CreateClient().GetAsync(query);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(2, body.GetProperty("total_size").GetInt32());

        var links = Links(response);
        Assert.Single(links, link => link.Rel == "next");
        Assert.DoesNotContain(links, link => link.Rel == "last");
    }

    [Fact]
    public async Task Missing_Resource_Has_No_Link_Header() {
        var response = await _factory.CreateClient().GetAsync("/v1/students/missing-link-target");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.False(response.Headers.Contains(LinkHeader.Name));
    }

    private static EstimatedThing Thing(string name) => new() {
        Uid = Guid.NewGuid(), Name = name, CanonicalName = $"estimatedThings/{name}", Label = name,
    };

    private static IReadOnlyList<WebLink> Links(HttpResponseMessage response) {
        Assert.True(response.Headers.TryGetValues(LinkHeader.Name, out var values), "The response carries no Link header.");
        return LinkHeader.Parse(string.Join(", ", values), response.RequestMessage!.RequestUri);
    }
}
