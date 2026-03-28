using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Schemata.Authorization.Skeleton.Models;

namespace Schemata.Authorization.Foundation.Binding;

public sealed class OAuthRequestBinderProvider : IModelBinderProvider
{
    private static readonly HashSet<Type> Supported = [
        typeof(AuthorizeRequest),
        typeof(TokenRequest),
        typeof(InteractRequest),
        typeof(DeviceAuthorizeRequest),
        typeof(IntrospectRequest),
        typeof(RevokeRequest),
        typeof(EndSessionRequest),
    ];

    #region IModelBinderProvider Members

    public IModelBinder? GetBinder(ModelBinderProviderContext context) {
        if (!Supported.Contains(context.Metadata.ModelType)) return null;

        var source = context.BindingInfo.BindingSource;

        if (source == BindingSource.Form) {
            var binder = typeof(OAuthFormBinder<>).MakeGenericType(context.Metadata.ModelType);
            return (IModelBinder?)Activator.CreateInstance(binder);
        }

        if (source == BindingSource.Query) {
            var binder = typeof(OAuthQueryBinder<>).MakeGenericType(context.Metadata.ModelType);
            return (IModelBinder?)Activator.CreateInstance(binder);
        }

        return null;
    }

    #endregion
}
