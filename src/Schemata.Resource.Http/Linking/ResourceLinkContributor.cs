using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.Extensions.Options;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Exceptions;
using Schemata.Abstractions.Resource;
using Schemata.Common;
using Schemata.Core.Building;
using Schemata.Resource.Foundation.Models;
using Schemata.Transport.Http.Linking;

namespace Schemata.Resource.Http.Linking;

/// <summary>
///     Contributes RFC 8288 links for the generic resource surface: self and canonical targets,
///     AIP-158 pagination navigation built on the existing page token, and related child
///     collections declared by nested resource patterns.
/// </summary>
public sealed class ResourceLinkContributor(
    ResourceRegistry                   registry,
    IDataProtectionProvider            protection,
    IOptions<SchemataResourceOptions>  options
) : ILinkContributor
{
    private const string PageTokenField = "page_token";
    private const string SkipField      = "skip";

    private static readonly ConcurrentDictionary<Type, (PropertyInfo? Next, PropertyInfo? Total)> Paging =
        new();

    private IDataProtector Protector => field ??= protection.CreateProtector(PageToken.ProtectionPurpose);

    #region ILinkContributor Members

    public IEnumerable<WebLink> GetLinks(LinkContext context) {
        if (context.Resource is null) {
            return [];
        }

        var value = context.Value;
        if (value is null) {
            return [];
        }

        return TryReadPaging(value, out var nextPageToken, out var totalSize)
            ? ListLinks(context.HttpContext.Request, context.Resource, nextPageToken, totalSize)
            : ResourceLinks(context, value);
    }

    #endregion

    private IReadOnlyList<WebLink> ListLinks(
        HttpRequest           request,
        ResourceRegistration? registration,
        string?               nextPageToken,
        int?                  totalSize
    ) {
        var links = new List<WebLink> {
            new(UriHelper.GetEncodedPathAndQuery(request), "self"),
        };

        if (!string.IsNullOrWhiteSpace(nextPageToken)) {
            links.Add(new(BuildTarget(request, PageTokenField, nextPageToken, SkipField), "next"));
        }

        var next    = ReadToken(nextPageToken);
        var current = ReadToken(request.Query[PageTokenField].ToString());

        if (current is not null) {
            links.Add(new(BuildTarget(request, null, null, PageTokenField, SkipField), "first"));

            if (current.PageSize > 0 && current.Skip > 0) {
                var previous = Encode(current, Math.Max(current.Skip - current.PageSize, 0));
                links.Add(new(BuildTarget(request, PageTokenField, previous, SkipField), "prev"));
            }
        }

        // An estimated total cannot locate the true last page, so the explicit last link is
        // withheld while exact totals keep it.
        var basis = current ?? next;
        if (EffectiveTotalSize(registration) != TotalSizeMode.Estimated
         && basis is not null && basis.PageSize > 0 && totalSize is > 0) {
            var offset = current?.Skip ?? (next is null ? 0 : Math.Max(next.Skip - next.PageSize, 0));
            var last   = ((totalSize.Value - 1) / basis.PageSize) * basis.PageSize;
            if (last > offset) {
                var target = Encode(basis, last);
                links.Add(new(BuildTarget(request, PageTokenField, target, SkipField), "last"));
            }
        }

        return links;
    }

    private IReadOnlyList<WebLink> ResourceLinks(LinkContext context, object value) {
        if (value is not ICanonicalName named) {
            return [];
        }

        var request      = context.HttpContext.Request;
        var target       = UriHelper.GetEncodedPathAndQuery(request);
        var name         = named.CanonicalName;
        var registration = string.IsNullOrWhiteSpace(name) ? null : ResourceLinkTargets.Match(registry, name) ?? context.Resource;
        var canonical    = registration is null ? null : ResourceLinkTargets.Canonical(request, registration, name!);

        // A mutation response identifies the written resource, not the collection or method URI
        // that carried the request.
        var self  = HttpMethods.IsGet(request.Method) || canonical is null ? target : canonical;
        var links = new List<WebLink> { new(self, "self") };
        if (canonical is not null) {
            links.Add(new(canonical, "canonical"));
        }

        if (registration is null) {
            return links;
        }

        foreach (var child in Children(request, registration, name!)) {
            links.Add(child);
        }

        return links;
    }

    private IEnumerable<WebLink> Children(HttpRequest request, ResourceRegistration? parent, string parentName) {
        if (parent is null) {
            yield break;
        }

        var descriptor = ResourceNameDescriptor.ForType(parent.Entity);
        if (descriptor.Pattern is not { } parentPattern || !descriptor.IsAddressable) {
            yield break;
        }

        var parentSegments = parentPattern.Split('/');
        foreach (var candidate in registry.Resources) {
            if (candidate.Entity == parent.Entity) {
                continue;
            }

            if (!ResourceLinkTargets.IsPublished(candidate) || !ResourceLinkTargets.AllowsList(candidate)) {
                continue;
            }

            var child = ResourceNameDescriptor.ForType(candidate.Entity);
            if (child.Pattern is not { } pattern || !child.IsAddressable) {
                continue;
            }

            var segments = pattern.Split('/');
            if (segments.Length != parentSegments.Length + 2 || !HasPrefix(parentSegments, segments)) {
                continue;
            }

            var collection = $"{parentName}/{child.Collection}";
            yield return new(ResourceLinkTargets.Canonical(request, candidate, collection), "related", Title: child.Plural);
        }
    }

    private static bool HasPrefix(string[] parent, string[] child) {
        for (var i = 0; i < parent.Length; i++) {
            var expected = ResourceNameDescriptor.IsPlaceholder(parent[i]) ? "{}" : parent[i];
            var actual   = ResourceNameDescriptor.IsPlaceholder(child[i]) ? "{}" : child[i];
            if (!string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase)) {
                return false;
            }
        }

        return true;
    }

    private TotalSizeMode EffectiveTotalSize(ResourceRegistration? registration) {
        var mode = registration?.TotalSize ?? TotalSizeMode.Default;
        if (mode != TotalSizeMode.Default) {
            return mode;
        }

        var global = options.Value.TotalSize;

        return global == TotalSizeMode.Default ? TotalSizeMode.Exact : global;
    }

    private static string BuildTarget(HttpRequest request, string? name, string? value, params string[] remove) {
        var values = new List<KeyValuePair<string, string?>>();
        foreach (var pair in request.Query) {
            if (remove.Any(field => string.Equals(field, pair.Key, StringComparison.OrdinalIgnoreCase))) {
                continue;
            }

            foreach (var item in pair.Value) {
                values.Add(new(pair.Key, item));
            }
        }

        if (name is not null && value is not null) {
            values.Add(new(name, value));
        }

        return UriHelper.BuildRelative(request.PathBase, request.Path, QueryString.Create(values));
    }

    private static bool TryReadPaging(object value, out string? nextPageToken, out int? totalSize) {
        var type    = value.GetType();
        var isList  = type.GetInterfaces()
                         .Any(candidate => candidate.IsGenericType
                                        && candidate.GetGenericTypeDefinition() == typeof(IEntitiesResult<,>));
        var paging  = Paging.GetOrAdd(type, static item => (
            item.GetProperty(nameof(ListResultBase<object, object>.NextPageToken)),
            item.GetProperty(nameof(ListResultBase<object, object>.TotalSize))));

        nextPageToken = isList ? paging.Next?.GetValue(value) as string : null;
        totalSize     = isList ? paging.Total?.GetValue(value) as int? : null;

        return isList;
    }

    private PageToken? ReadToken(string? value) {
        try {
            return PageToken.FromStringAsync(value, Protector).GetAwaiter().GetResult();
        } catch (ValidationException) {
            // The list handler already rejected an invalid token before producing a response; a
            // link-contributor failure must not turn that response into a server error.
            return null;
        }
    }

    private string Encode(PageToken token, int skip) {
        var next = new PageToken {
            Caller      = token.Caller,
            Filter      = token.Filter,
            Language    = token.Language,
            OrderBy     = token.OrderBy,
            Parent      = token.Parent,
            ShowDeleted = token.ShowDeleted,
            PageSize    = token.PageSize,
            Skip        = skip,
        };

        return next.ToStringAsync(Protector).GetAwaiter().GetResult();
    }
}
