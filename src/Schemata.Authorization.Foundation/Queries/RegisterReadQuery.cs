using Schemata.Authorization.Skeleton.Models;
using Schemata.Messaging.Skeleton;

namespace Schemata.Authorization.Foundation.Queries;

public sealed record RegisterReadQuery(string? ClientId, string? BearerToken) : IQuery<RegistrationResponse?>;
