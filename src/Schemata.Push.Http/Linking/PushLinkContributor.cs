using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Extensions;
using Schemata.Abstractions.Linking;
using Schemata.Core.Building;
using Schemata.Push.Http.Models;
using Schemata.Push.Skeleton;
using Schemata.Push.Skeleton.Entities;
using Schemata.Transport.Http.Linking;

namespace Schemata.Push.Http.Linking;

/// <summary>
///     Contributes the subscription-list links: the collection itself and the provider-filtered
///     variants the list endpoint accepts. Provider keys and credentials never reach a target.
/// </summary>
public sealed class PushLinkContributor : ILinkContributor
{
    private const string ProviderField = "provider";

    #region ILinkContributor Members

    public IEnumerable<WebLink> GetLinks(LinkContext context) {
        var request = context.HttpContext.Request;
        if (!HttpMethods.IsGet(request.Method) || !IsSubscriptionCollection(context)) {
            return [];
        }

        var links   = new List<WebLink> { new(UriHelper.GetEncodedPathAndQuery(request), "self") };
        var current = request.Query[ProviderField].ToString();
        if (!string.IsNullOrWhiteSpace(current)) {
            links.Add(new(Target(request, null), "alternate", Title: "all providers"));
        } else {
            foreach (var provider in Providers(context).Distinct(StringComparer.Ordinal)) {
                links.Add(new(Target(request, provider), "alternate", Title: provider));
            }
        }

        return links;
    }

    #endregion

    private static bool IsSubscriptionCollection(LinkContext context) {
        var request = context.HttpContext.Request;
        var resource = context.HttpContext.GetEndpoint()?.Metadata.GetMetadata<ResourceRegistration>();
        if (resource is not null && typeof(SchemataPushSubscription).IsAssignableFrom(resource.Entity)) {
            return string.Equals(request.Path.Value, ResourceLinkTargets.CollectionPath(resource),
                                 StringComparison.OrdinalIgnoreCase);
        }

        return string.Equals(request.Path.Value, PushConstants.Routes.SubscriptionsPath,
                             StringComparison.OrdinalIgnoreCase);
    }

    private static IEnumerable<string> Providers(LinkContext context) {
        if (context.Value is not PushSubscriptionListResponse list) {
            yield break;
        }

        foreach (var subscription in list.Subscriptions) {
            if (!string.IsNullOrWhiteSpace(subscription.Provider)) {
                yield return subscription.Provider;
            }
        }
    }

    private static string Target(HttpRequest request, string? provider) {
        var values = new List<KeyValuePair<string, string?>>();
        foreach (var pair in request.Query) {
            if (string.Equals(pair.Key, ProviderField, StringComparison.OrdinalIgnoreCase)) {
                continue;
            }

            foreach (var value in pair.Value) {
                values.Add(new(pair.Key, value));
            }
        }

        if (provider is not null) {
            values.Add(new(ProviderField, provider));
        }

        return UriHelper.BuildRelative(request.PathBase, request.Path, QueryString.Create(values));
    }
}
