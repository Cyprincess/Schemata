using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Schemata.Authorization.Skeleton.Models;

namespace Schemata.Authorization.Foundation.Binding;

/// <summary>
///     Provides <see cref="OAuthFormBinder{T}" /> or <see cref="OAuthQueryBinder{T}" /> for known OAuth request
///     models based on the binding source.
/// </summary>
/// <remarks>
///     Registered as the first model binder provider in the MVC pipeline by
///     <see cref="Features.SchemataAuthorizationFeature{TApp, TAuth, TScope, TToken}" />.
///     Non-OAuth models fall through to the default model binder.
/// </remarks>
/// <seealso cref="OAuthFormBinder{T}" />
/// <seealso cref="OAuthQueryBinder{T}" />
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
