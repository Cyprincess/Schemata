using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Resource.Http.Integration.Tests.Fixtures;
using Xunit;

namespace Schemata.Resource.Http.Integration.Tests;

[Trait("Category", "Integration")]
[Trait("Layer", "Integration")]
public class HttpResourceRegistrationShould : IClassFixture<WebAppFactory>
{
    private readonly WebAppFactory _factory;

    public HttpResourceRegistrationShould(WebAppFactory factory) { _factory = factory; }

    [Fact]
    public async Task Repeated_Registration_Serves_List_And_Custom_Method_Requests() {
        using var host = _factory.WithWebHostBuilder(builder => {
            builder.ConfigureServices(services => {
                services.AddSchemataHttpResources();
                services.AddSchemataHttpResources();
            });
        });
        using var client = host.CreateClient();
        var fullName = "RepeatedRegistration";
        using var created = await client.PostAsync("/v1/students",
            new StringContent($$"""{"full_name":"{{fullName}}"}""", Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var createdBody = await created.Content.ReadFromJsonAsync<JsonElement>();
        var name = createdBody.GetProperty("name").GetString();

        using var listed = await client.GetAsync("/v1/students");
        Assert.Equal(HttpStatusCode.OK, listed.StatusCode);
        var listedBody = await listed.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains(listedBody.GetProperty("students").EnumerateArray(),
            item => item.GetProperty("full_name").GetString() == fullName);

        using var preview = await client.GetAsync($"/v1/{name}:preview");
        Assert.Equal(HttpStatusCode.OK, preview.StatusCode);
        var previewBody = await preview.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(fullName, previewBody.GetProperty("full_name").GetString());
    }
}
