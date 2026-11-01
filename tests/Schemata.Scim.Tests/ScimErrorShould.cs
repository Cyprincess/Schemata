using System;
using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Schemata.Abstractions.Exceptions;
using Xunit;

namespace Schemata.Scim.Tests;

[Trait("Layer", "Integration")]
public sealed class ScimErrorShould
{
    [Fact]
    public async Task Describe_Model_State_Failures_With_The_Invalid_Syntax_Keyword() {
        using var host     = await StartHostAsync();
        using var client   = host.GetTestClient();
        using var response = await client.GetAsync("/scim/v2/errors/required");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(ScimConstants.MediaType, response.Content.Headers.ContentType?.MediaType);
        AssertError(await ReadAsync(response), "400", ScimConstants.InvalidSyntax);
    }

    [Fact]
    public async Task Omit_The_Keyword_When_No_Scim_Type_Applies() {
        using var host   = await StartHostAsync();
        using var client = host.GetTestClient();

        using (var missing = await client.GetAsync("/scim/v2/ResourceTypes/unknown")) {
            Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
            AssertError(await ReadAsync(missing), "404");
        }

        using (var missingSchema = await client.GetAsync("/scim/v2/Schemas/"
                                                         + Uri.EscapeDataString("urn:example:missing"))) {
            Assert.Equal(HttpStatusCode.NotFound, missingSchema.StatusCode);
            AssertError(await ReadAsync(missingSchema), "404");
        }

        using (var wrongMethod = await client.PostAsync("/scim/v2/ServiceProviderConfig", null)) {
            Assert.Equal(HttpStatusCode.MethodNotAllowed, wrongMethod.StatusCode);
            AssertError(await ReadAsync(wrongMethod), "405");
        }
    }

    [Fact]
    public async Task Reject_Undeclared_Discovery_Methods() {
        using var host   = await StartHostAsync();
        using var client = host.GetTestClient();

        foreach (var path in new[] {
                     "/scim/v2/ServiceProviderConfig",
                     "/scim/v2/ResourceTypes",
                     "/scim/v2/ResourceTypes/User",
                     "/scim/v2/Schemas",
                     "/scim/v2/Schemas/" + Uri.EscapeDataString(ScimConstants.ServiceProviderConfigSchema),
                 }) {
            foreach (var method in new[] { HttpMethod.Put, HttpMethod.Patch, HttpMethod.Delete }) {
                using var request = new HttpRequestMessage(method, path);
                using var response = await client.SendAsync(request);

                Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
                Assert.Equal(ScimConstants.MediaType, response.Content.Headers.ContentType?.MediaType);
                AssertError(await ReadAsync(response), "405");
            }
        }
    }

    [Fact]
    public async Task Map_Unhandled_And_Domain_Failures_To_Scim_Errors() {
        using var host = await StartHostAsync(endpoints => {
            endpoints.MapGet("/scim/v2/errors/boom", _ => throw new InvalidOperationException("secret detail"));
            endpoints.MapGet("/scim/v2/errors/domain", _ => throw new SchemataException(409, "ABORTED", "conflict"));
        });
        using var client = host.GetTestClient();

        using (var failure = await client.GetAsync("/scim/v2/errors/boom")) {
            Assert.Equal(HttpStatusCode.InternalServerError, failure.StatusCode);
            Assert.Equal(ScimConstants.MediaType, failure.Content.Headers.ContentType?.MediaType);
            var root = await ReadAsync(failure);
            AssertError(root, "500");
            Assert.DoesNotContain("secret detail", root.GetProperty("detail").GetString());
        }

        using (var domain = await client.GetAsync("/scim/v2/errors/domain")) {
            Assert.Equal(HttpStatusCode.Conflict, domain.StatusCode);
            var root = await ReadAsync(domain);
            AssertError(root, "409");
            Assert.Equal("conflict", root.GetProperty("detail").GetString());
        }
    }

    [Fact]
    public async Task Leave_Non_Scim_Paths_Out_Of_The_Protocol() {
        using var host     = await StartHostAsync();
        using var client   = host.GetTestClient();
        using var response = await client.GetAsync("/outside/missing");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.NotEqual(ScimConstants.MediaType, response.Content.Headers.ContentType?.MediaType);
        Assert.DoesNotContain(ScimConstants.ErrorSchema, await response.Content.ReadAsStringAsync());
    }

    private static void AssertError(JsonElement root, string status, string? scimType = null) {
        Assert.Equal(1, root.GetProperty("schemas").GetArrayLength());
        Assert.Equal(ScimConstants.ErrorSchema, root.GetProperty("schemas")[0].GetString());
        Assert.Equal(JsonValueKind.String, root.GetProperty("status").ValueKind);
        Assert.Equal(status, root.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.String, root.GetProperty("detail").ValueKind);
        Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("detail").GetString()));
        if (scimType is null) {
            Assert.False(root.TryGetProperty("scimType", out _));
        } else {
            Assert.Equal(scimType, root.GetProperty("scimType").GetString());
        }

        Assert.False(root.TryGetProperty("error", out _));
    }

    private static async Task<IHost> StartHostAsync(Action<IEndpointRouteBuilder>? mapEndpoints = null) {
        return await new HostBuilder()
                    .ConfigureWebHost(web => web.UseTestServer()
                                               .ConfigureServices(services => {
                                                    services.AddRouting();
                                                    services.AddControllers()
                                                            .AddApplicationPart(typeof(ScimErrorProbeController).Assembly);
                                                    services.AddSchemataScim();
                                                })
                                               .Configure(app => {
                                                    app.UseSchemataScimErrors();
                                                    app.UseRouting();
                                                    app.UseEndpoints(endpoints => {
                                                        endpoints.MapControllers();
                                                        mapEndpoints?.Invoke(endpoints);
                                                    });
                                                }))
                    .StartAsync();
    }

    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response) {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        return document.RootElement.Clone();
    }
}

[ApiController]
[Route("~" + ScimConstants.BasePath + "/errors")]
public sealed class ScimErrorProbeController : ControllerBase
{
    [HttpGet("required")]
    public IActionResult Required([Required, FromQuery] string value) => Ok(value);
}
