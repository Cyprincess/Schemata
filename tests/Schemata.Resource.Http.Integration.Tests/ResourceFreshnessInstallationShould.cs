using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Schemata.Resource.Http.Integration.Tests.Fixtures;
using Xunit;

namespace Schemata.Resource.Http.Integration.Tests;

[Trait("Layer", "Integration")]
public class ResourceFreshnessInstallationShould
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task WithoutFreshness_PublicHttpHost_AllowsStaleTagsAndEmitsNoEntityTag(bool exclusionsFirst, bool repeatInstall) {
        using var baseline = new WebAppFactory();
        using var host = baseline.WithWebHostBuilder(builder => {
            builder.UseSetting("ResourceExclusionsFirst", exclusionsFirst ? "true" : "false");
            builder.UseSetting("ResourceRepeatInstall", repeatInstall ? "true" : "false");
        });
        using var client = host.CreateClient();
        using var created = await client.PostAsJsonAsync("/v1/students", new { full_name = "FreshnessReplay" });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var createBody = await created.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(createBody.TryGetProperty("etag", out var createTag) && createTag.ValueKind != JsonValueKind.Null);
        var name = createBody.GetProperty("name").GetString();
        Assert.NotNull(name);

        using var update = new HttpRequestMessage(HttpMethod.Patch, $"/v1/{name}") {
            Content = new StringContent("""{"full_name":"FreshnessUpdated","update_mask":"full_name"}""", Encoding.UTF8, "application/json"),
        };
        update.Headers.TryAddWithoutValidation("If-Match", "W/\"stale\"");
        using var updated = await client.SendAsync(update);
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        var updateBody = await updated.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("FreshnessUpdated", updateBody.GetProperty("full_name").GetString());
        Assert.False(updateBody.TryGetProperty("etag", out var updateTag) && updateTag.ValueKind != JsonValueKind.Null);
        using var preview = await client.GetAsync($"/v1/{name}:preview?EntityTag={Uri.EscapeDataString("W/\"stale\"")}");
        Assert.Equal(HttpStatusCode.OK, preview.StatusCode);
        var previewBody = await preview.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("FreshnessUpdated", previewBody.GetProperty("full_name").GetString());
        Assert.False(previewBody.TryGetProperty("etag", out var previewTag) && previewTag.ValueKind != JsonValueKind.Null);

        using var delete = new HttpRequestMessage(HttpMethod.Delete, $"/v1/{name}");
        delete.Headers.TryAddWithoutValidation("If-Match", "W/\"stale\"");
        using var deleted = await client.SendAsync(delete);
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        using var missing = await client.GetAsync($"/v1/{name}");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }
}
