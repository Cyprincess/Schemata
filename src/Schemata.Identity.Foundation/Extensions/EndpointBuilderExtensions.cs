using Microsoft.AspNetCore.Routing;
using Schemata.Identity.Skeleton.Entities;

// ReSharper disable once CheckNamespace
namespace Microsoft.AspNetCore.Builder;

public static class EndpointBuilderExtensions
{
    public static IEndpointRouteBuilder UseIdentity<TUser, TRole>(this IEndpointRouteBuilder endpoints)
        where TUser : SchemataUser
        where TRole : SchemataRole {
        return endpoints;
    }
}
