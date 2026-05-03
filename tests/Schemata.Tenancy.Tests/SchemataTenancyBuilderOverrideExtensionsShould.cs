using System;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Schemata.Tenancy.Foundation;
using Schemata.Tenancy.Skeleton;
using Schemata.Tenancy.Skeleton.Entities;
using Xunit;

namespace Schemata.Tenancy.Tests;

public class SchemataTenancyBuilderOverrideExtensionsShould
{
    [Fact]
    public void ForAll_Applies_Configure_To_Root_Services_Immediately() {
        var services = new ServiceCollection();
        var builder  = new SchemataTenancyBuilder<SchemataTenant<Guid>, Guid>(services);

        builder.ForAll(s => s.AddSingleton<IMarker, MarkerA>());

        using var sp = services.BuildServiceProvider();
        Assert.IsType<MarkerA>(sp.GetRequiredService<IMarker>());
    }

    [Fact]
    public void ForTenant_String_Records_Into_Keyed_Options_Without_Touching_Root_Services() {
        var services = new ServiceCollection();
        services.AddOptions<SchemataTenancyOptions>();
        var builder = new SchemataTenancyBuilder<SchemataTenant<Guid>, Guid>(services);

        builder.ForTenant("alpha", s => s.AddSingleton<IMarker, MarkerA>());

        using var sp = services.BuildServiceProvider();

        // ForTenant(id) must NOT register into the root container.
        Assert.Null(sp.GetService<IMarker>());

        var opts = sp.GetRequiredService<IOptions<SchemataTenancyOptions>>().Value;
        Assert.Single(opts.TenantOverrides["alpha"]);
    }

    [Fact]
    public void ForTenant_Dynamic_Appends_To_DynamicOverrides_List() {
        var services = new ServiceCollection();
        services.AddOptions<SchemataTenancyOptions>();
        var builder = new SchemataTenancyBuilder<SchemataTenant<Guid>, Guid>(services);

        builder.ForTenant((_, _, _) => { });
        builder.ForTenant((_, _, _) => { });

        using var sp   = services.BuildServiceProvider();
        var       opts = sp.GetRequiredService<IOptions<SchemataTenancyOptions>>().Value;
        Assert.Equal(2, opts.DynamicOverrides.Count);
    }

    #region Nested type: IMarker

    private interface IMarker;

    #endregion

    #region Nested type: MarkerA

    private sealed class MarkerA : IMarker;

    #endregion
}
