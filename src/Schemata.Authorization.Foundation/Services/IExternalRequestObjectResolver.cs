using System;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Authorization.Skeleton.Entities;

namespace Schemata.Authorization.Foundation.Services;

/// <summary>Retrieves a request object from an exact client-registered HTTPS URI.</summary>
public interface IExternalRequestObjectResolver
{
    Task<string> ResolveAsync(SchemataApplication application, Uri uri, CancellationToken ct);
}
