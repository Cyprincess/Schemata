using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Schemata.Abstractions.Exceptions;
using Schemata.Scim.Models;
using Xunit;

namespace Schemata.Scim.Tests;

[Trait("Layer", "Integration")]
public sealed class ScimDiscoveryShould
{
    private const string ProbeSchema = "urn:example:params:scim:schemas:2.0:Probe";

    private static readonly ScimSchema Probe = new() {
        Id          = ProbeSchema,
        Name        = "Probe",
        Description = "Probe resource",
        Attributes = [
            new() {
                Name        = "displayName",
                Type        = ScimConstants.StringType,
                Description = "A probe label.",
                Required    = true,
            },
        ],
    };

    private static readonly ScimResourceType ProbeType = new() {
        Id          = "probe-id",
        Name        = "Probe",
        Endpoint    = "/Probes",
        Description = "Probe resource",
        Schema      = ProbeSchema,
    };

    [Fact]
    public async Task Serve_Discovery_Endpoints_With_The_Scim_Media_Type() {
        using var host   = await StartHostAsync();
        using var client = host.GetTestClient();

        foreach (var path in new[] { "/scim/v2/ServiceProviderConfig", "/scim/v2/ResourceTypes", "/scim/v2/Schemas" }) {
            using var response = await client.GetAsync(path);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(ScimConstants.MediaType, response.Content.Headers.ContentType?.MediaType);
        }
    }

    [Fact]
    public async Task Declare_Unsupported_Optional_Capabilities() {
        using var host     = await StartHostAsync();
        using var client   = host.GetTestClient();
        using var response = await client.GetAsync("/scim/v2/ServiceProviderConfig");
        var       root     = await ReadAsync(response);

        Assert.Equal(ScimConstants.ServiceProviderConfigSchema, root.GetProperty("schemas")[0].GetString());
        Assert.Equal("ServiceProviderConfig", root.GetProperty("meta").GetProperty("resourceType").GetString());
        Assert.EndsWith("/scim/v2/ServiceProviderConfig", root.GetProperty("meta").GetProperty("location").GetString());
        Assert.False(root.GetProperty("meta").TryGetProperty("version", out _));

        Assert.False(root.GetProperty("patch").GetProperty("supported").GetBoolean());
        Assert.False(root.GetProperty("bulk").GetProperty("supported").GetBoolean());
        Assert.Equal(0, root.GetProperty("bulk").GetProperty("maxOperations").GetInt32());
        Assert.Equal(0, root.GetProperty("bulk").GetProperty("maxPayloadSize").GetInt32());
        Assert.False(root.GetProperty("filter").GetProperty("supported").GetBoolean());
        Assert.Equal(0, root.GetProperty("filter").GetProperty("maxResults").GetInt32());
        Assert.False(root.GetProperty("changePassword").GetProperty("supported").GetBoolean());
        Assert.False(root.GetProperty("sort").GetProperty("supported").GetBoolean());
        Assert.False(root.GetProperty("etag").GetProperty("supported").GetBoolean());
    }

    [Fact]
    public async Task Expose_Host_Configured_Service_Provider_Details() {
        using var host = await StartHostAsync(configureServices: services => services.Configure<ScimServiceProviderOptions>(options => {
            options.DocumentationUri = "https://example.test/help/scim";
            options.AuthenticationSchemes.Add(new() {
                Type        = "oauthbearertoken",
                Name        = "OAuth Bearer Token",
                Description = "Authentication scheme using the OAuth Bearer Token Standard",
                SpecUri     = "https://www.rfc-editor.org/info/rfc6750",
            });
        }));
        using var client   = host.GetTestClient();
        using var response = await client.GetAsync("/scim/v2/ServiceProviderConfig");
        var       root     = await ReadAsync(response);

        Assert.Equal("https://example.test/help/scim", root.GetProperty("documentationUri").GetString());

        var schemes = root.GetProperty("authenticationSchemes");
        Assert.Equal(1, schemes.GetArrayLength());
        Assert.Equal("oauthbearertoken", schemes[0].GetProperty("type").GetString());
        Assert.Equal("OAuth Bearer Token", schemes[0].GetProperty("name").GetString());
    }

    [Fact]
    public async Task List_Resource_Types_With_Id_And_Meta() {
        using var host     = await StartHostAsync();
        using var client   = host.GetTestClient();
        using var response = await client.GetAsync("/scim/v2/ResourceTypes");
        var       root     = await ReadAsync(response);

        Assert.Equal(ScimConstants.ListResponseSchema, root.GetProperty("schemas")[0].GetString());
        Assert.Equal(1, root.GetProperty("totalResults").GetInt32());
        Assert.Equal(1, root.GetProperty("itemsPerPage").GetInt32());
        Assert.Equal(1, root.GetProperty("startIndex").GetInt32());

        var resource = root.GetProperty("Resources")[0];
        Assert.Equal(ScimConstants.ResourceTypeSchema, resource.GetProperty("schemas")[0].GetString());
        Assert.Equal("probe-id", resource.GetProperty("id").GetString());
        Assert.Equal("Probe", resource.GetProperty("name").GetString());
        Assert.Equal("/Probes", resource.GetProperty("endpoint").GetString());
        Assert.Equal(ProbeSchema, resource.GetProperty("schema").GetString());
        Assert.Equal("ResourceType", resource.GetProperty("meta").GetProperty("resourceType").GetString());
        Assert.EndsWith("/scim/v2/ResourceTypes/probe-id", resource.GetProperty("meta").GetProperty("location").GetString());
    }

    [Fact]
    public async Task Return_A_Single_Resource_Type_By_Id_Or_Name() {
        using var host   = await StartHostAsync();
        using var client = host.GetTestClient();

        foreach (var id in new[] { "probe-id", "Probe" }) {
            using var response = await client.GetAsync("/scim/v2/ResourceTypes/" + id);
            var       root     = await ReadAsync(response);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(ScimConstants.MediaType, response.Content.Headers.ContentType?.MediaType);
            Assert.Equal(ScimConstants.ResourceTypeSchema, root.GetProperty("schemas")[0].GetString());
            Assert.Equal("probe-id", root.GetProperty("id").GetString());
            Assert.Equal("ResourceType", root.GetProperty("meta").GetProperty("resourceType").GetString());
            Assert.EndsWith("/scim/v2/ResourceTypes/probe-id", root.GetProperty("meta").GetProperty("location").GetString());
            Assert.Equal(root.GetProperty("meta").GetProperty("location").GetString(), response.Content.Headers.ContentLocation?.ToString());
        }
    }

    [Fact]
    public async Task List_Installed_Schemas_With_Schema_Id_And_Meta() {
        using var host     = await StartHostAsync();
        using var client   = host.GetTestClient();
        using var response = await client.GetAsync("/scim/v2/Schemas");
        var       root     = await ReadAsync(response);

        Assert.Equal(ScimConstants.ListResponseSchema, root.GetProperty("schemas")[0].GetString());

        var resources = root.GetProperty("Resources");
        Assert.Equal(root.GetProperty("totalResults").GetInt32(), resources.GetArrayLength());
        Assert.Equal(4, resources.GetArrayLength());

        var ids = new List<string>();
        foreach (var resource in resources.EnumerateArray()) {
            Assert.Equal(ScimConstants.SchemaSchema, resource.GetProperty("schemas")[0].GetString());
            Assert.Equal("Schema", resource.GetProperty("meta").GetProperty("resourceType").GetString());
            Assert.EndsWith(
                "/scim/v2/Schemas/" + resource.GetProperty("id").GetString(),
                resource.GetProperty("meta").GetProperty("location").GetString());
            Assert.False(resource.GetProperty("meta").TryGetProperty("version", out _));
            ids.Add(resource.GetProperty("id").GetString()!);
        }

        Assert.Contains(ScimConstants.ServiceProviderConfigSchema, ids);
        Assert.Contains(ScimConstants.ResourceTypeSchema, ids);
        Assert.Contains(ScimConstants.SchemaSchema, ids);
        Assert.Contains(ProbeSchema, ids);
    }

    [Fact]
    public async Task Return_A_Single_Schema() {
        using var host     = await StartHostAsync();
        using var client   = host.GetTestClient();
        using var response = await client.GetAsync("/scim/v2/Schemas/" + Uri.EscapeDataString(ProbeSchema));
        var       root     = await ReadAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(ScimConstants.MediaType, response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(ScimConstants.SchemaSchema, root.GetProperty("schemas")[0].GetString());
        Assert.Equal(ProbeSchema, root.GetProperty("id").GetString());
        Assert.Equal("Schema", root.GetProperty("meta").GetProperty("resourceType").GetString());
        Assert.Equal("displayName", root.GetProperty("attributes")[0].GetProperty("name").GetString());
        Assert.Equal(root.GetProperty("meta").GetProperty("location").GetString(), response.Content.Headers.ContentLocation?.ToString());
    }

    [Fact]
    public async Task Serve_Every_Advertised_Service_Provider_Attribute_In_Its_Schema() {
        using var host     = await StartHostAsync();
        using var client   = host.GetTestClient();
        using var response = await client.GetAsync("/scim/v2/Schemas/" + Uri.EscapeDataString(ScimConstants.ServiceProviderConfigSchema));
        var       root     = await ReadAsync(response);

        var names = new List<string>();
        foreach (var attribute in root.GetProperty("attributes").EnumerateArray()) {
            names.Add(attribute.GetProperty("name").GetString()!);
        }

        Assert.Contains("documentationUri", names);
        Assert.Contains("patch", names);
        Assert.Contains("bulk", names);
        Assert.Contains("filter", names);
        Assert.Contains("changePassword", names);
        Assert.Contains("sort", names);
        Assert.Contains("etag", names);
        Assert.Contains("authenticationSchemes", names);
    }

    [Fact]
    public async Task Serve_Discovery_For_An_Empty_Registry() {
        using var host   = await StartHostAsync(registerProbe: false);
        using var client = host.GetTestClient();

        using var resourceTypes = await client.GetAsync("/scim/v2/ResourceTypes");
        var       types         = await ReadAsync(resourceTypes);
        Assert.Equal(0, types.GetProperty("totalResults").GetInt32());
        Assert.Equal(0, types.GetProperty("Resources").GetArrayLength());

        using var schemas = await client.GetAsync("/scim/v2/Schemas");
        var       root    = await ReadAsync(schemas);
        Assert.Equal(3, root.GetProperty("totalResults").GetInt32());

        using var missing = await client.GetAsync("/scim/v2/ResourceTypes/User");
        var       error   = await ReadAsync(missing);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.Equal(ScimConstants.ErrorSchema, error.GetProperty("schemas")[0].GetString());
    }

    [Fact]
    public async Task Convert_Protocol_Failures_To_Scim_Errors() {
        using var host = await StartHostAsync(
            frameworkErrors: true,
            mapEndpoints: endpoints => {
                endpoints.MapGet("/scim/v2/throw", _ => {
                    throw new InvalidOperationException("boom");
                });
                endpoints.MapGet("/scim/v2/throw-domain", _ => {
                    throw new SchemataException(409, "ABORTED", "conflict");
                });
            });
        using var client = host.GetTestClient();

        using (var missing = await client.GetAsync("/scim/v2/missing")) {
            var root = await ReadAsync(missing);
            AssertScimError(missing, root, "404", "not found");
        }

        using (var wrongMethod = await client.PostAsync("/scim/v2/ServiceProviderConfig", null)) {
            var root = await ReadAsync(wrongMethod);
            AssertScimError(wrongMethod, root, "405", "not supported");
        }

        using (var failure = await client.GetAsync("/scim/v2/throw")) {
            var root = await ReadAsync(failure);
            AssertScimError(failure, root, "500", "internal error");
            Assert.DoesNotContain("boom", root.GetProperty("detail").GetString());
        }

        using (var failure = await client.GetAsync("/scim/v2/throw-domain")) {
            var root = await ReadAsync(failure);
            AssertScimError(failure, root, "409", "conflict");
        }
    }

    private static void AssertScimError(HttpResponseMessage response, JsonElement root, string status, string detail) {
        Assert.Equal(ScimConstants.MediaType, response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(ScimConstants.ErrorSchema, root.GetProperty("schemas")[0].GetString());
        Assert.Equal(status, root.GetProperty("status").GetString());
        Assert.Contains(detail, root.GetProperty("detail").GetString());
        Assert.False(root.TryGetProperty("error", out _));
    }

    private static async Task<IHost> StartHostAsync(
        bool                          registerProbe    = true,
        Action<IServiceCollection>?   configureServices = null,
        bool                          frameworkErrors   = false,
        Action<IEndpointRouteBuilder>? mapEndpoints      = null
    ) {
        return await new HostBuilder()
                    .ConfigureWebHost(web => web.UseTestServer()
                                               .ConfigureServices(services => {
                                                    services.AddRouting();
                                                    services.AddControllers();
                                                    if (registerProbe) {
                                                        services.AddSchemataScimResourceType(ProbeType, Probe);
                                                    } else {
                                                        services.AddSchemataScim();
                                                    }

                                                    configureServices?.Invoke(services);
                                                })
                                               .Configure(app => {
                                                    if (frameworkErrors) {
                                                        app.UseSchemataExceptionHandler();
                                                    }

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
