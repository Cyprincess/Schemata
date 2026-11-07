using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Abstractions.Resource;
using Schemata.Core.Building;
using Xunit;

namespace Schemata.Resource.Http.Integration.Tests;

[Trait("Layer", "Unit")]
public class ListRequestBindingMetadataProviderShould
{
    [Fact]
    public void Bind_Derived_List_Request_Properties_From_Snake_Case_Query_Names() {
        using var provider = new ServiceCollection()
                            .AddSingleton(new ResourceRegistry())
                            .AddSchemataHttpResources()
                            .BuildServiceProvider();
        var metadata = provider.GetRequiredService<IModelMetadataProvider>();
        var properties = new[] {
            (nameof(ListRequest.Filter), "filter"),
            (nameof(ListRequest.OrderBy), "order_by"),
            (nameof(ListRequest.PageSize), "page_size"),
            (nameof(ListRequest.PageToken), "page_token"),
        };

        foreach (var (property, query) in properties) {
            var binding = metadata.GetMetadataForProperty(typeof(DerivedListRequest), property);
            Assert.Equal(BindingSource.Query, binding.BindingSource);
            Assert.Equal(query, binding.BinderModelName);
        }
    }

    private sealed class DerivedListRequest : ListRequest;
}
