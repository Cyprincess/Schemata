using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Identity.Integration.Tests.Fixtures;
using Schemata.Identity.Skeleton.Entities;
using Schemata.Transport.Http.Linking;
using Xunit;

namespace Schemata.Identity.Integration.Tests;

[Trait("Category", "Integration")]
public class LinkHeaderShould : IClassFixture<WebAppFactory>
{
    private readonly WebAppFactory _factory;

    public LinkHeaderShould(WebAppFactory factory) { _factory = factory; }

    [Fact]
    public async Task User_And_Role_Expose_Self_And_Canonical_Without_Session_Links() {
        var roleName = $"link-role-{Guid.NewGuid():n}";
        using (var scope = _factory.Services.CreateScope()) {
            var roles = scope.ServiceProvider.GetRequiredService<IRoleStore<SchemataRole>>();
            var role  = new SchemataRole { Name = roleName, NormalizedName = roleName.ToUpperInvariant() };
            Assert.True((await roles.CreateAsync(role, default)).Succeeded);
        }

        var client = _factory.CreateClient();

        var user = await client.GetAsync("/v1/users/test-user");
        Assert.Equal(HttpStatusCode.OK, user.StatusCode);
        AssertIdentityLinks(user, "/v1/users/test-user");

        var roleResponse = await client.GetAsync($"/v1/roles/{roleName}");
        Assert.Equal(HttpStatusCode.OK, roleResponse.StatusCode);
        AssertIdentityLinks(roleResponse, $"/v1/roles/{roleName}");
    }

    private static void AssertIdentityLinks(HttpResponseMessage response, string expected) {
        var values = response.Headers.GetValues(LinkHeader.Name).ToList();
        Assert.Single(values);
        var links = LinkHeader.Parse(values[0], response.RequestMessage!.RequestUri);

        Assert.Equal(links.Count, links.Select(Key).Distinct().Count());
        Assert.Equal(expected, Path(Assert.Single(links, link => link.Rel == "self").Target));
        Assert.Equal(expected, Path(Assert.Single(links, link => link.Rel == "canonical").Target));
        Assert.DoesNotContain(links, link => link.Target.Contains("session", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(links, link => link.Target.Contains("tenant", StringComparison.OrdinalIgnoreCase));
    }

    private static string Key(WebLink link) => string.Join("|",
        link.Target, link.Rel, link.Anchor, link.Hreflang, link.Media, link.Title, link.TitleStar, link.Type,
        link.Extensions is null
            ? string.Empty
            : string.Join(";", link.Extensions.OrderBy(pair => pair.Key, StringComparer.Ordinal)
                                              .Select(pair => pair.Key + "=" + pair.Value)));

    private static string Path(string target) => new Uri(target, UriKind.RelativeOrAbsolute).AbsolutePath;
}
