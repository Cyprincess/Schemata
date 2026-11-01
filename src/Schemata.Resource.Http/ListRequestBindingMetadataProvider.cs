using System.Text.Json;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ModelBinding.Metadata;
using Schemata.Abstractions.Resource;

namespace Schemata.Resource.Http;

internal sealed class ListRequestBindingMetadataProvider : IBindingMetadataProvider
{
    public void CreateBindingMetadata(BindingMetadataProviderContext context) {
        if (context.Key.MetadataKind != ModelMetadataKind.Property
         || context.Key.ContainerType != typeof(ListRequest)) {
            return;
        }

        context.BindingMetadata.BindingSource = BindingSource.Query;
        context.BindingMetadata.BinderModelName = JsonNamingPolicy.SnakeCaseLower.ConvertName(context.Key.Name!);
    }
}
