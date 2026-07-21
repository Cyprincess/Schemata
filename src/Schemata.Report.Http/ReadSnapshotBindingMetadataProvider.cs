using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ModelBinding.Metadata;
using Schemata.Report.Foundation;

namespace Schemata.Report.Http;

/// <summary>
///     Binds the read-only <c>:read</c> custom method's <see cref="ReadSnapshotRequest" /> from the
///     query string using the AIP snake_case wire names (<c>page_size</c>, <c>page_token</c>). Lives in
///     the HTTP transport so the transport-neutral Foundation DTO carries no MVC binding metadata, while
///     the wire contract stays identical to a per-property <c>[FromQuery(Name = "...")]</c>.
/// </summary>
internal sealed class ReadSnapshotBindingMetadataProvider : IBindingMetadataProvider
{
    #region IBindingMetadataProvider Members

    public void CreateBindingMetadata(BindingMetadataProviderContext context) {
        if (context.Key.MetadataKind != ModelMetadataKind.Property
         || context.Key.ContainerType != typeof(ReadSnapshotRequest)) {
            return;
        }

        var name = context.Key.Name switch {
            nameof(ReadSnapshotRequest.PageSize)  => "page_size",
            nameof(ReadSnapshotRequest.PageToken) => "page_token",
            var _                                 => null,
        };
        if (name is null) {
            return;
        }

        context.BindingMetadata.BindingSource   = BindingSource.Query;
        context.BindingMetadata.BinderModelName = name;
    }

    #endregion
}
