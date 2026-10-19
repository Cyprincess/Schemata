using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Extensions;
using Schemata.Core.Building;
using Schemata.Flow.Skeleton.Entities;
using Schemata.Transport.Http.Linking;

namespace Schemata.Flow.Http.Linking;

/// <summary>
///     Contributes the Flow relations between a process, its definition, its tokens and its
///     transitions. Child collection links come from the shared resource contributor.
/// </summary>
public sealed class FlowLinkContributor(ResourceRegistry registry) : ILinkContributor
{
    private const string DefinitionsPath = "/v1/processes:definitions";

    #region ILinkContributor Members

    public IEnumerable<WebLink> GetLinks(LinkContext context) {
        var request = context.HttpContext.Request;
        if (HttpMethods.IsGet(request.Method)
         && string.Equals(request.Path.Value, DefinitionsPath, StringComparison.OrdinalIgnoreCase)) {
            return [new WebLink(UriHelper.GetEncodedPathAndQuery(request), "self")];
        }

        return context.Value switch {
            SchemataProcess { DefinitionName.Length: > 0 } process => [
                new(Target(request, DefinitionsPath), "related", Title: process.DefinitionName),
            ],
            SchemataProcessToken token => TokenLinks(request, token),
            SchemataProcessTransition transition => TransitionLinks(request, transition),
            _ => [],
        };
    }

    #endregion

    private IEnumerable<WebLink> TokenLinks(HttpRequest request, SchemataProcessToken token) {
        if (!string.IsNullOrWhiteSpace(token.Process) && Process(request, token.Process) is { } process) {
            yield return new(process, "up", Title: "process");
        }

        if (!string.IsNullOrWhiteSpace(token.Spawner)
         && Canonical(request, token.Spawner, typeof(SchemataProcessToken)) is { } spawner) {
            yield return new(spawner, "related", Title: "spawner");
        }
    }

    private IEnumerable<WebLink> TransitionLinks(HttpRequest request, SchemataProcessTransition transition) {
        if (!string.IsNullOrWhiteSpace(transition.Process) && Process(request, transition.Process) is { } process) {
            yield return new(process, "up", Title: "process");
        }

        if (!string.IsNullOrWhiteSpace(transition.Token)
         && Canonical(request, transition.Token, typeof(SchemataProcessToken)) is { } token) {
            yield return new(token, "related", Title: "token");
        }
    }

    private string? Process(HttpRequest request, string name) {
        var registration = ResourceLinkTargets.Find(registry, typeof(SchemataProcess));

        return registration is null ? null : ResourceLinkTargets.Instance(request, registration, name);
    }

    private string? Canonical(HttpRequest request, string canonicalName, Type entity) {
        var registration = ResourceLinkTargets.Find(registry, entity);

        return registration is null ? null : ResourceLinkTargets.Canonical(request, registration, canonicalName);
    }

    private static string Target(HttpRequest request, string path) =>
        UriHelper.BuildRelative(request.PathBase, new PathString(path));
}
